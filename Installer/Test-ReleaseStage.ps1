#requires -Version 7.0
param(
    [Parameter(Mandatory)][string]$Stage
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Stage = [IO.Path]::GetFullPath($Stage).TrimEnd([char[]]'\/')
$violations = [Collections.Generic.List[string]]::new()
function Add-Violation([string]$Reason, [string]$Path) {
    $violations.Add("STAGE: ${Reason}: $([IO.Path]::GetRelativePath($Stage, $Path))")
}

foreach ($relative in @(
        'bin\APS.ControlCommunity.exe', 'bin\APS.EmulatorCommunity.exe', 'bin\RootConfig.xml', 'bin\AgentServer.xml',
        'mcp\APS.Mcp.exe', 'mcp\apsmcp.sample.json', 'tools\Enable-AgentAccess.ps1')) {
    if (-not (Test-Path -LiteralPath (Join-Path $Stage $relative) -PathType Leaf)) { $violations.Add("STAGE: missing: $relative") }
}
foreach ($profileName in @('Sputter', 'ADS', 'Cluster')) {
    foreach ($name in @('HWConf.xml', 'HWConf.Emulator.xml')) {
        $relative = "Configs\$profileName\$name"
        if (-not (Test-Path -LiteralPath (Join-Path $Stage $relative) -PathType Leaf)) { $violations.Add("STAGE: missing: $relative") }
    }
}

$agentServer = Join-Path $Stage 'bin\AgentServer.xml'
if ((Test-Path -LiteralPath $agentServer -PathType Leaf) -and
    ([xml][IO.File]::ReadAllText($agentServer)).DocumentElement.GetAttribute('enabled') -ne 'false') {
    Add-Violation 'agent server not disabled' $agentServer
}

$forbiddenDirectories = @('tests', 'Logs', 'AuditLog', 'Snapshots')
$runtimes = Join-Path $Stage 'bin\runtimes'
foreach ($entry in Get-ChildItem -LiteralPath $Stage -Recurse -Force) {
    if ($entry.PSIsContainer) {
        if ($entry.Name -in $forbiddenDirectories) { Add-Violation 'forbidden folder' $entry.FullName }
        elseif ($entry.Name -match '^net\d' -and $entry.Name -ne 'net10.0' -and
            $entry.FullName.StartsWith($runtimes + '\', [StringComparison]::OrdinalIgnoreCase)) {
            Add-Violation 'foreign target framework' $entry.FullName
        }
        continue
    }
    if ($entry.Extension -eq '.pdb') { Add-Violation 'debug symbols' $entry.FullName }
    elseif ($entry.Name -like '*Tests*.dll') { Add-Violation 'test assembly' $entry.FullName }
    elseif ($entry.Name -like 'BlockEditorDemo*') { Add-Violation 'demo app' $entry.FullName }
    elseif ($entry.Name -eq 'apsmcp.json') { Add-Violation 'live bridge config' $entry.FullName }
}

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Output $_ }
    exit 1
}
Write-Output "Stage check passed: $Stage"
