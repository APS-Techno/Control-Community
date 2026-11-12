namespace APS.Accounts.Domain;

/// <summary>
/// Kept as an enum (not free-form strings) so every gate check in Control is exhaustive
/// and auditable.
/// </summary>
public enum Permission
{
    SafetyOverride,   // set/clear alarm & interlock overrides
    ServiceMenu,
    Calibration,      // drive/holder/shutter calibration
    EditRecipe,       // create/edit/save recipes

    RunRecipe,        // start a run (stamps the authenticated user)
    SignOffRecipe,    // approve/release a run

    QuotaExempt,      // session does not consume the time budget

    ManageUsers,      // create/edit/disable users, reset passwords
    ManageRoles,      // create/edit custom roles
    ManageQuotas,     // set per-user time allowances
    ManageConfig,     // edit system configs, switch accounts mode
    ManageTargets,    // add/edit targets, install/remove, set depths & limits
    ViewAuditLog,
}
