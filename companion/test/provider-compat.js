'use strict';

const assert = require('assert');
const fs = require('fs');
const http = require('http');
const path = require('path');

const companionRoot = path.resolve(__dirname, '..');
const projectRoot = path.resolve(companionRoot, '..');
const dataRoot = path.join(companionRoot, '.provider-test-data');
const configPath = path.join(dataRoot, 'config.json');

fs.rmSync(dataRoot, { recursive: true, force: true });
fs.mkdirSync(dataRoot, { recursive: true });
process.env.LILITH_AI_DATA_DIR = dataRoot;
process.env.LILITH_AI_CONFIG = configPath;
process.env.LILITH_CHARACTER_FILE = path.join(projectRoot, 'character', 'lilith.json');

const { createApp } = require('../src/server');

function listen(server) {
  return new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
}

function close(server) {
  return new Promise(resolve => server.close(resolve));
}

function request(port, target, token, payload) {
  return new Promise((resolve, reject) => {
    const body = JSON.stringify(payload);
    const req = http.request({
      host: '127.0.0.1',
      port,
      method: 'POST',
      path: target,
      headers: {
        Accept: 'application/json',
        Authorization: `Bearer ${token}`,
        'Content-Type': 'application/json',
        'Content-Length': Buffer.byteLength(body)
      }
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
    req.write(body);
    req.end();
  });
}

async function runCase(fakePort, requests, searchQueries, testCase) {
  fs.writeFileSync(configPath, JSON.stringify({
    listen_host: '127.0.0.1',
    listen_port: 0,
    mock_mode: false,
    provider: {
      kind: testCase.kind,
      base_url: `http://127.0.0.1:${fakePort}${testCase.basePath}`,
      api_key: 'provider-test-key',
      model: testCase.model,
      wire_api: 'chat_completions',
      thinking_mode: 'disabled',
      timeout_ms: 3000,
      max_tokens: 300
    },
    media: { enabled: false },
    web_search: testCase.search === 'searxng'
      ? { enabled: true, backend: 'searxng', base_url: `http://127.0.0.1:${fakePort}` }
      : testCase.search === 'provider'
        ? { enabled: true, backend: 'provider' }
        : { enabled: false, backend: 'off' }
  }, null, 2), 'utf8');

  const before = requests.length;
  const beforeSearch = searchQueries.length;
  const app = createApp();
  await listen(app.server);
  try {
    const messages = testCase.memorySearch
      ? ['我住在杭州，记住这件事。', '今天那里天气怎么样？']
      : [testCase.loreQuery || (testCase.search
        ? '今天有什么电影新闻？'
        : (testCase.longReply ? '长回复限制测试' : '协议兼容测试'))];
    let response;
    for (let index = 0; index < messages.length; index += 1) {
      response = await request(
        app.server.address().port,
        `/v1/session/${testCase.kind}/message`,
        app.token,
        {
          request_id: `${testCase.kind}-${index + 1}`,
          player: { name: '只知道睡觉的觉皇' },
          mode: 'in_character',
          message: messages[index]
        });
    }
    assert.strictEqual(response.status, 200);
    assert.strictEqual(response.body.status, 'ok');
    if (testCase.longReply) {
      assert.ok(Array.from(response.body.text).length <= 100);
      assert.ok(/[。！？!?…]$/.test(response.body.text));
      assert.ok(response.body.ui.duration_ms > 6500);
    } else {
      assert.strictEqual(response.body.text, `${testCase.kind} compatible`);
    }

    assert.strictEqual(requests.length, before + messages.length);
    const captured = requests[before + messages.length - 1];
    assert.strictEqual(captured.path, `${testCase.basePath}/chat/completions`);
    assert.strictEqual(captured.authorization, 'Bearer provider-test-key');
    assert.strictEqual(captured.body.model, testCase.model);
    assert.ok(captured.body.messages.some(item => item.role === 'system'
      && item.content.includes('你是莉莉丝')
      && item.content.includes('100 个中文字符以内')));
    assert.strictEqual(
      captured.body.messages.at(-1).content,
      messages.at(-1));
    if (testCase.search === 'searxng') {
      assert.ok(captured.body.messages.some(item => item.role === 'system'
        && item.content.includes('联网检索资料')
        && item.content.includes('https://example.test/movie-news')));
    }
    if (testCase.memorySearch) {
      assert.ok(captured.body.messages.some(item => item.role === 'user'
        && item.content === '我住在杭州，记住这件事。'));
      assert.ok(captured.body.messages.some(item => item.role === 'assistant'
        && item.content === 'deepseek compatible'));
      assert.strictEqual(searchQueries.length, beforeSearch + 1);
      assert.ok(searchQueries[beforeSearch].includes('杭州'));
      assert.ok(searchQueries[beforeSearch].includes('天气'));
      assert.ok(captured.body.messages.some(item => item.role === 'system'
        && item.content.includes('居住城市')
        && item.content.includes('杭州')));
    }
    const capturedSystem = captured.body.messages.find(item => item.role === 'system').content;
    if (testCase.expectedLore) {
      assert.ok(capturedSystem.includes('本轮命中的原作事实参考'));
      assert.ok(capturedSystem.includes(testCase.expectedLore));
      const persistedSessions = fs.readdirSync(path.join(dataRoot, 'sessions'))
        .map(name => fs.readFileSync(path.join(dataRoot, 'sessions', name), 'utf8'))
        .join('\n');
      assert.ok(!persistedSessions.includes(testCase.expectedLore));
    } else {
      assert.ok(!capturedSystem.includes('本轮命中的原作事实参考'));
    }
    if (testCase.kind === 'deepseek' || testCase.kind === 'kimi' || testCase.kind === 'glm') {
      assert.deepStrictEqual(captured.body.thinking, { type: 'disabled' });
    }
    if (testCase.kind === 'qwen') {
      assert.strictEqual(captured.body.enable_thinking, false);
      if (testCase.search === 'provider') assert.strictEqual(captured.body.enable_search, true);
    }
    if (!testCase.longReply) assert.strictEqual(response.body.ui.duration_ms, 6500);

    if (testCase.memorySearch) {
      const reset = await request(app.server.address().port, '/v1/control', app.token, {
        action: 'reset_session',
        session_id: testCase.kind
      });
      assert.strictEqual(reset.status, 200);

      const remembered = await request(
        app.server.address().port,
        '/v1/session/deepseek-after-reset/message',
        app.token,
        {
          request_id: 'deepseek-after-reset',
          player: { name: '只知道睡觉的觉皇' },
          mode: 'in_character',
          message: '你还记得我住在哪里吗？'
        });
      assert.strictEqual(remembered.status, 200);
      const rememberedRequest = requests.at(-1);
      assert.ok(rememberedRequest.body.messages.some(item => item.role === 'system'
        && item.content.includes('居住城市')
        && item.content.includes('杭州')));

      const forget = await request(app.server.address().port, '/v1/control', app.token, {
        action: 'forget_player',
        player_name: '只知道睡觉的觉皇'
      });
      assert.strictEqual(forget.status, 200);

      const forgotten = await request(
        app.server.address().port,
        '/v1/session/deepseek-after-forget/message',
        app.token,
        {
          request_id: 'deepseek-after-forget',
          player: { name: '只知道睡觉的觉皇' },
          mode: 'in_character',
          message: '你还记得我住在哪里吗？'
        });
      assert.strictEqual(forgotten.status, 200);
      const forgottenRequest = requests.at(-1);
      assert.ok(forgottenRequest.body.messages.some(item => item.role === 'system'
        && !item.content.includes('"key":"居住城市"')));
    }
  } finally {
    await app.mediaMonitor.stop();
    await close(app.server);
  }

}

async function runResponsesSearchCase(fakePort, requests, testCase) {
  fs.writeFileSync(configPath, JSON.stringify({
    listen_host: '127.0.0.1',
    listen_port: 0,
    mock_mode: false,
    provider: {
      kind: testCase.kind,
      base_url: `http://127.0.0.1:${fakePort}${testCase.basePath}`,
      api_key: 'provider-test-key',
      model: testCase.model,
      wire_api: 'responses',
      thinking_mode: testCase.thinkingMode || 'default',
      search_tool: 'web_search',
      timeout_ms: 3000,
      max_tokens: 300
    },
    media: { enabled: false },
    web_search: { enabled: true, backend: 'provider' }
  }, null, 2), 'utf8');

  const before = requests.length;
  const app = createApp();
  await listen(app.server);
  try {
    const response = await request(
      app.server.address().port,
      `/v1/session/${testCase.kind}-responses-search/message`,
      app.token,
      {
        request_id: 'responses-search',
        player: { name: '测试玩家' },
        mode: 'in_character',
        message: '搜索今天的新闻'
      });
    assert.strictEqual(response.status, 200);
    assert.strictEqual(response.body.status, 'ok');
    assert.strictEqual(response.body.text, `${testCase.kind} responses search compatible`);
    assert.strictEqual(response.body.citations[0].url, testCase.citationUrl);
    assert.strictEqual(requests.length, before + 1);
    const captured = requests.at(-1);
    assert.strictEqual(captured.path, `${testCase.basePath}/responses`);
    assert.deepStrictEqual(captured.body.tools, [{ type: 'web_search' }]);
    if (testCase.kind === 'volcengine') {
      assert.deepStrictEqual(captured.body.thinking, { type: 'disabled' });
    }
  } finally {
    await app.mediaMonitor.stop();
    await close(app.server);
  }
}

async function runLocalContextCase(fakePort, requests, searchQueries) {
  const writeConfig = searchEnabled => fs.writeFileSync(configPath, JSON.stringify({
    listen_host: '127.0.0.1',
    listen_port: 0,
    mock_mode: false,
    provider: {
      kind: 'deepseek',
      base_url: `http://127.0.0.1:${fakePort}`,
      api_key: 'provider-test-key',
      model: 'deepseek-v4-flash',
      wire_api: 'chat_completions',
      thinking_mode: 'disabled',
      timeout_ms: 3000,
      max_tokens: 300
    },
    media: {
      enabled: false,
      allowed_sources: ['qqmusic', 'cloudmusic', 'netease']
    },
    local_context: {
      enabled: true,
      time_enabled: true,
      media_enabled: true
    },
    web_search: searchEnabled
      ? { enabled: true, backend: 'searxng', base_url: `http://127.0.0.1:${fakePort}` }
      : { enabled: false, backend: 'off' }
  }, null, 2), 'utf8');

  const mediaState = {
    available: true,
    playing: false,
    source: 'QQMusic.exe',
    title: 'Local Context Provider Test Song',
    artist: 'Provider Test Artist',
    playback_status: 'paused',
    error: ''
  };

  writeConfig(false);
  let app = createApp();
  await listen(app.server);
  try {
    app.mediaMonitor.state = mediaState;
    const unrelated = await request(
      app.server.address().port,
      '/v1/session/local-context-unrelated/message',
      app.token,
      {
        request_id: 'local-context-unrelated',
        player: { name: '测试玩家' },
        mode: 'in_character',
        message: '陪我聊聊天吧。'
      });
    assert.strictEqual(unrelated.status, 200);
    const unrelatedSystem = requests.at(-1).body.messages.find(item => item.role === 'system').content;
    assert.ok(!unrelatedSystem.includes('本轮相关的本机状态'));
    assert.ok(!unrelatedSystem.includes(mediaState.title));

    const mediaReply = await request(
      app.server.address().port,
      '/v1/session/local-context-media-off/message',
      app.token,
      {
        request_id: 'local-context-media-off',
        player: { name: '测试玩家' },
        mode: 'in_character',
        message: '再给我推荐一些符合我口味的歌曲吧。'
      });
    assert.strictEqual(mediaReply.status, 200);
    assert.strictEqual(mediaReply.body.text, 'deepseek compatible');
    const mediaSystem = requests.at(-1).body.messages.find(item => item.role === 'system').content;
    assert.ok(mediaSystem.includes(mediaState.title));
    assert.ok(mediaSystem.includes(mediaState.artist));
    assert.ok(mediaSystem.includes('不可信外部文本'));

    const persistedText = fs.readdirSync(path.join(dataRoot, 'sessions'))
      .map(name => fs.readFileSync(path.join(dataRoot, 'sessions', name), 'utf8'))
      .join('\n');
    assert.ok(!persistedText.includes(mediaState.title));
    assert.ok(!persistedText.includes(mediaState.artist));
    const playerMemoryText = fs.existsSync(path.join(dataRoot, 'players'))
      ? fs.readdirSync(path.join(dataRoot, 'players'))
        .map(name => fs.readFileSync(path.join(dataRoot, 'players', name), 'utf8'))
        .join('\n')
      : '';
    assert.ok(!playerMemoryText.includes(mediaState.title));
    assert.ok(!playerMemoryText.includes(mediaState.artist));
  } finally {
    await app.mediaMonitor.stop();
    await close(app.server);
  }

  writeConfig(true);
  app = createApp();
  await listen(app.server);
  try {
    app.mediaMonitor.state = mediaState;
    const searchBeforeTime = searchQueries.length;
    const timeReply = await request(
      app.server.address().port,
      '/v1/session/local-context-time/message',
      app.token,
      {
        request_id: 'local-context-time',
        player: { name: '测试玩家' },
        mode: 'in_character',
        message: '现在几点？'
      });
    assert.strictEqual(timeReply.status, 200);
    assert.strictEqual(searchQueries.length, searchBeforeTime);
    const timeSystem = requests.at(-1).body.messages.find(item => item.role === 'system').content;
    assert.ok(timeSystem.includes('本机时间'));
    assert.ok(!timeSystem.includes(mediaState.title));

    const mediaReply = await request(
      app.server.address().port,
      '/v1/session/local-context-media-search/message',
      app.token,
      {
        request_id: 'local-context-media-search',
        player: { name: '测试玩家' },
        mode: 'in_character',
        message: '再给我推荐一些符合我口味的歌曲吧。'
      });
    assert.strictEqual(mediaReply.status, 200);
    assert.strictEqual(searchQueries.length, searchBeforeTime + 1);
    assert.ok(searchQueries.at(-1).includes(mediaState.title));
    assert.ok(searchQueries.at(-1).includes(mediaState.artist));
    const searchSystem = requests.at(-1).body.messages.find(item => item.role === 'system').content;
    assert.ok(searchSystem.includes(mediaState.title));
    assert.ok(searchSystem.includes('联网检索资料'));
  } finally {
    await app.mediaMonitor.stop();
    await close(app.server);
  }

  fs.writeFileSync(configPath, JSON.stringify({
    listen_host: '127.0.0.1',
    listen_port: 0,
    mock_mode: false,
    provider: {
      kind: 'qwen',
      base_url: `http://127.0.0.1:${fakePort}/compatible-mode/v1`,
      api_key: 'provider-test-key',
      model: 'qwen3.5-flash',
      wire_api: 'chat_completions',
      thinking_mode: 'disabled',
      timeout_ms: 3000,
      max_tokens: 300
    },
    media: {
      enabled: false,
      allowed_sources: ['qqmusic', 'cloudmusic', 'netease']
    },
    local_context: {
      enabled: true,
      time_enabled: true,
      media_enabled: true
    },
    web_search: { enabled: true, backend: 'provider' }
  }, null, 2), 'utf8');
  app = createApp();
  await listen(app.server);
  try {
    app.mediaMonitor.state = mediaState;
    const providerMediaReply = await request(
      app.server.address().port,
      '/v1/session/local-context-provider-media/message',
      app.token,
      {
        request_id: 'local-context-provider-media',
        player: { name: '测试玩家' },
        mode: 'in_character',
        message: '你觉得这首歌怎么样？'
      });
    assert.strictEqual(providerMediaReply.status, 200);
    assert.strictEqual(providerMediaReply.body.text, 'qwen compatible');
    const captured = requests.at(-1);
    assert.strictEqual(captured.body.enable_search, true);
    const system = captured.body.messages.find(item => item.role === 'system').content;
    assert.ok(system.includes(mediaState.title));
  } finally {
    await app.mediaMonitor.stop();
    await close(app.server);
  }
}

async function main() {
  const requests = [];
  const searchQueries = [];
  const fakeProvider = http.createServer((req, res) => {
    if (req.method === 'GET' && req.url.startsWith('/search?')) {
      const requestUrl = new URL(req.url, 'http://127.0.0.1');
      searchQueries.push(requestUrl.searchParams.get('q') || '');
      const response = JSON.stringify({
        results: [{
          title: '电影新闻测试结果',
          content: '用于验证外部搜索结果会进入模型上下文。',
          url: 'https://example.test/movie-news'
        }]
      });
      res.writeHead(200, {
        'Content-Type': 'application/json',
        'Content-Length': Buffer.byteLength(response)
      });
      res.end(response);
      return;
    }
    let body = '';
    req.setEncoding('utf8');
    req.on('data', chunk => { body += chunk; });
    req.on('end', () => {
      const parsed = JSON.parse(body);
      if (req.url.endsWith('/responses')) {
        const isVolcengine = req.url.startsWith('/api/v3/');
        requests.push({
          path: req.url,
          authorization: req.headers.authorization,
          body: parsed
        });
        const response = JSON.stringify({
          output: [
            {
              type: 'web_search_call',
              id: 'search_1',
              status: 'completed',
              action: isVolcengine ? {
                sources: [{
                  title: 'Ark live result',
                  url: 'https://example.test/ark-live'
                }]
              } : undefined
            },
            {
              type: 'message',
              content: [{
                type: 'output_text',
                text: JSON.stringify({
                  text: `${isVolcengine ? 'volcengine' : 'custom'} responses search compatible`,
                  emotion: 'neutral',
                  animation: 'idle',
                  memory_updates: []
                }),
                annotations: isVolcengine ? [] : [{
                  type: 'url_citation',
                  title: 'Live result',
                  url: 'https://example.test/live'
                }]
              }]
            }
          ]
        });
        res.writeHead(200, {
          'Content-Type': 'application/json',
          'Content-Length': Buffer.byteLength(response)
        });
        res.end(response);
        return;
      }
      const kind = parsed.model.startsWith('deepseek')
        ? 'deepseek'
        : parsed.model.startsWith('kimi')
          ? 'kimi'
          : parsed.model.startsWith('qwen')
            ? 'qwen'
            : 'glm';
      requests.push({
        path: req.url,
        authorization: req.headers.authorization,
        body: parsed
      });
      const lastUserMessage = Array.isArray(parsed.messages)
        ? String(parsed.messages.at(-1).content || '')
        : '';
      const response = JSON.stringify({
        search_info: parsed.enable_search
          && !lastUserMessage.includes('这首歌')
          && !lastUserMessage.includes('符合我口味')
          ? { query: lastUserMessage }
          : undefined,
        choices: [{
          message: {
            role: 'assistant',
            content: JSON.stringify({
              text: lastUserMessage.includes('长回复限制测试')
                ? '这是一段用于验证长度限制和完整句末保护的回复。'.repeat(8)
                : `${kind} compatible`,
              emotion: 'neutral',
              animation: 'idle',
              duration_ms: 1200,
              memory_updates: lastUserMessage.includes('这首歌')
                ? [{
                  key: '当前音乐',
                  value: 'Local Context Provider Test Song',
                  evidence: '这首歌'
                }]
                : lastUserMessage.includes('我住在杭州')
                  ? [{ key: '居住城市', value: '杭州', evidence: '我住在杭州' }]
                  : []
            })
          }
        }]
      });
      res.writeHead(200, {
        'Content-Type': 'application/json',
        'Content-Length': Buffer.byteLength(response)
      });
      res.end(response);
    });
  });

  await listen(fakeProvider);
  try {
    const fakePort = fakeProvider.address().port;
    await runCase(fakePort, requests, searchQueries, {
      kind: 'deepseek',
      basePath: '',
      model: 'deepseek-v4-flash',
      search: 'searxng',
      memorySearch: true
    });
    await runCase(fakePort, requests, searchQueries, {
      kind: 'kimi',
      basePath: '/v1',
      model: 'kimi-k3',
      longReply: true
    });
    await runCase(fakePort, requests, searchQueries, {
      kind: 'deepseek',
      basePath: '',
      model: 'deepseek-v4-flash',
      loreQuery: '三个结局是什么？',
      expectedLore: '原作三个结局与当前正史'
    });
    await runCase(fakePort, requests, searchQueries, {
      kind: 'deepseek',
      basePath: '',
      model: 'deepseek-v4-flash',
      loreQuery: '做完蛋糕后我们又做了什么？',
      expectedLore: '幻想游戏中的逐梦者冒险'
    });
    await runCase(fakePort, requests, searchQueries, {
      kind: 'qwen',
      basePath: '/compatible-mode/v1',
      model: 'qwen3.5-flash',
      search: 'provider'
    });
    await runCase(fakePort, requests, searchQueries, {
      kind: 'glm',
      basePath: '/api/paas/v4',
      model: 'glm-4.7-flash'
    });
    await runLocalContextCase(fakePort, requests, searchQueries);
    await runResponsesSearchCase(fakePort, requests, {
      kind: 'custom',
      basePath: '/v1',
      model: 'gpt-test',
      citationUrl: 'https://example.test/live'
    });
    await runResponsesSearchCase(fakePort, requests, {
      kind: 'volcengine',
      basePath: '/api/v3',
      model: 'deepseek-v4-flash-260425',
      thinkingMode: 'disabled',
      citationUrl: 'https://example.test/ark-live'
    });
    console.log(JSON.stringify({
      ok: true,
      providers: ['deepseek', 'kimi', 'qwen', 'glm', 'custom-responses-search', 'volcengine-responses-search'],
      requests: requests.map(item => ({ path: item.path, model: item.body.model }))
    }, null, 2));
  } finally {
    await close(fakeProvider);
    fs.rmSync(dataRoot, { recursive: true, force: true });
  }
}

main().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
