import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const css = fs.readFileSync(path.join(root, 'src/shared/tokens.css'), 'utf8');

function parseBlock(text) {
  const values = {};
  for (const line of text.split(/\r?\n/)) {
    const match = line.match(/^\s*(--[\w-]+)\s*:\s*(#[0-9a-fA-F]{3,8})\s*;/);
    if (match) values[match[1]] = match[2];
  }
  return values;
}

function themeBlocks(source) {
  const lightMatch = source.match(/:root\s*\{([\s\S]*?)\}/);
  const darkMatch = source.match(/@media\s*\(prefers-color-scheme:\s*dark\)\s*\{\s*:root\s*\{([\s\S]*?)\}/);
  assert.ok(lightMatch, 'light :root token block exists');
  assert.ok(darkMatch, 'dark :root token block exists');
  const light = parseBlock(lightMatch[1]);
  const darkOverrides = parseBlock(darkMatch[1]);
  return {
    light,
    dark: { ...light, ...darkOverrides },
  };
}

function rgb(hex) {
  let value = hex.slice(1);
  if (value.length === 3) value = [...value].map(x => x + x).join('');
  assert.equal(value.length, 6, `expected RGB hex color: ${hex}`);
  return [0, 2, 4].map(i => parseInt(value.slice(i, i + 2), 16));
}

function luminance(hex) {
  const [r, g, b] = rgb(hex).map(v => {
    const channel = v / 255;
    return channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

function contrastRatio(foreground, background) {
  const a = luminance(foreground);
  const b = luminance(background);
  return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
}

test('primary button foreground reaches 4.5:1 in light and dark themes', () => {
  const themes = themeBlocks(css);
  for (const [name, tokens] of Object.entries(themes)) {
    assert.ok(tokens['--accent-strong'], `${name} --accent-strong exists`);
    assert.ok(tokens['--on-accent'], `${name} --on-accent exists`);
    const ratio = contrastRatio(tokens['--on-accent'], tokens['--accent-strong']);
    assert.ok(ratio >= 4.5, `${name} on-accent contrast is ${ratio.toFixed(2)}:1`);
  }
});

test('danger text reaches 4.5:1 on window and alternate rows in light and dark themes', () => {
  const themes = themeBlocks(css);
  for (const [name, tokens] of Object.entries(themes)) {
    assert.ok(tokens['--danger-text'], `${name} --danger-text exists`);
    for (const surface of ['--window', '--row-alt']) {
      const ratio = contrastRatio(tokens['--danger-text'], tokens[surface]);
      assert.ok(ratio >= 4.5, `${name} danger-text on ${surface} contrast is ${ratio.toFixed(2)}:1`);
    }
  }
});

test('primary buttons and error messages consume the dedicated contrast tokens', () => {
  assert.match(css, /button\.primary\s*\{[^}]*background:\s*var\(--accent-strong\)/s);
  assert.match(css, /button\.primary\s*\{[^}]*color:\s*var\(--on-accent\)/s);
  assert.match(fs.readFileSync(path.join(root, 'src/popup/popup.css'), 'utf8'), /\.error\s*\{\s*color:\s*var\(--danger-text\)/);
  assert.match(fs.readFileSync(path.join(root, 'src/options/options.css'), 'utf8'), /\.error\s*\{\s*color:\s*var\(--danger-text\)/);
});
