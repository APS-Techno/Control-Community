# VacuumControls — Immediate Fixes

Per feedback (2026-05-26): vacuum-widget rendering bugs must be fixed BEFORE
tuning any Controls.xml schematic coordinates — otherwise coordinate fixes
play whack-a-mole against widget bugs.

## Widget bugs (immediate)

- [x] **ScrollPumpControl** — needs orientation support (configurable rotation/flip; today it draws one fixed direction). ✅ *Fixed in two passes:*
  - *Widget side: `Direction` property (up/down/left/right, default "up"); `CalculateSize` swaps W/H for left/right; `DefineAnchors` returns single anchor named after its client side (top/bottom/left/right) so existing `SP*:top` pipe references keep working for default direction; `OnPaint` uses `Graphics.RotateTransform(0/90/180/270)` around client center and renders into logical "up" coordinates.*
  - *Builder integration: added `ScrollPump` to `DirectionTypeConverter.GetStandardValues` and `ShouldSerializeDirection` so the property panel shows the Direction dropdown; `ControlFactory.CreateScrollPump` now propagates `item.Direction` (mapping legacy "hor"/empty → "up"); `XML_SPumpControl` gained `dir` attribute and `DocumentSerializer` reads/writes it; `SchemeValidator.GetValidAnchors` returns the direction-keyed name so pipe-endpoint validation matches the widget's actual anchor.*
  - *Control integration: matching `dir` attribute on the Control-side `XML_SPumpControl`; `FrmMain.LoadSPumps` propagates it into the live widget.*
- [x] **GateValveControl** — vertical orientation: alignment anchors sit on the component center instead of the pipe-connector centerline; breaks pipe joining when vertical. ✅ *Fixed in `CanvasInteractionController.GetControlAlignmentPoints` (Builder): Alt-snap "center" point is now the anchor centroid (= pipe axis intersection) instead of the bounding-box geometric center. Widget anchors themselves were already on the bore centerline; the bug was alignment using BB-center. Anchorless controls still fall back to BB-center.*
- [x] **GateValveControl** — vertical orientation: caption currently on the left and eats horizontal space; should be in the bottom-right corner when vertical. ✅ *Fixed: caption now placed at `(TotalW - textW, TotalH - textH)` with `TotalW = max(Bonnet.Right, textW)` and `TotalH = Body.Bottom + FlangeThick + captionGap + textH`. Same transformation applied to `ValveControl` and `ThrottleValveControl` in the same pass. **Follow-up**: `captionGap` bumped from 2 → 8 (Max with `8 * sf`) in all three so the ≤6px hover-radius anchor dot at the bottom-flange tip no longer overlaps the caption text.*
- [x] **TurboPumpControl** — vertical orientation: (a) proportions distort when rotated, (b) text labels stay horizontal — should rotate with the widget so a vertical TP gets vertical labels. ✅ *Fixed by refactoring to a single logical "right" layout plus a `Graphics.RotateTransform` in `OnPaint`. `CalculateSize` swaps W↔H for up/down so the rotated content fits. `DefineAnchors` rotates logical anchor points to client coords and names each by which client edge it lands on (so `tp:bottom` still means "bottom in client coords" regardless of orientation). LED labels and ID label switched from `TextRenderer.DrawText` to `Graphics.DrawString` so they honor the rotation transform — vertical TP now gets vertical labels.*
- [x] **GasInletControl** — horizontal (left/right) orientation: pipe anchor point is visually separated from the drawn pipe stub; anchor and pipe end must coincide. ✅ *Fixed: dropped phantom `idLabelW` reservation in `PaintHorizontal`; horizontal size now adds `idLabelH` below body; anchor Y uses `(Height - idLabelH)/2` so it lands on the bore centerline.*

## Audit findings

### Same-pattern generalizations of the bugs above

- [x] **ValveControl** — vertical orientation: same caption-bloat as GateValve. ✅ *Fixed in the same pass as GateValve — bottom-right corner caption via `L.TotalSize`.*
- [x] **ThrottleValveControl** — vertical orientation: same caption-bloat. ✅ *Fixed in the same pass as GateValve — bottom-right corner caption via `L.TotalSize`.*
- [x] **ValveControl & ThrottleValveControl** — vertical anchors at `(body center X, 0)` / `(body center X, totalHeight)`. ✅ *Same Alt-snap fix in `CanvasInteractionController` covers these — alignment now uses anchor centroid for all anchored widgets uniformly.*
- [ ] **PumpControl** — like ScrollPump, has only a single fixed `"top"` anchor and no `Direction` property. Decide whether rotary-vane pumps also need orientation support and, if yes, add it the same way.

### Code-level detail on already-listed bugs (for the fixer)

- [x] **GasInletControl horizontal anchor bug — root cause located**: `PaintHorizontal` (line 152) computes `idLabelW = TextRenderer.MeasureText(ID, _idFont).Height + 2` and subtracts it from `cylinderW`. But the ID label is drawn *below* the body (`DrawIdLabel(..., vertical: false)`, line 178/196), not to the side — so this horizontal space is reserved but never used. ✅ *Fixed — see the widget bugs section above.*
- [x] **TurboPumpControl vertical proportions — root cause located**: in `"up"`/`"down"` modes the tube swaps dims (`tubeDia × tubeLen`) but the control panel stays `PanelW × PanelH` (70 wide × 110 tall, portrait). ✅ *Fixed via full rotate-transform refactor — see entry above.*
- [x] **TurboPumpControl vertical labels — root cause located**: `DrawIdLabel` and the per-LED label loop in `PaintPanel` both draw text via `TextRenderer.DrawText` with no rotation. ✅ *Fixed — switched to `Graphics.DrawString` which honors the rotation transform.*

## Live-test visual bugs (2026-06-01, Cluster_Demo SYSTEM_START)

Reported from operator testing of Control against the Emulator. Widget-level
fixes first (per the rule at the top of this file); any Controls.xml coordinate
tuning comes after the widget bugs are settled.

- [x] **ThrottleValveControl** — shows vacuum flow on the pipes *both before and
  after* it even when it sits between two **closed** valves (so no flow is
  possible). ✅ *Root cause was in the flow/connectivity pass, not the widget:
  `VacuumSchematic.RecalculateFlow` seeds flow from every member of `_openComponents`,
  and `FrmMain.LoadThrottleValves` force-registered the throttle open (`Position=100`)
  at load — making it a permanent flow source that self-illuminated its own stubs.
  Fix: throttles now register via `RegisterThrottleComponent` and are **skipped in the
  seed loop** — an open throttle still conducts (it stays in `_openComponents`, so
  `PropagateFlow` traverses through it when a real source reaches it) but never
  originates flow; a closed throttle blocks. Covered by `VacuumSchematicFlowTests`
  (3 cases). **Known limitation:** the engine is touch-based, so a dead-end pipe past
  a closed valve while a pump runs can still light — a path-reachability rework, deferred.*
- [ ] ~~**TransferModuleControl** — face **C1 is drawn at the wrong clock position**~~
  **Won't fix** — the widget is internally consistent (`FaceMidpoint(idx=2)` → 2 o'clock,
  and the arm uses the same `(idx-1)·60`); C1 `idx=2/angle=60` *is* 2 o'clock. The
  bottom-of-schematic placement was a `Controls.xml` layout choice. Being removed in the branch.
- [x] **TransferModuleControl** — the **yellow position dot sits too far** from
  centre (wrong radius / offset). ✅ *Best-guess tweak: `DrawStateDot` now positions
  relative to the hex centre/radius (`centre.X`, `centre.Y - 0.65·radius`) — top of the
  cavity, above the ID label — instead of floating in the empty top-right box corner. Tunable.*
- [x] **TransferModuleControl** — the **pressure display needs adjustment**
  (position / size / format of the TM pressure readout). ✅ *Best-guess tweak: moved to
  the lower cavity (`centre.Y + 0.30·radius`) to clear the ID label and arm pivot;
  font 12→11pt; compact `0.0E+0` format with a `— T` placeholder until a real reading
  arrives. Tunable.*
- [x] **MagnetronControl vs chamber caption** — the **chamber caption is hidden
  behind the magnetron** (overlap / draw-order). ✅ *Fixed as a side effect of the
  chamber-display rework: `DrawChambers` now seats each chamber caption in a thickened
  top-edge "nameplate" band (~2× the side walls) instead of centred in the cavity, so
  the `MAG_C1` magnetron (top=60, below C1's title band at ~57.5–60%) no longer covers
  it. Confirmed clear in live testing 2026-06-01.*

## Cluster process panel (2026-06-09)

Control-side process panel (not a VacuumControls schematic widget) — likely
under `GUIControls/Process/`.

- [ ] **Cluster process panel — face 1 flickers.** The face-1 cell repaints with
  visible flicker. Probable cause: a cosmetic update on the 200 ms tick invalidating
  the whole face without double-buffering, or redundant `Invalidate()`/full repaint.
  Check `DoubleBuffered`/`SetStyle(OptimizedDoubleBuffer)` on the panel and whether
  only changed regions are invalidated. See [[project_process_panel]],
  [[project_cluster_panel_multidevice]], [[project_ui_thread_plc_read_freeze]].

## Locale correctness (2026-06-08, ru-RU VMWare VM)

Reported from running the deployed suite on a Russian-locale VMWare Workstation
VM ("WM"): the scheme rendered wrong — pipes lost their bends and the editor
could not select some pipes. Same VM-locale class as the earlier loopback fix.

- [x] **Pipe bends + `@x,y` endpoints dropped on comma-decimal locales** — on the
  ru-RU VM the schematic drew pipes as straight diagonals (bends lost) and bent
  pipes were unclickable in the editor. ✅ *Root cause: the shared
  `VacuumSchematicCanvas` render path (`DrawPipes`, `ResolveEndpoint`) parsed
  `bends="x,y x,y"` waypoints and `@x,y` free-point endpoints with the **current
  culture**, so a decimal like `"4.5"` silently failed to parse where `.` isn't the
  decimal separator → every bend was dropped from the render. The hit-test path
  (`DesignCanvas.GetPipePoints`) was already invariant, so the clickable region
  still followed the real L-shaped path while the drawn line was straight — visible
  line ≠ hot zone → bent pipes couldn't be selected. Fix: extracted
  `BuildPipePoints` (CultureInfo.InvariantCulture) into the base, shared by
  `DrawPipes` and `DesignCanvas.GetPipePoints` so render and hit-test geometry can
  never diverge again; fixed `ResolveEndpoint`'s `@x,y` parse; fixed the Builder
  waypoint write path (`InsertWaypoint` / `UpdateWaypointPosition`) to format with
  InvariantCulture so editing on a comma locale no longer persists a corrupt
  `"30,0"`. Regression test `PipeBendCultureTests` pins ru-RU. Confirmed working on
  the VM 2026-06-09. Commits: VacuumControls `e9aa8bc`, Builder `e1ee546`.*
