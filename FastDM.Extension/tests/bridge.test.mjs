// extension-এর bridge.js কে একটা নকল অ্যাপ-সার্ভারের বিরুদ্ধে টেস্ট (Node 18+ লাগে: node --test tests/)
import test from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';

// ---- chrome API-র নকল ----
const store = {};
const tabsCreated = [];
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
};

const bridge = await import('../src/background/bridge.js');
const { CONN, PORTS } = await import('../src/shared/protocol.js');

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

const reset = () => { for (const k of Object.keys(store)) delete store[k]; tabsCreated.length = 0; };

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
