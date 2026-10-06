import { getStatus, pair, forgetPairing } from '../background/bridge.js';
import { CONN } from '../shared/protocol.js';
import { applyI18n, t } from '../shared/i18n.js';

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

$('extVersion').textContent = chrome.runtime.getManifest().version;
applyI18n();
refresh();
