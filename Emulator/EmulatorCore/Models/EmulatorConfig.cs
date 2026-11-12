using System.Xml.Serialization;
using EmulatorCore.Interfaces;

namespace EmulatorCore.Models;

[XmlRoot("EmulatorConfig")]
public class EmulatorConfig
{
    [XmlElement("Engine")]
    public EngineConfig Engine { get; set; } = new();

    [XmlArray("Protocols")]
    [XmlArrayItem("S7Server", typeof(S7ServerConfig))]
    [XmlArrayItem("TcpSerial", typeof(TcpSerialConfig))]
    [XmlArrayItem("Serial", typeof(SerialConfig))]
    public List<ProtocolConfig> Protocols { get; set; } = [];

    [XmlElement("Physics")]
    public PhysicsConfig Physics { get; set; } = new();

    [XmlArray("DeviceLibraries")]
    [XmlArrayItem("Library")]
    public List<LibraryConfig> DeviceLibraries { get; set; } = [];
}

public class EngineConfig
{
    [XmlAttribute("TickIntervalMs")]
    public int TickIntervalMs { get; set; } = 100;

    [XmlAttribute("Sound")]
    public bool Sound { get; set; } = true;
}

public class ProtocolConfig
{
    [XmlAttribute("deviceId")]
    public string DeviceId { get; set; } = "";

    [XmlAttribute("bindAddress")]
    public string BindAddress { get; set; } = "127.0.0.1";

    [XmlAttribute("port")]
    public int Port { get; set; }
}

public class S7ServerConfig : ProtocolConfig
{
    [XmlAttribute("plcId")]
    public string PlcId { get; set; } = "";
}

public class TcpSerialConfig : ProtocolConfig
{
    [XmlAttribute("framing")]
    public string Framing { get; set; } = "CrLfTerminated";
}

public class SerialConfig : ProtocolConfig
{
    [XmlAttribute("portName")]
    public string PortName { get; set; } = "";

    [XmlAttribute("baudRate")]
    public int BaudRate { get; set; } = 9600;

    [XmlAttribute("framing")]
    public string Framing { get; set; } = "CrLfTerminated";
}

public class PhysicsConfig
{
    [XmlElement("Chamber")]
    public List<ChamberConfig> Chambers { get; set; } = [];

    [XmlAttribute("noiseStddev")]
    public float NoiseStddev { get; set; } = 0.02f;

    /// <summary>Fallback scroll/rotary-vane pump speed (L/s) — used only when
    /// a pump element in HWConf omits the speed= attribute.</summary>
    public float ScrollPumpSpeed { get; set; } = 10f;

    /// <summary>Fallback turbo-molecular pump speed (L/s) — used only when a
    /// pump element in HWConf omits the speed= attribute. Real configs set
    /// speed= per pump (e.g. 2300 L/s).</summary>
    public float TurboPumpSpeed { get; set; } = 2000f;

    /// <summary>Turbo pump spin-up time in seconds.</summary>
    public float TurboSpinUpTime { get; set; } = 30f;

    /// <summary>Pressure (Torr) above which turbo pump is ineffective.</summary>
    public float TurboCrossoverPressure { get; set; } = 0.5f;

    /// <summary>MFC first-order response time constant in seconds.</summary>
    public float MfcResponseTau { get; set; } = 0.5f;

    /// <summary>Substrate heating coefficient (C per W per s). Multiplied by HeatLoad to get heating rate.</summary>
    public float SubstrateHeatingCoeff { get; set; } = 0.002f;

    /// <summary>Substrate cooling coefficient (1/s). Multiplied by (T - ambient) to get cooling rate.</summary>
    public float SubstrateCoolingCoeff { get; set; } = 0.005f;

    /// <summary>Substrate radiative cooling coefficient (C/s per K^4). Multiplied by (T_K^4 - T_amb_K^4)
    /// to give a Stefan-Boltzmann-style loss term that dominates at high temperature and caps
    /// the steady-state substrate temperature even when several sources are discharging together.</summary>
    public float SubstrateRadiativeCoeff { get; set; } = 5e-12f;

    /// <summary>Chamber wall heating coefficient (C per W per s). Multiplied by total HeatLoad
    /// (discharge power, not gated by the shutter — the plume hits chamber shielding regardless).</summary>
    public float ChamberHeatingCoeff { get; set; } = 3e-4f;

    /// <summary>Chamber wall cooling coefficient (1/s). The walls are water/forced-convection
    /// cooled, so this is faster than substrate conductive cooling.</summary>
    public float ChamberCoolingCoeff { get; set; } = 0.01f;

    /// <summary>Pressure (Torr) below which magnetron discharges cannot sustain.</summary>
    public float DischargePressureThreshold { get; set; } = 0.001f;

    [XmlArray("Magnetrons")]
    [XmlArrayItem("Magnetron")]
    public List<MagnetronConfig> Magnetrons { get; set; } = [];

    [XmlElement("Deposition")]
    public DepositionConfig? Deposition { get; set; }
}

public class DepositionConfig
{
    [XmlAttribute("reactiveMfc")]
    public string ReactiveMfc { get; set; } = "";

    /// <summary>Metal-mode deposition rate per discharge current, nm/(s*A).</summary>
    [XmlAttribute("ratePerAmp")]
    public float RatePerAmp { get; set; } = 1.0f;

    /// <summary>Target nitridation rate, 1/(sccm*s).</summary>
    [XmlAttribute("poisonRate")]
    public float PoisonRate { get; set; } = 0.00673f;

    /// <summary>Sputter cleaning rate, 1/(A*s).</summary>
    [XmlAttribute("cleanRate")]
    public float CleanRate { get; set; } = 0.1f;

    /// <summary>Deposition rate of a fully poisoned target relative to metal mode.</summary>
    [XmlAttribute("poisonedRateFraction")]
    public float PoisonedRateFraction { get; set; } = 0.2f;

    /// <summary>Discharge voltage increase at full poisoning, V.</summary>
    [XmlAttribute("poisonedVoltageShift")]
    public float PoisonedVoltageShift { get; set; } = 100f;
}

public class ChamberConfig
{
    [XmlAttribute("id")]
    public string Id { get; set; } = "";

    [XmlAttribute("type")]
    public ChamberType Type { get; set; } = ChamberType.Process;

    public float InitialPressure { get; set; } = 760f;
    public float Volume { get; set; } = 50f;
    public float BasePressure { get; set; } = 2.2e-4f;
    public float LeakRate { get; set; } = 1e-5f;
    public float PumpdownTau { get; set; } = 5.0f;
}

public class LibraryConfig
{
    [XmlAttribute("path")]
    public string Path { get; set; } = "";
}

public class MagnetronConfig
{
    [XmlAttribute("id")]
    public string Id { get; set; } = "";

    [XmlAttribute("voltage")]
    public float Voltage { get; set; }

    /// <summary>Chamber this magnetron is installed in. Defaults to "Main"
    /// for legacy configs that don't set it. Source loop reads this chamber's
    /// pressure when gating discharges.</summary>
    [XmlAttribute("chamber")]
    public string ChamberId { get; set; } = "Main";
}
