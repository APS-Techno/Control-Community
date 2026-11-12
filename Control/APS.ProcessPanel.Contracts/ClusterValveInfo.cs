namespace APS.ProcessPanel.Contracts;

/// <summary>One per-chamber valve's state for the cluster panel valve LEDs.</summary>
public sealed record ClusterValveInfo(string Id, string Descriptor, bool IsOpen);
