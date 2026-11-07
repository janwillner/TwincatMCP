using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TwinCAT.Ads;
using TwinCAT.TypeSystem;
using TwinCatMcp.Ads;
using TwinCatMcp.Core;

namespace TwinCatMcp.Tools;

/// <summary>Symbolic ADS access to a running PLC (or any symbolic ADS server): browse, read, write, monitor, RPC.</summary>
[McpServerToolType]
public sealed class AdsSymbolTools(AdsSessionManager ads)
{
    private const string NetIdDesc = "AMS Net ID of the target, e.g. 5.12.34.56.1.1, or 'local' (default) for this PC.";
    private const string PortDesc = "AMS port: 851 = PLC runtime 1 (default), 852 = PLC 2 ..., 801 = TwinCAT 2 PLC.";

    [McpServerTool(Name = "ads_browse_symbols", ReadOnly = true, Idempotent = true), Description(
        "Lists the PLC's symbols (variables) via ADS - the way to discover valid names for ads_read_symbols / ads_write_symbols. " +
        "Filter by prefix ('MAIN.'), glob ('*.bStart*') or regex ('re:^GVL\\.n'). depth>0 also lists struct/FB members (and array elements with expandArrays). " +
        "Paged: use offset/limit; 'hasMore' tells if more exist. Example: {filter:'MAIN.', depth:1}.")]
    public Task<CallToolResult> BrowseSymbols(
        [Description("Name filter: prefix (case-insensitive), glob with * and ?, or regex as 're:<pattern>' / '/<pattern>/'. Empty = all.")] string? filter = null,
        [Description("0 = only top-level variables; 1 = plus their members; 2 = members of members ...")] int depth = 0,
        [Description("Also expand array elements when depth > 0 (can be many).")] bool expandArrays = false,
        [Description("Include data type details (members, enum values, dimensions) per symbol.")] bool includeTypes = false,
        [Description("Paging start index.")] int offset = 0,
        [Description("Max entries returned (1..2000).")] int limit = 200,
        [Description("Re-upload the symbol table (after a PLC download / online change).")] bool refresh = false,
        [Description(NetIdDesc)] string? netId = null,
        [Description(PortDesc)] int port = AdsSessionManager.DefaultPlcPort)
        => ads.Run(() =>
        {
            limit = Math.Clamp(limit, 1, 2000);
            var match = SymbolFilter.Create(filter);
            var loader = ads.Symbols(netId, port, refresh);
            var found = new List<ISymbol>();
            void Visit(ISymbol s, int level)
            {
                if (match.IsMatch(s.InstancePath))
                {
                    found.Add(s);
                }

                if (level >= depth || s.Category == DataTypeCategory.Pointer || s.Category == DataTypeCategory.Reference)
                {
                    return;
                }

                if (s.Category == DataTypeCategory.Array && !expandArrays)
                {
                    return;
                }

                if (SymbolExtensions.IsContainerType(s))
                {
                    foreach (var sub in s.SubSymbols)
                    {
                        Visit(sub, level + 1);
                    }
                }
            }

            foreach (var root in loader.Symbols)
            {
                Visit(root, 0);
            }

            var page = found.Skip(Math.Max(0, offset)).Take(limit).Select(s => AdsSymbolAccess.DescribeSymbol(s, includeTypes)).ToList();
            return new
            {
                total = found.Count,
                offset,
                count = page.Count,
                hasMore = offset + page.Count < found.Count,
                symbols = page,
            };
        });

    [McpServerTool(Name = "ads_get_symbol_info", ReadOnly = true, Idempotent = true), Description(
        "Detailed info for one or more symbols: data type, category, size, index group/offset, comment, attributes and the type layout " +
        "(struct members with offsets, enum values, array dimensions, RPC methods). Example: {symbols:['MAIN.stData','GVL.eMode']}.")]
    public Task<CallToolResult> GetSymbolInfo(
        [Description("Full instance paths, e.g. ['MAIN.nCounter', 'MAIN.stData.fTemp', 'MAIN.aValues[2]'].")] string[] symbols,
        [Description(NetIdDesc)] string? netId = null,
        [Description(PortDesc)] int port = AdsSessionManager.DefaultPlcPort)
        => ads.Run(() => new
        {
            symbols = ads.Resolve(netId, port, symbols).Select(s => AdsSymbolAccess.DescribeSymbol(s, withType: true)).ToList(),
        });

    [McpServerTool(Name = "ads_read_symbols", ReadOnly = true, Idempotent = true), Description(
        "Reads current values of PLC symbols in ONE ADS sum command and returns them as typed JSON: structs/FBs as objects, arrays as (nested) arrays, " +
        "enums as {name, value}, STRING/WSTRING as text, TIME/LTIME/DATE/DT/TOD as IEC literals (T#1s500ms, DT#2024-01-15-12:30:00). " +
        "Members and elements can be addressed directly: MAIN.stData.fTemp, MAIN.aValues[3]. Example: {symbols:['MAIN.nCounter','GVL.stConfig']}.")]
    public Task<CallToolResult> ReadSymbols(
        [Description("Full instance paths to read.")] string[] symbols,
        [Description(NetIdDesc)] string? netId = null,
        [Description(PortDesc)] int port = AdsSessionManager.DefaultPlcPort)
        => ads.Run(() =>
        {
            var resolved = ads.Resolve(netId, port, symbols);
            var client = ads.Get(netId, port);
            return new { values = AdsSymbolAccess.ReadValues(client, resolved) };
        });

    [McpServerTool(Name = "ads_write_symbols", Destructive = true), Description(
        "Writes values to PLC symbols (one ADS sum command). The JSON value is converted to the symbol's type: numbers (or '16#FF'), true/false, strings, " +
        "enum names ('Running') or values, TIME as 'T#2s' (or ms), DT as 'DT#2024-01-15-12:30:00'. Structs take an object - only the given members are changed; " +
        "arrays take an array - a shorter array changes only the leading elements; single members/elements by path (MAIN.stData.nCount, MAIN.aValues[4]). " +
        "Example: {values:{'MAIN.bStart':true,'GVL.stCfg':{'fGain':1.5},'MAIN.eMode':'Auto'}}. Changes the running machine - be careful.")]
    public Task<CallToolResult> WriteSymbols(
        [Description("JSON object: symbol path -> value.")] JsonElement values,
        [Description("Read the values back after writing and return them.")] bool readBack = true,
        [Description(NetIdDesc)] string? netId = null,
        [Description(PortDesc)] int port = AdsSessionManager.DefaultPlcPort)
        => ads.Run(() =>
        {
            if (values.ValueKind != JsonValueKind.Object)
            {
                throw new TcException("'values' must be a JSON object mapping symbol paths to values.") { Hint = "Example: {\"MAIN.nSetpoint\": 42, \"MAIN.bEnable\": true}" };
            }

            var pairs = values.EnumerateObject().ToList();
            var resolved = ads.Resolve(netId, port, pairs.Select(p => p.Name));
            var client = ads.Get(netId, port);
            var errors = AdsSymbolAccess.WriteValues(client, resolved, pairs.Select(p => p.Value).ToList());
            var failed = resolved.Select((s, i) => (s, err: errors[i])).Where(x => x.err != AdsErrorCode.NoError).ToList();
            if (failed.Count > 0)
            {
                throw new TcException("Write failed for: " + string.Join(", ", failed.Select(f => $"{f.s.InstancePath} ({f.err})")))
                {
                    Hint = AdsSessionManager.HintFor(failed[0].err) ?? "Check that the symbols are writable and the PLC is running.",
                };
            }

            return new
            {
                written = resolved.Count,
                values = readBack ? AdsSymbolAccess.ReadValues(client, resolved) : null,
            };
        });

    [McpServerTool(Name = "ads_monitor", ReadOnly = true), Description(
        "Samples one or more symbols over time and returns a time series per symbol plus statistics (min, max, number of changes, first/last value). " +
        "mode 'poll' (default) reads all symbols every intervalMs with a sum command; mode 'notification' registers ADS on-change notifications " +
        "(cycle time = intervalMs) and records every change. Use it to observe how values evolve, e.g. after setting a start bit. " +
        "Example: {symbols:['MAIN.fActPos','MAIN.eState'], durationSeconds:5, intervalMs:100}.")]
    public Task<CallToolResult> Monitor(
        [Description("Symbols to observe.")] string[] symbols,
        [Description("How long to observe (0.1 .. 300 s).")] double durationSeconds = 5,
        [Description("Sample interval (poll) or notification cycle time in ms (>= 1).")] int intervalMs = 100,
        [Description("'poll' or 'notification'.")] string mode = "poll",
        [Description("Only record samples whose value differs from the previous one.")] bool onlyChanges = false,
        [Description("Max recorded samples per symbol (statistics still cover all).")] int maxSamples = 500,
        [Description(NetIdDesc)] string? netId = null,
        [Description(PortDesc)] int port = AdsSessionManager.DefaultPlcPort)
        => ads.RunAsync(async () =>
        {
            var duration = TimeSpan.FromSeconds(Math.Clamp(durationSeconds, 0.1, 300));
            intervalMs = Math.Max(1, intervalMs);
            var resolved = ads.Resolve(netId, port, symbols);
            var client = ads.Get(netId, port);
            var series = resolved.Select(s => new Series(s.InstancePath, s.TypeName, Math.Max(1, maxSamples), onlyChanges)).ToList();

            if (mode.Equals("notification", StringComparison.OrdinalIgnoreCase))
            {
                await MonitorNotifications(client, resolved, series, duration, intervalMs).ConfigureAwait(false);
            }
            else if (mode.Equals("poll", StringComparison.OrdinalIgnoreCase))
            {
                var sw = Stopwatch.StartNew();
                while (true)
                {
                    var t = sw.Elapsed.TotalMilliseconds;
                    var raw = AdsSymbolAccess.ReadRaw(client, resolved);
                    for (var i = 0; i < resolved.Count; i++)
                    {
                        if (raw[i].Data is { } d)
                        {
                            series[i].Add(t, AdsSymbolAccess.Decode(resolved[i], d));
                        }
                        else
                        {
                            series[i].Errors++;
                        }
                    }

                    var remaining = duration - sw.Elapsed;
                    if (remaining <= TimeSpan.Zero)
                    {
                        break;
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(intervalMs, remaining.TotalMilliseconds))).ConfigureAwait(false);
                }
            }
            else
            {
                throw new TcException($"Unknown mode '{mode}'.") { Hint = "Use 'poll' or 'notification'." };
            }

            return new { mode = mode.ToLowerInvariant(), durationSeconds = duration.TotalSeconds, series = series.Select(s => s.ToResult()).ToList() };
        });

    private static async Task MonitorNotifications(AdsClient client, IReadOnlyList<ISymbol> symbols, List<Series> series, TimeSpan duration, int intervalMs)
    {
        var queue = new ConcurrentQueue<(uint Handle, double T, byte[] Data)>();
        var handles = new Dictionary<uint, int>();
        var sw = Stopwatch.StartNew();
        void OnNotification(object? sender, AdsNotificationEventArgs e) => queue.Enqueue((e.Handle, sw.Elapsed.TotalMilliseconds, e.Data.ToArray()));

        client.AdsNotification += OnNotification;
        try
        {
            var settings = new NotificationSettings(AdsTransMode.OnChange, intervalMs, 0);
            for (var i = 0; i < symbols.Count; i++)
            {
                var h = client.AddDeviceNotification(symbols[i].InstancePath, symbols[i].ByteSize, settings, i);
                handles[h] = i;
            }

            await Task.Delay(duration).ConfigureAwait(false);
        }
        finally
        {
            foreach (var h in handles.Keys)
            {
                try
                {
                    client.TryDeleteDeviceNotification(h);
                }
                catch (Exception)
                {
                    // best effort
                }
            }

            client.AdsNotification -= OnNotification;
        }

        while (queue.TryDequeue(out var n))
        {
            if (handles.TryGetValue(n.Handle, out var idx))
            {
                series[idx].Add(n.T, AdsSymbolAccess.Decode(symbols[idx], n.Data));
            }
        }
    }

    private sealed class Series(string symbol, string type, int maxSamples, bool onlyChanges)
    {
        private readonly List<object> _samples = [];
        private JsonNode? _first;
        private JsonNode? _last;
        private string? _lastJson;
        private double? _min;
        private double? _max;
        private int _count;
        private int _changes;

        public int Errors { get; set; }

        public void Add(double t, JsonNode? value)
        {
            var json = value?.ToJsonString() ?? "null";
            var changed = _count > 0 && json != _lastJson;
            if (changed)
            {
                _changes++;
            }

            if (_count == 0)
            {
                _first = value;
            }

            _count++;
            _last = value;
            _lastJson = json;
            if (AdsSymbolAccess.AsNumber(value) is { } n)
            {
                _min = _min is null ? n : Math.Min(_min.Value, n);
                _max = _max is null ? n : Math.Max(_max.Value, n);
            }

            if ((!onlyChanges || changed || _count == 1) && _samples.Count < maxSamples)
            {
                _samples.Add(new { t = Math.Round(t, 1), v = value?.DeepClone() });
            }
        }

        public object ToResult() => new
        {
            symbol,
            type,
            sampleCount = _count,
            changes = _changes,
            min = _min,
            max = _max,
            first = _first?.DeepClone(),
            last = _last?.DeepClone(),
            errors = Errors == 0 ? (int?)null : Errors,
            truncated = _samples.Count >= maxSamples && _count > _samples.Count ? true : (bool?)null,
            samples = _samples,
        };
    }

    [McpServerTool(Name = "ads_wait_for_value", ReadOnly = true), Description(
        "Polls a symbol until it satisfies a condition or the timeout elapses - for test automation (e.g. set MAIN.bStart, then wait until MAIN.eState = 'Done'). " +
        "operator '==' (default) / '!=' compares typed values (enum names, T#.. literals, partial struct objects allowed); '>', '>=', '<', '<=' compare numerically. " +
        "Returns reached=true/false, the last value and the elapsed time (not an error on timeout). Example: {symbol:'MAIN.nStep', value:100, operator:'>=', timeoutSeconds:30}.")]
    public Task<CallToolResult> WaitForValue(
        [Description("Symbol path.")] string symbol,
        [Description("Expected value (same JSON format as ads_write_symbols).")] JsonElement value,
        [Description("'==', '!=', '>', '>=', '<', '<='.")] string @operator = "==",
        [Description("Max wait time in seconds.")] double timeoutSeconds = 10,
        [Description("Poll interval in ms.")] int intervalMs = 100,
        [Description(NetIdDesc)] string? netId = null,
        [Description(PortDesc)] int port = AdsSessionManager.DefaultPlcPort)
        => ads.RunAsync(async () =>
        {
            var s = ads.Resolve(netId, port, [symbol])[0];
            var client = ads.Get(netId, port);
            if (s.DataType is null)
            {
                throw new TcException($"Data type of '{s.InstancePath}' is unknown.");
            }

            var op = @operator.Trim();
            var numeric = op is ">" or ">=" or "<" or "<=";
            if (!numeric && op is not ("==" or "=" or "!=" or "<>"))
            {
                throw new TcException($"Unknown operator '{@operator}'.") { Hint = "Use ==, !=, >, >=, < or <=." };
            }

            double? target = null;
            if (numeric)
            {
                target = value.ValueKind == JsonValueKind.Number
                    ? value.GetDouble()
                    : AdsSymbolAccess.AsNumber(AdsValueCodec.Decode(s.DataType, AdsValueCodec.Encode(s.DataType, value)))
                      ?? throw new TcException($"Operator '{op}' needs a numeric value.");
            }

            var sw = Stopwatch.StartNew();
            var timeout = TimeSpan.FromSeconds(Math.Max(0, timeoutSeconds));
            JsonNode? current;
            var polls = 0;
            while (true)
            {
                var raw = AdsSymbolAccess.ReadRaw(client, [s])[0];
                var data = raw.Data ?? throw AdsSessionManager.ToTcException(raw.Error, $"Reading '{s.InstancePath}'");
                current = AdsValueCodec.Decode(s.DataType, data);
                polls++;
                bool ok;
                if (numeric)
                {
                    var n = AdsSymbolAccess.AsNumber(current) ?? throw new TcException($"'{s.InstancePath}' ({s.TypeName}) is not numeric.");
                    ok = op switch { ">" => n > target, ">=" => n >= target, "<" => n < target, _ => n <= target };
                }
                else
                {
                    // Expected value merged into the current image => partial struct objects compare only the given members.
                    var expected = AdsValueCodec.Decode(s.DataType, AdsValueCodec.Encode(s.DataType, value, data));
                    var equal = JsonNode.DeepEquals(expected, current);
                    ok = op is "!=" or "<>" ? !equal : equal;
                }

                if (ok || sw.Elapsed >= timeout)
                {
                    return new
                    {
                        reached = ok,
                        symbol = s.InstancePath,
                        value = current,
                        elapsedMs = (long)sw.Elapsed.TotalMilliseconds,
                        polls,
                    };
                }

                await Task.Delay(Math.Max(1, intervalMs)).ConfigureAwait(false);
            }
        });

    [McpServerTool(Name = "ads_invoke_rpc", Destructive = true), Description(
        "Calls a method of a PLC function block instance via ADS RPC. The method must be marked with {attribute 'TcRpcEnable'} in the PLC. " +
        "Arguments as JSON object by parameter name (or array by position); primitive and string parameters are supported. Returns returnValue and out parameters. " +
        "Use ads_get_symbol_info on the FB instance to see its rpcMethods. Example: {symbol:'MAIN.fbAxis', method:'MoveTo', args:{fPos:100.0, fVelo:50.0}}.")]
    public Task<CallToolResult> InvokeRpc(
        [Description("Instance path of the FB, e.g. MAIN.fbController.")] string symbol,
        [Description("Method name.")] string method,
        [Description("In-parameters: JSON object {name: value} or array [value, ...]. Omit for none.")] JsonElement? args = null,
        [Description(NetIdDesc)] string? netId = null,
        [Description(PortDesc)] int port = AdsSessionManager.DefaultPlcPort)
        => ads.Run(() =>
        {
            var s = ads.Resolve(netId, port, [symbol])[0];
            var loader = ads.Symbols(netId, port);
            var rpcType = s.DataType as IRpcCallableType;
            var m = rpcType?.RpcMethods.FirstOrDefault(x => x.Name.Equals(method, StringComparison.OrdinalIgnoreCase))
                    ?? throw new TcException($"'{s.InstancePath}' ({s.TypeName}) has no RPC method '{method}'.")
                    {
                        Hint = rpcType is null || rpcType.RpcMethods.Count == 0
                            ? "Mark the method with {attribute 'TcRpcEnable'} in the PLC, download, then retry (ads_browse_symbols refresh=true)."
                            : "Available: " + string.Join(", ", rpcType.RpcMethods.Select(x => x.Name)),
                    };

            var inParams = m.Parameters.Where(p => RpcMethodParameterExtension.IsInput(p)).ToList();
            var values = new object[inParams.Count];
            for (var i = 0; i < inParams.Count; i++)
            {
                var p = inParams[i];
                JsonElement? arg = null;
                if (args is { ValueKind: JsonValueKind.Object } obj)
                {
                    foreach (var prop in obj.EnumerateObject())
                    {
                        if (prop.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            arg = prop.Value;
                        }
                    }
                }
                else if (args is { ValueKind: JsonValueKind.Array } arr && i < arr.GetArrayLength())
                {
                    arg = arr[i];
                }

                if (arg is null)
                {
                    throw new TcException($"Missing argument '{p.Name}' ({p.TypeName}) for {m.Name}.")
                    {
                        Hint = "Parameters: " + string.Join(", ", inParams.Select(x => $"{x.Name}: {x.TypeName}")),
                    };
                }

                values[i] = RpcArgument(loader, p, arg.Value);
            }

            var client = ads.Get(netId, port);
            var ret = client.InvokeRpcMethod(s.InstancePath, m.Name, values, out var outValues);
            var outParams = m.Parameters.Where(p => RpcMethodParameterExtension.IsOutput(p)).ToList();
            return new
            {
                symbol = s.InstancePath,
                method = m.Name,
                returnValue = ret is null ? null : JsonSerializer.SerializeToNode(ret, Json.Options),
                outParameters = outParams.Count == 0 || outValues is null
                    ? null
                    : outParams.Select((p, i) => new { name = p.Name, value = i < outValues.Length && outValues[i] is not null ? JsonSerializer.SerializeToNode(outValues[i], Json.Options) : null }).ToList(),
            };
        });

    private static object RpcArgument(ISymbolLoader loader, IRpcMethodParameter p, JsonElement v)
    {
        IDataType? type = null;
        loader.DataTypes.TryGetType(p.TypeName, out type);
        var kind = type switch
        {
            null => null,
            IEnumType { BaseType: not null } et => AdsPrimitives.KindOf(et.BaseType),
            _ => AdsPrimitives.KindOf(type),
        };
        if (type is IStringType || p.TypeName.StartsWith("STRING", StringComparison.OrdinalIgnoreCase) || p.TypeName.StartsWith("WSTRING", StringComparison.OrdinalIgnoreCase))
        {
            return v.ValueKind == JsonValueKind.String ? v.GetString()! : v.GetRawText();
        }

        if (type is null || kind is null)
        {
            throw new TcException($"RPC parameter '{p.Name}' of type {p.TypeName} is not supported.")
            {
                Hint = "Only primitive (BOOL, INT, LREAL, ...), enum and STRING parameters can be passed via ads_invoke_rpc.",
            };
        }

        var bytes = AdsValueCodec.Encode(type, v);
        return kind.Value switch
        {
            PrimitiveKind.Bool => bytes[0] != 0,
            PrimitiveKind.SInt => (sbyte)bytes[0],
            PrimitiveKind.USInt => bytes[0],
            PrimitiveKind.Int => BitConverter.ToInt16(bytes),
            PrimitiveKind.UInt => BitConverter.ToUInt16(bytes),
            PrimitiveKind.DInt => BitConverter.ToInt32(bytes),
            PrimitiveKind.UDInt or PrimitiveKind.Time or PrimitiveKind.Tod or PrimitiveKind.Date or PrimitiveKind.Dt => BitConverter.ToUInt32(bytes),
            PrimitiveKind.LInt => BitConverter.ToInt64(bytes),
            PrimitiveKind.Real => BitConverter.ToSingle(bytes),
            PrimitiveKind.LReal => BitConverter.ToDouble(bytes),
            _ => BitConverter.ToUInt64(bytes),
        };
    }
}
