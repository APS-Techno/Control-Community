using APS.SystemControl.Core;

namespace APS.SystemControl;

public static class GUIConst
{
    public const string VALVE_PREFIX = "VLV_";
    public const string MFC_PREFIX = "MFC_";
    public const string SENSOR_PREFIX = "SNS_";
    public const string PUMP_PREFIX = "PMP_";
    public const string POWER_PREFIX = "POW_";
    public const string STAGE_PREFIX = "STG_";
    public const string PANEL_PREFIX = "PNL_";
    public const string GASINLET_PREFIX = "GAS_";
    public const string MAGNETRON_PREFIX = "MAG_";
    public const string ROBOT_PREFIX = "ROB_";

    public const string SYSTEM_PREFIX = "SYS_";

    public static string ControlName(DeviceType type, string id) =>
        type switch
        {
            DeviceType.System => SYSTEM_PREFIX + id,
            DeviceType.Pump => PUMP_PREFIX + id,
            DeviceType.Valve => VALVE_PREFIX + id,
            DeviceType.Stage => STAGE_PREFIX + id,
            DeviceType.Power => POWER_PREFIX + id,
            DeviceType.MFC => MFC_PREFIX + id,
            DeviceType.Sensor => SENSOR_PREFIX + id,
            DeviceType.Controller => PANEL_PREFIX + id,
            DeviceType.Robot => ROBOT_PREFIX + id,
            _ => ""
        };
}
