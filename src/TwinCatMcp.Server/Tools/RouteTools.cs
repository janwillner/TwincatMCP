using System.ComponentModel;
using System.Net;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TwinCatMcp.Ads;
using TwinCatMcp.Core;

namespace TwinCatMcp.Tools;

/// <summary>ADS routes: list/add/remove via the system service, remote route registration and network search via UDP 48899.</summary>
[McpServerToolType]
public sealed class RouteTools(AdsSessionManager ads)
{
    private const string SysNetIdDesc = "System whose route table is used: 'local' (default) = this PC, or the AMS Net ID of a remote system reachable via ADS.";

    [McpServerTool(Name = "route_list", ReadOnly = true, Idempotent = true), Description(
        "Lists the ADS routes of a TwinCAT system. source 'ads' (default) enumerates the active routes through the system service " +
        "(also works for remote systems); source 'file' reads StaticRoutes.xml of this PC (persistent routes, read-only).")]
    public Task<CallToolResult> ListRoutes(
        [Description(SysNetIdDesc)] string? netId = null,
        [Description("'ads' (active routes via system service) or 'file' (local StaticRoutes.xml).")] string source = "ads",
        [Description("Explicit StaticRoutes.xml path for source 'file' (default: TwinCAT target folder / Usermode runtimes).")] string? filePath = null)
        => ads.Run(() =>
        {
            if (source.Equals("file", StringComparison.OrdinalIgnoreCase))
            {
                var files = filePath is not null ? [filePath] : StaticRoutesFile.CandidatePaths().Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (files.Count == 0)
                {
                    throw new TcException("No StaticRoutes.xml found on this PC.") { Hint = "Pass filePath, or use source 'ads'." };
                }

                return new
                {
                    files = files.Select(f => new { file = f, routes = StaticRoutesFile.Parse(File.ReadAllText(f)) }).ToList(),
                };
            }

            if (!source.Equals("ads", StringComparison.OrdinalIgnoreCase))
            {
                throw new TcException($"Unknown source '{source}'.") { Hint = "Use 'ads' or 'file'." };
            }

            var c = ads.Get(netId, AdsSessionManager.SystemServicePort);
            var routes = new SystemServiceClient(c).EnumRoutes();
            return new { system = c.Address?.NetId.ToString(), count = routes.Count, routes };
        });

    [McpServerTool(Name = "route_add"), Description(
        "Adds an ADS route on a TwinCAT system (default: this PC) via its system service: from that system to the target 'routeNetId' at 'address' (IP or host name). " +
        "temporary=true keeps it only until TwinCAT restarts; otherwise it is persisted in StaticRoutes.xml. " +
        "For a working connection the remote side usually needs a route back to this PC too - use route_add_remote for that. " +
        "Example: {name:'CX-1234', address:'192.168.1.20', routeNetId:'5.12.34.56.1.1'}.")]
    public Task<CallToolResult> AddRoute(
        [Description("Route name (unique on that system), e.g. the remote host name.")] string name,
        [Description("IP address or host name of the route target.")] string address,
        [Description("AMS Net ID of the route target.")] string routeNetId,
        [Description("Temporary route (not persisted).")] bool temporary = false,
        [Description(SysNetIdDesc)] string? netId = null)
        => ads.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(address))
            {
                throw new TcException("name and address are required.");
            }

            var target = AdsSessionManager.ParseNetId(routeNetId);
            var sys = new SystemServiceClient(ads.Get(netId, AdsSessionManager.SystemServicePort));
            sys.AddRoute(name.Trim(), target, address.Trim(), temporary);
            return new { added = name, routeNetId = target.ToString(), address, temporary };
        });

    [McpServerTool(Name = "route_remove", Destructive = true, Idempotent = true), Description(
        "Removes an ADS route (by route name, see route_list) from a TwinCAT system (default: this PC) via its system service.")]
    public Task<CallToolResult> RemoveRoute(
        [Description("Route name as shown by route_list.")] string name,
        [Description(SysNetIdDesc)] string? netId = null)
        => ads.Run(() =>
        {
            new SystemServiceClient(ads.Get(netId, AdsSessionManager.SystemServicePort)).RemoveRoute(name.Trim());
            return new { removed = name };
        });

    [McpServerTool(Name = "route_broadcast_search", ReadOnly = true), Description(
        "Searches the local networks for TwinCAT systems (UDP broadcast to port 48899, like 'Broadcast Search' in the XAE). " +
        "Returns IP, AMS Net ID, host name, TwinCAT version and OS of every system that answers. Optionally query specific hosts (unicast).")]
    public Task<CallToolResult> BroadcastSearch(
        [Description("How long to collect answers (ms).")] int timeoutMs = 1500,
        [Description("Additional hosts/IPs to query directly (e.g. systems in other subnets).")] string[]? hosts = null,
        [Description("Send to the broadcast addresses of all local interfaces (default true).")] bool broadcast = true)
        => ads.RunAsync(async () =>
        {
            var targets = new List<IPAddress>();
            if (broadcast)
            {
                targets.AddRange(AdsDiscovery.BroadcastAddresses());
            }

            foreach (var h in hosts ?? [])
            {
                targets.Add(await AdsDiscovery.ResolveAsync(h).ConfigureAwait(false));
            }

            if (targets.Count == 0)
            {
                throw new TcException("Nothing to search: broadcast=false and no hosts given.");
            }

            var found = await AdsDiscovery.SearchAsync(targets, TimeSpan.FromMilliseconds(Math.Clamp(timeoutMs, 100, 30_000))).ConfigureAwait(false);
            return new { count = found.Count, systems = found };
        });

    [McpServerTool(Name = "route_add_remote"), Description(
        "Registers a route on a REMOTE TwinCAT system back to this PC using the TwinCAT UDP route protocol (port 48899) - the same as " +
        "'Add Route' with 'Remote Route' in the XAE. Needs the remote OS credentials (e.g. Administrator / 1 on a CX with default image); " +
        "the password is sent in clear text by this protocol. Afterwards add the local route with route_add (or set alsoAddLocal=true). " +
        "Example: {remoteHost:'192.168.1.20', password:'1', alsoAddLocal:true, remoteNetId:'5.12.34.56.1.1'}.")]
    public Task<CallToolResult> AddRemoteRoute(
        [Description("IP address or host name of the remote TwinCAT system.")] string remoteHost,
        [Description("Password of the remote user.")] string password,
        [Description("Remote OS user name.")] string userName = "Administrator",
        [Description("Name of the route on the remote system (default: this PC's address).")] string? routeName = null,
        [Description("This PC's AMS Net ID as the remote should use it (default: local Net ID).")] string? localNetId = null,
        [Description("This PC's IP address / host name as seen by the remote (default: the local IP used to reach it).")] string? localAddress = null,
        [Description("Also add the matching local route to the remote (needs remoteNetId or discovers it).")] bool alsoAddLocal = false,
        [Description("AMS Net ID of the remote (for alsoAddLocal; discovered via UDP if empty).")] string? remoteNetId = null,
        [Description("Answer timeout in ms.")] int timeoutMs = 5000)
        => ads.RunAsync(async () =>
        {
            var remote = await AdsDiscovery.ResolveAsync(remoteHost).ConfigureAwait(false);
            var ownNetId = localNetId is null
                ? ads.Get(null, AdsSessionManager.SystemServicePort).Address!.NetId
                : AdsSessionManager.ParseNetId(localNetId);
            var ownAddress = localAddress ?? AdsDiscovery.LocalAddressFor(remote).ToString();
            var result = await AdsDiscovery.AddRemoteRouteAsync(remote, routeName ?? ownAddress, ownNetId, ownAddress, userName, password,
                TimeSpan.FromMilliseconds(Math.Clamp(timeoutMs, 200, 60_000))).ConfigureAwait(false);
            if (result != 0)
            {
                throw new TcException($"The remote system rejected the route (result 0x{result:X}).")
                {
                    Hint = result == 0x704 || result == 0x70A
                        ? "Check user name and password of the remote system."
                        : "Check credentials, that the route name is not used yet, and the remote's firewall.",
                };
            }

            object? local = null;
            if (alsoAddLocal)
            {
                var rid = remoteNetId;
                string? name = null;
                if (rid is null)
                {
                    var found = await AdsDiscovery.SearchAsync([remote], TimeSpan.FromMilliseconds(1500)).ConfigureAwait(false);
                    rid = found.FirstOrDefault()?.NetId ?? throw new TcException("Remote route added, but the remote Net ID could not be discovered for the local route.")
                    {
                        Hint = "Call route_add with the remote's Net ID.",
                    };
                    name = found[0].ComputerName;
                }

                var sys = new SystemServiceClient(ads.Get(null, AdsSessionManager.SystemServicePort));
                name ??= remoteHost;
                sys.AddRoute(name, AdsSessionManager.ParseNetId(rid), remote.ToString(), temporary: false);
                local = new { name, netId = rid, address = remote.ToString() };
            }

            return new
            {
                remote = remote.ToString(),
                remoteRouteName = routeName ?? ownAddress,
                localNetId = ownNetId.ToString(),
                localAddress = ownAddress,
                localRoute = local,
            };
        });
}
