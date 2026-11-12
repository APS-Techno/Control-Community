using System;
using System.Collections.Generic;
using System.Windows.Forms;
using APS.Controls.Core;
using APS.SystemControl.Plugin;

namespace CoreLogicTests.Fixtures;

internal sealed class StubMenuContext : IMenuContext, IDisposable
{
    private readonly Form _owner = new();

    public StubHw HwSink { get; } = new();
    public IHwCommandSink Hw => HwSink;
    public StubHardware HardwareRegistry { get; } = new();
    public IHardwareRegistry Hardware => HardwareRegistry;
    public IUiServices Ui { get; } = new StubUi();
    public IFlagService Flags { get; } = new StubFlags();
    /// <summary>Settable so a test can put the stub context into "a recipe owns the machine"
    /// or "the run is held for the operator" and see what a menu contributor does about it.</summary>
    public StubRuntime RuntimeState { get; } = new();
    public IRuntimeState Runtime => RuntimeState;
    public IAuthorization Auth { get; } = PermissiveAuthorization.Instance;
    public Form Owner => _owner;

    public void Dispose() => _owner.Dispose();

    internal sealed class StubHw : IHwCommandSink
    {
        /// <summary>Every command a contributor sent, so a gate test can assert that one did
        /// NOT reach the hardware rather than only that a control looked disabled.</summary>
        public readonly List<(DeviceType Type, string DeviceId, object[] Args)> Sent = new();
        public void SendCommand(DeviceType type, string deviceId, params object[] args)
            => Sent.Add((type, deviceId, args));
    }

    internal sealed class StubHardware : IHardwareRegistry
    {
        /// <summary>Devices a test has registered by id; empty by default, so
        /// <see cref="GetDevice{T}"/> keeps returning null for every test that never sets one.
        /// </summary>
        public readonly Dictionary<string, object> Devices = new(StringComparer.OrdinalIgnoreCase);
        public bool Has(DeviceType type, string deviceId) => true;
        public T? GetDevice<T>(string deviceId) where T : class
            => Devices.TryGetValue(deviceId, out var d) ? d as T : null;
        public IEnumerable<string> ListDevices(DeviceType type) => Array.Empty<string>();
    }

    private sealed class StubUi : IUiServices
    {
        public string? InputBox(string title, string prompt, string defaultValue = "") => null;
        public int? InputInt(string title, string prompt, int defaultValue) => null;
        public bool Confirm(string title, string message) => false;
        public DialogResult ShowMessage(string title, string message) => DialogResult.OK;
        public void RunOnUi(Action action) => action();
    }

    private sealed class StubFlags : IFlagService
    {
        public bool Has(string featureName) => true;
    }

    internal sealed class StubRuntime : IRuntimeState
    {
        public bool IsRecipeRunning { get; set; }

        /// <summary>Defaults to mirroring <see cref="IsRecipeRunning"/> — the un-held case — so
        /// existing tests that only set the run flag keep the behaviour they encoded. Set it
        /// explicitly to model a HELD run (running, but manual control unlocked).</summary>
        public bool? ManualControlGatedOverride { get; set; }
        public bool IsManualControlGated => ManualControlGatedOverride ?? IsRecipeRunning;
    }
}
