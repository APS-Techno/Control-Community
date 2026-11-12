namespace APS.ProcessPanel.Contracts;

/// <summary>Implemented by the cluster overview panel. FrmMain pushes a fresh
/// snapshot each UI tick. All calls occur on the UI thread.</summary>
public interface IClusterProcessPanel
{
    void UpdateCluster(ClusterSnapshot snapshot);
}
