using System.Globalization;
using System.Xml.Linq;

namespace EmulatorCore.Engine;

public static class HwConfAttributes
{
    public static int ParseInt(XElement element, string attributeName)
    {
        string? value = element.Attribute(attributeName)?.Value;
        return value != null ? int.Parse(value) : 0;
    }

    public static float ParseFloat(XElement element, string attributeName)
    {
        string? value = element.Attribute(attributeName)?.Value;
        return value != null ? float.Parse(value, CultureInfo.InvariantCulture) : 0f;
    }

    public static int ParseIntAttr(XElement element, string attributeName, int defaultValue = 0)
    {
        string? value = element.Attribute(attributeName)?.Value;
        return value != null ? int.Parse(value) : defaultValue;
    }

    public static int ParseIntAttrDefault(XElement element, string attributeName, int defaultValue)
        => ParseIntAttr(element, attributeName, defaultValue);
}
