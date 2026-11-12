using System;
using System.Reflection;
using System.Windows.Forms;
using APS.SystemControl.Core.Classes;
using APS.SystemControl.Forms;
using Xunit;

namespace CoreLogicTests;

/// <summary>
/// frmSources mutates the live Source objects on every keystroke, so Cancel/X must
/// restore everything SaveFieldToSource can touch (audit M-15), and the read-only lock
/// must cover it too: stale ZeroDistanceMm/LiftPositionMm values feed BuildConditions
/// and the signed .depo.json (audit C6, C7).
/// </summary>
public class SourcesDialogGeometryGuardTests
{
    private static SourceManager NewManager(out Source s1)
    {
        var sm = new SourceManager(6);
        s1 = sm.GetSource(1)!;
        s1.Name = "Cu source";
        s1.Material = "Cu";
        s1.ZeroDistanceMm = 100;
        s1.LiftPositionMm = 50;
        return sm;
    }

    private static T Field<T>(object o, string name) =>
        (T)o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o)!;

    /// <summary>Raises FormClosing without needing a shown window (Close() on a
    /// handle-less form disposes without firing the event).</summary>
    private static void RaiseFormClosing(Form frm) =>
        typeof(Form).GetMethod("OnFormClosing", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(frm, [new FormClosingEventArgs(CloseReason.UserClosing, false)]);

    [Fact]
    public void Cancel_rolls_back_zero_and_lift_edits()
    {
        var sm = NewManager(out var s1);
        using var frm = new frmSources(sm);

        // Simulate the operator spinning the editors — ValueChanged runs
        // SaveFieldToSource, which mutates the live Source immediately.
        Field<NumericUpDown>(frm, "_numZero").Value = 55;
        Field<NumericUpDown>(frm, "_numLift").Value = 12;
        Assert.Equal(55d, s1.ZeroDistanceMm);

        RaiseFormClosing(frm);                  // DialogResult != OK → rollback

        Assert.Equal(100d, s1.ZeroDistanceMm);
        Assert.Equal(50d, s1.LiftPositionMm);
    }

    [Fact]
    public void Cancel_rolls_back_catalog_edits()
    {
        var sm = NewManager(out _);
        sm.Magnetrons = new() { new MagnetronDef("M1", "Model 1") };
        sm.PowerSources = new() { "PSU-A" };
        sm.Formats = new() { new SourceFormat("f1", "Format 1") };
        sm.MagnetronInstances = new()
        {
            new MagnetronInstance(Guid.NewGuid().ToString(), "M1 #1", "SN-1", "M1", "f1", "{}", "spare", "Active")
        };
        using var frm = new frmSources(sm);

        // Simulate what the BtnEdit… handlers do when their dialog returns OK:
        // replace the catalog lists wholesale on the live SourceManager.
        sm.Magnetrons = new() { new MagnetronDef("M2", "Model 2") };
        sm.PowerSources = new() { "OTHER" };
        sm.Formats = new();
        sm.MagnetronInstances = new();

        RaiseFormClosing(frm);   // DialogResult != OK → rollback

        Assert.Equal("M1", Assert.Single(sm.Magnetrons).Id);
        Assert.Equal("PSU-A", Assert.Single(sm.PowerSources));
        Assert.Equal("f1", Assert.Single(sm.Formats).Id);
        Assert.Equal("M1 #1", Assert.Single(sm.MagnetronInstances).Label);
    }

    [Fact]
    public void ReadOnly_mode_disables_zero_and_lift_editors()
    {
        var sm = NewManager(out _);
        using var frm = new frmSources(sm, readOnly: true);

        Assert.False(Field<NumericUpDown>(frm, "_numZero").Enabled);
        Assert.False(Field<NumericUpDown>(frm, "_numLift").Enabled);
    }

    [Fact]
    public void ReadOnly_mode_never_writes_geometry_to_the_live_source()
    {
        var sm = NewManager(out var s1);
        using var frm = new frmSources(sm, readOnly: true);

        // Even a programmatic value change fires ValueChanged; the save path
        // itself must refuse to touch the live Source while read-only.
        Field<NumericUpDown>(frm, "_numZero").Value = 55;

        Assert.Equal(100d, s1.ZeroDistanceMm);
    }
}
