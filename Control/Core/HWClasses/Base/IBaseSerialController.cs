using System.IO.Ports;

namespace APS.SystemControl.Core.HWClasses.Base;



public struct SerialConnectionParams
{
    public string PortName;
    public int BaudRate;
    public Parity Parity;
    public int DataBits;
    public StopBits StopBits;
}
public interface IBaseSerialController
{
    public abstract bool Connect(SerialConnectionParams connectionParams);
    public abstract void Write(string data);
    public abstract string Read();

}
