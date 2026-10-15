# FastDM — Codex Full-Project Audit & Remediation Master Plan

> **উদ্দেশ্য:** এই ফাইলের নির্দেশনা অনুসারে বর্তমান FastDM workspace পুরোটা audit করো, যাচাইকৃত bug fix করো, security/reliability/UI উন্নত করো, automated tests বাড়াও, তারপর build/test চালিয়ে ফলাফল নথিভুক্ত করো। শুধু পরামর্শ বা report দিয়ে থেমে যেও না—যে পরিবর্তনগুলো নিরাপদভাবে implement করা যায় সেগুলো workspace-এ implement করো।
>
> **গুরুত্বপূর্ণ সীমা:** এখানে যেসব বিষয় “নিশ্চিত পর্যবেক্ষণ” বলা হয়েছে সেগুলো GitHub-এর public `master` snapshot `ae4b366ba2e9df45ca0e5ccde7f31eb08da95f6f` দেখে চিহ্নিত। তোমার বর্তমান workspace অন্য branch/commit বা uncommitted changes-এ থাকতে পারে। তাই এই তালিকাকে starting hypothesis হিসেবে নেবে, current source যাচাই করবে, এবং এই ফাইলের কোনো finding ভুল/আগেই fix হয়ে থাকলে report-এ তা লিখবে।

---

## 1. তোমার ভূমিকা ও চূড়ান্ত লক্ষ্য

তুমি FastDM-এর senior Windows desktop, networking, security, QA এবং UI/UX engineer হিসেবে কাজ করবে। এটি C#/.NET 10 Windows Forms download manager; সঙ্গে yt-dlp video downloader, HLS/DASH media engine, FTP/SFTP support, Chromium Manifest V3 extension, local bridge, `fastdm://` protocol handler এবং optional MSIX package project রয়েছে।

চূড়ান্ত লক্ষ্য:

1. user-facing bug-এর root cause বের করা; শুধু message বদলে প্রকৃত bug ঢেকে না রাখা।
2. download integrity, pause/resume, retries, cookies/credentials, bridge authentication, local state recovery, process lifecycle ও error handling সঠিক করা।
3. security/privacy issues কমানো—বিশেষ করে browser cookies, proxy/FTP/SFTP credentials, URLs, diagnostic logs এবং child-process arguments।
4. UI-কে professional, consistent, accessible, responsive এবং actionable করা; পুরো app অকারণে rewrite না করে existing visual language ও useful features বজায় রাখা।
5. reproducible build/package workflow, dependency/binary provenance এবং automated regression coverage তৈরি করা।
6. changes-এর পরে বাস্তব build/test commands চালিয়ে ফলাফল যাচাই করা; যা run করা সম্ভব হয়নি তা পরিষ্কারভাবে জানানো।

**শুধু audit report তৈরি করে থেমে যেও না।** প্রথমে audit করো; তারপর risk অনুযায়ী fix, tests, UI improvements ও documentation update implement করো। বড় architectural change প্রয়োজন হলে আগে existing behaviour এবং test coverage বুঝে ছোট, reversible ধাপে করো।

## 2. প্রথমে workspace নিরাপদে বুঝে নাও

কোনো ফাইল edit করার আগে:

- `git status --short --branch`, `git branch --show-current`, `git log -5 --oneline --decorate`, `git diff`, `git diff --cached` চালাও।
- user-এর uncommitted changes, local patches, untracked files, user-specific tools/configuration বা branch changes কখনও reset/overwrite করো না। কোনো `git reset --hard`, `git clean -fd`, force-push, `git am`, বা mass file delete করো না।
- project tree এবং সব tracked source/docs/manifests/test/workflows পড়ো; কেবল নিচে উল্লেখ করা ফাইল নয়। build/output/generated/cache folders বাদ দাও।
- `FastDM/Tools/`, `FastDM/FastDM-structure-and-engine-fixes.patch`, `FastDM.Package/Package.appxmanifest.bak` এবং যেকোনো স্থানীয় artifacts-এর উদ্দেশ্য যাচাই করো। patch ফাইল স্বয়ংক্রিয়ভাবে apply করো না; backup/reference file-কে live source ধরে নিও না।
- বর্তমান branch-এর actual code-ই source of truth। remote `master`-এর finding local branch-এর উপর অন্ধভাবে প্রয়োগ করো না।
- baseline build/test সম্ভব হলে change-এর আগে চালিয়ে existing failure আলাদা করে নথিভুক্ত করো। environment-এ কোনো SDK/component অনুপস্থিত থাকলে exact command ও exact error রেকর্ড করো।

কাজ শুরুর আগে `docs/CODEX_AUDIT_REPORT.md`-এ baseline তথ্য এবং প্রাথমিক finding লিখতে পারো; কাজ শেষ হলে report update করে confirmed/fixed/deferred/unverified status দাও।

## 3. জরুরি user-reported issue: yt-dlp / YouTube cookies

দুটি screenshot-এ নিম্নের error এসেছে:

1. `Sign in to confirm you're not a bot` — YouTube/target site request-এর জন্য authenticated session বা আরও কিছু site-specific requirement চাইছে।
2. `Could not copy Chrome cookie database` — Windows-এ yt-dlp browser cookie database পড়তে/copy করতে পারছে না; browser process file lock বা browser-এর cookie protection এর কারণ হতে পারে।

সংশ্লিষ্ট code-এ ইতিমধ্যে রয়েছে:

- `FastDM/YtDlp/YtDlp.cs` → `YtDlpCookies`, `HintFor`, `YtDlpRunner.CommonArgs`, `YtDlpRunner.RunAsync`, `YtDlpInfo.LoadAsync`, `YtDlpUpdater`, `YtDlpEngine`।
- `FastDM/UI/PreferencesForm.cs` → `BuildAdvanced`, “Video sites (yt-dlp)”, browser selection, cookies.txt path এবং yt-dlp updater UI।
- `FastDM/Core/Models.cs` → `YtCookieBrowser`, `YtCookieFile`, `LastYtDlpUpdate`।
- `FastDM/UI/MainForm.Downloads.cs` → download error/state/logging path।
- `FastDM/UI/MainForm.Add.cs`, `FastDM/UI/YtPickerForm.cs`, `FastDM/UI/MainForm.Bridge.cs` → video add/quality selection/extension integration।

### 3.1 Cookie-selection ও diagnosis-এর প্রকৃত bug

`YtDlpRunner.CommonArgs()`-এ cookies.txt path সেট থাকলে ফাইলটি exist করলে `--cookies` ব্যবহৃত হয়; নইলে selected browser থাকলে browser path-এ fallback হতে পারে। কিন্তু `YtDlpCookies.HintFor()` শুধু `cookieFile.Length > 0` দেখে “cookies file did not work” বলে। ফলে path ভুল/ফাইল নেই—এমন অবস্থাতেও ব্যবহারকারীকে ভুল diagnosis দেওয়া সম্ভব।

**করণীয়:**

- cookie source selection-কে explicit, testable result-এ রূপান্তর করো—যেমন `None`, `CookieFile`, `Browser` এবং invalid/missing-file state। একই logic argument builder, log redaction ও error classifier-এ ব্যবহার করো যাতে UI ভুল করে অন্য source-এর error না দেখায়।
- configured cookie file নেই, unreadable, empty/invalid Netscape format, selected browser-এর database locked, cookies extract হয়েছে কিন্তু site rejected করেছে, login প্রয়োজন, expired session, network/403, missing/outdated yt-dlp, JavaScript runtime/ffmpeg missing—এসব আলাদা classify করো। Classification অনিশ্চিত হলে “সম্ভাব্য কারণ” বলে জানাও; নিশ্চিত কারণ হিসেবে বলো না।
- `YtDlpRunner.RunAsync()` থেকে exit code, safe stderr tail, cancellation result এবং elapsed time structured ভাবে ফেরত নাও। stderr থেকে actionable error বের করো; generic “failed” দিয়ে তথ্য হারিও না।
- `YtDlpInfo.LoadAsync()` এবং `YtDlpEngine.RunAsync()`—দুই path-এই একই diagnostic/error policy প্রয়োগ করো। URL-এ query token/credentials থাকলে full URL error dialog বা log-এ রাখবে না।
- child process-এর stdout/stderr encoding end-to-end যাচাই করো। screenshot-এ `you�re`-এর মতো replacement character দেখা যায়। ASCII apostrophe, Unicode right single quote, Bangla title এবং mixed UTF-8 error message দিয়ে tests যোগ করো; অন্ধভাবে সব text replace করে দিও না। বর্তমান code `StandardOutputEncoding = Encoding.UTF8`, `StandardErrorEncoding = Encoding.UTF8`, `PYTHONUTF8=1` সেট করে—এসবের পরও UI text garbled হলে actual source byte/code path নির্ধারণ করো।
- cancellation/timeout হলে process ও child process বন্ধ হচ্ছে কিনা, event handlers/process exit/error capture race আছে কিনা পরীক্ষা করো। cancellation-কে authentication failure হিসেবে শ্রেণিবদ্ধ করো না।

### 3.2 Security/privacy-সম্মত cookie UX

- login/session cookie খুব sensitive credential। কোনো cookie, cookies.txt-এর content, Authorization header বা raw bridge cookie value log করো না; telemetry বা GitHub issue-তে পাঠাবে না। ব্যবহারকারীর explicit action ছাড়া cookies export/copy/upload করবে না।
- Chrome/Edge-এর security protection দুর্বল করা, registry/policy দিয়ে app-bound encryption বন্ধ করা, browser process নিজে থেকে kill করা, বা auth/CAPTCHA protection bypass করার চেষ্টা করবে না। UI যেন বলে browser পুরোপুরি বন্ধ করে আবার চেষ্টা করা যেতে পারে; background/system tray processes-ও কারণ হতে পারে। এটি ব্যর্থ হলে user-এর নিজের exported cookie file বা supported browser path নির্বাচন করতে দাও।
- `cookies.txt` নির্বাচন করার সময় path exists/readable কিনা validate করো; file selection, clear/remove button, active source indicator এবং save/apply behaviour নিশ্চিত করো। file content read/parse করার দরকার হলে memory-তে minimal validation করো, content UI/log-এ প্রকাশ করো না। file-টি session credential হতে পারে—এটি অন্যের সঙ্গে share না করার warning দাও।
- cookies export-এর জন্য official yt-dlp documentation link দিতে পারো; third-party export extension recommend করার আগে provenance/security ঝুঁকি স্পষ্ট করো। FastDM নিজে cookies file তৈরি করবে না, যদি না আলাদা safe, explicit, reviewed feature হিসেবে বাস্তবায়ন করা হয়।
- Cookie সমস্যা মানে প্রতিটি ব্যর্থতার সমাধান cookie নয়। DRM-protected content, private/removed video, network/proxy errors বা unsupported extractor-এ ভুল cookie নির্দেশনা দেখিও না। Site terms, user authorization এবং applicable rights বজায় রাখো।

### 3.3 Screenshot error dialogue-এর UI redesign

বর্তমান warning-এ long technical message কয়েক লাইনে গাদাগাদি করে আছে এবং তাৎক্ষণিক action নেই। Professional error flow তৈরি করো:

- পরিষ্কার title: `Video couldn't be accessed` বা localised equivalent; “Video” একা title নয়।
- top line-এ compact, human-readable summary এবং কারণটি নিশ্চিত না হলে “Possible cause” label।
- context অনুযায়ী action: `Open video settings`, `Choose cookies.txt`, `Retry`, `Copy diagnostic details`, `Close`। যে action বাস্তবে implement করা হয়নি, button বানাবে না।
- technical detail collapsible/secondary panel-এ; text selectable/copyable; long errors readable wrap করবে, dialog resize/scroll support রাখবে এবং keyboard focus/default button ঠিক করবে।
- `Copy diagnostic details` output অবশ্যই redacted হবে: credentials, cookie contents, URL query fragments/tokens, proxy password, user private paths ইত্যাদি মুছে/মাস্ক করে দাও। raw error copy করার আগে redaction tests লিখো।
- browser cookie database failure-এ আলাদা actionable guidance; login-required failure-এ আলাদা guidance; missing tool/runtime failure-এ install/repair guidance; network error-এ connection/proxy guidance।
- text-এ “নিশ্চিতভাবে কাজ করবে” প্রতিশ্রুতি নয়; external website-এর access policy/behaviour FastDM নিয়ন্ত্রণ করে না।
- একই error dialog theme/font/spacing/design tokens-এর সঙ্গে সামঞ্জস্যপূর্ণ হবে; high-DPI scaling, 125/150/200%, small screen, keyboard-only, screen reader/accessibility labels পরীক্ষা করো।

## 4. Snapshot-এ দেখা concrete findings — current source যাচাই করে fix করো

### P0/P1 — authentication data ও download isolation

**A. Global cookie jar cross-download persistence**

`FastDM/Core/DownloadEngine.cs`-এ static `CookieContainer Jar` এবং global `HttpClient`; `AddCookies()` এই shared jar-এ extension-provided cookies যোগ করে। `FastDM/UI/MainForm.Bridge.cs`-এর `ExternalAdd()` নতুন request-এর cookies-কে download/dialog processing-এর আগে এই jar-এ যোগ করে। Code comment অনুযায়ী jar domain-scoped এবং in-memory, কিন্তু app session-এ cookies থেকে যায় এবং একই domain-এর unrelated/concurrent jobs-এ session context overlap হতে পারে।

**করণীয়:** per-download/request-scoped cookie isolation ডিজাইন করো; cookies শুধু সংশ্লিষ্ট item-এর request-এ প্রয়োগ হবে, অন্য download/profile-এ auto-share হবে না। HLS/DASH, redirect, retries ও resume flow-তে প্রয়োজনীয় cookie continuity ভাঙবে না। Cookies এর expiry/path/secure/domain rules সঠিকভাবে মানো। Regression test: একই host-এ ভিন্ন user/session cookies দেওয়া দুই download একে অপরের cookie ব্যবহার করতে পারবে না; restart-এর পরে stale extension cookies পাওয়া যাবে না।

**B. Credentials-সহ URL persistence/leak audit**

`FastDM/Network/CredentialStore.cs`-এ `CredStore.TryGet()` URI `UserInfo` থেকে username/password extract করতে পারে। সম্পূর্ণ repo-তে URL input → `DownloadItem.Url` → state serialization → log/error/dialog → bridge → child process পর্যন্ত trace করো। FTP/SFTP credentials URL-এ দেওয়া হলে তা `state.json`, diagnostic logs, task list, clipboard, exception message বা command line-এ রেখে দেওয়া যাবে না। Credentials আলাদা secure store/session memory-তে রাখো এবং persisted/displayed URL redact করো; existing URLs থেকে credentials safe migration করার backward-compatible path দাও। Username/password handling-এ URI encoding, delimiter, Unicode এবং malformed UserInfo tests যোগ করো।

**C. Proxy password child-process argument**

`FastDM/YtDlp/YtDlp.cs`-এর `YtDlpRunner.CommonArgs()` manual proxy credentials-কে `--proxy` argument-এ URL হিসেবে যোগ করে। Process command-line introspection-এর মাধ্যমে secret exposure-এর ঝুঁকি আছে। yt-dlp/proxy support বিবেচনা করে credentials process arguments ও logs-এ না রাখার নিরাপদ viable design খুঁজে বের করো; সম্ভব না হলে limitation স্পষ্ট করো, log redaction নিশ্চিত করো, exposure কমাও এবং কার্যকারিতা/compatibility test করো। কোনো secret temporary file হলে restrictive ACL, cleanup এবং crash cleanup বিবেচনা করো।

### P1 — update/retry correctness

**D. yt-dlp updater result ও retry window**

`FastDM/YtDlp/YtDlp.cs`-এর `YtDlpUpdater.UpdateAsync()` process exit code উপেক্ষা করে stdout/stderr মিলিয়ে string ফেরত দেয়। `MaybeAutoUpdateAsync()` update attempt-এর আগেই `LastYtDlpUpdate` সেট করে এবং exception silently swallow করে। Failed/cancelled update-এর পরও 24 ঘণ্টা আর চেষ্টা না হওয়ার সম্ভাবনা আছে।

**করণীয়:** exit code, executable version before/after, update success/failure আলাদা করে surface করো; last-successful-check এবং last-attempt-এর semantics স্পষ্ট করো। transient network failure হলে reasonable backoff/retry; cancellation হলে appropriate state; permission/Store packaged case হলে স্পষ্ট message। Failed update-কে success দেখিও না। Existing binary update yt-dlp-এর নিজের verification mechanism ব্যবহার করে; custom updater করলে official checksum/signature/provenance যাচাই ছাড়া binary replace করো না। Update process চলাকালে downloader ব্যবহার করছে এমন executable replace/race handling যাচাই করো।

### P1 — bridge robustness

**E. `FastDM/Bridge/BridgeServer.cs` request handling**

Snapshot-এ `HandleAdd()`-এর request-body null/too-large condition duplicate দেখা যায়; rate-limit branch-এ `_ = Send(res, 429, ...)` fire-and-forget। `LoopAsync()` প্রতিটি incoming request-এর জন্য unconstrained `Task.Run()` করে।

**করণীয়:** duplicated check সরাও; response send সব route-এ awaited এবং exactly-once হওয়া নিশ্চিত করো; response writer disposal/exception handling ঠিক করো; concurrent request/task limit এবং request body/read timeout প্রয়োগ করো; cancellation/shutdown-এ in-flight request gracefully drain/terminate করো। Existing 127.0.0.1 binding, strict Host/extension Origin checks, pairing confirmation, per-request token checks, token-origin binding, rate limiting এবং URL/cookie validation বজায় রাখো। Browser CORS Origin নিজে native local process authentication নয়—security boundary হিসেবে token/user pairing-ই গুরুত্বপূর্ণ; এটা দুর্বল করবে না।

Tests: oversized/malformed JSON, empty body, concurrent add flooding, rate-limit response completion, shutdown during request, forged Origin, wrong token/origin, unexpected method/path, URL schemes, cookie domain mismatch, invalid cookie data।

### P1 — clean-clone reproducibility ও distribution

**F. Bundled external tools নেই/ignored**

`FastDM/FastDM.csproj` `Tools\**\*` output/publish-এ copy করে এবং `FastDM/YtDlp/YtDlp.cs` `yt-dlp.exe`, `deno.exe`, `qjs.exe` খোঁজে। কিন্তু repo tree-তে `FastDM/Tools/` দেখা যায়নি এবং `.gitignore`-এ `FastDM/Tools/` বাদ। `YtDlpLocator` PATH-এও খোঁজে, তাই fresh clone-এ video/media functionality নির্ভরযোগ্যভাবে পাওয়া নাও যেতে পারে।

**করণীয়:** bundled binaries track করা বা exclude করার intentional distribution strategy ঠিক করো। নিরাপদ default হলো documented deterministic bootstrap/package pipeline: official upstream source/release, pinned/controlled version, SHA-256/signature/provenance verification, required executable version checks, licence notices এবং clear missing-tool diagnostics। Arbitrary PATH executable অন্ধভাবে বিশ্বাস করা উচিত কি না যাচাই করো; external-tool discovery explicit/configurable ও deterministic করো। Network/download verification ছাড়া executable auto-download/run করো না। `ffmpeg`, `yt-dlp`, JS runtime ও native DLL-র licence/distribution obligations যাচাই করো; binary malware scanning বা dependency provenance দাবি করলে বাস্তবে চালানো checks রেকর্ড করো।

**G. Package version mismatch**

Snapshot-এ `FastDM/FastDM.csproj` version `1.0.14`, কিন্তু `FastDM.Package/Package.appxmanifest` identity version `1.0.13.0`; `.bak` manifest-ও stale `1.0.13.0`।

**করণীয়:** release version-এর একক source of truth বা automated consistency validation যোগ করো। app version, MSIX identity version, extension version এবং release artifacts যে জায়গায় আলাদা হওয়া প্রয়োজন সেখানে intentional documented mapping থাকবে। MSIX version-four-part numeric rules, upgrade monotonicity এবং Store submission rules যাচাই করো। Backup manifest source হিসেবে ব্যবহৃত হবে না। stale package files delete করার আগে repository policy/usage যাচাই করো।

**H. Package project CI coverage**

`.github/workflows/ci.yml` desktop `FastDM.csproj`, `FastDM.Tests` এবং extension `npm test` চালায়; `FastDM.Package/FastDM.Package.wapproj` build/manifest validation স্পষ্টভাবে CI-তে নেই। WAP project `Microsoft.DesktopBridge.props` import করে; উপযুক্ত Visual Studio MSIX Packaging Tools না থাকলে build failure environment-related হতে পারে।

**করণীয়:** package manifest consistency checks এবং possible MSIX packaging validation যোগ করো। GitHub runner-এ প্রয়োজনীয় Desktop Bridge toolchain সত্যিই আছে কি না যাচাই করে package job implement করো; unavailable environment-এ fake pass নয়—job condition/documented manual validation ও exact prerequisites দাও। Existing portable build যেন WAP dependency না পেয়ে ভেঙে না যায়।

### P1/P2 — network/download reliability

**I. HTTP segmented download, resume ও timeout**

`FastDM/Core/DownloadEngine.cs`-এ range segmentation, retry, stall timeout এবং temporary file handling রয়েছে; এই core path-এর direct integration tests কম/নেই বলে দৃশ্যমান। প্রতিটি edge case থেকে bug ধরে নিও না—কিন্তু test করে correctness প্রমাণ করো:

- Server `Accept-Ranges` ভুল দাবি করে, range request-এ `200` দেয়, `206`/`Content-Range` mismatch দেয়, range response-এ unexpected length/EOF হয়, redirect হয়, empty/unknown length হয়, content পরিবর্তিত হয়, ETag/Last-Modified বদলায়।
- Cancel/pause/retry-এর সময় byte counters, segment offsets, temp-file length এবং final file corruption/duplicate/missing bytes।
- Non-range single-stream download, zero-byte file, slow/stalled server, timeout, 404/403/429, transient 5xx/network reset, very large file ও simultaneous downloads।
- Existing target file, unique-name generation, path separator/path traversal attempts, reserved Windows filenames, Unicode/Bangla filenames, invalid `Content-Disposition` এবং very long names।

**করণীয়:** local loopback test HTTP server দিয়ে deterministic regression suite তৈরি করো। Resume-এর আগে validators/expected size/segment state সামঞ্জস্য করো; invalid range হলে corrupted output নীরবে final হিসেবে mark করো না। Retry শুধুমাত্র retryable conditions-এ bounded backoff সহ হবে। User cancel, auth failure এবং permanent HTTP errors স্পষ্টভাবে পৃথক হবে।

**J. Static `HttpClient` reset/lifecycle**

`DownloadEngine.ResetClient()` নতুন `HttpClient` assign করে; আগের handler/client disposal strategy যাচাই করো। Proxy settings বারবার বদলালে connection/handler resources leak হবে কি না পরীক্ষা করো। Existing in-flight downloads/proxy-change behaviour ভেঙে না দিয়ে safe replacement/lifecycle policy implement করো।

**K. Redirect, cookie, referer ও proxy policy**

`DownloadEngine.CreateClient()` auto redirect (`MaxAutomaticRedirections = 10`), shared cookies, proxy এবং per-item Referer/User-Agent ব্যবহার করে। Cross-origin redirect-এ Referer, sensitive headers, authorization/cookies কোথাও unintendedভাবে পাঠানো হচ্ছে কি না tests দিয়ে যাচাই করো। URL scheme validation, proxy bypass for loopback, DNS/redirect edge cases, TLS certificate validation এবং user-configured credentials নিরাপদে handle করো। TLS validation কখনও globalভাবে disable করবে না।

**L. FTP/SFTP credential ও host-key correctness**

`FastDM/Network/RemoteTransfer.cs`, `RemoteLister.cs`, `CredentialStore.cs` audit করো। SFTP server key/fingerprint checking বজায় রাখো; trust-all callback বা silent host-key acceptance যোগ কোরো না। Credential Manager write/read/delete failure হলে settings UI ব্যবহারকারীকে ভুলভাবে “remembered” বলছে কি না পরীক্ষা করো। P/Invoke error handling, secret memory lifetime, port/default-host handling, reconnect/retry ও path normalization যাচাই করো।

### P2 — state, scheduling, media ও observability

**M. State persistence/migration**

`FastDM/Core/StateStore.cs`, `FastDM/Core/Models.cs`, `FastDM/UI/MainForm.cs` এবং `FastDM.Tests/StateStoreTests.cs` audit করো। Atomic temp-write, `.bak/.bak2`, corrupted-file quarantine, `KeepBlocked` এবং schema migration behaviour বজায় রাখো। Process crash/locked disk/permission failure, malformed schema, unknown future schema, large state, simultaneous save এবং backup restore tests যোগ করো। State JSON-এ URL credentials/cookie values/secret text ঢুকে যাচ্ছে কি না নিশ্চিত করো।

**N. Scheduler, concurrency, traffic control ও tray lifecycle**

`FastDM/Core/Scheduler.cs`, `TrafficControl.cs`, `FastDM/UI/MainForm.Downloads.cs`, `MainForm.Shell.cs`, `StartupManager.cs`, `Program.cs` audit করো। Max simultaneous jobs/connection limits, pause/resume, shutdown, wake/sleep, overnight schedule (যেমন 02:00–06:00), timezone/DST, single-instance events, named-pipe cancellation/invalid input এবং UI-thread marshaling race পরীক্ষা করো। Background exceptions unobserved থাকবে না; exit-এর আগে state save আর active-download policy স্পষ্ট থাকবে।

**O. HLS/DASH/media parser**

`FastDM/Media/MediaDetector.cs`, `MediaParsers.cs`, `MediaModels.cs`, `PlaylistBuilder.cs`, `StreamEngine.cs`, `FastDM/UI/MediaPickerForms.cs` audit করো। Malformed/huge HLS playlists, DASH XML namespaces/entities, relative/absolute URL resolution, redirects, encrypted/DRM restrictions, byte-range segments, missing audio/video tracks, discontinuities, segment retries/resume, temp directory cleanup এবং ffmpeg exit/stream merge behaviour tests করো। Untrusted remote XML/manifest parsing-এ DTD/external entity resolution বন্ধ রাখো; unbounded memory/segment count বা arbitrary local-file read-এর পথ যেন না থাকে। DRM protection bypass করার feature implement করবে না।

**P. Process execution/tool safety**

সব `Process.Start`, `ProcessStartInfo`, shell invocation, updater, antivirus/external app launch, ffmpeg/yt-dlp/deno/qjs invocation খুঁজে দেখো। যেখানে সম্ভব `UseShellExecute=false` এবং `ArgumentList` ব্যবহার করো; user-supplied URL/title/file path যেন shell metacharacter injection ঘটাতে না পারে। Executable location trust, arguments, current working directory, environment variable, timeout, cancellation, output size bound এবং exit-code validation যাচাই করো। Raw process command বা secrets logs-এ লেখো না।

**Q. Logging & privacy**

`FastDM/Core/AppLog.cs` ও সব `AppLog.Write` call site audit করো। Logs opt-in/clear user-controlled কি না, retention/encoding/concurrency ঠিক আছে কি না, exception message-এ URL userinfo/query tokens/cookies/local personal paths/passwords বেরিয়ে যায় কি না দেখো। Structured redaction helper ও test যোগ করো। `CookieFile` path log করতে হলে দরকারের বেশি path তথ্য দেখাবে না। Error diagnostics default-এ কোনো remote service-এ যাবে না।

## 5. UI/UX quality gate — পুরো desktop application ও extension

Relevant folders: `FastDM/UI/*.cs`, `FastDM/UI/Theme.cs`, `DetailsPanel.cs`, `Controls.cs`, `FastDM.Extension/src/popup/*`, `src/options/*`, `src/background/*`, `_locales/en/messages.json`, `_locales/bn/messages.json`.

সম্পূর্ণ UI audit করো—শুধু screenshot dialog নয়:

- Main window, add URL, batch import, folder download, media/yt-dlp quality picker, preferences, network/proxy, scheduler, details panel, menus, context menu, confirmation/error dialogs, popup/options, task rows, empty/loading/error states।
- একই spacing scale, typography, icon style, button priority, alignment, consistent naming ও visual hierarchy। Existing Theme palette/design tokens পুনর্ব্যবহার করো; arbitrary colour/controls প্রতিটি form-এ আলাদা করে implement কোরো না।
- High-DPI 100/125/150/200%, Windows text scaling, window resize/minimum size, horizontal clipping, long filename/URL, translated Bangla strings, right-to-left না হলেও mixed Unicode, dark/light/system theme, keyboard navigation, focus indicators, tab order, Enter/Esc, contrast, accessible names/tooltips পরীক্ষা করো।
- Fixed-size WinForms forms/dialogs clipping করছে কি না দেখো; layout/anchoring/table/flow/auto-scale ব্যবহার করে প্রয়োজনমতো robust করো। Buttons-এর click handlers সব বাস্তবে কাজ করে কি না, disabled/loading state ও duplicate click handling পরীক্ষা করো।
- Progress UI accurate/monotonic এবং unknown/estimated sizes-এর জন্য honest হবে; speed/ETA flicker, status transitions, error state, retry/resume state, completed file action এবং deleted-list recovery সঠিক হবে।
- long network operations UI thread block করবে না; exception event handlers-এ propagate/unobserved হবে না।
- Browser extension popup/options keyboard accessible হবে; error message swallow করে silently fail নয়। Node test-এ background handlers mock করা আছে—বাস্তব browser manual matrix-ও document করো।
- User-visible text/resource localization পদ্ধতি যাচাই করো। Desktop UI currently largely hard-coded English; bilingual support প্রয়োজন হলে consistent resource strategy design করো। অন্তত existing Bangla/English extension locale parity বজায় রাখো এবং missing/mismatched keys test করো।
- UI aesthetic-only changes দিয়ে functional bugs ঢেকে রেখো না। UI screenshots automated snapshot না থাকলে DPI/functional manual test matrix report-এ যোগ করো।

## 6. Test coverage & engineering process

বর্তমান checks (repo snapshot অনুযায়ী):

- `scripts/Test-Project.ps1`
- `.github/workflows/ci.yml`
- `FastDM.Tests/` — bridge, protocol, state store, batch import, file check, yt-dlp output/site tests
- `FastDM.Extension/tests/bridge.test.mjs`, `locales.test.mjs`

বর্তমান test suite যে behaviour cover করে না/অপর্যাপ্ত হতে পারে, সেখানে উপরের real bugs-এর ভিত্তিতে tests যোগ করো। শুধু “tests passed” বলবে না—actual executed commands ও outputs উল্লেখ করো।

### বাধ্যতামূলক যাচাই

PowerShell-এ repository root থেকে:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Test-Project.ps1
```

এছাড়াও আলাদা করে, environment উপযুক্ত হলে:

```powershell
dotnet build FastDM/FastDM.csproj -c Release -p:Platform=x64
dotnet run --project FastDM.Tests -c Release
node --test FastDM.Extension/tests/bridge.test.mjs FastDM.Extension/tests/locales.test.mjs
```

Windows/MSIX toolchain সঠিকভাবে ইনস্টল থাকলে তবেই `FastDM.Package/FastDM.Package.wapproj` build/validation চালাও। Toolchain না থাকলে exact prerequisite/error জানাও; app build pass-কে MSIX pass বলে চালাবে না।

অতিরিক্ত:

- `git diff --check` চালাও; merge markers, accidental generated files, `.tmp`, test outputs, credentials, private cookie files, local user profile path বা binaries accidentally staged হয়েছে কি না যাচাই করো।
- Static analysis/build warnings triage করো; useful nullable/platform warnings চাপা দিও না। Existing unrelated warnings আলাদা রিপোর্ট করো।
- Tests deterministic হবে; internet/YouTube live calls CI test হিসেবে ব্যবহার করো না। yt-dlp integration test external access চাইলে opt-in manual test হিসেবে document করো এবং real cookies/credentials কখনও commit করো না।
- Tool/binary tests mocked temp paths বা checksums fixture দিয়ে করো; live browser profile read বা real cookie file access automated test-এ কোরো না।
- package manifest validation, version consistency, extension manifest/resources parity, output tool availability এবং clean-clone build check যুক্ত করো।

## 7. Release/package/repository hygiene

- `FastDM.Package/Package.appxmanifest`, `.wapproj`, `FastDM/FastDM.csproj`, `FastDM.slnx`, `FastDM.Extension/manifest.json`, `FastDM.Extension/tools/pack.ps1`, version strings, package display metadata ও release instructions cross-check করো।
- `Package.appxmanifest.bak` ও `FastDM/FastDM-structure-and-engine-fixes.patch`-এর provenance, necessity এবং documentation যাচাই করো। Unused/stale হলে change rationale লিখে উপযুক্তভাবে remove/relocate করার প্রস্তাব বাস্তবায়ন করো—কিন্তু user-এর অনুমতি ছাড়া untracked/local data বা user changes মুছবে না।
- MSIX identity/publisher changes casually করবে না; Store-installed version-এর identity এবং publisher compatibility ভাঙবে না। Manifest-এ target-device declarations/StartupTask/protocol capabilities/visual assets/architecture যাচাই করো। Unsupported extension warning হলে actual audience/target devices-এর ভিত্তিতে সিদ্ধান্ত নাও; অন্ধভাবে capability বাদ দিও না।
- Third-party components (yt-dlp, ffmpeg, Deno/QuickJS, libraries, native DLL) version, source, licence, attribution, update strategy, hash/verification এবং redistributability নথিভুক্ত করো। Current snapshot-এ এসব tool binary GitHub tree-তে দৃশ্যমান নয়—লোকাল Tools folder-এ থাকলে তাদের content/provenance আলাদাভাবে যাচাই করো।
- clean clone থেকে `README.md`-এর instructions সত্যিই কাজ করে কি না যাচাই করো। Missing tool message ও bootstrap guide যোগ করো।
- `FastDM` নাম সর্বত্র বজায় রাখো; নতুন ভুল branding (`FastDM Pro`, `Fast DM` inconsistency ইত্যাদি) যোগ করবে না। User-approved naming intent বদলাবে না।

## 8. Coding rules — কী করবে না

- কোনো security check সরাবে না শুধুমাত্র যাতে test pass করে।
- TLS certificate validation disable, browser cookie encryption weaken, credentials/logging expose, raw cookies commit/upload, trust-all SSH host keys, unauthenticated bridge download trigger বা URL scheme validation relax করবে না।
- “Sign in to confirm you’re not a bot” বা cookie errors সমাধানের নামে site authentication, CAPTCHA, DRM বা access controls bypass করার চেষ্টা করবে না। বৈধভাবে authenticated user session-এর documented/explicit consent-based handling-ই scope।
- unrelated architecture wholesale rewrite বা third-party packages indiscriminately upgrade করবে না। Package update দরকার হলে changelog/compatibility/security কারণ যাচাই করে targeted change করো এবং tests দাও।
- user changes overwrite/reset/delete করবে না; repo-wide formatting churn এড়িয়ে চলো।
- tests/build না চালিয়ে success দাবি করবে না। Internet-dependent checks unavailable হলে limitation জানাও।
- শুধু comments/README পরিবর্তন করে functional issue fixed বলবে না।

## 9. কাজের priority / acceptance criteria

### Phase 1 — baseline ও critical privacy/security

- [ ] branch/status/diff সংরক্ষিত; baseline findings recorded.
- [ ] credentials-bearing URLs persistence/logging audit ও safe remediation.
- [ ] global cookies isolation issue remedied or explicit design limitation documented with concrete mitigation; automated regression tests exist.
- [ ] proxy credentials/process output/log redaction audited and improved.
- [ ] local extension bridge request handling/rate limiting/response lifecycle hardened without weakening pairing/auth.

### Phase 2 — yt-dlp bug & user experience

- [ ] cookie source selection consistent, missing/invalid path detected and accurate diagnostics.
- [ ] browser database lock, login-required, expired/unusable cookies, outdated yt-dlp, missing runtime, network failure পৃথক actionable states.
- [ ] updater exit code/success state and failed retry timing correct.
- [ ] readable professional error UX; redacted copy diagnostics; buttons work; English/Bangla Unicode display tests pass.
- [ ] no actual auth-bypass or weakened browser security added.

### Phase 3 — download correctness & full UI

- [ ] local-server integration tests for range/resume/retry/cancel/zero-byte/redirect/error/temp-file handling.
- [ ] media parser/stream merge and SFTP/credential boundary review documented; applicable automated tests added.
- [ ] major forms/popups/preferences/theme/DPI/accessibility audited and meaningful issues fixed.
- [ ] state persistence, scheduler, UI-thread/background lifecycle and logs tested.

### Phase 4 — build/package/reproducibility

- [ ] missing `Tools/` strategy documented and clean-clone workflow deterministic.
- [ ] version consistency validation for desktop/MSIX/extension; stale manifest backup resolved intentionally.
- [ ] CI includes all checks feasible within runner/toolchain; unavailable MSIX toolchain honestly reported.
- [ ] repo hygiene/licensing/dependency provenance check done.

### Phase 5 — final verification & handoff

- [ ] all available tests/builds run after final code change.
- [ ] `git diff --check` passes; no unintended generated/binary/secret files.
- [ ] `docs/CODEX_AUDIT_REPORT.md` contains finding, severity, file(s), root cause, fix, tests and status (`FIXED`, `MITIGATED`, `DEFERRED`, `NOT REPRODUCED`, `BLOCKED`).
- [ ] `docs/TESTING.md` updated with repeatable tests and manual matrix.
- [ ] final message summarizes files changed, important decisions, exact commands/results, any unverified items and remaining limitations; never claim complete perfection or 100% bug-free.

## 10. Required output from you (Codex)

When finished, provide:

1. **Executive summary** — major root causes fixed, remaining limitations and whether user-facing YouTube cookie errors are now accurately diagnosed.
2. **Changed files** — file-by-file summary with reason.
3. **Security review** — credentials, cookies, proxy, bridge, URL/process execution, logs, update pipeline, downloaded tools and SFTP host-key handling.
4. **UI review** — concrete changes to error dialogue/preferences/main UI/accessibility and what manual verification remains.
5. **Tests/builds** — exact command, pass/fail/block status; do not conflate project build with MSIX packaging.
6. **Remaining risks** — external yt-dlp/site/Google/Chrome restrictions, missing upstream tools, unavailable toolchains or untested real-world cases.

Start now by inspecting the complete current workspace. Do not ask the user to upload every source file: you already have access to the opened project folder. Ask a question only if a destructive/irreversible decision or a genuinely blocking ambiguity cannot be resolved safely. Otherwise make the smallest professional, tested implementation decisions and continue through the phases.

---

## Reference links for current external behaviour

Use these as technical references, not as a substitute for inspecting the installed yt-dlp version or reproducing the issue:

- yt-dlp FAQ — cookies, cookies.txt format and troubleshooting: https://github.com/yt-dlp/yt-dlp/wiki/FAQ
- yt-dlp official source — cookie database/error handling implementation: https://github.com/yt-dlp/yt-dlp/blob/master/yt_dlp/cookies.py
- yt-dlp release notes/current binaries/checksum documentation: https://github.com/yt-dlp/yt-dlp/releases and https://github.com/yt-dlp/yt-dlp
- Chromium security FAQ — platform-specific cookie encryption: https://github.com/chromium/chromium/blob/main/docs/security/faq.md
- Google Security — Device Bound Session Credentials background and security model: https://blog.google/security/protecting-cookies-with-device-bound-session-credentials/

**Reminder:** browser/site behaviour changes over time. Confirm the installed yt-dlp version (`yt-dlp --version` or the app’s safe equivalent), executable provenance, exact error classification and official upstream guidance at implementation time.
