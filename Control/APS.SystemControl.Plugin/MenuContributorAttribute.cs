namespace APS.SystemControl.Plugin;

/// <summary>
/// Marks a class for menu-contributor discovery. The class must implement
/// IMenuContributor and have a public parameterless constructor (the
/// composer instantiates it via Activator.CreateInstance).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class MenuContributorAttribute : Attribute
{
}
