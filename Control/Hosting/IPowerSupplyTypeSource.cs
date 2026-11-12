namespace APS.SystemControl.Hosting;

internal interface IPowerSupplyTypeSource
{
    string? Classify(string id);
}
