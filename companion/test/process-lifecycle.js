'use strict';

const assert = require('assert');
const childProcess = require('child_process');
const fs = require('fs');
const net = require('net');
const os = require('os');
const path = require('path');

async function freePort() {
  const server = net.createServer();
  await new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(0, '127.0.0.1', resolve);
  });
  const port = server.address().port;
  await new Promise(resolve => server.close(resolve));
  return port;
}

async function main() {
  const dataRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'lilith-parent-test-'));
  const port = await freePort();
  fs.writeFileSync(path.join(dataRoot, 'config.json'), JSON.stringify({
    listen_host: '127.0.0.1',
    listen_port: port,
    mock_mode: true,
    media: { enabled: false }
  }), 'utf8');

  const serverPath = path.resolve(__dirname, '..', 'src', 'server.js');
  const child = childProcess.spawn(process.execPath, [serverPath], {
    env: {
      ...process.env,
      LILITH_AI_DATA_DIR: dataRoot,
      LILITH_GAME_PID: '2147483647'
    },
    stdio: ['ignore', 'pipe', 'pipe'],
    windowsHide: true
  });
  let stdout = '';
  let stderr = '';
  child.stdout.on('data', chunk => { stdout += chunk; });
  child.stderr.on('data', chunk => { stderr += chunk; });

  let timeout;
  try {
    const exit = await Promise.race([
      new Promise(resolve => child.once('exit', (code, signal) => resolve({ code, signal }))),
      new Promise((_, reject) => {
        timeout = setTimeout(() => reject(new Error('Companion did not exit after its game parent disappeared.')), 5000);
      })
    ]);
    assert.strictEqual(exit.code, 0, stderr);
    assert.match(stdout, /LilithCompanion listening/);
    assert.strictEqual(fs.existsSync(path.join(dataRoot, 'runtime.json')), false);
  } finally {
    clearTimeout(timeout);
    if (!child.killed) child.kill();
    fs.rmSync(dataRoot, { recursive: true, force: true });
  }
  console.log('Companion parent lifecycle test passed.');
}

main().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
