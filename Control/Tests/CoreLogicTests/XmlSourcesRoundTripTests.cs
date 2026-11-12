using APS.SystemControl.Core.Classes;
using APS.SystemControl.XML;
using APS.SystemControl.XML.Core;
using Xunit;

namespace CoreLogicTests;

public class XmlSourcesRoundTripTests
{
    [Fact]
    public void SourceManager_round_trips_cluster_chamber_and_shutter_without_drift()
    {
        // A cluster source carries chamber + shutterId; a single-chamber source carries neither.
        // SourceManager.Save (used by the Sources form / on mount) must NOT
        // strip the cluster's chamber/shutter map, and must NOT add empty attrs to plain sources.
        string path1 = Path.Combine(Path.GetTempPath(), $"src-{Guid.NewGuid():N}.xml");
        string path2 = Path.Combine(Path.GetTempPath(), $"src-{Guid.NewGuid():N}.xml");
        try
        {
            var doc = new XML_Sources
            {
                Sources = new()
                {
                    new XML_Source { FlangeNumber = 1, Chamber = "C1", ShutterId = "C1_Sh",
                        Material = "Cu", MagnetronId = "MAG_C1", PowerSupplyId = "C1_RF" },
                    new XML_Source { FlangeNumber = 2, Material = "Si" },   // no chamber/shutter
                },
            };
            Serializer<XML_Sources>.Save(doc, path1);

            var mgr = new SourceManager(6);
            mgr.Load(path1);
            Assert.Equal("C1", mgr.GetSource(1)!.Chamber);
            Assert.Equal("C1_Sh", mgr.GetSource(1)!.ShutterId);
            Assert.Null(mgr.GetSource(2)!.Chamber);
            Assert.Null(mgr.GetSource(2)!.ShutterId);

            // Save back through SourceManager (the path the Sources form / a mount uses) and reload.
            mgr.Save(path2);
            var mgr2 = new SourceManager(6);
            mgr2.Load(path2);
            Assert.Equal("C1", mgr2.GetSource(1)!.Chamber);
            Assert.Equal("C1_Sh", mgr2.GetSource(1)!.ShutterId);
            Assert.Null(mgr2.GetSource(2)!.Chamber);
            Assert.Null(mgr2.GetSource(2)!.ShutterId);

            // No empty-attribute drift on the plain (single-chamber) source.
            string raw = File.ReadAllText(path2);
            Assert.DoesNotContain("chamber=\"\"", raw);
            Assert.DoesNotContain("shutterId=\"\"", raw);
        }
        finally
        {
            if (File.Exists(path1)) File.Delete(path1);
            if (File.Exists(path2)) File.Delete(path2);
        }
    }

    [Fact]
    public void Formats_instances_and_flange_instance_ref_round_trip()
    {
        string path = Path.Combine(Path.GetTempPath(), $"src-{Guid.NewGuid():N}.xml");
        try
        {
            var magId = Guid.NewGuid().ToString();
            var doc = new XML_Sources
            {
                Magnetrons = new() { new XML_Magnetron { Id = "MAG-4", DisplayName = "MAG 4\" w Chimney" } },
                PowerSources = new() { new XML_PowerSource { IdAttr = "PSU-A" } },
                Formats = new() { new XML_Format { Id = "4in-round", Label = "4\" round" } },
                MagnetronInstances = new()
                {
                    new XML_MagnetronInstance { Id = magId, Label = "MAG-4 #1", ModelId = "MAG-4",
                        Format = "4in-round", PhysicsJson = "{\"a\":1}", Comment = "spare", State = "Active" }
                },
                Sources = new() { new XML_Source { FlangeNumber = 2, Material = "Si",
                    MagnetronId = "MAG-4", MagnetronInstanceId = magId, PowerSupplyId = "PSU-A" } },
            };
            Serializer<XML_Sources>.Save(doc, path);
            var back = Serializer<XML_Sources>.Load(path)!;

            Assert.Equal("4\" round", back.Formats!.Single().Label);
            Assert.Equal("MAG-4 #1", back.MagnetronInstances!.Single().Label);
            Assert.Equal("{\"a\":1}", back.MagnetronInstances!.Single().PhysicsJson);
            Assert.Equal(magId, back.Sources!.Single().MagnetronInstanceId);
            Assert.Equal("MAG-4", back.Sources!.Single().MagnetronId); // model id preserved (emulator contract)
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Eln_links_round_trip_and_legacy_powersource_text_still_loads()
    {
        string inPath  = Path.Combine(Path.GetTempPath(), $"src-{Guid.NewGuid():N}.xml");
        string outPath = Path.Combine(Path.GetTempPath(), $"src-{Guid.NewGuid():N}.xml");
        try
        {
            var magId = Guid.NewGuid().ToString();
            var doc = new XML_Sources
            {
                // Legacy bare-text power source id (no id attribute) — must still load.
                PowerSources = new() { new XML_PowerSource { Text = "DC1", ElnLink = "http://eln/psu/DC1" } },
                MagnetronInstances = new()
                {
                    new XML_MagnetronInstance { Id = magId, Label = "M1", ModelId = "MAG-4",
                        State = "Active", ElnLink = "http://eln/mag/M1" }
                },
            };
            Serializer<XML_Sources>.Save(doc, inPath);

            var mgr = new SourceManager(6);
            mgr.Load(inPath);
            Assert.Contains("DC1", mgr.PowerSources);                                 // legacy text id parsed
            Assert.Equal("http://eln/psu/DC1", mgr.GetPowerSourceElnLink("DC1"));
            Assert.Equal("http://eln/mag/M1", mgr.MagnetronInstances.Single().ElnLink);

            // Save through SourceManager (writes PSU as id attribute) and reload — links survive.
            mgr.Save(outPath);
            string raw = File.ReadAllText(outPath);
            Assert.Contains("id=\"DC1\"", raw);
            Assert.Contains("http://eln/psu/DC1", raw);

            var mgr2 = new SourceManager(6);
            mgr2.Load(outPath);
            Assert.Equal("http://eln/psu/DC1", mgr2.GetPowerSourceElnLink("DC1"));
            Assert.Equal("http://eln/mag/M1", mgr2.MagnetronInstances.Single().ElnLink);
        }
        finally
        {
            if (File.Exists(inPath))  File.Delete(inPath);
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    [Fact]
    public void Source_Name_Attribute_RoundTrips()
    {
        var mgr = new SourceManager(6);
        var s = mgr.GetSource(2)!;
        s.Material = "W";
        s.Name = "W_1";

        string tmp = Path.Combine(Path.GetTempPath(), $"sources-name-{Guid.NewGuid():N}.xml");
        try
        {
            mgr.Save(tmp);
            var reloaded = new SourceManager(6);
            reloaded.Load(tmp);
            Assert.Equal("W_1", reloaded.GetSource(2)!.Name);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Source_Without_Name_Omits_Attribute_And_Loads_Empty()
    {
        var mgr = new SourceManager(6);
        mgr.GetSource(1)!.Material = "Si";   // no Name set
        string tmp = Path.Combine(Path.GetTempPath(), $"sources-noname-{Guid.NewGuid():N}.xml");
        try
        {
            mgr.Save(tmp);
            Assert.DoesNotContain("name=", File.ReadAllText(tmp));
            var reloaded = new SourceManager(6);
            reloaded.Load(tmp);
            Assert.Equal("", reloaded.GetSource(1)!.Name);
        }
        finally { File.Delete(tmp); }
    }
}
