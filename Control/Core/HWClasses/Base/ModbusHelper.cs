namespace APS.SystemControl.Core.HWClasses.Base;

/// <remarks>
/// BuildXxx methods do NOT append CRC — the BaseSerialPort framing layer
/// is responsible for appending CRC via AppendCRC before transmission.
/// ParseRegisterResponse performs CRC verification internally.
/// </remarks>
public static class ModbusHelper
{
    #region CRC16 Lookup Table

    // Pre-computed CRC16/IBM (Modbus) lookup table (polynomial 0xA001, reflected).
    private static readonly ushort[] _crcTable = BuildCrcTable();

    private static ushort[] BuildCrcTable()
    {
        const ushort Polynomial = 0xA001;
        var table = new ushort[256];
        for (int i = 0; i < 256; i++)
        {
            ushort crc = (ushort)i;
            for (int j = 0; j < 8; j++)
            {
                if ((crc & 0x0001) != 0)
                    crc = (ushort)((crc >> 1) ^ Polynomial);
                else
                    crc >>= 1;
            }
            table[i] = crc;
        }
        return table;
    }

    #endregion

    #region CRC Methods

    public static ushort CalcCRC16(byte[] data, int offset, int length)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (offset < 0 || length < 0 || offset + length > data.Length)
            throw new ArgumentOutOfRangeException(nameof(length), "offset/length exceed data bounds.");

        ushort crc = 0xFFFF;
        for (int i = offset; i < offset + length; i++)
            crc = (ushort)((_crcTable[(crc ^ data[i]) & 0xFF]) ^ (crc >> 8));
        return crc;
    }

    /// <summary>
    /// Returns a new byte array containing <paramref name="frame"/> followed
    /// by its CRC16 in little-endian order (low byte first).
    /// </summary>
    public static byte[] AppendCRC(byte[] frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ushort crc = CalcCRC16(frame, 0, frame.Length);
        var result = new byte[frame.Length + 2];
        Array.Copy(frame, result, frame.Length);
        result[frame.Length]     = (byte)(crc & 0xFF);
        result[frame.Length + 1] = (byte)((crc >> 8) & 0xFF);
        return result;
    }

    /// <summary>
    /// Verifies that the last two bytes of <paramref name="frame"/> contain
    /// the correct Modbus CRC16 for the preceding bytes.
    /// </summary>
    public static bool VerifyCRC(byte[] frame)
    {
        if (frame == null || frame.Length < 3)
            return false;

        ushort expected = CalcCRC16(frame, 0, frame.Length - 2);
        ushort received = (ushort)(frame[frame.Length - 2] | (frame[frame.Length - 1] << 8));
        return expected == received;
    }

    #endregion

    #region Frame Builders (without CRC)

    /// <summary>
    /// Builds a Modbus function 0x03 (Read Holding Registers) request frame
    /// without CRC. BaseSerialPort appends CRC before transmission.
    /// </summary>
    /// <param name="slaveAddr">Slave device address (1–247).</param>
    /// <param name="startReg">Starting register address (0-based).</param>
    public static byte[] BuildReadRegisters(byte slaveAddr, ushort startReg, ushort count)
    {
        return
        [
            slaveAddr,
            0x03,
            (byte)(startReg >> 8),
            (byte)(startReg & 0xFF),
            (byte)(count >> 8),
            (byte)(count & 0xFF),
        ];
    }

    /// <summary>
    /// Builds a Modbus function 0x06 (Write Single Register) request frame
    /// without CRC.
    /// </summary>
    /// <param name="slaveAddr">Slave device address (1–247).</param>
    /// <param name="reg">Register address (0-based).</param>
    public static byte[] BuildWriteRegister(byte slaveAddr, ushort reg, ushort value)
    {
        return
        [
            slaveAddr,
            0x06,
            (byte)(reg >> 8),
            (byte)(reg & 0xFF),
            (byte)(value >> 8),
            (byte)(value & 0xFF),
        ];
    }

    /// <summary>
    /// Builds a Modbus function 0x10 (Write Multiple Registers) request frame
    /// without CRC.
    /// </summary>
    /// <param name="slaveAddr">Slave device address (1–247).</param>
    /// <param name="startReg">Starting register address (0-based).</param>
    public static byte[] BuildWriteMultipleRegisters(byte slaveAddr, ushort startReg, ushort[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length == 0)
            throw new ArgumentException("At least one register value is required.", nameof(values));

        int regCount  = values.Length;
        int byteCount = regCount * 2;

        // Header: addr(1) + fn(1) + startReg(2) + count(2) + byteCount(1) = 7 bytes
        var frame = new byte[7 + byteCount];
        frame[0] = slaveAddr;
        frame[1] = 0x10;
        frame[2] = (byte)(startReg >> 8);
        frame[3] = (byte)(startReg & 0xFF);
        frame[4] = (byte)(regCount >> 8);
        frame[5] = (byte)(regCount & 0xFF);
        frame[6] = (byte)byteCount;

        for (int i = 0; i < regCount; i++)
        {
            frame[7 + i * 2]     = (byte)(values[i] >> 8);
            frame[7 + i * 2 + 1] = (byte)(values[i] & 0xFF);
        }
        return frame;
    }

    /// <summary>
    /// Builds a Modbus function 0x04 (Read Input Registers) request frame
    /// without CRC. Same layout as FC03; only the function code differs.
    /// Remote I/O modules commonly expose analog inputs as input registers.
    /// </summary>
    public static byte[] BuildReadInputRegisters(byte slaveAddr, ushort startReg, ushort count)
    {
        return
        [
            slaveAddr,
            0x04,
            (byte)(startReg >> 8),
            (byte)(startReg & 0xFF),
            (byte)(count >> 8),
            (byte)(count & 0xFF),
        ];
    }

    /// <summary>
    /// Builds a Modbus function 0x01 (Read Coils) request frame without CRC.
    /// </summary>
    public static byte[] BuildReadCoils(byte slaveAddr, ushort startCoil, ushort count)
    {
        return
        [
            slaveAddr,
            0x01,
            (byte)(startCoil >> 8),
            (byte)(startCoil & 0xFF),
            (byte)(count >> 8),
            (byte)(count & 0xFF),
        ];
    }

    /// <summary>
    /// Builds a Modbus function 0x05 (Write Single Coil) request frame without
    /// CRC. The protocol encodes ON as 0xFF00 and OFF as 0x0000; any other
    /// value is an illegal data value on a compliant slave.
    /// </summary>
    public static byte[] BuildWriteCoil(byte slaveAddr, ushort coil, bool on)
    {
        return
        [
            slaveAddr,
            0x05,
            (byte)(coil >> 8),
            (byte)(coil & 0xFF),
            (byte)(on ? 0xFF : 0x00),
            0x00,
        ];
    }

    #endregion

    #region Response Parser

    /// <summary>
    /// Parses a Modbus function 0x03 (Read Holding Registers) or, when
    /// <paramref name="expectedFn"/> is 0x04, a Read Input Registers response.
    /// Verifies CRC and returns the decoded register values.
    /// </summary>
    /// <param name="response">Raw response bytes including the trailing CRC.</param>
    /// <param name="expectedFn">0x03 (default) or 0x04. Any other reply function is rejected.</param>
    /// <returns>Array of register values (big-endian word order from the frame).</returns>
    /// <exception cref="ArgumentException">Thrown when response is too short or CRC is invalid.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the function code indicates an error.</exception>
    public static ushort[] ParseRegisterResponse(byte[] response, byte expectedFn = 0x03)
    {
        ArgumentNullException.ThrowIfNull(response);

        // Minimum valid response: addr(1) + fn(1) + byteCount(1) + 2 data bytes + CRC(2) = 7 bytes
        if (response.Length < 7)
            throw new ArgumentException($"Response too short ({response.Length} bytes; minimum 7).", nameof(response));

        if (!VerifyCRC(response))
            throw new ArgumentException("Modbus CRC verification failed.", nameof(response));

        byte fn = response[1];

        // Check for exception response (function code has high bit set)
        if ((fn & 0x80) != 0)
            throw new InvalidOperationException($"Modbus exception response: function 0x{fn:X2}, code {response[2]}.");

        if (fn != expectedFn)
            throw new ArgumentException($"Unexpected function code 0x{fn:X2} in response (expected 0x{expectedFn:X2}).", nameof(response));

        int byteCount = response[2];
        if (byteCount % 2 != 0)
            throw new ArgumentException($"Byte count {byteCount} is odd; Modbus registers are 16-bit pairs.", nameof(response));

        if (response.Length < 3 + byteCount + 2)
            throw new ArgumentException(
                $"Response length {response.Length} is inconsistent with declared byte count {byteCount}.",
                nameof(response));

        int regCount = byteCount / 2;
        var registers = new ushort[regCount];
        for (int i = 0; i < regCount; i++)
        {
            registers[i] = (ushort)((response[3 + i * 2] << 8) | response[3 + i * 2 + 1]);
        }
        return registers;
    }

    /// <summary>
    /// Parses a Modbus function 0x01 (Read Coils) response. Verifies CRC and
    /// unpacks the coil bits LSB-first, as the protocol packs them. Returns
    /// exactly <paramref name="count"/> values; padding bits in the last byte
    /// are dropped.
    /// </summary>
    /// <param name="response">Raw response bytes including the trailing CRC.</param>
    /// <exception cref="ArgumentException">Thrown when response is too short, CRC is invalid, or the byte count does not cover <paramref name="count"/> coils.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the function code indicates an error.</exception>
    public static bool[] ParseCoilResponse(byte[] response, int count)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count), count, "At least one coil must be requested.");

        // Minimum valid response: addr(1) + fn(1) + byteCount(1) + 1 data byte + CRC(2) = 6 bytes
        if (response.Length < 6)
            throw new ArgumentException($"Response too short ({response.Length} bytes; minimum 6).", nameof(response));

        if (!VerifyCRC(response))
            throw new ArgumentException("Modbus CRC verification failed.", nameof(response));

        byte fn = response[1];
        if ((fn & 0x80) != 0)
            throw new InvalidOperationException($"Modbus exception response: function 0x{fn:X2}, code {response[2]}.");

        if (fn != 0x01)
            throw new ArgumentException($"Unexpected function code 0x{fn:X2} in response (expected 0x01).", nameof(response));

        int byteCount = response[2];
        int needed = (count + 7) / 8;
        if (byteCount < needed)
            throw new ArgumentException(
                $"Byte count {byteCount} cannot hold {count} coils (needs {needed}).", nameof(response));

        if (response.Length < 3 + byteCount + 2)
            throw new ArgumentException(
                $"Response length {response.Length} is inconsistent with declared byte count {byteCount}.",
                nameof(response));

        var coils = new bool[count];
        for (int i = 0; i < count; i++)
            coils[i] = (response[3 + i / 8] & (1 << (i % 8))) != 0;
        return coils;
    }

    #endregion

    #region IEEE 754 Float Encoding/Decoding

    /// <summary>
    /// Encodes a <see cref="float"/> as a 4-byte big-endian IEEE 754 array.
    /// </summary>
    public static byte[] EncodeIEEE754(float value)
    {
        byte[] le = BitConverter.GetBytes(value); // always little-endian on .NET
        return [le[3], le[2], le[1], le[0]];
    }

    /// <summary>
    /// Decodes a big-endian IEEE 754 float from 4 bytes starting at
    /// <paramref name="offset"/> in <paramref name="data"/>.
    /// </summary>
    public static float DecodeIEEE754(byte[] data, int offset)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (offset < 0 || offset + 4 > data.Length)
            throw new ArgumentOutOfRangeException(nameof(offset), "offset + 4 exceeds data length.");

        byte[] le = [data[offset + 3], data[offset + 2], data[offset + 1], data[offset]];
        return BitConverter.ToSingle(le, 0);
    }

    /// <summary>
    /// Encodes a <see cref="float"/> as two 16-bit Modbus registers in
    /// big-endian IEEE 754 order: registers[0] = high word, registers[1] = low word.
    /// </summary>
    public static ushort[] FloatToRegisters(float value)
    {
        byte[] be = EncodeIEEE754(value);
        ushort hi = (ushort)((be[0] << 8) | be[1]);
        ushort lo = (ushort)((be[2] << 8) | be[3]);
        return [hi, lo];
    }

    /// <summary>
    /// Decodes a <see cref="float"/> from two Modbus registers stored in
    /// big-endian IEEE 754 word order.
    /// </summary>
    /// <param name="hi">High word (first register).</param>
    /// <param name="lo">Low word (second register).</param>
    public static float RegistersToFloat(ushort hi, ushort lo)
    {
        byte[] be =
        [
            (byte)(hi >> 8),
            (byte)(hi & 0xFF),
            (byte)(lo >> 8),
            (byte)(lo & 0xFF),
        ];
        return DecodeIEEE754(be, 0);
    }

    #endregion
}
