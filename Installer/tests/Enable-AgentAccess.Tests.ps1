BeforeAll {
    $script:Script = Join-Path $PSScriptRoot '..\Enable-AgentAccess.ps1'
    $script:RepoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent

    function Invoke-Enable([string]$Root, [string[]]$Extra = @()) {
        $out = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script:Script -AppRoot $Root @Extra 2>&1
        [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = (@($out) -join "`n") }
    }

    function Get-ServerXml([string]$Root) {
        [xml](Get-Content -LiteralPath (Join-Path $Root 'bin\AgentServer.xml') -Raw)
    }

    function Get-McpConfig([string]$Root) {
        Get-Content -LiteralPath (Join-Path $Root 'mcp\apsmcp.json') -Raw | ConvertFrom-Json
    }
}

Describe 'Enable-AgentAccess' {
    BeforeEach {
        $script:Root = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path (Join-Path $script:Root 'bin') | Out-Null
        New-Item -ItemType Directory -Path (Join-Path $script:Root 'mcp') | Out-Null
        $source = Get-Content -LiteralPath (Join-Path $script:RepoRoot 'Control\AgentServer.xml') -Raw
        $withOther = $source.Replace('</AgentServer>', "  <Key name=`"other`" hash=`"AB`" tier=`"Read`"/>`r`n</AgentServer>")
        [IO.File]::WriteAllText((Join-Path $script:Root 'bin\AgentServer.xml'), $withOther)
        Copy-Item -LiteralPath (Join-Path $script:RepoRoot 'Mcp\APS.Mcp\apsmcp.sample.json') -Destination (Join-Path $script:Root 'mcp\apsmcp.sample.json')
    }

    AfterEach {
        Remove-Item -LiteralPath $script:Root -Recurse -Force -ErrorAction SilentlyContinue
    }

    It 'enables the server on loopback with one Recipe key' {
        (Invoke-Enable $script:Root).ExitCode | Should -Be 0
        $server = (Get-ServerXml $script:Root).AgentServer
        $server.enabled | Should -Be 'true'
        $server.bind | Should -Be '127.0.0.1'
        $server.allowManual | Should -Be 'false'
        $keys = @($server.Key | Where-Object { $_.name -eq 'installer' })
        $keys.Count | Should -Be 1
        $keys[0].tier | Should -Be 'Recipe'
    }

    It 'writes a key whose hash Control accepts' {
        (Invoke-Enable $script:Root).ExitCode | Should -Be 0
        $apiKey = (Get-McpConfig $script:Root).apiKey
        $apiKey | Should -MatchExactly '^[0-9a-f]{64}$'
        $expected = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($apiKey)))
        $hash = @((Get-ServerXml $script:Root).AgentServer.Key | Where-Object { $_.name -eq 'installer' })[0].hash
        ($hash -ieq $expected) | Should -BeTrue
    }

    It 'copies host and port and leaves certSha256 null' {
        (Invoke-Enable $script:Root).ExitCode | Should -Be 0
        $config = Get-McpConfig $script:Root
        $config.host | Should -Be '127.0.0.1'
        $config.port | Should -Be 8026
        $config.certSha256 | Should -BeNullOrEmpty
        $config.downloadDir | Should -BeNullOrEmpty
        $config.PSObject.Properties.Name | Should -Contain 'certSha256'
    }

    It 'keeps other keys and comments' {
        (Invoke-Enable $script:Root).ExitCode | Should -Be 0
        @((Get-ServerXml $script:Root).AgentServer.Key | Where-Object { $_.name -eq 'other' }).Count | Should -Be 1
        Get-Content -LiteralPath (Join-Path $script:Root 'bin\AgentServer.xml') -Raw | Should -Match 'Agent MCP server\. Disabled by default'
    }

    It 'rotates the key on a second run' {
        (Invoke-Enable $script:Root).ExitCode | Should -Be 0
        $first = @((Get-ServerXml $script:Root).AgentServer.Key | Where-Object { $_.name -eq 'installer' })[0].hash
        (Invoke-Enable $script:Root).ExitCode | Should -Be 0
        $keys = @((Get-ServerXml $script:Root).AgentServer.Key | Where-Object { $_.name -eq 'installer' })
        $keys.Count | Should -Be 1
        $keys[0].hash | Should -Not -Be $first
        $apiKey = (Get-McpConfig $script:Root).apiKey
        $expected = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($apiKey)))
        ($keys[0].hash -ieq $expected) | Should -BeTrue
    }

    It 'keeps the certificate pin and download folder when it rotates the key' {
        (Invoke-Enable $script:Root).ExitCode | Should -Be 0
        $path = Join-Path $script:Root 'mcp\apsmcp.json'
        $config = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        $config.certSha256 = 'ABCDEF'
        $config.downloadDir = 'C:\Runs'
        [IO.File]::WriteAllText($path, ($config | ConvertTo-Json))
        (Invoke-Enable $script:Root).ExitCode | Should -Be 0
        $after = Get-McpConfig $script:Root
        $after.certSha256 | Should -Be 'ABCDEF'
        $after.downloadDir | Should -Be 'C:\Runs'
        $after.apiKey | Should -Not -Be $config.apiKey
    }

    It '-Manual grants Manual tier' {
        (Invoke-Enable $script:Root @('-Manual')).ExitCode | Should -Be 0
        $server = (Get-ServerXml $script:Root).AgentServer
        $server.allowManual | Should -Be 'true'
        @($server.Key | Where-Object { $_.name -eq 'installer' })[0].tier | Should -Be 'Manual'
    }

    It 'prints the claude mcp add line' {
        $result = Invoke-Enable $script:Root
        $result.ExitCode | Should -Be 0
        $line = 'claude mcp add aps-control -- "' + (Join-Path $script:Root 'mcp\APS.Mcp.exe') + '" --config "' + (Join-Path $script:Root 'mcp\apsmcp.json') + '"'
        $result.Output | Should -BeLike "*$line*"
    }

    It 'fails cleanly without AgentServer.xml' {
        Remove-Item -LiteralPath (Join-Path $script:Root 'bin\AgentServer.xml')
        (Invoke-Enable $script:Root).ExitCode | Should -Be 1
        Test-Path -LiteralPath (Join-Path $script:Root 'mcp\apsmcp.json') | Should -BeFalse
    }

    It 'script is ASCII' {
        $bytes = [IO.File]::ReadAllBytes($script:Script)
        @($bytes | Where-Object { $_ -ge 0x80 }).Count | Should -Be 0
    }
}
