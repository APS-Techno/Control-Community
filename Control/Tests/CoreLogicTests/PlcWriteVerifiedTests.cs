using APS.Hardware.S7;
using Xunit;

namespace APS.SystemControl.Tests.CoreLogic;

public class PlcWriteVerifiedTests
{
    [Fact]
    public void WriteFloatVerified_Succeeds_WhenWriteIsAcceptedFirstTry()
    {
        var fake = new FakePlcController();
        bool ok = PlcVerifiedWrites.WriteFloatVerified(
            fake, mode: (int)Mode.DB, address: 100, value: 25.0f,
            tolerance: 0.5f, maxAttempts: 5);
        Assert.True(ok);
        Assert.Equal(1, fake.FloatWrites);
        Assert.Equal(25.0f, fake.ReadFloat((int)Mode.DB, 100));
    }

    [Fact]
    public void WriteFloatVerified_RetriesAndSucceeds_WhenFirstWritesAreDropped()
    {
        var fake = new FakePlcController();
        fake.DropNextWrites(((int)Mode.DB, 100), count: 2);
        bool ok = PlcVerifiedWrites.WriteFloatVerified(
            fake, (int)Mode.DB, 100, 25.0f, tolerance: 0.5f, maxAttempts: 5);
        Assert.True(ok);
        Assert.Equal(3, fake.FloatWrites);
    }

    [Fact]
    public void WriteFloatVerified_ReturnsFalse_AfterMaxAttempts()
    {
        var fake = new FakePlcController();
        fake.DropNextWrites(((int)Mode.DB, 100), count: 99);
        bool ok = PlcVerifiedWrites.WriteFloatVerified(
            fake, (int)Mode.DB, 100, 25.0f, tolerance: 0.5f, maxAttempts: 5);
        Assert.False(ok);
        Assert.Equal(5, fake.FloatWrites);
    }

    [Fact]
    public void WriteFloatVerified_TreatsValuesWithinTolerance_AsSuccess()
    {
        var fake = new FakePlcController();
        // Simulate ADC drift: PLC always reads back 25.3 even when 25.0 was written.
        fake.FixedFloatReadback(((int)Mode.DB, 100), 25.3f);
        bool ok = PlcVerifiedWrites.WriteFloatVerified(
            fake, (int)Mode.DB, 100, 25.0f, tolerance: 0.5f, maxAttempts: 5);
        // |25.3 - 25.0| = 0.3 <= 0.5 → first attempt accepted as within tolerance.
        Assert.True(ok);
        Assert.Equal(1, fake.FloatWrites);   // no retry
    }

    [Fact]
    public void WriteFloatVerified_RejectsValueOutsideTolerance()
    {
        var fake = new FakePlcController();
        fake.FixedFloatReadback(((int)Mode.DB, 100), 26.0f);
        bool ok = PlcVerifiedWrites.WriteFloatVerified(
            fake, (int)Mode.DB, 100, 25.0f, tolerance: 0.5f, maxAttempts: 3);
        Assert.False(ok);
        Assert.Equal(3, fake.FloatWrites);   // exhausted attempts
    }

    [Fact]
    public void WriteFloatVerified_RetriesAndSucceeds_WhenReadbackThrowsCommException()
    {
        var fake = new FakePlcController();
        fake.ThrowCommOnNextReads(2);
        bool ok = PlcVerifiedWrites.WriteFloatVerified(
            fake, (int)Mode.DB, 100, 25.0f, tolerance: 0.5f, maxAttempts: 5);
        Assert.True(ok);
        Assert.Equal(3, fake.FloatWrites);   // retried past the two comm failures
    }

    [Fact]
    public void WriteFloatVerified_ReturnsFalse_WhenReadbackAlwaysThrows()
    {
        var fake = new FakePlcController();
        fake.ThrowCommOnNextReads(99);
        bool ok = PlcVerifiedWrites.WriteFloatVerified(
            fake, (int)Mode.DB, 100, 25.0f, tolerance: 0.5f, maxAttempts: 3);
        Assert.False(ok);
        Assert.Equal(3, fake.FloatWrites);   // exhausted attempts, no escaping exception
    }

    [Fact]
    public void WriteBoolVerified_RetriesAndSucceeds_WhenReadbackThrowsCommException()
    {
        var fake = new FakePlcController();
        fake.ThrowCommOnNextReads(1);
        bool ok = PlcVerifiedWrites.WriteBoolVerified(
            fake, (int)Mode.MB, 30, byteIndex: 1, value: true, maxAttempts: 5);
        Assert.True(ok);
        Assert.Equal(2, fake.BoolWrites);
    }

    [Fact]
    public void WriteBoolVerified_Succeeds_WhenWriteIsAcceptedFirstTry()
    {
        var fake = new FakePlcController();
        bool ok = PlcVerifiedWrites.WriteBoolVerified(
            fake, (int)Mode.MB, 30, byteIndex: 1, value: true, maxAttempts: 5);
        Assert.True(ok);
        Assert.Equal(1, fake.BoolWrites);
        Assert.True(fake.ReadBool((int)Mode.MB, 30, 1));
    }

    [Fact]
    public void WriteBoolVerified_ReturnsFalse_WhenAllAttemptsDropped()
    {
        var fake = new FakePlcController();
        fake.DropNextWrites(((int)Mode.MB, 30), count: 99); // shared drop table by design
        bool ok = PlcVerifiedWrites.WriteBoolVerified(
            fake, (int)Mode.MB, 30, 1, true, maxAttempts: 3);
        Assert.False(ok);
        Assert.Equal(3, fake.BoolWrites);
    }
}
