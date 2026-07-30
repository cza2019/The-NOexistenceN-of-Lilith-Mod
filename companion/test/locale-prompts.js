'use strict';

const assert = require('assert');
const fs = require('fs');
const path = require('path');
const { buildSystemPrompt, localContextPrompt, replyMatchesLocale } = require('../src/server');
const { localizedCharacterPath } = require('../src/localization');

const root = path.resolve(__dirname, '..', '..');
const markers = {
  'zh-CN': ['莉莉丝', '简体中文', '本机状态'],
  'zh-TW': ['莉莉絲', '繁體中文', '本機狀態'],
  en: ['Lilith', 'natural English', 'local state'],
  ja: ['リリス', '自然な日本語', 'ローカル状態']
};

for (const [locale, expected] of Object.entries(markers)) {
  const character = JSON.parse(fs.readFileSync(localizedCharacterPath(root, locale), 'utf8'));
  const local = { time: { date: '2026-07-29', time: '12:34', weekday: 'test' } };
  const prompt = buildSystemPrompt(character, 'Player', 'in_character', [], [], 100, local, false, '', locale);
  assert.ok(prompt.includes(expected[0]), `${locale} card missing`);
  assert.ok(prompt.includes(expected[1]), `${locale} language rule missing`);
  assert.ok(localContextPrompt(local, locale).toLocaleLowerCase().includes(expected[2].toLocaleLowerCase()), `${locale} local context missing`);
  assert.ok(prompt.includes('100 Unicode characters'));
  assert.ok(prompt.split('\n').at(-1).includes('FINAL OUTPUT LANGUAGE REQUIREMENT'));
}

assert.strictEqual(replyMatchesLocale('测试收到，你在测试我的反应吗？', 'en'), false);
assert.strictEqual(replyMatchesLocale('Test received. Are you checking my reaction?', 'en'), true);
assert.strictEqual(replyMatchesLocale('I recommend 夏色片想い by 菊池桃子.', 'en'), true);
assert.strictEqual(replyMatchesLocale('嗯？test……你在测试我的反应吗？', 'ja'), false);
assert.strictEqual(replyMatchesLocale('テストだね。ちゃんと届いているよ、覚皇。', 'ja'), true);
assert.strictEqual(replyMatchesLocale('了解、覚皇。', 'ja'), true);
assert.strictEqual(replyMatchesLocale('Apple Musicで「孤独の肖像」を再生しているよ、觉皇。', 'ja'), true);
assert.strictEqual(replyMatchesLocale('This answer is written entirely in English instead of Japanese.', 'ja'), false);
assert.strictEqual(replyMatchesLocale('テスト收到。', 'zh-CN'), false);
assert.strictEqual(replyMatchesLocale('测试收到。', 'zh-CN'), true);
assert.strictEqual(replyMatchesLocale('我正在听夏色片想い，这首歌很轻快。', 'zh-CN'), true);
assert.strictEqual(replyMatchesLocale('你正在听旦那様とのラブラブ・ラブソング，是钉宫理惠演唱的。', 'zh-CN'), true);
assert.strictEqual(replyMatchesLocale('推荐 Lemon、Pretender 和 夜に駆ける。', 'zh-CN'), true);
assert.strictEqual(replyMatchesLocale('Okay，推荐你听这首歌。', 'zh-CN'), true);
assert.strictEqual(replyMatchesLocale('我正在聽夏色片想い，這首歌很輕快。', 'zh-TW'), true);
assert.strictEqual(replyMatchesLocale('This answer is written entirely in English instead of Chinese.', 'zh-CN'), false);
assert.strictEqual(replyMatchesLocale('これはすべて日本語だけで書いてあります。', 'zh-CN'), false);
assert.strictEqual(replyMatchesLocale('12345……', 'zh-CN'), true);
assert.strictEqual(replyMatchesLocale('♪', 'en'), true);

console.log(JSON.stringify({ ok: true, locales: Object.keys(markers) }));
