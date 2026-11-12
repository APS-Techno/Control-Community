namespace APS.Accounts.Domain;

public static class BuiltInRoles
{
    public const string Operator = "Operator";
    public const string Engineer = "Engineer";
    public const string Admin    = "Admin";

    public const string LocalAdminUsername = "local-admin";

    private static readonly Permission[] OperatorPerms =
    {
        Permission.RunRecipe,
    };

    private static readonly Permission[] EngineerPerms =
    {
        Permission.RunRecipe,
        Permission.SignOffRecipe,
        Permission.EditRecipe,
        Permission.SafetyOverride,
        Permission.Calibration,
        Permission.ServiceMenu,
        Permission.ManageTargets,
    };

    private static readonly Permission[] AdminPerms =
        Enum.GetValues<Permission>();   // everything, including QuotaExempt

    public static IReadOnlyDictionary<string, IReadOnlyList<Permission>> Definitions { get; } =
        new Dictionary<string, IReadOnlyList<Permission>>
        {
            [Operator] = OperatorPerms,
            [Engineer] = EngineerPerms,
            [Admin]    = AdminPerms,
        };

    public static bool IsBuiltIn(string roleName) => Definitions.ContainsKey(roleName);

    /// <summary>
    /// The principal seated in Standalone mode (no DB) and offered as break-glass in
    /// Networked mode. Full permissions, quota-exempt, flagged offline.
    /// </summary>
    public static AuthenticatedPrincipal CreateLocalAdmin() => new(
        userId: Guid.Empty,
        username: LocalAdminUsername,
        displayName: "Local Administrator",
        roles: new[] { Admin },
        permissions: AdminPerms,
        isOffline: true,
        sessionId: Guid.NewGuid());
}
