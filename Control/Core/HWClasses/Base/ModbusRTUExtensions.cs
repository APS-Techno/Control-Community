namespace APS.SystemControl.Core.HWClasses.Base;

/// <summary>
/// The <see cref="BaseSerialPort"/> must be configured with
/// <c>FramingMode.ModbusRTU</c> so the CRC is appended on send and verified on
/// receive.
/// </summary>
public static class ModbusRTUExtensions
{
    public static ushort[] ReadHoldingRegisters(
        this BaseSerialPort serial, byte slaveAddr, ushort startRegister, ushort count)
    {
        ArgumentNullException.ThrowIfNull(serial);

        byte[] request = ModbusHelper.BuildReadRegisters(slaveAddr, startRegister, count);
        // Response: addr(1) + fn(1) + byteCount(1) + data(count*2) + CRC(2)
        int expectedLen = 3 + count * 2 + 2;
        byte[] response = serial.SendAndReceive(request, expectedLen);
        return ModbusHelper.ParseRegisterResponse(response);
    }

    /// <summary>
    /// Writes a single 16-bit value to holding register <paramref name="register"/>
    /// on <paramref name="slaveAddr"/>. The echo response is consumed and
    /// discarded; on framing error the underlying SendAndReceive throws.
    /// </summary>
    public static void WriteHoldingRegister(
        this BaseSerialPort serial, byte slaveAddr, ushort register, ushort value)
    {
        ArgumentNullException.ThrowIfNull(serial);

        byte[] request = ModbusHelper.BuildWriteRegister(slaveAddr, register, value);
        // Echo response: addr(1) + fn(1) + reg(2) + val(2) + CRC(2) = 8 bytes
        serial.SendAndReceive(request, 8);
    }

    /// <summary>
    /// Reads <paramref name="count"/> 16-bit input registers (function 0x04)
    /// starting at <paramref name="startRegister"/> from <paramref name="slaveAddr"/>.
    /// Remote I/O modules typically publish analog inputs here.
    /// </summary>
    public static ushort[] ReadInputRegisters(
        this BaseSerialPort serial, byte slaveAddr, ushort startRegister, ushort count)
    {
        ArgumentNullException.ThrowIfNull(serial);

        byte[] request = ModbusHelper.BuildReadInputRegisters(slaveAddr, startRegister, count);
        int expectedLen = 3 + count * 2 + 2;
        byte[] response = serial.SendAndReceive(request, expectedLen);
        return ModbusHelper.ParseRegisterResponse(response, 0x04);
    }

    /// <summary>
    /// Reads <paramref name="count"/> coils (function 0x01) starting at
    /// <paramref name="startCoil"/> from <paramref name="slaveAddr"/>.
    /// </summary>
    public static bool[] ReadCoils(
        this BaseSerialPort serial, byte slaveAddr, ushort startCoil, ushort count)
    {
        ArgumentNullException.ThrowIfNull(serial);

        byte[] request = ModbusHelper.BuildReadCoils(slaveAddr, startCoil, count);
        // Response: addr(1) + fn(1) + byteCount(1) + ceil(count/8) bytes + CRC(2)
        int expectedLen = 3 + (count + 7) / 8 + 2;
        byte[] response = serial.SendAndReceive(request, expectedLen);
        return ModbusHelper.ParseCoilResponse(response, count);
    }

    /// <summary>
    /// Writes a single coil (function 0x05) on <paramref name="slaveAddr"/>.
    /// The echo response is consumed and discarded; on framing error the
    /// underlying SendAndReceive throws.
    /// </summary>
    public static void WriteCoil(
        this BaseSerialPort serial, byte slaveAddr, ushort coil, bool on)
    {
        ArgumentNullException.ThrowIfNull(serial);

        byte[] request = ModbusHelper.BuildWriteCoil(slaveAddr, coil, on);
        // Echo response: addr(1) + fn(1) + coil(2) + val(2) + CRC(2) = 8 bytes
        serial.SendAndReceive(request, 8);
    }
}
