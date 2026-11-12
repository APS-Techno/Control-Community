using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;
using Xunit;

namespace APS.SystemControl.Tests.CoreLogic;

public class IsolateOperationTests
{
    [Fact]
    public async Task RunAsync_ClosesAllConnectedValvesAndZeroesAllConnectedMFCs()
    {
        var v1 = new FakeValve("V1", initialOpen: true);
        var v2 = new FakeValve("V2", initialOpen: true);
        var mfc1 = new FakeMfc("Ar1", initialFlow: 5.0f);
        var mfc2 = new FakeMfc("N2_1", initialFlow: 7.0f);

        await IsolateOperation.RunAsync(
            valves: [v1, v2],
            mfcs:   [mfc1, mfc2],
            ct: default);

        Assert.False(v1.RawIsOpen);
        Assert.False(v2.RawIsOpen);
        Assert.Equal(0f, mfc1.LastWritten);
        Assert.Equal(0f, mfc2.LastWritten);
    }

    [Fact]
    public async Task RunAsync_SkipsDisconnectedDevices()
    {
        var connected    = new FakeValve("V1", initialOpen: true);
        var disconnected = new FakeValve("V2", initialOpen: true) { Connected = false };
        await IsolateOperation.RunAsync([connected, disconnected], [], default);
        Assert.False(connected.RawIsOpen);
        Assert.True(disconnected.RawIsOpen);   // untouched
    }

    [Fact]
    public async Task RunAsync_RespectsCancellation()
    {
        var v = new FakeValve("V1", initialOpen: true);
        var mfc = new FakeMfc("Ar1", initialFlow: 5.0f);
        var cts = new CancellationTokenSource();
        cts.Cancel();   // pre-cancelled

        await Assert.ThrowsAsync<TaskCanceledException>(async () =>
            await IsolateOperation.RunAsync([v], [mfc], cts.Token));
    }

    private sealed class FakeValve : BaseValve
    {
        private bool _isOpen;
        public bool RawIsOpen => _isOpen;
        public FakeValve(string id, bool initialOpen)
            : base(new BaseDeviceParams(id, 0, id))
        {
            _isOpen = initialOpen;
            Connected = true;
        }
        protected override bool ReadHW() => _isOpen;
        protected override void WriteHW(bool n) => _isOpen = n;
    }

    private sealed class FakeMfc : BaseMFC
    {
        public float LastWritten { get; private set; }
        public FakeMfc(string id, float initialFlow)
            : base(new BaseDeviceParams(id, 0, id))
        {
            LastWritten = initialFlow;
            Connected = true;
        }
        protected override float ReadHW() => LastWritten;
        protected override void WriteHW(float n) => LastWritten = n;
    }
}
