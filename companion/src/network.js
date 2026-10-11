'use strict';

const http = require('http');

function normalizeProxyUrl(value) {
  const text = String(value || '').trim();
  if (!text) return '';
  let parsed;
  try { parsed = new URL(text); } catch {
    throw new Error('proxy_url must be an http:// or https:// URL');
  }
  if (!['http:', 'https:'].includes(parsed.protocol) || parsed.username || parsed.password) {
    throw new Error('proxy_url must be an unauthenticated http:// or https:// URL');
  }
  return parsed.toString();
}

function proxyEnvironment(network = {}) {
  const explicit = normalizeProxyUrl(network.proxy_url);
  const inherited = process.env.HTTPS_PROXY || process.env.https_proxy
    || process.env.HTTP_PROXY || process.env.http_proxy || '';
  let selected = '';
  try { selected = explicit || normalizeProxyUrl(inherited); } catch {
    if (explicit) throw new Error('Invalid proxy_url');
  }
  const existingNoProxy = process.env.NO_PROXY || process.env.no_proxy || '';
  const noProxy = [...new Set(['localhost', '127.0.0.1', '::1',
    ...existingNoProxy.split(',').map(item => item.trim()).filter(Boolean)])].join(',');
  return { ...process.env, HTTP_PROXY: selected, HTTPS_PROXY: selected,
    NO_PROXY: noProxy, http_proxy: selected, https_proxy: selected, no_proxy: noProxy };
}

function providerUsesProxy(target, network) {
  const env = proxyEnvironment(network);
  if (!env.HTTPS_PROXY) return false;
  const url = new URL(target);
  const port = url.port || (url.protocol === 'https:' ? '443' : '80');
  return !env.NO_PROXY.split(',').some(entry => {
    if (entry === '*') return true;
    const [host, entryPort] = entry.toLowerCase().split(':');
    if (entryPort && entryPort !== port) return false;
    if (host.startsWith('*.') || host.startsWith('.')) {
      const suffix = host.replace(/^\*?\./, '');
      return url.hostname === suffix || url.hostname.endsWith('.' + suffix);
    }
    return url.hostname === host;
  });
}

function applyNetworkProxy(config) {
  if (typeof http.setGlobalProxyFromEnv === 'function') {
    http.setGlobalProxyFromEnv(proxyEnvironment(config && config.network));
  }
}

module.exports = { normalizeProxyUrl, proxyEnvironment, providerUsesProxy, applyNetworkProxy };
