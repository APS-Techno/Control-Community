namespace APS.Wire;

/// <summary>Constants of Control's Agent line-JSON wire. The protocol version never bumped for the
/// telemetry bus — bus support is advertised as a capability in the auth reply instead.</summary>
public static class WireProtocol
{
    public const int Version = 1;
    public const string BusCapability = "bus";
}
