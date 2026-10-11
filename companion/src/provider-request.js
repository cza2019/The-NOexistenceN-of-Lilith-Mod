'use strict';

const crypto = require('crypto');
const http = require('http');
const https = require('https');
const { Resolver } = require('dns').promises;
const net = require('net');
const { providerUsesProxy } = require('./network');

const CONNECTION_TIMEOUT_MS = 5000;
const FALLBACK_TTL_MS = 10 * 60 * 1000;
const OFFICIAL_HOSTS = new Set(['api.deepseek.com', 'api.moonshot.cn', 'api.moonshot.ai',
  'dashscope.aliyuncs.com', 'open.bigmodel.cn', 'ark.cn-beijing.volces.com',
  'generativelanguage.googleapis.com']);
const fallbackCache = new Map();
let fallbackEpoch = 0;
function clearProviderDnsCache() { fallbackCache.clear(); fallbackEpoch += 1; }

const TRANSIENT_CODES = new Set([
  'ECONNRESET', 'ECONNREFUSED', 'EPIPE', 'ETIMEDOUT', 'EAI_AGAIN',
  'ERR_STREAM_PREMATURE_CLOSE', 'ETIMEOUT', 'PROVIDER_TIMEOUT', 'PROVIDER_CONNECT_TIMEOUT',
  'EPROTO', 'ERR_TLS_HANDSHAKE_TIMEOUT', 'ERR_SSL_SSLV3_ALERT_HANDSHAKE_FAILURE',
  'ERR_SSL_TLSV1_ALERT_INTERNAL_ERROR', 'ERR_SSL_WRONG_VERSION_NUMBER'
]);
const TRANSIENT_STATUS = new Set([408, 429, 500, 502, 503, 504]);
const SAFE_CODES = new Set([
  ...TRANSIENT_CODES, 'ENOTFOUND', 'ENETUNREACH', 'EHOSTUNREACH',
  'CERT_HAS_EXPIRED', 'DEPTH_ZERO_SELF_SIGNED_CERT',
  'UNABLE_TO_VERIFY_LEAF_SIGNATURE', 'ERR_TLS_CERT_ALTNAME_INVALID',
  'PROVIDER_HTTP', 'PROVIDER_INVALID_JSON'
]);

function providerErrorCode(error) {
  // Never log upstream bodies, URLs, error messages, headers or user text.
  return SAFE_CODES.has(error && error.code) ? error.code : 'PROVIDER_ERROR';
}

function providerError(code) {
  return Object.assign(new Error(code), { code });
}

function retryDelay(value) {
  if (!value) return 500;
  const seconds = Number(value);
  const delay = Number.isFinite(seconds) ? seconds * 1000 : Date.parse(value) - Date.now();
  return Number.isFinite(delay) ? Math.max(500, delay) : 500;
}

function createProviderLookup(network = {}) {
  const servers = network.provider_dns_servers;
  if (!Array.isArray(servers) || !servers.length) return undefined;
  // This resolver is scoped to provider requests. It does not change Windows
  // DNS, pin CDN addresses, or bypass hostname/certificate verification.
  const resolver = new Resolver({ timeout: 1000, tries: 2 });
  resolver.setServers(servers);
  return (hostname, options, callback) => {
    const family = typeof options === 'number' ? options : options.family || 0;
    const all = Boolean(options && options.all);
    const literalFamily = net.isIP(hostname);
    const answer = addresses => all
      ? callback(null, addresses)
      : callback(null, addresses[0].address, addresses[0].family);
    if (literalFamily) return answer([{ address: hostname, family: literalFamily }]);
    const queries = [];
    if (family !== 6) queries.push(resolver.resolve4(hostname)
      .then(addresses => addresses.map(address => ({ address, family: 4 }))));
    if (family !== 4) queries.push(resolver.resolve6(hostname)
      .then(addresses => addresses.map(address => ({ address, family: 6 }))));
    Promise.allSettled(queries).then(results => {
      const addresses = results.flatMap(result => result.status === 'fulfilled' ? result.value : []);
      if (addresses.length) return answer(addresses);
      const failure = results.find(result => result.status === 'rejected'
        && result.reason.code !== 'ENODATA' && result.reason.code !== 'ENOTFOUND');
      callback(failure ? failure.reason : providerError('ENOTFOUND'));
    });
  };
}

function requestJson(target, payload, headers, timeoutMs, lookup) {
  return new Promise((resolve, reject) => {
    const url = new URL(target);
    const transport = url.protocol === 'https:' ? https : http;
    const body = JSON.stringify(payload);
    let timer;
    let connectionTimer;
    let stage = 'connect';
    const socketListeners = [];
    const cleanup = () => {
      clearTimeout(timer);
      clearTimeout(connectionTimer);
      for (const [socket, event, listener] of socketListeners) socket.removeListener(event, listener);
    };
    const fail = error => {
      cleanup();
      error.requestStage = stage;
      error.reusedSocket = Boolean(request.reusedSocket);
      reject(error);
    };
    const request = transport.request(url, {
      method: 'POST',
      headers: {
        ...headers,
        'Content-Type': 'application/json',
        'Content-Length': Buffer.byteLength(body)
      },
      timeout: timeoutMs,
      lookup
    }, response => {
      stage = 'response';
      let responseBody = '';
      response.setEncoding('utf8');
      response.on('data', chunk => { responseBody += chunk; });
      response.on('error', fail);
      response.on('end', () => {
        cleanup();
        // An overloaded gateway can return HTML. Preserve its HTTP status.
        if (response.statusCode < 200 || response.statusCode >= 300) {
          const error = providerError('PROVIDER_HTTP');
          error.statusCode = response.statusCode;
          error.retryDelayMs = retryDelay(response.headers['retry-after']);
          error.requestStage = stage;
          error.reusedSocket = Boolean(request.reusedSocket);
          reject(error);
          return;
        }
        try {
          resolve(responseBody ? JSON.parse(responseBody) : {});
        } catch {
          fail(providerError('PROVIDER_INVALID_JSON'));
        }
      });
    });
    request.on('socket', socket => {
      const connected = () => {
        stage = 'response_wait';
        clearTimeout(connectionTimer);
      };
      if (request.reusedSocket || (!socket.connecting
        && (url.protocol !== 'https:' || socket.encrypted && !socket.secureConnecting))) connected();
      const track = (event, listener) => {
        socket.on(event, listener);
        socketListeners.push([socket, event, listener]);
      };
      track('connect', () => { if (url.protocol === 'https:') stage = 'tls'; else connected(); });
      track('secureConnect', connected);
    });
    const timeout = () => request.destroy(providerError('PROVIDER_TIMEOUT'));
    request.on('timeout', timeout);
    request.on('error', fail);
    timer = setTimeout(timeout, timeoutMs);
    timer.unref();
    connectionTimer = setTimeout(() => request.destroy(providerError('PROVIDER_CONNECT_TIMEOUT')),
      Math.min(CONNECTION_TIMEOUT_MS, timeoutMs));
    connectionTimer.unref();
    request.end(body);
  });
}

function createProviderRequester(timeoutMs, network = {}, dependencies = {}) {
  const now = dependencies.now || Date.now;
  const request = dependencies.request || requestJson;
  const lookupFactory = dependencies.lookup || createProviderLookup;
  const usesProxy = dependencies.usesProxy || providerUsesProxy;
  const cache = dependencies.cache || fallbackCache;
  const epoch = fallbackEpoch;
  const sleep = dependencies.sleep || (ms => new Promise(resolve => setTimeout(resolve, ms)));
  const started = now();
  const deadline = started + Math.max(1000, Number(timeoutMs) || 30000);
  const id = crypto.randomUUID();
  let retries = 0;
  let attempts = 0;
  const manualLookup = lookupFactory(network);
  // One transient retry and one time budget for the entire reply, including
  // any language correction. Never turn a 30s timeout into multiple 30s waits.
  return async (target, payload, headers) => {
    const url = new URL(target);
    const proxied = usesProxy(target, network);
    const automatic = network.provider_dns_fallback_enabled !== false && !manualLookup
      && !proxied && url.protocol === 'https:' && OFFICIAL_HOSTS.has(url.hostname);
    if (cache.get(url.hostname) <= now()) cache.delete(url.hostname);
    let route = proxied ? 'proxy' : manualLookup ? 'manual'
      : automatic && cache.has(url.hostname) ? 'fallback_cache' : 'system';
    const backupLookup = () => lookupFactory({ provider_dns_servers: ['223.5.5.5'] });
    for (;;) {
      attempts += 1;
      try {
        const remaining = deadline - now();
        if (remaining <= 0) throw providerError('PROVIDER_TIMEOUT');
        const lookup = proxied ? undefined : route.startsWith('fallback') ? backupLookup() : manualLookup;
        const result = await request(target, payload, headers, remaining, lookup);
        // An in-flight request from before a reload must not restore stale preference.
        if (route === 'fallback' && epoch === fallbackEpoch) cache.set(url.hostname, now() + FALLBACK_TTL_MS);
        console.log(`Provider request ${retries ? 'recovered' : 'completed'}: id=${id}; dns=${route}; attempt=${attempts}; elapsed_ms=${now() - started}; retry=${retries > 0}`);
        return result;
      } catch (error) {
        const transient = TRANSIENT_CODES.has(error.code) || TRANSIENT_STATUS.has(error.statusCode);
        const switchDns = automatic && error.requestStage === 'tls' && transient && !error.statusCode;
        const delay = switchDns ? 0 : error.retryDelayMs || 500;
        const retry = transient && retries < 1 && deadline - now() > delay + 100;
        console.warn(`Provider request failed: time=${new Date().toISOString()}; id=${id}; code=${providerErrorCode(error)}; stage=${error.requestStage || 'deadline'}; dns=${route}; reused_socket=${Boolean(error.reusedSocket)}; http_status=${error.statusCode || 0}; attempt=${attempts}; elapsed_ms=${now() - started}; retry=${retry}; dns_switch=${retry && switchDns}`);
        if (!retry) throw error;
        retries += 1;
        if (switchDns) {
          if (route.startsWith('fallback')) { cache.delete(url.hostname); route = 'system'; }
          else route = 'fallback';
        }
        await sleep(delay);
      }
    }
  };
}

module.exports = { createProviderLookup, createProviderRequester, providerErrorCode,
  clearProviderDnsCache, requestJson, CONNECTION_TIMEOUT_MS, FALLBACK_TTL_MS };
