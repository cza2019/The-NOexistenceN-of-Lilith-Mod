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
      const requestBody = JSON.parse(body);
      captured.push(requestBody);
      const inputText = JSON.stringify(requestBody.input || []);
      let spokenText;
      let plainMetadata = false;
      if (inputText.includes('我正在听什么歌')) {
        spokenText = '你正在听夏色片想い，这首歌的旋律很轻快。';
        plainMetadata = true;
      } else if (inputText.includes('persistent-mismatch')) {
        spokenText = '这次回复仍然完全使用中文，没有遵循日文要求。';
      } else {
        spokenText = inputText.includes('日本語')
          ? 'テストだね。ちゃんと届いているよ。'
          : '嗯？你在测试我的反应吗？';
      }
      const result = JSON.stringify({
        output: [{
          type: 'message',
          content: [{
            type: 'output_text',
            text: plainMetadata
              ? `${spokenText}\n\nemotion=curious\nanimation=listen`
              : JSON.stringify({
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
    media: {
      enabled: false,
      allowed_sources: ['qqmusic']
    },
    local_context: {
      enabled: true,
      media_enabled: true
    },
    web_search: { enabled: true, backend: 'provider' }
  }, null, 2), 'utf8');

  process.env.LILITH_AI_DATA_DIR = dataRoot;
  process.env.LILITH_AI_CONFIG = configPath;
  const { createApp } = require('../src/server');
  const app = createApp();
  await listen(app.server);
  try {
    app.mediaMonitor.state = {
      available: true,
      playing: false,
      playback_status: 'paused',
      source: 'QQMusic.exe',
      title: '夏色片想い (夏色单相思)',
      artist: '菊池桃子'
    };

    const mediaStart = captured.length;
    const mediaResult = await post(
      app.server.address().port,
      '/v1/session/language-mixed-media/message',
      app.token,
      {
        request_id: 'language-mixed-media',
        player: { name: '觉皇' },
        mode: 'in_character',
        locale: 'zh-CN',
        message: '我正在听什么歌'
      });
    assert.strictEqual(mediaResult.status, 200);
    assert.strictEqual(mediaResult.body.status, 'ok');
    assert.strictEqual(mediaResult.body.text, '你正在听夏色片想い，这首歌的旋律很轻快。');
    assert.strictEqual(mediaResult.body.ui.emotion, 'neutral');
    assert.strictEqual(mediaResult.body.ui.animation, 'listen');
    assert.strictEqual(captured.length, mediaStart + 1);
    assert.ok(captured[mediaStart].instructions.includes('夏色片想い (夏色单相思)'));
    assert.deepStrictEqual(captured[mediaStart].tools, [{ type: 'web_search' }]);
    const persistedSessions = fs.readdirSync(path.join(dataRoot, 'sessions'))
      .filter(name => name.endsWith('.json'))
      .map(name => fs.readFileSync(path.join(dataRoot, 'sessions', name), 'utf8'))
      .join('\n');
    assert.ok(!persistedSessions.includes('emotion=curious'));
    assert.ok(!persistedSessions.includes('animation=listen'));

    const retryStart = captured.length;
    const result = await post(
      app.server.address().port,
      '/v1/session/language-retry/message',
      app.token,
      {
        request_id: 'language-retry',
        player: { name: '覚皇' },
        mode: 'in_character',
        locale: 'ja',
        message: 'retry-language'
      });
    assert.strictEqual(result.status, 200);
    assert.strictEqual(result.body.status, 'ok');
    assert.strictEqual(result.body.text, 'テストだね。ちゃんと届いているよ。');
    assert.strictEqual(captured.length, retryStart + 2);
    assert.deepStrictEqual(captured[retryStart].tools, [{ type: 'web_search' }]);
    assert.strictEqual(Object.hasOwn(captured[retryStart + 1], 'tools'), false);
    assert.ok(captured[retryStart + 1].input.at(-1).content.includes('日本語'));

    const fallbackStart = captured.length;
    const fallbackResult = await post(
      app.server.address().port,
      '/v1/session/language-fallback/message',
      app.token,
      {
        request_id: 'language-fallback',
        player: { name: '覚皇' },
        mode: 'in_character',
        locale: 'ja',
        message: 'persistent-mismatch'
      });
    assert.strictEqual(fallbackResult.status, 200);
    assert.strictEqual(fallbackResult.body.status, 'fallback');
    assert.strictEqual(fallbackResult.body.fallback_reason, 'language_mismatch');
    assert.strictEqual(captured.length, fallbackStart + 2);
    assert.strictEqual(Object.hasOwn(captured[fallbackStart + 1], 'tools'), false);
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
