#requires -Version 7.0
param(
    [string]$Iscc,
    [switch]$AllowDirty,
    [switch]$NoSetup
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path $PSScriptRoot -Parent
$profiles = @('Sputter', 'ADS', 'Cluster')

function Invoke-Checked([string]$File, [string[]]$Arguments) {
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$File failed with exit code $LASTEXITCODE." }
}

function Resolve-Iscc {
    if ($Iscc) {
        if (Test-Path -LiteralPath $Iscc -PathType Leaf) { return $Iscc }
        throw "ISCC.exe not found at $Iscc."
    }
    $command = Get-Command 'ISCC.exe' -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) { return $command.Source }
    $candidates = @()
    if ($env:LOCALAPPDATA) { $candidates += Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe' }
    if (${env:ProgramFiles(x86)}) { $candidates += Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe' }
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }
    throw 'ISCC.exe not found; install Inno Setup 6 or pass -Iscc.'
}

$dirty = [bool](git -C $RepoRoot status --porcelain)
if ($dirty -and -not $AllowDirty) { throw 'Working tree is not clean; commit first or pass -AllowDirty.' }
$isccPath = if ($NoSetup) { $null } else { Resolve-Iscc }

$outRoot = Join-Path $RepoRoot '_OUT'
$stage = Join-Path $outRoot ('stage-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($stage)

Invoke-Checked 'dotnet' @('build', (Join-Path $RepoRoot 'APS.Community.slnx'), '-c', 'Release', "-p:ApsBinDir=$stage\bin\", '--nologo', '-v:minimal')

$bin = Join-Path $stage 'bin'
foreach ($name in @('tests', 'Logs')) {
    $path = Join-Path $bin $name
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
}
Get-ChildItem -LiteralPath $bin -Filter 'BlockEditorDemo.*' -File | Remove-Item -Force
Get-ChildItem -LiteralPath $bin -Recurse -Directory | Where-Object { $_.Name -in @('AuditLog', 'Snapshots') } |
    Remove-Item -Recurse -Force
$runtimes = Join-Path $bin 'runtimes'
if (Test-Path -LiteralPath $runtimes) {
    Get-ChildItem -LiteralPath $runtimes -Directory | Where-Object { $_.Name -notin @('win', 'win-x64') } |
        Remove-Item -Recurse -Force
}

Invoke-Checked 'dotnet' @('publish', (Join-Path $RepoRoot 'Mcp\APS.Mcp\APS.Mcp.csproj'), '-c', 'Release', '-r', 'win-x64',
    '--self-contained', 'false', '-o', (Join-Path $stage 'mcp'), '--nologo', '-v:minimal')

Get-ChildItem -LiteralPath $stage -Recurse -Filter '*.pdb' -File | Remove-Item -Force

$tracked = (git -C $RepoRoot ls-files -z -- ($profiles | ForEach-Object { "Configs/$_" })) -split "`0" | Where-Object { $_ }
if (-not $tracked) { throw 'No tracked profile files found.' }
foreach ($relative in $tracked) {
    $target = Join-Path $stage $relative
    [void][IO.Directory]::CreateDirectory((Split-Path $target -Parent))
    Copy-Item -LiteralPath (Join-Path $RepoRoot $relative) -Destination $target
}

[void][IO.Directory]::CreateDirectory((Join-Path $stage 'tools'))
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Enable-AgentAccess.ps1') -Destination (Join-Path $stage 'tools')
foreach ($name in @('LICENSE', 'NOTICE', 'THIRD-PARTY-NOTICES.md', 'README.md')) {
    Copy-Item -LiteralPath (Join-Path $RepoRoot $name) -Destination $stage
}

& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Test-ReleaseStage.ps1') -Stage $stage
if ($LASTEXITCODE -ne 0) { throw "Stage check failed; stage kept for inspection: $stage" }

if ($NoSetup) {
    Write-Output "Stage ready: $stage"
    return
}

$version = (Get-Item -LiteralPath (Join-Path $bin 'APS.ControlCommunity.exe')).VersionInfo.FileVersion
$outDir = Join-Path $outRoot $(if ($dirty) { 'Installer-dirty' } else { 'Installer' })
[void][IO.Directory]::CreateDirectory($outDir)
Get-ChildItem -LiteralPath $outDir -File | Where-Object { $_.Name -like 'ControlCommunitySetup-*.exe' -or $_.Name -eq 'SHA256SUMS.txt' } |
    Remove-Item -Force

$baseName = "ControlCommunitySetup-$version" + $(if ($dirty) { '-dirty' } else { '' })
Invoke-Checked $isccPath @("/DBuildStage=$stage", "/DMyAppVersion=$version", "/O$outDir", "/F$baseName", (Join-Path $PSScriptRoot 'ControlCommunitySetup.iss'))

$setup = Join-Path $outDir "$baseName.exe"
if (-not (Test-Path -LiteralPath $setup -PathType Leaf)) { throw "Installer not produced: $setup" }
$hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $outDir 'SHA256SUMS.txt'), "$hash  $(Split-Path $setup -Leaf)`n", [Text.UTF8Encoding]::new($false))

Remove-Item -LiteralPath $stage -Recurse -Force
Write-Output "Installer: $setup"
Write-Output "SHA256: $hash"
