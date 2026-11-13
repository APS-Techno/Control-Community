# Process panels for Community: design

Status: draft, awaiting owner approval. Size: project.

## Intent

The operator of a Community profile needs a Process tab with:
- chamber layout (schematic or cluster tiles),
- pressure and gas readings,
- power-supply readings,
- recipe step progress.

Commercial has two such panels (a 6-flange chamber panel and a cluster overview panel). They contain closed device names, so they cannot move to Community as they are. Community gets generic ports whose content is declared in profile XML.

## Goals

- Two generic panels in `Control/GUI_Libraries/Generic` (`APS.Controls.Generic`):
  - `ChamberProcessPanel`: port of the chamber panel (schematic, drive pills, valve LED groups, shutter and deposition-time overlay).
  - `ClusterProcessPanel`: port of the cluster panel (radial transfer hex, chamber tiles, aggregated readings, valve and shutter LEDs, pumps, robot pill).
- Both derive from `BaseProcessPanel`. Both are visually and behaviorally equal to the Commercial panels at the same data.
- A per-profile `ProcessPanel.xml` declares everything the panels cannot learn from live data: reading rows, valve groups and descriptions, drives, pumps, reading groups and accents.
- The ADS, Cluster and Sputter profiles in `Control-Community/Configs` get a `ProcessPanel.xml` and a Process panel entry in `Panels.xml` where the profile has a Process tab.
- No closed type, assembly or device names in source. Device names appear only in profile XML.

## Non-goals

- No change to `Control-Commercial` and no change to its `Community` pin. Replacing the Commercial panels is a later request.
- No change to `IProcessPanel`, `IClusterProcessPanel`, `ClusterSnapshot` or the `FrmMain` feeding code, unless a gap is found. A gap is reported to the owner before any contract change.
- No new reading sources. Only existing telemetry ids are shown.

## Anti-goals

- Hardcoded chamber ids (`C1`..`C4`, `LL`, `TM`) or gas names in panel source.
- A spec that the panel silently ignores when an id is wrong. A bad id is reported once in the Events log.

## Constraints

- `BaseProcessPanel` builds reading rows in its constructor through `DefineReadings()`. `UpdateReadings` drops ids with no row. The spec MUST therefore be loaded before `DefineReadings()` runs, and cluster extra readings MUST be declared in the spec.
- Panels are created by `LoadCustomPanelsXML` from `Panels.xml`. A read-only panel MUST declare an empty `<event></event>`. `PanelDragManager` rewrites `Panels.xml` on exit, so the reading lists live in `ProcessPanel.xml`, not there.
- Dark theme, `DpiScale.Px` for dialog and chrome pixels. Canvas widgets follow `BaseScale` only. Geometry in `ClusterProcessPanel` uses `UiScale * dpi`, as in the source panel. GDI+ shape helpers use the NaN-rejecting epsilon guard.
- Invariant culture for numbers. Keep each file's encoding and line endings. Comments are not allowed by default.
- Repository rules: do not push, do not bypass the pre-commit hook, close running Community apps before `dotnet build`, run EmulatorTests with the hang timeout and exclusions. The denylist scan MUST pass.

## Approaches considered

1. Declarative spec in the profile (chosen). One new XML schema. New machines need no code.
2. Derive everything from HWConf, Sources and Valves. Rejected: the device model has no gas, unit, format or log-scale metadata, so a schema would be invented in code.
3. Inline in `Panels.xml`. Rejected: the file is rewritten by `PanelDragManager`, so hand-written lists are fragile.

## Design

### ProcessPanel.xml

One file per profile, loaded from the profile folder. Sections:
- `<readings>`: `<reading id label group format unit scale accent device section/>`. Fields map one to one to `ReadingSpec`. `group` is a `ReadingGroup` name, `scale` is `Linear` or `Log`, `accent` is a `ReadingPalette` name, `device` keys the Sources-driven visibility.
- `<valveGroups>`: `<group header><valve id descr/></group>`.
- `<drives>`: ids and labels for the pills.
- `<pumps>`: id and LED label (cluster panel).

A small loader (`ProcessPanelSpec`) parses and validates the file and returns immutable records. It is pure and unit-testable. Unknown `group` or `accent` names, duplicate reading ids and empty ids are errors reported through the Events log. The panel then skips the bad entry and keeps the rest.

### ChamberProcessPanel

Port of the chamber panel with `DefineReadings()`, `ValveGroups` and `ValveDescr` taken from the spec. Flange count comes from `ChamberSchematicControl`, as today. Material and power-source labels come from `OnSourcesChanged`. Deposition time comes from `ProcessParameterKeys.DepositFlange`.

### ClusterProcessPanel

Port of the cluster panel. Chamber list, tile order, valve and shutter LEDs, sources and positions come from `ClusterSnapshot`. The spec supplies only the reading rows the base constructor must pre-build and the pump list. The pure mapper and tile plan move with the panel and keep their tests.

### Wiring

- `Panels.xml` entries of the three profiles gain the panel `<type>` in `APS.Controls.Generic.dll`.
- No `FrmMain` change expected. `FindProcessPanel` and `FindClusterPanel` already discover both interfaces.

## Verification

- Unit tests: spec loader (valid, bad group, bad accent, duplicate id), cluster mapper and tile plan ported from the Commercial tests.
- Build the solution. Run every test project, as the pre-commit hook does.
- Launch Control with the Cluster and ADS profiles and the matching emulator. Check each Process tab visually at 96 DPI: layout, readings update, step progress during an embedded recipe. 144 DPI is not available on this host and is reported as not checked.
- Run `tools/Test-Denylist.ps1` with `APS_COMMUNITY_DENYLIST` set.

## Left to the builder

- Exact attribute spelling in `ProcessPanel.xml`.
- File split inside `APS.Controls.Generic`.
- Which of the three profiles have a Process tab and need a `Panels.xml` entry.
- Choosing the reading ids for each profile from its HWConf.
