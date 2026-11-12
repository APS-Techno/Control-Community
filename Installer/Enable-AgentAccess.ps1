param(
    [string]$AppRoot,
    [switch]$Manual
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $AppRoot) { $AppRoot = Split-Path $PSScriptRoot -Parent }
$AppRoot = [IO.Path]::GetFullPath($AppRoot)
$serverPath = Join-Path $AppRoot 'bin\AgentServer.xml'
$samplePath = Join-Path $AppRoot 'mcp\apsmcp.sample.json'
$configPath = Join-Path $AppRoot 'mcp\apsmcp.json'
$bridgePath = Join-Path $AppRoot 'mcp\APS.Mcp.exe'

foreach ($path in @($serverPath, $samplePath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        [Console]::Error.WriteLine("Required file missing: $path")
        exit 1
    }
}

$bytes = New-Object byte[] 32
$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
$key = -join ($bytes | ForEach-Object { $_.ToString('x2') })

$sha = [Security.Cryptography.SHA256]::Create()
try {
    $hash = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($key))).Replace('-', '')
} finally { $sha.Dispose() }

$tier = if ($Manual) { 'Manual' } else { 'Recipe' }
$bind = '127.0.0.1'

$doc = New-Object System.Xml.XmlDocument
$doc.PreserveWhitespace = $true
$doc.Load($serverPath)
$server = $doc.DocumentElement
$server.SetAttribute('enabled', 'true')
$server.SetAttribute('bind', $bind)
if ($Manual) { $server.SetAttribute('allowManual', 'true') }

foreach ($old in @($server.SelectNodes("Key[@name='installer']"))) {
    $previous = $old.PreviousSibling
    if ($previous -is [System.Xml.XmlWhitespace]) { [void]$server.RemoveChild($previous) }
    [void]$server.RemoveChild($old)
}

$keyNode = $doc.CreateElement('Key')
$keyNode.SetAttribute('name', 'installer')
$keyNode.SetAttribute('hash', $hash)
$keyNode.SetAttribute('tier', $tier)
$anchor = $server.LastChild
if ($anchor -is [System.Xml.XmlWhitespace]) {
    [void]$server.InsertBefore($doc.CreateWhitespace("`r`n  "), $anchor)
    [void]$server.InsertBefore($keyNode, $anchor)
} else {
    [void]$server.AppendChild($doc.CreateWhitespace("`r`n  "))
    [void]$server.AppendChild($keyNode)
    [void]$server.AppendChild($doc.CreateWhitespace("`r`n"))
}

$utf8 = New-Object System.Text.UTF8Encoding($false)
$settings = New-Object System.Xml.XmlWriterSettings
$settings.Encoding = $utf8
$settings.OmitXmlDeclaration = -not ($doc.FirstChild -is [System.Xml.XmlDeclaration])
$writer = [System.Xml.XmlWriter]::Create($serverPath, $settings)
try { $doc.Save($writer) } finally { $writer.Dispose() }

$configSource = if (Test-Path -LiteralPath $configPath -PathType Leaf) { $configPath } else { $samplePath }
$config = Get-Content -LiteralPath $configSource -Raw | ConvertFrom-Json
$config.apiKey = $key
$config.host = $bind
$config.port = [int]$server.GetAttribute('port')
[IO.File]::WriteAllText($configPath, ($config | ConvertTo-Json), $utf8)

Write-Output "Agent access enabled. Tier: $tier."
Write-Output 'Restart APS Control Community if it is running.'
Write-Output 'Register the bridge with Claude Code:'
Write-Output ('claude mcp add aps-control -- "' + $bridgePath + '" --config "' + $configPath + '"')
