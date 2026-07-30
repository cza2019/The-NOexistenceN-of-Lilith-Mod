'use strict';

const assert = require('assert');
const fs = require('fs');
const http = require('http');
const path = require('path');

const companionRoot = path.resolve(__dirname, '..');
const dataRoot = path.join(companionRoot, '.language-retry-test-data');
const configPath = path.join(dataRoot, 'config.json');

function listen(server) {
  return new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
}

function close(server) {
  return new Promise(resolve => server.close(resolve));
}

function post(port, target, token, payload) {
  return new Promise((resolve, reject) => {
    const body = JSON.stringify(payload);
    const request = http.request({
      host: '127.0.0.1',
      port,
      path: target,
      method: 'POST',
      headers: {
        Authorization: `Bearer ${token}`,
        'Content-Type': 'application/json',
        'Content-Length': Buffer.byteLength(body)
      }
    }, response => {
      let text = '';
      response.setEncoding('utf8');
      response.on('data', chunk => { text += chunk; });
      response.on('end', () => resolve({ status: response.statusCode, body: JSON.parse(text) }));
    });
    request.on('error', reject);
    request.end(body);
  });
}

async function main() {
  fs.rmSync(dataRoot, { recursive: true, force: true });
  fs.mkdirSync(dataRoot, { recursive: true });
  const captured = [];
  const fakeProvider = http.createServer((request, response) => {
    let body = '';
    request.setEncoding('utf8');
    request.on('data', chunk => { body += chunk; });
    request.on('end', () => {
      captured.push(JSON.parse(body));
      const spokenText = captured.length === 1
        ? '嗯？你在测试我的反应吗？'
        : 'テストだね。ちゃんと届いているよ。';
      const result = JSON.stringify({
        output: [{
          type: 'message',
          content: [{
            type: 'output_text',
            text: JSON.stringify({
              text: spokenText,
              emotion: 'neutral',
              animation: 'idle',
              memory_updates: []
            }),
            annotations: []
          }]
        }]
      });
      response.writeHead(200, {
        'Content-Type': 'application/json',
        'Content-Length': Buffer.byteLength(result)
      });
      response.end(result);
    });
  });

  await listen(fakeProvider);
  fs.writeFileSync(configPath, JSON.stringify({
    listen_host: '127.0.0.1',
    listen_port: 0,
    mock_mode: false,
    provider: {
      kind: 'volcengine',
      base_url: `http://127.0.0.1:${fakeProvider.address().port}`,
      api_key: 'language-retry-test-key',
      model: 'deepseek-v4-flash-test',
      wire_api: 'responses',
      thinking_mode: 'disabled',
      search_tool: 'web_search',
      timeout_ms: 3000,
      max_tokens: 300
    },
    media: { enabled: false },
    web_search: { enabled: true, backend: 'provider' }
  }, null, 2), 'utf8');

  process.env.LILITH_AI_DATA_DIR = dataRoot;
  process.env.LILITH_AI_CONFIG = configPath;
  const { createApp } = require('../src/server');
  const app = createApp();
  await listen(app.server);
  try {
    const result = await post(
      app.server.address().port,
      '/v1/session/language-retry/message',
      app.token,
      {
        request_id: 'language-retry',
        player: { name: '覚皇' },
        mode: 'in_character',
        locale: 'ja',
        message: 'test'
      });
    assert.strictEqual(result.status, 200);
    assert.strictEqual(result.body.status, 'ok');
    assert.strictEqual(result.body.text, 'テストだね。ちゃんと届いているよ。');
    assert.strictEqual(captured.length, 2);
    assert.deepStrictEqual(captured[0].tools, [{ type: 'web_search' }]);
    assert.strictEqual(Object.hasOwn(captured[1], 'tools'), false);
    assert.ok(captured[1].input.at(-1).content.includes('日本語'));
  } finally {
    await app.mediaMonitor.stop();
    await close(app.server);
    await close(fakeProvider);
    fs.rmSync(dataRoot, { recursive: true, force: true });
  }

  console.log('Reply language retry test passed.');
}

main().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
