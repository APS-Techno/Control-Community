using APS.Accounts.Services;

namespace APS.SystemControl.Hosting;

public sealed class ControlEdition
{
    public static ControlEdition Community { get; } = new(
        "APS Control Community",
        new StandaloneStartupGate(),
        new AllFeaturesPolicy(),
        Array.Empty<IControlModule>());

    internal ControlEdition(string productName, IStartupGate startup, IFeaturePolicy features,
        IReadOnlyList<IControlModule> modules)
    {
        ProductName = productName;
        Startup = startup;
        Features = features;
        Modules = modules;
    }

    internal string ProductName { get; }

    internal IStartupGate Startup { get; }

    internal IFeaturePolicy Features { get; }

    internal IReadOnlyList<IControlModule> Modules { get; }

    internal T? Find<T>() where T : class
    {
        T? found = null;
        foreach (var module in Modules)
        {
            if (module is not T match) continue;
            if (found is not null) return null;
            found = match;
        }
        return found;
    }
}
