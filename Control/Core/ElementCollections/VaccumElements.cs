using APS.SystemControl.BaseHWClasses;

namespace APS.SystemControl.Core
{
    public class Valves : Elements
    {

    }

    public class MFCs : Elements
    {
        public void SetFlow(string name, float value)
        {
            if (_elements[name] is PLCMFC mfc)
                mfc.SetFlow(value);
        }
    }

    public class PLCs : Elements
    {
    }


    public class Sensors : Elements
    {
    }

    public class Stages : Elements
    {
    }

    public class Pumps : Elements
    {
        public void Power(string name, bool state)
        {
            if (_elements[name] is PlcPump pump)
                pump.Power(state);
        }

        public bool[] Status(string name)
        {
            if (_elements[name] is PlcPump pump)
                return pump.Status;
            else
                return [];
        }
    }

}
