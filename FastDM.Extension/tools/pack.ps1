$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$manifest = Get-Content -LiteralPath (Join-Path $root 'manifest.json') -Raw | ConvertFrom-Json
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("FastDM-Extension-pack-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot | Out-Null
$stage = Join-Path $tempRoot 'stage'
$archive = Join-Path $tempRoot 'extension.zip'
$baseName = "FastDM-Extension-$($manifest.version)"
$destination = Join-Path $dist ($baseName + '.zip')
$suffix = 1
while (Test-Path -LiteralPath $destination) {
  $destination = Join-Path $dist ("$baseName-$suffix.zip")
  $suffix++
}

try {
  New-Item -ItemType Directory -Path $stage | Out-Null
  foreach ($item in @('manifest.json', '_locales', 'src', 'assets')) {
    $source = Join-Path $root $item
    if (-not (Test-Path -LiteralPath $source)) { throw "Required extension package path is missing: $item" }
    Copy-Item -LiteralPath $source -Destination $stage -Recurse
  }

  Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive
  [System.IO.File]::Move($archive, $destination)
  Write-Host "Created $destination"
}
finally {
  if (Test-Path -LiteralPath $tempRoot) {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force
  }
}
