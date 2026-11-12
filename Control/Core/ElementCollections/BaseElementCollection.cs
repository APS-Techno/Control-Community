using System.Collections;
using APS.SystemControl.Core.HWClasses.Base;

namespace APS.SystemControl.Core;

public class Elements : IEnumerable<KeyValuePair<string, IBaseComponent>>
{
    public Dictionary<string, IBaseComponent> _elements;

    public Elements() => _elements = [];

    public IEnumerator<KeyValuePair<string, IBaseComponent>> GetEnumerator()
    {
        return _elements.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public void Add(string name, IBaseComponent element)
    {
        // Trim the key: an element with child elements (the <robot>) has a Value
        // padded with inter-element whitespace, which must not leak into the
        // lookup key or FindElement(id) misses it. No-op for normal device ids.
        _elements.Add(name.Trim(), element);
    }

    public int Count => _elements.Count;

    public IBaseComponent? this[string name]
    {
        get
        {
            _elements.TryGetValue(name, out IBaseComponent? value);
            return value;
        }
    }

    /// <summary>
    /// Returns the element at the given insertion-order index (0-based).
    /// Used for numeric recipe shorthand (e.g. "valve 10" → 10th valve).
    /// </summary>
    public IBaseComponent? ByIndex(int index)
    {
        if (index < 0 || index >= _elements.Count) return null;
        return _elements.Values.ElementAt(index);
    }
}