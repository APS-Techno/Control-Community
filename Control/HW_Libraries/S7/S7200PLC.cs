using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;
using Sharp7;
using S7Util = Sharp7.S7;

namespace APS.Hardware.S7;

public enum Mode
{
    DB = 0,
    EB = 1,
    AB = 2,
    MB = 3
}

public class S7200PLC : IPLCController, ISelfTestable
{
    private readonly object _lock = new();
    private readonly S7Client client = new();
    public bool Connected { get; private set; }

    private readonly string defaultAddress = "192.168.127.208";

    #region [Connect]
    public void Connect(string Address)
    {
        int res;
        ushort RemoteTSAP = 0x0200;
        ushort LocalTSAP = 0x0200;

        client.SetConnectionType(S7Client.CONNTYPE_BASIC);
        client.SetConnectionParams(Address, LocalTSAP, RemoteTSAP);

        res = client.Connect();
        Connected = res == 0;

        if (Connected)
        {
            var cpuInfo = new S7Client.S7CpuInfo();
            int infoRes = client.GetCpuInfo(ref cpuInfo);
            try
            {
                OnConnected(Address, cpuInfo, infoRes);
            }
            catch
            {
                try { client.Disconnect(); } catch { }
                Connected = false;
                throw;
            }
        }
    }

    /// <summary>
    /// Called after a successful connect with the CPU info read from the PLC
    /// (<paramref name="infoResult"/> is non-zero when that read failed). An
    /// exception thrown here closes the connection and propagates out of Connect.
    /// </summary>
    protected virtual void OnConnected(string address, S7Client.S7CpuInfo cpuInfo, int infoResult) { }

    public void Connect()
    {
        Connect(defaultAddress);
    }

    public void Connect(int Address)
    {
        throw new NotImplementedException();
    }

    public void Connect(string Address, int Rack, int Slot)
    {
        throw new NotImplementedException();
    }

    public void Disconnect()
    {
        lock (_lock)
        {
            try { client.Disconnect(); }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning(
                    $"S7200PLC disconnect failed: {ex.Message}");
            }
            Connected = false;
        }
    }
    #endregion

    #region [Diagnostics]

    /// <summary>
    /// Reads the CPU serial (read-only liveness call) under the I/O lock. Seam:
    /// overridable so the probe can be unit-tested without hardware.
    /// </summary>
    protected virtual bool TryReadCpuSerial(out string serial)
    {
        serial = "";
        lock (_lock)
        {
            var info = new S7Client.S7CpuInfo();
            if (client.GetCpuInfo(ref info) != 0) return false;
            serial = info.SerialNumber?.Trim() ?? "";
            return true;
        }
    }

    /// <summary>
    /// Read-only liveness probe: queries CPU info. Alive (with serial identity) on
    /// a clean reply; Unresponsive otherwise. PLC-hosted devices inherit this result.
    /// </summary>
    public DeviceProbeResult Probe()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool ok = TryReadCpuSerial(out string serial);
        sw.Stop();
        return ok
            ? DeviceProbeResult.Good(sw.Elapsed.TotalMilliseconds, identity: serial)
            : DeviceProbeResult.Bad(HealthLevel.Unresponsive, "CPU info query failed",
                sw.Elapsed.TotalMilliseconds);
    }

    #endregion

    #region [Read]

    /// <summary>
    /// Throws <see cref="PlcCommException"/> when the Sharp7 result code is
    /// non-zero, so a comm failure never yields a zero-buffer that looks like a
    /// real reading. Caller must hold <c>_lock</c>.
    /// </summary>
    private void ReadChecked(int mode, int address, int size, byte[] buffer)
    {
        int rc = mode switch
        {
            (int)Mode.DB => client.DBRead(1, address, size, buffer),
            (int)Mode.EB => client.EBRead(address, size, buffer),
            (int)Mode.AB => client.ABRead(address, size, buffer),
            (int)Mode.MB => client.MBRead(address, size, buffer),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode,
                "Unsupported S7 area mode"),
        };
        ThrowOnCommError(rc, $"read mode={mode} addr={address} size={size}");
    }

    internal static void ThrowOnCommError(int rc, string what)
    {
        if (rc != 0)
            throw new PlcCommException(rc, what);
    }

    /// <summary>
    /// Writes <paramref name="buffer"/> to the given area, throwing
    /// <see cref="PlcCommException"/> when the Sharp7 result code is non-zero —
    /// the write-side twin of <see cref="ReadChecked"/>. Caller must hold
    /// <c>_lock</c>.
    /// </summary>
    private void WriteChecked(int mode, int address, int size, byte[] buffer)
    {
        int rc = mode switch
        {
            (int)Mode.DB => client.DBWrite(1, address, size, buffer),
            (int)Mode.EB => client.EBWrite(address, size, buffer),
            (int)Mode.AB => client.ABWrite(address, size, buffer),
            (int)Mode.MB => client.MBWrite(address, size, buffer),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode,
                "Unsupported S7 area mode"),
        };
        ThrowOnCommError(rc, $"write mode={mode} addr={address} size={size}");
    }

    public bool ReadBool(int mode, int address, int byteIndex)
    {
        byte[] buffer = new byte[1];
        lock (_lock)
        {
            ReadChecked(mode, address, 1, buffer);
            return S7Util.GetBitAt(buffer, 0, byteIndex);
        }
    }

    /// <summary>
    /// Bulk-reads multiple bool addresses in a single S7 round-trip via
    /// <c>ReadMultiVars</c>. Dedupes unique (mode, address) byte targets
    /// and decodes the requested bits client-side. One TCP
    /// exchange replaces one per address.
    /// </summary>
    public bool[] ReadBoolsBulk(IReadOnlyList<(int mode, int address, int byteIndex)> requests)
    {
        if (requests == null || requests.Count == 0)
            return [];

        var uniqueTargets = new List<(int mode, int address)>();
        var targetIndex = new Dictionary<(int, int), int>();
        foreach (var r in requests)
        {
            var key = (r.mode, r.address);
            if (!targetIndex.ContainsKey(key))
            {
                targetIndex[key] = uniqueTargets.Count;
                uniqueTargets.Add(key);
            }
        }

        var buffers = new byte[uniqueTargets.Count][];
        var pinned = new System.Runtime.InteropServices.GCHandle[uniqueTargets.Count];
        var items = new S7Client.S7DataItem[uniqueTargets.Count];
        try
        {
            for (int i = 0; i < uniqueTargets.Count; i++)
            {
                buffers[i] = new byte[1];
                pinned[i] = System.Runtime.InteropServices.GCHandle.Alloc(
                    buffers[i], System.Runtime.InteropServices.GCHandleType.Pinned);
                int area;
                int dbNumber = 0;
                switch (uniqueTargets[i].mode)
                {
                    case (int)Mode.DB: area = (int)S7Area.DB; dbNumber = 1; break;
                    case (int)Mode.EB: area = (int)S7Area.PE; break;
                    case (int)Mode.AB: area = (int)S7Area.PA; break;
                    case (int)Mode.MB: area = (int)S7Area.MK; break;
                    default: throw new ArgumentException(
                        $"Unsupported mode {uniqueTargets[i].mode} in ReadBoolsBulk");
                }
                items[i] = new S7Client.S7DataItem
                {
                    Area = area,
                    WordLen = (int)S7WordLength.Byte,
                    DBNumber = dbNumber,
                    // ReadMultiVars does not apply the bit-level shift that ReadArea
                    // applies for byte reads; pre-shift so the wire address is the
                    // 24-bit bit address S7 expects.
                    Start = uniqueTargets[i].address << 3,
                    Amount = 1,
                    pData = pinned[i].AddrOfPinnedObject(),
                };
            }

            int rc;
            lock (_lock)
            {
                rc = client.ReadMultiVars(items, items.Length);
            }
            if (rc != 0)
                throw new System.IO.IOException($"ReadMultiVars failed with code {rc}");

            var result = new bool[requests.Count];
            for (int j = 0; j < requests.Count; j++)
            {
                var key = (requests[j].mode, requests[j].address);
                int idx = targetIndex[key];
                byte b = buffers[idx][0];
                result[j] = (b & (1 << requests[j].byteIndex)) != 0;
            }
            return result;
        }
        finally
        {
            for (int i = 0; i < pinned.Length; i++)
                if (pinned[i].IsAllocated) pinned[i].Free();
        }
    }

    public float ReadFloat(int mode, int address)
    {
        byte[] buffer = new byte[4];
        lock (_lock)
        {
            ReadChecked(mode, address, 4, buffer);
            return S7Util.GetRealAt(buffer, 0);
        }
    }

    public int ReadInt(int mode, int address)
    {
        byte[] bytes = new byte[2];
        lock (_lock)
        {
            ReadChecked(mode, address, 2, bytes);
            return S7Util.GetIntAt(bytes, 0);
        }
    }

    public string ReadString(int mode, int address)
    {
        return "";
    }

    public byte ReadByte(int mode, int address)
    {
        byte[] buffer = new byte[1];
        lock (_lock)
        {
            ReadChecked(mode, address, 1, buffer);
            return buffer[0];
        }
    }
    #endregion

    #region [Write]
    public void WriteBool(int mode, int address, int index, bool value)
    {
        lock (_lock)
        {
            // A byte packs up to 8 outputs. The read must succeed before the write,
            // otherwise the write would zero the other bits; ReadChecked throws on
            // a failed read, so the write is skipped.
            byte[] buffer = new byte[1];
            ReadChecked(mode, address, 1, buffer);
            S7Util.SetBitAt(buffer, 0, index, value);
            WriteChecked(mode, address, 1, buffer);
        }
    }

    public void WriteFloat(int mode, int address, float value)
    {
        lock (_lock)
        {
            byte[] buffer = new byte[4];
            S7Util.SetRealAt(buffer, 0, value);
            WriteChecked(mode, address, 4, buffer);
        }
    }

    public void WriteInt(int mode, int address, short value)
    {
        lock (_lock)
        {
            byte[] buffer = new byte[2];
            S7Util.SetIntAt(buffer, 0, value);
            WriteChecked(mode, address, 2, buffer);
        }
    }

    public void WriteString(int mode, int address, string value)
    {
        throw new NotImplementedException();
    }

    public void WriteByte(int mode, int address, byte value)
    {
        lock (_lock)
        {
            WriteChecked(mode, address, 1, [value]);
        }
    }

    public void WriteBytes(int mode, int address, byte[] data)
    {
        lock (_lock)
        {
            WriteChecked(mode, address, data.Length, data);
        }
    }

    public byte[] ReadBytes(int mode, int address, int count)
    {
        byte[] buffer = new byte[count];
        lock (_lock)
        {
            ReadChecked(mode, address, count, buffer);
        }
        return buffer;
    }
    #endregion
}
