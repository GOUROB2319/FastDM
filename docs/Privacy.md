---
Title: FastDM Privacy Policy
---

# Privacy Policy for FastDM

**Last updated: October 15, 2026**

FastDM is a Windows download manager developed by Gourob Saha.

## Information FastDM collects

FastDM does not send telemetry, analytics, advertising data, or download history to the developer. It does not require an online account.

FastDM does make network requests to services you choose to use. Those services receive the information required for the request, which can include the URL, your IP address, request headers, browser cookies, and credentials. Video downloads may use yt-dlp and the video site's own services. The extension sends a selected link and, only when enabled, matching browser cookies to the FastDM app on your computer.

## Data stored on your device

- The download list, settings, and URLs are stored in the FastDM state file under your Windows application data folder. URL query strings can contain signed links or access tokens; treat this file as private.
- Credentials embedded in a URL are removed before the URL is saved and are held in session memory only. Credentials entered in a login prompt can be saved only when you select **Remember**, which uses Windows Credential Manager. A saved proxy password is also held by Windows Credential Manager.
- Browser-extension cookies are held in memory for the selected request or download. They are not saved in the download list and are cleared when FastDM exits.
- yt-dlp can read a browser profile you select or a cookies.txt file you select. FastDM does not create or upload a cookie file. yt-dlp sends the relevant cookies to the video service as part of its request.
- Diagnostic logging is off by default. When enabled, logs stay on the device and FastDM redacts URL credentials, URL query and fragment values, cookies, authorization values, common token/password fields, and Windows user-profile paths. Do not share logs without reviewing them.

## Network connections and updates

FastDM connects directly to the download URLs and services you request, using your configured proxy when applicable. FastDM does not relay downloads through a developer-operated server. The desktop app may check for app updates; the portable build may also use yt-dlp's upstream update mechanism when you request or enable it. The MSIX build receives app updates through its package distribution channel.

The privacy practices of the destination websites and any configured proxy apply to information sent to those services. Review their policies before sending them sensitive URLs, cookies, or credentials.

## Children's Privacy

FastDM does not knowingly collect personal information from children. Users should follow the rules that apply in their location and the terms of the services they access.

## Changes to this policy

This policy may be updated when FastDM's data handling changes. The current version is published with the project.

## Contact

Gourob Saha
Email: gourobsaha2319@gmail.com
