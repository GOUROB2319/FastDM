export const MENU = Object.freeze({
  LINK: 'fastdm-link',
  MEDIA: 'fastdm-media',
  PAGE: 'fastdm-page',
});

const t = (key) => chrome.i18n.getMessage(key) || key;

// Phase 1: তিনটা সাধারণ মেনু (লিঙ্ক, ভিডিও/অডিও, পেজ)। কোনো সাইট-নির্দিষ্ট মেনু নেই।
export function createMenus() {
  chrome.contextMenus.removeAll(() => {
    chrome.contextMenus.create({ id: MENU.LINK, title: t('menuDownloadLink'), contexts: ['link'] });
    chrome.contextMenus.create({ id: MENU.MEDIA, title: t('menuDownloadMedia'), contexts: ['video', 'audio'] });
    chrome.contextMenus.create({ id: MENU.PAGE, title: t('menuSendPage'), contexts: ['page'] });
  });
}

// ক্লিক থেকে পাঠানোর লিঙ্ক বের করা
export function targetUrl(info, tab) {
  const usable = (u) => u && !u.startsWith('blob:') && !u.startsWith('data:');
  if (info.menuItemId === MENU.LINK) return info.linkUrl;
  if (info.menuItemId === MENU.MEDIA) return usable(info.srcUrl) ? info.srcUrl : (info.pageUrl || (tab && tab.url));
  return info.pageUrl || (tab && tab.url);
}
