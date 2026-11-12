using APS.SystemControl.Core.Evidence;
using Xunit;

namespace APS.SystemControl.Tests.CoreLogic;

public class DepositionSnapshotSerializerTests
{
    [Fact]
    public void RoundTrips_AllFields()
    {
        var snap = new DepositionSnapshot
        {
            BasedOn = "sha256:abc",
            Conditions = new DepositionConditions
            {
                Fingerprint = "sha256:def",
                Specimen = "Sample-42",
                Signature = new DepositionSignature
                {
                    UserId = "11111111-1111-1111-1111-111111111111",
                    Name = "Alex P.", Role = "Engineer",
                    SignedAtUtc = "2026-06-19T14:32:05.0000000Z", Intent = "authorized"
                },
                Software = new() { ["control"] = "1.4.0", ["worker"] = "MAG_WORKER 1.2.0" },
                PlcSerials = ["S7-200-001"],
                Sources =
                [
                    new DepositionSource { Flange = 2, Material = "W", Format = "4\" round",
                        MagnetronModel = "MAG-4", MagnetronInstance = "g-1", ErosionDepthMm = 3.2,
                        MagnetronElnLink = "http://eln/mag/g-1",
                        PowerSupply = new DepositionPsu { Id = "DC1", Model = "PSU-A",
                            Description = "DC magnetron supply", Responding = true,
                            ElnLink = "http://eln/psu/DC1" } }
                ],
                Recipe = new DepositionRecipe { Name = "W_dep", Format = "rcpx", Content = "<Recipe/>" }
            },
            Outcome = new DepositionOutcome
            {
                Result = "completed",
                DataFile = "2026-07-02_16-00-00_W_dep.tsv",
                Window = new DepositionWindow { StartedUtc = "2026-06-19T14:32:10.0000000Z", EndedUtc = "2026-06-19T15:00:00.0000000Z" },
                Means = new() { ["PSU-A"] = 250.0 },
                BasePressure = new BasePressure { Channel = "pm", Min = 0.001, Unit = "mTorr" }
            }
        };

        string json = DepositionSnapshotSerializer.ToJson(snap);
        var back = DepositionSnapshotSerializer.FromJson(json);

        Assert.Equal("sha256:abc", back.BasedOn);
        Assert.Equal("Sample-42", back.Conditions.Specimen);
        Assert.Equal("Engineer", back.Conditions.Signature.Role);
        Assert.Equal("W", back.Conditions.Sources[0].Material);
        Assert.Equal(3.2, back.Conditions.Sources[0].ErosionDepthMm);
        Assert.Equal("http://eln/mag/g-1", back.Conditions.Sources[0].MagnetronElnLink);
        Assert.Equal("DC1", back.Conditions.Sources[0].PowerSupply!.Id);
        Assert.Equal("PSU-A", back.Conditions.Sources[0].PowerSupply!.Model);
        Assert.True(back.Conditions.Sources[0].PowerSupply!.Responding);
        Assert.Equal("http://eln/psu/DC1", back.Conditions.Sources[0].PowerSupply!.ElnLink);
        Assert.Equal("completed", back.Outcome!.Result);
        Assert.Equal("2026-07-02_16-00-00_W_dep.tsv", back.Outcome.DataFile);
        Assert.Equal(0.001, back.Outcome.BasePressure!.Min);
        // camelCase on the wire
        Assert.Contains("\"fingerprint\"", json);
        Assert.Contains("\"basedOn\"", json);
        Assert.Contains("\"dataFile\"", json);
        // null outcome omitted
        var minimal = new DepositionSnapshot();
        Assert.DoesNotContain("\"outcome\"", DepositionSnapshotSerializer.ToJson(minimal));
    }

    [Fact]
    public void Legacy_outcome_without_dataFile_deserializes_to_null()
    {
        // Hand-written JSON as an old Control version would have produced it.
        const string legacy = """
        {
          "schema": 1,
          "kind": "deposition-snapshot",
          "conditions": { "fingerprint": "sha256:old", "recipe": { "name": "W_dep", "format": "rcpx", "content": "x" } },
          "outcome": { "result": "completed", "window": { "startedUtc": "2026-06-01T00:00:00Z", "endedUtc": "2026-06-01T01:00:00Z" } }
        }
        """;

        var back = DepositionSnapshotSerializer.FromJson(legacy);

        Assert.Equal("completed", back.Outcome!.Result);
        Assert.Null(back.Outcome.DataFile);
    }

    [Fact]
    public void Null_dataFile_is_omitted_from_json()
    {
        var snap = new DepositionSnapshot
        {
            Outcome = new DepositionOutcome { Result = "completed" }, // DataFile stays null
        };

        Assert.DoesNotContain("dataFile", DepositionSnapshotSerializer.ToJson(snap));
    }
}
