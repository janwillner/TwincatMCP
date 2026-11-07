using TwinCAT.Ads;

namespace TwinCatMcp.Core;

/// <summary>
/// Safety net for state-changing operations on the PC's OWN TwinCAT system (its local AMS Net ID).
/// A new TwinCAT project targets &lt;Local&gt;; forgetting system_set_target would otherwise activate a configuration over,
/// restart, or write into whatever application currently runs on this PC. Such operations require an explicit
/// allowLocalSystem=true. Separate runtimes on the same PC (e.g. a Usermode Runtime instance with its own Net ID)
/// and customer ADS server ports (25000+) are not affected.
/// </summary>
public static class LocalTargetGuard
{
    /// <summary>First AMS port of customer ADS servers; TwinCAT runtime/system ports are below.</summary>
    public const int FirstCustomerPort = 25000;

    private static readonly Lazy<string?> LocalNetIdLazy = new(ResolveLocalNetId);

    /// <summary>AMS Net ID of this PC's TwinCAT system (null if the router is not reachable).</summary>
    public static string? LocalNetId => LocalNetIdLazy.Value;

    public static bool IsLocal(string? netId, string? localNetId)
    {
        var id = netId?.Trim() ?? "";
        return id.Length == 0
               || id.Equals("local", StringComparison.OrdinalIgnoreCase)
               || id.Equals("localhost", StringComparison.OrdinalIgnoreCase)
               || id == "127.0.0.1.1.1"
               || (localNetId is not null && id == localNetId);
    }

    public static void Ensure(string? netId, bool allowLocalSystem, string operation, int? port = null)
        => Ensure(netId, allowLocalSystem, operation, LocalNetId, port);

    public static void Ensure(string? netId, bool allowLocalSystem, string operation, string? localNetId, int? port = null)
    {
        if (allowLocalSystem || port is >= FirstCustomerPort || !IsLocal(netId, localNetId))
        {
            return;
        }

        throw new TcException(
            $"Refusing to {operation} on this PC's own TwinCAT system ({localNetId ?? "local"}): it may run another application.")
        {
            Hint = "Set the intended target first (system_set_target / netId, e.g. a Usermode Runtime instance from rt_umrt_list), " +
                   "or pass allowLocalSystem=true if you really want to change the local system.",
        };
    }

    private static string? ResolveLocalNetId()
    {
        try
        {
            using var c = new AdsClient();
            c.Connect(AmsNetId.Local, 10000);
            return c.Address?.NetId.ToString();
        }
        catch
        {
            return null;
        }
    }
}
