import { getStatus, pair, forgetPairing } from '../background/bridge.js';
import { CONN } from '../shared/protocol.js';
import { applyI18n, t } from '../shared/i18n.js';
import { hasCookieAccess, enableCookieAccess, disableCookieAccess } from '../background/cookies.js';

const $ = (id) => document.getElementById(id);

function setPill(kind, text) {
  $('pill').className = `pill ${kind}`;
  $('pillText').textContent = text;
}

async function refresh() {
  const s = await getStatus();
  $('errorText').classList.add('hidden');
  $('appVersion').textContent = s.version || '—';

  if (s.state === CONN.CONNECTED) {
    setPill('ok', t('statusConnected'));
    $('btnConnect').classList.add('hidden');
    $('btnForget').classList.remove('hidden');
  } else if (s.state === CONN.NOT_PAIRED) {
    setPill('warn', t('statusNotPaired'));
    $('btnConnect').classList.remove('hidden');
    $('btnConnect').textContent = t('btnConnect');
    $('btnForget').classList.add('hidden');
  } else {
    setPill('bad', t('statusNotRunning'));
    $('btnConnect').classList.remove('hidden');
    $('btnConnect').textContent = t('btnConnect');
    $('btnForget').classList.toggle('hidden', false);
  }
}

$('btnConnect').addEventListener('click', async () => {
  const btn = $('btnConnect');
  btn.disabled = true;
  $('errorText').classList.add('hidden');
  setPill('warn', t('waitingApproval'));
  try {
    await pair();
  } catch (e) {
    const map = { pairing_denied: 'errPairingDenied', pairing_timeout: 'errPairingTimeout', pairing_busy: 'errPairingBusy', not_running: 'statusNotRunning' };
    $('errorText').textContent = t(map[e && e.code] || 'errGeneric');
    $('errorText').classList.remove('hidden');
  }
  btn.disabled = false;
  refresh();
});

$('btnForget').addEventListener('click', async () => {
  await forgetPairing();
  refresh();
});

async function refreshCookies() {
  const on = await hasCookieAccess();
  $('cookiePill').className = `pill ${on ? 'ok' : 'warn'}`;
  $('cookiePillText').textContent = t(on ? 'cookiesOn' : 'cookiesOff');
  $('btnCookiesEnable').classList.toggle('hidden', on);
  $('btnCookiesDisable').classList.toggle('hidden', !on);
}

// permissions.request অবশ্যই এই বাটন-ক্লিক (user gesture) থেকেই ডাকতে হয়
$('btnCookiesEnable').addEventListener('click', async () => {
  $('cookieError').classList.add('hidden');
  let granted = false;
  try { granted = await enableCookieAccess(); } catch { /* granted = false */ }
  if (!granted) {
    $('cookieError').textContent = t('errCookiesDenied');
    $('cookieError').classList.remove('hidden');
  }
  refreshCookies();
});

$('btnCookiesDisable').addEventListener('click', async () => {
  try { await disableCookieAccess(); } catch { /* ignore */ }
  refreshCookies();
});

$('extVersion').textContent = chrome.runtime.getManifest().version;
applyI18n();
refresh();
refreshCookies();
