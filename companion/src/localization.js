'use strict';

const fs = require('fs');
const path = require('path');

const SUPPORTED_LOCALES = Object.freeze(['zh-CN', 'zh-TW', 'en', 'ja']);
const DEFAULT_LOCALE = 'zh-CN';
const CATALOG_ROOT = path.resolve(__dirname, '..', '..', 'locales');

const catalogs = new Map();

function normalizeLocale(value, fallback = DEFAULT_LOCALE) {
  const raw = String(value || '').trim().replace(/_/g, '-');
  if (!raw) return fallback;
  const lower = raw.toLowerCase();
  if (lower === 'follow-game' || lower === 'follow_game' || lower === 'auto') return fallback;
  if (lower === 'zh-tw' || lower === 'zh-hant' || lower === 'zh-hk' || lower === 'zh-mo'
    || lower.includes('traditional')) return 'zh-TW';
  if (lower === 'zh-cn' || lower === 'zh-hans' || lower === 'zh-sg'
    || lower.includes('simplified')) return 'zh-CN';
  if (lower === 'ja' || lower.startsWith('ja-') || lower.includes('japanese')) return 'ja';
  if (lower === 'en' || lower.startsWith('en-') || lower.includes('english')) return 'en';
  return SUPPORTED_LOCALES.includes(raw) ? raw : fallback;
}

function readCatalog(locale) {
  const normalized = normalizeLocale(locale);
  if (catalogs.has(normalized)) return catalogs.get(normalized);
  let parsed = null;
  try {
    parsed = JSON.parse(fs.readFileSync(path.join(CATALOG_ROOT, `${normalized}.json`), 'utf8')
      .replace(/^\uFEFF/, ''));
  } catch (error) {
    if (normalized !== DEFAULT_LOCALE) return readCatalog(DEFAULT_LOCALE);
  }
  const strings = parsed && parsed.strings && typeof parsed.strings === 'object'
    ? parsed.strings
    : {};
  const catalog = { locale: normalized, strings };
  catalogs.set(normalized, catalog);
  return catalog;
}

function formatTemplate(value, args) {
  return String(value || '').replace(/\{(\d+)\}/g, (match, rawIndex) => {
    const index = Number(rawIndex);
    return index >= 0 && index < args.length ? String(args[index]) : match;
  });
}

function t(locale, key, ...args) {
  const normalized = normalizeLocale(locale);
  const localized = readCatalog(normalized).strings[key];
  const fallback = readCatalog(DEFAULT_LOCALE).strings[key];
  return formatTemplate(localized === undefined ? (fallback === undefined ? key : fallback) : localized, args);
}

function localizedCharacterPath(projectRoot, locale, configuredPath = '') {
  if (configuredPath) return configuredPath;
  const normalized = normalizeLocale(locale);
  if (normalized === DEFAULT_LOCALE) {
    return path.join(projectRoot, 'character', 'lilith.json');
  }
  return path.join(projectRoot, 'character', 'locales', normalized, 'lilith.json');
}

function localizedLorePath(projectRoot, locale, configuredPath = '') {
  if (configuredPath) return configuredPath;
  const normalized = normalizeLocale(locale);
  if (normalized === DEFAULT_LOCALE) {
    return path.join(projectRoot, 'character', 'lore', 'index.json');
  }
  return path.join(projectRoot, 'character', 'lore', `index.${normalized}.json`);
}

function clearCatalogCache() {
  catalogs.clear();
}

module.exports = {
  CATALOG_ROOT,
  DEFAULT_LOCALE,
  SUPPORTED_LOCALES,
  clearCatalogCache,
  localizedCharacterPath,
  localizedLorePath,
  normalizeLocale,
  readCatalog,
  t
};
