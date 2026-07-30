'use strict';

const assert = require('assert');
const fs = require('fs');
const http = require('http');
const path = require('path');

const companionRoot = path.resolve(__dirname, '..');
const dataRoot = path.join(companionRoot, '.gemini-test-data');
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

function get(target) {
  return new Promise((resolve, reject) => {
    http.get(target, response => {
      let body = '';
      response.setEncoding('utf8');
      response.on('data', chunk => { body += chunk; });
      response.on('end', () => resolve({ status: response.statusCode, body }));
    }).on('error', reject);
  });
}

function modelOutput(text, annotations = []) {
  return {
    type: 'model_output',
    content: [{ type: 'text', text, annotations }]
  };
}

function envelope(text) {
  return JSON.stringify({
    text,
    emotion: 'neutral',
    animation: 'idle',
    memory_updates: []
  });
}

function sendJson(response, status, value) {
  const body = JSON.stringify(value);
  response.writeHead(status, {
    'Content-Type': 'application/json',
    'Content-Length': Buffer.byteLength(body)
  });
  response.end(body);
}

async function main() {
  fs.rmSync(dataRoot, { recursive: true, force: true });
  fs.mkdirSync(dataRoot, { recursive: true });
  process.env.LILITH_AI_DATA_DIR = dataRoot;
  process.env.LILITH_AI_CONFIG = configPath;

  const captured = [];
  const fakeProvider = http.createServer((request, response) => {
    let raw = '';
    request.setEncoding('utf8');
    request.on('data', chunk => { raw += chunk; });
    request.on('end', () => {
      const body = JSON.parse(raw);
      captured.push({
        path: request.url,
        apiKey: request.headers['x-goog-api-key'],
        authorization: request.headers.authorization,
        body
      });
      const input = Array.isArray(body.input) ? body.input : [];
      const userSteps = input.filter(step => step && step.type === 'user_input');
      const allUserText = userSteps.flatMap(step => Array.isArray(step.content) ? step.content : [])
        .map(item => item && item.text || '').join('\n');
      const current = userSteps.at(-1);
      const content = Array.isArray(current && current.content) ? current.content : [];
      const message = content.map(item => item.text || '').join('');

      if (message === 'provider-error') {
        sendJson(response, 500, { error: { message: 'test failure' } });
        return;
      }
      if (message === 'provider-timeout') {
        setTimeout(() => {
          if (!response.writableEnded) sendJson(response, 200, { steps: [modelOutput(envelope('Late reply.'))] });
        }, 1500);
        return;
      }
      if (userSteps.length > 1 && input.some(step => step && step.signature === 'secret-retry-signature')) {
        sendJson(response, 200, { steps: [modelOutput(envelope('わかった。'))] });
        return;
      }
      if (message === 'retry-search') {
        sendJson(response, 200, {
          steps: [
            { type: 'thought', summary: [{ type: 'text', text: 'temporary thought' }], signature: 'secret-retry-signature' },
            { type: 'google_search_call', arguments: { queries: ['retry query'] } },
            { type: 'google_search_result', call_id: 'retry', result: [{ search_suggestions: '<div>retry suggestion</div>' }] },
            modelOutput(envelope('This is the wrong language.'), [{
              type: 'url_citation', url: 'https://example.test/retry', title: 'Retry source'
            }])
          ]
        });
        return;
      }
      if (allUserText.includes('empty-output')) {
        sendJson(response, 200, { steps: [] });
        return;
      }
      if (message === 'ground-this') {
        sendJson(response, 200, {
          steps: [
            { type: 'thought', summary: [{ type: 'text', text: 'temporary thought' }], signature: 'secret-signature' },
            { type: 'google_search_call', arguments: { queries: ['grounding query'] } },
            { type: 'google_search_result', call_id: 'search-1', result: [{ search_suggestions: '<div class="suggestion">Google suggestion</div>' }] },
            modelOutput(envelope('Grounded reply with <script>alert(1)</script>.'), [
              { type: 'url_citation', url: 'https://example.test/a', title: 'Source A' },
              { type: 'url_citation', url: 'http://example.test/blocked', title: 'Blocked source' },
              { type: 'url_citation', url: 'https://example.test/a', title: 'Duplicate' },
              { type: 'url_citation', url: 'https://example.test/b', title: 'Source B' }
            ])
          ]
        });
        return;
      }
      const localeReply = {
        'locale-zh-CN': '好的。',
        'locale-zh-TW': '好的。',
        'locale-en': 'Okay.',
        'locale-ja': 'わかった。'
      }[message];
      sendJson(response, 200, {
        steps: [modelOutput(message === 'fenced-output'
          ? `\`\`\`json\n${envelope('Fenced reply.')}\n\`\`\``
          : envelope(localeReply || 'Plain reply.'))]
      });
    });
  });

  await listen(fakeProvider);
  fs.writeFileSync(configPath, JSON.stringify({
    listen_host: '127.0.0.1',
    listen_port: 0,
    mock_mode: false,
    provider: {
      kind: 'gemini',
      base_url: `http://127.0.0.1:${fakeProvider.address().port}/v1beta`,
      api_key: 'gemini-test-key',
      model: 'gemini-3.6-flash',
      wire_api: 'gemini_interactions',
      reasoning_effort: 'low',
      timeout_ms: 1200,
      temperature: 0.6,
      max_tokens: 321
    },
    network: { proxy_url: '' },
    media: { enabled: false },
    web_search: { enabled: true, backend: 'provider' }
  }, null, 2), 'utf8');

  const {
    createApp,
    normalizeProxyUrl,
    proxyEnvironment
  } = require('../src/server');
  const opened = [];
  const app = createApp({ openGrounding: url => opened.push(url), groundingTtlMs: 500 });
  await listen(app.server);
  try {
    const send = (session, locale, message) => post(
      app.server.address().port,
      `/v1/session/${session}/message`,
      app.token,
      { request_id: session, player: { name: 'Tester' }, mode: 'in_character', locale, message });

    const systemPrompts = [];
    for (const locale of ['zh-CN', 'zh-TW', 'en', 'ja']) {
      const response = await send(`locale-${locale}`, locale, `locale-${locale}`);
      assert.strictEqual(response.status, 200);
      assert.strictEqual(response.body.status, 'ok');
      systemPrompts.push(captured.at(-1).body.system_instruction);
    }
    assert.strictEqual(new Set(systemPrompts).size, 4);
    assert.strictEqual(opened.length, 0);

    const plain = await send('conversation', 'en', 'plain-first');
    assert.strictEqual(plain.body.text, 'Plain reply.');
    const plainRequest = captured.at(-1);
    assert.strictEqual(plainRequest.path, '/v1beta/interactions');
    assert.strictEqual(plainRequest.apiKey, 'gemini-test-key');
    assert.strictEqual(plainRequest.authorization, undefined);
    assert.strictEqual(plainRequest.body.store, false);
    assert.strictEqual(plainRequest.body.model, 'gemini-3.6-flash');
    assert.deepStrictEqual(plainRequest.body.tools, [{ type: 'google_search' }]);
    assert.deepStrictEqual(plainRequest.body.generation_config, {
      temperature: 0.6,
      thinking_level: 'low',
      max_output_tokens: 321
    });

    const grounded = await send('conversation', 'en', 'ground-this');
    assert.strictEqual(grounded.body.status, 'ok');
    assert.ok(grounded.body.text.includes('Grounded reply'));
    assert.deepStrictEqual(grounded.body.citations.map(item => item.url), [
      'https://example.test/a',
      'https://example.test/b'
    ]);
    const groundedRequest = captured.at(-1);
    assert.ok(groundedRequest.body.system_instruction.includes('LOCAL VISIBLE CHAT TRANSCRIPT'));
    assert.ok(groundedRequest.body.system_instruction.includes('Plain reply.'));
    assert.strictEqual(groundedRequest.body.input.length, 1);
    assert.strictEqual(Object.hasOwn(groundedRequest.body, 'previous_interaction_id'), false);
    await new Promise(resolve => setImmediate(resolve));
    assert.strictEqual(opened.length, 1);
    assert.match(opened[0], /^http:\/\/127\.0\.0\.1:\d+\/v1\/grounding\/[a-f0-9]{64}$/);
    const page = await get(opened[0]);
    assert.strictEqual(page.status, 200);
    assert.ok(page.body.includes('Google suggestion'));
    assert.ok(page.body.includes('&lt;script&gt;alert(1)&lt;/script&gt;'));
    assert.ok(!page.body.includes('http://example.test/blocked'));
    assert.strictEqual((await get(opened[0])).status, 410);

    const retry = await send('retry', 'ja', 'retry-search');
    assert.strictEqual(retry.body.status, 'ok');
    assert.strictEqual(retry.body.text, 'わかった。');
    const retryRequest = captured.at(-1);
    assert.strictEqual(Object.hasOwn(retryRequest.body, 'tools'), false);
    assert.ok(retryRequest.body.input.some(step => step.type === 'thought'
      && step.signature === 'secret-retry-signature'));
    assert.strictEqual(retryRequest.body.input.filter(step => step.type === 'google_search_call').length, 1);
    await new Promise(resolve => setImmediate(resolve));
    assert.strictEqual(opened.length, 2);
    await new Promise(resolve => setTimeout(resolve, 550));
    assert.strictEqual((await get(opened[1])).status, 410);

    assert.strictEqual((await send('fenced', 'en', 'fenced-output')).body.text, 'Fenced reply.');
    assert.strictEqual((await send('empty', 'en', 'empty-output')).body.status, 'fallback');
    assert.strictEqual((await send('error', 'en', 'provider-error')).body.status, 'fallback');
    assert.strictEqual((await send('timeout', 'en', 'provider-timeout')).body.status, 'fallback');

    const proxiedRequests = [];
    const transportProxy = http.createServer((request, response) => {
      let raw = '';
      request.setEncoding('utf8');
      request.on('data', chunk => { raw += chunk; });
      request.on('end', () => {
        proxiedRequests.push({ url: request.url, body: JSON.parse(raw) });
        sendJson(response, 200, { steps: [modelOutput(envelope('Proxy reply.'))] });
      });
    });
    await listen(transportProxy);
    const writeProxyConfig = proxyUrl => fs.writeFileSync(configPath, JSON.stringify({
      listen_host: '127.0.0.1',
      listen_port: 0,
      mock_mode: false,
      provider: {
        kind: 'gemini',
        base_url: 'http://provider.invalid/v1beta',
        api_key: 'proxy-key',
        model: 'gemini-3.6-flash',
        wire_api: 'gemini_interactions',
        reasoning_effort: 'low',
        timeout_ms: 1200,
        max_tokens: 64
      },
      network: { proxy_url: proxyUrl },
      media: { enabled: false },
      web_search: { enabled: false, backend: 'off' }
    }), 'utf8');
    const runProxyCase = async session => {
      const proxyApp = createApp({ openGrounding: () => {} });
      await listen(proxyApp.server);
      try {
        const response = await post(
          proxyApp.server.address().port,
          `/v1/session/${session}/message`,
          proxyApp.token,
          { request_id: session, player: { name: 'Tester' }, mode: 'in_character', locale: 'en', message: 'proxy' });
        assert.strictEqual(response.body.text, 'Proxy reply.');
      } finally {
        await proxyApp.mediaMonitor.stop();
        await close(proxyApp.server);
      }
    };
    const proxyAddress = `http://127.0.0.1:${transportProxy.address().port}`;
    writeProxyConfig(proxyAddress);
    await runProxyCase('explicit-proxy');
    const previousHttpsProxy = process.env.HTTPS_PROXY;
    const previousHttpProxy = process.env.HTTP_PROXY;
    try {
      process.env.HTTPS_PROXY = proxyAddress;
      process.env.HTTP_PROXY = proxyAddress;
      writeProxyConfig('');
      await runProxyCase('environment-proxy');
    } finally {
      if (previousHttpsProxy === undefined) delete process.env.HTTPS_PROXY;
      else process.env.HTTPS_PROXY = previousHttpsProxy;
      if (previousHttpProxy === undefined) delete process.env.HTTP_PROXY;
      else process.env.HTTP_PROXY = previousHttpProxy;
      await close(transportProxy);
    }
    assert.strictEqual(proxiedRequests.length, 2);
    assert.ok(proxiedRequests.every(item => item.url === 'http://provider.invalid/v1beta/interactions'));
    assert.ok(proxiedRequests.every(item => item.body.store === false));

    const persisted = fs.readdirSync(path.join(dataRoot, 'sessions'))
      .map(name => fs.readFileSync(path.join(dataRoot, 'sessions', name), 'utf8'))
      .join('\n');
    for (const forbidden of [
      'temporary thought',
      'secret-signature',
      'secret-retry-signature',
      'Google suggestion',
      'retry suggestion',
      'google_search_call'
    ]) assert.ok(!persisted.includes(forbidden), `session data contained ${forbidden}`);

    assert.strictEqual(normalizeProxyUrl('http://127.0.0.1:7890'), 'http://127.0.0.1:7890/');
    assert.throws(() => normalizeProxyUrl('socks5://127.0.0.1:1080'));
    assert.throws(() => normalizeProxyUrl('http://user:pass@127.0.0.1:7890'));
    const proxyEnv = proxyEnvironment({ proxy_url: 'https://proxy.example:8443' });
    assert.strictEqual(proxyEnv.HTTPS_PROXY, 'https://proxy.example:8443/');
    assert.ok(proxyEnv.NO_PROXY.includes('localhost'));
    assert.ok(proxyEnv.NO_PROXY.includes('127.0.0.1'));
  } finally {
    await app.mediaMonitor.stop();
    await close(app.server);
    await close(fakeProvider);
    fs.rmSync(dataRoot, { recursive: true, force: true });
  }

  console.log('Gemini Interactions tests passed.');
}

main().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
