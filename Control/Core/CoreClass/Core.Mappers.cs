using APS.SystemControl.BaseHWClasses;

namespace APS.SystemControl.Core.CoreClass;

public class HwElementCollection
{
    public Dictionary<DeviceType, Elements> Item = new()
    {
        { DeviceType.Valve,  []},
        { DeviceType.Pump,   []},
        { DeviceType.MFC,    []},
        { DeviceType.Sensor, []},
        { DeviceType.Stage,  []},
        { DeviceType.Controller,  []},
        { DeviceType.Power,  []},
        { DeviceType.Drive,  []},
        { DeviceType.Robot,  []},
    };
}

public class HwXmlTypeMapper
{
    public Dictionary<DeviceType, Type> Type = new()
    {
        { DeviceType.Valve,  typeof(BaseValve) },
        { DeviceType.Pump,  typeof(BasePump) },
        { DeviceType.MFC,  typeof(BaseMFC) },
        { DeviceType.Sensor,  typeof(BaseSensor) },
        { DeviceType.Stage,  typeof(BaseStage) },
        { DeviceType.Controller,  typeof(BaseController) },
        { DeviceType.Power,  typeof(BasePowerSupply) },
        { DeviceType.Drive,  typeof(BaseStepperDrive) },
        { DeviceType.Robot,  typeof(BaseRobot) },
    };

    public static string Name(DeviceType target)
    {
        return target switch
        {
            DeviceType.Valve => "valves",
            DeviceType.Pump => "pumps",
            DeviceType.MFC => "mfcs",
            DeviceType.Sensor => "sensors",
            DeviceType.Stage => "stages",
            DeviceType.Controller => "controllers",
            DeviceType.Power => "powersupplies",
            DeviceType.Drive => "drives",
            DeviceType.Robot => "robots",
            _ => "unknown",
        };
    }
}
