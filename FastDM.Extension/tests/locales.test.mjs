// Language files and manifest: no missing or mismatched text keys (Node 18+: node --test tests/locales.test.mjs)
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (p) => fs.readFileSync(path.join(root, p), 'utf8');
const json = (p) => JSON.parse(read(p));
const walk = (dir) => fs.readdirSync(path.join(root, dir), { withFileTypes: true }).flatMap((e) =>
  e.isDirectory() ? walk(path.join(dir, e.name)) : [path.join(dir, e.name)]);

const en = json('_locales/en/messages.json');
const bn = json('_locales/bn/messages.json');
const manifest = json('manifest.json');

test('English and Bangla have exactly the same keys', () => {
  const enKeys = Object.keys(en).sort();
  const bnKeys = Object.keys(bn).sort();
  assert.deepEqual(bnKeys.filter((k) => !(k in en)), [], 'keys only in bn');
  assert.deepEqual(enKeys.filter((k) => !(k in bn)), [], 'keys only in en');
});

test('every message has non-empty text', () => {
  for (const [lang, file] of [['en', en], ['bn', bn]]) {
    for (const [key, v] of Object.entries(file)) {
      assert.ok(v && typeof v.message === 'string' && v.message.trim().length > 0, `${lang}.${key} is empty`);
    }
  }
});

test('manifest __MSG_x__ references exist', () => {
  const refs = [...JSON.stringify(manifest).matchAll(/__MSG_([A-Za-z0-9_]+)__/g)].map((m) => m[1]);
  assert.ok(refs.length > 0);
  for (const key of refs) assert.ok(key in en, `manifest uses missing key ${key}`);
});

test('data-i18n keys in HTML and t(\'key\') calls in JS exist', () => {
  const files = walk('src');
  const used = new Map();
  const add = (key, file) => { if (!used.has(key)) used.set(key, file); };

  for (const f of files.filter((x) => x.endsWith('.html'))) {
    for (const m of read(f).matchAll(/data-i18n(?:-title)?="([A-Za-z0-9_]+)"/g)) add(m[1], f);
  }
  for (const f of files.filter((x) => x.endsWith('.js'))) {
    for (const m of read(f).matchAll(/\bt\(\s*'([A-Za-z0-9_]+)'/g)) add(m[1], f);
  }
  assert.ok(used.size > 10, 'expected to find many keys');
  for (const [key, file] of used) assert.ok(key in en, `${file} uses missing key ${key}`);
});
