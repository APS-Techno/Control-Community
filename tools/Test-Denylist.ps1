[CmdletBinding()]
param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot),
    [string]$DenylistDir = $env:APS_COMMUNITY_DENYLIST
)

$ErrorActionPreference = 'Stop'

if (-not $DenylistDir -or -not (Test-Path -LiteralPath (Join-Path $DenylistDir 'denylist.txt'))) {
    Write-Output 'Denylist not found: pass -DenylistDir or set APS_COMMUNITY_DENYLIST to a folder containing denylist.txt'
    exit 2
}

$Root = (Resolve-Path -LiteralPath $Root).Path
$skipDirs = @('bin', 'obj', '.git', '.vs', 'TestResults')

function Read-Patterns([string]$Name) {
    $path = Join-Path $DenylistDir $Name
    if (-not (Test-Path -LiteralPath $path)) { return @() }
    @(Get-Content -LiteralPath $path | ForEach-Object { $_.Trim() } |
        Where-Object { $_ -ne '' -and -not $_.StartsWith('#') } |
        ForEach-Object { [regex]::new($_, 'IgnoreCase') })
}

$deny = Read-Patterns 'denylist.txt'
$allow = Read-Patterns 'denylist-allow.txt'
if ($deny.Count -eq 0) {
    Write-Output 'Denylist has no patterns: denylist.txt is empty'
    exit 2
}
$documentExt = @('pdf', 'ppt', 'pptx', 'doc', 'docx', 'xls', 'xlsx', 'zip', '7z', 'rar', 'msg')
$hits = 0

function Test-Allowed([string]$Text) {
    foreach ($a in $allow) { if ($a.IsMatch($Text)) { return $true } }
    $false
}

function Get-DiskFiles([string]$Dir) {
    foreach ($item in Get-ChildItem -LiteralPath $Dir -Force) {
        if ($item.PSIsContainer) {
            if ($skipDirs -notcontains $item.Name) { Get-DiskFiles $item.FullName }
        }
        else { $item.FullName.Substring($Root.Length).TrimStart('\', '/').Replace('\', '/') }
    }
}

function Get-ScanFiles {
    [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
    $inGit = $false
    if (Get-Command git -ErrorAction SilentlyContinue) {
        $null = & git -C $Root rev-parse --is-inside-work-tree 2>$null
        $inGit = ($LASTEXITCODE -eq 0)
    }
    if ($inGit) {
        & git -C $Root -c core.quotepath=false ls-files -co --exclude-standard |
            Where-Object { Test-Path -LiteralPath (Join-Path $Root $_) -PathType Leaf }
    }
    else { Get-DiskFiles $Root }
}

foreach ($rel in Get-ScanFiles) {
    if (-not (Test-Allowed $rel)) {
        foreach ($d in $deny) {
            if ($d.IsMatch($rel)) { "${rel}:0: $d"; $hits++ }
        }
    }

    $bytes = [IO.File]::ReadAllBytes((Join-Path $Root $rel))
    $probe = [Math]::Min($bytes.Length, 8192)
    $isBinary = $false
    for ($i = 0; $i -lt $probe; $i++) { if ($bytes[$i] -eq 0) { $isBinary = $true; break } }
    if ($isBinary) {
        if ($documentExt -contains [IO.Path]::GetExtension($rel).TrimStart('.').ToLowerInvariant() -and -not (Test-Allowed $rel)) {
            "${rel}:0: unscannable document"
            $hits++
        }
        continue
    }

    $lines = [Text.Encoding]::UTF8.GetString($bytes) -split "`r?`n"
    for ($n = 0; $n -lt $lines.Length; $n++) {
        $line = $lines[$n]
        if (Test-Allowed $line) { continue }
        foreach ($d in $deny) {
            if ($d.IsMatch($line)) { "${rel}:$($n + 1): $d"; $hits++ }
        }
    }
}

exit ([int]($hits -gt 0))
