'use strict';

const assert = require('assert');
const dgram = require('dgram');
const http = require('http');
const { createProviderLookup, createProviderRequester } = require('../src/provider-request');

async function main() {
  const dns = dgram.createSocket('udp4');
  let queries = 0;
  dns.on('message', (query, remote) => {
    queries++;
    let offset = 12;
    const labels = [];
    while (query[offset]) {
      const length = query[offset++];
      labels.push(query.subarray(offset, offset + length).toString());
      offset += length;
    }
    offset++;
    const type = query.readUInt16BE(offset);
    const hostname = labels.join('.');
    const addresses = hostname === 'missing.invalid' ? []
      : type === 1 ? [Buffer.from([127, 0, 0, 1]), Buffer.from([127, 0, 0, 2])]
        : hostname === 'dual.invalid' ? [Buffer.from('00000000000000000000000000000001', 'hex')] : [];
    const header = Buffer.alloc(12);
    header.writeUInt16BE(query.readUInt16BE(0), 0);
    header.writeUInt16BE(hostname === 'missing.invalid' ? 0x8183 : 0x8180, 2);
    header.writeUInt16BE(1, 4);
    header.writeUInt16BE(addresses.length, 6);
    const answers = addresses.map(address => {
      const record = Buffer.alloc(12);
      record.writeUInt16BE(0xc00c, 0);
      record.writeUInt16BE(type, 2);
      record.writeUInt16BE(1, 4);
      record.writeUInt32BE(30, 6);
      record.writeUInt16BE(address.length, 10);
      return Buffer.concat([record, address]);
    });
    dns.send(Buffer.concat([header, query.subarray(12, offset + 4), ...answers]), remote.port, remote.address);
  });
  await new Promise(resolve => dns.bind(0, '127.0.0.1', resolve));
  const network = { provider_dns_servers: [`127.0.0.1:${dns.address().port}`] };
  const lookup = createProviderLookup(network);
  const resolve = (hostname, options) => new Promise((accept, reject) => lookup(hostname, options,
    (error, address, family) => error ? reject(error) : accept({ address, family })));
  let calls = 0;
  const provider = http.createServer((request, response) => {
    calls++;
    assert.strictEqual(request.headers.host, `provider.invalid:${provider.address().port}`);
    request.resume();
    request.on('end', () => response.end('{"ok":true}'));
  });
  await new Promise(resolve => provider.listen(0, '127.0.0.1', resolve));
  try {
    assert.strictEqual(createProviderLookup(), undefined);
    assert.strictEqual(createProviderLookup({ provider_dns_servers: [] }), undefined);
    const all = await resolve('provider.invalid', { all: true });
    assert.deepStrictEqual(all.address, [{ address: '127.0.0.1', family: 4 }, { address: '127.0.0.2', family: 4 }]);
    assert.deepStrictEqual(await resolve('provider.invalid', { family: 4 }), { address: '127.0.0.1', family: 4 });
    assert.deepStrictEqual(await resolve('dual.invalid', { family: 6 }), { address: '::1', family: 6 });
    const beforeLiteral = queries;
    assert.deepStrictEqual(await resolve('127.0.0.1', { all: true }), { address: [{ address: '127.0.0.1', family: 4 }], family: undefined });
    assert.strictEqual(queries, beforeLiteral);
    await assert.rejects(resolve('missing.invalid', { all: true }), { code: 'ENOTFOUND' });
    const result = await createProviderRequester(3000, network)(
      `http://provider.invalid:${provider.address().port}/chat/completions`, { message: 'Test' }, {});
    assert.deepStrictEqual(result, { ok: true });
    assert.strictEqual(calls, 1);
  } finally {
    await new Promise(resolve => { provider.close(resolve); provider.closeAllConnections(); });
    await new Promise(resolve => dns.close(resolve));
  }
  console.log('Provider DNS tests passed.');
}

main().catch(error => { console.error(error); process.exitCode = 1; });
