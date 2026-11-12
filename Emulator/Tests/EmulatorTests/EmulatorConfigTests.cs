using System.Xml.Serialization;
using EmulatorCore.Interfaces;
using EmulatorCore.Models;

namespace EmulatorTests;

public class EmulatorConfigTests
{
    [Fact]
    public void PhysicsConfig_DeserializesMagnetronAndThermalFields()
    {
        const string xml = """
            <EmulatorConfig>
              <Physics>
                <SubstrateHeatingCoeff>0.002</SubstrateHeatingCoeff>
                <SubstrateCoolingCoeff>0.005</SubstrateCoolingCoeff>
                <DischargePressureThreshold>0.001</DischargePressureThreshold>
                <Magnetrons>
                  <Magnetron id="MAG-4" voltage="400" />
                  <Magnetron id="MAG4" voltage="350" />
                </Magnetrons>
              </Physics>
            </EmulatorConfig>
            """;

        var ser = new XmlSerializer(typeof(EmulatorConfig));
        using var reader = new StringReader(xml);
        var cfg = (EmulatorConfig)ser.Deserialize(reader)!;

        Assert.Equal(0.002f, cfg.Physics.SubstrateHeatingCoeff);
        Assert.Equal(0.005f, cfg.Physics.SubstrateCoolingCoeff);
        Assert.Equal(0.001f, cfg.Physics.DischargePressureThreshold);
        Assert.Equal(2, cfg.Physics.Magnetrons.Count);
        Assert.Equal("MAG-4", cfg.Physics.Magnetrons[0].Id);
        Assert.Equal(400f, cfg.Physics.Magnetrons[0].Voltage);
        Assert.Equal("MAG4", cfg.Physics.Magnetrons[1].Id);
        Assert.Equal(350f, cfg.Physics.Magnetrons[1].Voltage);
    }

    [Fact]
    public void ChamberConfig_TypeAttribute_DeserializesEnum()
    {
        string xml = @"<?xml version='1.0'?>
<EmulatorConfig>
  <Physics>
    <Chamber id='C1' type='Process'><InitialPressure>1</InitialPressure></Chamber>
    <Chamber id='LL' type='Loadlock'><InitialPressure>760</InitialPressure></Chamber>
    <Chamber id='TM' type='Transfer'><InitialPressure>0.01</InitialPressure></Chamber>
  </Physics>
</EmulatorConfig>";

        var serializer = new XmlSerializer(typeof(EmulatorConfig));
        using var reader = new StringReader(xml);
        var cfg = (EmulatorConfig)serializer.Deserialize(reader)!;

        Assert.Equal(3, cfg.Physics.Chambers.Count);
        Assert.Equal(ChamberType.Process,  cfg.Physics.Chambers[0].Type);
        Assert.Equal(ChamberType.Loadlock, cfg.Physics.Chambers[1].Type);
        Assert.Equal(ChamberType.Transfer, cfg.Physics.Chambers[2].Type);
    }

    [Fact]
    public void ChamberConfig_TypeAttribute_DefaultsToProcess_WhenMissing()
    {
        string xml = @"<?xml version='1.0'?>
<EmulatorConfig><Physics><Chamber id='Main'/></Physics></EmulatorConfig>";

        var serializer = new XmlSerializer(typeof(EmulatorConfig));
        using var reader = new StringReader(xml);
        var cfg = (EmulatorConfig)serializer.Deserialize(reader)!;

        Assert.Equal(ChamberType.Process, cfg.Physics.Chambers[0].Type);
    }
}
