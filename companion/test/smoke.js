'use strict';

const assert = require('assert');
const fs = require('fs');
const http = require('http');
const path = require('path');

const companionRoot = path.resolve(__dirname, '..');
const projectRoot = path.resolve(companionRoot, '..');
const dataRoot = path.join(companionRoot, '.test-data');
const configPath = path.join(dataRoot, 'config.json');

fs.rmSync(dataRoot, { recursive: true, force: true });
fs.mkdirSync(dataRoot, { recursive: true });
fs.writeFileSync(configPath, JSON.stringify({
  listen_host: '127.0.0.1',
  listen_port: 0,
  mock_mode: true,
  media: {
    enabled: true,
    bridge_path: path.join(
      projectRoot,
      'media-bridge',
      'bin',
      'Release',
      'net8.0-windows10.0.19041.0',
      'LilithMediaBridge.exe')
  }
}, null, 2), 'utf8');

process.env.LILITH_AI_DATA_DIR = dataRoot;
process.env.LILITH_AI_CONFIG = configPath;
process.env.LILITH_CHARACTER_FILE = path.join(projectRoot, 'character', 'lilith.json');

const { createApp } = require('../src/server');

function request(port, method, target, token, payload) {
  return new Promise((resolve, reject) => {
    const body = payload === undefined ? '' : JSON.stringify(payload);
    const headers = { Accept: 'application/json' };
    if (token) headers.Authorization = `Bearer ${token}`;
    if (body) {
      headers['Content-Type'] = 'application/json';
      headers['Content-Length'] = Buffer.byteLength(body);
    }
    const req = http.request({
      host: '127.0.0.1',
      port,
      method,
      path: target,
      headers
    }, res => {
      let responseBody = '';
      res.setEncoding('utf8');
      res.on('data', chunk => { responseBody += chunk; });
      res.on('end', () => {
        try {
          resolve({ status: res.statusCode, body: JSON.parse(responseBody) });
        } catch (error) {
          reject(error);
        }
      });
    });
    req.on('error', reject);
    if (body) req.write(body);
    req.end();
  });
}

async function main() {
  const app = createApp();
  assert.deepStrictEqual(app.config.local_context, {
    enabled: true,
    time_enabled: true,
    media_enabled: true
  });
  assert.strictEqual(app.config.conversation.allow_parenthetical_actions, false);
  await new Promise(resolve => app.server.listen(0, '127.0.0.1', resolve));
  const port = app.server.address().port;
  try {
    const health = await request(port, 'GET', '/health');
    assert.strictEqual(health.status, 200);
    assert.strictEqual(health.body.ok, true);

    const unauthorized = await request(port, 'GET', '/v1/media');
    assert.strictEqual(unauthorized.status, 401);

    await new Promise(resolve => setTimeout(resolve, 1000));
    const media = await request(port, 'GET', '/v1/media', app.token);
    assert.strictEqual(media.status, 200);
    assert.strictEqual(typeof media.body.playing, 'boolean');
    assert.ok(!/chrome|msedge|firefox/i.test(media.body.source || ''));

    app.mediaMonitor.state = {
      available: true,
      playing: false,
      source: 'QQMusic.exe',
      title: 'Local Context Persistence Test Song',
      artist: 'Test Artist',
      playback_status: 'paused',
      error: ''
    };
    const localContextMessage = await request(
      port,
      'POST',
      '/v1/session/local-context-persistence/message',
      app.token,
      {
        request_id: 'local-context-persistence',
        player: { name: '测试玩家' },
        mode: 'in_character',
        message: '你觉得这首歌怎么样？'
      });
    assert.strictEqual(localContextMessage.status, 200);
    const persistedSessionFiles = fs.readdirSync(path.join(dataRoot, 'sessions'))
      .map(name => path.join(dataRoot, 'sessions', name));
    const persistedSessionText = persistedSessionFiles
      .map(file => fs.readFileSync(file, 'utf8'))
      .join('\n');
    assert.ok(!persistedSessionText.includes('Local Context Persistence Test Song'));

    const message = await request(port, 'POST', '/v1/session/smoke/message', app.token, {
      request_id: 'smoke-1',
      player: { name: '只知道睡觉的觉皇' },
      mode: 'in_character',
      message: '你在吗？'
    });
    assert.strictEqual(message.status, 200);
    assert.strictEqual(typeof message.body.text, 'string');
    assert.ok(message.body.text.length > 0);
    assert.strictEqual(typeof message.body.ui.duration_ms, 'number');

    const reload = await request(port, 'POST', '/v1/control', app.token, {
      action: 'reload'
    });
    assert.strictEqual(reload.status, 200);
    assert.strictEqual(reload.body.ok, true);
    assert.strictEqual(reload.body.reloaded, true);

    const seed = await request(port, 'POST', '/v1/control', app.token, {
      action: 'seed_assistant',
      session_id: 'lilith-opener',
      text: '我刚才先说了一句话。'
    });
    assert.strictEqual(seed.status, 200);
    assert.strictEqual(seed.body.ok, true);
    assert.strictEqual(seed.body.seeded, true);
    const seededFiles = fs.readdirSync(path.join(dataRoot, 'sessions'))
      .map(name => path.join(dataRoot, 'sessions', name));
    const seededHistory = seededFiles
      .map(file => JSON.parse(fs.readFileSync(file, 'utf8')))
      .find(history => history.some(item => item.content === '我刚才先说了一句话。'));
    assert.ok(seededHistory);
    assert.deepStrictEqual(seededHistory.at(-1), {
      role: 'assistant',
      content: '我刚才先说了一句话。'
    });

    const history = await request(
      port,
      'GET',
      '/v1/session/lilith-opener/history',
      app.token);
    assert.strictEqual(history.status, 200);
    assert.strictEqual(history.body.session_id, 'lilith-opener');
    assert.deepStrictEqual(history.body.messages.at(-1), seededHistory.at(-1));

    const repeatedSeed = await request(port, 'POST', '/v1/control', app.token, {
      action: 'seed_assistant',
      session_id: 'lilith-opener',
      text: seededHistory.at(-1).content
    });
    assert.strictEqual(repeatedSeed.status, 200);
    const historyAfterRepeatedSeed = await request(
      port,
      'GET',
      '/v1/session/lilith-opener/history',
      app.token);
    assert.strictEqual(historyAfterRepeatedSeed.body.messages.length, history.body.messages.length);

    const reset = await request(port, 'POST', '/v1/control', app.token, {
      action: 'reset_session',
      session_id: 'smoke'
    });
    assert.strictEqual(reset.status, 200);
    assert.strictEqual(reset.body.ok, true);
    const historyAfterReset = await request(port, 'GET', '/v1/session/smoke/history', app.token);
    assert.strictEqual(historyAfterReset.status, 200);
    assert.deepStrictEqual(historyAfterReset.body.messages, []);

    const forget = await request(port, 'POST', '/v1/control', app.token, {
      action: 'forget_player',
      player_name: '只知道睡觉的觉皇'
    });
    assert.strictEqual(forget.status, 200);
    assert.strictEqual(forget.body.ok, true);

    console.log(JSON.stringify({
      ok: true,
      health: health.body,
      media: media.body,
      reply_status: message.body.status
    }, null, 2));
  } finally {
    await app.mediaMonitor.stop();
    await new Promise(resolve => app.server.close(resolve));
    fs.rmSync(dataRoot, { recursive: true, force: true });
  }
}

main().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
