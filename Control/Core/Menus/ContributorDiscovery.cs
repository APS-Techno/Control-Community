using System.Reflection;
using APS.SystemControl.Plugin;

namespace APS.SystemControl.Core.Menus;

/// <summary>
/// Two complementary discovery paths:
///   • DiscoverFromAssemblies — reflection scan for [MenuContributor] types,
///     instantiates each via parameterless ctor. Use for HW/GUI libraries
///     that ship pure menu providers (no panel needed).
///   • DiscoverFromInstances — filter an existing collection (e.g. loaded
///     panels) for objects already implementing IMenuContributor.
/// Both paths are loud: they catch reflection errors per-type and continue,
/// so one broken contributor cannot prevent the rest from loading.
/// </summary>
internal static class ContributorDiscovery
{
    public static IReadOnlyList<IMenuContributor> DiscoverFromAssemblies(
        IEnumerable<Assembly> assemblies,
        Action<string>? onError = null)
    {
        var result = new List<IMenuContributor>();
        foreach (var asm in assemblies)
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex)
            {
                onError?.Invoke($"GetTypes failed for {asm.GetName().Name}: {ex.Message}");
                types = ex.Types.Where(t => t != null).ToArray()!;
            }

            foreach (var t in types)
            {
                if (t == null) continue;
                if (!t.IsClass || t.IsAbstract) continue;
                if (t.GetCustomAttribute<MenuContributorAttribute>() == null) continue;
                if (!typeof(IMenuContributor).IsAssignableFrom(t))
                {
                    onError?.Invoke($"{t.FullName} has [MenuContributor] but does not implement IMenuContributor.");
                    continue;
                }
                if (t.GetConstructor(Type.EmptyTypes) == null)
                {
                    onError?.Invoke($"{t.FullName} has [MenuContributor] but lacks a public parameterless ctor.");
                    continue;
                }
                try
                {
                    var inst = (IMenuContributor)Activator.CreateInstance(t)!;
                    result.Add(inst);
                }
                catch (Exception ex)
                {
                    onError?.Invoke($"{t.FullName} ctor threw: {ex.Message}");
                }
            }
        }
        return result;
    }

    public static IEnumerable<IMenuContributor> DiscoverFromInstances(IEnumerable<object> instances)
        => instances.OfType<IMenuContributor>();
}
