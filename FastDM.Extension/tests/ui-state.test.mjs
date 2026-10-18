import test from 'node:test';
import assert from 'node:assert/strict';
import { popupView, optionsView } from '../src/shared/ui-state.js';

test('popup loading, paired, unpaired and disconnected states expose the right actions', () => {
  assert.deepEqual(popupView('checking'), {
    panel: 'loading', connect: false, problemOpen: false, footerOpen: false,
  });
  assert.deepEqual(popupView('connected'), {
    panel: 'tasks', connect: false, problemOpen: false, footerOpen: true,
  });
  assert.deepEqual(popupView('not_paired'), {
    panel: 'problem', connect: true, problemOpen: false, footerOpen: true,
  });
  const disconnected = popupView('not_running');
  assert.equal(disconnected.problemOpen, true);
  assert.equal(disconnected.footerOpen, false);
  assert.equal(Number(disconnected.problemOpen) + Number(disconnected.footerOpen), 1,
    'disconnected view has exactly one Open FastDM action');
});

test('options expose only actions supported by each connection state', () => {
  assert.deepEqual(optionsView('checking'), { connect: false, forget: false, connectEnabled: false });
  assert.deepEqual(optionsView('connected'), { connect: false, forget: true, connectEnabled: false });
  assert.deepEqual(optionsView('not_paired'), { connect: true, forget: false, connectEnabled: true });
  assert.deepEqual(optionsView('not_running'), { connect: true, forget: false, connectEnabled: false });
  assert.deepEqual(optionsView('error'), { connect: true, forget: false, connectEnabled: true });
});
