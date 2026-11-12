using System.Globalization;
using APS.SystemControl.Core;
using APS.SystemControl.Core.Classes;

namespace CoreLogicTests;

/// <summary>
/// On a comma-decimal locale (e.g. ru-RU) a culture-sensitive parse of a dotted config literal
/// such as the interlock threshold "5.0e-2" fails, the value stays a string, and a later
/// <c>(float)</c> cast throws, aborting every <c>*_GV open</c> interlock and the whole
/// SYSTEM_START recipe. Config numerics must parse with InvariantCulture.
/// </summary>
public class LocaleNumberParsingTests : IDisposable
{
    private readonly CultureInfo _original = CultureInfo.CurrentCulture;

    public LocaleNumberParsingTests()
    {
        // ru-RU uses ',' as the decimal separator, so "5.0e-2" is unparseable under it.
        CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
    }

    public void Dispose() => CultureInfo.CurrentCulture = _original;

    [Theory]
    [InlineData("5.0e-2", 0.05f)]   // the exact interlock GV-open threshold
    [InlineData("1.5e-3", 0.0015f)]
    [InlineData("0.5", 0.5f)]
    public void Obj_parses_dotted_float_literal_under_comma_locale(string input, float expected)
    {
        object? result = Core.Obj(input);
        float f = Assert.IsType<float>(result);
        Assert.Equal(expected, f, 6);
    }

    [Fact]
    public void Obj_keeps_integer_and_bool_forms()
    {
        Assert.IsType<int>(Core.Obj("60"));
        Assert.IsType<bool>(Core.Obj("true"));
    }

    [Fact]
    public void Alarm_loader_parses_dotted_threshold_under_comma_locale()
    {
        string path = Path.Combine(Path.GetTempPath(), $"alarms_locale_{Guid.NewGuid():N}.xml");
        File.WriteAllText(path,
            "<alarms><chamber id=\"C1\"><states initial=\"idle\" />" +
            "<alarm sensor=\"C1_pm\" cond=\"more\" val=\"1.5e-3\" state=\"idle\">x</alarm>" +
            "</chamber></alarms>");
        try
        {
            var result = AlarmConfigLoader.Load(path, deviceResolver: null);
            var rule = result.ChamberAlarms["C1"].Rules.Single(r => r.Sensor == "C1_pm");
            Assert.Equal(0.0015f, rule.Value, 6);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("16.9", 16.9f)]   // the exact C3 deposit dwell ($C3_Run3_Time) that crashed the recipe
    [InlineData("10", 10f)]
    [InlineData("0.5", 0.5f)]
    public void ConvertStringToType_parses_dotted_float_arg_under_comma_locale(string input, float expected)
    {
        object? result = CoreThread.ConvertStringToType(input, typeof(float));
        float f = Assert.IsType<float>(result);
        Assert.Equal(expected, f, 6);
    }

    [Fact]
    public void ConvertStringToType_parses_int_arg_under_comma_locale()
    {
        Assert.Equal(60, Assert.IsType<int>(CoreThread.ConvertStringToType("60", typeof(int))));
    }
}
