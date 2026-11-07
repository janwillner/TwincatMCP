using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using TwinCAT.Ads;
using TwinCatMcp.Core;

namespace TwinCatMcp.Ads;

public sealed record DiscoveredSystem(string Ip, string NetId, string? ComputerName, string? TwinCatVersion, string? OsVersion, string? Fingerprint);

/// <summary>UDP 48899 I/O for broadcast search and remote route registration.</summary>
public static class AdsDiscovery
{
    /// <summary>Directed broadcast addresses of all operational IPv4 interfaces plus 255.255.255.255.</summary>
    public static IEnumerable<IPAddress> BroadcastAddresses()
    {
        var set = new HashSet<IPAddress> { IPAddress.Broadcast };
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            foreach (var ua in nic.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask is null)
                {
                    continue;
                }

                var ip = ua.Address.GetAddressBytes();
                var mask = ua.IPv4Mask.GetAddressBytes();
                var bc = new byte[4];
                for (var i = 0; i < 4; i++)
                {
                    bc[i] = (byte)(ip[i] | ~mask[i]);
                }

                set.Add(new IPAddress(bc));
            }
        }

        return set;
    }

    public static async Task<List<DiscoveredSystem>> SearchAsync(IEnumerable<IPAddress> targets, TimeSpan timeout, CancellationToken cancel = default)
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) { EnableBroadcast = true };
        var request = AdsDiscoveryProtocol.BuildServerInfoRequest(0);
        foreach (var t in targets.Distinct())
        {
            try
            {
                await udp.SendAsync(request, request.Length, new IPEndPoint(t, AdsDiscoveryProtocol.Port)).ConfigureAwait(false);
            }
            catch (SocketException)
            {
                // interface may not allow broadcast - ignore
            }
        }

        var found = new Dictionary<string, DiscoveredSystem>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        cts.CancelAfter(timeout);
        while (!cts.IsCancellationRequested)
        {
            UdpReceiveResult rx;
            try
            {
                rx = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                continue;
            }

            var r = AdsDiscoveryProtocol.ParseResponse(rx.Buffer);
            if (r is null || !r.IsResponse || r.ServiceId != AdsDiscoveryProtocol.ServiceServerInfo)
            {
                continue;
            }

            var ip = rx.RemoteEndPoint.Address.IsIPv4MappedToIPv6 ? rx.RemoteEndPoint.Address.MapToIPv4() : rx.RemoteEndPoint.Address;
            found.TryAdd(r.NetId + "@" + ip, new DiscoveredSystem(ip.ToString(), r.NetId, r.ComputerName, r.TwinCatVersion, r.OsVersion, r.Fingerprint));
        }

        return found.Values.OrderBy(f => f.Ip, StringComparer.Ordinal).ToList();
    }

    /// <summary>Local IPv4 address the OS would use to reach <paramref name="remote"/>.</summary>
    public static IPAddress LocalAddressFor(IPAddress remote)
    {
        using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        s.Connect(remote, AdsDiscoveryProtocol.Port);
        return ((IPEndPoint)s.LocalEndPoint!).Address;
    }

    public static async Task<IPAddress> ResolveAsync(string host)
    {
        if (IPAddress.TryParse(host, out var ip))
        {
            return ip;
        }

        var addrs = await Dns.GetHostAddressesAsync(host).ConfigureAwait(false);
        return addrs.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
               ?? throw new TcException($"Host '{host}' has no IPv4 address.");
    }

    /// <summary>Sends the UDP 'add route' request and returns the remote's result code (0 = route added).</summary>
    public static async Task<uint> AddRemoteRouteAsync(IPAddress remote, string routeName, AmsNetId localNetId, string localAddress, string user, string password, TimeSpan timeout)
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        var request = AdsDiscoveryProtocol.BuildAddRouteRequest(routeName, localNetId, localAddress, user, password, 0);
        await udp.SendAsync(request, request.Length, new IPEndPoint(remote, AdsDiscoveryProtocol.Port)).ConfigureAwait(false);
        using var cts = new CancellationTokenSource(timeout);
        while (true)
        {
            UdpReceiveResult rx;
            try
            {
                rx = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw new TcException($"No answer from {remote}:{AdsDiscoveryProtocol.Port} within {timeout.TotalSeconds:0.#} s.")
                {
                    Hint = "Check that the target runs TwinCAT, is reachable and UDP 48899 is not blocked by a firewall (route_broadcast_search).",
                };
            }

            var r = AdsDiscoveryProtocol.ParseResponse(rx.Buffer);
            if (r is { IsResponse: true, ServiceId: AdsDiscoveryProtocol.ServiceAddRoute })
            {
                return r.Result ?? throw new TcException("The remote answered without a result code.");
            }
        }
    }
}
