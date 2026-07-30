'use strict';

const assert = require('assert');
const fs = require('fs');
const path = require('path');
const { formatLorePrompt, loadLore, retrieveLore } = require('../src/lore');
const {
  DEFAULT_LOCALE,
  SUPPORTED_LOCALES,
  localizedCharacterPath,
  localizedLorePath,
  normalizeLocale,
  readCatalog
} = require('../src/localization');

const root = path.resolve(__dirname, '..', '..');
assert.strictEqual(normalizeLocale('zh-Hant'), 'zh-TW');
assert.strictEqual(normalizeLocale('zh_CN'), 'zh-CN');
assert.strictEqual(normalizeLocale('en-US'), 'en');
assert.strictEqual(normalizeLocale('ja-JP'), 'ja');
assert.strictEqual(normalizeLocale('unknown'), DEFAULT_LOCALE);

const baseKeys = Object.keys(readCatalog(DEFAULT_LOCALE).strings).sort();
const baseLore = loadLore(localizedLorePath(root, DEFAULT_LOCALE));
const baseIds = baseLore.entries.map(item => item.id);
for (const locale of SUPPORTED_LOCALES) {
  const catalog = readCatalog(locale);
  assert.deepStrictEqual(Object.keys(catalog.strings).sort(), baseKeys, `${locale} UI keys differ`);
  const card = JSON.parse(fs.readFileSync(localizedCharacterPath(root, locale), 'utf8'));
  assert.strictEqual(card.character_id, 'lilith');
  if (locale !== DEFAULT_LOCALE) assert.strictEqual(card.locale, locale);
  const lore = loadLore(localizedLorePath(root, locale));
  assert.deepStrictEqual(lore.entries.map(item => item.id), baseIds, `${locale} lore IDs differ`);
  for (let index = 0; index < lore.entries.length; index += 1) {
    assert.deepStrictEqual(lore.entries[index].related, baseLore.entries[index].related, `${locale} related differs`);
    assert.deepStrictEqual(lore.entries[index].source_refs, baseLore.entries[index].source_refs, `${locale} sources differ`);
  }
}

const cases = {
  'zh-CN': [
    ['我们做过草莓蛋糕吗？', 'main-strawberry-cake'],
    ['三个结局是什么？', 'ending-three-routes'],
    ['为什么桌宠里的莉莉丝还在？', 'post-ending1-desktop']
  ],
  'zh-TW': [
    ['我們做過草莓蛋糕嗎？', 'main-strawberry-cake'],
    ['三個結局是什麼？', 'ending-three-routes'],
    ['為什麼桌寵裡的莉莉絲還在？', 'post-ending1-desktop']
  ],
  en: [
    ['What happened when we made strawberry cake?', 'main-strawberry-cake'],
    ['What are the three endings?', 'ending-three-routes'],
    ['Why is desktop Lilith still here?', 'post-ending1-desktop']
  ],
  ja: [
    ['苺のケーキを作った時、何があった？', 'main-strawberry-cake'],
    ['三つのエンディングは何？', 'ending-three-routes'],
    ['なぜデスクトップのリリスがまだいるの？', 'post-ending1-desktop']
  ]
};

for (const [locale, localeCases] of Object.entries(cases)) {
  const lore = loadLore(localizedLorePath(root, locale));
  for (const [message, expected] of localeCases) {
    const result = retrieveLore(message, [], lore, { locale });
    assert.ok(result.entries.some(item => item.id === expected), `${locale} did not match ${expected}: ${message}`);
    assert.ok(formatLorePrompt(result, { locale }).length > 0);
  }
  assert.deepStrictEqual(
    retrieveLore(locale === 'en' ? 'Stay and chat with me.' : locale === 'ja' ? '少し話そう。' : '陪我聊聊天。', [], lore, { locale }).entries,
    []);
}

console.log(JSON.stringify({ ok: true, locales: SUPPORTED_LOCALES, ui_keys: baseKeys.length, lore_entries: baseIds.length }));
