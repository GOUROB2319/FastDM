# FastDM Browser Extension — আর্কিটেকচার ও Feasibility প্ল্যান

> এই ডকুমেন্টে যেখানে "যাচাই দরকার" লেখা আছে, সেখানে আমি নিশ্চিত না। কোড লেখার আগে ছোট টেস্ট (spike) দিয়ে নিশ্চিত হতে হবে।

---

## ১. বর্তমান কোডবেসের কোন অংশ reuse হবে, কোনটা নতুন

### Reuse (বদলানো লাগবে না বা সামান্য)
| বর্তমান অংশ | এক্সটেনশনে কীভাবে কাজে লাগবে |
|---|---|
| `ShowAddDialog` এর লিঙ্ক শ্রেণিবিভাগ (ফোল্ডার / yt-dlp সাইট / HLS-DASH / ফাইল) | এক্সটেনশন থেকে আসা URL-ও একই পাইপলাইনে যাবে। শুধু UI-থেকে-আলাদা একটা মেথড (`ProcessUrlsAsync`) বের করতে হবে |
| `RemoteLister` (HTTP/FTP/SFTP ফোল্ডার স্ক্যান) + `FolderDownloadForm` | "Download Folder with FastDM" আর প্লেলিস্ট জেনারেশনের ভিত্তি |
| `MediaDetector`, `StreamEngine`, `YtDlpEngine` | ভিডিও/স্ট্রিম ধরার পুরো ডাউনলোড অংশ |
| `SecretStore`, `CredStore` | পেয়ারিং টোকেন আর পরে ক্লাউড টোকেন সংরক্ষণ |
| `SpeedLimiter`, `ProxyConfig`, `Scheduler`, কিউ | এক্সটেনশন থেকে যোগ হওয়া ডাউনলোডেও স্বয়ংক্রিয়ভাবে প্রযোজ্য |
| Single-instance ইভেন্ট (`Program.cs` + `SetupSingleInstanceListener`) | অ্যাপ চালু থাকলে `fastdm://` ডাকে সামনে আনা, ডেটা পাঠানো |
| `Theme.P` প্যালেট (রঙ), টার্মিনোলজি (Queued, Downloading, Paused, Complete, Error) | এক্সটেনশন UI-র design token হিসেবে কপি হবে (একই পরিবার) |

### নতুন তৈরি করতে হবে
| নতুন অংশ | কারণ |
|---|---|
| **Bridge server** (অ্যাপের ভেতরে, `127.0.0.1`-এ) | এক্সটেনশন ↔ অ্যাপ যোগাযোগ |
| **Pairing/Auth** (টোকেন, অ্যাপে অনুমোদন ডায়ালগ) | অন্য কোনো প্রোগ্রাম বা ওয়েবপেজ যাতে অ্যাপে ডাউনলোড ঢোকাতে না পারে |
| **`fastdm://` protocol handler** (MSIX `uap:Protocol`) | অ্যাপ বন্ধ থাকলে চালু করা |
| **Per-item headers** (`Referer`, `Cookie`, `User-Agent`) | `DownloadItem`-এ নতুন ফিল্ড, `Engine`-এর রিকোয়েস্টে প্রয়োগ, yt-dlp-তে `--cookies` ফাইল |
| **M3U/M3U8 প্লেলিস্ট জেনারেটর** | ফোল্ডার স্ক্যানের ট্রি থেকে ফাইল |
| **Cloud provider abstraction** (পরের ধাপ) | Google Drive / OneDrive |
| **এক্সটেনশন প্রজেক্ট** (JS/HTML/CSS) | নতুন |

---

## ২. অ্যাপ ↔ এক্সটেনশন যোগাযোগ: তিনটা পথের তুলনা

| পথ | সুবিধা | অসুবিধা / ঝুঁকি |
|---|---|---|
| **Native Messaging** | ব্রাউজারের নিজস্ব নিরাপদ চ্যানেল, `allowed_origins` দিয়ে এক্সটেনশন সীমাবদ্ধ, কোনো খোলা পোর্ট নেই | ব্রাউজার `HKCU\Software\{Google\Chrome \| Microsoft\Edge}\NativeMessagingHosts\<নাম>`-এ রেজিস্ট্রি এন্ট্রি দিয়ে ম্যানিফেস্ট ফাইল খোঁজে, আর হোস্ট exe ব্রাউজার ইনস্টল করে না (Microsoft Edge ডকুমেন্টেশন অনুযায়ী)। **Store (MSIX) অ্যাপ থেকে এই রেজিস্ট্রি ঠিকমতো লেখা যায় কি না: যাচাই দরকার** (একটা পাবলিক GitHub ইস্যুতে দেখলাম একটা Store-ইনস্টল অ্যাপের এন্ট্রি বসানো আছে, তাই সম্ভব মনে হয়, কিন্তু আমাদের বিল্ডে টেস্ট না করে নিশ্চিত বলা যাবে না)। আলাদা হোস্ট exe-ও লাগবে |
| **Custom protocol `fastdm://`** | MSIX-এ আনুষ্ঠানিক সাপোর্ট (`uap:Protocol`), অ্যাপ বন্ধ থাকলেও চালু করে | ডেটার পরিমাণ সীমিত (URL-এ), ব্রাউজার প্রথমবার "Open FastDM?" জিজ্ঞেস করে, কোনো রিটার্ন ডেটা নেই (স্ট্যাটাস দেখানো যায় না) |
| **Localhost bridge** (`http://127.0.0.1:<port>`) | অ্যাপের ভেতরেই `HttpListener`, দুই দিকে ডেটা (প্রগ্রেস, স্ট্যাটাস), Store প্যাকেজে সহজ | খোলা পোর্ট মানে নিরাপত্তা দায়িত্ব: টোকেন + Origin চেক + শুধু লুপব্যাক বাঁধা লাগবে। ব্রাউজারের network নীতি (Private Network Access) এক্সটেনশনের জন্য কেমন: **প্রোটোটাইপে যাচাই দরকার** |

### সিদ্ধান্ত (Phase 1)
**Localhost bridge + পেয়ারিং টোকেন (মূল পথ) + `fastdm://` (অ্যাপ বন্ধ থাকলে চালু করার ফলব্যাক)।**

কারণ: Store প্যাকেজে সবচেয়ে সহজে কাজ করে, প্রগ্রেস/স্ট্যাটাস পপআপে দেখানো যায়, আর আলাদা হোস্ট exe বা রেজিস্ট্রি যাচাইয়ের ওপর শুরুতেই নির্ভর করতে হয় না। **Native Messaging পরে ঐচ্ছিক আপগ্রেড হিসেবে যোগ হবে**, রেজিস্ট্রি টেস্ট পাস করলে।

### নিরাপত্তা মডেল (bridge)
1. শুধু `127.0.0.1`-এ listen, `Host` হেডার চেক (DNS rebinding ঠেকাতে)।
2. **পেয়ারিং:** এক্সটেনশন "Connect" চাপলে অ্যাপে একটা অনুমোদন ডায়ালগ ("এই ব্রাউজার এক্সটেনশনকে কি অনুমতি দেবে?") আসে। অনুমোদন দিলে র‍্যান্ডম টোকেন তৈরি হয়ে এক্সটেনশনের `storage`-এ আর অ্যাপে (Credential Manager-এ হ্যাশ) থাকে।
3. প্রতিটা রিকোয়েস্টে কাস্টম হেডার `X-FastDM-Token` (এটা থাকলে সাধারণ ওয়েবপেজ থেকে cross-origin রিকোয়েস্ট প্রিফ্লাইটে আটকে যায়)।
4. `Origin` হেডার `chrome-extension://` বা `moz-extension://` দিয়ে শুরু না হলে বাতিল (ওয়েবপেজ Origin জাল করতে পারে না)।
5. রেট লিমিট, রিকোয়েস্ট সাইজ লিমিট, শুধু নির্দিষ্ট ধরনের URL (http/https/ftp/sftp)।
6. কুকি শুধু মেমোরিতে, ডিস্কে সেভ নয়; yt-dlp-র জন্য অস্থায়ী কুকি ফাইল কাজ শেষে মুছে ফেলা।

---

## ৩. বার্তা প্রোটোকল v1 (খসড়া)

```
GET  /v1/ping                      → { app: "FastDM", version, protocol: 1, paired: bool }
POST /v1/pair                      → (অ্যাপে অনুমোদন ডায়ালগ) { token }
POST /v1/add        {token}        → { id, status }
     body: { url, kind: "auto|file|folder|media|page|playlist",
             title?, referer?, userAgent?, cookies?: [{name,value,domain,path,secure}], 
             options?: { askFolder: bool } }
GET  /v1/tasks      {token}        → [{ id, name, state, percent, speed, eta }]   (পপআপ প্রগ্রেসের জন্য)
POST /v1/tasks/{id}/pause|resume|remove {token}
```
- `kind: "auto"` মানে অ্যাপ নিজেই লিঙ্কের ধরন চিনে নেবে (বর্তমান `ShowAddDialog` পাইপলাইন)।
- রিকোয়েস্টে `version` ফিল্ড, যাতে পরে প্রোটোকল বদলালে পুরোনো/নতুন এক্সটেনশন-অ্যাপ জুটি ভাঙে না।

---

## ৪. ফোল্ডার আর্কিটেকচার (Phase 1)

### সলিউশন রুটে (FastDM ও FastDM.Package-এর পাশে)
```
FastDM.Extension/
  manifest.json                 # MV3, ন্যূনতম permission
  _locales/
    en/messages.json
    bn/messages.json
  src/
    background/
      service-worker.js         # প্রবেশ বিন্দু, ইভেন্ট রাউটার
      bridge.js                 # লোকালহোস্ট ক্লায়েন্ট: ping/pair/add/tasks, ফেইল হলে fastdm:// ফলব্যাক
      menus.js                  # কনটেক্সট মেনু (কনটেন্ট অনুযায়ী গতিশীল)
      state.js                  # সংযোগ অবস্থা, টোকেন (chrome.storage.local)
    popup/
      popup.html  popup.css  popup.js
    options/
      options.html  options.js
    shared/
      protocol.js               # বার্তার ধরন/ধ্রুবক
      tokens.css                # রঙ/স্পেসিং (Theme.P-র সাথে মেলানো)
      icons.svg                 # Fluent-স্টাইলের SVG আইকন
  assets/icons/                 # 16/32/48/128
  tools/pack.ps1                # স্টোরের জন্য zip
  README.md
```

### অ্যাপের ভেতরে (FastDM প্রজেক্টে নতুন ফোল্ডার)
```
FastDM/
  Bridge/
    BridgeServer.cs             # HttpListener, রাউটিং, লিমিট
    BridgeAuth.cs               # পেয়ারিং, টোকেন, Origin/Host চেক
    BridgeModels.cs             # AddRequest, TaskInfo ইত্যাদি
    ProtocolHandler.cs          # fastdm:// আর্গুমেন্ট পার্সিং
  (Form1.cs-এ ছোট বদল: ProcessUrlsAsync বের করা, HandleExternalAdd)
  (Package.appxmanifest-এ uap:Protocol যোগ)
```
(পরের ধাপে `Cloud/ICloudProvider.cs`, `Cloud/GoogleDriveProvider.cs` ইত্যাদি, আর `Playlist/M3uBuilder.cs`)

---

## ৫. Feasibility ম্যাট্রিক্স (ফিচার ধরে)

| ফিচার | শুধু Extension? | Extension + Desktop? | Native Messaging? | Cloud API/OAuth? | ব্রাউজার সীমাবদ্ধতা | Store নীতির ঝুঁকি |
|---|---|---|---|---|---|---|
| Link/Media-তে right-click "Download with FastDM" | না | **হ্যাঁ** | না (bridge যথেষ্ট) | না | `contextMenus` permission যথেষ্ট | কম |
| ওয়েবপেজ (যেমন YouTube) থেকে পাঠানো | না | **হ্যাঁ** (এক্সটেনশন শুধু পেজ URL পাঠায়, ফরম্যাট তালিকা অ্যাপের yt-dlp বের করে) | না | না | এক্সটেনশন সাইটের কোড স্ক্র্যাপ করবে না | **উচ্চ, যাচাই দরকার:** Chrome Web Store ও Edge Add-ons-এ YouTube/সুরক্ষিত কনটেন্ট ডাউনলোডের এক্সটেনশন নীতি-লঙ্ঘন ধরা হতে পারে। জমা দেওয়ার আগে উভয় স্টোরের বর্তমান নীতি পড়ে নিতে হবে। সম্ভব হলে স্টোর বিল্ডে YouTube-নির্দিষ্ট UI বাদ দিয়ে সাধারণ "Send link" রাখা নিরাপদ |
| HTTP directory listing পেজে "Download Folder" | না | **হ্যাঁ** | না | না | কন্টেন্ট স্ক্রিপ্ট দিয়ে "Index of /" / h5ai ধরনের পেজ চেনা সম্ভব | কম |
| **FTP** ফোল্ডারে right-click | না | হ্যাঁ, কিন্তু সীমিত | না | না | আমার জানামতে Chrome/Edge এখন `ftp://` ব্রাউজিং সাপোর্ট করে না (যাচাই দরকার), তাই ব্রাউজারে FTP লিস্টিং পেজই নেই। শুধু পেজের ভেতরে থাকা `ftp://` **লিঙ্কে** right-click করে পাঠানো যায়, ফোল্ডার স্ক্যান অ্যাপ করবে। Windows Explorer-এ right-click সম্পূর্ণ আলাদা (shell integration), এক্সটেনশনের সীমার বাইরে | কম |
| প্লেলিস্ট জেনারেশন (.m3u/.m3u8) | না | **অ্যাপের ফিচার** (এক্সটেনশন শুধু ট্রিগার) | না | না | — | কম |
| ব্রাউজারের নিজের ডাউনলোড ধরা (IDM-এর মতো) | না | হ্যাঁ | না | না | `downloads` permission লাগবে (স্টোর রিভিউয়ে ব্যাখ্যা দিতে হবে) | মাঝারি |
| ভিডিও লিঙ্ক ধরা (`.m3u8/.mpd/.mp4`) + কুকি/Referer | না | হ্যাঁ | না | না | `webRequest` (পর্যবেক্ষণ) + `cookies` + হোস্ট permission। **অনেক বেশি permission**, রিভিউ কঠিন। `optional_host_permissions` দিয়ে ব্যবহারকারীর অনুমতি নিয়ে চাওয়া ভালো | মাঝারি-উচ্চ |
| **Google Drive** বড় ফাইল/ফোল্ডার | না | হ্যাঁ | না | **হ্যাঁ** | ব্রাউজার কুকি দিয়ে আনঅফিশিয়ালি ডাউনলোড (ZIP স্ক্র্যাপ, confirm টোকেন) করা যাবে না: নির্ভরযোগ্য না ও সাপোর্টেড না | মাঝারি |
| OneDrive ও অন্যান্য | না | হ্যাঁ | না | হ্যাঁ (Microsoft Graph ইত্যাদি) | — | মাঝারি |

### Google Drive: আলাদা spike দরকার (কোড লেখার আগে)
আমার বর্তমান বোঝাপড়া (Google-এর ডকুমেন্টেশনে মিলিয়ে নিতে হবে):
- Drive-এর "ফোল্ডার ZIP করে নামাও" ওয়েব ফিচার API-তে নেই। API দিয়ে করতে হয়: ফোল্ডারের সন্তান তালিকা (`files.list`) + প্রতি ফাইলের `files.get?alt=media`, যা `Range` সাপোর্ট করে (pause/resume সম্ভব)। এতে ZIP এড়িয়ে **প্রতি ফাইল আলাদা ম্যানেজ** করা যায়, ঠিক যেমন তুমি চেয়েছ।
- প্রাইভেট ফাইলে OAuth লাগবে (ডেস্কটপ অ্যাপ: লুপব্যাক রিডাইরেক্ট + PKCE)। `drive.readonly` স্কোপ "restricted" ধরনের, তাই পাবলিক রিলিজে Google-এর ভেরিফিকেশন (আর সম্ভবত সিকিউরিটি অ্যাসেসমেন্ট) লাগতে পারে। না হলে "unverified app" সতর্কতা আর ব্যবহারকারী সংখ্যায় সীমা।
- "Anyone with the link" পাবলিক ফাইল/ফোল্ডারে API key দিয়েই সম্ভব হতে পারে (OAuth ছাড়া), তবে কোটা সীমা আছে ("too many downloads" ধরনের)।
- তাই প্রথম ধাপ হতে পারে: **পাবলিক লিঙ্ক + API key**, দ্বিতীয় ধাপ: **OAuth দিয়ে নিজের প্রাইভেট ফাইল**।

ক্লাউড আর্কিটেকচার (পরের ধাপের জন্য এখন থেকে মাথায় রেখে):
```
ICloudProvider { CanHandle(Uri); ListAsync(...) → ট্রি; ResolveAsync(file) → (url, header factory, size); Authenticate... }
Provider → DownloadItem (url + "request signer") → বর্তমান Engine (Range, resume, retry)
```
`Engine.ApplyAuth` এর জায়গায় প্রতি-আইটেম "request signer" দিয়ে টোকেন রিফ্রেশ সামলানো হবে। মূল ইঞ্জিন প্রোভাইডার সম্পর্কে কিছু জানবে না।

---

## ৬. এক্সটেনশনের permission (ন্যূনতম, ধাপ ধরে)

| ধাপ | Permission | কেন |
|---|---|---|
| Phase 1 | `contextMenus`, `storage`, `host_permissions: http://127.0.0.1/*` | মেনু, টোকেন সংরক্ষণ, অ্যাপের সাথে কথা |
| Phase 2 | `activeTab` (বর্তমান পেজ URL/শিরোনাম) | "Download with FastDM" পেজে |
| Phase 2+ (ঐচ্ছিক) | `optional_permissions: cookies`, `optional_host_permissions: <all_urls>` | শুধু ইউজার "লগইন করা সাইটে কাজ করুক" চালু করলে চাওয়া হবে |
| Phase 3+ | কন্টেন্ট স্ক্রিপ্ট (`scripting`) | ডিরেক্টরি লিস্টিং পেজ চেনা |
| পরে | `downloads`, `webRequest` | ব্রাউজার ডাউনলোড ধরা, মিডিয়া স্নিফিং |

MV3 সার্ভিস ওয়ার্কার যেকোনো সময় বন্ধ হয়ে যায়, তাই কোনো স্থায়ী কানেকশন রাখব না: প্রতি কাজে ছোট `fetch`, আর পপআপ খোলা থাকলে পপআপই ১ সেকেন্ড পর পর `/v1/tasks` পোল করবে।

---

## ৭. Phase 1 এর কাজ ও সফলতার মাপকাঠি

**Phase 1 — Extension Foundation:**
1. অ্যাপে `Bridge/` (ping, pair, add, tasks), `ProcessUrlsAsync` রিফ্যাক্টর, `fastdm://` রেজিস্ট্রেশন
2. এক্সটেনশন: manifest, service worker, bridge ক্লায়েন্ট, পপআপ (সংযোগ অবস্থা, Connect বাটন, সাম্প্রতিক টাস্ক), অপশন পেজ, লোকালাইজেশন (bn/en)
3. ডিজাইন টোকেন, ডার্ক/লাইট সিস্টেম থিম অনুসরণ

**পাস হওয়ার শর্ত:**
- আনপ্যাকড এক্সটেনশন Edge ও Chrome-এ লোড হয়
- "Connect" চাপলে অ্যাপে অনুমোদন ডায়ালগ আসে, অনুমোদনে পপআপে "Connected" দেখায়
- অ্যাপ বন্ধ থাকলে পপআপ "FastDM is not running" দেখায় আর "Open FastDM" বাটনে `fastdm://` দিয়ে অ্যাপ চালু হয়
- একটা সাধারণ লিঙ্কে right-click → "Download with FastDM" → অ্যাপে ডাউনলোড যোগ হয়
- অপরিচিত Origin বা টোকেন ছাড়া রিকোয়েস্ট ফেরত যায় (আমি নিজে নেগেটিভ টেস্ট করে দেখাব)

---

## ৮. আগে যাচাই করতে হবে (spikes), কোড লেখার আগে

1. **Localhost fetch:** MV3 service worker থেকে `http://127.0.0.1:<port>`-এ fetch Edge ও Chrome-এ কাজ করে কি না (প্রোটোটাইপ)
2. **MSIX protocol:** `fastdm://` Store প্যাকেজ বিল্ডে ব্রাউজার থেকে অ্যাপ চালু করে কি না
3. **MSIX + Native Messaging রেজিস্ট্রি** (শুধু ঐচ্ছিক আপগ্রেডের জন্য)
4. **Store নীতি:** Chrome Web Store ও Edge Add-ons-এর বর্তমান নীতি (ডাউনলোডার এক্সটেনশন, YouTube, broad host permission)
5. **Google Drive:** OAuth স্কোপ ও ভেরিফিকেশন, API key কোটা (Phase 4-এর আগে)

---

## ৯. UX পরামর্শ (বিদ্যমান ডিজাইন বজায় রেখে)

- **পপআপ:** উপরে সংযোগ অবস্থা (Connected / Not running / Not paired), মাঝে সাম্প্রতিক ডাউনলোড (নাম, প্রগ্রেস বার, Pause/Resume), নিচে "Open FastDM"। অ্যাপের একই শব্দ: Queued, Downloading, Paused, Complete, Error
- **Empty state:** "No downloads yet. Right-click a link and choose Download with FastDM."
- **Error state:** কারণ + একটা স্পষ্ট পরের কাজ (যেমন "Open FastDM", "Pair again")
- **First-run:** একটাই ধাপ: "Connect to FastDM", ৩টা প্রশ্ন নয়
- **Permission prompt:** ঐচ্ছিক permission শুধু প্রথমবার দরকারের মুহূর্তে চাওয়া, কারণ লিখে
- **Context menu:** কন্টেন্ট ধরন অনুযায়ী গতিশীল (লিঙ্কে "Download with FastDM", ফোল্ডার-লিস্টিং পেজে "Download Folder with FastDM", ভিডিও এলিমেন্টে "Download video with FastDM"), মেনু একটাই প্যারেন্টের নিচে, ৩টার বেশি আইটেম না
- **আইকন:** Fluent-স্টাইলের SVG (অ্যাপের Segoe Fluent Icons-এর কাছাকাছি)
