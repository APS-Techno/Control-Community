using System.Xml.Linq;

namespace APS.SystemControl.Tests.Shared;

public static class WorkerScan
{
    public static string TypeName(string workerPath)
        => XDocument.Load(workerPath).Root!.Element("operations")!.Attribute("typename")!.Value;
}
