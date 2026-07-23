'use strict';

const assert = require('assert');
const fs = require('fs');
const os = require('os');
const path = require('path');

const { buildCandidates } = require('../../scripts/build-lore-candidates');

const tempRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'lilith-lore-candidates-'));
try {
  const storyRoot = path.join(tempRoot, '原作');
  fs.mkdirSync(path.join(storyRoot, '01_主剧情'), { recursive: true });
  fs.writeFileSync(path.join(storyRoot, '01_主剧情', '01_Test.txt'), [
    '《不/存在的你，和我》剧情导出',
    '【标签】Dialogue1',
    '莉莉丝：我们做了草莓蛋糕。',
    '【选择】还记得吗？',
    '    跳转：Test.Dialogue2',
    '【变量变化】LikAbility=LikAbility+1',
    '【标签】Dialogue2',
    '旁白：故事继续。',
    '【跳转】Other.Dialogue1'
  ].join('\n'), 'utf8');

  const output = buildCandidates(storyRoot);
  assert.strictEqual(output.source_name, '原作');
  assert.strictEqual(output.totals.files, 1);
  assert.ok(output.totals.sections >= 2);
  assert.ok(!JSON.stringify(output).includes(tempRoot));
  const dialogue1 = output.files[0].sections.find(section => section.label === 'Dialogue1');
  assert.ok(dialogue1);
  assert.deepStrictEqual(dialogue1.speakers, ['莉莉丝']);
  assert.strictEqual(dialogue1.choices.length, 1);
  assert.strictEqual(dialogue1.jumps.length, 1);
  assert.strictEqual(dialogue1.variables.length, 1);
  assert.ok(dialogue1.source_ref.includes('01_主剧情/01_Test.txt#Dialogue1'));
} finally {
  fs.rmSync(tempRoot, { recursive: true, force: true });
}

console.log(JSON.stringify({ ok: true }, null, 2));
