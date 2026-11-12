using APS.SystemControl.Forms;

namespace APS.SystemControl.Tests.Overrides;

/// <summary>
/// frmOverrides.ApplyToggle must rewrite RuntimeOverrides.xml when an active override's
/// Persist checkbox is UN-ticked; otherwise the persisted entry stays in the file and the
/// next start silently re-arms the override as persisted (audit C12).
/// </summary>
public class SaveNeededTests
{
    [Fact]
    public void Unticking_persist_on_a_previously_persisted_override_requires_save()
        => Assert.True(frmOverrides.SaveNeeded(disabled: true, persist: false, previousPersisted: true));

    [Fact]
    public void Persisted_disable_requires_save()
        => Assert.True(frmOverrides.SaveNeeded(disabled: true, persist: true, previousPersisted: false));

    [Fact]
    public void Reenabling_requires_save_to_remove_any_disk_entry()
        => Assert.True(frmOverrides.SaveNeeded(disabled: false, persist: false, previousPersisted: true));

    [Fact]
    public void Session_only_override_with_nothing_on_disk_skips_the_write()
        => Assert.False(frmOverrides.SaveNeeded(disabled: true, persist: false, previousPersisted: false));
}
