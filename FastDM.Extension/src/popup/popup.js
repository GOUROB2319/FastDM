import { getStatus, pair, listTasks, taskAction, BridgeError } from '../background/bridge.js';
import { CONN, TASK_STATE } from '../shared/protocol.js';
import { applyI18n, t, fmtBytes } from '../shared/i18n.js';
import { popupView } from '../shared/ui-state.js';

const $ = (id) => document.getElementById(id);
const el = {
  pill: $('pill'), pillText: $('pillText'), panelLoading: $('panelLoading'),
  panelProblem: $('panelProblem'), problemText: $('problemText'), problemError: $('problemError'),
  btnConnect: $('btnConnect'), btnOpenApp: $('btnOpenApp'),
  panelTasks: $('panelTasks'), taskList: $('taskList'), taskStatus: $('taskStatus'), empty: $('empty'),
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
  el.taskList.classList.remove('hidden');
  el.taskStatus.classList.add('hidden');
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
      b.setAttribute('aria-label', `${t(canPause ? 'btnPause' : 'btnResume')} ${task.name || ''}`.trim());
      const actionStatus = document.createElement('span');
      actionStatus.className = 'task-action-status';
      actionStatus.setAttribute('role', 'status');
      actionStatus.setAttribute('aria-live', 'polite');
      b.addEventListener('click', async () => {
        b.disabled = true;
        try {
          await taskAction(task.id, canPause ? 'pause' : 'resume');
          await refresh();
        } catch {
          actionStatus.textContent = t('errGeneric');
          b.disabled = false;
          schedule(1500);
        }
      });
      top.append(b, actionStatus);
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

function showProblem(textKey, state, errorKey = null) {
  const view = popupView(state);
  el.panelLoading.classList.toggle('hidden', view.panel !== 'loading');
  el.panelTasks.classList.toggle('hidden', view.panel !== 'tasks');
  el.panelProblem.classList.toggle('hidden', view.panel !== 'problem');
  el.problemText.textContent = t(textKey);
  el.problemError.textContent = errorKey ? t(errorKey) : '';
  el.problemError.classList.toggle('hidden', !errorKey);
  el.btnConnect.classList.toggle('hidden', !view.connect);
  el.btnOpenApp.classList.toggle('hidden', !view.problemOpen);
  el.btnOpen.classList.toggle('hidden', !view.footerOpen);
}

function showTaskStatus(messageKey) {
  el.taskList.classList.add('hidden');
  el.empty.classList.add('hidden');
  el.taskStatus.textContent = t(messageKey);
  el.taskStatus.classList.toggle('error', messageKey === 'errGeneric');
  el.taskStatus.classList.remove('hidden');
}

async function refresh() {
  if (pairing) return;

  let status;
  try {
    status = await getStatus();
  } catch {
    setPill('bad', t('statusError'));
    showProblem('statusError', 'error');
    return schedule(2000);
  }

  if (status.state === CONN.CONNECTED) {
    const view = popupView(status.state);
    setPill('ok', t('statusConnected'));
    el.panelLoading.classList.toggle('hidden', view.panel !== 'loading');
    el.panelProblem.classList.toggle('hidden', view.panel !== 'problem');
    el.panelTasks.classList.toggle('hidden', view.panel !== 'tasks');
    el.btnOpenApp.classList.toggle('hidden', !view.problemOpen);
    el.btnOpen.classList.toggle('hidden', !view.footerOpen);
    showTaskStatus('recentLoading');
    try {
      renderTasks(await listTasks());
    } catch (error) {
      if (error instanceof BridgeError && error.code === 'not_paired') return refresh();
      showTaskStatus('errGeneric');
    }
    return schedule(1000);
  }

  if (status.state === CONN.NOT_PAIRED) {
    setPill('warn', t('statusNotPaired'));
    showProblem('notPairedHint', status.state);
    return schedule(2000);
  }

  if (status.state === CONN.NOT_RUNNING) {
    setPill('bad', t('statusNotRunning'));
    showProblem('notRunningHint', status.state);
    return schedule(2000);
  }

  setPill('muted', t('statusChecking'));
  showProblem('statusChecking', status.state);
  schedule(2000);
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
  } catch (error) {
    pairing = false;
    el.btnConnect.disabled = false;
    const map = { pairing_denied: 'errPairingDenied', pairing_timeout: 'errPairingTimeout', pairing_busy: 'errPairingBusy' };
    el.problemError.textContent = t(map[error && error.code] || 'errGeneric');
    el.problemError.classList.remove('hidden');
    el.problemText.textContent = t('notPairedHint');
  }
});

const open = async () => {
  try { await chrome.runtime.sendMessage({ type: 'launch' }); } catch { /* ignored */ }
  window.close();
};
el.btnOpenApp.addEventListener('click', open);
el.btnOpen.addEventListener('click', open);
el.btnOptions.addEventListener('click', () => chrome.runtime.openOptionsPage());

applyI18n();
el.panelLoading.classList.remove('hidden');
el.panelProblem.classList.add('hidden');
el.panelTasks.classList.add('hidden');
refresh();
