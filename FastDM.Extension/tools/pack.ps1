# স্টোরে জমা দেওয়ার জন্য এক্সটেনশন zip বানায় (tests/, tools/, package.json বাদ)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$manifest = Get-Content (Join-Path $root 'manifest.json') -Raw | ConvertFrom-Json
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist 'stage'
$zip = Join-Path $dist ("FastDM-Extension-" + $manifest.version + ".zip")

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null

foreach ($item in @('manifest.json', '_locales', 'src', 'assets')) {
  Copy-Item (Join-Path $root $item) -Destination $stage -Recurse -Force
}

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Remove-Item $stage -Recurse -Force
Write-Host "Created $zip"
