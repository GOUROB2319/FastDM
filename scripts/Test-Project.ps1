$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot

Push-Location $root
try {
    Write-Host 'Building the desktop app...'
    dotnet build 'FastDM/FastDM.csproj' -c Debug -p:Platform=x64
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

    Write-Host 'Core build and extension tests passed.'
}
finally {
    Pop-Location
}
