using System.Reflection;
using APS.SystemControl.Core.Classes;
using APS.SystemControl.Forms;

namespace CoreLogicTests;

/// <summary>
/// Edits are written to the live Source on every keystroke, but a NumericUpDown only
/// commits typed text when it loses focus, and the schematic canvas never takes focus.
/// The dialog must commit pending numeric text before switching flanges, and Save must
/// persist without closing.
/// </summary>
public class SourcesDialogEditRetentionTests
{
    private static T Field<T>(object o, string name) =>
        (T)o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o)!;

    private static void Call(object o, string name, params object?[] args) =>
        o.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(o, args);

    /// <summary>Types into the spinner the way a keyboard does: the inner edit box changes,
    /// Value does not follow until the control validates (focus loss / Enter). Setting
    /// NumericUpDown.Text directly would commit immediately and hide the trap.</summary>
    private static void TypeInto(NumericUpDown n, string text) =>
        n.Controls.OfType<TextBox>().First().Text = text;

    private static void RaiseFormClosing(Form frm) =>
        typeof(Form).GetMethod("OnFormClosing", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(frm, [new FormClosingEventArgs(CloseReason.UserClosing, false)]);

    private static SourceManager TwoSources(out Source s1, out Source s2)
    {
        var sm = new SourceManager(6);
        s1 = sm.GetSource(1)!; s1.Name = "Cu"; s1.Material = "Cu"; s1.ZeroDistanceMm = 100;
        s2 = sm.GetSource(2)!; s2.Name = "W";  s2.Material = "W";  s2.ZeroDistanceMm = 90;
        return sm;
    }

    [Fact]
    public void Committed_edits_survive_switching_to_another_flange_and_back()
    {
        var sm = TwoSources(out var s1, out _);
        using var frm = new frmSources(sm);

        Call(frm, "SelectFlange", 1);
        Field<TextBox>(frm, "_txtName").Text = "Cu-edited";
        Field<NumericUpDown>(frm, "_numZero").Value = 123;

        Call(frm, "SelectFlange", 2);
        Assert.Equal("W", Field<TextBox>(frm, "_txtName").Text);
        Call(frm, "SelectFlange", 1);

        Assert.Equal("Cu-edited", Field<TextBox>(frm, "_txtName").Text);
        Assert.Equal(123m, Field<NumericUpDown>(frm, "_numZero").Value);
        Assert.Equal("Cu-edited", s1.Name);
        Assert.Equal(123d, s1.ZeroDistanceMm);
    }

    [Fact]
    public void Typed_but_uncommitted_distance_is_kept_when_another_flange_is_selected()
    {
        var sm = TwoSources(out var s1, out _);
        using var frm = new frmSources(sm);
        Call(frm, "SelectFlange", 1);

        // Typing into the spinner changes its edit text only; ValueChanged (and so
        // SaveFieldToSource) does not fire until the control validates, which a click on
        // the schematic canvas never causes. (Reading .Value here would itself validate,
        // so the uncommitted state is observed on the live Source instead.)
        var zero = Field<NumericUpDown>(frm, "_numZero");
        TypeInto(zero, "77.5");
        Assert.Equal(100d, s1.ZeroDistanceMm);   // still uncommitted — this is the trap

        Call(frm, "SelectFlange", 2);
        Assert.Equal(77.5d, s1.ZeroDistanceMm);  // committed before the switch

        Call(frm, "SelectFlange", 1);
        Assert.Equal(77.5m, Field<NumericUpDown>(frm, "_numZero").Value);
    }

    [Fact]
    public void Save_persists_and_keeps_the_dialog_open_and_a_later_close_does_not_roll_back()
    {
        var sm = TwoSources(out var s1, out _);
        string file = Path.Combine(Path.GetTempPath(), $"sources-{Guid.NewGuid():N}.xml");
        try
        {
            using var frm = new frmSources(sm, filePath: file);
            Call(frm, "SelectFlange", 1);
            Field<TextBox>(frm, "_txtName").Text = "Cu-saved";
            TypeInto(Field<NumericUpDown>(frm, "_numZero"), "64");   // typed, not yet committed

            Call(frm, "BtnSave_Click", null, EventArgs.Empty);

            Assert.False(frm.IsDisposed);
            Assert.NotEqual(DialogResult.OK, frm.DialogResult);   // still open, not confirmed away
            Assert.True(File.Exists(file));
            Assert.Contains("Cu-saved", File.ReadAllText(file));
            Assert.Equal(64d, s1.ZeroDistanceMm);

            // Closing afterwards must not undo what was saved.
            RaiseFormClosing(frm);
            Assert.Equal("Cu-saved", s1.Name);
            Assert.Equal(64d, s1.ZeroDistanceMm);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Edits_after_a_save_still_roll_back_on_close()
    {
        var sm = TwoSources(out var s1, out _);
        string file = Path.Combine(Path.GetTempPath(), $"sources-{Guid.NewGuid():N}.xml");
        try
        {
            using var frm = new frmSources(sm, filePath: file);
            Call(frm, "SelectFlange", 1);
            Field<TextBox>(frm, "_txtName").Text = "Cu-saved";
            Call(frm, "BtnSave_Click", null, EventArgs.Empty);

            Field<TextBox>(frm, "_txtName").Text = "Cu-abandoned";
            RaiseFormClosing(frm);

            Assert.Equal("Cu-saved", s1.Name);
        }
        finally { File.Delete(file); }
    }
}
