$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $root 'FastDM/FastDM.csproj'
$packageProject = Join-Path $root 'FastDM.Package/FastDM.Package.wapproj'
$manifestPath = Join-Path $root 'FastDM.Package/Package.appxmanifest'

[xml]$appXml = Get-Content -LiteralPath $appProject -Raw
[xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
$wapText = Get-Content -LiteralPath $packageProject -Raw
$appVersion = $appXml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$identityVersion = $manifest.Package.Identity.Version
$expectedIdentityVersion = "$appVersion.0"
if ($identityVersion -ne $expectedIdentityVersion) {
  throw "MSIX identity version '$identityVersion' does not match desktop version '$appVersion' (expected '$expectedIdentityVersion')."
}
if ($wapText -notmatch [regex]::Escape('Package.appxmanifest') -or
    $wapText -notmatch [regex]::Escape('..\FastDM\FastDM.csproj')) {
  throw 'MSIX project must include its manifest and reference the desktop app project.'
}

$assets = @(
  $manifest.Package.Properties.Logo,
  $manifest.Package.Applications.Application.VisualElements.Square150x150Logo,
  $manifest.Package.Applications.Application.VisualElements.Square44x44Logo,
  $manifest.Package.Applications.Application.VisualElements.DefaultTile.Wide310x150Logo,
  $manifest.Package.Applications.Application.VisualElements.DefaultTile.Square71x71Logo,
  $manifest.Package.Applications.Application.VisualElements.DefaultTile.Square310x310Logo,
  $manifest.Package.Applications.Application.VisualElements.SplashScreen.Image
) | Where-Object { $_ }

foreach ($asset in $assets) {
  $relative = $asset.Replace('\', [System.IO.Path]::DirectorySeparatorChar)
  $full = Join-Path (Join-Path $root 'FastDM.Package') $relative
  $variants = @(Get-ChildItem -LiteralPath ([System.IO.Path]::GetDirectoryName($full)) -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like ([System.IO.Path]::GetFileNameWithoutExtension($full) + '.scale-*.png') })
  if (-not (Test-Path -LiteralPath $full) -and $variants.Count -eq 0) {
    throw "Manifest asset has no source file or scale variants: $asset"
  }
}

$missingContent = @()
foreach ($match in [regex]::Matches($wapText, '<Content\s+Include="([^"]+)"')) {
  $relative = $match.Groups[1].Value.Replace('\', [System.IO.Path]::DirectorySeparatorChar)
  if (-not (Test-Path -LiteralPath (Join-Path (Join-Path $root 'FastDM.Package') $relative))) {
    $missingContent += $match.Groups[1].Value
  }
}
if ($missingContent.Count -gt 0) {
  throw "MSIX project references missing content: $($missingContent -join ', ')"
}

$outputTools = Get-ChildItem -LiteralPath (Join-Path $root 'FastDM/bin') -Directory -Recurse -Filter 'Tools' -ErrorAction SilentlyContinue |
  Sort-Object LastWriteTime -Descending | Select-Object -First 1
$localTools = Join-Path $root 'FastDM/Tools'
foreach ($tool in @('yt-dlp.exe', 'deno.exe', 'qjs.exe', 'ffmpeg.exe', 'ffprobe.exe')) {
  $source = Join-Path $localTools $tool
  $copied = if ($outputTools) { Join-Path $outputTools.FullName $tool } else { '' }
  if (Test-Path -LiteralPath $source) {
    if (-not $outputTools -or -not (Test-Path -LiteralPath $copied)) { throw "Local tool '$tool' exists but was not copied to an app output directory." }
    Write-Host "Tool present and copied: $tool"
  }
  else {
    Write-Host "Optional local tool absent: $tool"
  }
}

Write-Host "Package manifest and version checks passed ($appVersion / $identityVersion)."
