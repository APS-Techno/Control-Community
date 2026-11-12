# APS.Mcp — Control agent bridge

`APS.Mcp` is a stdio MCP (Model Context Protocol) server. It exposes the APS Control
application's Agent server (a pinned-TLS TCP protocol built into Control) as a set of
MCP tools, so an agent such as Claude Code can inspect and operate a running deposition
system: read machine/hardware/recipe state, tail the event log, validate and upload
`.rcpx` recipes, start/abort recipes, issue direct device commands, and after a run copy
its data log, process-log window, recipe and snapshot to the local disk for an ELN — all
subject to the interlocks and tier gating enforced on the Control side.

```
Claude Code  <-- stdio (this process's stdin/stdout) -->  APS.Mcp
APS.Mcp      <-- pinned TLS, line-JSON protocol       -->  Control's AgentServer (bin\AgentServer.xml)
```

`APS.Mcp` itself holds no privilege beyond the API key in its config file — every command
is re-checked against that key's tier by Control, and every interlock still applies on the
hardware thread exactly as it does for the operator panels.

The TLS/pinning/framing layer lives in the shared `APS.Wire` library
(project-referenced as `..\..\Wire\APS.Wire`), which the MQTT
telemetry bridge uses too. `APS.Mcp` keeps only the MCP tools
and its single-retry `WireClient`.

**Server prerequisite**: the Agent server only starts when `bin\AgentServer.xml` has `enabled="true"` and at least one `<Key>`. Otherwise Control stays silently off: no port is opened, and the bridge fails to connect.

## 1. Server setup (on the Control machine)

1. Edit `bin\AgentServer.xml` (the copy in `bin\`, not the source tree — Control reads it
   from the deployed `bin\` directory next to the running exe):

   ```xml
   <AgentServer enabled="true" port="8026" bind="127.0.0.1" allowRecipe="true" allowManual="false">
     <Key name="dev" hash="..." tier="Manual"/>
   </AgentServer>
   ```

   - `enabled` — flip to `true` (default is `false`, i.e. the feature is off).
   - `bind`/`port` — the interface/port the TLS listener binds. `127.0.0.1` keeps it
     loopback-only; use a LAN address only if you understand the exposure.
   - `allowRecipe` / `allowManual` — deployment-wide ceilings. Every key's tier is
     clamped to these: with `allowManual="false"`, even a key tagged `tier="Manual"` is
     downgraded to `Recipe`; with `allowRecipe="false"` it is further downgraded to
     `Read`.
   - One `<Key>` element per credential. `tier` is `Read`, `Recipe`, or `Manual` (see the
     table below). `hash` is the SHA-256 hex of the plaintext API key — generate it in
     PowerShell:

     ```powershell
     [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes("your-key")))
     ```

     Keep the plaintext key for the bridge config (step 2); only the hash goes in
     `AgentServer.xml`.
   - `recipeFolder` (optional, per `<Key>`) — confines that key to one folder under the
     recipes directory, e.g. `<Key name="agent" hash="..." tier="Recipe" recipeFolder="Agent-Paper2"/>`:
     `upload_recipe` saves into `<RecipesDir>\Agent-Paper2\` (created on first upload) and
     `describe_recipes` lists only the recipes in that folder, plus the embedded system
     recipes. Control refuses an upload from that key whose name already exists anywhere else
     in the library, or matches an embedded recipe, because recipes are started and listed by
     name alone (uploads through the agent server are serialized; a file written at the same
     moment by another program is not covered). Known limitation: a directory junction or
     symlink created inside the key's folder by someone with access to the Control PC makes
     the target's files count as inside it; do not put links in an agent's recipe folder. The value must be a
     plain folder name (no `\`, `/`, `..`, drive, or device name); an invalid value makes
     Control skip the key entirely, the same as an unknown tier. Leave it out for the whole
     library, as before. The operator's recipe list and `start_recipe` find recipes in
     subfolders, so the operator runs these like any other recipe.
     **It confines where the key writes and what `describe_recipes` shows — nothing else.**
     The same key can still `start_recipe` any recipe by name, see other recipes' names in
     `list_runs`, `recipe_status` and `tail_log`, and fetch their `.rcpx` with
     `fetch_run_files`; an `already_exists` refusal also confirms that a name exists
     elsewhere. To keep an agent away from other work, deny those tools in the MCP client as
     well (or use a Read-tier key where it only needs to look) — that denial is the client's,
     not Control's: anyone holding the key can call them over the protocol directly.

2. Restart Control. On successful startup, the Events log (System tab / floating events
   overlay) shows a line like:

   ```
   Agent server listening on 127.0.0.1:8026 (cert 3F2A9C…)
   ```

   The `cert <thumbprint>` is the SHA-1 thumbprint of Control's self-signed TLS
   certificate (`bin\AgentServer.pfx`, generated on first run). For pinning
   (`certSha256` in the bridge config below) you need the certificate's SHA-256
   fingerprint, not this thumbprint — the bridge logs that value itself on first
   connect (see step 3) if you leave `certSha256` unset (trust-on-first-use).

   If this line never appears, check (in order): `enabled="true"` and at least one
   `<Key>` present — both are silent no-ops, not errors.

## 2. Bridge setup (wherever the MCP client runs — usually the same machine)

```powershell
dotnet publish APS.Mcp -c Release -o publish
copy APS.Mcp\apsmcp.sample.json publish\apsmcp.json
notepad publish\apsmcp.json
```

Fill in:

```json
{
  "host": "127.0.0.1",
  "port": 8026,
  "apiKey": "your-key",
  "certSha256": null,
  "downloadDir": null
}
```

- `host`/`port` — must match `bind`/`port` from `AgentServer.xml`.
- `downloadDir` — local folder that `fetch_run_files` (§8) writes run artifacts into, one
  sub-folder per run id. Leave `null` and the tool refuses with a message naming this
  field; nothing else changes. When the files are meant for the ELN MCP server's
  `upload_file`, the folder must sit **inside that server's `MCPUploadRoot`** (a registry
  value on the ELN side), because `upload_file` rejects any path outside it.
- `apiKey` — the **plaintext** key whose SHA-256 you hashed into `AgentServer.xml`.
- `certSha256` — leave `null` for the first run: the bridge accepts the server's
  certificate on trust-on-first-use and prints its SHA-256 fingerprint to stderr
  (`aps-mcp: WARNING no certSha256 pinned; server cert fingerprint is …`). Copy that
  value back into `certSha256` and restart the bridge to pin it — after that, a
  changed/wrong certificate is refused instead of silently trusted.

`apsmcp.json` is gitignored (it holds a live API key); only `apsmcp.sample.json` is
tracked, and it ships alongside the published exe (`CopyToOutputDirectory` in the
`.csproj`) as the template to copy from.

The bridge also accepts `--config <path>` on the command line, or an `APS_MCP_CONFIG`
environment variable, in that priority order, before falling back to
`apsmcp.json` next to the exe. If none resolves to an existing file, the process exits
immediately with a `FileNotFoundException` on stderr naming the missing path — that
message points back to this section.

## 3. Claude Code registration

```powershell
claude mcp add aps-control -- <abs-path>\publish\APS.Mcp.exe --config <abs-path>\apsmcp.json
```

Use absolute paths for both the exe and the config — Claude Code launches the server
from an arbitrary working directory. Once registered, a fresh Claude Code session can
call the tools listed below against this machine.

## 4. Capability tiers

The tier granted by a key (after the `allowRecipe`/`allowManual` deployment clamp) gates
which wire commands — and therefore which MCP tools — succeed. A call above the key's
tier fails with error code `tier_denied` (surfaced to the model as an `McpException`
saying which tier is required and which tier the key grants); it does not touch hardware.

| Tier     | Tools allowed | Typical use |
|----------|----------------|-------------|
| `Read`   | `describe_machine`, `describe_hardware`, `describe_recipes`, `describe_dsl`, `describe_interlocks`, `describe_alarms`, `system_status`, `device_snapshot`, `tail_log`, `recipe_status`, `list_runs`, `fetch_run_files`, `compute_deposition_plan`, `validate_recipe` | Read-only monitoring/inspection agent |
| `Recipe` | everything in `Read`, plus `upload_recipe`, `start_recipe`, `abort_recipe` | Recipe authoring/automation, no manual device control |
| `Manual` | everything in `Recipe`, plus `device_command` | Full manual control, same as an operator panel |

`Recipe` and `Manual` are each a superset of the tier below — a `Manual` key can also
validate/start recipes and read status.

**Control mode (Control >= 2026-09-06).** Besides the tier, every mutating tool (`start_recipe`,
`upload_recipe`, `abort_recipe`, `resolve_pause`, `device_command`) needs Control to be in
**Online-Remote** — an operator switches it under *Service → Control mode…* (Engineer role).
Otherwise the tool fails with `control_mode: control mode is Local — host commands need
Online-Remote; only an operator can switch it (Service → Control mode…) — do not poll, ask the
operator`. Read tools and telemetry are never affected. `describe_machine` and `system_status`
report the current `controlMode` (check it before planning any mutating call — a host cannot change
it), and the auth reply's `capabilities` array contains `"controlMode"` on a Control that enforces it.

## 5. Verify-after-command workflow

Every mutating tool (`upload_recipe`, `start_recipe`, `abort_recipe`, `device_command`)
returns as soon as Control **accepts** the request onto the hardware/recipe thread — it
does not wait for the effect to complete, and interlock denials happen asynchronously.
After any such call:

1. Call `device_snapshot` (and/or `recipe_status`) to confirm the state actually changed
   the way you expect — pressures, flows, valve/pump states, or recipe step/progress.
2. Call `tail_log` to see the event-log lines for that action — interlock denials, alarm
   trips, and the recipe failure protocol (on `abort_recipe`) all surface there, naming
   the blocking device or the reason, even though the tool call itself returned
   `"accepted"`.

Do not treat `"accepted"` alone as confirmation that a command took effect.

## 6. `device_command` argument shape (read this before calling it)

`device_command(deviceType, deviceId, argsJson)` forwards `argsJson` almost verbatim to
the same code path the operator panels use, and that path is **not** uniform across
device kinds:

- **Valve, MFC, Pump, Stage** — `argsJson` is just the raw value(s) the panel sends, with
  no leading command name: `[true]` to open a valve, `[5.0]` for an MFC flow setpoint,
  `[position]` to move a stage.
- **Power, Drive, Controller** — `argsJson` leads with a command-name string, followed by
  its values. These are the **panel** command names, **not** the recipe DSL verbs:
  `["SetCurrent", 250]` / `["SetVoltage", v]` / `["SetPower", w]` / `["SetFrequency", f]` /
  `["SetDutyCycle", d]` / `["OutputOn"]` / `["OutputOff"]` / `["ClearFault"]` /
  `["Reconnect"]`, plus `["home"]` to home a drive. Append `:<n>` for a channel on a
  multi-channel supply (`["SetVoltage:2", v]`).

> **Not `["set", 5.0]` or `["power", true]`.** Those are DSL verbs and this path does not
> take them. They used to be accepted, logged nowhere, and silently discarded — the caller
> believed the supply was energised while it stayed off. `CoreThread.Execute.cs:1879` now
> has a `default:` case that rejects an unknown name loudly, so the wrong form fails
> instead of lying. The authoritative list is `Control/Core/Classes/PowerCommands.cs`.

Never prefix a Valve/MFC/Pump/Stage command with a command-name token — it is not
expected there and will be misinterpreted as the value itself. For a PSU, remember the
two-step rule: set the level first (`["SetCurrent", 250]`), then energise
(`["OutputOn"]`) — a single combined call does not exist.

## 7. Operational notes (learned from live smoke)

- **`abort_recipe` is not instant.** It triggers the machine's full `FAILURE_PROTOCOL`
  shutdown sequence (sources off, safe state) — observed to take on the order of 30
  seconds, not an immediate stop. Poll `recipe_status` afterward with a generous
  timeout (60+ seconds recommended) rather than assuming the recipe has stopped as
  soon as `abort_recipe` returns `"accepted"`.
- **`message` recipe steps block unattended runs.** A `message` step pops a blocking
  operator-acknowledge dialog on the Control UI, and that dialog **survives**
  `abort_recipe` — until a human dismisses it, shutdown can stall. Avoid uploading or
  starting recipes containing `message` steps for unattended/agent-driven runs.
- **Mutating commands are not auto-retried on a dropped connection.** If the TCP/TLS
  connection to Control drops mid-call, the bridge reconnects and silently retries once —
  but only for read commands (`describe_*`, `system_status`, `device_snapshot`,
  `tail_log`, `recipe_status`, `validate_recipe`), where a retry is always safe. For
  `upload_recipe`, `start_recipe`, `abort_recipe`, and `device_command`, the original
  request may have already reached and executed on the server before the response was
  lost, so retrying could duplicate the effect (e.g. a valve toggled twice, a recipe
  started twice). The bridge instead surfaces the connection error to the caller without
  re-sending. On that error, do not assume the command failed — re-query `recipe_status`
  and/or `device_snapshot` (see §5) to learn whether it actually took effect before
  deciding whether to retry it yourself.

## 8. Post-run artifacts → ELN (`list_runs`, `fetch_run_files`; added 2026-09-04)

Two `Read`-tier tools (the bridge's 16th and 17th) let an agent with **no file tools of
its own** get a finished run's files from the Control machine onto the computer where
the ELN MCP server runs, so it can attach them with the ELN's `upload_file`:

1. `list_runs(count)` — the most recent runs, newest first. Each carries a `runId`, the
   recipe name, start/end time, `outcome` (`complete` / `failed` / `aborted`, `running`
   for the run in progress, `unknown` when no process log covers it) and the artifacts
   that exist for it. Record the `runId` on the ELN item.
2. `fetch_run_files(runId = "latest", kinds = "tsv,log,recipe,snapshot")` — copies the
   named artifacts over the control wire (base64, 1 MiB chunks, so a 3 MB TSV is a few
   round trips) into `<downloadDir>\<runId>\` and returns each file's **absolute local
   path**, byte count and SHA-256. Pass those paths straight to `upload_file`. Kinds the
   machine does not have come back in `notAvailable`, not as errors.

What a run is, and what it leaves behind:

| Kind | File | Exists when |
|---|---|---|
| `tsv` | DataLogger time series, `Data\…\yyyy-MM-dd_HH-mm-ss_<recipe>.tsv` | Recipe logging on (default for user recipes). **The run id is this file's stem**; a run with no TSV is not listed. |
| `log` | The window of `Logs\Process_<launch>.log` from `Recipe started:` to `Recipe complete/failed/aborted:` for this run, with a one-line header naming the source file | The process log of the Control launch that ran it still exists |
| `recipe` | The `.rcpx` in the Recipes tree with the run's name (what `upload_recipe` saved) | Still on disk under that name |
| `snapshot` | The signed `.depo.json` beside the TSV whose `outcome.dataFile` names it | Operator-authorised runs only; runs started over this bridge have none |

Paths never cross the wire in either direction: the agent names a run id and a kind,
Control resolves the file itself, and the bridge decides where it lands. `start_recipe`
cannot return the run id synchronously (the TSV is opened on the Core thread after the
start is queued); call `list_runs(1)` a moment after starting, or after the run ends.

## 9. Session authority and planning (`describe_machine`, `compute_deposition_plan`)

`describe_machine` also reports the key's own authority: `sessionTier` (`Read`, `Recipe` or
`Manual`), `allowedMutatingTools`, and `canExecuteMutatingNow` (true only when the tier is `Recipe`
or higher and `controlMode` is `Remote`).

`describe_machine` also reports `isEmulator` (boolean) and `executionTarget` (`"emulator"` or
`"physical"`). `isEmulator` is true only when every PLC address and every LAN device host in the
loaded `HWConf.xml` is a loopback address (`localhost`, `127.x.x.x`, `::1`) and no device uses a
serial port; otherwise it is false. A profile can state the answer itself with
`<ExecutionTarget>emulator</ExecutionTarget>` or `<ExecutionTarget>physical</ExecutionTarget>` under
the root of its `Config.xml`; that value overrides the derived one.

`recipe_status` carries a run receipt for the current or last run: `basePressureLogQuotes` is an array
of the exact base-pressure lines Control logged at run start (`Base pressure: 1.50E-006 Torr` from
sensor `pm`; without a `pm` sensor, one `Base pressure (<chamber>, <sensor>): <value> Torr` line per
chamber with a connected pressure sensor), and `executedDepositsCount` is the number of `deposit`
steps that have completed. Quote the strings verbatim. Both reset when the next run starts.

`compute_deposition_plan` (Read tier) is pure arithmetic on the machine: dwells, rounded and
whole-second dwells, and the period they give. Its `measurementStatus` is
`planned_from_rate_and_dwell_not_measured`; it touches no hardware and a bad input fails with
`bad_request`.

## 10. `validate_recipe` response (`findings`, `xmlLine`, `scriptSummary`)

`validate_recipe` returns `valid` (false when any finding has severity `error`), `findings`, and
`scriptSummary`. Each finding has `line`, `column`, `severity`, `message`. `line` is the line in the
**validated script**: for an `.rcpx` this is the generated `var` block (one line per `<Param>`)
followed by the `<Script>` text, so it is *not* the line in the XML file. When `isRcpx` is true and
the finding sits in the `<Script>` text, the finding also carries `xmlLine`, the line of the same
text in the submitted `.rcpx`. Findings in the generated `var` block, or with line 0, have no
`xmlLine`. With `isRcpx` false there is no `xmlLine` anywhere.

`scriptSummary` is computed from the script text alone, without running anything:

- `depositSecondsBySource.<source>`: `outsideLoopSeconds`, `inLoopSeconds`, `totalSeconds` (2 decimals).
  Both `deposit <source> <seconds>` and `deposit <source> <seconds> <retract>` are counted. `var`
  values are substituted (`$name` or the bare name), `loop N` / `endloop` multiply, nested loops
  multiply together. "In loop" means inside at least one `loop`. `if` branches are all counted.
- `totalDepositSeconds`: sum over all sources.
- `hasUnresolvedVariables`: true when a deposit time, deposit source or loop count could not be
  resolved to a literal. Those deposits are left out (an unresolved loop count counts as 1), so the
  totals are then incomplete.
- `pwaitTimeoutsNote`: fixed text. `pwait`/`swait` timeouts are ceilings and are never added to the
  deposit totals.
- `inertCommentLinesWithSemicolon`: array of `{scriptLine, xmlLine}` (`xmlLine` only for `.rcpx`)
  for lines whose `#` or `//` comment contains `;`. Such text is inert: it is stripped before the
  line is split into statements, so it is never executed and never a finding.
- `commentsNote`: fixed text stating the same rule.

## Build & test

```powershell
dotnet build
dotnet test
```

Run from the `Mcp` directory. Both should be
clean/all-green before registering the bridge with a client.
