BeforeAll {
    $script:Checker = Join-Path $PSScriptRoot '..\Test-ReleaseStage.ps1'
    $script:Required = @(
        'bin\APS.ControlCommunity.exe'
        'bin\APS.EmulatorCommunity.exe'
        'bin\RootConfig.xml'
        'bin\AgentServer.xml'
        'mcp\APS.Mcp.exe'
        'mcp\apsmcp.sample.json'
        'tools\Enable-AgentAccess.ps1'
        'Configs\Sputter\HWConf.xml'
        'Configs\Sputter\HWConf.Emulator.xml'
        'Configs\ADS\HWConf.xml'
        'Configs\ADS\HWConf.Emulator.xml'
        'Configs\Cluster\HWConf.xml'
        'Configs\Cluster\HWConf.Emulator.xml'
    )

    function Add-StageFile([string]$Stage, [string]$Relative, [string]$Content = '') {
        $path = Join-Path $Stage $Relative
        New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force | Out-Null
        [IO.File]::WriteAllText($path, $Content)
    }

    function New-ValidStage {
        $stage = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString('N'))
        foreach ($relative in $script:Required) { Add-StageFile $stage $relative }
        Add-StageFile $stage 'bin\AgentServer.xml' '<AgentServer enabled="false" port="8026" bind="127.0.0.1"/>'
        Add-StageFile $stage 'bin\runtimes\win\lib\net10.0\x.dll'
        $stage
    }

    function Invoke-Check([string]$Stage) {
        $out = & pwsh -NoProfile -File $script:Checker -Stage $Stage 2>&1
        [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = (@($out) -join "`n") }
    }
}

Describe 'Test-ReleaseStage' {
    BeforeEach { $script:Stage = New-ValidStage }
    AfterEach { Remove-Item -LiteralPath $script:Stage -Recurse -Force -ErrorAction SilentlyContinue }

    It 'accepts a valid stage' {
        (Invoke-Check $script:Stage).ExitCode | Should -Be 0
    }

    It 'rejects <Relative>' -ForEach @(
        @{ Relative = 'bin\tests\a.dll' }
        @{ Relative = 'bin\x.pdb' }
        @{ Relative = 'bin\CoreLogicTests.dll' }
        @{ Relative = 'bin\BlockEditorDemo.exe' }
        @{ Relative = 'bin\runtimes\win\lib\net9.0\System.IO.Ports.dll' }
        @{ Relative = 'bin\Logs\a.log' }
        @{ Relative = 'Configs\ADS\AuditLog\a.jsonl' }
        @{ Relative = 'Configs\ADS\Snapshots\a.emstate' }
        @{ Relative = 'mcp\apsmcp.json' }
    ) {
        Add-StageFile $script:Stage $Relative
        $result = Invoke-Check $script:Stage
        $result.ExitCode | Should -Be 1
        $result.Output | Should -Match ([regex]::Escape((Split-Path $Relative -Leaf)) + '|' + [regex]::Escape((Split-Path (Split-Path $Relative -Parent) -Leaf)))
    }

    It 'rejects a missing <Relative>' -ForEach @(
        @{ Relative = 'bin\APS.ControlCommunity.exe' }
        @{ Relative = 'bin\APS.EmulatorCommunity.exe' }
        @{ Relative = 'bin\RootConfig.xml' }
        @{ Relative = 'bin\AgentServer.xml' }
        @{ Relative = 'mcp\APS.Mcp.exe' }
        @{ Relative = 'mcp\apsmcp.sample.json' }
        @{ Relative = 'tools\Enable-AgentAccess.ps1' }
        @{ Relative = 'Configs\Sputter\HWConf.xml' }
        @{ Relative = 'Configs\ADS\HWConf.Emulator.xml' }
        @{ Relative = 'Configs\Cluster\HWConf.xml' }
    ) {
        Remove-Item -LiteralPath (Join-Path $script:Stage $Relative)
        $result = Invoke-Check $script:Stage
        $result.ExitCode | Should -Be 1
        $result.Output | Should -Match ([regex]::Escape($Relative))
    }

    It 'rejects an enabled AgentServer.xml' {
        Add-StageFile $script:Stage 'bin\AgentServer.xml' '<AgentServer enabled="true" port="8026" bind="127.0.0.1"/>'
        (Invoke-Check $script:Stage).ExitCode | Should -Be 1
    }
}
