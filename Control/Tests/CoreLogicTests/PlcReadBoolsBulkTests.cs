using Xunit;

namespace APS.SystemControl.Tests.CoreLogic;

public class PlcReadBoolsBulkTests
{
    [Fact]
    public void ReadBoolsBulk_returns_correct_bools_for_each_request()
    {
        var plc = new FakePlcController();
        plc.WriteBool(3, 100, 0, true);   // MB 100 bit 0 = true
        plc.WriteBool(3, 100, 5, true);   // MB 100 bit 5 = true
        plc.WriteBool(3, 101, 2, false);  // MB 101 bit 2 = false (default anyway)

        var requests = new[] {
            (mode: 3, address: 100, byteIndex: 0),
            (mode: 3, address: 100, byteIndex: 5),
            (mode: 3, address: 101, byteIndex: 2),
        };

        var result = plc.ReadBoolsBulk(requests);

        Assert.Equal([true, true, false], result);
        Assert.Equal(1, plc.BoolBulkReads);
        Assert.Equal(3, plc.BoolBulkBitsRead);
        Assert.Equal(0, plc.BoolReads); // bulk path must NOT increment per-bit counter
    }

    [Fact]
    public void ReadBoolsBulk_returns_empty_for_empty_input()
    {
        var plc = new FakePlcController();
        var result = plc.ReadBoolsBulk([]);
        Assert.Empty(result);
        Assert.Equal(1, plc.BoolBulkReads); // even empty calls count
    }
}
