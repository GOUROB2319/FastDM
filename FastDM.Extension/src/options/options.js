import { getStatus, pair, forgetPairing } from '../background/bridge.js';
import { CONN } from '../shared/protocol.js';
import { applyI18n, t } from '../shared/i18n.js';
import { hasCookieAccess, enableCookieAccess, disableCookieAccess } from '../background/cookies.js';
import { optionsView } from '../shared/ui-state.js';

const $ = (id) => document.getElementById(id);
let connectionState = CONN.CHECKING;

function setPill(kind, text) {
  $('pill').className = `pill ${kind}`;
  $('pillText').textContent = text;
}

function applyConnectionState(state) {
  connectionState = state;
  const view = optionsView(state);
  $('btnConnect').classList.toggle('hidden', !view.connect);
  $('btnConnect').disabled = !view.connectEnabled;
  $('btnForget').classList.toggle('hidden', !view.forget);

  if (state === CONN.CONNECTED) {
    setPill('ok', t('statusConnected'));
    $('connectionHint').textContent = t('statusConnected');
  } else if (state === CONN.NOT_PAIRED) {
    setPill('warn', t('statusNotPaired'));
    $('connectionHint').textContent = t('notPairedHint');
  } else if (state === CONN.NOT_RUNNING) {
    setPill('bad', t('statusNotRunning'));
    $('connectionHint').textContent = t('notRunningHint');
  } else if (state === 'error') {
    setPill('bad', t('statusError'));
    $('connectionHint').textContent = t('statusError');
  } else {
    setPill('muted', t('statusChecking'));
    $('connectionHint').textContent = t('statusChecking');
  }
}

async function refresh({ keepError = false } = {}) {
  if (!keepError) $('errorText').classList.add('hidden');
  try {
    const status = await getStatus();
    $('appVersion').textContent = status.version || '—';
    applyConnectionState(status.state);
  } catch {
    $('appVersion').textContent = '—';
    applyConnectionState('error');
    $('errorText').textContent = t('errGeneric');
    $('errorText').classList.remove('hidden');
  }
}

$('btnConnect').addEventListener('click', async () => {
  const btn = $('btnConnect');
  btn.disabled = true;
  $('errorText').classList.add('hidden');
  setPill('warn', t('waitingApproval'));
  $('connectionHint').textContent = t('waitingApproval');
  try {
    await pair();
    await refresh();
  } catch (error) {
    await refresh();
    const map = { pairing_denied: 'errPairingDenied', pairing_timeout: 'errPairingTimeout', pairing_busy: 'errPairingBusy', not_running: 'statusNotRunning' };
    $('errorText').textContent = t(map[error && error.code] || 'errGeneric');
    $('errorText').classList.remove('hidden');
  } finally {
    btn.disabled = !optionsView(connectionState).connectEnabled;
  }
});

$('btnForget').addEventListener('click', async () => {
  const btn = $('btnForget');
  btn.disabled = true;
  try {
    await forgetPairing();
    await refresh();
  } catch {
    $('errorText').textContent = t('errGeneric');
    $('errorText').classList.remove('hidden');
  } finally {
    btn.disabled = false;
  }
});

async function refreshCookies() {
  try {
    const on = await hasCookieAccess();
    $('cookiePill').className = `pill ${on ? 'ok' : 'warn'}`;
    $('cookiePillText').textContent = t(on ? 'cookiesOn' : 'cookiesOff');
    $('btnCookiesEnable').classList.toggle('hidden', on);
    $('btnCookiesDisable').classList.toggle('hidden', !on);
    $('cookieError').classList.add('hidden');
  } catch {
    $('cookiePill').className = 'pill bad';
    $('cookiePillText').textContent = t('statusError');
    $('cookieError').textContent = t('errGeneric');
    $('cookieError').classList.remove('hidden');
    $('btnCookiesEnable').classList.remove('hidden');
    $('btnCookiesDisable').classList.add('hidden');
  }
}

$('btnCookiesEnable').addEventListener('click', async () => {
  const btn = $('btnCookiesEnable');
  btn.disabled = true;
  $('cookieError').classList.add('hidden');
  let message = null;
  try {
    const granted = await enableCookieAccess();
    if (!granted) message = 'errCookiesDenied';
  } catch {
    message = 'errGeneric';
  } finally {
    btn.disabled = false;
    await refreshCookies();
  }
  if (message) {
    $('cookieError').textContent = t(message);
    $('cookieError').classList.remove('hidden');
  }
});

$('btnCookiesDisable').addEventListener('click', async () => {
  const btn = $('btnCookiesDisable');
  btn.disabled = true;
  let failed = false;
  try {
    await disableCookieAccess();
  } catch {
    failed = true;
  } finally {
    btn.disabled = false;
    await refreshCookies();
  }
  if (failed) {
    $('cookieError').textContent = t('errGeneric');
    $('cookieError').classList.remove('hidden');
  }
});

$('extVersion').textContent = chrome.runtime.getManifest().version;
applyI18n();
applyConnectionState(CONN.CHECKING);
refresh();
refreshCookies();
