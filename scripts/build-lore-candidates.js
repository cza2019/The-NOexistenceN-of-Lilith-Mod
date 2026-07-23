'use strict';

const fs = require('fs');
const path = require('path');

function listTextFiles(root) {
  const files = [];
  const visit = directory => {
    for (const item of fs.readdirSync(directory, { withFileTypes: true })) {
      const fullPath = path.join(directory, item.name);
      if (item.isDirectory()) visit(fullPath);
      else if (item.isFile() && item.name.toLowerCase().endsWith('.txt')) files.push(fullPath);
    }
  };
  visit(root);
  return files.sort((left, right) => left.localeCompare(right, 'zh-CN'));
}

function relativeSource(root, file) {
  return path.relative(root, file).split(path.sep).join('/');
}

function sectionId(relativePath, label, index) {
  const stem = relativePath.replace(/\.txt$/i, '').replace(/[^\p{L}\p{N}]+/gu, '-');
  const suffix = String(label || `section-${index + 1}`).replace(/[^\p{L}\p{N}_.-]+/gu, '-');
  return `${stem}-${suffix}`.replace(/-+/g, '-').replace(/^-|-$/g, '').toLocaleLowerCase('zh-CN');
}

function parseFile(root, file) {
  const relativePath = relativeSource(root, file);
  const text = fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, '').replace(/\r\n?/g, '\n');
  const lines = text.split('\n');
  const sections = [];
  let current = { label: 'script-start', lines: [], line_start: 1 };
  const flush = endLine => {
    const meaningful = current.lines.filter(line => line.trim());
    if (!meaningful.length) return;
    const body = meaningful.join('\n').trim();
    const choices = meaningful.filter(line => line.startsWith('【选择】'));
    const jumps = meaningful.filter(line => /^(【条件跳转】|【跳转】|【调用分支】)|^\s*跳转：/.test(line));
    const variables = meaningful.filter(line => line.startsWith('【变量变化】'));
    const speakers = [...new Set(meaningful
      .map(line => line.match(/^([^【\s][^：:\n]{0,30})[：:]/u))
      .filter(Boolean)
      .map(match => match[1].trim()))];
    sections.push({
      id: sectionId(relativePath, current.label, sections.length),
      source_ref: `${relativePath}#${current.label}`,
      label: current.label,
      line_start: current.line_start,
      line_end: endLine,
      speakers,
      choices,
      jumps,
      variables,
      body
    });
  };

  lines.forEach((line, index) => {
    const label = line.match(/^【标签】(.+)$/u);
    if (label) {
      flush(index);
      current = { label: label[1].trim(), lines: [], line_start: index + 1 };
      return;
    }
    if (/^={8,}$|^-{8,}$/.test(line.trim())) return;
    current.lines.push(line);
  });
  flush(lines.length);

  return {
    relative_path: relativePath,
    chars: Array.from(text).length,
    lines: lines.length,
    sections
  };
}

function buildCandidates(sourceRoot) {
  const resolved = path.resolve(sourceRoot);
  if (!fs.statSync(resolved).isDirectory()) throw new Error(`Source is not a directory: ${resolved}`);
  const files = listTextFiles(resolved).map(file => parseFile(resolved, file));
  return {
    schema_version: 1,
    source_name: path.basename(resolved),
    generated_at: new Date().toISOString(),
    totals: {
      files: files.length,
      sections: files.reduce((sum, file) => sum + file.sections.length, 0),
      chars: files.reduce((sum, file) => sum + file.chars, 0)
    },
    files
  };
}

function main() {
  const sourceRoot = process.argv[2];
  const repositoryRoot = path.resolve(__dirname, '..');
  const output = process.argv[3]
    ? path.resolve(process.argv[3])
    : path.join(repositoryRoot, 'character', 'private-source', 'candidates.json');
  if (!sourceRoot) {
    console.error('Usage: node scripts/build-lore-candidates.js <source-root> [output-json]');
    process.exitCode = 2;
    return;
  }
  const candidates = buildCandidates(sourceRoot);
  fs.mkdirSync(path.dirname(output), { recursive: true });
  fs.writeFileSync(output, JSON.stringify(candidates, null, 2) + '\n', 'utf8');
  console.log(JSON.stringify({ ok: true, output, totals: candidates.totals }, null, 2));
}

if (require.main === module) main();

module.exports = { buildCandidates, parseFile };
