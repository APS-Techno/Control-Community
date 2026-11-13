# Process panels for Community: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add generic `ChamberProcessPanel` and `ClusterProcessPanel` to Community, with all device-specific content declared in a per-profile `ProcessPanel.xml`.

**Architecture:** A pure spec loader (`ProcessPanelSpec`) in the Framework parses `ProcessPanel.xml`. The host publishes the parsed spec before it creates any panel, because panels are built with `Activator.CreateInstance` (parameterless) and `BaseProcessPanel` calls `DefineReadings()` in its constructor. The two panels in `APS.Controls.Generic` read the published spec. Both are ports of the Commercial panels with the hardcoded names removed.

**Tech Stack:** C# / .NET 10 WinForms, xUnit (`CoreLogicTests`), profile XML.

**Spec:** `docs/superpowers/specs/2026-11-13-process-panels-design.md`

## Global Constraints

- No closed type, assembly or device names in source. Device ids appear only in profile XML (and in tests as neutral ids such as `GAUGE_A`, `PSU-A`).
- No change to `Control-Commercial`, its `Community` pin, `IProcessPanel`, `IClusterProcessPanel` or `ClusterSnapshot`.
- Comments are not allowed by default. Keep each file's encoding and line endings. Invariant culture for numbers read from XML.
- Dark theme. `DpiScale.Px` for dialog and chrome pixels. GDI+ shape helpers keep the NaN-rejecting epsilon guard.
- Close running `APS.ControlCommunity.exe` and `APS.EmulatorCommunity.exe` before `dotnet build`. Never `--no-verify`. Do not push. Commit only after the owner authorizes commits for this work.
- Spec correction found during planning: `FrmMain.LoadCustomPanelsXML` needs a small change (Task 2) because panels cannot receive the spec by constructor. The design doc said no `FrmMain` change was expected.

## Review Focus

- `ProcessPanel.xml` missing: panel must still construct, with empty reading rows and no exception (Task 1, Task 3).
- Spec reading id with an unknown `group` or `accent`: entry skipped, rest loaded, one Events log line (Task 1).
- Duplicate reading ids: first wins, second reported (Task 1).
- Numeric attributes parsed with invariant culture on a ru-RU machine (Task 1).
- Snapshot chamber with no matching reading row: value dropped without throwing (Task 4).
- Valve id in the spec absent from live `ValveInfo` data: LED stays off, no throw (Task 3).
- Empty or single-chamber `ClusterSnapshot`: layout and paint do not throw (Task 4).
- `Reload GUI config`: spec re-read and panels rebuilt with the new rows (Task 2).

---

### Task 1: ProcessPanelSpec loader

**Files:**
- Create: `Control/GUIControls/Process/ProcessPanelSpec.cs`
- Test: `Control/Tests/CoreLogicTests/ProcessPanelSpecTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record ProcessPanelSpec(IReadOnlyList<ReadingSpec> Readings, IReadOnlyList<ValveGroupSpec> ValveGroups, IReadOnlyList<DriveSpec> Drives, IReadOnlyList<PumpSpec> Pumps, IReadOnlyList<string> Errors)`
  - `public sealed record ValveGroupSpec(string Header, IReadOnlyList<ValveSpec> Valves)`, `public sealed record ValveSpec(string Id, string Description)`, `public sealed record DriveSpec(string Id, string Label)`, `public sealed record PumpSpec(string Id, string Label)`
  - `public static ProcessPanelSpec Empty { get; }`
  - `public static ProcessPanelSpec Load(string? path)`: missing file returns `Empty` with no error; unreadable XML returns `Empty` with one error.
  - `public static ProcessPanelSpec Current { get; set; }`: defaults to `Empty`.
- Namespace `APS.SystemControl.GUIControls.Process`.

XML shape (the builder fixes attribute spelling; this is the minimum):

```xml
<processPanel>
  <readings>
    <reading id="pm" label="pm" group="Pressure" format="E1" unit="Torr" scale="Log" accent="Vacuum" device="" section="" sparkline="true"/>
  </readings>
  <valveGroups><group header="Gas inlets"><valve id="V1" descr="N2 inlet"/></group></valveGroups>
  <drives><drive id="Holder" label="Holder"/></drives>
  <pumps><pump id="TM" label="TM"/></pumps>
</processPanel>
```

- [ ] **Step 1: Write failing tests** in `ProcessPanelSpecTests` (write temp files under `Path.GetTempPath()`):
  - `Load_ValidFile_ReturnsReadingsInDeclarationOrder`: three readings, assert ids order and that `format`, `unit`, `Scale == ReadingScale.Log`, `Accent == ReadingPalette.Vacuum`, `Device` map.
  - `Load_MissingFile_ReturnsEmptyWithoutErrors`: `Load(@"C:\nope\x.xml")` has `Readings.Count == 0` and `Errors.Count == 0`; `Load(null)` same.
  - `Load_UnknownGroup_SkipsEntryAndReportsIt`: one bad, one good; `Readings.Count == 1`, `Errors.Count == 1`, error text contains the bad id.
  - `Load_UnknownAccent_SkipsEntryAndReportsIt`: same shape.
  - `Load_DuplicateId_KeepsFirstAndReportsSecond`.
  - `Load_EmptyId_SkipsEntry`.
  - `Load_MalformedXml_ReturnsEmptyWithOneError`.
  - `Load_ValveGroups_PreserveOrderAndDescriptions`.
  - `Load_InvariantCulture_UnderRuRu`: set `CultureInfo.CurrentCulture = new("ru-RU")` in the test, restore in `finally`; any numeric attribute you add parses with `.` decimals.
- [ ] **Step 2: Run and confirm failure.** `dotnet test Control/Tests/CoreLogicTests --filter ProcessPanelSpecTests` Expected: compile failure, type not defined.
- [ ] **Step 3: Implement `ProcessPanelSpec.Load`** with `XDocument`. `accent` resolves by reflection-free lookup against the public static `Color` fields of `ReadingPalette` (name match, case-insensitive). `group` and `scale` use `Enum.TryParse(ignoreCase: true)`. Errors are human-readable strings naming the id.
- [ ] **Step 4: Run tests; all pass.**
- [ ] **Step 5: Commit** (after owner authorization): `feat: process panel spec loader`.

---

### Task 2: Host wiring

**Files:**
- Modify: `Control/Core/CoreClass/Core.Configuration.cs:119` (add property next to `GUIPanelsConfigFile`)
- Modify: `Control/Forms/Main/FrmMain.LoadGUIControls.cs` (`LoadCustomPanelsXML`, before the `foreach` over panels)
- Test: `Control/Tests/CoreLogicTests/ProcessPanelSpecTests.cs` (extend)

**Interfaces:**
- Consumes: `ProcessPanelSpec.Load`, `ProcessPanelSpec.Current` (Task 1).
- Produces: `public string ProcessPanelConfigFile => Path.Combine(ConfigDir, "ProcessPanel.xml");` on the same class as `GUIPanelsConfigFile`.

- [ ] **Step 1: Write failing test** `ProcessPanelConfigFile_IsNextToPanelsXml`: build the configuration object the way a neighbouring `Core.Configuration` test does, assert both paths share a directory and the file name is `ProcessPanel.xml`.
- [ ] **Step 2: Run; fails** (property missing).
- [ ] **Step 3: Add the property.**
- [ ] **Step 4: In `LoadCustomPanelsXML`**, call `ProcessPanelSpec.Current = ProcessPanelSpec.Load(config.ProcessPanelConfigFile)` before panels are created, and publish each `spec.Errors` entry to the Events log through the same bus the neighbouring code uses (`EventLogBus`, level Operator). `ReloadGuiConfig` already re-runs this method, so reload is covered.
- [ ] **Step 5: Run `ProcessPanelSpecTests`; pass.** Build the solution.
- [ ] **Step 6: Commit** (after authorization): `feat: load ProcessPanel.xml before panels are created`.

---

### Task 3: ChamberProcessPanel

**Files:**
- Create: `Control/GUI_Libraries/Generic/ChamberProcessPanel.cs`
- Source to port: the chamber panel in the sibling closed repository (read only; paths in the local ledger)
- Test: `Control/Tests/CoreLogicTests/ChamberProcessPanelTests.cs`, ported from the matching layout tests in the sibling closed repository

**Interfaces:**
- Consumes: `ProcessPanelSpec.Current` (Task 1).
- Produces: `public class ChamberProcessPanel : BaseProcessPanel, IAnimatedSchematicPanel` with the same `HolderFlangeSeconds` and `ShutterFlangeSeconds` setters and `DrivesOverlayName` constant as the source.

- [ ] **Step 1: Port the layout tests** with neutral ids. Add:
  - `Ctor_NoSpec_BuildsWithoutRows`: `ProcessPanelSpec.Current = Empty`, construct, no throw.
  - `Ctor_WithSpec_CreatesRowForEverySpecReading`: spec of 3 readings, assert 3 rows by `Name`.
  - `OnValvesChanged_UnknownId_DoesNotThrow` and `OnValvesChanged_SetsLedForSpecValve`.
  - `OnDrivesChanged_UpdatesPillsDeclaredInSpec`.
  Reset `ProcessPanelSpec.Current` in each test's `finally`.
- [ ] **Step 2: Run; fail** (type missing).
- [ ] **Step 3: Port the class.** `DefineReadings()` returns `ProcessPanelSpec.Current.Readings`. Replace the static `ValveGroups`, `ValveDescr` and the fixed Holder and Shutter pills with the spec's `ValveGroups` and `Drives`. Keep the paint, flange, source, deposition-time and animation logic unchanged. Remove every device-name literal.
- [ ] **Step 4: Run tests; pass.**
- [ ] **Step 5: Add a `ProcessPanel.xml`** to `Configs/ADS` with the readings its `HWConf.xml` and `Sources.xml` support (list taken from those files, not from the Commercial panel) and a panel entry in `Configs/ADS/Panels.xml` with `<parent>tabProcess</parent>`, `<event>SendCommandEvent</event>`, `<msghandler>ProcessCustomPanelEvent</msghandler>`, `animHolderSeconds` and `animShutterSeconds` as the Commercial profile has them.
- [ ] **Step 6: Run the denylist scan** (`tools/Test-Denylist.ps1` with `APS_COMMUNITY_DENYLIST` set). Exit 0.
- [ ] **Step 7: Commit** (after authorization): `feat: ChamberProcessPanel`.

---

### Task 4: ClusterProcessPanel

**Files:**
- Create in `Control/GUI_Libraries/Generic/`: `ClusterProcessPanel.cs`, `ClusterLayout.cs`, `ClusterPanelMapper.cs`, `ClusterTilePlan.cs`, `ClusterValveLed.cs`
- Source to port (read only): the cluster panel, layout, mapper, tile plan and valve LED classes in the sibling closed repository
- Test: `Control/Tests/CoreLogicTests/Cluster{Layout,PanelMapper,TilePlan,TileMetrics,ProcessPanelSmoke}Tests.cs`, ported from the matching tests in the sibling closed repository

**Interfaces:**
- Consumes: `ProcessPanelSpec.Current` (Task 1), `ClusterSnapshot` and `IClusterProcessPanel` (existing).
- Produces: `public sealed class ClusterProcessPanel : BaseProcessPanel, IClusterProcessPanel`; `ClusterPanelMapper.PumpId(string pumpId, string kind)` replaces the fixed `TurboId("TM")` and `ScrollId("TM")` calls.

- [ ] **Step 1: Port the five test files** changing only namespaces and any literal closed names. Add:
  - `UpdateCluster_ChamberWithoutRow_DoesNotThrow`.
  - `UpdateCluster_EmptyChamberList_DoesNotThrow` and `UpdateCluster_SingleChamber_PaintsWithoutThrow` (render to a `Bitmap` via `DrawToBitmap`).
  - `Ctor_NoSpec_BuildsWithoutRows`.
- [ ] **Step 2: Run; fail.**
- [ ] **Step 3: Port the classes.** Remove the static `ProcessChambers` and `SecondaryReadings`. `DefineReadings()` returns `ProcessPanelSpec.Current.Readings`. Pump LEDs come from `Spec.Pumps`. Chamber list, tile order, sources, shutters and valves keep coming from the snapshot. Replace the `"Cluster_Demo"` summary default with the empty string. Keep `UiScale`, the DPI threading and the epsilon guard.
- [ ] **Step 4: Run tests; pass.**
- [ ] **Step 5: Add `ProcessPanel.xml`** to `Configs/Cluster` (readings from its `HWConf.xml`) and the panel entry to `Configs/Cluster/Panels.xml` with `<event />`, `<msghandler />`, `<parent>tabProcess</parent>`.
- [ ] **Step 6: Denylist scan** exit 0.
- [ ] **Step 7: Commit** (after authorization): `feat: ClusterProcessPanel`.

---

### Task 5: Sputter profile and end-to-end check

**Files:**
- Modify: `Configs/Sputter/Panels.xml`; Create: `Configs/Sputter/ProcessPanel.xml`. Choose `ChamberProcessPanel` if the profile's `Controls.xml` has a single chamber, otherwise skip this profile and report it.

- [ ] **Step 1: Build the full solution** (`dotnet build APS.Community.slnx`). No warnings from the new files.
- [ ] **Step 2: Run every test project**, as the pre-commit hook does. EmulatorTests: `--blame-hang-timeout 90s`, excluding `Concurrent_SerialCommands_DuringTicks_NeverCorruptQueue` and `TcpSerialFramingTests`.
- [ ] **Step 3: Launch Control and the Emulator** with the Cluster profile, then the ADS profile. At 96 DPI check: panel appears on the Process tab, readings update, valve and drive indicators move, step progress changes during an embedded recipe. Report 144 DPI as not checked.
- [ ] **Step 4: Edit `ProcessPanel.xml` while running** and use Reload GUI config; confirm the rows change.
- [ ] **Step 5: Update `Configs/ADS/README.md`** with a short `ProcessPanel.xml` section.
- [ ] **Step 6: Commit** (after authorization): `feat: process panels in Sputter profile, docs`.
