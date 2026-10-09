import { getStatus, pair, listTasks, taskAction, BridgeError } from '../background/bridge.js';
import { CONN, TASK_STATE } from '../shared/protocol.js';
import { applyI18n, t, fmtBytes } from '../shared/i18n.js';

const $ = (id) => document.getElementById(id);
const el = {
  pill: $('pill'), pillText: $('pillText'),
  panelProblem: $('panelProblem'), problemText: $('problemText'), problemError: $('problemError'),
  btnConnect: $('btnConnect'), btnOpenApp: $('btnOpenApp'),
  panelTasks: $('panelTasks'), taskList: $('taskList'), empty: $('empty'),
  btnOpen: $('btnOpen'), btnOptions: $('btnOptions'),
};

let pairing = false;
let timer = null;

function setPill(kind, text) {
  el.pill.className = `pill ${kind}`;
  el.pillText.textContent = text;
}

const STATE_KEY = {
  [TASK_STATE.QUEUED]: 'stateQueued',
  [TASK_STATE.DOWNLOADING]: 'stateDownloading',
  [TASK_STATE.PAUSED]: 'statePaused',
  [TASK_STATE.COMPLETE]: 'stateComplete',
  [TASK_STATE.ERROR]: 'stateError',
};

function renderTasks(tasks) {
  el.taskList.replaceChildren();
  el.empty.classList.toggle('hidden', tasks.length > 0);

  for (const task of tasks) {
    const li = document.createElement('li');
    li.className = `task ${String(task.state).toLowerCase()}`;

    const top = document.createElement('div');
    top.className = 'task-top';
    const name = document.createElement('div');
    name.className = 'task-name';
    name.textContent = task.name || '';
    name.title = task.name || '';
    top.appendChild(name);

    const canPause = task.state === TASK_STATE.DOWNLOADING || task.state === TASK_STATE.QUEUED;
    const canResume = task.state === TASK_STATE.PAUSED || task.state === TASK_STATE.ERROR;
    if (canPause || canResume) {
      const b = document.createElement('button');
      b.className = 'icon';
      b.textContent = t(canPause ? 'btnPause' : 'btnResume');
      b.addEventListener('click', async () => {
        b.disabled = true;
        try { await taskAction(task.id, canPause ? 'pause' : 'resume'); } catch { /* পরের রিফ্রেশে ঠিক হবে */ }
        refresh();
      });
      top.appendChild(b);
    }

    const bar = document.createElement('div');
    bar.className = 'bar';
    const fill = document.createElement('span');
    fill.style.width = `${Math.max(0, Math.min(100, task.percent || 0))}%`;
    bar.appendChild(fill);

    const meta = document.createElement('div');
    meta.className = 'task-meta';
    const left = document.createElement('span');
    left.textContent = `${t(STATE_KEY[task.state] || 'stateQueued')}${task.percent ? ` • ${task.percent.toFixed(0)}%` : ''}`;
    const right = document.createElement('span');
    right.textContent = task.state === TASK_STATE.DOWNLOADING && task.speed
      ? `${fmtBytes(task.speed)}/s${task.eta ? ` • ${task.eta}` : ''}`
      : fmtBytes(task.size);
    meta.append(left, right);

    li.append(top, bar, meta);
    el.taskList.appendChild(li);
  }
}

function showProblem(textKey, { connect = false, open = false } = {}) {
  el.panelTasks.classList.add('hidden');
  el.panelProblem.classList.remove('hidden');
  el.problemText.textContent = t(textKey);
  el.btnConnect.classList.toggle('hidden', !connect);
  el.btnOpenApp.classList.toggle('hidden', !open);
}

async function refresh() {
  if (pairing) return;
  const status = await getStatus();

  if (status.state === CONN.CONNECTED) {
    setPill('ok', t('statusConnected'));
    el.panelProblem.classList.add('hidden');
    el.panelTasks.classList.remove('hidden');
    try {
      renderTasks(await listTasks());
    } catch (e) {
      if (e instanceof BridgeError && e.code === 'not_paired') return refresh();
    }
    return schedule(1000);
  }

  if (status.state === CONN.NOT_PAIRED) {
    setPill('warn', t('statusNotPaired'));
    showProblem('notPairedHint', { connect: true });
    return schedule(2000);
  }

  setPill('bad', t('statusNotRunning'));
  showProblem('notRunningHint', { open: true });
  return schedule(2000);
}

function schedule(ms) {
  clearTimeout(timer);
  timer = setTimeout(refresh, ms);
}

el.btnConnect.addEventListener('click', async () => {
  pairing = true;
  el.btnConnect.disabled = true;
  el.problemError.classList.add('hidden');
  el.problemText.textContent = t('waitingApproval');
  try {
    await pair();
    pairing = false;
    el.btnConnect.disabled = false;
    refresh();
  } catch (e) {
    pairing = false;
    el.btnConnect.disabled = false;
    const map = { pairing_denied: 'errPairingDenied', pairing_timeout: 'errPairingTimeout', pairing_busy: 'errPairingBusy' };
    el.problemError.textContent = t(map[e && e.code] || 'errGeneric');
    el.problemError.classList.remove('hidden');
    el.problemText.textContent = t('notPairedHint');
  }
});

// অ্যাপ চালু করার কাজ service worker করে (পপআপ বন্ধ হলেও যাতে ট্যাব ঠিকমতো বন্ধ হয়)
const open = async () => {
  try { await chrome.runtime.sendMessage({ type: 'launch' }); } catch { /* ignore */ }
  window.close();
};
el.btnOpenApp.addEventListener('click', open);
el.btnOpen.addEventListener('click', open);
el.btnOptions.addEventListener('click', () => chrome.runtime.openOptionsPage());

applyI18n();
refresh();
