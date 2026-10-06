import { PORTS, HEADER_TOKEN, CONN } from '../shared/protocol.js';
import { getState, setState, clearToken } from './state.js';

export class BridgeError extends Error {
  constructor(code, message, status) {
    super(message || code);
    this.code = code;       // unreachable | not_running | not_paired | pairing_denied | pairing_timeout | pairing_busy | bad_url | rate_limited | http_error
    this.status = status;
  }
}

async function request(port, path, { method = 'GET', token, body, timeoutMs = 2500 } = {}) {
  const headers = {};
  if (token) headers[HEADER_TOKEN] = token;
  if (body !== undefined) headers['Content-Type'] = 'application/json';

  const ctrl = new AbortController();
  const timer = setTimeout(() => ctrl.abort(), timeoutMs);
  try {
    const res = await fetch(`http://127.0.0.1:${port}${path}`, {
      method,
      headers,
      body: body !== undefined ? JSON.stringify(body) : undefined,
      signal: ctrl.signal,
    });
    let data = null;
    try { data = await res.json(); } catch { /* খালি বডি (যেমন 204) */ }
    return { ok: res.ok, status: res.status, data };
  } catch (e) {
    throw new BridgeError('unreachable', e && e.message);
  } finally {
    clearTimeout(timer);
  }
}

// অ্যাপ কোন পোর্টে চলছে খুঁজে বের করা (শেষ পরিচিত পোর্ট আগে)
export async function discover() {
  const { token, port: last } = await getState();
  const order = last ? [last, ...PORTS.filter((p) => p !== last)] : PORTS;
  for (const port of order) {
    try {
      const r = await request(port, '/v1/ping', { token, timeoutMs: 800 });
      if (r.ok && r.data && r.data.app === 'FastDM') {
        if (port !== last) await setState({ port });
        return { port, info: r.data, token };
      }
    } catch { /* এই পোর্টে অ্যাপ নেই */ }
  }
  return null;
}

// পপআপ/অপশনে দেখানোর জন্য সংযোগের অবস্থা
export async function getStatus() {
  const found = await discover();
  if (!found) return { state: CONN.NOT_RUNNING };
  return {
    state: found.info.paired ? CONN.CONNECTED : CONN.NOT_PAIRED,
    port: found.port,
    version: found.info.version,
  };
}

// পেয়ারিং: অ্যাপে অনুমোদন ডায়ালগ আসে, ইউজার Yes দিলে টোকেন পাই
export async function pair() {
  const found = await discover();
  if (!found) throw new BridgeError('not_running');
  const r = await request(found.port, '/v1/pair', { method: 'POST', timeoutMs: 70000 });
  if (r.ok && r.data && r.data.token) {
    await setState({ token: r.data.token, port: found.port });
    return true;
  }
  const code = (r.data && r.data.error) || 'http_error';
  throw new BridgeError(code, code, r.status);
}

export async function forgetPairing() {
  await clearToken();
}

async function authed(path, opts = {}) {
  const found = await discover();
  if (!found) throw new BridgeError('not_running');
  if (!found.info.paired) {
    // অ্যাপ বলছে এই ব্রাউজার পেয়ার করা নয় (যেমন অ্যাপে "Forget paired browsers" হয়েছে): পুরোনো টোকেন ফেলে দিই
    if (found.token) await clearToken();
    throw new BridgeError('not_paired');
  }
  const r = await request(found.port, path, { ...opts, token: found.token });
  if (r.status === 401) {
    await clearToken();
    throw new BridgeError('not_paired', 'unauthorized', 401);
  }
  if (!r.ok) {
    const code = (r.data && r.data.error) || 'http_error';
    throw new BridgeError(code, code, r.status);
  }
  return r.data;
}

export function add(url, meta = {}) {
  return authed('/v1/add', { method: 'POST', body: { url, kind: 'auto', ...meta } });
}

export function listTasks() {
  return authed('/v1/tasks');
}

export function taskAction(id, action) {
  return authed(`/v1/tasks/${encodeURIComponent(id)}/${action}`, { method: 'POST' });
}

// fastdm:// দিয়ে অ্যাপ চালু করা (অ্যাপ বন্ধ থাকলে)। লিঙ্ক দিলে অ্যাপ Add ডায়ালগ খোলে।
export async function launchApp(url) {
  const target = url ? `fastdm://add?url=${encodeURIComponent(url)}` : 'fastdm://open';
  const tab = await chrome.tabs.create({ url: target, active: false });
  setTimeout(() => { chrome.tabs.remove(tab.id).catch(() => {}); }, 2000);
  return target;
}

// ক্লিক থেকে লিঙ্ক পাঠানোর মূল ফাংশন
//   { via: 'bridge' }   → অ্যাপে পৌঁছেছে, Add ডায়ালগ খুলেছে
//   { via: 'protocol' } → অ্যাপ বন্ধ ছিল, fastdm:// দিয়ে চালু করা হয়েছে
//   { needsPairing }    → আগে "Connect" করতে হবে
//   { error }           → অন্য কোনো সমস্যা
export async function sendLink(url, meta = {}) {
  try {
    await add(url, meta);
    return { via: 'bridge' };
  } catch (e) {
    if (!(e instanceof BridgeError)) return { error: String(e && e.message) };
    if (e.code === 'not_running') {
      try {
        await launchApp(url);
        return { via: 'protocol' };
      } catch (e2) {
        return { error: String(e2 && e2.message) };
      }
    }
    if (e.code === 'not_paired') return { needsPairing: true };
    return { error: e.code };
  }
}
