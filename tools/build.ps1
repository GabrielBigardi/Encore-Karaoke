param(
    [ValidateSet('all','win-x64','win-x86','win-arm64')]
    [string]$Runtime = 'all',
    [switch]$SkipRestore
)
$ErrorActionPreference = 'Stop'
$encoreRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$env:DOTNET_CLI_HOME = Join-Path $encoreRoot '.build\dotnet'
$env:NUGET_PACKAGES = Join-Path $encoreRoot '.build\packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$encoreProject = Join-Path $encoreRoot 'src\Encore\Encore.csproj'
$encoreRuntimes = if ($Runtime -eq 'all') { @('win-x64','win-x86','win-arm64') } else { @($Runtime) }
$encoreDist = Join-Path $encoreRoot 'dist'
New-Item -ItemType Directory -Path $encoreDist -Force | Out-Null
if (-not $SkipRestore) {
    & dotnet restore $encoreProject --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Package restore failed.' }
}
foreach ($encoreRid in $encoreRuntimes) {
    $encorePublish = Join-Path $encoreRoot ('.build\publish\' + $encoreRid)
    & dotnet publish $encoreProject -c Release -r $encoreRid --self-contained true --no-restore -o $encorePublish --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $encoreRid." }
    Copy-Item -LiteralPath (Join-Path $encorePublish 'Encore.exe') -Destination (Join-Path $encoreDist ('Encore-' + $encoreRid + '.exe')) -Force
}
Copy-Item -LiteralPath (Join-Path $encoreRoot 'USER_GUIDE.md') -Destination $encoreDist -Force
Copy-Item -LiteralPath (Join-Path $encoreRoot 'THIRD_PARTY_NOTICES.md') -Destination $encoreDist -Force
Copy-Item -LiteralPath (Join-Path $encoreRoot 'LICENSE') -Destination $encoreDist -Force
$encoreLicenses = Join-Path $encoreDist 'licenses'
New-Item -ItemType Directory -Path $encoreLicenses -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $encoreRoot 'licenses\NLayer-LICENSE.txt') -Destination $encoreLicenses -Force
Copy-Item -LiteralPath (Join-Path $encoreRoot '.build\packages\naudio\2.2.1\license.txt') -Destination (Join-Path $encoreLicenses 'NAudio-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $encoreRoot '.build\packages\nvorbis\0.10.4\LICENSE') -Destination (Join-Path $encoreLicenses 'NVorbis-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $encoreRoot '.build\packages\microsoft.netcore.app.runtime.win-x64\8.0.31\LICENSE.TXT') -Destination (Join-Path $encoreLicenses 'DotNet-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $encoreRoot '.build\packages\microsoft.netcore.app.runtime.win-x64\8.0.31\THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $encoreLicenses 'DotNet-THIRD-PARTY-NOTICES.txt') -Force
Copy-Item -LiteralPath (Join-Path $encoreRoot '.build\packages\microsoft.windowsdesktop.app.runtime.win-x64\8.0.31\LICENSE') -Destination (Join-Path $encoreLicenses 'WindowsDesktop-LICENSE.txt') -Force
$encoreHashes = Get-ChildItem -LiteralPath $encoreDist -Filter 'Encore-*.exe' | Get-FileHash -Algorithm SHA256 | ForEach-Object { $_.Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($_.Path) }
[IO.File]::WriteAllLines((Join-Path $encoreDist 'SHA256SUMS.txt'), [string[]]$encoreHashes)
Get-ChildItem -LiteralPath $encoreDist -Filter '*.exe' | Select-Object Name, @{Name='SizeMB';Expression={[Math]::Round($_.Length / 1MB,1)}}
