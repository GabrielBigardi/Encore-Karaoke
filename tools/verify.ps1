param(
    [ValidateSet('win-x64','win-x86')]
    [string]$Runtime = 'win-x64',
    [switch]$Audio
)
$ErrorActionPreference = 'Stop'
$encoreRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$encoreBinary = Join-Path $encoreRoot ('dist\Encore-' + $Runtime + '.exe')
if (-not (Test-Path -LiteralPath $encoreBinary)) { throw 'Run tools/build.ps1 first.' }
$encoreReports = Join-Path $encoreRoot 'artifacts'
$encoreIsolated = Join-Path $encoreRoot ('.build\isolated-' + $Runtime)
New-Item -ItemType Directory -Path $encoreReports,$encoreIsolated -Force | Out-Null
Copy-Item -LiteralPath $encoreBinary -Destination (Join-Path $encoreIsolated 'Encore.exe') -Force
$encoreExecutable = Join-Path $encoreIsolated 'Encore.exe'
# Self-contained hosts must work without using a separately installed runtime.
$env:DOTNET_ROOT = Join-Path $encoreRoot '.build\nonexistent-runtime'
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
$env:DOTNET_ROOT_X86 = $env:DOTNET_ROOT
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $encoreRoot ('.build\bundle-' + $Runtime)
$encoreChecks = @('self-test','ui-test')
if ($Audio) { $encoreChecks += 'audio-test' }
foreach ($encoreCheck in $encoreChecks) {
    $encoreReport = Join-Path $encoreReports ($Runtime + '-' + $encoreCheck + '.json')
    $encoreData = Join-Path $encoreRoot ('.build\verify-' + $Runtime + '-' + $encoreCheck + '-' + [Guid]::NewGuid().ToString('N'))
    $encoreError = Join-Path $encoreReports ($Runtime + '-' + $encoreCheck + '-error.txt')
    $encoreArguments = @(('--' + $encoreCheck), ('--report="' + $encoreReport + '"'), ('--data-dir="' + $encoreData + '"'), ('--error-log="' + $encoreError + '"'))
    $encoreProcess = Start-Process -FilePath $encoreExecutable -ArgumentList $encoreArguments -WindowStyle Hidden -PassThru
    if (-not $encoreProcess.WaitForExit(45000)) { $encoreProcess.Kill(); throw "Timed out: $encoreCheck" }
    if ($encoreProcess.ExitCode -ne 0) {
        if (Test-Path -LiteralPath $encoreReport) { Get-Content -LiteralPath $encoreReport -Encoding utf8 }
        if (Test-Path -LiteralPath $encoreError) { Get-Content -LiteralPath $encoreError -Encoding utf8 }
        throw "Check failed: $encoreCheck (exit $($encoreProcess.ExitCode))"
    }
    $encoreResult = Get-Content -LiteralPath $encoreReport -Encoding utf8 -Raw | ConvertFrom-Json
    if ($encoreResult.Failed -gt 0 -or ($encoreCheck -eq 'audio-test' -and -not $encoreResult.Passed)) { throw "Report failed: $encoreCheck" }
    Write-Output ($Runtime + ' / ' + $encoreCheck + ': passed')
}
