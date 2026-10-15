$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path -Parent $PSScriptRoot
$files = & git -C $sourceRoot ls-files --cached --others --exclude-standard
if ($LASTEXITCODE -ne 0) { throw 'git ls-files failed while preparing the clean workspace.' }

$tempParent = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$tempRoot = Join-Path $tempParent ("FastDM-clean-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot | Out-Null
try {
  foreach ($relative in $files) {
    if ([string]::IsNullOrWhiteSpace($relative)) { continue }
    $source = Join-Path $sourceRoot ($relative.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
    $destination = Join-Path $tempRoot ($relative.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
    $parent = Split-Path -Parent $destination
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination
  }

  Push-Location $tempRoot
  try {
    Write-Host 'Building a clean workspace without ignored local tools or build outputs...'
    dotnet build 'FastDM/FastDM.csproj' -c Release -p:Platform=x64
    if ($LASTEXITCODE -ne 0) { throw "Clean workspace app build failed with exit code $LASTEXITCODE." }
    powershell -ExecutionPolicy Bypass -File 'scripts/Test-PackageManifest.ps1'
    if ($LASTEXITCODE -ne 0) { throw "Clean workspace package checks failed with exit code $LASTEXITCODE." }

    # Build MSIX only in this isolated copy so package outputs cannot replace
    # artifacts in the user's workspace.
    $msbuild = Get-Command 'MSBuild.exe' -ErrorAction SilentlyContinue
    if ($msbuild) { $msbuildPath = $msbuild.Source }
    else {
      $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
      if (Test-Path -LiteralPath $vswhere) {
        $msbuildPath = (& $vswhere -latest -products '*' -find 'MSBuild/**/Bin/MSBuild.exe' | Select-Object -First 1)
      }
      if (-not $msbuildPath) {
        $candidate = Join-Path $env:ProgramFiles 'Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe'
        if (Test-Path -LiteralPath $candidate) { $msbuildPath = $candidate }
      }
    }
    if ($msbuildPath -and (Test-Path -LiteralPath $msbuildPath)) {
      Write-Host 'Building the MSIX project in the isolated workspace...'
      & $msbuildPath 'FastDM.Package/FastDM.Package.wapproj' /m /p:Configuration=Release /p:Platform=x64 /p:GenerateTemporaryStoreCertificate=false /v:minimal
      if ($LASTEXITCODE -ne 0) { throw "Clean workspace MSIX build failed with exit code $LASTEXITCODE." }
      $bundle = Get-ChildItem -LiteralPath 'FastDM.Package/AppPackages' -Filter '*.msixbundle' -Recurse -File | Select-Object -First 1
      if (-not $bundle) { throw 'MSIX build succeeded but did not create an .msixbundle.' }
      Write-Host "MSIX package build passed: $($bundle.Name) ($($bundle.Length) bytes)."
    }
    else { Write-Host 'MSIX package build skipped: Visual Studio MSBuild was not found.' }

    dotnet run --project 'FastDM.Tests' -c Release
    if ($LASTEXITCODE -ne 0) { throw "Clean workspace regression tests failed with exit code $LASTEXITCODE." }
    dotnet run --project 'FastDM.IntegrationTests' -c Release
    if ($LASTEXITCODE -ne 0) { throw "Clean workspace integration tests failed with exit code $LASTEXITCODE." }
    Push-Location 'FastDM.Extension'
    try {
      npm test
      if ($LASTEXITCODE -ne 0) { throw "Clean workspace extension tests failed with exit code $LASTEXITCODE." }
      powershell -ExecutionPolicy Bypass -File 'tools/pack.ps1'
      if ($LASTEXITCODE -ne 0) { throw "Clean workspace extension package failed with exit code $LASTEXITCODE." }
      $extensionZip = Get-ChildItem -LiteralPath 'dist' -Filter 'FastDM-Extension-*.zip' -File | Select-Object -First 1
      if (-not $extensionZip) { throw 'Extension packer succeeded but did not create an archive.' }
      Write-Host "Extension package passed: $($extensionZip.Name) ($($extensionZip.Length) bytes)."
    }
    finally { Pop-Location }
  }
  finally { Pop-Location }
}
finally {
  $resolvedParent = [System.IO.Path]::GetFullPath((Split-Path -Parent $tempRoot))
  if ($resolvedParent -ne $tempParent.TrimEnd([System.IO.Path]::DirectorySeparatorChar)) {
    throw "Refusing to remove a temporary path outside the OS temp directory: $tempRoot"
  }
  if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force }
}

Write-Host 'Clean workspace build and tests passed.'
