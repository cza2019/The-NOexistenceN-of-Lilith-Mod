'use strict';
const http = require('http');
const fs = require('fs');
const assert = require('assert');
let calls = 0;
const server = http.createServer((request, response) => {
  let body = '';
  request.on('data', chunk => { body += chunk; });
  request.on('end', () => {
    assert.strictEqual(request.headers.authorization, 'Bearer settings-private-key');
    assert.strictEqual(JSON.parse(body).content, '\u4f60\u597d');
    if (request.url === '/unauthorized') { response.writeHead(401); response.end('private-upstream-body'); }
    else if (++calls === 1) { response.writeHead(503); response.end('unavailable'); }
    else response.end(JSON.stringify({ content: '\u597d', calls }));
  });
});
server.listen(0, '127.0.0.1', () => fs.writeFileSync(process.argv[2], String(server.address().port)));
