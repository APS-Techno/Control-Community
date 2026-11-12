namespace APS.SystemControl.Hosting;

internal interface IPlcSerialSource
{
    IReadOnlyList<string> GetPlcSerials();
}
