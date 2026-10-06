import { createMenus, targetUrl, modeFor, refererFor } from './menus.js';
import { sendLink, launchApp } from './bridge.js';
import { cookiesFor } from './cookies.js';

// মেনু: ইনস্টল ও ব্রাউজার চালুর সময় আবার তৈরি (removeAll দিয়ে, তাই ডুপ্লিকেট হয় না)
chrome.runtime.onInstalled.addListener(() => createMenus());
chrome.runtime.onStartup.addListener(() => createMenus());

function flashBadge(text, color) {
  chrome.action.setBadgeBackgroundColor({ color });
  chrome.action.setBadgeText({ text });
  setTimeout(() => chrome.action.setBadgeText({ text: '' }), 2500);
}

chrome.contextMenus.onClicked.addListener(async (info, tab) => {
  const mode = modeFor(info.menuItemId);
  if (!mode) return;                          // parent মেনু বা অজানা আইটেম

  const url = targetUrl(info, tab);
  if (!url || !/^(https?|ftp|sftp):/i.test(url)) {
    flashBadge('!', '#FF5C77');
    return;
  }

  const meta = {
    mode,
    title: tab && tab.title,
    referer: refererFor(info),
    userAgent: navigator.userAgent,
    cookies: await cookiesFor(url),           // permission না থাকলে []
  };
  if (!meta.cookies.length) delete meta.cookies;

  const r = await sendLink(url, meta);
  if (r.via) {
    flashBadge('✓', '#2ECC71');
  } else if (r.needsPairing) {
    flashBadge('!', '#F5A623');
    chrome.runtime.openOptionsPage();        // সেখানে "Connect" বাটন আছে
  } else {
    flashBadge('!', '#FF5C77');
  }
});

// পপআপ থেকে "Open FastDM" অনুরোধ
chrome.runtime.onMessage.addListener((msg, _sender, sendResponse) => {
  if (msg && msg.type === 'launch') {
    launchApp(msg.url)
      .then(() => sendResponse({ ok: true }))
      .catch((e) => sendResponse({ ok: false, error: String(e && e.message) }));
    return true;      // অ্যাসিঙ্ক উত্তর
  }
  return false;
});
