'use strict';

const assert = require('assert');
const { normalizeResponse, stripParentheticalActions } = require('../src/server');

const firstExample = '（歪了歪头，露出促狭的笑容）三天而已嘛～莉莉丝会一直在你手机里陪着你。';
assert.strictEqual(
  stripParentheticalActions(firstExample),
  '三天而已嘛～莉莉丝会一直在你手机里陪着你。');
assert.strictEqual(
  stripParentheticalActions('111……（忍不住轻笑）这是个神秘暗号吗？'),
  '111……这是个神秘暗号吗？');
assert.strictEqual(
  stripParentheticalActions('《不/存在的你，和我》（2026 年版）'),
  '《不/存在的你，和我》（2026 年版）');

assert.strictEqual(
  normalizeResponse({ text: firstExample }, '', 100).text,
  '三天而已嘛～莉莉丝会一直在你手机里陪着你。');
assert.strictEqual(
  normalizeResponse({ text: firstExample }, '', 100, true).text,
  firstExample);
assert.strictEqual(
  normalizeResponse({ text: '（轻轻笑了笑）' }, '', 100).text,
  '……');

console.log('Response normalization tests passed.');
