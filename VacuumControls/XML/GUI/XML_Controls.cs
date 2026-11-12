using System.Xml.Serialization;

namespace APS.Controls.XML.GUI
{
    [XmlRoot("controls")]
    public class XML_Controls
    {
        [XmlElement("vacuum")]
        public XML_VacuumControls? Vacuum { get; set; }

        [XmlElement("schematic")] public XML_Schematic? Schematic { get; set; }

        [XmlText]
        public string? SystemScheme { get; set; }
    }


    [XmlRoot("vacuum")]
    public class XML_VacuumControls
    {
        [XmlElement("library")]
        public string? Library { get; set; }

        [XmlElement("valves")]
        public XML_ValveControls? Valves { get; set; }

        [XmlElement("MFCs")]
        public XML_MFCControls? MFCs { get; set; }

        [XmlElement("sensors")]
        public XML_Sensors? Sensors { get; set; }

        [XmlElement("leds")]
        public XML_LedControls? Leds { get; set; }

        [XmlElement("tpumps")]
        public XML_TPumpControls? TPumps { get; set; }

        [XmlElement("pumps")]
        public XML_PumpControls? Pumps { get; set; }

        [XmlElement("stages")]
        public XML_StageControls? Stages { get; set; }

        [XmlElement("gasinlets")]
        public XML_GasInletControls? GasInlets { get; set; }

        [XmlElement("magnetrons")]
        public XML_MagnetronControls? Magnetrons { get; set; }

        [XmlElement("gatevalves")]
        public XML_GateValveControls? GateValves { get; set; }

        [XmlElement("spumps")]
        public XML_SPumpControls? SPumps { get; set; }

        [XmlElement("tconnectors")]
        public XML_TConnectorControls? TConnectors { get; set; }

        [XmlElement("crossconnectors")]
        public XML_CrossConnectorControls? CrossConnectors { get; set; }

        [XmlElement("airinlets")]
        public XML_AirInletControls? AirInlets { get; set; }

        [XmlElement("throttlevalves")]
        public XML_ThrottleValveControls? ThrottleValves { get; set; }

        [XmlElement("gasmixers")]
        public XML_GasMixerControls? GasMixers { get; set; }

        [XmlElement("transferModules")]
        public XML_TransferModuleControls? TransferModules { get; set; }
    }
}
