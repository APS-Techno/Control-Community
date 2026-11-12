using System.Text.Json.Nodes;

namespace EmulatorCore.Interfaces;

public interface ISnapshotable
{
    JsonObject GetSnapshot();
    void RestoreSnapshot(JsonObject snapshot);
}
