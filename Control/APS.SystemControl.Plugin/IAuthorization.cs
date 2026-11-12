namespace APS.SystemControl.Plugin;

/// <summary>
/// Permission check exposed to menu contributors and the composer. String-keyed so the
/// Plugin assembly stays free of an APS.Accounts dependency; the host maps the key to its
/// permission enum.
/// </summary>
public interface IAuthorization
{
    bool Can(string permission);
}

/// <summary>Allows everything — the default for tests and non-gated hosts.</summary>
public sealed class PermissiveAuthorization : IAuthorization
{
    public static readonly PermissiveAuthorization Instance = new();
    public bool Can(string permission) => true;
}
