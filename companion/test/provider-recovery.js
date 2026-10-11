'use strict';

const assert = require('assert');
const net = require('net');
const http = require('http');
const { spawn } = require('child_process');
const path = require('path');
const fs = require('fs');
const os = require('os');
const { createProviderRequester, requestJson, CONNECTION_TIMEOUT_MS, FALLBACK_TTL_MS,
  clearProviderDnsCache } = require('../src/provider-request');

const target = 'https://api.deepseek.com/chat/completions';
const failure = (code = 'ECONNRESET', stage = 'tls', statusCode) =>
  Object.assign(new Error('private-error-marker'), { code, requestStage: stage, statusCode });
const response = { choices: [{ message: { content: 'OK' } }] };

async function policyTests() {
  let time = 1000;
  const cache = new Map();
  let calls = [];
  let failSystem = true;
  let failBackup = false;
  const deps = {
    cache, now: () => time, usesProxy: () => false,
    sleep: async ms => { time += ms; },
    lookup: network => network.provider_dns_servers?.length ? network.provider_dns_servers.join(',') : undefined,
    request: async (_target, _body, _headers, budget, lookup) => {
      calls.push({ budget, lookup });
      time += 30;
      if (!lookup && failSystem || lookup && failBackup) throw failure();
      return response;
    }
  };
  const run = (network = {}, url = target, options = deps) =>
    createProviderRequester(10000, network, options)(url, { private: 'private-body-marker' },
      { Authorization: 'private-key-marker' });
  assert.deepStrictEqual(await run(), response);
  assert.deepStrictEqual(calls.map(x => x.lookup), [undefined, '223.5.5.5']);
  assert.ok(calls[1].budget < calls[0].budget);
  for (const code of ['PROVIDER_CONNECT_TIMEOUT', 'EPROTO', 'ERR_TLS_HANDSHAKE_TIMEOUT',
    'ERR_SSL_SSLV3_ALERT_HANDSHAKE_FAILURE']) {
    cache.clear(); calls = [];
    await run({}, target, { ...deps, request: async (...args) => {
      calls.push({ lookup: args[4] });
      if (!args[4]) throw failure(code);
      return response;
    } });
    assert.deepStrictEqual(calls.map(x => x.lookup), [undefined, '223.5.5.5']);
  }
  calls = [];
  await run();
  assert.deepStrictEqual(calls.map(x => x.lookup), ['223.5.5.5']);
  time += FALLBACK_TTL_MS;
  calls = [];
  await run();
  assert.deepStrictEqual(calls.map(x => x.lookup), [undefined, '223.5.5.5']);
  cache.clear();
  failBackup = true;
  calls = [];
  await assert.rejects(run());
  assert.strictEqual(calls.length, 2);
  assert.strictEqual(cache.size, 0);
  failBackup = false;
  for (const network of [{ provider_dns_fallback_enabled: false }, { provider_dns_servers: ['1.1.1.1'] }]) {
    calls = [];
    if (network.provider_dns_servers) await run(network);
    else await assert.rejects(run(network));
    assert.ok(calls.every(call => call.lookup !== '223.5.5.5'));
  }
  calls = [];
  await assert.rejects(run({}, 'https://private.example/chat'));
  assert.ok(calls.every(call => call.lookup === undefined));
  calls = [];
  await assert.rejects(run({ provider_dns_servers: ['1.1.1.1'] }, target,
    { ...deps, usesProxy: () => true }));
  assert.ok(calls.every(call => call.lookup === undefined), 'Proxy must resolve its own target');
  for (const status of [400, 401, 402, 403, 404, 422, 500, 503]) {
    calls = [];
    await assert.rejects(run({}, target, { ...deps, request: async (...args) => {
      calls.push(args[4]); throw failure('PROVIDER_HTTP', 'response', status);
    } }));
    assert.ok(calls.every(lookup => lookup === undefined));
    assert.strictEqual(calls.length, status >= 500 ? 2 : 1);
  }
  for (const [code, stage] of [['ECONNRESET', 'response_wait'], ['ECONNREFUSED', 'connect'],
    ['ERR_TLS_CERT_ALTNAME_INVALID', 'tls']]) {
    calls = [];
    await assert.rejects(run({}, target, { ...deps, request: async (...args) => {
      calls.push(args[4]); throw failure(code, stage);
    } }));
    assert.ok(calls.every(lookup => lookup === undefined));
  }
  // The retry allowance is also shared with the language correction request.
  let count = 0;
  const shared = createProviderRequester(10000, {}, { ...deps, request: async () => {
    if (++count !== 2) throw failure(); return response;
  } });
  await shared(target, {}, {});
  await assert.rejects(shared(target, {}, {}));
  assert.strictEqual(count, 3);
  let inFlightCalls = 0;
  const inFlight = createProviderRequester(10000, {}, { ...deps, request: async () => {
    if (++inFlightCalls === 1) throw failure();
    clearProviderDnsCache();
    return response;
  } });
  cache.clear();
  await inFlight(target, {}, {});
  assert.strictEqual(cache.size, 0, 'A pre-reload request must not repopulate the cleared cache');
  // Test the production cache reset with successive requester instances.
  clearProviderDnsCache();
  failSystem = true;
  const productionDeps = { ...deps }; delete productionDeps.cache;
  calls = [];
  await run({}, target, productionDeps);
  calls = [];
  await run({}, target, productionDeps);
  assert.deepStrictEqual(calls.map(x => x.lookup), ['223.5.5.5']);
  const dataRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'lilith-dns-reload-'));
  process.env.LILITH_AI_DATA_DIR = dataRoot;
  process.env.LILITH_AI_CONFIG = path.join(dataRoot, 'config.json');
  fs.writeFileSync(process.env.LILITH_AI_CONFIG, JSON.stringify({ mock_mode: true, media: { enabled: false } }));
  const { createApp } = require('../src/server');
  const app = createApp();
  await new Promise(resolve => app.server.listen(0, '127.0.0.1', resolve));
  try {
    const reloaded = await fetch(`http://127.0.0.1:${app.server.address().port}/v1/control`, {
      method: 'POST', headers: { Authorization: `Bearer ${app.token}`, 'Content-Type': 'application/json' },
      body: JSON.stringify({ action: 'reload' })
    });
    assert.strictEqual(reloaded.status, 200);
    assert.strictEqual((await reloaded.json()).reloaded, true);
  } finally {
    await app.mediaMonitor.stop();
    app.server.closeAllConnections();
    await new Promise(resolve => app.server.close(resolve));
    fs.rmSync(dataRoot, { recursive: true, force: true });
  }
  calls = [];
  await run({}, target, productionDeps);
  assert.deepStrictEqual(calls.map(x => x.lookup), [undefined, '223.5.5.5']);
  clearProviderDnsCache();
}

async function connectionTimerTest() {
  const sockets = new Set();
  const server = net.createServer(socket => { sockets.add(socket); socket.on('error', () => {}); });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const started = Date.now();
  try {
    await assert.rejects(requestJson(`https://127.0.0.1:${server.address().port}`, {}, {}, 15000), error => {
      assert.strictEqual(error.code, 'PROVIDER_CONNECT_TIMEOUT');
      assert.strictEqual(error.requestStage, 'tls');
      return true;
    });
    const elapsed = Date.now() - started;
    assert.ok(elapsed >= CONNECTION_TIMEOUT_MS - 100 && elapsed < CONNECTION_TIMEOUT_MS + 1500);
  } finally {
    for (const socket of sockets) socket.destroy();
    await new Promise(resolve => server.close(resolve));
  }
}

async function cliTest() {
  let calls = 0;
  const proxy = http.createServer((request, result) => {
    assert.strictEqual(request.headers.authorization, 'Bearer private-key-marker');
    assert.strictEqual(request.url, 'http://provider.invalid/chat/completions');
    if (++calls === 1) { request.socket.destroy(); return; }
    result.end(JSON.stringify(response));
  });
  await new Promise(resolve => proxy.listen(0, '127.0.0.1', resolve));
  try {
    const child = spawn(process.execPath, [path.resolve(__dirname, '../src/provider-test.js')],
      { env: { ...process.env, NO_PROXY: '', no_proxy: '' } });
    let stdout = ''; let stderr = '';
    child.stdout.on('data', chunk => { stdout += chunk; });
    child.stderr.on('data', chunk => { stderr += chunk; });
    child.stdin.end(JSON.stringify({ target: 'http://provider.invalid/chat/completions',
      payload: { content: 'private-body-marker' }, headers: { Authorization: 'Bearer private-key-marker' },
      network: { proxy_url: `http://127.0.0.1:${proxy.address().port}`, provider_dns_servers: ['223.5.5.5'] },
      timeout_ms: 3000 }));
    const exit = await new Promise(resolve => child.on('exit', resolve));
    assert.strictEqual(exit, 0, stderr);
    assert.deepStrictEqual(JSON.parse(stdout), { ok: true, response });
    assert.strictEqual(calls, 2);
    assert.ok(stderr.includes('dns=proxy'));
    assert.ok(!stderr.includes('private-'));
  } finally {
    proxy.closeAllConnections();
    await new Promise(resolve => proxy.close(resolve));
  }
}

(async () => {
  const logs = []; const warn = console.warn; const log = console.log;
  console.warn = console.log = value => logs.push(value);
  try {
    await policyTests();
    await connectionTimerTest();
    await cliTest();
    assert.ok(logs.some(line => line.includes('dns_switch=true')));
    assert.ok(logs.every(line => !line.includes('private-')));
  } finally { console.warn = warn; console.log = log; }
  console.log('Provider automatic recovery, TLS deadline, cache, proxy and CLI tests passed.');
})().catch(error => { console.error(error); process.exitCode = 1; });
