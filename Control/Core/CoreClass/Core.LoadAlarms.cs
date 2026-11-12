using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.Classes;
using APS.SystemControl.Core.HWClasses.Base;

namespace APS.SystemControl.Core;

public partial class Core
{
    public bool LoadAlarms(string fileName)
    {
        try
        {
            var result = AlarmConfigLoader.Load(fileName, deviceResolver: ResolveDevice);

            foreach (var (id, a) in result.ChamberAlarms)
            {
                chamberAlarms[id] = a;
                if (result.InitialStates.TryGetValue(id, out var init) && init.HasValue)
                    chamberStates[id] = init.Value;
                else
                    chamberStates[id] = SystemState.Startup;
            }
            // Ensure the default chamber always exists even if Alarms.xml was silent
            // (defence-in-depth so the back-compat shim never throws).
            // MainIsAutoFallback flags the case where other real chambers were
            // declared but none was Main — UI uses it to hide the orphan tile.
            if (!chamberStates.ContainsKey(CoreConstants.DefaultChamberId))
            {
                MainIsAutoFallback = result.ChamberAlarms.Count > 0;
                chamberAlarms[CoreConstants.DefaultChamberId] = new Alarms(CoreConstants.DefaultChamberId);
                chamberStates[CoreConstants.DefaultChamberId] = SystemState.Startup;
            }
            else
            {
                MainIsAutoFallback = false;
            }

            foreach (var line in result.StateTransitionLog)
                SendLogMesssage("Core", line);

            foreach (var warning in result.Warnings)
                SendLogMesssage("Core", $"Alarms warning: {warning}");

            SendLogMesssage("Core", $"Alarms configuration loaded from {fileName}");
            return true;
        }
        catch (Exception ex)
        {
            LastErrorMessage = "Failed to load alarms from file: " + ex.Message;
            return false;
        }
    }

    private IBaseComponent? ResolveDevice(string id)
    {
        foreach (var elements in hwElements.Item.Values)
        {
            if (elements._elements.TryGetValue(id, out var component))
                return component;
        }
        return null;
    }
}
