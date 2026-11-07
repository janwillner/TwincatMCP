using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using TwinCAT.Ads;
using TwinCatMcp.Core;

namespace TwinCatMcp.Ads;

/// <summary>
/// Caches connected <see cref="AdsClient"/> instances per "netId:port" target.
/// ADS calls are thread-safe and do NOT go through the COM STA thread.
/// </summary>
public sealed class AdsSessionManager : IDisposable
{
    public const int DefaultPlcPort = 851;
    private readonly ConcurrentDictionary<string, AdsClient> _clients = new(StringComparer.OrdinalIgnoreCase);
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
        if (_clients.TryRemove(key, out var c))
        {
            c.Dispose();
            return true;
        }

        return false;
    }

    public IReadOnlyCollection<string> ConnectedTargets => _clients.Keys.ToArray();

    /// <summary>Runs an ADS operation and converts the outcome into a tool result.</summary>
    public async Task<CallToolResult> RunAsync(Func<Task<object?>> work)
    {
        try
        {
            return ToolResult.Ok(await work().ConfigureAwait(false));
        }
        catch (AdsErrorException ex)
        {
            return ToolResult.Error(new TcException($"ADS error {ex.ErrorCode} ({(int)ex.ErrorCode}): {ex.Message}", ex)
            {
                Hint = ex.ErrorCode switch
                {
                    AdsErrorCode.TargetPortNotFound => "Target port not found: the PLC runtime/application is not running on this port (851 = PLC1). Activate the configuration and start the PLC.",
                    AdsErrorCode.TargetMachineNotFound => "Target machine not found: no ADS route to this AMS Net ID. Add a route (route_add) or check the Net ID.",
                    AdsErrorCode.DeviceSymbolNotFound => "Symbol not found. Use ads_browse_symbols to list valid names (e.g. MAIN.nCounter, GVL.bStart).",
                    AdsErrorCode.ClientSyncTimeOut => "Timeout: target not reachable or not in Run mode.",
                    _ => null,
                },
            });
        }
        catch (Exception ex)
        {
            return ToolResult.Error(ex);
        }
    }

    public Task<CallToolResult> Run(Func<object?> work) => RunAsync(() => Task.FromResult(work()));

    public void Dispose()
    {
        foreach (var c in _clients.Values)
        {
            try { c.Dispose(); } catch { /* ignore */ }
        }

        _clients.Clear();
    }
}
