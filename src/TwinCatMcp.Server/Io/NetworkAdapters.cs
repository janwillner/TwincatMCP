using System.Net.NetworkInformation;
using TwinCatMcp.Core;

namespace TwinCatMcp.Io;

/// <summary>A local Ethernet adapter in the form TwinCAT uses for device binding (Pnp AddressInfo).</summary>
public sealed record NetworkAdapter(
    string Name,
    string Description,
    string Mac,
    string Guid,
    string DeviceName,
    string Status,
    string InterfaceType,
    long SpeedMbit,
    bool TwinCatDriver);

public static class NetworkAdapters
{
    /// <summary>
    /// Local Ethernet adapters. <c>TwinCatDriver</c> is true when the TwinCAT RT-Ethernet driver is installed for the adapter
    /// (TwinCAT renames such adapters to "TwinCAT-Intel PCI Ethernet Adapter ..."), which is required for real-time EtherCAT.
    /// </summary>
    public static IReadOnlyList<NetworkAdapter> List(bool includeVirtual = true)
        => NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT
                        or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.Ethernet3Megabit or NetworkInterfaceType.Unknown)
            .Where(n => n.GetPhysicalAddress().GetAddressBytes().Length == 6)
            .Where(n => includeVirtual || !IsVirtual(n.Description))
            .Select(n =>
            {
                var mac = string.Join("-", n.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")));
                return new NetworkAdapter(
                    n.Name,
                    n.Description,
                    mac,
                    n.Id,
                    @"\DEVICE\" + n.Id,
                    n.OperationalStatus.ToString(),
                    n.NetworkInterfaceType.ToString(),
                    n.Speed > 0 ? n.Speed / 1_000_000 : 0,
                    n.Description.Contains("TwinCAT", StringComparison.OrdinalIgnoreCase));
            })
            .OrderByDescending(a => a.TwinCatDriver)
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Finds one adapter by name, description, MAC, GUID or \DEVICE\{GUID} (exact first, then unique substring).</summary>
    public static NetworkAdapter Find(IReadOnlyList<NetworkAdapter> adapters, string query)
    {
        var q = (query ?? "").Trim();
        string? mac = null;
        try
        {
            mac = IoXml.NormalizeMac(q);
        }
        catch (TcException)
        {
            // not a MAC
        }

        var exact = adapters.Where(a =>
            a.Name.Equals(q, StringComparison.OrdinalIgnoreCase)
            || a.Description.Equals(q, StringComparison.OrdinalIgnoreCase)
            || a.Guid.Trim('{', '}').Equals(q.Trim('{', '}'), StringComparison.OrdinalIgnoreCase)
            || a.DeviceName.Equals(q, StringComparison.OrdinalIgnoreCase)
            || (mac is not null && IoXml.NormalizeMac(a.Mac) == mac)).ToList();
        if (exact.Count == 1)
        {
            return exact[0];
        }

        var partial = exact.Count > 1 ? exact : adapters.Where(a =>
            a.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || a.Description.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        return partial.Count switch
        {
            1 => partial[0],
            0 => throw new TcException($"No network adapter matches '{query}'.") { Hint = "Use io_list_network_adapters (name, description, MAC or GUID)." },
            _ => throw new TcException($"'{query}' matches {partial.Count} adapters: {string.Join(", ", partial.Select(a => a.Name))}.") { Hint = "Use the MAC address or GUID." },
        };
    }

    private static bool IsVirtual(string description)
        => description.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
           || description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase)
           || description.Contains("TAP-", StringComparison.OrdinalIgnoreCase)
           || description.Contains("Loopback", StringComparison.OrdinalIgnoreCase)
           || description.Contains("VPN", StringComparison.OrdinalIgnoreCase);
}
