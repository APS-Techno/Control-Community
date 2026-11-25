# ADS profile

Single process chamber with a load lock and a linear transfer arm, running on the open reference stack.

| File | Read by | Role |
|------|---------|------|
| `Config.xml` | Control | Profile manifest and `<ManualLogging clockSensor="simtime" intervalMs="500"/>` settings. |
| `HWConf.xml` | Control | Active hardware configuration: S7 PLC `main` at `127.0.0.1`, LAN power supplies, throttle controller and rotary drives on `127.0.0.1:5025..5033`. |
| `HWConf.Emulator.xml` | Emulator (`HWConfigPath` in `EmulatorConfig.xml`) | Devices to simulate plus physics (conductance, pump speed, gas routing, `cm` range, `TP` backing limits). |
| `EmulatorConfig.xml` | Emulator | S7 server on `127.0.0.1:102`, one `TcpSerial` entry per LAN device, chamber physics, `<Deposition>` reactive model. |
| `Recipes/Textbook-*.rcpx` | Control | Validated open textbook recipes for pressure control, pure-Ar sputtering, and reactive `N21`/`Ar1` sweep. |

## LAN devices

| Id | Port | Type | Mode |
|----|------|------|------|
| `PSU_A` | 5025 | `LanPowerSupply` | DC |
| `PSU_B` | 5026 | `LanPowerSupply` | PDC |
| `PSU_C` | 5027 | `LanPowerSupply` | PDC |
| `PSU_ION` | 5028 | `LanPowerSupply` | DC |
| `PSU_RF1` | 5029 | `LanPowerSupply` | RF (spare, no source mapped) |
| `PSU_RF2` | 5030 | `LanPowerSupply` | RF |
| `TC1` | 5031 | `LanThrottleController` | pressure controller used by `pwait` |
| `Holder` | 5032 | `LanRotaryDrive` | substrate holder |
| `Shutter` | 5033 | `LanRotaryDrive` | shutter |

Only one profile runs at a time, so the port range is shared with the other profiles.

## Sources

| Flange | Source | Magnetron | Supply |
|--------|--------|-----------|--------|
| 1 | empty | | |
| 2 | `Si` | `MAG2` | `PSU_A` |
| 3 | `C` | `MAG3` | `PSU_B` |
| 4 | `Ion` | `MAG4` | `PSU_ION` |
| 5 | `Ru` | `MAG5` | `PSU_C` |
| 6 | `Co` | `MAG6` | `PSU_RF2` |

## Behaviour

- Gas routing: `Ar1`, `Ar2` pass gas while `V2` or `V3` is open; `N21`, `N22` while `V1` or `V4` is open.
- `pwait <target> <timeout>` is bound to `TC1`; a leading sensor argument is ignored with a validation warning, and a target of `0` parks the throttle fully open.
- `deposit <source> <time> <retract>` is bound to `Holder`: the holder moves to the source, the `Shutter` drive opens over it for the dwell, then parks at the retract position.
- `Alarms.xml` has two state machines: `Main` (idle pressure alarm with a 60 s settling delay) and `Loadlock` (maintenance while `A2` is open and `GVL` is closed).
- `Interlocks/Interlocks.Main.xml` requires `Valve GVM closed`, `Valve FL closed`, `Pump SP1 on`, and `Pump TP off` on `Valve_PP_open`, and requires `Valve GVM closed` on `Valve_FL_close`.
- `LOAD_SUBSTRATE` and `UNLOAD_SUBSTRATE` move the substrate through `GVL` with stages `VS` and `HS`.
- `FAILURE_PROTOCOL` homes `Shutter` and `Holder`, zeroes `N21`/`N22` and closes `V1`/`V4` before turning off `PSU_A`..`PSU_RF2`, then opens `TC1` (`pwait pm 0 1`), zeroes `Ar1`/`Ar2`, and closes the remaining gas valves.

## Telemetry and physics

- `cm` (address `104`): capacitance manometer on `Main`. In `HWConf.Emulator.xml`, `minPressure="1e-4"` and `maxPressure="1e-2"` Torr; readings outside that span show `UNDER` or `OVER`. Shown in `Controls.xml` and `ProcessPanel.xml`.
- `pf` (address `108`, `property="ForelinePressure"`, `source="TP"`): foreline pressure between `TP` and `FL`. In `HWConf.Emulator.xml`, `TP` declares `backingPump="SP1"`, `forelineVolume="2"`, and `forelineLimit="5"` Torr. Shown in `Controls.xml` and `ProcessPanel.xml`.
- `simtime` (address `400`, `property="SimTime"`): simulation clock read by `<ManualLogging clockSensor="simtime" intervalMs="500"/>` in `Config.xml`. Declared in `HWConf*.xml` only; omitted from `Controls.xml` and `ProcessPanel.xml`.
- `thick` (address `404`, `property="Thickness"`): cumulative simulated film thickness (`nm`), shown as `Film` in `Controls.xml` and `ProcessPanel.xml`.
- `<Deposition>` in `EmulatorConfig.xml` (`reactiveMfc="N21"`, `ratePerAmp="1.0"`, `poisonRate="0.00673"`, `cleanRate="0.1"`, `poisonedRateFraction="0.2"`, `poisonedVoltageShift="100"`): models first-order target nitridation vs. discharge cleaning on current-regulated magnetron runs (`PSU_A`) and integrates `thick` while `Shutter` is open.

## Textbook recipes

- `Recipes/Textbook-Ex03-PressureControl.rcpx`: opens `M1` and `V1`, sets `N21` to `50` sccm, holds `2mT` -> `4mT` -> `2mT` on `TC1`, zeroes `N21`, closes `V1` and `M1`, opens `TC1` (`pwait 0 5`), and waits on `swait pm 10uT 300`.
- `Recipes/Textbook-Ex05-Si-60s.rcpx` and `Recipes/Textbook-Ex05-Si-120s.rcpx`: pure-Ar (`20` sccm `Ar1` via `V2`, `V6`, `M1`, `2mT`) DC sputtering of `Si` (`PSU_A`, `Current` `250` mA) with `120` s pre-sputter and `60` s or `120` s exposure (`deposit Si 60 Home` / `120`), then `power PSU_A off`, `set PSU_A Current 0`, `pwait 0 1`, `mfc Ar1 0`, and closing `V2`, `V6`, `M1`.
- `Recipes/Textbook-Ex06-ReactiveSweep.rcpx`: fixed `20` sccm `Ar1`, `2mT`, and `250` mA DC on `Si` (`PSU_A`), stepping `N21` (`0` -> `2` -> `4` -> `0` sccm via `V1`) with `90` s conditioning and `60` s exposure at each point before clean shutdown.

## Process tab

`ProcessPanel.xml` declares what the Process panel (`APS.Controls.Generic.ChamberProcessPanel`, loaded from `Panels.xml`) shows: the reading rows (id, group, format, unit, scale, accent), the valve LED groups and the drive pills. Reading ids follow the telemetry names: a sensor or MFC by its id, a supply as `<id>_V`, `<id>_I`, `<id>_W`, a throttle controller as `<id>_P`, `<id>_Pos`. The file is read when the panels load, so use Reload GUI config after a change. A bad entry is skipped and reported in the Events log.
Set `flanges="N"` on the root element (1 to 6, default 6) to draw only the flanges the system has.
