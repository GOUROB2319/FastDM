// Phase 2: একটাই "FastDM" parent মেনু, নিচে সাবমেনু।
//   লিঙ্ক/ভিডিও/অডিওতে: "Download with FastDM" (সরাসরি শুরু) + "Open in FastDM" (Add ডায়ালগ)
//   পেজে: "Send page link to FastDM" (Add ডায়ালগ, কারণ পেজ ভিডিও/ফোল্ডার হতে পারে, ইউজার ঠিক করবে)
// কোনো সাইট-নির্দিষ্ট মেনু নেই (স্টোর নীতির কারণে)।
export const MENU = Object.freeze({
  PARENT: 'fastdm-parent',
  DL_LINK: 'fastdm-dl-link',
  OPEN_LINK: 'fastdm-open-link',
  DL_MEDIA: 'fastdm-dl-media',
  OPEN_MEDIA: 'fastdm-open-media',
  OPEN_PAGE: 'fastdm-open-page',
});

// bridge-এর mode: download = ডায়ালগ ছাড়া শুরু, open = Add ডায়ালগ
export const MODE = Object.freeze({ DOWNLOAD: 'download', OPEN: 'open' });

const MODE_BY_MENU = Object.freeze({
  [MENU.DL_LINK]: MODE.DOWNLOAD,
  [MENU.DL_MEDIA]: MODE.DOWNLOAD,
  [MENU.OPEN_LINK]: MODE.OPEN,
  [MENU.OPEN_MEDIA]: MODE.OPEN,
  [MENU.OPEN_PAGE]: MODE.OPEN,
});

const t = (key) => chrome.i18n.getMessage(key) || key;

export function createMenus() {
  chrome.contextMenus.removeAll(() => {
    const parent = MENU.PARENT;
    chrome.contextMenus.create({ id: parent, title: 'FastDM', contexts: ['link', 'video', 'audio', 'page'] });
    chrome.contextMenus.create({ id: MENU.DL_LINK, parentId: parent, title: t('menuDownloadLink'), contexts: ['link'] });
    chrome.contextMenus.create({ id: MENU.OPEN_LINK, parentId: parent, title: t('menuOpenLink'), contexts: ['link'] });
    chrome.contextMenus.create({ id: MENU.DL_MEDIA, parentId: parent, title: t('menuDownloadMedia'), contexts: ['video', 'audio'] });
    chrome.contextMenus.create({ id: MENU.OPEN_MEDIA, parentId: parent, title: t('menuOpenMedia'), contexts: ['video', 'audio'] });
    chrome.contextMenus.create({ id: MENU.OPEN_PAGE, parentId: parent, title: t('menuSendPage'), contexts: ['page'] });
  });
}

export function modeFor(menuItemId) {
  return MODE_BY_MENU[menuItemId] || null;     // অজানা আইটেম (যেমন parent) → null
}

// ক্লিক থেকে পাঠানোর লিঙ্ক বের করা
export function targetUrl(info, tab) {
  const usable = (u) => u && !u.startsWith('blob:') && !u.startsWith('data:');
  if (info.menuItemId === MENU.DL_LINK || info.menuItemId === MENU.OPEN_LINK) return info.linkUrl;
  if (info.menuItemId === MENU.DL_MEDIA || info.menuItemId === MENU.OPEN_MEDIA) {
    return usable(info.srcUrl) ? info.srcUrl : (info.pageUrl || (tab && tab.url));
  }
  return info.pageUrl || (tab && tab.url);
}

// Referer: লিঙ্ক/মিডিয়া যে পেজ বা iframe-এ ছিল। পেজের লিঙ্ক নিজেই পাঠালে Referer নেই।
export function refererFor(info) {
  if (info.menuItemId === MENU.OPEN_PAGE) return undefined;
  return info.frameUrl || info.pageUrl || undefined;
}
