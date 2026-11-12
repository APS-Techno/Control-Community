# APS Control Community

Open source Control and Emulator stack for teaching and experiments. It ships with three reference machines in `Configs/`. The Emulator simulates their hardware, so the full Control application runs on a desktop without a real machine. All devices talk over loopback TCP: no serial ports, no drivers, no licence file.

|Profile|Machine|Pressure wait|Deposit|
|---|---|---|---|
|`Sputter`|Single-chamber batch coater, two sources|none (`swait` only)|`deposit <source> <seconds>` (shutter valve)|
|`ADS`|Main chamber with load lock, five sources, rotary holder and shutter|`pwait <target> <timeout>` on the throttle controller `TC1`|`deposit <source> <seconds> <retract>`|
|`Cluster`|Transfer module, load lock and four process chambers with a transfer robot|`pwait <sensor> <target> <timeout>`|`deposit <source> <seconds>` (shutter valve)|

Demo devices (`Control/HW_Libraries/Demo`, emulated in `Emulator/Devices/Demo_Devices`) use line-based ASCII over TCP: `LanPowerSupply` (DC, pulsed DC, RF), `LanThrottleController`, `LanRotaryDrive`, `LanTransferRobot`.

## Install

Requirements: Windows 10 or 11 (x64) and the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0). Use the "Desktop Runtime", not the base "Runtime". The installer does not include .NET. If the runtime is missing, the installer tells you and gives the download link.

1. Download `ControlCommunitySetup-<version>.exe` and `SHA256SUMS.txt` from [GitHub Releases](https://github.com/APS-Techno/Control-Community/releases).
2. Check the download. In PowerShell, run `Get-FileHash .\ControlCommunitySetup-<version>.exe`. The hash must match the line in `SHA256SUMS.txt`.
3. Run the installer. It installs for the current user only and does not need administrator rights. The installer is not code-signed, so Windows SmartScreen can show a warning. Click "More info", then "Run anyway".

The installer puts the files in `%LOCALAPPDATA%\Programs\APS Control Community`:

|Folder|Contents|
|---|---|
|`bin`|Control, Emulator and plugins|
|`Configs`|The `Sputter`, `ADS` and `Cluster` profiles|
|`mcp`|The MCP bridge (`APS.Mcp.exe`)|
|`tools`|`Enable-AgentAccess.ps1`|
|`Backups`|Earlier copies of the profiles (made by an upgrade)|

First start:

1. Start "APS Emulator Community" from the Start menu.
2. In the Emulator, open "Open Project...", select `Sputter`, `ADS` or `Cluster`, and keep "Also point Control at this project" selected. This sets the same profile for Control.
3. Start "APS Control Community".

Upgrade: each install sets the three shipped profiles back to the version of the installer. Control writes calibration changes into `HWConf.xml`. Before the installer replaces the profiles, it copies them to `Backups\Configs-<date>-<time>\`. Your own profiles in other folders, the `AuditLog` and `Snapshots` folders, `bin\RootConfig.xml` and the agent settings do not change.

### Agent access

The agent server is off after installation. To turn it on, start "Enable agent access" from the Start menu. The script:

- turns on the agent server in `bin\AgentServer.xml` for the local computer only (`127.0.0.1`),
- makes a new random API key with the `Recipe` tier (read, validate, upload, start and abort recipes; no manual device commands),
- writes the key to `mcp\apsmcp.json` for the bridge,
- shows the command that registers the bridge with Claude Code: `claude mcp add aps-control -- "<install folder>\mcp\APS.Mcp.exe" --config "<install folder>\mcp\apsmcp.json"`.

Restart Control after you run the script. Each run makes a new key and replaces the previous one. To give the key the `Manual` tier (manual device commands too), run the script with `-Manual`:

```
powershell -NoProfile -ExecutionPolicy Bypass -File "%LOCALAPPDATA%\Programs\APS Control Community\tools\Enable-AgentAccess.ps1" -Manual
```

On the first connection the bridge accepts the server certificate and writes its SHA-256 fingerprint to stderr. To pin it, put the value in `certSha256` in `mcp\apsmcp.json`. Tools, tiers and the full bridge setup: [`Mcp/README.md`](Mcp/README.md).

Uninstall removes the program files, `mcp\apsmcp.json` and the agent certificate. It keeps `Backups`, logs and run data.

### Cite

If you use this software in research, cite the preprint in [`CITATION.cff`](CITATION.cff).

## Build

Requirements: Windows, .NET SDK 10.0.100 or later.

```
dotnet build APS.Community.slnx
```

The applications, the Emulator and the plugins write to `bin/`. Plugin assemblies go to the by-kind subfolders `bin/HW_LIB` (hardware drivers), `bin/GUI_LIB` (controls) and `bin/Workers` (process workers). Test projects write to `bin/tests`. Some projects use their own output folder: `Mcp`, `Wire` and `Production/Domain` (`bin/Debug/net10.0` in the project folder), the Icons generator, the Icons tests and the VacuumControls tests (a `bin` folder next to the project).

## Run

1. Start the Emulator: `bin/APS.EmulatorCommunity.exe --project Sputter` (or `ADS`, `Cluster`). Control reads its profile from `<Prefix>` in `bin/RootConfig.xml`; set it to the same name, or open the project from the Emulator's Open Project dialog with the Control sync option.
2. Start Control from `bin`: `bin/APS.ControlCommunity.exe`

Control looks for plugin assemblies in `HW_LIB`, `GUI_LIB` and `Workers` next to the executable. A missing or blank folder entry in `RootConfig.xml` falls back to those standard names.

## Tests

Run each test project with `dotnet test <project>`; the projects are `*Tests.csproj` under `Control/Tests`, `Emulator/Tests`, `Icons/Tests`, `Mcp`, `RecipeModel/Tests`, `VacuumControls/Tests` and `Wire`. The scanner tests need PowerShell 7 (`pwsh`) and Pester 5: `Invoke-Pester tools/tests`.

Two Emulator tests can hang. Run EmulatorTests with a hang timeout and this filter:

```
dotnet test Emulator/Tests/EmulatorTests/EmulatorTests.csproj --blame-hang-timeout 90s --filter "FullyQualifiedName!~Concurrent_SerialCommands_DuringTicks_NeverCorruptQueue&FullyQualifiedName!~TcpSerialFramingTests"
```

## Contributing

After cloning, enable the commit hook once: `git config core.hooksPath .githooks`. The hook builds the solution, runs the tests and, if the environment variable `APS_COMMUNITY_DENYLIST` is set, runs the denylist scan (`tools/Test-Denylist.ps1`). The variable is an optional maintainer setting. See AGENTS.md for the repository rules.

## License

Apache-2.0. See LICENSE, NOTICE and THIRD-PARTY-NOTICES.md.
