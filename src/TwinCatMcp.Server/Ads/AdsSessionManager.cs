using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using TwinCAT;
using TwinCAT.Ads;
using TwinCAT.Ads.TypeSystem;
using TwinCAT.TypeSystem;
using TwinCAT.ValueAccess;
using TwinCatMcp.Core;

namespace TwinCatMcp.Ads;

/// <summary>
/// Caches connected <see cref="AdsClient"/> instances (and their symbol tables) per "netId:port" target.
/// ADS calls are thread-safe and do NOT go through the COM STA thread.
/// </summary>
public sealed class AdsSessionManager : IDisposable
{
    public const int DefaultPlcPort = 851;
    public const int SystemServicePort = 10000;
    private readonly ConcurrentDictionary<string, AdsClient> _clients = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<ISymbolLoader>> _loaders = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<AdsSessionManager> _logger;

    public AdsSessionManager(ILogger<AdsSessionManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Normalizes the target address. Empty / "local" / "localhost" means the local router (AmsNetId.Local).
    /// </summary>
    public static AmsNetId ParseNetId(string? netId)
    {
        if (string.IsNullOrWhiteSpace(netId) || netId.Equals("local", StringComparison.OrdinalIgnoreCase) || netId.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return AmsNetId.Local;
        }

        if (!AmsNetId.TryParse(netId.Trim(), out var parsed))
        {
            throw new TcException($"Invalid AMS Net ID '{netId}'.") { Hint = "Format: 6 bytes, e.g. 5.12.34.56.1.1, or 'local'." };
        }

        return parsed;
    }

    public static string Key(AmsNetId netId, int port) => $"{netId}:{port}";

    /// <summary>Returns a connected client for the target (connecting on first use).</summary>
    public AdsClient Get(string? netId, int port = DefaultPlcPort, int timeoutMs = 5000)
    {
        var id = ParseNetId(netId);
        var key = Key(id, port);
        var client = _clients.GetOrAdd(key, _ =>
        {
            var c = new AdsClient();
            c.Timeout = timeoutMs;
            c.Connect(id, port);
            _logger.LogInformation("ADS connected to {Key}", key);
            return c;
        });

        if (!client.IsConnected)
        {
            client.Connect(id, port);
        }

        client.Timeout = timeoutMs;
        return client;
    }

    public bool Disconnect(string? netId, int port)
    {
        var key = Key(ParseNetId(netId), port);
        _loaders.TryRemove(key, out _);
        if (_clients.TryRemove(key, out var c))
        {
            c.Dispose();
            return true;
        }

        return false;
    }

    public IReadOnlyCollection<string> ConnectedTargets => _clients.Keys.ToArray();

    public IReadOnlyDictionary<string, AdsClient> Clients => _clients;

    /// <summary>
    /// Returns the (cached) flat symbol table of the target. <paramref name="refresh"/> re-uploads it
    /// (needed after a PLC download / online change).
    /// </summary>
    public ISymbolLoader Symbols(string? netId, int port, bool refresh = false)
    {
        var client = Get(netId, port);
        var key = Key(ParseNetId(netId), port);
        if (refresh)
        {
            _loaders.TryRemove(key, out _);
        }

        var lazy = _loaders.GetOrAdd(key, k => new Lazy<ISymbolLoader>(() =>
        {
            var settings = new SymbolLoaderSettings(SymbolsLoadMode.Flat, ValueAccessMode.IndexGroupOffsetPreferred);
            var loader = SymbolLoaderFactory.Create(client, settings);
            _ = loader.Symbols.Count; // force upload now so errors surface here
            _logger.LogInformation("ADS symbols uploaded from {Key}: {Count} root symbols", key, loader.Symbols.Count);
            return loader;
        }));

        try
        {
            return lazy.Value;
        }
        catch
        {
            _loaders.TryRemove(key, out _);
            throw;
        }
    }

    /// <summary>Resolves full instance paths (incl. struct members and array elements) to symbols.</summary>
    public IReadOnlyList<ISymbol> Resolve(string? netId, int port, IEnumerable<string> paths)
    {
        var list = paths.Select(SymbolPath.Normalize).ToList();
        if (list.Count == 0)
        {
            throw new TcException("No symbol given.") { Hint = "Pass at least one instance path, e.g. [\"MAIN.nCounter\"]." };
        }

        var loader = Symbols(netId, port);
        var result = new List<ISymbol>(list.Count);
        foreach (var p in list)
        {
            var s = TryResolve(loader, p);
            if (s is null)
            {
                // PLC may have been re-downloaded: refresh the symbol table once.
                loader = Symbols(netId, port, refresh: true);
                s = TryResolve(loader, p) ?? throw UnknownSymbol(loader, p);
            }

            result.Add(s);
        }

        return result;
    }

    public static ISymbol? TryResolve(ISymbolLoader loader, string path)
    {
        ISymbol? best = null;
        foreach (var root in loader.Symbols)
        {
            if (SymbolPath.IsSelfOrDescendant(root.InstancePath, path) && (best is null || root.InstancePath.Length > best.InstancePath.Length))
            {
                best = root;
            }
        }

        var current = best;
        while (current is not null && !current.InstancePath.Equals(path, StringComparison.OrdinalIgnoreCase))
        {
            ISymbol? next = null;
            foreach (var sub in current.SubSymbols)
            {
                if (SymbolPath.IsSelfOrDescendant(sub.InstancePath, path) && (next is null || sub.InstancePath.Length > next.InstancePath.Length))
                {
                    next = sub;
                }
            }

            current = next;
        }

        return current;
    }

    private static TcException UnknownSymbol(ISymbolLoader loader, string path)
    {
        var suggestions = SymbolPath.Suggest(path, loader.Symbols.Select(s => s.InstancePath), 5);
        return new TcException($"Symbol '{path}' not found.")
        {
            Hint = (suggestions.Count > 0 ? "Did you mean: " + string.Join(", ", suggestions) + ". " : string.Empty) +
                   "Use ads_browse_symbols to list valid names (e.g. MAIN.nCounter, GVL.bStart). Members/elements: MAIN.stData.nValue, MAIN.aValues[3].",
        };
    }

    /// <summary>Runs an ADS operation and converts the outcome into a tool result.</summary>
    public async Task<CallToolResult> RunAsync(Func<Task<object?>> work)
    {
        try
        {
            return ToolResult.Ok(await work().ConfigureAwait(false));
        }
        catch (AdsErrorException ex)
        {
            return ToolResult.Error(ToTcException(ex));
        }
        catch (Exception ex) when (ex.InnerException is AdsErrorException inner)
        {
            return ToolResult.Error(ToTcException(inner));
        }
        catch (Exception ex)
        {
            return ToolResult.Error(ex);
        }
    }

    public Task<CallToolResult> Run(Func<object?> work) => RunAsync(() => Task.FromResult(work()));

    public static TcException ToTcException(AdsErrorException ex) => new($"ADS error {ex.ErrorCode} ({(int)ex.ErrorCode}, 0x{(int)ex.ErrorCode:X}): {ex.Message}", ex)
    {
        Hint = HintFor(ex.ErrorCode),
    };

    public static TcException ToTcException(AdsErrorCode code, string what) => new($"{what} failed: ADS error {code} ({(int)code}, 0x{(int)code:X}).")
    {
        Hint = HintFor(code),
    };

    public static string? HintFor(AdsErrorCode code) => code switch
    {
        AdsErrorCode.TargetPortNotFound => "Target port not found: nothing is registered on this AMS port (PLC runtime 1 = 851, system service = 10000). " +
                                           "Activate the configuration and start the PLC, or use ads_list_plc_runtimes to find running PLCs.",
        AdsErrorCode.TargetMachineNotFound => "Target machine not found: no ADS route to this AMS Net ID. Add a route (route_add / route_add_remote) or check the Net ID (route_list).",
        AdsErrorCode.DeviceSymbolNotFound => "Symbol not found. Use ads_browse_symbols to list valid names (e.g. MAIN.nCounter, GVL.bStart).",
        AdsErrorCode.ClientSyncTimeOut or AdsErrorCode.DeviceTimeOut => "Timeout: target not reachable, not in Run mode or busy. Check ads_read_state and the route.",
        AdsErrorCode.DeviceServiceNotSupported => "The ADS server on this port does not support this service (wrong port or index group?).",
        AdsErrorCode.DeviceInvalidGroup => "Invalid index group for this ADS server.",
        AdsErrorCode.DeviceInvalidOffset => "Invalid index offset for this ADS server.",
        AdsErrorCode.DeviceInvalidSize => "Invalid data size: the requested length does not match the server's expectation.",
        AdsErrorCode.DeviceNotFound => "Not found (e.g. file/directory does not exist on the target).",
        AdsErrorCode.DeviceInvalidState or AdsErrorCode.DeviceNotReady => "The device is not in a state that allows this request (e.g. PLC stopped or TwinCAT in config mode).",
        AdsErrorCode.DeviceAccessDenied or AdsErrorCode.AccessDenied => "Access denied by the target.",
        AdsErrorCode.DeviceSymbolVersionInvalid => "Symbol version changed (PLC was re-downloaded). Call ads_browse_symbols with refresh=true and retry.",
        AdsErrorCode.PortNotConnected or AdsErrorCode.PortDisabled => "The TwinCAT system service / router is not running on this PC.",
        AdsErrorCode.DeviceNoMoreHandles => "No more entries / handles.",
        _ => null,
    };

    public void Dispose()
    {
        foreach (var c in _clients.Values)
        {
            try { c.Dispose(); } catch { /* ignore */ }
        }

        _clients.Clear();
        _loaders.Clear();
    }
}
