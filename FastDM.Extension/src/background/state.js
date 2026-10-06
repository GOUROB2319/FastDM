// chrome.storage.local-এ টোকেন ও শেষ পরিচিত পোর্ট
export async function getState() {
  const s = await chrome.storage.local.get(['token', 'port']);
  return { token: s.token || null, port: s.port || null };
}

export async function setState(patch) {
  await chrome.storage.local.set(patch);
}

export async function clearToken() {
  await chrome.storage.local.remove('token');
}
