import test from 'node:test';
import assert from 'node:assert/strict';
import { cookiesFor } from '../src/background/cookies.js';

test('cookiesFor preserves browser expiry and scope metadata for one URL', async () => {
  let requestedUrl = '';
  globalThis.chrome = {
    permissions: { contains: async () => true },
    cookies: {
      getAll: async ({ url }) => {
        requestedUrl = url;
        return [{ name: 'session', value: 'value', domain: '.example.test', path: '/private', secure: true, expirationDate: 1_900_000_000 }];
      },
    },
  };

  const cookies = await cookiesFor('https://www.example.test/private/file');
  assert.equal(requestedUrl, 'https://www.example.test/private/file');
  assert.deepEqual(cookies, [{
    name: 'session',
    value: 'value',
    domain: '.example.test',
    path: '/private',
    secure: true,
    expirationDate: 1_900_000_000,
  }]);
});

test('cookiesFor sends no cookies for unsupported schemes or without permission', async () => {
  let reads = 0;
  globalThis.chrome = {
    permissions: { contains: async () => false },
    cookies: { getAll: async () => { reads++; return []; } },
  };

  assert.deepEqual(await cookiesFor('ftp://example.test/file'), []);
  assert.deepEqual(await cookiesFor('https://example.test/file'), []);
  assert.equal(reads, 0);
});
