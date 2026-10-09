# FastDM Browser Extension (Phase 1 — Foundation)

Chrome ও Edge (Chromium, Manifest V3) এক্সটেনশন, যা লিঙ্ক FastDM ডেস্কটপ অ্যাপে পাঠায়।

## যোগাযোগ কীভাবে হয়
- **মূল পথ:** অ্যাপের ভেতরের লোকাল সার্ভার `http://127.0.0.1:17432–17436` (শুধু লুপব্যাক)। প্রথমবার "Connect" চাপলে FastDM অ্যাপে অনুমোদনের ডায়ালগ আসে, অনুমোদনে টোকেন তৈরি হয়। এরপর প্রতি রিকোয়েস্টে `X-FastDM-Token`।
- **ফলব্যাক:** অ্যাপ বন্ধ থাকলে `fastdm://add?url=...` দিয়ে অ্যাপ চালু হয় ও Add ডায়ালগ খোলে (ডাউনলোড নিজে থেকে শুরু হয় না, ইউজার নিশ্চিত করে)।

## ডেভেলপমেন্টে চালানো (Edge ও Chrome)
1. FastDM অ্যাপ চালু করো (Settings → "Allow the browser extension to connect" চালু থাকতে হবে)।
2. **Edge:** `edge://extensions` → Developer mode চালু → **Load unpacked** → এই `FastDM.Extension` ফোল্ডার বাছো।
   **Chrome:** `chrome://extensions` → Developer mode → **Load unpacked** → একই ফোল্ডার।
3. টুলবারে FastDM আইকন → **Connect** → FastDM অ্যাপে "Yes"।

## Phase 1 টেস্ট চেকলিস্ট
- [ ] এক্সটেনশন Edge-এ লোড হয়, আইকন দেখা যায়
- [ ] এক্সটেনশন Chrome-এ লোড হয়
- [ ] অ্যাপ বন্ধ: পপআপে "FastDM is not running" + Open FastDM বাটন
- [ ] অ্যাপ চালু, পেয়ার ছাড়া: "Not connected yet" + Connect বাটন
- [ ] Connect → অ্যাপে অনুমোদন ডায়ালগ → Yes → পপআপে "Connected"
- [ ] No চাপলে পপআপে "The connection was denied in FastDM."
- [ ] কোনো লিঙ্কে রাইট-ক্লিক → "Download with FastDM" → অ্যাপে Add ডায়ালগ, আইকনে ✓ ব্যাজ
- [ ] ভিডিও/অডিও এলিমেন্টে রাইট-ক্লিক → "Download media with FastDM"
- [ ] পেজে রাইট-ক্লিক → "Send page link to FastDM"
- [ ] অ্যাপ বন্ধ অবস্থায় লিঙ্কে রাইট-ক্লিক → অ্যাপ চালু হয় (fastdm://), Add ডায়ালগ আসে
- [ ] অ্যাপে "Forget paired browsers" চাপলে পরের লিঙ্ক পাঠানোয় Settings পেজ খোলে ও আবার Connect দরকার হয়
- [ ] ভাষা বাংলা করে (ব্রাউজারের ভাষা) পপআপের লেখা বাংলায় আসে
- [ ] ডার্ক/লাইট থিম অনুসরণ করে

## নিরাপত্তা নিয়ম (অ্যাপের দিক)
Host হেডার `127.0.0.1:<port>` হতে হবে, Origin অবশ্যই `chrome-extension://` (বা `moz-extension://`), টোকেন ছাড়া `/v1/ping` ও `/v1/pair` বাদে সব ৪০১, রিকোয়েস্ট সাইজ ও হার সীমিত।

## টেস্ট (Node 18+)
```
npm test
```
এটা নকল অ্যাপ-সার্ভারের বিরুদ্ধে `bridge.js` পরীক্ষা করে (ডিসকভারি, পেয়ারিং, লিঙ্ক পাঠানো, ফলব্যাক, টোকেন বাতিল)।

## স্টোরের জন্য প্যাকেজ
```
powershell -File tools/pack.ps1
```
`dist/FastDM-Extension-<version>.zip` তৈরি হয় (tests/tools বাদ)।

## মনে রাখার বিষয়
- Store বিল্ডে YouTube-নির্দিষ্ট কোনো UI নেই; সব সাধারণ "Send link"।
- Phase 1-এ permission: `contextMenus`, `storage`, আর `http://127.0.0.1/*`। কুকি/ডাউনলোড ধরা/ওয়েবরিকোয়েস্ট পরের ধাপে, ঐচ্ছিক permission হিসেবে।
