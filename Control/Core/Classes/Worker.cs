using System.Collections;
using APS.SystemControl.Core;

namespace APS.SystemControl.Core.Classes;

/// <summary>
/// Async operation delegate. The <see cref="CancellationToken"/> flows from
/// the recipe-execution pipeline so long-running operations (e.g. Warmup)
/// can abort promptly instead of waiting out a blocking delay.
/// </summary>
public delegate Task<object?> OperationDelegate(object[] args, CancellationToken ct);

public class Operation(OperationDelegate? function, Type returnType, Type[] args, DeviceType target = DeviceType.System, string? device = null, int minArgs = -1)
{
    public OperationDelegate? F { get; set; } = function;
    public Type Type { get; set; } = returnType;
    public Type[] Args { get; set; } = args;
    public DeviceType Target { get; set; } = target;
    /// <summary>
    /// When set, the dispatch auto-injects this named device as the first arg
    /// instead of reading a device name from the recipe line.
    /// </summary>
    public string? Device { get; set; } = device;
    /// <summary>
    /// How many of <see cref="Args"/> the recipe MUST supply. Trailing slots
    /// beyond this are optional: the executor leaves them null and the op
    /// applies its own default — e.g. <c>pump SP1 on [seconds]</c>, where the
    /// spin-up wait defaults to none.
    ///
    /// -1 (the default) means "all of them". This lives on the operation rather
    /// than at the call site because the pre-run validator reads its arity
    /// straight off this table; an optional slot that only the executor knew
    /// about would be reported to the operator as a missing argument.
    /// </summary>
    public int MinArgs { get; set; } = minArgs < 0 ? args.Length : minArgs;
}

public class Worker : IEnumerable<KeyValuePair<string, Operation>>
{
    private readonly Dictionary<string, Operation> _items;

    public Worker()
    {
        _items = [];
    }

    public IEnumerator<KeyValuePair<string, Operation>> GetEnumerator()
    {
        return _items.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public void Add(string name, OperationDelegate function, Type fType, Type[] args, DeviceType target = DeviceType.System, string? device = null, int minArgs = -1)
    {
        _items.Add(name, new Operation(function, fType, args, target, device, minArgs));
    }

    public bool Remove(string name)
    {
        return _items.Remove(name);
    }

    public Operation? this[string name]
    {
        get
        {
            _items.TryGetValue(name, out Operation? Function);
            return Function;
        }
    }

}


