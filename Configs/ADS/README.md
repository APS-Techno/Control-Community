# ADS profile

Single process chamber with a load lock and a linear transfer arm, running on the open reference stack.

| File | Read by | Role |
|------|---------|------|
| `HWConf.xml` | Control | Active hardware configuration: S7 PLC `main` at `127.0.0.1`, LAN power supplies, throttle controller and rotary drives on `127.0.0.1:5025..5033`. |
| `HWConf.Emulator.xml` | Emulator (`HWConfigPath` in `EmulatorConfig.xml`) | Devices to simulate plus physics (conductance, pump speed, gas routing). |
| `EmulatorConfig.xml` | Emulator | S7 server on `127.0.0.1:102`, one `TcpSerial` entry per LAN device, chamber physics. |

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
- `pwait <sensor> <target> <timeout>` is bound to `TC1`; a target of `0` parks the throttle fully open.
- `deposit <source> <time> <retract>` is bound to `Holder`: the holder moves to the source, the `Shutter` drive opens over it for the dwell, then parks at the retract position.
- `Alarms.xml` has two state machines: `Main` (idle pressure alarm with a 60 s settling delay) and `Loadlock` (maintenance while `A2` is open and `GVL` is closed).
- `LOAD_SUBSTRATE` and `UNLOAD_SUBSTRATE` move the substrate through `GVL` with stages `VS` and `HS`.

## Process tab

`ProcessPanel.xml` declares what the Process panel (`APS.Controls.Generic.ChamberProcessPanel`, loaded from `Panels.xml`) shows: the reading rows (id, group, format, unit, scale, accent), the valve LED groups and the drive pills. Reading ids follow the telemetry names: a sensor or MFC by its id, a supply as `<id>_V`, `<id>_I`, `<id>_W`, a throttle controller as `<id>_P`, `<id>_Pos`. The file is read when the panels load, so use Reload GUI config after a change. A bad entry is skipped and reported in the Events log.
