'use strict';

// Credentials arrive only through stdin, never process arguments or temp files.
const { createProviderRequester, providerErrorCode } = require('./provider-request');
const { applyNetworkProxy } = require('./network');

async function main() {
  const chunks = [];
  for await (const chunk of process.stdin) chunks.push(chunk);
  const input = JSON.parse(Buffer.concat(chunks).toString('utf8').replace(/^\uFEFF/, ''));
  applyNetworkProxy(input);
  const request = createProviderRequester(input.timeout_ms, input.network);
  const response = await request(input.target, input.payload, input.headers);
  process.stdout.write(JSON.stringify({ ok: true, response }));
}

// stdout is a single JSON result. Diagnostic logs use stderr and contain no bodies.
console.log = console.warn = message => process.stderr.write(String(message) + '\n');
main().catch(error => {
  process.stdout.write(JSON.stringify({ ok: false, code: providerErrorCode(error),
    http_status: Number(error.statusCode) || 0 }));
  process.exitCode = 1;
});
