using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using EmulatorCore.Interfaces;
using EmulatorCore.Logging;
using EmulatorCore.Models;
using System.Linq;

namespace S7Server;

/// <summary>
/// Custom ISO-on-TCP S7 protocol server compatible with the Sharp7 client library.
/// Implements TPKT (RFC 1006), COTP (CR/CC/DT), and S7 PDU handling for
/// CommSetup, ReadArea, and WriteArea operations.
/// </summary>
// SAFETY COUPLING: this server intentionally does NOT implement the S7 userdata/SZL PDU behind
// GetCpuInfo (no CPU serial). Clients may rely on the emulator failing GetCpuInfo to tell it
// apart from a real S7-200. Do not add SZL CPU-info without reviewing those clients.
public sealed class S7ProtocolServer : IS7ProtocolServer
{
    private const byte TpktVersion = 0x03;
    private const byte CotpCR = 0xE0;
    private const byte CotpCC = 0xD0;
    private const byte CotpDT = 0xF0;
    private const byte S7ProtocolId = 0x32;
    private const byte S7MsgTypeJobRequest = 0x01;
    private const byte S7MsgTypeAckData = 0x03;
    private const byte S7FuncCommSetup = 0xF0;
    private const byte S7FuncReadVar = 0x04;
    private const byte S7FuncWriteVar = 0x05;
    private const byte S7AreaDB = 0x84;
    private const byte S7AreaMK = 0x83;
    private const byte S7AreaPA = 0x82; // Process outputs (AB) — Delphi reads valve status here
    private const byte S7AreaPE = 0x81; // Process inputs (EB) — Delphi reads gate valve status here
    private const byte ReturnCodeSuccess = 0xFF;
    private const byte TransportSizeByte = 0x04;
    private const int TpktHeaderLen = 4;
    private const int CotpDtHeaderLen = 3; // 02 F0 80

    private readonly string _bindAddress;
    private readonly int _port;
    private readonly string _name;
    private readonly string _plcId;

    private readonly List<DbRegion> _dbRegions = [];
    private readonly Lock _dbLock = new();
    private byte[] _dbBuffer = new byte[4096];

    private readonly List<MbBitRegistration> _mbBits = [];
    private readonly List<EbBitRegistration> _ebBits = [];
    private readonly Lock _mbLock = new();
    private byte[] _mbBuffer = new byte[256];
    private byte[] _ebBuffer = new byte[32];

    // Built under the working-buffer locks by Flush()/WriteAreaData(); read by
    // client handlers with NO lock. Assigning the reference publishes a fully
    // built, immutable array, so a reader sees either the old or the new buffer,
    // never a torn one. A published snapshot reflects state as of its publish;
    // OnWrite/OnRead callbacks running under the lock before the next publish
    // must not assume their own write is already visible to readers.
    private volatile byte[]? _dbSnapshot;
    private volatile byte[]? _mbSnapshot;
    private volatile byte[]? _ebSnapshot;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private readonly List<Task> _clientTasks = [];
    private bool _disposed;

    public string Name => _name;
    public string PlcId => _plcId;
    public bool IsRunning => _listener != null && _cts != null && !_cts.IsCancellationRequested;
    public int ActiveClientCount
    {
        get { lock (_clientTasks) { return _clientTasks.Count(t => !t.IsCompleted); } }
    }

    public S7ProtocolServer(string bindAddress = "127.0.0.1", int port = 102, string name = "S7Server", string plcId = "")
    {
        _bindAddress = bindAddress;
        _port = port;
        _name = name;
        _plcId = plcId ?? "";
    }

    public void RegisterDbRegion(DbRegion region)
    {
        lock (_dbLock)
        {
            _dbRegions.Add(region);
            EnsureDbBufferSize(region.Address + region.Length);
        }
    }

    public void RegisterMbBit(MbBitRegistration bit)
    {
        lock (_mbLock)
        {
            _mbBits.Add(bit);
            EnsureMbBufferSize(bit.Address + 1);
        }
    }

    public void RegisterEbBit(EbBitRegistration bit)
    {
        lock (_mbLock)
            _ebBits.Add(bit);
    }

    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(S7ProtocolServer));
        if (_listener != null) return;

        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Parse(_bindAddress), _port);
        _listener.Start();
        _acceptTask = AcceptClientsAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
        try { _acceptTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _listener = null;
        _cts?.Dispose();
        _cts = null;
    }

    /// <summary>
    /// Pushes all registered device states into the internal buffers so that
    /// subsequent S7 reads return current values.
    /// </summary>
    public void Flush()
    {
        lock (_dbLock)
        {
            foreach (var region in _dbRegions)
            {
                // Input/command regions are write-through: the controller owns
                // their bytes. Flushing an OnRead echo back over them would
                // clobber a controller's in-progress byte-by-byte command write.
                if (region.SkipFlush) continue;
                var data = region.OnRead();
                if (data.Length > 0)
                {
                    EnsureDbBufferSize(region.Address + data.Length);
                    Buffer.BlockCopy(data, 0, _dbBuffer, region.Address, data.Length);
                }
            }
            _dbSnapshot = (byte[])_dbBuffer.Clone();
        }

        lock (_mbLock)
        {
            Array.Clear(_mbBuffer, 0, _mbBuffer.Length);
            foreach (var bit in _mbBits)
            {
                EnsureMbBufferSize(bit.Address + 1);
                if (bit.OnRead())
                    _mbBuffer[bit.Address] |= (byte)(1 << bit.BitIndex);
            }

            // Build EB buffer: start with MB as base (most inputs mirror outputs),
            // then overlay explicit EB bit registrations.
            EnsureEbBufferSize(_mbBuffer.Length);
            Buffer.BlockCopy(_mbBuffer, 0, _ebBuffer, 0, Math.Min(_mbBuffer.Length, _ebBuffer.Length));
            foreach (var bit in _ebBits)
            {
                EnsureEbBufferSize(bit.Address + 1);
                if (bit.OnRead())
                    _ebBuffer[bit.Address] |= (byte)(1 << bit.BitIndex);
                else
                    _ebBuffer[bit.Address] &= (byte)~(1 << bit.BitIndex);
            }
            _mbSnapshot = (byte[])_mbBuffer.Clone();
            _ebSnapshot = (byte[])_ebBuffer.Clone();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    internal void WriteMbForTest(int address, byte[] data)
    {
        lock (_mbLock)
        {
            EnsureMbBufferSize(address + data.Length);
            Buffer.BlockCopy(data, 0, _mbBuffer, address, data.Length);
            DispatchMbWriteCallbacks(address, data.Length);
            _mbSnapshot = (byte[])_mbBuffer.Clone();
        }
    }

    private async Task AcceptClientsAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await _listener!.AcceptTcpClientAsync(ct);
                client.NoDelay = true;
                var task = HandleClientAsync(client, ct);
                lock (_clientTasks)
                {
                    _clientTasks.Add(task);
                    _clientTasks.RemoveAll(t => t.IsCompleted);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using var _ = client;
        EmulatorLog.Log(LogLevel.Info, "S7Server", $"Client connected from {client.Client.RemoteEndPoint}");
        var stream = client.GetStream();
        var buf = new byte[2048];

        try
        {
            while (!ct.IsCancellationRequested && client.Connected)
            {
                if (!await ReadExactAsync(stream, buf, 0, TpktHeaderLen, ct))
                    break;

                if (buf[0] != TpktVersion)
                    break;

                int totalLen = BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(2));
                if (totalLen < TpktHeaderLen || totalLen > buf.Length)
                    break;

                int remaining = totalLen - TpktHeaderLen;
                if (remaining > 0 && !await ReadExactAsync(stream, buf, TpktHeaderLen, remaining, ct))
                    break;

                // Offset 4 = COTP header length (for CR it's 0x11 = 17)
                // Offset 5 = COTP PDU type
                byte cotpPduType = (byte)(buf[5] >> 4); // high nibble is PDU type code

                if (cotpPduType == 0x0E) // 0xE0 >> 4 = 0x0E → CR
                {
                    await HandleCotpCR(stream, buf, totalLen, ct);
                }
                else if (buf[4] == 0x02 && buf[5] == CotpDT && buf[6] == 0x80)
                {
                    await HandleCotpDT(stream, buf, totalLen, ct);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (SocketException) { }
        finally
        {
            EmulatorLog.Log(LogLevel.Info, "S7Server", "Client disconnected");
        }
    }

    private async Task HandleCotpCR(NetworkStream stream, byte[] request, int totalLen, CancellationToken ct)
    {
        // Sharp7 sends 22-byte ISO_CR:
        // 03 00 00 16 | 11 E0 00 00 00 01 00 C0 01 0A C1 02 XX XX C2 02 XX XX
        //  TPKT hdr   | COTP CR: len=17, PDU=0xE0, dst_ref=0, src_ref=1, class=0
        //               C0=TPDU-size, C1=calling-TSAP, C2=called-TSAP

        // Build CC (Connection Confirm) response - same 22 bytes
        // 03 00 00 16 | 11 D0 00 01 00 01 00 C0 01 0A C1 02 XX XX C2 02 XX XX
        var cc = new byte[22];
        cc[0] = TpktVersion;
        cc[1] = 0x00;               // TPKT reserved
        BinaryPrimitives.WriteUInt16BigEndian(cc.AsSpan(2), 22); // TPKT length
        cc[4] = 0x11;               // COTP header length (17 bytes follow)
        cc[5] = CotpCC;
        cc[6] = 0x00;               // dst-ref high (echo back src-ref from CR)
        cc[7] = 0x01;               // dst-ref low
        cc[8] = 0x00;               // src-ref high
        cc[9] = 0x01;               // src-ref low
        cc[10] = 0x00;              // class / option
        cc[11] = 0xC0; cc[12] = 0x01; cc[13] = 0x0A; // TPDU size = 1024
        cc[14] = 0xC1; cc[15] = 0x02;                 // calling TSAP
        if (totalLen >= 18)
        {
            cc[16] = request[16]; cc[17] = request[17];
        }
        cc[18] = 0xC2; cc[19] = 0x02;                 // called TSAP
        if (totalLen >= 22)
        {
            cc[20] = request[20]; cc[21] = request[21];
        }

        await stream.WriteAsync(cc, ct);
        await stream.FlushAsync(ct);
    }

    private async Task HandleCotpDT(NetworkStream stream, byte[] buf, int totalLen, CancellationToken ct)
    {
        // COTP DT header is 3 bytes (02 F0 80), starts at offset 4
        int s7Start = 7;
        if (totalLen < s7Start + 10) return; // too short for S7 header

        byte protocolId = buf[s7Start];
        if (protocolId != S7ProtocolId) return;

        byte msgType = buf[s7Start + 1];
        ushort pduRef = BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(s7Start + 4));
        ushort paramLen = BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(s7Start + 6));
        ushort dataLen = BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(s7Start + 8));

        if (msgType != S7MsgTypeJobRequest) return;

        int paramStart = s7Start + 10;
        if (paramStart >= totalLen) return;

        byte function = buf[paramStart];

        switch (function)
        {
            case S7FuncCommSetup:
                await HandleCommSetup(stream, buf, s7Start, pduRef, ct);
                break;
            case S7FuncReadVar:
                await HandleReadVar(stream, buf, totalLen, s7Start, paramStart, pduRef, paramLen, ct);
                break;
            case S7FuncWriteVar:
                await HandleWriteVar(stream, buf, totalLen, s7Start, paramStart, pduRef, paramLen, dataLen, ct);
                break;
        }
    }

    private async Task HandleCommSetup(NetworkStream stream, byte[] request, int s7Start, ushort pduRef, CancellationToken ct)
    {
        // Sharp7 S7_PN request (from offset 7):
        // 32 01 00 00 04 00 00 08 00 00 F0 00 00 01 00 01 00 F0
        //                                                    ^^  PDU size at s7Start+18..19

        ushort requestedPdu = 480;
        if (s7Start + 18 + 1 < request.Length)
        {
            requestedPdu = BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(s7Start + 18));
        }
        ushort negotiatedPdu = Math.Min(requestedPdu, (ushort)960);
        if (negotiatedPdu < 240) negotiatedPdu = 240;

        var resp = new byte[27];
        resp[0] = TpktVersion;
        resp[1] = 0x00;
        BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(2), 27);
        resp[4] = 0x02;
        resp[5] = CotpDT;
        resp[6] = 0x80;
        resp[7] = S7ProtocolId;
        resp[8] = S7MsgTypeAckData;
        resp[9] = 0x00;
        resp[10] = 0x00;
        BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(11), pduRef);
        BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(13), 8);
        BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(15), 0);
        resp[17] = S7FuncCommSetup;
        resp[18] = 0x00;
        resp[19] = 0x00;
        resp[20] = 0x01;
        resp[21] = 0x00;
        resp[22] = 0x01;
        BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(23), negotiatedPdu);
        // Sharp7 NegotiatePduLength requires Length == 27, PDU[17] == 0 && PDU[18] == 0 and reads
        // the PDU size at PDU[25..26]. The AckData header is 12 bytes, so [17]/[18] are its
        // error class/code and the params start at 19:
        //   32 03 00 00 [pduRef:2] [paramLen:2] [dataLen:2] [errClass] [errCode]
        resp[7] = S7ProtocolId;
        resp[8] = S7MsgTypeAckData;
        resp[9] = 0x00;                 // reserved
        resp[10] = 0x00;                // reserved
        BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(11), pduRef);
        BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(13), 8);     // param length = 8
        BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(15), 0);     // data length = 0
        resp[17] = 0x00;                // error class = 0 (no error)
        resp[18] = 0x00;                // error code = 0 (no error)
        // S7 params start at offset 19 (8 bytes)
        resp[19] = S7FuncCommSetup;
        resp[20] = 0x00;                // reserved
        resp[21] = 0x00;                // max AMQ calling high
        resp[22] = 0x01;                // max AMQ calling low
        resp[23] = 0x00;                // max AMQ called high
        resp[24] = 0x01;                // max AMQ called low
        BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(25), negotiatedPdu);

        await stream.WriteAsync(resp, ct);
        await stream.FlushAsync(ct);
    }

    private async Task HandleReadVar(NetworkStream stream, byte[] buf, int totalLen,
        int s7Start, int paramStart, ushort pduRef, ushort paramLen, CancellationToken ct)
    {
        if (paramStart + 1 >= totalLen) return;
        byte itemCount = buf[paramStart + 1];
        EmulatorLog.Log(LogLevel.Debug, "S7Server", $"ReadVar: {itemCount} item(s), paramLen={paramLen}");

        var items = new List<ReadItem>();
        int offset = paramStart + 2;
        for (int i = 0; i < itemCount; i++)
        {
            if (offset + 12 > totalLen) return;

            // Item structure (12 bytes):
            // [0]=0x12 spec_type, [1]=0x0A length, [2]=0x10 syntax_id,
            // [3]=transport_size, [4..5]=count, [6..7]=DB number,
            // [8]=area, [9..11]=address (3 bytes, bit-level)
            byte transportSize = buf[offset + 3];
            ushort count = BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(offset + 4));
            ushort dbNumber = BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(offset + 6));
            byte area = buf[offset + 8];

            // Address is 3 bytes, bit-level addressing
            int addr24 = (buf[offset + 9] << 16) | (buf[offset + 10] << 8) | buf[offset + 11];
            int byteAddr = addr24 >> 3;

            int byteLen = TransportSizeToByteLength(transportSize, count);

            items.Add(new ReadItem(area, dbNumber, byteAddr, byteLen, transportSize, count));
            offset += 12;
        }

        int totalDataSize = 0;
        foreach (var item in items)
        {
            // Each data item: 4 bytes header (return_code, transport_size, bit_count_hi, bit_count_lo) + data
            int itemDataLen = 4 + item.ByteLength;
            // Pad to even if needed (except last item)
            if (item.ByteLength % 2 != 0 && items.IndexOf(item) < items.Count - 1)
                itemDataLen++;
            totalDataSize += itemDataLen;
        }

        int s7ParamLen = 2; // function(1) + item_count(1)
        int s7DataLen = totalDataSize;
        int s7HeaderLen = 12; // AckData has error class + error code
        int cotpLen = 3;
        int tpktLen = TpktHeaderLen + cotpLen + s7HeaderLen + s7ParamLen + s7DataLen;

        var resp = new byte[tpktLen];

        resp[0] = TpktVersion;
        resp[1] = 0x00;
        BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(2), (ushort)tpktLen);

        resp[4] = 0x02;
        resp[5] = CotpDT;
        resp[6] = 0x80;

        int h = 7;
        resp[h + 0] = S7ProtocolId;
        resp[h + 1] = S7MsgTypeAckData;
        resp[h + 2] = 0x00; // reserved
        resp[h + 3] = 0x00;
        BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(h + 4), pduRef);
        BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(h + 6), (ushort)s7ParamLen);
        BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(h + 8), (ushort)s7DataLen);
        resp[h + 10] = 0x00; // error class
        resp[h + 11] = 0x00; // error code

        int p = h + 12;
        resp[p] = S7FuncReadVar;
        resp[p + 1] = itemCount;

        int d = p + 2;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            byte[] data = ReadAreaData(item.Area, item.DbNumber, item.ByteAddress, item.ByteLength);

            resp[d] = ReturnCodeSuccess;
            resp[d + 1] = TransportSizeByte;
            ushort bitCount = (ushort)(data.Length * 8);
            BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(d + 2), bitCount);
            Buffer.BlockCopy(data, 0, resp, d + 4, data.Length);
            d += 4 + data.Length;

            // Pad to even boundary (except for last item)
            if (data.Length % 2 != 0 && i < items.Count - 1)
            {
                resp[d] = 0x00;
                d++;
            }
        }

        await stream.WriteAsync(resp.AsMemory(0, tpktLen), ct);
        await stream.FlushAsync(ct);
    }

    private async Task HandleWriteVar(NetworkStream stream, byte[] buf, int totalLen,
        int s7Start, int paramStart, ushort pduRef, ushort paramLen, ushort dataLen, CancellationToken ct)
    {
        if (paramStart + 1 >= totalLen) return;
        byte itemCount = buf[paramStart + 1];
        EmulatorLog.Log(LogLevel.Debug, "S7Server", $"WriteArea: {itemCount} item(s), dataLen={dataLen}");

        var items = new List<WriteItem>();
        int offset = paramStart + 2;
        for (int i = 0; i < itemCount; i++)
        {
            if (offset + 12 > totalLen) return;

            byte transportSize = buf[offset + 3];
            ushort count = BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(offset + 4));
            ushort dbNumber = BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(offset + 6));
            byte area = buf[offset + 8];

            int addr24 = (buf[offset + 9] << 16) | (buf[offset + 10] << 8) | buf[offset + 11];
            int byteAddr = addr24 >> 3;
            int byteLen = TransportSizeToByteLength(transportSize, count);

            items.Add(new WriteItem(area, dbNumber, byteAddr, byteLen, transportSize, count));
            offset += 12;
        }

        int dataStart = paramStart + paramLen;

        int dOff = dataStart;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (dOff + 4 > totalLen) break;

            // Data item header: return_code(1), transport_size(1), length(2)
            ushort dataLength = BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(dOff + 2));

            // dataLength is in bits for transport size BYTE(0x04) / BIT(0x03)
            // and in bytes for transport size OCTET(0x09)
            int actualBytes;
            byte transportInData = buf[dOff + 1];
            if (transportInData == 0x09 || transportInData == 0x07) // OCTET string or REAL
                actualBytes = dataLength;
            else
                actualBytes = (dataLength + 7) / 8;

            dOff += 4;

            if (dOff + actualBytes > totalLen) break;

            var writeData = new byte[actualBytes];
            Buffer.BlockCopy(buf, dOff, writeData, 0, actualBytes);
            WriteAreaData(item.Area, item.DbNumber, item.ByteAddress, writeData);

            dOff += actualBytes;

            // Pad to even boundary (except for last item)
            if (actualBytes % 2 != 0 && i < items.Count - 1)
                dOff++;
        }

        // TPKT(4) + COTP(3) + S7_hdr(12) + params(2) + data(1 per item)
        int respDataLen = itemCount;
        int respTotalLen = TpktHeaderLen + CotpDtHeaderLen + 12 + 2 + respDataLen;

        var resp = new byte[respTotalLen];
        resp[0] = TpktVersion;
        resp[1] = 0x00;
        BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(2), (ushort)respTotalLen);

        resp[4] = 0x02;
        resp[5] = CotpDT;
        resp[6] = 0x80;

        int h = 7;
        resp[h + 0] = S7ProtocolId;
        resp[h + 1] = S7MsgTypeAckData;
        resp[h + 2] = 0x00;
        resp[h + 3] = 0x00;
        BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(h + 4), pduRef);
        BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(h + 6), 2);               // param len
        BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(h + 8), (ushort)respDataLen); // data len
        resp[h + 10] = 0x00; // error class
        resp[h + 11] = 0x00; // error code

        int p = h + 12;
        resp[p] = S7FuncWriteVar;
        resp[p + 1] = itemCount;

        int d = p + 2;
        for (int i = 0; i < itemCount; i++)
        {
            resp[d + i] = ReturnCodeSuccess;
        }

        await stream.WriteAsync(resp.AsMemory(0, respTotalLen), ct);
        await stream.FlushAsync(ct);
    }

    private byte[] ReadAreaData(byte area, int dbNumber, int byteAddr, int byteLen)
    {
        var result = new byte[byteLen];

        if (area == S7AreaDB)
        {
            var snap = _dbSnapshot; // volatile read of the published reference
            if (snap != null && byteAddr + byteLen <= snap.Length)
                Buffer.BlockCopy(snap, byteAddr, result, 0, byteLen);
        }
        else if (area is S7AreaMK or S7AreaPA)
        {
            // PA (process outputs) mirrors the MB snapshot.
            var snap = _mbSnapshot;
            if (snap != null && byteAddr + byteLen <= snap.Length)
                Buffer.BlockCopy(snap, byteAddr, result, 0, byteLen);
            EmulatorLog.Log(LogLevel.Debug, "S7Server",
                $"{AreaName(area)} Read addr={byteAddr} len={byteLen} data=[{FormatBytes(result)}]");
        }
        else if (area == S7AreaPE)
        {
            // PE (process inputs / EB) reads its own snapshot.
            var snap = _ebSnapshot;
            if (snap != null && byteAddr + byteLen <= snap.Length)
                Buffer.BlockCopy(snap, byteAddr, result, 0, byteLen);
            EmulatorLog.Log(LogLevel.Debug, "S7Server",
                $"{AreaName(area)} Read addr={byteAddr} len={byteLen} data=[{FormatBytes(result)}]");
        }

        return result;
    }

    private void WriteAreaData(byte area, int dbNumber, int byteAddr, byte[] data)
    {
        if (area == S7AreaDB)
        {
            lock (_dbLock)
            {
                EnsureDbBufferSize(byteAddr + data.Length);
                Buffer.BlockCopy(data, 0, _dbBuffer, byteAddr, data.Length);
                DispatchDbWriteCallbacks(byteAddr, data.Length);
                _dbSnapshot = (byte[])_dbBuffer.Clone();
            }
        }
        else if (area is S7AreaMK or S7AreaPA)
        {
            EmulatorLog.Log(LogLevel.Debug, "S7Server",
                $"{AreaName(area)} Write addr={byteAddr} len={data.Length} data=[{FormatBytes(data)}]");
            lock (_mbLock)
            {
                EnsureMbBufferSize(byteAddr + data.Length);
                Buffer.BlockCopy(data, 0, _mbBuffer, byteAddr, data.Length);
                DispatchMbWriteCallbacks(byteAddr, data.Length);
                // EB read snapshot mirrors MB; it is refreshed on the next Flush(), not here.
                _mbSnapshot = (byte[])_mbBuffer.Clone();
            }
        }
        else if (area == S7AreaPE)
        {
            // PE (process inputs / EB) writes go to the EB buffer, not MB.
            // This allows Control to toggle input-only devices like GVM.
            EmulatorLog.Log(LogLevel.Debug, "S7Server",
                $"{AreaName(area)} Write addr={byteAddr} len={data.Length} data=[{FormatBytes(data)}]");
            lock (_mbLock)
            {
                EnsureEbBufferSize(byteAddr + data.Length);
                Buffer.BlockCopy(data, 0, _ebBuffer, byteAddr, data.Length);
                DispatchEbWriteCallbacks(byteAddr, data.Length);
                _ebSnapshot = (byte[])_ebBuffer.Clone();
            }
        }
    }

    private void DispatchDbWriteCallbacks(int startAddr, int length)
    {
        // Must be called under _dbLock
        foreach (var region in _dbRegions)
        {
            int regionEnd = region.Address + region.Length;
            int writeEnd = startAddr + length;

            if (startAddr < regionEnd && writeEnd > region.Address)
            {
                var regionData = new byte[region.Length];
                Buffer.BlockCopy(_dbBuffer, region.Address, regionData, 0, region.Length);
                region.OnWrite(regionData);
            }
        }
    }

    private void DispatchMbWriteCallbacks(int startAddr, int length)
    {
        // Must be called under _mbLock
        foreach (var bit in _mbBits)
        {
            if (bit.Address >= startAddr && bit.Address < startAddr + length)
            {
                bool value = (_mbBuffer[bit.Address] & (1 << bit.BitIndex)) != 0;
                bit.OnWrite(value);
            }
        }
    }

    private void DispatchEbWriteCallbacks(int startAddr, int length)
    {
        // Must be called under _mbLock
        foreach (var bit in _ebBits)
        {
            if (bit.OnWrite != null && bit.Address >= startAddr && bit.Address < startAddr + length)
            {
                bool value = (_ebBuffer[bit.Address] & (1 << bit.BitIndex)) != 0;
                bit.OnWrite(value);
            }
        }
    }

    private void EnsureDbBufferSize(int required)
    {
        if (required > _dbBuffer.Length)
        {
            int newSize = Math.Max(required, _dbBuffer.Length * 2);
            var newBuf = new byte[newSize];
            Buffer.BlockCopy(_dbBuffer, 0, newBuf, 0, _dbBuffer.Length);
            _dbBuffer = newBuf;
        }
    }

    private void EnsureMbBufferSize(int required)
    {
        if (required > _mbBuffer.Length)
        {
            int newSize = Math.Max(required, _mbBuffer.Length * 2);
            var newBuf = new byte[newSize];
            Buffer.BlockCopy(_mbBuffer, 0, newBuf, 0, _mbBuffer.Length);
            _mbBuffer = newBuf;
        }
    }

    private void EnsureEbBufferSize(int required)
    {
        if (required > _ebBuffer.Length)
        {
            var newBuf = new byte[Math.Max(required, _ebBuffer.Length * 2)];
            Buffer.BlockCopy(_ebBuffer, 0, newBuf, 0, _ebBuffer.Length);
            _ebBuffer = newBuf;
        }
    }

    private static int TransportSizeToByteLength(byte transportSize, int count)
    {
        return transportSize switch
        {
            0x01 => (count + 7) / 8,   // BIT → round up to bytes
            0x02 => count,              // BYTE
            0x03 => count,              // CHAR
            0x04 => count * 2,          // WORD (2 bytes each)
            0x05 => count * 2,          // INT (2 bytes each)
            0x06 => count * 4,          // DWORD (4 bytes each)
            0x07 => count * 4,          // DINT (4 bytes each)
            0x08 => count * 4,          // REAL (4 bytes each)
            _ => count,
        };
    }

    private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, int offset, int count, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset + totalRead, count - totalRead), ct);
            if (read == 0) return false;
            totalRead += read;
        }
        return true;
    }

    private static string FormatBytes(byte[] data)
        => string.Join(" ", data.Select(b => b.ToString("X2")));

    private static string AreaName(byte area) => area switch
    {
        S7AreaPE => "PE",
        S7AreaPA => "PA",
        S7AreaMK => "MB",
        S7AreaDB => "DB",
        _ => $"0x{area:X2}",
    };

    private readonly record struct ReadItem(byte Area, int DbNumber, int ByteAddress, int ByteLength, byte TransportSize, int Count);
    private readonly record struct WriteItem(byte Area, int DbNumber, int ByteAddress, int ByteLength, byte TransportSize, int Count);
}
