'use strict';

const assert = require('assert');
const fs = require('fs');
const http = require('http');
const os = require('os');
const path = require('path');

const dataRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'lilith-provider-reliability-'));
process.env.LILITH_AI_DATA_DIR = dataRoot;
process.env.LILITH_AI_CONFIG = path.join(dataRoot, 'config.json');
const { createApp } = require('../src/server');

const success = response => response.end(JSON.stringify({
  choices: [{ message: { content: 'Connection recovered.' } }]
}));
const listen = server => new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const close = server => new Promise(resolve => {
  server.close(resolve);
  server.closeAllConnections();
});

async function runCase(name, handler, expected, timeout = 3000) {
  let calls = 0;
  const payloads = [];
  const provider = http.createServer((request, response) => {
    let body = '';
    request.setEncoding('utf8');
    request.on('data', chunk => { body += chunk; });
    request.on('end', () => {
      payloads.push(body);
      handler(request, response, ++calls);
    });
  });
  await listen(provider);
  fs.writeFileSync(process.env.LILITH_AI_CONFIG, JSON.stringify({
    mock_mode: false,
    provider: {
      kind: 'deepseek',
      base_url: `http://127.0.0.1:${provider.address().port}/private-url-marker`,
      api_key: 'private-key-marker',
      model: 'deepseek-flash',
      thinking_mode: 'disabled',
      timeout_ms: timeout
    },
    media: { enabled: false },
    web_search: { enabled: false }
  }));
  const app = createApp();
  await listen(app.server);
  const start = Date.now();
  try {
    const response = await fetch(`http://127.0.0.1:${app.server.address().port}/v1/session/${name}/message`, {
      method: 'POST',
      headers: { Authorization: `Bearer ${app.token}`, 'Content-Type': 'application/json' },
      body: JSON.stringify({ message: 'private-chat-marker', locale: 'en' })
    });
    assert.strictEqual(response.status, 200);
    const result = await response.json();
    assert.strictEqual(result.status, expected.status, name);
    assert.strictEqual(calls, expected.calls, name);
    if (expected.samePayload) assert.strictEqual(payloads[0], payloads[1]);
    if (expected.maxElapsed) assert.ok(Date.now() - start < expected.maxElapsed, `${name}: deadline exceeded`);
    if (expected.status === 'ok') assert.strictEqual(result.text, 'Connection recovered.');
    else assert.strictEqual(result.fallback_reason, 'provider_error');
    // A retry must not append duplicate turns to the visible conversation.
    const history = await fetch(`http://127.0.0.1:${app.server.address().port}/v1/session/${name}/history`, {
      headers: { Authorization: `Bearer ${app.token}` }
    }).then(value => value.json());
    assert.strictEqual(history.messages.length, 2);
  } finally {
    await app.mediaMonitor.stop();
    await close(app.server);
    await close(provider);
  }
}

async function main() {
  const logs = [];
  const originalWarn = console.warn;
  const originalLog = console.log;
  console.warn = (...parts) => logs.push(parts.join(' '));
  console.log = (...parts) => logs.push(parts.join(' '));
  try {
    await runCase('reset', (req, res, call) => {
      if (call === 1) req.socket.destroy();
      else success(res);
    }, { status: 'ok', calls: 2, samePayload: true });
    await runCase('partial-response', (req, res, call) => {
      if (call > 1) return success(res);
      res.writeHead(200, { 'Content-Type': 'application/json' });
      res.write('{"choices":');
      setTimeout(() => res.destroy(), 20);
    }, { status: 'ok', calls: 2, samePayload: true });
    await runCase('html-503', (req, res, call) => {
      if (call > 1) return success(res);
      res.writeHead(503);
      res.end('<html>private-body-marker</html>');
    }, { status: 'ok', calls: 2 });
    await runCase('persistent-502', (req, res) => {
      res.writeHead(502);
      res.end('private-body-marker');
    }, { status: 'fallback', calls: 2 });
    for (const code of [400, 401, 402, 422]) {
      await runCase(`permanent-${code}`, (req, res) => {
        res.writeHead(code);
        res.end(JSON.stringify({ error: { message: 'private-body-marker' } }));
      }, { status: 'fallback', calls: 1 });
    }
    await runCase('rate-limit', (req, res, call) => {
      if (call > 1) return success(res);
      res.writeHead(429, { 'Retry-After': '1' });
      res.end('{}');
    }, { status: 'ok', calls: 2 });
    await runCase('long-retry-after', (req, res) => {
      res.writeHead(429, { 'Retry-After': '120' });
      res.end('{}');
    }, { status: 'fallback', calls: 1 });
    await runCase('invalid-json', (req, res) => res.end('private-body-marker'), {
      status: 'fallback', calls: 1
    });
    await runCase('shared-deadline', (req, res, call) => {
      if (call === 1) setTimeout(() => req.socket.destroy(), 150);
      // The second attempt stalls. Its timeout must use the remaining budget.
    }, { status: 'fallback', calls: 2, maxElapsed: 1600 }, 1100);
    await runCase('keepalive-timeout', (req, res) => {
      const keepalive = setInterval(() => res.write('\n'), 50);
      res.on('close', () => clearInterval(keepalive));
    }, { status: 'fallback', calls: 1, maxElapsed: 1600 }, 1000);
    await runCase('language-deadline', (req, res, call) => {
      const timer = setTimeout(() => res.end(JSON.stringify({
        choices: [{ message: { content: call === 1 ? '这是中文回复，需要纠正回复语言。' : 'Connection recovered.' } }]
      })), 650);
      res.on('close', () => clearTimeout(timer));
    }, { status: 'fallback', calls: 2, maxElapsed: 1600 }, 1000);
    const output = logs.join('\n');
    for (const secret of ['private-url-marker', 'private-key-marker', 'private-chat-marker', 'private-body-marker']) {
      assert.ok(!output.includes(secret), `Diagnostic log exposed ${secret}`);
    }
    for (const evidence of ['code=ECONNRESET', 'http_status=503', 'code=PROVIDER_TIMEOUT', 'retry=true', 'retry=false', 'Provider request recovered:']) {
      assert.ok(output.includes(evidence), `Missing diagnostic: ${evidence}`);
    }
  } finally {
    console.warn = originalWarn;
    console.log = originalLog;
    fs.rmSync(dataRoot, { recursive: true, force: true });
  }
  console.log('Provider reliability tests passed.');
}

main().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
