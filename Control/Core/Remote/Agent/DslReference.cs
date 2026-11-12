namespace APS.SystemControl.Core.Remote.Agent;

/// <summary>Hand-maintained DSL cheat sheet served by describe.dsl so the reference always matches the installed Control version.</summary>
internal static class DslReference
{
    internal const string Text = """
        APS recipe DSL — one command per line, '#' comments.
        Core commands:
          valve <id> open|close            MFC <id> <sccm>          pump <id> on|off
          delay <n>[s|m|h]                 stage <id> <pos>
          pwait [<sensor>] <target> <timeoutSec> # arity is WORKER-DERIVED. Where pwait binds a throttle
                                                 # controller the canonical form is
                                                 # 2 args; a leading <sensor> validates but is IGNORED and
                                                 # is never waited on — convergence always reads the bound
                                                 # controller. Multi-chamber workers require the sensor.
                                                 # Target ALWAYS carries a unit suffix.
                                                 # Completes only when the reading has HELD within ±10 % of
                                                 # the target for 6 consecutive 0.5 s samples (3 s); a
                                                 # transient passing through the band does not count.
                                                 # Target 0 = open the throttle and return at once; a target
                                                 # at/below the gauge floor (0.01 mTorr) opens the throttle
                                                 # and waits for the gauge to bottom out.
          swait <sensor> <target> <timeoutSec>   # passive wait for any sensor — use this for pl, NOT pwait
          deposit <source> <seconds> <retract>   # source by effective name (Sources.xml name, else material);
                                                 # <retract> = where the SHUTTER parks afterwards (Home = closed);
                                                 # the holder stays over <source> — move it with `holder`
          holder <pos>                     shutter <pos>   # ONE position each, by source name (Home = shutter
                                                          # closed / holder parked). shutter takes NO
                                                          # open/close word and no separate source arg.
          set <psu> <Param> <value>        # <Param> must be a name the PSU's driver accepts (exact
                                           # spelling; lists differ per model, and some models also
                                           # take short aliases). validate_recipe
                                           # rejects any other name — do not guess.
        Pressure units: Torr with suffixes T/mT/uT/nT/kT (e.g. 50mT, 3.5uT). Numbers with suffixes are single tokens.
        PSU rule: setting a power level does NOT energize the output — always `set <psu> <Setpoint> <value>` AND `power <psu> on`.
        Warmup/ramp times are SECONDS. Aborting a recipe runs the failure protocol.
        Structured .rcpx recipes carry a <Recipe> XML wrapper with parameters; the executable script is the
        parameter var-block plus the script body.
        Control mode: upload_recipe, start_recipe, abort_recipe, resolve_pause and device_command run ONLY while
        Control is in Online-Remote (system_status.controlMode == "Remote"); only an operator can switch it, at
        the console under Service → Control mode…; a host cannot, so if it is Local or Offline do not poll for a
        change — report it and stop.
        """;
}
