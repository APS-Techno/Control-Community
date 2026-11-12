# APS Control Community

Open source Control and Emulator stack for teaching and experiments. It ships with three reference machines in `Configs/`. The Emulator simulates their hardware, so the full Control application runs on a desktop without a real machine. All devices talk over loopback TCP: no serial ports, no drivers, no licence file.

|Profile|Machine|Pressure wait|Deposit|
|---|---|---|---|
|`Sputter`|Single-chamber batch coater, two sources|none (`swait` only)|`deposit <source> <seconds>` (shutter valve)|
|`ADS`|Main chamber with load lock, five sources, rotary holder and shutter|`pwait <target> <timeout>` on the throttle controller `TC1`|`deposit <source> <seconds> <retract>`|
|`Cluster`|Transfer module, load lock and four process chambers with a transfer robot|`pwait <sensor> <target> <timeout>`|`deposit <source> <seconds>` (shutter valve)|

Demo devices (`Control/HW_Libraries/Demo`, emulated in `Emulator/Devices/Demo_Devices`) use line-based ASCII over TCP: `LanPowerSupply` (DC, pulsed DC, RF), `LanThrottleController`, `LanRotaryDrive`, `LanTransferRobot`.

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
