namespace APS.SystemControl.Core.Production;

public interface IPowerAccrualSink
{
    void Sample(string magnetronId, double watts, double dtSeconds);
}
