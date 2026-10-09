// extension-এর bridge.js কে একটা নকল অ্যাপ-সার্ভারের বিরুদ্ধে টেস্ট (Node 18+ লাগে: node --test tests/)
import test from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';

// ---- chrome API-র নকল ----
const store = {};
const tabsCreated = [];
const permState = { granted: false };
const cookieCalls = [];
let cookieJar = [];
globalThis.chrome = {
  storage: {
    local: {
      async get(keys) { const o = {}; for (const k of [].concat(keys)) if (k in store) o[k] = store[k]; return o; },
      async set(p) { Object.assign(store, p); },
      async remove(k) { for (const x of [].concat(k)) delete store[x]; },
    },
  },
  tabs: {
    async create(o) { tabsCreated.push(o.url); return { id: tabsCreated.length }; },
    remove() { return Promise.resolve(); },
  },
  i18n: { getMessage: (k) => k },
  permissions: {
    async contains() { return permState.granted; },
  },
  cookies: {
    async getAll({ url }) { cookieCalls.push(url); return cookieJar; },
  },
};

const bridge = await import('../src/background/bridge.js');
const { CONN, PORTS } = await import('../src/shared/protocol.js');
const menus = await import('../src/background/menus.js');
const cookies = await import('../src/background/cookies.js');

// ---- নকল অ্যাপ ----
function fakeApp(port, opts = {}) {
  const validTokens = new Set(opts.tokens || []);
  const added = [];
  let pairMode = opts.pairMode || 'allow';       // allow | deny | busy

  const server = http.createServer((req, res) => {
    const send = (code, obj) => {
      res.writeHead(code, { 'Content-Type': 'application/json', Connection: 'close' });
      res.end(obj === undefined ? '' : JSON.stringify(obj));
    };
    const token = req.headers['x-fastdm-token'];
    const paired = validTokens.has(token);

    if (req.method === 'GET' && req.url === '/v1/ping') return send(200, { app: 'FastDM', version: '1.0.0', protocol: 1, paired });
    if (req.method === 'POST' && req.url === '/v1/pair') {
      if (pairMode === 'deny') return send(403, { error: 'pairing_denied' });
      if (pairMode === 'busy') return send(429, { error: 'pairing_busy' });
      const t = 'tok-' + Math.random().toString(36).slice(2);
      validTokens.add(t);
      return send(200, { token: t });
    }
    if (!paired) return send(401, { error: 'unauthorized' });
    if (req.method === 'POST' && req.url === '/v1/add') {
      let body = '';
      req.on('data', (c) => (body += c));
      req.on('end', () => { added.push(JSON.parse(body)); send(200, { ok: true, status: 'opened' }); });
      return;
    }
    if (req.method === 'GET' && req.url === '/v1/tasks') return send(200, [{ id: 'a', name: 'f.zip', state: 'Downloading', percent: 40, speed: 1000, size: 5000, downloaded: 2000, eta: '3s' }]);
    return send(404, { error: 'not_found' });
  });

  return {
    added,
    revokeAll: () => validTokens.clear(),
    setPairMode: (m) => { pairMode = m; },
    start: () => new Promise((r) => server.listen(port, '127.0.0.1', r)),
    stop: () => new Promise((r) => { server.closeAllConnections?.(); server.close(r); }),
  };
}

const reset = () => {
  for (const k of Object.keys(store)) delete store[k];
  tabsCreated.length = 0;
  permState.granted = false;
  cookieCalls.length = 0;
  cookieJar = [];
};

test('অ্যাপ বন্ধ থাকলে NOT_RUNNING', async () => {
  reset();
  assert.equal((await bridge.getStatus()).state, CONN.NOT_RUNNING);
});

test('পেয়ারের আগে NOT_PAIRED, পেয়ারের পর CONNECTED, লিঙ্ক অ্যাপে পৌঁছায়', async () => {
  reset();
  const app = fakeApp(PORTS[2]);          // মাঝের একটা পোর্টে চালু: ডিসকভারি পরীক্ষা
  await app.start();
  try {
    assert.equal((await bridge.getStatus()).state, CONN.NOT_PAIRED);
    assert.equal(await bridge.pair(), true);
    assert.ok(store.token, 'টোকেন সেভ হওয়া উচিত');
    assert.equal(store.port, PORTS[2]);
    assert.equal((await bridge.getStatus()).state, CONN.CONNECTED);

    const r = await bridge.sendLink('https://example.com/a.zip', { title: 'T' });
    assert.deepEqual(r, { via: 'bridge' });
    assert.equal(app.added.length, 1);
    assert.equal(app.added[0].url, 'https://example.com/a.zip');
    assert.equal(app.added[0].kind, 'auto');

    const tasks = await bridge.listTasks();
    assert.equal(tasks[0].name, 'f.zip');
  } finally { await app.stop(); }
});

test('পেয়ারিং ছাড়া sendLink needsPairing দেয়', async () => {
  reset();
  const app = fakeApp(PORTS[0]);
  await app.start();
  try {
    assert.deepEqual(await bridge.sendLink('https://example.com/x'), { needsPairing: true });
    assert.equal(app.added.length, 0);
  } finally { await app.stop(); }
});

test('অ্যাপ বন্ধ থাকলে fastdm:// ফলব্যাক, লিঙ্ক এনকোড করা', async () => {
  reset();
  const url = 'https://example.com/a b?x=1&y=2';
  const r = await bridge.sendLink(url);
  assert.deepEqual(r, { via: 'protocol' });
  assert.equal(tabsCreated.length, 1);
  assert.equal(tabsCreated[0], 'fastdm://add?url=' + encodeURIComponent(url));
});

test('অ্যাপ টোকেন বাতিল করলে টোকেন মুছে যায়, needsPairing', async () => {
  reset();
  const app = fakeApp(PORTS[0]);
  await app.start();
  try {
    await bridge.pair();
    app.revokeAll();
    assert.deepEqual(await bridge.sendLink('https://example.com/x'), { needsPairing: true });
    assert.equal(store.token, undefined);
  } finally { await app.stop(); }
});

test('পেয়ারিং প্রত্যাখ্যান/ব্যস্ততার কোড ঠিকমতো আসে', async () => {
  reset();
  const app = fakeApp(PORTS[0]);
  await app.start();
  try {
    app.setPairMode('deny');
    await assert.rejects(bridge.pair(), (e) => e.code === 'pairing_denied');
    app.setPairMode('busy');
    await assert.rejects(bridge.pair(), (e) => e.code === 'pairing_busy');
    assert.equal(store.token, undefined);
  } finally { await app.stop(); }
});

// ---------------- Phase 2 ----------------

test('Phase 2: mode, referer, userAgent, cookies অ্যাপের কাছে পৌঁছায়', async () => {
  reset();
  const app = fakeApp(PORTS[0]);
  await app.start();
  try {
    await bridge.pair();
    const meta = {
      mode: 'download',
      referer: 'https://site.example/page',
      userAgent: 'UA/1.0',
      cookies: [{ name: 'sid', value: 'abc', domain: '.site.example', path: '/', secure: true }],
    };
    assert.deepEqual(await bridge.sendLink('https://cdn.site.example/f.zip', meta), { via: 'bridge' });
    const body = app.added[0];
    assert.equal(body.mode, 'download');
    assert.equal(body.referer, 'https://site.example/page');
    assert.equal(body.userAgent, 'UA/1.0');
    assert.equal(body.cookies[0].name, 'sid');
    assert.equal(body.kind, 'auto');
  } finally { await app.stop(); }
});

test('Phase 2: মেনু আইটেম → mode (Download = সরাসরি, Open/Page = ডায়ালগ)', () => {
  const M = menus.MENU;
  assert.equal(menus.modeFor(M.DL_LINK), 'download');
  assert.equal(menus.modeFor(M.DL_MEDIA), 'download');
  assert.equal(menus.modeFor(M.OPEN_LINK), 'open');
  assert.equal(menus.modeFor(M.OPEN_MEDIA), 'open');
  assert.equal(menus.modeFor(M.OPEN_PAGE), 'open');
  assert.equal(menus.modeFor(M.PARENT), null);
  assert.equal(menus.modeFor('something-else'), null);
});

test('Phase 2: targetUrl আর refererFor', () => {
  const M = menus.MENU;
  const info = { linkUrl: 'https://a.test/f.zip', pageUrl: 'https://a.test/dl', frameUrl: 'https://a.test/frame', srcUrl: 'blob:xyz' };
  assert.equal(menus.targetUrl({ ...info, menuItemId: M.DL_LINK }, {}), 'https://a.test/f.zip');
  assert.equal(menus.targetUrl({ ...info, menuItemId: M.OPEN_LINK }, {}), 'https://a.test/f.zip');
  // blob: মিডিয়া হলে পেজের লিঙ্কে ফলব্যাক
  assert.equal(menus.targetUrl({ ...info, menuItemId: M.DL_MEDIA }, {}), 'https://a.test/dl');
  assert.equal(menus.targetUrl({ ...info, srcUrl: 'https://a.test/v.mp4', menuItemId: M.OPEN_MEDIA }, {}), 'https://a.test/v.mp4');
  assert.equal(menus.targetUrl({ pageUrl: 'https://a.test/dl', menuItemId: M.OPEN_PAGE }, {}), 'https://a.test/dl');
  assert.equal(menus.refererFor({ ...info, menuItemId: M.DL_LINK }), 'https://a.test/frame');
  assert.equal(menus.refererFor({ pageUrl: 'https://a.test/dl', menuItemId: M.DL_LINK }), 'https://a.test/dl');
  assert.equal(menus.refererFor({ ...info, menuItemId: M.OPEN_PAGE }), undefined);
});

test('Phase 2: permission না থাকলে কুকি খালি, getAll ডাকাই হয় না', async () => {
  reset();
  cookieJar = [{ name: 'sid', value: 'abc', domain: 'a.test', path: '/', secure: true }];
  assert.deepEqual(await cookies.cookiesFor('https://a.test/f.zip'), []);
  assert.equal(cookieCalls.length, 0);
});

test('Phase 2: permission থাকলে কুকি bridge ফরম্যাটে; ftp/অবৈধ URL-এ খালি', async () => {
  reset();
  permState.granted = true;
  cookieJar = [{ name: 'sid', value: 'abc', domain: '.a.test', path: '/x', secure: true, httpOnly: true, storeId: '0' }];
  assert.deepEqual(await cookies.cookiesFor('https://a.test/f.zip'), [
    { name: 'sid', value: 'abc', domain: '.a.test', path: '/x', secure: true },
  ]);
  assert.deepEqual(await cookies.cookiesFor('ftp://a.test/f.zip'), []);
  assert.deepEqual(await cookies.cookiesFor(''), []);
  assert.equal(cookieCalls.length, 1);
});

// ---------------- Phase 3 ----------------

test('Phase 3: playlist মেনু → mode playlist, লিঙ্ক ও পেজের URL', () => {
  const M = menus.MENU;
  assert.equal(menus.modeFor(M.PLAYLIST_LINK), 'playlist');
  assert.equal(menus.modeFor(M.PLAYLIST_PAGE), 'playlist');
  const info = { linkUrl: 'https://a.test/movies/', pageUrl: 'https://a.test/', frameUrl: 'https://a.test/' };
  assert.equal(menus.targetUrl({ ...info, menuItemId: M.PLAYLIST_LINK }, {}), 'https://a.test/movies/');
  assert.equal(menus.targetUrl({ pageUrl: 'https://a.test/movies/', menuItemId: M.PLAYLIST_PAGE }, {}), 'https://a.test/movies/');
  assert.equal(menus.refererFor({ ...info, menuItemId: M.PLAYLIST_PAGE }), undefined);
  assert.equal(menus.refererFor({ ...info, menuItemId: M.PLAYLIST_LINK }), 'https://a.test/');
});

test('Phase 3: অ্যাপ বন্ধ থাকলে playlist mode fastdm://playlist দিয়ে চালু করে', async () => {
  reset();
  // কোনো অ্যাপ চলছে না → bridge পৌঁছায় না → ফলব্যাক
  const r = await bridge.sendLink('https://a.test/movies/', { mode: 'playlist' });
  assert.deepEqual(r, { via: 'protocol' });
  assert.ok(tabsCreated.at(-1).startsWith('fastdm://playlist?url='));
  const r2 = await bridge.sendLink('https://a.test/f.zip', { mode: 'download' });
  assert.deepEqual(r2, { via: 'protocol' });
  assert.ok(tabsCreated.at(-1).startsWith('fastdm://add?url='));
});
