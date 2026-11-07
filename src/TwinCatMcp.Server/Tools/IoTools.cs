using System.ComponentModel;
using System.Runtime.InteropServices;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TCatSysManagerLib;
using TwinCatMcp.Core;
using TwinCatMcp.Io;

namespace TwinCatMcp.Tools;

/// <summary>I/O devices (TIID), network adapters, device/box scan and process image variables.</summary>
[McpServerToolType]
public sealed class IoTools(XaeSession session)
{
    [McpServerTool(Name = "io_list_devices", ReadOnly = true), Description(
        "Lists the I/O devices below TIID (EtherCAT masters, Profinet, EtherNet/IP, ...) with type, number of boxes, " +
        "bound network adapter and AMS Net ID. Start here before adding boxes or linking variables.")]
    public Task<CallToolResult> ListDevices()
        => session.RunAsync(s => TreeInfo.Children(s.Lookup("TIID")).Select(d =>
        {
            var info = TreeInfo.Describe(d);
            DeviceAddress? address = null;
            try { address = IoXml.ParseDeviceAddress(d.ProduceXml(false)); } catch { /* no DeviceDef */ }
            return new
            {
                info.Name,
                info.Path,
                type = DeviceTypes.NameOf(info.SubType),
                info.SubType,
                info.SubTypeName,
                info.Disabled,
                boxCount = IoTreeHelpers.DirectChildren(d).Count(IoTreeHelpers.IsBox),
                amsNetId = address?.AmsNetId,
                adapter = address is { IsBound: true } ? new { description = address.DeviceDesc, deviceName = address.DeviceName, mac = address.Mac } : null,
            };
        }).ToArray());

    [McpServerTool(Name = "io_add_device"), Description(
        "Adds an I/O device below TIID. Types: ethercat-master (111), ethercat-slave (130), ethercat-automation-protocol (112), " +
        "ethercat-simulation (144), profinet-controller (113), profinet-device (115), ethernetip-scanner (133), ethernetip-adapter (139), " +
        "realtime-ethernet-adapter (109), virtual-ethernet (45), usb (57), ... or a numeric item sub type. " +
        "Optionally binds a network adapter (see io_list_network_adapters). Next step for EtherCAT: ecat_add_terminals.")]
    public Task<CallToolResult> AddDevice(
        [Description("Device type (friendly name like 'ethercat-master') or numeric item sub type.")] string type = "ethercat-master",
        [Description("Tree item name, e.g. 'EtherCAT Master' or 'Device 1 (EtherCAT)'. Empty = TwinCAT default name.")] string? name = null,
        [Description("Optional network adapter to bind (name, description, MAC or GUID of a local adapter).")] string? adapter = null)
        => session.RunAsync(s =>
        {
            var subType = DeviceTypes.Resolve(type);
            var tiid = s.Lookup("TIID");
            var existing = TreeInfo.Children(tiid).Select(c => c.Name).ToList();
            var device = tiid.CreateChild(string.IsNullOrWhiteSpace(name) ? DeviceTypes.DefaultDeviceName(subType, existing) : name.Trim(), subType, "", Type.Missing);
            object? binding = null;
            if (!string.IsNullOrWhiteSpace(adapter))
            {
                binding = BindAdapter(device, NetworkAdapters.Find(NetworkAdapters.List(), adapter));
            }

            return new { device = TreeInfo.Describe(device), adapter = binding };
        }, TimeSpan.FromMinutes(2));

    [McpServerTool(Name = "io_remove_device", Destructive = true), Description(
        "Removes an I/O device (incl. all its boxes and their links) from TIID. Accepts the device name or full path.")]
    public Task<CallToolResult> RemoveDevice(
        [Description("Device name (e.g. 'EtherCAT Master') or path 'TIID^EtherCAT Master'.")] string device)
        => session.RunAsync(s =>
        {
            var item = s.Lookup(DevicePath(device));
            var name = item.Name;
            item.Parent.DeleteChild(name);
            return new { removed = "TIID^" + name };
        });

    [McpServerTool(Name = "io_list_network_adapters", ReadOnly = true), Description(
        "Lists the local Ethernet adapters with MAC, GUID and the '\\DEVICE\\{GUID}' name TwinCAT uses for binding. " +
        "twinCatDriver=true marks adapters with the TwinCAT RT-Ethernet driver installed (required for real-time EtherCAT on this PC). " +
        "For a remote target use io_scan_devices instead (it reports the target's adapters).")]
    public Task<CallToolResult> ListNetworkAdapters(
        [Description("Include virtual adapters (Hyper-V, TAP, VPN).")] bool includeVirtual = false)
        => Task.FromResult(Wrap(() => NetworkAdapters.List(includeVirtual)));

    [McpServerTool(Name = "io_set_device_adapter"), Description(
        "Binds an I/O device (e.g. the EtherCAT master) to a network adapter via its XML address info (DeviceDef/AddressInfo/Pnp). " +
        "Pass 'adapter' to pick a local adapter (name/description/MAC/GUID), or deviceName+mac(+description) for adapters of a remote target " +
        "(as reported by io_scan_devices). Pass clear=true to unbind.")]
    public Task<CallToolResult> SetDeviceAdapter(
        [Description("Device name or path, e.g. 'EtherCAT Master'.")] string device,
        [Description("Local adapter: name ('Ethernet 2'), description, MAC ('00-01-05-69-BC-0A') or GUID.")] string? adapter = null,
        [Description("Explicit adapter device name '\\DEVICE\\{GUID}' (remote targets).")] string? deviceName = null,
        [Description("Explicit MAC address (remote targets).")] string? mac = null,
        [Description("Explicit adapter description (remote targets).")] string? description = null,
        [Description("Remove the binding.")] bool clear = false)
        => session.RunAsync(s =>
        {
            var item = s.Lookup(DevicePath(device));
            if (clear)
            {
                item.ConsumeXml(IoXml.BuildAdapterXml("", "", "000000000000"));
                return new { device = item.PathName, address = IoXml.ParseDeviceAddress(item.ProduceXml(false)) };
            }

            NetworkAdapter target;
            if (!string.IsNullOrWhiteSpace(adapter))
            {
                target = NetworkAdapters.Find(NetworkAdapters.List(), adapter);
            }
            else if (!string.IsNullOrWhiteSpace(deviceName) && !string.IsNullOrWhiteSpace(mac))
            {
                target = new NetworkAdapter(description ?? "", description ?? "", mac, deviceName.Replace(@"\DEVICE\", "", StringComparison.OrdinalIgnoreCase), deviceName, "", "", 0, false);
            }
            else
            {
                throw new TcException("Pass 'adapter' (local adapter) or 'deviceName' + 'mac'.") { Hint = "Use io_list_network_adapters to see local adapters." };
            }

            return new { device = item.PathName, adapter = BindAdapter(item, target) };
        });

    [McpServerTool(Name = "io_scan_devices"), Description(
        "Scans the target system for I/O devices ('Scan' in XAE, via ProduceXml on TIID). Returns the found devices (type, sub type, adapter). " +
        "Requires the target in Config mode and real hardware; without hardware an empty list is returned. " +
        "addFound=true creates the found devices in the configuration with their address info (like XAE does).")]
    public Task<CallToolResult> ScanDevices(
        [Description("Create the found devices in the project and set their address info.")] bool addFound = false,
        [Description("Run 'Reload devices' first (needed for CX/K-bus devices).")] bool reload = false,
        int timeoutSeconds = 120)
        => session.RunAsync(s =>
        {
            var tiid = s.Lookup("TIID");
            string? reloadError = null;
            if (reload)
            {
                try { tiid.ConsumeXml(IoXml.ReloadDevicesXml); } catch (Exception ex) { reloadError = ex.Message; }
            }

            string xml;
            try
            {
                xml = tiid.ProduceXml(false);
            }
            catch (COMException ex)
            {
                return new
                {
                    scanned = false,
                    error = ex.Message,
                    hint = "The device scan needs a reachable target in Config mode (see system_ tools). Without hardware nothing can be found.",
                    reloadError,
                };
            }

            var found = IoXml.ParseFoundDevices(xml);
            var created = new List<object>();
            if (addFound)
            {
                foreach (var f in found)
                {
                    var existing = TreeInfo.Children(tiid).Select(c => c.Name).ToList();
                    var name = !string.IsNullOrWhiteSpace(f.Name) && !existing.Contains(f.Name, StringComparer.OrdinalIgnoreCase)
                        ? f.Name
                        : DeviceTypes.DefaultDeviceName(f.SubType, existing);
                    var dev = tiid.CreateChild(name, f.SubType, "", Type.Missing);
                    if (f.AddressInfoXml is not null)
                    {
                        dev.ConsumeXml(IoXml.WrapDeviceDef(f.AddressInfoXml));
                    }

                    created.Add(TreeInfo.Describe(dev));
                }
            }

            return new
            {
                scanned = true,
                foundCount = found.Count,
                found = found.Select(f => new { f.Name, f.SubType, type = DeviceTypes.NameOf(f.SubType), f.SubTypeName, f.Adapter, addressInfoXml = f.AddressInfoXml }),
                created = addFound ? created : null,
                hint = found.Count == 0 ? "No devices found. On a PC without fieldbus hardware this is expected; build the configuration offline (io_add_device, ecat_add_terminals)." : null,
                reloadError,
            };
        }, timeoutSeconds);

    [McpServerTool(Name = "io_scan_boxes"), Description(
        "Scans the boxes/terminals connected to an I/O device (e.g. EtherCAT slaves of a master, 'Scan' on the device in XAE) and adds them. " +
        "Requires a bound adapter, the target in Config mode and connected hardware; otherwise returns scanned=false with the reason.")]
    public Task<CallToolResult> ScanBoxes(
        [Description("Device name or path, e.g. 'EtherCAT Master'.")] string device,
        int timeoutSeconds = 120)
        => session.RunAsync(s =>
        {
            var item = s.Lookup(DevicePath(device));
            var before = TreeInfo.Children(item).Count();
            var address = IoXml.ParseDeviceAddress(item.ProduceXml(false));
            try
            {
                item.ConsumeXml(IoXml.ScanBoxesXml);
            }
            catch (COMException ex)
            {
                return new
                {
                    scanned = false,
                    device = item.PathName,
                    error = ex.Message,
                    hint = address is { IsBound: true }
                        ? "Box scan failed: check that the target is in Config mode and the EtherCAT hardware is connected and powered."
                        : "The device is not bound to a network adapter. Use io_set_device_adapter first (and the hardware must be connected).",
                };
            }

            var boxes = IoTreeHelpers.DirectChildren(item).Select(b => TreeInfo.Describe(b)).ToArray();
            return new { scanned = true, device = item.PathName, boxCountBefore = before, boxCount = boxes.Length, boxes };
        }, timeoutSeconds);

    [McpServerTool(Name = "io_list_variables", ReadOnly = true), Description(
        "Lists the process image variables below a box, terminal, channel, device or PLC instance with data type, bit size, " +
        "direction (input/output) and what each one is linked to. Examples: 'TIID^EtherCAT Master^Term 1 (EK1100)^Term 2 (EL1008)', " +
        "'TIPC^Plc1^Plc1 Instance'. Use the returned paths with mapping_link.")]
    public Task<CallToolResult> ListVariables(
        [Description("Tree path of a box/terminal/channel/PLC instance or variable group.")] string treePath,
        [Description("Filter: 'input', 'output' or empty for both.")] string? direction = null,
        [Description("Also include variables of boxes below this item (e.g. all terminals below a coupler).")] bool includeSubBoxes = false,
        [Description("Include links (requires reading the project mapping once).")] bool includeLinks = true,
        int maxVariables = 500)
        => session.RunAsync(s =>
        {
            var root = s.Lookup(treePath);
            var links = includeLinks ? MappingInfo.Parse(s.SysManager.ProduceMappingInfo()) : [];
            var vars = IoTreeHelpers.EnumerateVariables(root, includeSubBoxes, Math.Clamp(maxVariables, 1, 20_000))
                .Where(v => string.IsNullOrWhiteSpace(direction) || v.Direction.Equals(direction.Trim(), StringComparison.OrdinalIgnoreCase)
                                                                 || (direction.Trim().StartsWith("out", StringComparison.OrdinalIgnoreCase) && v.Direction == "output")
                                                                 || (direction.Trim().StartsWith("in", StringComparison.OrdinalIgnoreCase) && v.Direction == "input"))
                .Select(v => new
                {
                    v.Name,
                    v.Path,
                    v.Type,
                    v.BitSize,
                    v.Direction,
                    linkedTo = includeLinks ? MappingInfo.LinkedTo(links, v.Path).ToArray() : null,
                })
                .ToList();
            return new { treePath = root.PathName, count = vars.Count, variables = vars };
        }, TimeSpan.FromMinutes(5));

    [McpServerTool(Name = "io_get_variable_info", ReadOnly = true), Description(
        "Details of one or more process image variables: data type, bit size, bit address, direction, ADS index group/offset and links.")]
    public Task<CallToolResult> GetVariableInfo(
        [Description("Full variable paths, e.g. 'TIID^EtherCAT Master^Term 1 (EK1100)^Term 2 (EL1008)^Channel 1^Input'.")] string[] treePaths)
        => session.RunAsync(s =>
        {
            var links = MappingInfo.Parse(s.SysManager.ProduceMappingInfo());
            return treePaths.ToDictionary(p => p, p =>
            {
                try
                {
                    var v = IoTreeHelpers.TryGetVarInfo(s.Lookup(p))
                            ?? throw new TcException($"'{p}' is not a variable.") { Hint = "Use io_list_variables to find variable paths." };
                    return (object)new
                    {
                        v.Name,
                        v.Type,
                        v.BitSize,
                        v.BitAddress,
                        v.Direction,
                        ads = v.AdsIndexGroup is null ? null : new { port = v.AdsPort, indexGroup = $"0x{v.AdsIndexGroup:X8}", indexOffset = $"0x{v.AdsIndexOffset:X8}" },
                        linkedTo = MappingInfo.LinkedTo(links, v.Path).ToArray(),
                    };
                }
                catch (Exception ex)
                {
                    return new { error = ex.Message };
                }
            });
        });

    // ------------------------------------------------------------------ helpers

    internal static string DevicePath(string device)
    {
        var d = (device ?? "").Trim();
        return d.StartsWith("TIID^", StringComparison.OrdinalIgnoreCase) ? d : "TIID^" + d;
    }

    private static object BindAdapter(ITcSmTreeItem device, NetworkAdapter adapter)
    {
        device.ConsumeXml(IoXml.BuildAdapterXml(adapter.Description, adapter.DeviceName, adapter.Mac));
        var address = IoXml.ParseDeviceAddress(device.ProduceXml(false));
        return new
        {
            requested = new { adapter.Name, adapter.Description, adapter.Mac, adapter.DeviceName },
            applied = address,
            hint = adapter.TwinCatDriver ? null : "This adapter has no TwinCAT RT-Ethernet driver installed; real-time EtherCAT will not run on it (install it with TcRteInstall.exe).",
        };
    }

    internal static CallToolResult Wrap(Func<object?> work)
    {
        try
        {
            return ToolResult.Ok(work());
        }
        catch (Exception ex)
        {
            return ToolResult.Error(ex);
        }
    }
}
