using System.Text;
using APS.Emulator.Devices.Demo;
using APS.SystemEmulator.Engine;
using EmulatorCore.Engine;
using EmulatorCore.Interfaces;

namespace EmulatorTests;

public class LanPsuEmulatorTests
{
    private static string Send(LanPsuEmulator psu, string line)
        => Encoding.ASCII.GetString(psu.HandleCommand(Encoding.ASCII.GetBytes(line + "\n")));

    [Fact]
    public void Idn_ReturnsProductId()
        => Assert.Equal("APS,LANPSU-1000,0,1.0\n", Send(new LanPsuEmulator("P1", "PDC", ""), "*IDN?"));

    [Fact]
    public void Mode_ReturnsConfiguredMode()
        => Assert.Equal("RF\n", Send(new LanPsuEmulator("P1", "RF", ""), "SYST:MODE?"));

    [Fact]
    public void PowerSet_UpdatesCommandedPower()
    {
        var psu = new LanPsuEmulator("P1", "PDC", "");
        Assert.Equal("", Send(psu, "POW 250"));
        Assert.Equal(250f, psu.CommandedPower);
        Assert.Equal("250\n", Send(psu, "POW?"));
    }

    [Fact]
    public void OutOfRange_QueuesErrorAndKeepsSetpoint()
    {
        var psu = new LanPsuEmulator("P1", "PDC", "");
        Send(psu, "POW 200");
        Assert.Equal("", Send(psu, "POW 5000"));
        Assert.Equal(200f, psu.CommandedPower);
        Assert.Equal("-222,\"Data out of range\"\n", Send(psu, "SYST:ERR?"));
        Assert.Equal("0,\"No error\"\n", Send(psu, "SYST:ERR?"));
    }

    [Fact]
    public void Rf_FreqIsSettingsConflict()
    {
        var psu = new LanPsuEmulator("P1", "RF", "");
        Assert.Equal("", Send(psu, "FREQ 100"));
        Assert.Equal("-221,\"Settings conflict\"\n", Send(psu, "SYST:ERR?"));
    }

    [Fact]
    public void Measure_ZeroWhileOff()
    {
        var psu = new LanPsuEmulator("P1", "PDC", "");
        psu.SetActualOutput(400, 0.5f, 200);
        Assert.Equal("0\n", Send(psu, "MEAS:POW?"));
        Send(psu, "OUTP 1");
        psu.SetActualOutput(400, 0.5f, 200);
        Assert.Equal("200\n", Send(psu, "MEAS:POW?"));
        Assert.Equal("400\n", Send(psu, "MEAS:VOLT?"));
        Assert.Equal("500\n", Send(psu, "MEAS:CURR?"));
        Send(psu, "OUTP 0");
        Assert.Equal("0\n", Send(psu, "MEAS:POW?"));
        Send(psu, "OUTP 1");
        Assert.Equal("200\n", Send(psu, "MEAS:POW?"));
    }

    [Fact]
    public void Pdc_EngineZeroVoltage_DerivesVoltageAndCurrent()
    {
        var psu = new LanPsuEmulator("P1", "PDC", "");
        Send(psu, "OUTP 1");
        psu.SetActualOutput(0, 0, 200);
        Assert.Equal("400\n", Send(psu, "MEAS:VOLT?"));
        Assert.Equal("500\n", Send(psu, "MEAS:CURR?"));
        Assert.Equal("200\n", Send(psu, "MEAS:POW?"));
        Assert.Equal("400.0", ((IPowerReadout)psu).VoltageText);
        Assert.Equal("0.500", ((IPowerReadout)psu).CurrentText);
    }

    [Fact]
    public void Pdc_DerivedVoltageClampsToOpenCircuit()
    {
        var psu = new LanPsuEmulator("P1", "PDC", "");
        Send(psu, "OUTP 1");
        psu.SetActualOutput(0, 0, 1000);
        Assert.Equal("800\n", Send(psu, "MEAS:VOLT?"));
        Assert.Equal("1250\n", Send(psu, "MEAS:CURR?"));
    }

    [Fact]
    public void Rf_VoltageAndCurrentStayAsReported()
    {
        var psu = new LanPsuEmulator("P1", "RF", "");
        Send(psu, "OUTP 1");
        psu.SetActualOutput(0, 0, 200);
        Assert.Equal("0\n", Send(psu, "MEAS:VOLT?"));
        Assert.Equal("0\n", Send(psu, "MEAS:CURR?"));
        Assert.Equal("200\n", Send(psu, "MEAS:POW?"));
    }

    [Fact]
    public void Pdc_OutputOff_DerivedValuesAreZero()
    {
        var psu = new LanPsuEmulator("P1", "PDC", "");
        psu.SetActualOutput(0, 0, 200);
        Assert.Equal("0\n", Send(psu, "MEAS:VOLT?"));
        Assert.Equal("0\n", Send(psu, "MEAS:CURR?"));
        Assert.Equal("0\n", Send(psu, "MEAS:POW?"));
    }

    [Fact]
    public void UnknownHeader_Queues113()
    {
        var psu = new LanPsuEmulator("P1", "PDC", "");
        Assert.Equal("", Send(psu, "FOO?"));
        Assert.Equal("-113,\"Undefined header\"\n", Send(psu, "SYST:ERR?"));
    }

    [Fact]
    public void ErrorQueue_IsFifo()
    {
        var psu = new LanPsuEmulator("P1", "RF", "");
        Send(psu, "FOO");
        Send(psu, "DUTY 50");
        Send(psu, "POW -1");
        Assert.Equal("-113,\"Undefined header\"\n", Send(psu, "SYST:ERR?"));
        Assert.Equal("-221,\"Settings conflict\"\n", Send(psu, "SYST:ERR?"));
        Assert.Equal("-222,\"Data out of range\"\n", Send(psu, "SYST:ERR?"));
        Assert.Equal("0,\"No error\"\n", Send(psu, "SYST:ERR?"));
    }

    [Fact]
    public void Pdc_FreqAndDutyRangesEnforced()
    {
        var psu = new LanPsuEmulator("P1", "PDC", "");
        Send(psu, "FREQ 350.5");
        Send(psu, "FREQ 0.5");
        Send(psu, "DUTY 96");
        Send(psu, "DUTY 4");
        Send(psu, "FREQ 100.5");
        Send(psu, "DUTY 60");
        Assert.Equal("100.5\n", Send(psu, "FREQ?"));
        Assert.Equal("60\n", Send(psu, "DUTY?"));
        for (int i = 0; i < 4; i++)
            Assert.Equal("-222,\"Data out of range\"\n", Send(psu, "SYST:ERR?"));
        Assert.Equal("0,\"No error\"\n", Send(psu, "SYST:ERR?"));
    }

    [Fact]
    public void Output_SetAndQuery()
    {
        var psu = new LanPsuEmulator("P1", "PDC", "");
        Assert.Equal("0\n", Send(psu, "OUTP?"));
        Send(psu, "OUTP 1");
        Assert.True(psu.OutputEnabled);
        Assert.Equal("1\n", Send(psu, "OUTP?"));
        Send(psu, "OUTP 2");
        Assert.True(psu.OutputEnabled);
        Assert.Equal("-222,\"Data out of range\"\n", Send(psu, "SYST:ERR?"));
    }

    [Theory]
    [InlineData("DC", 800f)]
    [InlineData("PDC", 800f)]
    [InlineData("RF", 300f)]
    public void PowerSupplyContract(string mode, float openCircuit)
    {
        IPowerSupplyEmulator psu = new LanPsuEmulator("P1", mode, "");
        Assert.Equal(PowerRegulationMode.Power, psu.RegulationMode);
        Assert.Equal(openCircuit, psu.OpenCircuitVoltage);
    }

    [Fact]
    public void Curr_SetAndQuery_Pdc()
    {
        var psu = new LanPsuEmulator("P1", "PDC", "");
        Assert.Equal("", Send(psu, "CURR 300"));
        Assert.Equal("300\n", Send(psu, "CURR?"));
        Assert.Equal("0,\"No error\"\n", Send(psu, "SYST:ERR?"));
    }

    [Fact]
    public void Curr_SetAndQuery_Dc()
    {
        var psu = new LanPsuEmulator("P1", "DC", "");
        Assert.Equal("DC\n", Send(psu, "SYST:MODE?"));
        Assert.Equal("", Send(psu, "CURR 50.5"));
        Assert.Equal("50.5\n", Send(psu, "CURR?"));
        Assert.Equal("0,\"No error\"\n", Send(psu, "SYST:ERR?"));
    }

    [Fact]
    public void Curr_OnRf_Queues221()
    {
        var psu = new LanPsuEmulator("P1", "RF", "");
        Assert.Equal("", Send(psu, "CURR 300"));
        Assert.Equal("-221,\"Settings conflict\"\n", Send(psu, "SYST:ERR?"));
        Assert.Equal("", Send(psu, "CURR?"));
        Assert.Equal("-221,\"Settings conflict\"\n", Send(psu, "SYST:ERR?"));
        Assert.Equal(PowerRegulationMode.Power, psu.RegulationMode);
        Assert.Equal(0f, psu.CommandedCurrent);
    }

    [Fact]
    public void Curr_OutOfRange_Queues222AndKeepsSetpoint()
    {
        var psu = new LanPsuEmulator("P1", "DC", "");
        Send(psu, "CURR 200");
        Send(psu, "CURR 1000.5");
        Send(psu, "CURR -1");
        Send(psu, "CURR x");
        Assert.Equal("200\n", Send(psu, "CURR?"));
        for (int i = 0; i < 3; i++)
            Assert.Equal("-222,\"Data out of range\"\n", Send(psu, "SYST:ERR?"));
        Assert.Equal("0,\"No error\"\n", Send(psu, "SYST:ERR?"));
    }

    [Fact]
    public void MeasCurr_ReportsMilliamps()
    {
        var psu = new LanPsuEmulator("P1", "DC", "");
        Send(psu, "POW 100");
        Send(psu, "OUTP 1");
        psu.SetActualOutput(0, 0, psu.CommandedPower);
        float volts = float.Parse(Send(psu, "MEAS:VOLT?"), System.Globalization.CultureInfo.InvariantCulture);
        float milliamps = float.Parse(Send(psu, "MEAS:CURR?"), System.Globalization.CultureInfo.InvariantCulture);
        float expected = 1000f * 100f / volts;
        Assert.InRange(milliamps, expected * 0.99f, expected * 1.01f);
    }

    [Theory]
    [InlineData("FREQ 100")]
    [InlineData("FREQ?")]
    [InlineData("DUTY 50")]
    [InlineData("DUTY?")]
    public void Dc_FreqAndDutyQueue221(string line)
    {
        var psu = new LanPsuEmulator("P1", "DC", "");
        Assert.Equal("", Send(psu, line));
        Assert.Equal("-221,\"Settings conflict\"\n", Send(psu, "SYST:ERR?"));
    }

    [Fact]
    public void CurrentSetpoint_RegulatesCurrentInAmps_UntilCleared()
    {
        var psu = new LanPsuEmulator("P1", "PDC", "");
        Assert.Equal(PowerRegulationMode.Power, psu.RegulationMode);
        Send(psu, "CURR 300");
        Assert.Equal(PowerRegulationMode.Current, psu.RegulationMode);
        Assert.Equal(0.3f, psu.CommandedCurrent);
        Send(psu, "CURR 0");
        Assert.Equal(PowerRegulationMode.Power, psu.RegulationMode);
    }

    [Fact]
    public void CurrentRegulation_ReportsEngineOperatingPointInMilliamps()
    {
        var psu = new LanPsuEmulator("P1", "PDC", "");
        Send(psu, "CURR 300");
        Send(psu, "OUTP 1");
        psu.SetActualOutput(400f, 0.25f, 100f);
        Assert.Equal("400\n", Send(psu, "MEAS:VOLT?"));
        Assert.Equal("250\n", Send(psu, "MEAS:CURR?"));
        Assert.Equal("100\n", Send(psu, "MEAS:POW?"));
    }

    [Fact]
    public void Reset_ClearsCurrentSetpoint()
    {
        var psu = new LanPsuEmulator("P1", "DC", "");
        Send(psu, "CURR 300");
        psu.Reset();
        Assert.Equal("0\n", Send(psu, "CURR?"));
        Assert.Equal(PowerRegulationMode.Power, psu.RegulationMode);
    }

    [Fact]
    public void Registration_AcceptsDcMode()
    {
        var loader = new ConfigLoader(new DeviceRegistry([new DemoDeviceRegistration()]), RegistryTestHelpers.DefaultPhysics());
        var devices = loader.LoadHWConfig(TestConfigs.Write("<hardware><powersupplies hwtype=\"APS.LanPowerSupply\"><powersupply mode=\"dc\" descr=\"Magnetron\">PSU1</powersupply></powersupplies></hardware>"));
        var psu = Assert.Single(devices.OfType<LanPsuEmulator>());
        Assert.Equal("DC\n", Send(psu, "SYST:MODE?"));
    }

    [Fact]
    public void ImplementsPowerInterfaces()
    {
        Assert.True(typeof(IPowerSupplyEmulator).IsAssignableFrom(typeof(LanPsuEmulator)));
        Assert.True(typeof(IPowerReadout).IsAssignableFrom(typeof(LanPsuEmulator)));
        Assert.Equal("LanPsu", ((IPowerReadout)new LanPsuEmulator("P1", "PDC", "")).TypeName);
    }

    [Fact]
    public void Registration_FactoryReadsIdModeAndDescr()
    {
        var loader = new ConfigLoader(new DeviceRegistry([new DemoDeviceRegistration()]), RegistryTestHelpers.DefaultPhysics());
        var devices = loader.LoadHWConfig(TestConfigs.Write("<hardware><powersupplies hwtype=\"APS.LanPowerSupply\"><powersupply mode=\"rf\" descr=\"Bias\">PSU1</powersupply></powersupplies></hardware>"));
        var psu = Assert.Single(devices.OfType<LanPsuEmulator>());
        Assert.Equal("PSU1", psu.Id);
        Assert.Equal("Bias", psu.Description);
        Assert.Equal(300f, psu.OpenCircuitVoltage);
        Assert.Equal("RF\n", Send(psu, "SYST:MODE?"));
    }

    [Fact]
    public void Registration_InvalidMode_ReturnsNullAndWarnsOnce()
    {
        using var log = EmulatorLogCapture.Start();
        var loader = new ConfigLoader(new DeviceRegistry([new DemoDeviceRegistration()]), RegistryTestHelpers.DefaultPhysics());
        var devices = loader.LoadHWConfig(TestConfigs.Write("<hardware><powersupplies hwtype=\"APS.LanPowerSupply\"><powersupply mode=\"AC\">PSU1</powersupply></powersupplies></hardware>"));
        Assert.DoesNotContain(devices, d => d.Id == "PSU1");
        Assert.Single(log.Warnings, w => w.Contains("PSU1"));
    }
}
