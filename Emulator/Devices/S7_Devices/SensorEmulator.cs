using System.Text.Json.Nodes;
using EmulatorCore.Interfaces;
using EmulatorCore.Models;

namespace APS.Emulator.Devices.S7;

public class SensorEmulator : IDeviceEmulator, ISnapshotable
{
    private readonly int _dbAddress;
    private readonly string _chamber;
    private readonly string _property;
    private readonly string _host;
    private float _currentValue = float.NaN;
    private readonly Random? _rng;
    private readonly float _minimumPressure;
    private readonly float _maximumPressure;
    private TurboPumpEmulator? _pressureSource;

    public string Id { get; }
    public string Description { get; }
    public string Host => _host;
    public DeviceCategory Category => DeviceCategory.Sensor;
    public float CurrentValue => _currentValue;
    public string PressureSourceId { get; }
    public string ReadingStatus => float.IsPositiveInfinity(_currentValue) ? "Over range"
        : float.IsNegativeInfinity(_currentValue) ? "Under range"
        : float.IsNaN(_currentValue) ? "Invalid" : "Valid";

    public SensorEmulator(string id, int dbAddress, string chamber, string property, string description,
        string host = "", float minPressure = 0f, float maxPressure = float.MaxValue,
        string pressureSourceId = "")
    {
        Id = id;
        _dbAddress = dbAddress;
        _chamber = chamber;
        _property = property;
        _rng = property is "SimTime" or "Thickness" ? null : new Random();
        Description = description;
        _host = host;
        if (!float.IsFinite(minPressure) || !float.IsFinite(maxPressure) ||
            minPressure < 0f || maxPressure <= minPressure)
            throw new ArgumentOutOfRangeException(nameof(minPressure), "Pressure range must be finite, nonnegative and increasing.");
        _minimumPressure = minPressure;
        _maximumPressure = maxPressure;
        PressureSourceId = pressureSourceId;
        if (property == "ForelinePressure" && pressureSourceId.Length == 0)
            throw new ArgumentException($"Foreline sensor '{id}' requires a turbo source.", nameof(pressureSourceId));
    }

    public void BindPressureSource(TurboPumpEmulator source)
    {
        if (!source.Id.Equals(PressureSourceId, StringComparison.OrdinalIgnoreCase) ||
            source.BackingPumpId.Length == 0)
            throw new InvalidOperationException($"Foreline sensor '{Id}' requires a modeled turbo source '{PressureSourceId}'.");
        _pressureSource = source;
    }

    public void RegisterProtocol(IProtocolServer server)
    {
        if (server is IS7ProtocolServer s7)
        {
            s7.RegisterDbRegion(new DbRegion
            {
                DeviceId = Id,
                Address = _dbAddress,
                Length = 4,
                OnRead = () =>
                {
                    byte[] buffer = new byte[4];
                    WriteFloatBE(buffer, 0, _currentValue);
                    return buffer;
                },
                OnWrite = _ => { },
            });
        }
    }

    public void Update(float deltaTime, SimulationState state)
    {
        // Log replay override
        if (state.DataSource.TryGetValue(Id, _property, state.SimTime, out float replayValue))
        {
            ApplyReading(replayValue, false);
            return;
        }

        // The PLC "tmp" sensor reads the chamber wall temperature (slow, water-cooled);
        // the substrate pyrometer reads ThermalState.Temperature via its own device emulator.
        float rawValue = _property switch
        {
            "Pressure" => state.Chambers.TryGetValue(_chamber, out var ch) ? ch.Pressure
                : throw new KeyNotFoundException($"Sensor '{Id}' references unknown chamber '{_chamber}'"),
            "Temperature" => state.Thermal.ChamberTemperature, // single Thermal until per-chamber thermal ships
            "SimTime" => state.SimTime,
            "Thickness" => state.FilmThickness,
            "ForelinePressure" => _pressureSource?.ForelinePressure ?? float.NaN,
            _ => throw new InvalidOperationException($"Sensor '{Id}' has unsupported property '{_property}'."),
        };

        ApplyReading(rawValue, _property is not ("SimTime" or "Thickness"));
    }

    private void ApplyReading(float value, bool addNoise)
    {
        if (addNoise && float.IsFinite(value))
            value += GaussianNoise() * value * 0.001f;
        if (!float.IsFinite(value) || (_property == "SimTime" && value < 0f))
            _currentValue = float.NaN;
        else if ((_property == "Pressure" || _property == "ForelinePressure") && value < _minimumPressure)
            _currentValue = float.NegativeInfinity;
        else if ((_property == "Pressure" || _property == "ForelinePressure") && value > _maximumPressure)
            _currentValue = float.PositiveInfinity;
        else
            _currentValue = value;
    }

    public void Reset()
    {
        _currentValue = float.NaN;
    }

    public JsonObject GetSnapshot() => new()
    {
        ["currentValue"] = float.IsFinite(_currentValue) ? JsonValue.Create(_currentValue) : null,
        ["readingStatus"] = ReadingStatus,
    };

    public void RestoreSnapshot(JsonObject snapshot)
    {
        _currentValue = snapshot["currentValue"] is { } value ? value.GetValue<float>()
            : snapshot["readingStatus"]?.GetValue<string>() switch
            {
                "Over range" => float.PositiveInfinity,
                "Under range" => float.NegativeInfinity,
                _ => float.NaN,
            };
    }

    public object[] GetStatus() => [Id, float.IsFinite(_currentValue) ? (object)_currentValue : ReadingStatus, _chamber, _property, Description];

    private float GaussianNoise()
    {
        double u1 = 1.0 - _rng!.NextDouble();
        double u2 = _rng.NextDouble();
        return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2));
    }

    private static void WriteFloatBE(byte[] buffer, int offset, float value)
    {
        System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(buffer.AsSpan(offset), value);
    }
}
