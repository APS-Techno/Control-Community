BeforeAll {
    $script:OriginalDenylistEnv = $env:APS_COMMUNITY_DENYLIST
    $script:Scanner = Join-Path $PSScriptRoot '..\Test-Denylist.ps1'
    $script:Patterns = @(
        'SECRET_[A-Z]+'
        '\bForbiddenType\b'
        'Fake\.Closed\.(csproj|dll)'
        '10\.250\.'
        '192\.250\.1\.(?!208\b)\d+'
        'Q:[\\/]+Lab[\\/]'
    )

    function New-Temp {
        $p = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $p | Out-Null
        $p
    }

    function Invoke-Scanner([string]$Root, [string]$DenyDir) {
        $out = & pwsh -NoProfile -File $script:Scanner -Root $Root -DenylistDir $DenyDir
        [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = @($out) }
    }

    function Invoke-Scan([string]$Content, [string[]]$Allow = @(), [string[]]$Patterns = $script:Patterns) {
        $root = New-Temp
        $deny = New-Temp
        try {
            Set-Content -LiteralPath (Join-Path $root 'sample.txt') -Value $Content -Encoding utf8
            Set-Content -LiteralPath (Join-Path $deny 'denylist.txt') -Value $Patterns
            Set-Content -LiteralPath (Join-Path $deny 'denylist-allow.txt') -Value $Allow
            Invoke-Scanner $root $deny
        }
        finally { Remove-Item -LiteralPath $root, $deny -Recurse -Force }
    }

    function Invoke-InTree([scriptblock]$Setup, [switch]$Git) {
        $root = New-Temp
        $deny = New-Temp
        try {
            Set-Content -LiteralPath (Join-Path $deny 'denylist.txt') -Value $script:Patterns
            if ($Git) { $null = & git -C $root init -q }
            & $Setup $root
            Invoke-Scanner $root $deny
        }
        finally { Remove-Item -LiteralPath $root, $deny -Recurse -Force }
    }
}

Describe 'Test-Denylist' {
    It 'flags <Text>' -ForEach @(
        @{ Text = 'x = SECRET_KEY' }
        @{ Text = 'new ForbiddenType(' }
        @{ Text = '<ProjectReference Include="..\Fake.Closed.csproj"' }
        @{ Text = 'Fake.Closed.dll' }
        @{ Text = '10.250.92.41' }
        @{ Text = '192.250.1.20' }
        @{ Text = 'Q:\Lab\Data' }
        @{ Text = 'Q:/Lab/Data' }
        @{ Text = 'secret_key' }
    ) {
        (Invoke-Scan $Text).ExitCode | Should -Be 1
    }

    It 'passes <Text>' -ForEach @(
        @{ Text = 'namespace Fake.Closed.Domain;' }
        @{ Text = 'class BaseForbiddenType' }
        @{ Text = '192.250.1.208' }
        @{ Text = 'int clean = 1;' }
    ) {
        (Invoke-Scan $Text).ExitCode | Should -Be 0
    }

    It 'prints path:line: pattern' {
        (Invoke-Scan "ok`nSECRET_A").Output | Should -Contain 'sample.txt:2: SECRET_[A-Z]+'
    }

    It 'ignores comment lines with leading spaces in the denylist' {
        (Invoke-Scan 'text' -Patterns @('  # SECRET_X', 'text2')).ExitCode | Should -Be 0
    }

    It 'skips lines matching an allow pattern' {
        (Invoke-Scan 'SECRET_A allowed-here' -Allow @('allowed-here')).ExitCode | Should -Be 0
    }

    It 'exits 2 when no denylist folder is given' {
        $root = New-Temp
        try {
            $env:APS_COMMUNITY_DENYLIST = ''
            $out = & pwsh -NoProfile -File $script:Scanner -Root $root
            $LASTEXITCODE | Should -Be 2
            @($out).Count | Should -Be 1
        }
        finally {
            if ($null -eq $script:OriginalDenylistEnv) { Remove-Item Env:APS_COMMUNITY_DENYLIST -ErrorAction SilentlyContinue }
            else { $env:APS_COMMUNITY_DENYLIST = $script:OriginalDenylistEnv }
            Remove-Item -LiteralPath $root -Recurse -Force
        }
    }

    It 'exits 2 when the folder has no denylist.txt' {
        $root = New-Temp
        $deny = New-Temp
        try {
            $null = & pwsh -NoProfile -File $script:Scanner -Root $root -DenylistDir $deny
            $LASTEXITCODE | Should -Be 2
        }
        finally { Remove-Item -LiteralPath $root, $deny -Recurse -Force }
    }

    It 'reports a file whose name matches, including a binary one' {
        $r = Invoke-InTree {
            param($root)
            [IO.File]::WriteAllBytes((Join-Path $root 'SECRET_X.bin'), [byte[]](0, 83, 69))
        }
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Contain 'SECRET_X.bin:0: SECRET_[A-Z]+'
    }

    It 'ignores binary content' {
        $r = Invoke-InTree {
            param($root)
            [IO.File]::WriteAllBytes((Join-Path $root 'a.bin'), [byte[]](0) + [Text.Encoding]::ASCII.GetBytes('SECRET_A'))
        }
        $r.ExitCode | Should -Be 0
    }

    It 'reports a binary <Ext> document as unscannable' -ForEach @(
        @{ Ext = 'pdf' }, @{ Ext = 'pptx' }, @{ Ext = 'docx' }, @{ Ext = 'xlsx' }, @{ Ext = 'zip' }, @{ Ext = 'msg' }
    ) {
        $r = Invoke-InTree {
            param($root)
            [IO.File]::WriteAllBytes((Join-Path $root "doc.$Ext"), [byte[]](0, 1, 2))
        }
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Contain "doc.${Ext}:0: unscannable document"
    }

    It 'does not report a binary document whose path matches an allow pattern' {
        $root = New-Temp
        $deny = New-Temp
        try {
            [IO.File]::WriteAllBytes((Join-Path $root 'doc.pdf'), [byte[]](0, 1, 2))
            Set-Content -LiteralPath (Join-Path $deny 'denylist.txt') -Value $script:Patterns
            Set-Content -LiteralPath (Join-Path $deny 'denylist-allow.txt') -Value @('^doc\.pdf$')
            (Invoke-Scanner $root $deny).ExitCode | Should -Be 0
        }
        finally { Remove-Item -LiteralPath $root, $deny -Recurse -Force }
    }

    It 'does not report a text file with a document extension' {
        $r = Invoke-InTree {
            param($root)
            Set-Content -LiteralPath (Join-Path $root 'notes.doc') -Value 'plain'
        }
        $r.ExitCode | Should -Be 0
    }

    It 'exits 2 with one line when denylist.txt has no patterns' {
        $root = New-Temp
        $deny = New-Temp
        try {
            Set-Content -LiteralPath (Join-Path $deny 'denylist.txt') -Value @('', '# only a comment')
            $out = & pwsh -NoProfile -File $script:Scanner -Root $root -DenylistDir $deny
            $LASTEXITCODE | Should -Be 2
            @($out).Count | Should -Be 1
        }
        finally { Remove-Item -LiteralPath $root, $deny -Recurse -Force }
    }

    It 'skips <Dir> folders on disk' -ForEach @(
        @{ Dir = 'bin' }, @{ Dir = 'obj' }, @{ Dir = '.vs' }, @{ Dir = 'TestResults' }
    ) {
        $r = Invoke-InTree {
            param($root)
            $d = Join-Path $root $Dir
            New-Item -ItemType Directory -Path $d | Out-Null
            Set-Content -LiteralPath (Join-Path $d 'f.txt') -Value 'SECRET_A'
        }
        $r.ExitCode | Should -Be 0
    }

    It 'scans only git-listed files inside a git work tree' {
        $r = Invoke-InTree -Git {
            param($root)
            Set-Content -LiteralPath (Join-Path $root '.gitignore') -Value 'ignored.txt'
            Set-Content -LiteralPath (Join-Path $root 'ignored.txt') -Value 'SECRET_A'
            Set-Content -LiteralPath (Join-Path $root 'kept.txt') -Value 'fine'
        }
        $r.ExitCode | Should -Be 0
    }

    It 'still reports an untracked, non-ignored file in a git work tree' {
        $r = Invoke-InTree -Git {
            param($root)
            Set-Content -LiteralPath (Join-Path $root 'new.txt') -Value 'SECRET_A'
        }
        $r.ExitCode | Should -Be 1
    }

    It 'reports a non-ASCII file name in git mode' {
        $r = Invoke-InTree -Git {
            param($root)
            Set-Content -LiteralPath (Join-Path $root ('SECRET_A' + [char]0x0416 + '.txt')) -Value 'fine'
        }
        $r.ExitCode | Should -Be 1
    }

    It 'leaves APS_COMMUNITY_DENYLIST as it found it' {
        $env:APS_COMMUNITY_DENYLIST | Should -Be $script:OriginalDenylistEnv
    }
}
