using System;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace APS.SystemControl.Core.CoreClass;

/// <summary>
/// Resolves a worker operation method by name, disambiguating overloads by
/// parameter arity. The arity expected from Worker.xml is the var count; a
/// method may take one extra trailing <see cref="CancellationToken"/> param.
/// A single same-named method is returned regardless of varCount (back-compat
/// with the previous <c>Type.GetMethod(name)</c> behaviour).
/// </summary>
internal static class WorkerMethodResolver
{
    public static MethodInfo? Resolve(Type opsType, string methodName, int varCount)
    {
        var candidates = opsType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => string.Equals(m.Name, methodName, StringComparison.Ordinal))
            .ToList();

        if (candidates.Count == 0) return null;
        if (candidates.Count == 1) return candidates[0];

        foreach (var m in candidates)
        {
            var ps = m.GetParameters();
            bool wantsCt = ps.Length > 0 && ps[^1].ParameterType == typeof(CancellationToken);
            int effective = wantsCt ? ps.Length - 1 : ps.Length;
            if (effective == varCount) return m;
        }
        return null;
    }
}
