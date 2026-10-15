# Microsoft Store release checklist

This is a release gate, not a claim that the current package is ready to submit. Last checked: 2026-10-15. The Store Partner Center listing and certification account were not available during this audit.

## Build and identity

- [ ] Keep package identity `GourobSaha.FastDM` and publisher `CN=58FEFA1E-D2AC-4137-88CE-8D61C80321B7`; verify that Partner Center shows the same identity before submission.
- [ ] Keep app version in `FastDM/FastDM.csproj` as the numeric source of truth; the manifest identity version must remain `<app version>.0`. The current pair is `1.0.13` / `1.0.13.0`.
- [ ] Extension version is independently released (`0.1.0` currently). Confirm the Store description and extension listing explain the separate installation/update path.
- [ ] Build `FastDM.Package/FastDM.Package.wapproj` using Visual Studio MSBuild with the Desktop Bridge/MSIX Packaging Tools. Validate the x64 bundle, install it on a clean Windows machine, and retain the unsigned/signed artifact hashes.
- [ ] Test package install, update over the currently published version, protocol registration, startup task opt-in/opt-out, uninstall and user data retention.
- [ ] Confirm all manifest image variants, supported Windows versions, `internetClient` and `runFullTrust` declarations against actual app behavior. Do not change identity, publisher or capabilities casually.

## Third-party tools and listing disclosure

- [ ] Run `powershell -ExecutionPolicy Bypass -File scripts/Test-ToolProvenance.ps1 -ForRelease`; it must pass without `unverified` origins.
- [ ] Include pinned versions, hashes, architecture, upstream source/release record, Authenticode/checksum evidence, license texts, third-party notices, and required source offer for every executable and native DLL shipped in the bundle.
- [ ] The current local FFmpeg/ffprobe executables and DLLs fail this gate because their publisher/source and complete dependency notices/source are unknown. Replace them with a provenance-complete build before Store packaging.
- [ ] Decide whether optional video/merge tools are included. If they are a non-integrated dependency, disclose them at the beginning of the Store listing description per policy 10.2.4. Describe supported functions accurately and make no universal-site or guaranteed-download claim.
- [ ] Recheck current policy 10.2.4 and the [change history](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies-change-history) on the submission date. On 2026-10-15, policy 7.19 is effective; 7.20 is scheduled to take effect 2026-10-22.
- [ ] Verify the published privacy-policy URL and contact address in Partner Center. Check the policy against the actual Store build, including extension cookie permissions, local state, URL tokens, yt-dlp, optional tools, proxy and diagnostic logging.
- [ ] Confirm rights to distribute the extension, tool binaries, logos, third-party library notices, screenshots and sample media. Keep evidence in the release record.

## Certification and handoff

- [ ] Review Store screenshots, description, supported device/OS metadata, age rating, privacy declarations, package flight, certification notes and required dependency disclosure in Partner Center.
- [ ] Install and manually test the exact candidate package; run the clean-workspace and package-manifest checks from `docs/TESTING.md`.
- [ ] Record submission date and policy version. Microsoft certification timing and approval are outside this repository and cannot be promised.
