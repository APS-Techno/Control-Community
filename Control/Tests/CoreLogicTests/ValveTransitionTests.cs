using APS.SystemControl.Core;
using APS.SystemControl.Core.Classes;

namespace CoreLogicTests;

public class ValveTransitionTests
{
    private static Alarms MakeLoadlockSystem()
    {
        var a = new Alarms();
        // Real rule shape: opening A1 transitions Idle → Startup only if
        // GVM is already closed (can't vent with the gate open).
        a.AddValveTransition(
            SystemState.Idle, SystemState.Startup,
            valveId: "A1", valveOpen: true,
            requirements: ["GVM closed"]);
        return a;
    }

    [Fact]
    public void Fires_when_valve_opens_and_requirements_met()
    {
        var a = MakeLoadlockSystem();
        Assert.Equal(SystemState.Startup,
            a.CheckValveTransition(SystemState.Idle, "A1", isOpen: true,
                valveStateResolver: id => id == "GVM" ? false : null));
    }

    [Fact]
    public void Does_not_fire_when_required_sibling_wrong_state()
    {
        var a = MakeLoadlockSystem();
        Assert.Null(a.CheckValveTransition(SystemState.Idle, "A1", isOpen: true,
            valveStateResolver: id => id == "GVM" ? true : null));
    }

    [Fact]
    public void Does_not_fire_when_required_sibling_unknown()
    {
        // Resolver returns null → sibling state unknown → fail safe.
        var a = MakeLoadlockSystem();
        Assert.Null(a.CheckValveTransition(SystemState.Idle, "A1", isOpen: true,
            valveStateResolver: _ => null));
    }

    [Fact]
    public void Does_not_fire_from_wrong_current_state()
    {
        var a = MakeLoadlockSystem();
        Assert.Null(a.CheckValveTransition(SystemState.Processing, "A1", isOpen: true,
            valveStateResolver: _ => false));
    }

    [Fact]
    public void Does_not_fire_for_different_valve()
    {
        var a = MakeLoadlockSystem();
        Assert.Null(a.CheckValveTransition(SystemState.Idle, "PP", isOpen: true,
            valveStateResolver: _ => false));
    }

    [Fact]
    public void Close_action_does_not_fire_open_rule()
    {
        var a = MakeLoadlockSystem();
        // Rule was declared for valveOpen=true — closing A1 shouldn't fire.
        Assert.Null(a.CheckValveTransition(SystemState.Idle, "A1", isOpen: false,
            valveStateResolver: _ => false));
    }

    [Fact]
    public void Multiple_requirements_all_must_match()
    {
        var a = new Alarms();
        a.AddValveTransition(
            SystemState.Idle, SystemState.Startup,
            valveId: "A1", valveOpen: true,
            requirements: ["GVM closed", "PP open"]);

        // GVM closed + PP open → fires.
        Assert.Equal(SystemState.Startup,
            a.CheckValveTransition(SystemState.Idle, "A1", isOpen: true,
                valveStateResolver: id => id switch
                {
                    "GVM" => false,
                    "PP"  => true,
                    _     => null,
                }));

        // GVM closed + PP closed → does not fire.
        Assert.Null(a.CheckValveTransition(SystemState.Idle, "A1", isOpen: true,
            valveStateResolver: _ => false));
    }

    [Fact]
    public void Empty_requirements_list_always_passes_requirement_check()
    {
        var a = new Alarms();
        a.AddValveTransition(
            SystemState.Idle, SystemState.Startup,
            valveId: "A1", valveOpen: true,
            requirements: []);

        Assert.Equal(SystemState.Startup,
            a.CheckValveTransition(SystemState.Idle, "A1", isOpen: true,
                valveStateResolver: _ => null));
    }

    [Fact]
    public void Malformed_requirement_string_does_not_crash()
    {
        // A single-word or empty requirement is silently skipped per the
        // current parser; assert we just don't explode.
        var a = new Alarms();
        a.AddValveTransition(
            SystemState.Idle, SystemState.Startup,
            valveId: "A1", valveOpen: true,
            requirements: ["garbled"]);

        // With the only requirement malformed, the all-met flag stays true
        // (no iteration actually failed) — transition should fire.
        Assert.Equal(SystemState.Startup,
            a.CheckValveTransition(SystemState.Idle, "A1", isOpen: true,
                valveStateResolver: _ => null));
    }
}
