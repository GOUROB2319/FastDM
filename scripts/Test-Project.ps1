$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot

Push-Location $root
try {
    Write-Host 'Checking for unexpected WinForms resource files...'
    $allowedResx = @('FastDM/UI/MainForm.resx', 'FastDM/UI/AddUrlForm.resx')
    $unexpectedResx = @()
    Get-ChildItem -LiteralPath (Join-Path $root 'FastDM') -Filter '*.resx' -Recurse -File |
        ForEach-Object {
            $relative = $_.FullName.Substring($root.Length).TrimStart([char[]]@([char]92, [char]47)).Replace([char]92, [char]47)
            if ($relative -match '(^|/)(obj|bin)/') { return }
            if ($relative -notin $allowedResx) { $unexpectedResx += $relative }
        }
    if ($unexpectedResx.Count -gt 0) {
        throw ('Unexpected .resx file(s) under FastDM: ' + ($unexpectedResx -join ', ') +
            '. Only FastDM/UI/MainForm.resx and FastDM/UI/AddUrlForm.resx are allowed.')
    }

    Write-Host 'Building the desktop app...'
    dotnet build 'FastDM/FastDM.csproj' -c Debug -p:Platform=x64
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host 'Checking MSIX manifest, app version, assets and optional tool copies...'
    powershell -ExecutionPolicy Bypass -File 'scripts/Test-PackageManifest.ps1'
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host 'Running unit, bridge and protocol regression checks...'
    dotnet run --project 'FastDM.Tests' -c Release
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host 'Running local HTTP download-engine integration tests...'
    dotnet run --project 'FastDM.IntegrationTests' -c Release
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host 'Running extension tests...'
    Push-Location 'FastDM.Extension'
    try {
        npm test
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    finally {
        Pop-Location
    }

    Write-Host 'Desktop build, package checks, regression tests and extension tests passed.'
}
finally {
    Pop-Location
}
