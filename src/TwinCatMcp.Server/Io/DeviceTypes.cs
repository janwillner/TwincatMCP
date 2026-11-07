using System.Globalization;
using TwinCatMcp.Core;

namespace TwinCatMcp.Io;

/// <summary>Friendly I/O device type names -> ITcSmTreeItem item sub types below TIID (verified on TwinCAT 3.1.4026).</summary>
public static class DeviceTypes
{
    public sealed record DeviceType(string Name, int SubType, string Description);

    /// <summary>
    /// Sub types from InfoSys "ITcSmTreeItem Item Sub Types: Devices" / "Creating and handling Profinet devices"
    /// and checked with CreateChild on TIID (ItemSubTypeName in parentheses).
    /// </summary>
    public static readonly IReadOnlyList<DeviceType> All =
    [
        new("ethercat-master", 111, "EtherCAT Master"),
        new("ethercat-slave", 130, "EtherCAT Slave"),
        new("ethercat-automation-protocol", 112, "EtherCAT Automation Protocol (Network Variables)"),
        new("ethercat-simulation", 144, "EtherCAT Simulation"),
        new("ethercat-master-xfc", 148, "EtherCAT Master (XFC)"),
        new("realtime-ethernet-adapter", 109, "Real-Time Ethernet Adapter (Multiple Protocol Handler)"),
        new("realtime-ethernet-protocol", 138, "Real-Time Ethernet Protocol (BK90xx, AX2000-B900)"),
        new("virtual-ethernet", 45, "Virtual Ethernet Interface"),
        new("profinet-controller", 113, "Profinet I/O Controller (RT)"),
        new("profinet-device", 115, "Profinet I/O Device (RT)"),
        new("profinet-controller-el6631", 119, "Profinet I/O Controller EL6631, EtherCAT"),
        new("profinet-controller-el6632", 126, "Profinet I/O Controller EL6632, EtherCAT"),
        new("profinet-device-el6631", 118, "Profinet I/O Device EL6631-0010, EtherCAT"),
        new("ethernetip-scanner", 133, "EtherNet/IP Scanner"),
        new("ethernetip-adapter", 139, "EtherNet/IP Adapter (Slave)"),
        new("ethernetip-scanner-el6652", 151, "EtherNet/IP Scanner (EL6652)"),
        new("ethernetip-adapter-el6652", 145, "EtherNet/IP Adapter (EL6652-0010)"),
        new("profibus-master-ccat", 127, "Profibus Master (CCAT)"),
        new("canopen-master-ccat", 129, "CANopen Master (CCAT)"),
        new("ccat-adapter", 124, "CCAT Adapter"),
        new("cx-terminal-device", 120, "CX Terminal Device"),
        new("usb", 57, "Virtual USB Interface"),
        new("bacnet-ip", 147, "BACnet/IP Device"),
        new("mqtt-device", 157, "Iot Mqtt Device"),
        new("opcua-device", 155, "Real-Time OPC UA Device"),
    ];

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ethercat"] = "ethercat-master",
        ["ecat"] = "ethercat-master",
        ["ecat-master"] = "ethercat-master",
        ["eap"] = "ethercat-automation-protocol",
        ["network-variables"] = "ethercat-automation-protocol",
        ["virtual-ethercat"] = "ethercat-simulation",
        ["profinet"] = "profinet-controller",
        ["ethernetip"] = "ethernetip-scanner",
        ["ethernet-ip"] = "ethernetip-scanner",
        ["eip-scanner"] = "ethernetip-scanner",
        ["eip-adapter"] = "ethernetip-adapter",
        ["rt-ethernet"] = "realtime-ethernet-adapter",
    };

    /// <summary>Resolves "ethercat-master", "EtherCAT Master", "EtherCAT_Master", an alias or a numeric sub type.</summary>
    public static int Resolve(string type)
    {
        var t = (type ?? "").Trim();
        if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
        {
            return numeric;
        }

        var key = Normalize(t);
        key = Aliases.GetValueOrDefault(key, key);
        var match = All.FirstOrDefault(d => d.Name == key)
                    ?? All.FirstOrDefault(d => Normalize(d.Description) == key);
        return match?.SubType ?? throw new TcException($"Unknown I/O device type '{type}'.")
        {
            Hint = "Valid types: " + string.Join(", ", All.Select(d => d.Name)) + " - or pass the numeric item sub type.",
        };
    }

    public static string? NameOf(int subType) => All.FirstOrDefault(d => d.SubType == subType)?.Name;

    /// <summary>
    /// XAE-style default name "Device N (EtherCAT)" with the first free N (CreateChild with an empty name would create an
    /// item without a name).
    /// </summary>
    public static string DefaultDeviceName(int subType, IEnumerable<string> existingNames)
    {
        var label = subType == 111 ? "EtherCAT" : All.FirstOrDefault(d => d.SubType == subType)?.Description ?? $"Type {subType}";
        var names = existingNames.ToList();
        var n = names.Count + 1;
        while (names.Any(x => x.StartsWith($"Device {n} (", StringComparison.OrdinalIgnoreCase) || x.Equals($"Device {n}", StringComparison.OrdinalIgnoreCase)))
        {
            n++;
        }

        return $"Device {n} ({label})";
    }

    private static string Normalize(string s)
        => string.Join("-", s.ToLowerInvariant().Split([' ', '_', '-', '/', '(', ')', ','], StringSplitOptions.RemoveEmptyEntries));
}
