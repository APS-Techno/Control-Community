using APS.SystemControl.Core.HWClasses.Base;

namespace CoreLogicTests;

public class ModbusHelperTests
{
    [Fact]
    public void BuildReadRegisters_matches_canonical_bytes()
    {
        // FC03 frame without CRC, which BaseSerialPort appends.
        byte[] actual = ModbusHelper.BuildReadRegisters(slaveAddr: 1, startReg: 0x1001, count: 1);
        Assert.Equal(new byte[] { 0x01, 0x03, 0x10, 0x01, 0x00, 0x01 }, actual);
    }

    [Fact]
    public void BuildReadRegisters_encodes_slave_reg_count_big_endian()
    {
        byte[] actual = ModbusHelper.BuildReadRegisters(slaveAddr: 0x11, startReg: 0x1234, count: 0x5678);
        Assert.Equal(new byte[] { 0x11, 0x03, 0x12, 0x34, 0x56, 0x78 }, actual);
    }

    [Fact]
    public void BuildWriteRegister_matches_canonical_bytes()
    {
        byte[] actual = ModbusHelper.BuildWriteRegister(slaveAddr: 1, reg: 0x0001, value: 0x00FF);
        Assert.Equal(new byte[] { 0x01, 0x06, 0x00, 0x01, 0x00, 0xFF }, actual);
    }

    [Fact]
    public void BuildWriteRegister_encodes_big_endian_value()
    {
        byte[] actual = ModbusHelper.BuildWriteRegister(slaveAddr: 0x02, reg: 0x4041, value: 0xBEEF);
        Assert.Equal(new byte[] { 0x02, 0x06, 0x40, 0x41, 0xBE, 0xEF }, actual);
    }

    [Fact]
    public void BuildWriteMultipleRegisters_encodes_count_and_payload()
    {
        // FC16 frame: addr(1) + fn(1) + startReg(2) + count(2) + bytes(1) + data(2*count)
        byte[] actual = ModbusHelper.BuildWriteMultipleRegisters(
            slaveAddr: 1, startReg: 0x0010, values: [0x1122, 0x3344]);
        Assert.Equal(new byte[]
        {
            0x01, 0x10,
            0x00, 0x10,             // start reg
            0x00, 0x02,             // reg count
            0x04,                   // byte count
            0x11, 0x22, 0x33, 0x44  // payload
        }, actual);
    }

    [Fact]
    public void BuildWriteMultipleRegisters_rejects_empty_values()
    {
        Assert.Throws<ArgumentException>(() =>
            ModbusHelper.BuildWriteMultipleRegisters(1, 0, []));
    }

    [Fact]
    public void BuildWriteMultipleRegisters_rejects_null_values()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ModbusHelper.BuildWriteMultipleRegisters(1, 0, null!));
    }

    // ParseRegisterResponse expects the full frame including CRC.

    [Fact]
    public void ParseRegisterResponse_reads_single_register()
    {
        byte[] frame = ModbusHelper.AppendCRC([0x01, 0x03, 0x02, 0x12, 0x34]);
        ushort[] regs = ModbusHelper.ParseRegisterResponse(frame);
        Assert.Single(regs);
        Assert.Equal(0x1234, regs[0]);
    }

    [Fact]
    public void ParseRegisterResponse_reads_multiple_registers()
    {
        byte[] frame = ModbusHelper.AppendCRC([0x01, 0x03, 0x04, 0x00, 0x01, 0xFF, 0xFE]);
        ushort[] regs = ModbusHelper.ParseRegisterResponse(frame);
        Assert.Equal(2, regs.Length);
        Assert.Equal(0x0001, regs[0]);
        Assert.Equal(0xFFFE, regs[1]);
    }

    [Fact]
    public void ParseRegisterResponse_rejects_short_frame()
    {
        Assert.Throws<ArgumentException>(() =>
            ModbusHelper.ParseRegisterResponse([0x01, 0x03]));
    }

    [Fact]
    public void ParseRegisterResponse_rejects_bad_crc()
    {
        // Valid header but deliberately-wrong CRC bytes.
        byte[] frame = [0x01, 0x03, 0x02, 0x12, 0x34, 0x00, 0x00];
        Assert.Throws<ArgumentException>(() =>
            ModbusHelper.ParseRegisterResponse(frame));
    }

    [Fact]
    public void ParseRegisterResponse_rejects_exception_function_code()
    {
        // Modbus exception: fn has high bit set (0x03 | 0x80 = 0x83),
        // payload byte is the exception code.
        byte[] frame = ModbusHelper.AppendCRC([0x01, 0x83, 0x02, 0x00, 0x00]);
        Assert.Throws<InvalidOperationException>(() =>
            ModbusHelper.ParseRegisterResponse(frame));
    }

    [Fact]
    public void ParseRegisterResponse_rejects_wrong_function_code()
    {
        // fn 0x04 (Read Input Registers) is valid Modbus but unexpected here.
        byte[] frame = ModbusHelper.AppendCRC([0x01, 0x04, 0x02, 0x12, 0x34]);
        Assert.Throws<ArgumentException>(() =>
            ModbusHelper.ParseRegisterResponse(frame));
    }

    [Fact]
    public void VerifyCRC_round_trips_with_AppendCRC()
    {
        byte[] raw = [0x01, 0x06, 0x40, 0x41, 0xBE, 0xEF];
        byte[] framed = ModbusHelper.AppendCRC(raw);
        Assert.True(ModbusHelper.VerifyCRC(framed));
    }

    [Fact]
    public void VerifyCRC_detects_single_byte_corruption()
    {
        byte[] framed = ModbusHelper.AppendCRC([0x01, 0x06, 0x40, 0x41, 0xBE, 0xEF]);
        framed[3] ^= 0x01;
        Assert.False(ModbusHelper.VerifyCRC(framed));
    }

    [Fact]
    public void BuildReadInputRegisters_matches_canonical_bytes()
    {
        byte[] actual = ModbusHelper.BuildReadInputRegisters(slaveAddr: 1, startReg: 0x0000, count: 3);
        Assert.Equal(new byte[] { 0x01, 0x04, 0x00, 0x00, 0x00, 0x03 }, actual);
    }

    [Fact]
    public void BuildReadCoils_matches_canonical_bytes()
    {
        byte[] actual = ModbusHelper.BuildReadCoils(slaveAddr: 0x11, startCoil: 0x0013, count: 0x0025);
        Assert.Equal(new byte[] { 0x11, 0x01, 0x00, 0x13, 0x00, 0x25 }, actual);
    }

    [Fact]
    public void BuildWriteCoil_encodes_on_as_FF00_and_off_as_0000()
    {
        Assert.Equal(new byte[] { 0x01, 0x05, 0x00, 0x01, 0xFF, 0x00 },
            ModbusHelper.BuildWriteCoil(slaveAddr: 1, coil: 1, on: true));
        Assert.Equal(new byte[] { 0x01, 0x05, 0x00, 0x01, 0x00, 0x00 },
            ModbusHelper.BuildWriteCoil(slaveAddr: 1, coil: 1, on: false));
    }

    [Fact]
    public void ParseRegisterResponse_accepts_FC04_when_expected()
    {
        byte[] frame = ModbusHelper.AppendCRC([0x01, 0x04, 0x06, 0x0F, 0xFF, 0x00, 0x00, 0x08, 0x00]);
        ushort[] regs = ModbusHelper.ParseRegisterResponse(frame, 0x04);
        Assert.Equal(new ushort[] { 0x0FFF, 0x0000, 0x0800 }, regs);
    }

    [Fact]
    public void ParseRegisterResponse_rejects_FC03_reply_when_FC04_expected()
    {
        byte[] frame = ModbusHelper.AppendCRC([0x01, 0x03, 0x02, 0x12, 0x34]);
        Assert.Throws<ArgumentException>(() =>
            ModbusHelper.ParseRegisterResponse(frame, 0x04));
    }

    [Fact]
    public void ParseCoilResponse_unpacks_single_coil()
    {
        byte[] frame = ModbusHelper.AppendCRC([0x01, 0x01, 0x01, 0x01]);
        Assert.Equal(new[] { true }, ModbusHelper.ParseCoilResponse(frame, 1));

        frame = ModbusHelper.AppendCRC([0x01, 0x01, 0x01, 0x00]);
        Assert.Equal(new[] { false }, ModbusHelper.ParseCoilResponse(frame, 1));
    }

    [Fact]
    public void ParseCoilResponse_unpacks_eight_coils_LSB_first()
    {
        // 0b1010_0011 → coils 0,1,5,7 on
        byte[] frame = ModbusHelper.AppendCRC([0x01, 0x01, 0x01, 0xA3]);
        bool[] coils = ModbusHelper.ParseCoilResponse(frame, 8);
        Assert.Equal(new[] { true, true, false, false, false, true, false, true }, coils);
    }

    [Fact]
    public void ParseCoilResponse_unpacks_nine_coils_across_two_bytes()
    {
        byte[] frame = ModbusHelper.AppendCRC([0x01, 0x01, 0x02, 0x00, 0x01]);
        bool[] coils = ModbusHelper.ParseCoilResponse(frame, 9);
        Assert.Equal(9, coils.Length);
        Assert.All(coils[..8], c => Assert.False(c));
        Assert.True(coils[8]);
    }

    [Fact]
    public void ParseCoilResponse_rejects_byte_count_too_small_for_request()
    {
        byte[] frame = ModbusHelper.AppendCRC([0x01, 0x01, 0x01, 0xFF]);
        Assert.Throws<ArgumentException>(() =>
            ModbusHelper.ParseCoilResponse(frame, 9));
    }

    [Fact]
    public void ParseCoilResponse_rejects_exception_and_wrong_function()
    {
        byte[] exception = ModbusHelper.AppendCRC([0x01, 0x81, 0x02, 0x00]);
        Assert.Throws<InvalidOperationException>(() =>
            ModbusHelper.ParseCoilResponse(exception, 1));

        byte[] wrongFn = ModbusHelper.AppendCRC([0x01, 0x03, 0x02, 0x00, 0x01]);
        Assert.Throws<ArgumentException>(() =>
            ModbusHelper.ParseCoilResponse(wrongFn, 1));
    }
}
