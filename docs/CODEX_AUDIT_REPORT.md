# FastDM পূর্ণ প্রকৌশল ও নিরাপত্তা অডিট

**তারিখ:** ২০২৬-১০-১৫
**ওয়ার্কস্পেস:** `C:\dev\FastDM`
**মাস্টার পরিকল্পনা:** [`CODEX_AUDIT_AND_REMEDIATION.md`](../CODEX_AUDIT_AND_REMEDIATION.md)

## কাজের পরিধি ও সংরক্ষণ

ডেস্কটপ অ্যাপ, ডাউনলোড ও yt-dlp ইঞ্জিন, YouTube preview/playlist flow, cookie ও credential boundary, browser extension ও local bridge, state/logging, UI lifecycle, diagnostics, package/version metadata, CI এবং test coverage পর্যালোচনা করা হয়েছে। শুরুতে branch ছিল `master`, `HEAD` ছিল `220a112`; worktree-তে ব্যবহারকারীর একটিমাত্র পরিবর্তন ছিল `FastDM.slnx` থেকে Debug x64-এর `BuildType` লাইন সরানো। ওই পরিবর্তন অক্ষত রাখা হয়েছে। কোনো reset, commit, push বা release করা হয়নি; package/build verification হয়েছে অস্থায়ী clean copy-তে।

## যাচাইকৃত finding ও remediation

| গুরুত্ব | মূল কারণ / finding | পরিবর্তন ও প্রমাণ | অবস্থা |
| --- | --- | --- | --- |
| উচ্চ | একাধিক download-এর browser cookie একই shared request scope-এ পৌঁছাতে পারত; expiry ও host/path/secure scope-ও যাচাই দরকার ছিল। | প্রতি download-এ cookie scope, expiry ও domain/path/secure filtering; shared HTTP client cookie-free। unit ও loopback পরীক্ষা পৃথক session এবং একই host-এ concurrent download যাচাই করে। | **FIXED** |
| উচ্চ | URL user-info credential state, backup, log, command line বা `fastdm://` fallback-এ থেকে যেতে পারত। | `UrlCredential` credential আলাদা করে, persisted/display URL থেকে user-info সরায়; state ও backup migration, URL/log redaction, protocol fallback sanitization যোগ হয়েছে। regression test encoded/Unicode credential, query preservation ও redaction যাচাই করে। | **FIXED** |
| উচ্চ | Local bridge-এ body size, duration, concurrency, origin এবং request completion সীমা যথেষ্ট শক্ত ছিল না। | strict UTF-8 ও URL allow-list, body/time/concurrency bounds, explicit busy/rate-limit response, cancellation এবং extension-origin-bound pairing আছে। loopback test oversized/concurrent request, wrong origin, pairing cooldown ও response completion যাচাই করে। | **FIXED** |
| উচ্চ | HTTP range response-এর status, `Content-Range` বা body length না মিললে ভুল/অসম্পূর্ণ bytes গ্রহণের ঝুঁকি ছিল; zero-byte output-ও complete ধরা যেত। | range/length validation, retry/cancel/resume এবং non-empty output promotion যাচাই করা হয়। loopback tests exact bytes, mismatch, redirect, 503, cancellation ও resume দেখে। | **FIXED** |
| উচ্চ | yt-dlp update চলাকালে download process শুরু হতে পারত; retry আচরণও rate-limit-এ অতিরিক্ত request করতে পারত। | update exclusive process gate নেয়; active child শেষ না হওয়া পর্যন্ত অপেক্ষা করে এবং নতুন process gate-এর পেছনে থাকে। yt-dlp retries সীমাবদ্ধ; 429-এ `Retry-After` ছাড়া outer retry হয় না, header থাকলে সর্বোচ্চ একবার; transient network failure bounded retry পায়। integration/policy tests যোগ হয়েছে। | **FIXED; regression-tested** |
| উচ্চ | yt-dlp metadata request/output ও stale signed media URL reuse অনির্দিষ্ট ছিল। | metadata child cancellation/timeout, 16 MB output cap এবং 2 মিনিটের user-local cache (প্রতি JSON 8 MB cap) যোগ হয়েছে। cache-এ cookie রাখা হয় না; consume/expiry/settings change-এ মুছে ফেলা হয়, stale file-ও পরিষ্কার হয়। cached extraction ব্যর্থ/expired হলে একবার info refresh হয়। | **FIXED; regression-tested** |
| মাঝারি | সরাসরি YouTube video ও playlist add-এ preview দেরিতে বা আলাদা পথে আসত; format size ও merge progress বিভ্রান্তিকর হতে পারত। | একটি resizable preview তাৎক্ষণিক খোলে; title/channel/duration/thumbnail/format/আনুমানিক size async পূরণ হয়। playlist entries নির্বাচন, reported/unknown count, `MaxBatchUrls` cap এবং duplicate suppression আছে। unknown size স্পষ্ট; FFmpeg না থাকলে merge-নির্ভর format অকার্যকর। transfer, merge ও output verification আলাদা stage; merge-এ speed/ETA থাকে না। | **FIXED; parser/stage tests; UI manual verification বাকি** |
| মাঝারি | একই video একাধিক clipboard/bridge event-এ duplicate job হতে পারত; selected details item মুছে গেলে stale থাকতে পারত। | video ID/cache key দিয়ে dedupe ও active-job suppression; item/history deletion-এ Details selection পরিষ্কার। integration ও selection code review করা হয়েছে। | **FIXED** |
| মাঝারি | browser cookie DB locked, missing/malformed cookies, bot verification, visitor data, PO token, format, network, 429/5xx, missing FFmpeg ও merge error একসঙ্গে দেখানো হতো। | পৃথক failure category, actionable next step, redacted technical detail dialog যোগ হয়েছে। supplied screenshots-এর Chrome DB-copy failure এবং YouTube bot-verification case-কে আলাদা করে। CAPTCHA/auth bypass বা browser protection দুর্বল করা হয়নি। | **FIXED; unit-tested** |
| মাঝারি | child process cancellation ও Unicode output নির্ভরযোগ্যভাবে শেষ নাও হতে পারত। | process tree terminate করে wait, UTF-8 output drain; Bengali output ও cancellation integration test করা হয়েছে। | **FIXED; integration-tested** |
| মাঝারি | browser extension cookie expiration metadata bridge-এ হারিয়ে যেত এবং expired cookie পাঠানো যেত। | expiry ও scope metadata forward হয়; expired/unsupported URL cookie বাদ যায়। bridge/model tests আছে। | **FIXED** |
| মাঝারি | tool discovery ambient `PATH` থেকে ভুল executable চালাতে পারত; proxy secret process arguments/log-এ পড়তে পারত। | app-local yt-dlp/FFmpeg resolve, restricted child `PATH`, URL stdin দিয়ে পাঠানো, authenticated proxy child environment-এ; diagnostic redaction tests আছে। | **MITIGATED** |
| মাঝারি | extension ZIP overwrite হতে পারত; clean source checkout ও optional tool behavior যাচাইয়ের পথ অসম্পূর্ণ ছিল। | packer unique archive name নেয়; clean workspace script source-only app, metadata, MSIX (MSBuild থাকলে), .NET, integration ও extension test এবং extension pack চালায়। | **FIXED; locally verified** |
| কম | nullable warning ও app/MSIX version drift regression আড়াল করতে পারত। | clean Release build-এ zero warning; package metadata check desktop `1.0.13` ও MSIX `1.0.13.0` মিলিয়ে যাচাই করেছে। | **FIXED; verified** |
| মাঝারি | SFTP first-use host key নীরবে trust-on-first-use করে। | changed key প্রত্যাখ্যান হয়, তবে প্রথম trust-এর জন্য explicit user confirmation নেই। | **DEFERRED** |

## Screenshot থেকে পাওয়া প্রমাণ

- **Image #1:** Chrome cookie database copy ব্যর্থ হয়। UI/browser database lock diagnosis দেয় এবং browser/background process বন্ধ করা বা user-selected cookies.txt ব্যবহারের পরামর্শ দেয়। FastDM browser protection দুর্বল করে না বা browser process বন্ধ করে না।
- **Image #2:** YouTube visitor verification/sign-in চায়। এটি cookie database error থেকে আলাদা দেখানো হয়। browser-এ verification শেষ করে user চাইলে supported cookie source নির্বাচন করতে পারেন; FastDM CAPTCHA, authentication বা DRM bypass করে না।

## যাচাইয়ের ফল

| কমান্ড / artifact | ফল |
| --- | --- |
| `powershell -ExecutionPolicy Bypass -File scripts/Test-CleanWorkspace.ps1` | **PASS**; source-only temporary copy-তে সব build, package ও test সফল; script শেষে শুধু সেই temporary copy মুছে ফেলা হয়েছে। |
| `dotnet build FastDM/FastDM.csproj -c Release -p:Platform=x64` (clean copy) | **PASS**; 0 warning, 0 error। |
| package manifest/version check (clean copy) | **PASS**; app `1.0.13`, MSIX `1.0.13.0`; required assets যাচাই হয়েছে। yt-dlp, Deno, QJS, FFmpeg ও ffprobe optional tool clean copy-তে অনুপস্থিত ছিল। |
| `dotnet run --project FastDM.Tests -c Release` (clean copy) | **PASS**; 292 checks, 0 failure। |
| `dotnet run --project FastDM.IntegrationTests -c Release` (clean copy) | **PASS**; 12 process/loopback checks, 0 failure। |
| `npm test` (clean copy) | **PASS**; 20 tests, 0 failure। |
| `FastDM.Extension/tools/pack.ps1` (clean copy) | **PASS**; `FastDM-Extension-0.1.0.zip`, 33,273 bytes। |
| Visual Studio 18 `MSBuild.exe FastDM.Package/FastDM.Package.wapproj` (clean copy) | **PASS**; `/p:GenerateTemporaryStoreCertificate=false` দিয়ে 73,240,889 byte unsigned `FastDM.Package_1.0.13.0_x64.msixbundle` এবং upload package তৈরি হয়েছে। Installation, signing, Store submission বা deployment করা হয়নি। |
| `scripts/Test-Project.ps1` | Debug build-এ চলমান FastDM process `PID 7636` executable lock করায় blocked হয়েছিল। process বন্ধ করা হয়নি; clean copy-এর Release build ও পূর্ণ suite দিয়ে যাচাই করা হয়েছে। |
| `git diff --check` | **PASS**; whitespace error নেই। Git line-ending conversion notices দেখিয়েছে edited C# ও Markdown ফাইলে; contents বদলানো বা error নয়। |

## অবশিষ্ট সীমাবদ্ধতা

- clean checkout-এ optional yt-dlp/Deno/QJS/FFmpeg/ffprobe নেই; তাই বাস্তব YouTube extraction, cookie login, FFmpeg merge/conversion চালানো হয়নি। Live site/network test-ও করা হয়নি।
- preview dialog-এর manual Windows UI pass—DPI 100/125/150/200%, ছোট window, keyboard-only, screen reader, theme/translation—করা হয়নি। preview/parser/retry logic ও app compilation পরীক্ষিত; visual accessibility এখনো manual review চায়।
- Chrome/Edge install ও end-to-end pairing, real browser cookie export/profile lock, `fastdm://` activation, SFTP/HLS/DASH server behavior বাস্তব environment-এ চালানো হয়নি।
- signed URL query স্থানীয় download state-এ থাকতে পারে, কারণ server resume-এর জন্য তা দরকার হতে পারে; log ও diagnostic-এ query value redacted। authenticated proxy secret yt-dlp child চলাকালে environment-এ থাকে এবং উচ্চ privilege-সহ local process inspection-এ দেখা যেতে পারে।
- clean MSIX unsigned; local FFmpeg/ffprobe-এর provenance যাচাই করা হয়নি। SFTP first-use trust behavior deferred। এই build signed/distribution-ready release নয়।

## পরিবর্তিত ফাইলের পরিসর

এই remediation-এ `FastDM/`-এর YouTube preview/engine/diagnostics/UI, `FastDM.Tests/`, `FastDM.IntegrationTests/`, `scripts/Test-CleanWorkspace.ps1`, `docs/TESTING.md` এবং এই report পরিবর্তিত হয়েছে; `FastDM.slnx`-এর পূর্ববর্তী user diff অক্ষত আছে। Master plan এই report দিয়ে প্রতিস্থাপিত নয়।
