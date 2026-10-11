'use strict';

const assert = require('assert');
const fs = require('fs');
const http = require('http');
const path = require('path');

const companionRoot = path.resolve(__dirname, '..');
const dataRoot = path.join(companionRoot, '.test-update-data');
const configPath = path.join(dataRoot, 'config.json');

function listen(server) {
  return new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
}

function close(server) {
  return new Promise(resolve => server.close(resolve));
}

function requestCompanion(port, target, token) {
  return new Promise((resolve, reject) => {
    const headers = { Accept: 'application/json' };
    if (token) headers.Authorization = `Bearer ${token}`;
    const request = http.request({
      host: '127.0.0.1',
      port,
      path: target,
      method: 'GET',
      headers
    }, response => {
      let body = '';
      response.setEncoding('utf8');
      response.on('data', chunk => { body += chunk; });
      response.on('end', () => resolve({
        status: response.statusCode,
        body: body ? JSON.parse(body) : {}
      }));
    });
    request.on('error', reject);
    request.end();
  });
}

async function main() {
  fs.rmSync(dataRoot, { recursive: true, force: true });
  fs.mkdirSync(dataRoot, { recursive: true });

  let upstreamRequests = 0;
  let upstreamRequest = null;
  const proxy = http.createServer((request, response) => {
    upstreamRequests += 1;
    upstreamRequest = {
      url: request.url,
      accept: request.headers.accept,
      userAgent: request.headers['user-agent'],
      apiVersion: request.headers['x-github-api-version']
    };
    setTimeout(() => {
      response.writeHead(200, { 'Content-Type': 'application/json' });
      response.end(JSON.stringify({
        tag_name: 'v0.3.3',
        html_url: 'https://github.com/cza2019/The-NOexistenceN-of-Lilith-Mod/releases/tag/v0.3.3'
      }));
    }, 30);
  });
  await listen(proxy);

  fs.writeFileSync(configPath, JSON.stringify({
    listen_host: '127.0.0.1',
    listen_port: 0,
    mock_mode: true,
    network: { proxy_url: `http://127.0.0.1:${proxy.address().port}` },
    media: { enabled: false }
  }, null, 2), 'utf8');
  process.env.LILITH_AI_DATA_DIR = dataRoot;
  process.env.LILITH_AI_CONFIG = configPath;

  const {
    checkForUpdate,
    compareReleaseVersions,
    createApp,
    parseReleaseVersion,
    requestGetJson
  } = require('../src/server');

  assert.deepStrictEqual(parseReleaseVersion('v0.3.2'), [0, 3, 2]);
  assert.deepStrictEqual(parseReleaseVersion('2.4-beta+build'), [2, 4, 0]);
  assert.strictEqual(parseReleaseVersion('1.2.3.4.5'), null);
  assert.strictEqual(parseReleaseVersion('1.a'), null);
  assert.strictEqual(compareReleaseVersions([0, 3, 3], [0, 3, 2]), 1);
  assert.strictEqual(compareReleaseVersions([0, 3, 2], [0, 3, 2, 0]), 0);

  const repository = 'cza2019/The-NOexistenceN-of-Lilith-Mod';
  const releaseUrl = version => `https://github.com/${repository}/releases/tag/v${version}`;
  const stubCheck = tag => checkForUpdate({
    currentVersion: '0.3.2',
    repository,
    request: async () => ({ tag_name: tag, html_url: releaseUrl(tag.replace(/^v/, '')) })
  });
  assert.strictEqual((await stubCheck('v0.3.2')).status, 'up_to_date');
  assert.strictEqual((await stubCheck('v0.3.1')).status, 'up_to_date');
  assert.strictEqual((await stubCheck('v0.3.3')).status, 'update_available');
  assert.strictEqual((await stubCheck('v0.3.3-beta')).status, 'update_available');
  assert.strictEqual((await checkForUpdate({
    currentVersion: '0.3.3', repository,
    request: async () => ({ tag_name: 'v0.3.4', html_url: releaseUrl('0.3.4') })
  })).status, 'update_available');
  assert.strictEqual((await checkForUpdate({
    currentVersion: '0.3.2',
    repository: '',
    request: async () => { throw new Error('must not run'); }
  })).status, 'unavailable');
  assert.strictEqual((await checkForUpdate({
    currentVersion: '0.3.2',
    repository,
    request: async () => ({ tag_name: 'not-a-version', html_url: releaseUrl('bad') })
  })).status, 'unavailable');
  assert.strictEqual((await checkForUpdate({
    currentVersion: '0.3.2',
    repository,
    request: async () => ({ tag_name: 'v0.3.3', html_url: 'https://evil.example/releases/tag/v0.3.3' })
  })).status, 'unavailable');
  assert.strictEqual((await checkForUpdate({
    currentVersion: '0.3.2',
    repository,
    request: async () => ({
      tag_name: 'v0.3.3',
      html_url: `https://github.com:444/${repository}/releases/tag/v0.3.3`
    })
  })).status, 'unavailable');

  const failureServer = http.createServer((request, response) => {
    if (request.url === '/hang') return;
    const status = request.url === '/rate-limit' ? 429 : 404;
    response.writeHead(status, { 'Content-Type': 'application/json' });
    response.end(JSON.stringify({ message: 'simulated failure' }));
  });
  await listen(failureServer);
  for (const target of ['/missing', '/rate-limit', '/hang']) {
    const failed = await checkForUpdate({
      currentVersion: '0.3.2',
      repository,
      timeoutMs: 100,
      request: (_url, headers, timeoutMs) => requestGetJson(
        `http://127.0.0.1:${failureServer.address().port}${target}`,
        headers,
        timeoutMs)
    });
    assert.strictEqual(failed.status, 'unavailable', target);
  }
  await close(failureServer);

  const app = createApp({
    releaseInfo: { version: '0.3.2', github_repository: repository },
    updateApiRoot: 'http://github-update.test',
    updateTimeoutMs: 1000
  });
  await listen(app.server);
  try {
    const port = app.server.address().port;
    const unauthorized = await requestCompanion(port, '/v1/update?current_version=0.3.2');
    assert.strictEqual(unauthorized.status, 401);
    const invalid = await requestCompanion(
      port,
      '/v1/update?current_version=0.3.1',
      app.token);
    assert.strictEqual(invalid.status, 400);

    const [first, second] = await Promise.all([
      requestCompanion(port, '/v1/update?current_version=0.3.2', app.token),
      requestCompanion(port, '/v1/update?current_version=0.3.2', app.token)
    ]);
    assert.strictEqual(first.status, 200);
    assert.deepStrictEqual(first.body, second.body);
    assert.deepStrictEqual(first.body, {
      protocol_version: 1,
      status: 'update_available',
      current_version: '0.3.2',
      latest_version: 'v0.3.3',
      release_url: releaseUrl('0.3.3')
    });
    assert.strictEqual(upstreamRequests, 1);
    assert.strictEqual(
      upstreamRequest.url,
      'http://github-update.test/repos/cza2019/The-NOexistenceN-of-Lilith-Mod/releases/latest');
    assert.strictEqual(upstreamRequest.accept, 'application/vnd.github+json');
    assert.strictEqual(upstreamRequest.userAgent, 'LilithMod/0.3.2');
    assert.strictEqual(upstreamRequest.apiVersion, '2022-11-28');
    assert.strictEqual(
      fs.existsSync(path.join(dataRoot, 'update-state.json')),
      false);
  } finally {
    await app.mediaMonitor.stop();
    await close(app.server);
    await close(proxy);
    fs.rmSync(dataRoot, { recursive: true, force: true });
  }

  console.log('Startup update check tests passed.');
}

main().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
