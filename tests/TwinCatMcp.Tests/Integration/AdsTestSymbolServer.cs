using System.Text;
using TwinCAT.Ads;
using TwinCAT.Ads.Server;
using TwinCAT.Ads.Server.TypeSystem;
using TwinCAT.Ads.TypeSystem;
using TwinCAT.TypeSystem;

namespace TwinCatMcp.Tests.Integration;

/// <summary>
/// In-process symbolic ADS server registered at the local AMS router on a free customer port (25000..25999).
/// Hosts PLC-like test symbols (BOOL, INT, LREAL, STRING, WSTRING, TIME, struct, arrays, enum, RPC function block).
/// Values live in a byte process image, so reads/writes are exactly the raw ADS images.
/// </summary>
public sealed class AdsTestSymbolServer : AdsSymbolicServer
{
    public const uint IndexGroup = 0x4020;
    private const uint BaseOffset = 0x1000;
    private readonly byte[] _image = new byte[0x2000];
    private readonly HashSet<string> _changed = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    private AdsTestSymbolServer(ushort port) : base(port, "McpTestServer")
    {
        serverVersion = new Version(1, 2, 3);
    }

    public AdsState State { get; private set; } = AdsState.Run;

    public ushort Port => ServerPort;

    /// <summary>Starts a server on the first free customer port.</summary>
    public static AdsTestSymbolServer Start()
    {
        var rnd = new Random();
        Exception? last = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var port = (ushort)rnd.Next(25100, 25999);
            var server = new AdsTestSymbolServer(port);
            try
            {
                server.ConnectServer();
                if (server.IsConnected)
                {
                    return server;
                }
            }
            catch (Exception ex)
            {
                last = ex;
            }

            server.Dispose();
        }

        throw new InvalidOperationException("Could not register the ADS test server at the local AMS router.", last);
    }

    protected override void OnConnected()
    {
        var dtBool = new PrimitiveType("BOOL", typeof(bool));
        var dtInt = new PrimitiveType("INT", typeof(short));
        var dtDInt = new PrimitiveType("DINT", typeof(int));
        var dtLReal = new PrimitiveType("LREAL", typeof(double));
        var dtTime = new PrimitiveType("TIME", typeof(uint));
        var dtString40 = new StringType(40, Encoding.Latin1);
        var dtString20 = new StringType(20, Encoding.Latin1);
        var dtWString = new WStringType(20);
        var dtData = new StructType("ST_Data", null)
            .AddAligned(new Member("bOk", dtBool))
            .AddAligned(new Member("nCount", dtInt))
            .AddAligned(new Member("fTemp", dtLReal))
            .AddAligned(new Member("sName", dtString20));
        var dtValues = new ArrayType(dtInt, new DimensionCollection().AddDimension(new Dimension(1, 5)));
        var dtMatrix = new ArrayType(dtDInt, new DimensionCollection().AddDimension(new Dimension(0, 2)).AddDimension(new Dimension(0, 3)));
        var dtColor = new EnumType<short>("E_Color", dtInt, new EnumValueCollection<short>().AddValue("Red", 1).AddValue("Green", 2).AddValue("Blue", 3));
        var add = new RpcMethod("Add")
            .AddParameter("a", dtInt, MethodParamFlags.In)
            .AddParameter("b", dtInt, MethodParamFlags.In)
            .SetReturnType(dtInt);
        var dtRpc = new StructType("FB_Calc").AddAligned(new Member("nCalls", dtInt)).AddMethod(add);

        symbolFactory.AddType(dtBool).AddType(dtInt).AddType(dtDInt).AddType(dtLReal).AddType(dtTime)
            .AddType(dtString40).AddType(dtString20).AddType(dtWString).AddType(dtData).AddType(dtValues)
            .AddType(dtMatrix).AddType(dtColor).AddType(dtRpc);

        var area = new DataArea("PlcImage", IndexGroup, BaseOffset, (uint)_image.Length);
        symbolFactory.AddDataArea(area);
        symbolFactory
            .AddSymbol("MAIN.bFlag", dtBool, area)
            .AddSymbol("MAIN.nCounter", dtInt, area)
            .AddSymbol("MAIN.fValue", dtLReal, area)
            .AddSymbol("MAIN.sText", dtString40, area)
            .AddSymbol("MAIN.tDelay", dtTime, area)
            .AddSymbol("MAIN.stData", dtData, area)
            .AddSymbol("MAIN.aValues", dtValues, area)
            .AddSymbol("MAIN.aMatrix", dtMatrix, area)
            .AddSymbol("MAIN.eColor", dtColor, area)
            .AddSymbol("MAIN.fbCalc", dtRpc, area)
            .AddSymbol("GVL.wsText", dtWString, area)
            .AddSymbol("GVL.nSetpoint", dtDInt, area);

        base.OnConnected();

        // Initial values
        SetRaw("MAIN.bFlag", [1]);
        SetRaw("MAIN.nCounter", BitConverter.GetBytes((short)10));
        SetRaw("MAIN.fValue", BitConverter.GetBytes(1.25));
        SetRaw("MAIN.sText", Encoding.Latin1.GetBytes("Hello PLC"));
        SetRaw("MAIN.tDelay", BitConverter.GetBytes(1500u));
        SetRaw("MAIN.eColor", BitConverter.GetBytes((short)2));
        SetRaw("GVL.wsText", Encoding.Unicode.GetBytes("Grüße"));
        SetRaw("GVL.nSetpoint", BitConverter.GetBytes(-5));
        var values = new byte[10];
        for (short i = 0; i < 5; i++)
        {
            BitConverter.GetBytes((short)((i + 1) * 10)).CopyTo(values, i * 2);
        }

        SetRaw("MAIN.aValues", values);
        var matrix = new byte[24];
        for (var i = 0; i < 6; i++)
        {
            BitConverter.GetBytes(i + 1).CopyTo(matrix, i * 4);
        }

        SetRaw("MAIN.aMatrix", matrix);
        var data = Symbols["MAIN.stData"];
        var dataType = (IStructType)data.DataType!;
        var raw = new byte[data.ByteSize];
        raw[dataType.Members["bOk"].ByteOffset] = 1;
        BitConverter.GetBytes((short)3).CopyTo(raw, dataType.Members["nCount"].ByteOffset);
        BitConverter.GetBytes(21.5).CopyTo(raw, dataType.Members["fTemp"].ByteOffset);
        Encoding.Latin1.GetBytes("Sensor").CopyTo(raw, dataType.Members["sName"].ByteOffset);
        SetRaw("MAIN.stData", raw);
    }

    /// <summary>Process image address of a root symbol (index group / offset).</summary>
    public (uint IndexGroup, uint IndexOffset, int Size) AddressOf(string path)
    {
        var s = Symbols[path];
        var a = (IProcessImageAddress)s;
        return (a.IndexGroup, a.IndexOffset, s.ByteSize);
    }

    /// <summary>Overwrites the value of a root symbol (zero padded) and fires change notifications.</summary>
    public void SetRaw(string path, byte[] bytes)
    {
        var (_, io, size) = AddressOf(path);
        lock (_lock)
        {
            var slot = _image.AsSpan((int)(io - BaseOffset), size);
            slot.Clear();
            bytes.AsSpan(0, Math.Min(bytes.Length, size)).CopyTo(slot);
            _changed.Add(path);
        }

        _ = Task.Run(() => FireNotifications(DateTime.Now.Ticks));
    }

    public byte[] GetRaw(string path)
    {
        var (_, io, size) = AddressOf(path);
        lock (_lock)
        {
            return _image.AsSpan((int)(io - BaseOffset), size).ToArray();
        }
    }

    private Span<byte> Slot(ISymbol symbol)
    {
        var a = (IProcessImageAddress)symbol;
        return _image.AsSpan((int)(a.IndexOffset - BaseOffset), symbol.ByteSize);
    }

    protected override AdsErrorCode OnReadRawValue(ISymbol symbol, Span<byte> span)
    {
        lock (_lock)
        {
            Slot(symbol).CopyTo(span);
        }

        return AdsErrorCode.NoError;
    }

    protected override AdsErrorCode OnWriteRawValue(ISymbol symbol, ReadOnlySpan<byte> span)
    {
        lock (_lock)
        {
            span.CopyTo(Slot(symbol));
            _changed.Add(symbol.InstancePath);
        }

        _ = Task.Run(() => FireNotifications(DateTime.Now.Ticks));
        return AdsErrorCode.NoError;
    }

    protected override bool HasValueChanged(ISymbol symbol)
    {
        lock (_lock)
        {
            // A change of a root symbol also changes its members and vice versa.
            var changed = _changed.Any(c => c.Equals(symbol.InstancePath, StringComparison.OrdinalIgnoreCase) ||
                                            symbol.InstancePath.StartsWith(c + ".", StringComparison.OrdinalIgnoreCase) ||
                                            symbol.InstancePath.StartsWith(c + "[", StringComparison.OrdinalIgnoreCase) ||
                                            c.StartsWith(symbol.InstancePath, StringComparison.OrdinalIgnoreCase));
            _changed.Clear();
            return changed;
        }
    }

    protected override AdsErrorCode OnSetValue(ISymbol symbol, object value, out bool valueChanged)
    {
        valueChanged = false;
        return AdsErrorCode.DeviceServiceNotSupported;
    }

    protected override AdsErrorCode OnGetValue(ISymbol symbol, out object? value)
    {
        value = null;
        return AdsErrorCode.DeviceServiceNotSupported;
    }

    protected override AdsErrorCode OnRpcInvoke(IInterfaceInstance structInstance, IRpcMethod method, object[] values, out object? returnValue)
    {
        if (method.Name == "Add")
        {
            returnValue = (short)((short)values[0] + (short)values[1]);
            return AdsErrorCode.NoError;
        }

        returnValue = null;
        return AdsErrorCode.DeviceServiceNotSupported;
    }

    protected override Task<ResultReadDeviceState> OnReadDeviceStateAsync(AmsAddress sender, uint invokeId, CancellationToken cancel)
        => Task.FromResult(ResultReadDeviceState.CreateSuccess(new StateInfo(State, (ushort)0)));

    protected override Task<ResultAds> OnWriteControlAsync(AmsAddress sender, uint invokeId, AdsState adsState, ushort deviceState, ReadOnlyMemory<byte> data, CancellationToken cancel)
    {
        State = adsState;
        return Task.FromResult(ResultAds.CreateSuccess());
    }
}
