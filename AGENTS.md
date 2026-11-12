# Repository rules

- Branch: `master`. Never push without an explicit request from the owner.
- Hook: `git config core.hooksPath .githooks`. The pre-commit hook builds the solution, runs every test project, and runs the denylist scan when `APS_COMMUNITY_DENYLIST` is set. Never bypass it with `--no-verify`.
- Denylist gate: `tools/Test-Denylist.ps1` scans tracked and untracked files. The pattern files are private and are not in this repository. Set `APS_COMMUNITY_DENYLIST` to the folder that holds `denylist.txt`, or pass `-DenylistDir`. Exit 0 means clean.
- No closed names: do not add closed type or assembly names, register maps other than Sputter, keys, license records, private hosts, machine-local paths or customer names.
- Comments are not allowed by default. A comment must prove its right to exist.
- Emulator tests: run EmulatorTests with `--blame-hang-timeout 90s` and exclude `Concurrent_SerialCommands_DuringTicks_NeverCorruptQueue` and `TcpSerialFramingTests`; they hang.
- Before `dotnet build`, close running APS.ControlCommunity.exe and APS.EmulatorCommunity.exe.
- Keep each file's encoding and line endings.
