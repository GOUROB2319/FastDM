// অ্যাপের Bridge প্রোটোকল v1 (FastDM/Bridge/BridgeServer.cs-এর সাথে মেলে)
export const PROTOCOL_VERSION = 1;
export const PORTS = [17432, 17433, 17434, 17435, 17436];
export const HEADER_TOKEN = 'X-FastDM-Token';

// সংযোগের অবস্থা
export const CONN = Object.freeze({
  CHECKING: 'checking',
  NOT_RUNNING: 'not_running',
  NOT_PAIRED: 'not_paired',
  CONNECTED: 'connected',
});

// অ্যাপের ডাউনলোড-অবস্থার নাম (ডেস্কটপের সাথে একই)
export const TASK_STATE = Object.freeze({
  QUEUED: 'Queued',
  DOWNLOADING: 'Downloading',
  PAUSED: 'Paused',
  COMPLETE: 'Complete',
  ERROR: 'Error',
});
