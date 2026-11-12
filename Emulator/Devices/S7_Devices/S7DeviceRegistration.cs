using System.Globalization;
using EmulatorCore.Engine;
using EmulatorCore.Interfaces;
using static EmulatorCore.Engine.HwConfAttributes;

namespace APS.Emulator.Devices.S7;

public sealed class S7DeviceRegistration : IDeviceRegistration
{
    public void Register(IDeviceRegistry registry)
    {
        registry.Add("S7200Valve", CreateValve);
        registry.Add("S7200GasValve", CreateValve);
        registry.Add("S7200SequencedValve", CreateValve);
        registry.Add("S7200PositionValve", CreateValve);
        registry.Add("S7200MFC", CreateMfc);
        registry.Add("S7200Pump", CreatePump);
        registry.Add("S7200TurboPump", CreateTurboPump);
        registry.Add("S7200InputValve", CreateInputValve);
        registry.Add("S7200Sensor", CreateSensor);
        registry.Add("S7200RotationStage", _ => null);
        registry.Add("S7200Stage", CreateStage);
        registry.Add("S7200ThrottleValve", CreateThrottleValve);
    }

    public void Wire(IReadOnlyList<IDeviceEmulator> devices)
    {
        foreach (var device in devices)
        {
            if (device is TurboPumpEmulator turbo && turbo.BackingPumpId.Length > 0)
            {
                PumpEmulator? backing = null;
                foreach (var candidate in devices)
                    if (candidate is PumpEmulator pump && pump.Id.Equals(turbo.BackingPumpId, StringComparison.OrdinalIgnoreCase))
                        backing = pump;
                turbo.BindBackingPump(backing ?? throw new InvalidOperationException(
                    $"Turbo '{turbo.Id}' references missing backing pump '{turbo.BackingPumpId}'."));
            }
        }
        foreach (var device in devices)
        {
            if (device is SensorEmulator sensor && sensor.PressureSourceId.Length > 0)
            {
                TurboPumpEmulator? source = null;
                foreach (var candidate in devices)
                    if (candidate is TurboPumpEmulator turbo && turbo.Id.Equals(sensor.PressureSourceId, StringComparison.OrdinalIgnoreCase))
                        source = turbo;
                sensor.BindPressureSource(source ?? throw new InvalidOperationException(
                    $"Sensor '{sensor.Id}' references missing turbo source '{sensor.PressureSourceId}'."));
            }
        }
    }

    private static string Id(DeviceCreateContext ctx) => ctx.Element.Value.Trim();

    private static string Descr(DeviceCreateContext ctx) => ctx.Element.Attribute("descr")?.Value ?? "";

    private static IDeviceEmulator? CreateValve(DeviceCreateContext ctx)
    {
        var element = ctx.Element;
        string id = Id(ctx);
        string descr = Descr(ctx);
        int address = ParseInt(element, "address");
        int index = ParseInt(element, "index");
        float openDelay = ParseFloat(element, "delay");
        // Firmware-sequenced valves are commanded through a V-memory flag (DB1),
        // mirroring the real S7-200 ladder; status is still read back from the M-bit.
        // The command flag location/polarity is per-valve config (cmdaddr/cmdbit/cmdinvert),
        // so one device type serves PP, FL, and any per-chamber equivalents.
        if (ctx.ShortType.Equals("S7200SequencedValve", StringComparison.OrdinalIgnoreCase))
            return new ValveEmulator(id, address, index, descr, openDelay, ctx.Host,
                cmdDbByte: ParseInt(element, "cmdaddr"),
                cmdDbBit: ParseInt(element, "cmdbit"),
                cmdInvert: ParseInt(element, "cmdinvert") != 0);
        return new ValveEmulator(id, address, index, descr, openDelay, ctx.Host);
    }

    private static IDeviceEmulator? CreateMfc(DeviceCreateContext ctx)
    {
        var element = ctx.Element;
        string id = Id(ctx);
        int address = ParseInt(element, "address");
        string chamber = element.Attribute("chamber")?.Value
            ?? throw new InvalidOperationException(
                $"MFC '{id}' is missing the required chamber= attribute in HWConf.xml.");
        string inletValve = element.Attribute("valve")?.Value ?? "";
        return new MFCEmulator(id, address, Descr(ctx), chamber, ctx.Host, inletValve);
    }

    private static IDeviceEmulator? CreateSensor(DeviceCreateContext ctx)
    {
        var element = ctx.Element;
        string id = Id(ctx);
        int address = ParseInt(element, "address");
        string chamber = element.Attribute("chamber")?.Value
            ?? throw new InvalidOperationException(
                $"Sensor '{id}' is missing the required chamber= attribute in HWConf.xml.");
        string property = element.Attribute("property")?.Value ?? "Pressure";
        if (property != "Pressure" && property != "Temperature" &&
            property != "SimTime" && property != "ForelinePressure" && property != "Thickness")
            throw new InvalidOperationException(
                $"Sensor '{id}' has invalid property='{property}'. Allowed: Pressure, Temperature, SimTime, ForelinePressure, Thickness.");
        return new SensorEmulator(id, address, chamber, property, Descr(ctx), ctx.Host,
            minPressure: ParseFloat(element, "minPressure"),
            maxPressure: element.Attribute("maxPressure") != null ? ParseFloat(element, "maxPressure") : float.MaxValue,
            pressureSourceId: element.Attribute("source")?.Value ?? "");
    }

    private static IDeviceEmulator? CreatePump(DeviceCreateContext ctx)
    {
        var element = ctx.Element;
        string id = Id(ctx);
        int address = ParseInt(element, "address");
        int index = ParseInt(element, "index");
        float pumpSpeed = ParseFloat(element, "speed");
        if (pumpSpeed <= 0) pumpSpeed = ctx.Physics.ScrollPumpSpeed;
        string chamber = element.Attribute("chamber")?.Value
            ?? throw new InvalidOperationException(
                $"Pump '{id}' is missing the required chamber= attribute in HWConf.xml.");
        string gate = element.Attribute("gate")?.Value ?? "";
        return new PumpEmulator(id, address, index, pumpSpeed, Descr(ctx),
            chamberId: chamber, gateValveId: gate, host: ctx.Host);
    }

    private static IDeviceEmulator? CreateStage(DeviceCreateContext ctx)
    {
        var element = ctx.Element;
        int address = ParseInt(element, "address");
        int triggerAddr = ParseInt(element, "MB");
        int triggerBit = ParseInt(element, "index");
        // Optional dedicated stop bit (matches Control's S7200Stage stopByte/stopBit).
        int stopByte = int.TryParse(element.Attribute("haltByte")?.Value,
            NumberStyles.Integer, CultureInfo.InvariantCulture, out var sb) ? sb : -1;
        int stopBit = int.TryParse(element.Attribute("haltBit")?.Value,
            NumberStyles.Integer, CultureInfo.InvariantCulture, out var sbi) ? sbi : -1;
        return new StageEmulator(Id(ctx), address, triggerAddr, triggerBit, Descr(ctx), ctx.Host, stopByte, stopBit);
    }

    private static IDeviceEmulator? CreateTurboPump(DeviceCreateContext ctx)
    {
        var element = ctx.Element;
        string id = Id(ctx);
        int address = ParseInt(element, "address");
        int writeAddr = ParseInt(element, "writeaddr");
        int writeIndex = ParseInt(element, "writeindex");
        float fullSpeed = ParseFloat(element, "speed");
        if (fullSpeed <= 0) fullSpeed = ctx.Physics.TurboPumpSpeed;
        string chamber = element.Attribute("chamber")?.Value
            ?? throw new InvalidOperationException(
                $"Turbo pump '{id}' is missing the required chamber= attribute in HWConf.xml.");
        string gate = element.Attribute("gate")?.Value ?? "";
        string foreline = element.Attribute("foreline")?.Value ?? "";
        return new TurboPumpEmulator(id, address, writeAddr, writeIndex, fullSpeed, Descr(ctx),
            chamberId: chamber, gateValveId: gate, forelineValveId: foreline,
            spinUpTime: ctx.Physics.TurboSpinUpTime,
            crossoverPressure: ctx.Physics.TurboCrossoverPressure,
            host: ctx.Host,
            backingPumpId: element.Attribute("backingPump")?.Value ?? "",
            forelineVolume: element.Attribute("forelineVolume") != null ? ParseFloat(element, "forelineVolume") : 2f,
            forelineLimit: element.Attribute("forelineLimit") != null ? ParseFloat(element, "forelineLimit") : 5f);
    }

    private static IDeviceEmulator? CreateInputValve(DeviceCreateContext ctx)
    {
        var element = ctx.Element;
        return new InputValveEmulator(Id(ctx), ParseInt(element, "address"), ParseInt(element, "index"), Descr(ctx), ctx.Host);
    }

    private static IDeviceEmulator? CreateThrottleValve(DeviceCreateContext ctx)
    {
        var element = ctx.Element;
        string id = Id(ctx);
        int address = ParseInt(element, "address");
        string chamber = element.Attribute("chamber")?.Value
            ?? throw new InvalidOperationException(
                $"PLC throttle valve '{id}' is missing the required chamber= attribute in HWConf.xml.");
        return new VacPLCThrottleEmulator(id, address, Descr(ctx), chamber, ctx.Host);
    }
}
