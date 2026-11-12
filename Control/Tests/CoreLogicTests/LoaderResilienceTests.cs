using System;
using System.Collections.Generic;
using System.Reflection;
using System.Xml.Linq;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core;
using APS.SystemControl.Core.CoreClass;
using APS.SystemControl.XML;
using Xunit;
using SysCore = APS.SystemControl.Core.Core;

namespace APS.SystemControl.Tests.CoreLogic;

/// <summary>
/// Code audit 2026-08-29:
///   C19 — '#' comment and whitespace-only lines are legal DSL that the legacy .recipe
///        execution path must skip, as the interpreter path does.
///   C21 — one failing PLC must not abandon the PLCs after it, and its real exception
///        text must reach the operator.
/// </summary>
public class LoaderResilienceTests
{
    [Fact]
    public async Task Legacy_recipe_tolerates_comment_and_whitespace_lines()
    {
        var thread = new CoreThread(new ChannelPool());

        bool ok = await thread.ExecuteRecipe(["# just a comment", "   ", "\t"]);

        Assert.True(ok);
    }

    public sealed class LoadablePlc : IPLCController
    {
        public bool Connected => true;
        public void Connect() { }
        public void Connect(int Address) { }
        public void Connect(string Address) { }
        public void Connect(string Address, int Rack, int Slot) { }
        public void Disconnect() { }
        public void WriteFloat(int mode, int address, float value) { }
        public float ReadFloat(int mode, int address) => 0f;
        public void WriteInt(int mode, int address, short value) { }
        public int ReadInt(int mode, int address) => 0;
        public void WriteBool(int mode, int address, int index, bool value) { }
        public bool ReadBool(int mode, int address, int index) => false;
        public void WriteString(int mode, int address, string value) { }
        public string ReadString(int mode, int address) => "";
        public bool[] ReadBoolsBulk(IReadOnlyList<(int mode, int address, int byteIndex)> requests) =>
            new bool[requests.Count];
        public byte ReadByte(int mode, int address) => 0;
        public void WriteByte(int mode, int address, byte value) { }
    }

    public sealed class ConnectThrowsPlc : IPLCController
    {
        public bool Connected => false;
        public void Connect() { }
        public void Connect(int Address) { }
        public void Connect(string Address) => throw new InvalidOperationException("boom: host unreachable");
        public void Connect(string Address, int Rack, int Slot) { }
        public void Disconnect() { }
        public void WriteFloat(int mode, int address, float value) { }
        public float ReadFloat(int mode, int address) => 0f;
        public void WriteInt(int mode, int address, short value) { }
        public int ReadInt(int mode, int address) => 0;
        public void WriteBool(int mode, int address, int index, bool value) { }
        public bool ReadBool(int mode, int address, int index) => false;
        public void WriteString(int mode, int address, string value) { }
        public string ReadString(int mode, int address) => "";
        public bool[] ReadBoolsBulk(IReadOnlyList<(int mode, int address, int byteIndex)> requests) =>
            new bool[requests.Count];
        public byte ReadByte(int mode, int address) => 0;
        public void WriteByte(int mode, int address, byte value) { }
    }

    private static XElement PlcElement(string className, string id) =>
        // Attributes(): Atr[0] = element Value (the id), then positional
        // attributes from index 1 — Atr[1] = class, Atr[2] = address.
        new("controller",
            new XAttribute("class", className),
            new XAttribute("address", "127.0.0.1"))
        { Value = id };

    [Fact]
    public void LoadPLCs_reports_the_real_error_and_continues_past_bad_entries()
    {
        var pool = new ChannelPool();
        var core = new CoreThread(pool).Core;

        // Register this test assembly as the section's hardware DLL.
        var dlls = typeof(SysCore)
            .GetField("HWDlls", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(core)!;
        dlls.GetType().GetMethod("Add")!.Invoke(dlls, ["TestLib", typeof(LoadablePlc).Assembly]);

        var section = new XMLHWSection("plcs");
        section.SetAttributeValue("lib", "TestLib");
        section._list.Add(PlcElement("No.Such.Class", "missing"));
        section._list.Add(PlcElement(typeof(ConnectThrowsPlc).FullName!, "dead"));
        section._list.Add(PlcElement(typeof(LoadablePlc).FullName!, "good"));

        bool result = core.LoadPLCs(section);

        Assert.True(result);
        Assert.True(core.plcs.ContainsKey("good"));
        Assert.False(core.plcs.ContainsKey("missing"));
        Assert.False(core.plcs.ContainsKey("dead"));

        // The REAL errors reached the operator channel (not "Syntax error: PLC").
        var errors = new List<string>();
        while (pool.GUIReader!.TryRead(out var msg))
            if (msg is ValMsg<string> { Value: not null } v) errors.Add(v.Value);
        Assert.Contains(errors, e => e.Contains("No.Such.Class"));
        Assert.Contains(errors, e => e.Contains("boom"));
    }
}
