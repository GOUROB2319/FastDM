export function popupView(state) {
  if (state === 'connected') return { panel: 'tasks', connect: false, problemOpen: false, footerOpen: true };
  if (state === 'not_paired') return { panel: 'problem', connect: true, problemOpen: false, footerOpen: true };
  if (state === 'not_running') return { panel: 'problem', connect: false, problemOpen: true, footerOpen: false };
  if (state === 'error') return { panel: 'problem', connect: false, problemOpen: true, footerOpen: false };
  return { panel: 'loading', connect: false, problemOpen: false, footerOpen: false };
}

export function optionsView(state) {
  if (state === 'connected') return { connect: false, forget: true, connectEnabled: false };
  if (state === 'not_paired') return { connect: true, forget: false, connectEnabled: true };
  if (state === 'not_running') return { connect: true, forget: false, connectEnabled: false };
  if (state === 'error') return { connect: true, forget: false, connectEnabled: true };
  return { connect: false, forget: false, connectEnabled: false };
}
