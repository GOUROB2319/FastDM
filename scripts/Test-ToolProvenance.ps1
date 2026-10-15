param([switch]$ForRelease)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$toolDir = Join-Path $root 'FastDM/Tools'
$lockPath = Join-Path $root 'docs/tool-bundle.lock.json'
$lock = Get-Content -LiteralPath $lockPath -Raw -Encoding utf8 | ConvertFrom-Json
$failures = [System.Collections.Generic.List[string]]::new()
$verifiedCount = 0

function Get-ToolVersionOutput([string]$Executable, [string]$Argument) {
  $start = [System.Diagnostics.ProcessStartInfo]::new()
  $start.FileName = $Executable
  $start.UseShellExecute = $false
  $start.CreateNoWindow = $true
  $start.RedirectStandardOutput = $true
  $start.RedirectStandardError = $true
  $start.Arguments = '"' + $Argument.Replace('"', '\"') + '"'
  $process = [System.Diagnostics.Process]::Start($start)
  $stdoutTask = $process.StandardOutput.ReadToEndAsync()
  $stderrTask = $process.StandardError.ReadToEndAsync()
  $process.WaitForExit()
  $output = $stdoutTask.GetAwaiter().GetResult() + $stderrTask.GetAwaiter().GetResult()
  return @{ ExitCode = $process.ExitCode; Output = $output }
}

foreach ($tool in $lock.tools) {
  $path = Join-Path $toolDir $tool.name
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
    if ($ForRelease -and $tool.requiredForRelease) { $failures.Add("Required release tool is missing: $($tool.name)") }
    else { Write-Host "Optional local tool absent: $($tool.name)" }
    continue
  }

  $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
  if ($actual -ne $tool.sha256) {
    $failures.Add("$($tool.name) SHA-256 mismatch (expected $($tool.sha256), got $actual)")
    continue
  }
  Write-Host "Hash matches audit lock: $($tool.name)"

  if ($tool.version) {
    $versionArgument = if ($tool.name -in @('ffmpeg.exe', 'ffprobe.exe')) { '-version' } else { '--version' }
    $versionResult = Get-ToolVersionOutput $path $versionArgument
    if ($versionResult.ExitCode -ne 0 -or $versionResult.Output -notmatch [regex]::Escape($tool.version)) {
      $failures.Add("$($tool.name) version does not match lock version $($tool.version)")
    }
    else { Write-Host "Version matches audit lock: $($tool.name) $($tool.version)" }
  }

  if ($tool.name -eq 'deno.exe') {
    $signature = Get-AuthenticodeSignature -LiteralPath $path
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Deno Land') {
      $failures.Add('deno.exe does not have the expected valid Deno Land Authenticode signature')
    }
    else { Write-Host 'Authenticode signature valid: deno.exe (Deno Land)' }
  }

  if ($tool.provenance -match 'unverified') {
    Write-Warning "$($tool.name) is hash-identified but its upstream origin/redistribution provenance is unverified."
    if ($ForRelease) { $failures.Add("Release provenance is unverified for $($tool.name)") }
  }
  else { $verifiedCount++ }
}

if ($ForRelease -and $verifiedCount -eq 0) { $failures.Add('No release tool has independently verified provenance.') }
if ($failures.Count -gt 0) { throw ($failures -join [Environment]::NewLine) }
Write-Host "Local tool integrity checks passed ($verifiedCount upstream/signature verified entries)."
