using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TCatSysManagerLib;
using TwinCatMcp.Ads;
using TwinCatMcp.Core;
using TwinCatMcp.Io;

namespace TwinCatMcp.Tools;

/// <summary>EtherCAT boxes/terminals, ESI catalog, slave identity, CoE startup list and topology.</summary>
[McpServerToolType]
public sealed class EcatTools(XaeSession session, AdsSessionManager ads)
{
    /// <summary>CoE SDO upload/download index group (ADS to the EtherCAT master, port = slave address).</summary>
    private const uint CoeSdoIndexGroup = 0xF302;

    [McpServerTool(Name = "ecat_search_catalog", ReadOnly = true), Description(
        "Searches the EtherCAT device description (ESI) catalog installed with TwinCAT by product type, description words, group or vendor. " +
        "Returns per type: description, vendor, product code, all revisions (product revision strings like 'EL1008-0000-0018' for Beckhoff, " +
        "identity strings 'V..._P..._R...' for other vendors) and the latest one. The first call parses the ESI files (cached afterwards). " +
        "Examples: query='EL1008', query='4Ch analog input 0-10V', query='EK1100', vendor='Beckhoff'.")]
    public async Task<CallToolResult> SearchCatalog(
        [Description("Words that must all occur in type, description or group (case-insensitive), e.g. 'EL31' or 'stepper 50V'. Empty = all (use vendor).")] string? query = null,
        [Description("Optional vendor filter: name substring ('Beckhoff') or vendor id (2 / 0x2).")] string? vendor = null,
        int maxResults = 20,
        [Description("Re-read the ESI files (e.g. after installing new ESI files).")] bool refresh = false)
    {
        try
        {
            var catalog = await EsiCatalogStore.GetAsync(refresh).ConfigureAwait(false);
            var hits = EsiCatalog.Search(catalog.Devices, query, vendor, Math.Clamp(maxResults, 1, 500));
            return ToolResult.Ok(new
            {
                catalog = new { devices = catalog.Devices.Count, files = catalog.FileCount, catalog.Directories, cached = catalog.FromDiskCache },
                count = hits.Count,
                results = hits,
            });
        }
        catch (Exception ex)
        {
            return ToolResult.Error(ex);
        }
    }

    [McpServerTool(Name = "ecat_add_box"), Description(
        "Adds one EtherCAT box/terminal (EK1100 coupler, ELxxxx terminal, EPxxxx box, drive, ...) below an EtherCAT master or a coupler/box. " +
        "The product may be a short type 'EL1008' (latest revision from the ESI catalog), a variant 'EL3102-0010', a full product revision " +
        "'EL1008-0000-0018' or an identity string 'V00000002_P03F03052_R00120000'. Item sub type is chosen automatically (9099; " +
        "special terminals like EL6001 = 9101). Default name follows XAE: 'Term 2 (EL1008)'.")]
    public Task<CallToolResult> AddBox(
        [Description("Parent path: EtherCAT master ('TIID^EtherCAT Master') or a box/coupler ('TIID^EtherCAT Master^Term 1 (EK1100)').")] string parent,
        [Description("Product, e.g. 'EK1100', 'EL1008', 'EL3102-0010', 'EL1008-0000-0018'.")] string product,
        [Description("Optional tree item name. Default 'Term N (TYPE)'.")] string? name = null,
        [Description("Optional: insert before this sibling box name.")] string? before = null)
        => RunWithCatalog(catalog => session.RunAsync(s => DescribeCreated(CreateBox(s.Lookup(parent), product, name, before, catalog)), TimeSpan.FromMinutes(2)));

    [McpServerTool(Name = "ecat_add_terminals"), Description(
        "Batch-adds EtherCAT boxes/terminals in order, e.g. products=['EK1100','EL1008','EL2008','EL3102','EL7041'] below 'TIID^EtherCAT Master'. " +
        "A coupler/junction (EK1xxx, CU11xx, BK1120) placed directly below a master becomes the parent of the following terminals, so a complete " +
        "station can be created in one call. Products are resolved through the ESI catalog (short names get the latest revision). " +
        "Returns the created items (path, product revision) and stops at the first error (reported with the items created so far).")]
    public Task<CallToolResult> AddTerminals(
        [Description("Parent path: EtherCAT master or coupler, e.g. 'TIID^EtherCAT Master' or 'TIID^EtherCAT Master^Term 1 (EK1100)'.")] string parent,
        [Description("Products in physical order, e.g. ['EK1100','EL1008','EL2008'].")] string[] products,
        [Description("Optional names (same order as products); missing/empty entries get the default 'Term N (TYPE)'.")] string[]? names = null)
        => RunWithCatalog(catalog => session.RunAsync(s =>
        {
            var current = s.Lookup(parent);
            var startedAtDevice = current.ItemType == IoTreeHelpers.ItemTypeDevice;
            var created = new List<object>();
            for (var i = 0; i < products.Length; i++)
            {
                ITcSmTreeItem box;
                try
                {
                    box = CreateBox(current, products[i], names is not null && i < names.Length ? names[i] : null, null, catalog);
                }
                catch (Exception ex) when (!XaeSession.IsSessionFatal(ex))
                {
                    return new { complete = false, created, failed = new { product = products[i], index = i, error = ex.Message } };
                }

                created.Add(DescribeCreated(box));
                if (startedAtDevice && current.ItemType == IoTreeHelpers.ItemTypeDevice && IsCoupler(products[i]))
                {
                    current = box;
                }
            }

            return (object)new { complete = true, created };
        }, TimeSpan.FromMinutes(10)));

    [McpServerTool(Name = "ecat_get_slave_info", ReadOnly = true), Description(
        "EtherCAT identity and addresses of one or more boxes/terminals: vendor id, product code, revision, serial number, product revision string, " +
        "EtherCAT (physical) address, auto-increment address, previous port and mailbox protocols (CoE/FoE/...).")]
    public Task<CallToolResult> GetSlaveInfo(
        [Description("Box paths, e.g. ['TIID^EtherCAT Master^Term 1 (EK1100)^Term 2 (EL1008)'].")] string[] treePaths)
        => session.RunAsync(s => treePaths.ToDictionary(p => p, p =>
        {
            try
            {
                var item = s.Lookup(p);
                var info = IoXml.ParseSlaveInfo(item.ProduceXml(false))
                           ?? throw new TcException($"'{p}' is not an EtherCAT slave.") { Hint = "Pass a box/terminal below an EtherCAT master." };
                var slave = item as ITcEtherCATSlave;
                return (object)new
                {
                    name = item.Name,
                    description = item.ItemSubTypeName,
                    vendorId = slave?.vendorId ?? info.VendorId,
                    productCode = $"0x{slave?.ProductCode ?? info.ProductCode:X8}",
                    revisionNo = $"0x{slave?.RevisionNumber ?? info.RevisionNo:X8}",
                    serialNo = slave?.SerialNumber ?? info.SerialNo,
                    info.ProductRevision,
                    identity = ProductType.IdentityString(info.VendorId, info.ProductCode, info.RevisionNo),
                    ethercatAddress = info.PhysAddr,
                    autoIncAddress = info.AutoIncAddr is { } a ? $"0x{a:X4}" : null,
                    previousPort = info.PreviousPhysAddr is null ? null : $"{info.PreviousPort} of {info.PreviousPhysAddr}",
                    mailbox = info.MailboxProtocols,
                };
            }
            catch (Exception ex) when (!XaeSession.IsSessionFatal(ex))
            {
                return new { error = ex.Message };
            }
        }));

    [McpServerTool(Name = "ecat_get_coe", ReadOnly = true), Description(
        "Reads the CoE startup list (init commands sent to the slave at state transitions, e.g. PS = Pre-Op->Safe-Op) of an EtherCAT slave. " +
        "fixed=true entries are generated by TwinCAT (PDO assignment) and cannot be changed. " +
        "Optionally reads CoE objects ONLINE from a running slave via ADS (needs an activated configuration with connected hardware).")]
    public async Task<CallToolResult> GetCoe(
        [Description("Slave path, e.g. 'TIID^EtherCAT Master^Term 1 (EK1100)^Term 5 (EL7041)'.")] string treePath,
        [Description("Optional online reads, e.g. ['0x8010:01', '0x1018:02'] (index:subindex hex).")] string[]? onlineObjects = null,
        [Description("Data type for decoding online values (UINT, UDINT, INT, REAL, ... or empty for raw).")] string? onlineDataType = null,
        [Description("Bytes to read per online object.")] int onlineSize = 4)
    {
        CoeContext ctx;
        try
        {
            ctx = await session.Sta.InvokeAsync(() =>
            {
                var item = session.Lookup(treePath);
                var xml = item.ProduceXml(false);
                var info = IoXml.ParseSlaveInfo(xml)
                           ?? throw new TcException($"'{treePath}' is not an EtherCAT slave.") { Hint = "Pass a box/terminal below an EtherCAT master." };
                var device = IoTreeHelpers.OwningDevice(item);
                var netId = device is null ? null : IoXml.ParseDeviceAddress(device.ProduceXml(false))?.AmsNetId;
                return new CoeContext(item.PathName, info, IoXml.ParseCoeInitCmds(xml), netId);
            }, XaeSession.DefaultTimeout).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return ToolResult.Error(ex);
        }

        if (onlineObjects is not { Length: > 0 })
        {
            return ToolResult.Ok(Shape(ctx, null));
        }

        return await ads.RunAsync(async () =>
        {
            var online = new List<object>();
            foreach (var obj in onlineObjects)
            {
                try
                {
                    var (index, sub) = ParseObject(obj);
                    if (ctx.NetId is null || ctx.Info.PhysAddr is null)
                    {
                        throw new TcException("EtherCAT master AMS Net ID / slave address unknown.");
                    }

                    var client = ads.Get(ctx.NetId, ctx.Info.PhysAddr.Value, 5000);
                    var result = await client.ReadAsync(CoeSdoIndexGroup, ((uint)index << 16) | (uint)sub, Math.Clamp(onlineSize, 1, 1024), CancellationToken.None).ConfigureAwait(false);
                    if (result.ErrorCode != TwinCAT.Ads.AdsErrorCode.NoError)
                    {
                        throw new TcException($"ADS error {result.ErrorCode} reading CoE {obj}.");
                    }

                    var hex = Convert.ToHexString(result.Data.ToArray()).ToLowerInvariant();
                    online.Add(new { @object = obj, data = hex, value = CoeValue.Decode(hex, onlineDataType) });
                }
                catch (Exception ex) when (!XaeSession.IsSessionFatal(ex))
                {
                    online.Add(new { @object = obj, error = ex.Message });
                }
            }

            return Shape(ctx, online);
        }).ConfigureAwait(false);
    }

    [McpServerTool(Name = "ecat_set_startup_parameter"), Description(
        "Adds, replaces or removes a CoE startup parameter (entry of the slave's CoE startup list, written at a state transition). " +
        "Example: EL7041 max current 0x8010:01 = 1500 (UINT, mA) at transition PS. An existing user entry with the same index, subindex " +
        "and transition is replaced. Values are encoded little-endian according to dataType (or pass dataType='HEX' with raw bytes).")]
    public Task<CallToolResult> SetStartupParameter(
        [Description("Slave path, e.g. 'TIID^EtherCAT Master^Term 1 (EK1100)^Term 5 (EL7041)'.")] string treePath,
        [Description("CoE index, hex '0x8010' or decimal.")] string index,
        [Description("Subindex, e.g. '1' or '0x01'.")] string subIndex,
        [Description("Value, e.g. '1500', '0x10', 'TRUE', '2.5' or raw hex with dataType='HEX'.")] string? value = null,
        [Description("BOOL, USINT, SINT, UINT, INT, UDINT, DINT, ULINT, LINT, BYTE, WORD, DWORD, LWORD, REAL, LREAL or HEX.")] string dataType = "UINT",
        [Description("State transition: PS (Pre-Op->Safe-Op, default), IP (Init->Pre-Op), SO (Safe-Op->Op), ... comma-separated for several.")] string transition = "PS",
        [Description("Optional comment shown in the startup list.")] string? comment = null,
        [Description("Remove the entry instead of setting it.")] bool remove = false,
        [Description("Use CoE complete access.")] bool completeAccess = false)
        => session.RunAsync(s =>
        {
            var item = s.Lookup(treePath);
            var xml = item.ProduceXml(false);
            if (IoXml.ParseSlaveInfo(xml) is not { } info || !info.MailboxProtocols.Contains("CoE", StringComparer.OrdinalIgnoreCase))
            {
                throw new TcException($"'{treePath}' is not an EtherCAT slave with CoE mailbox.") { Hint = "Simple terminals (EL1008, EL2008, EK1100) have no CoE." };
            }

            var idx = (int)ProductType.ParseEsiNumber(index);
            var sub = (int)ProductType.ParseEsiNumber(subIndex);
            int removed = 0;
            string fragment;
            if (remove)
            {
                (fragment, removed) = IoXml.RemoveCoeInitCmd(xml, idx, sub, transition);
            }
            else
            {
                if (value is null)
                {
                    throw new TcException("'value' is required unless remove=true.");
                }

                fragment = IoXml.UpsertCoeInitCmd(xml, new CoeInitCmd(transition.ToUpperInvariant(), idx, sub, CoeValue.Encode(value, dataType), comment, false, completeAccess, 0, 1));
            }

            item.ConsumeXml(fragment);
            var after = IoXml.ParseCoeInitCmds(item.ProduceXml(false));
            return new
            {
                treePath = item.PathName,
                removed = remove ? removed : (int?)null,
                entry = remove ? null : after.LastOrDefault(c => c.Index == idx && c.SubIndex == sub),
                startupList = after.Select(c => new { @object = c.IndexHex, c.Transition, c.Data, c.Comment, c.Fixed }),
            };
        });

    [McpServerTool(Name = "ecat_get_topology", ReadOnly = true), Description(
        "Returns the EtherCAT topology as a tree: masters -> couplers/boxes -> terminals, with product revision of every box. " +
        "details=true adds EtherCAT addresses and previous port (slower: reads the XML of every box).")]
    public Task<CallToolResult> GetTopology(
        [Description("Optional master name/path; empty = all EtherCAT masters.")] string? master = null,
        [Description("Also return EtherCAT addresses and identity (vendor/product/revision) per box.")] bool details = false)
        => session.RunAsync(s =>
        {
            var masters = string.IsNullOrWhiteSpace(master)
                ? TreeInfo.Children(s.Lookup("TIID")).Where(d => d.ItemSubType == IoTreeHelpers.EtherCatMasterSubType).ToList()
                : [s.Lookup(IoTools.DevicePath(master))];
            return masters.Select(m => new
            {
                name = m.Name,
                path = m.PathName,
                adapter = IoXml.ParseDeviceAddress(m.ProduceXml(false)) is { IsBound: true } a ? a.DeviceDesc ?? a.DeviceName : null,
                boxes = Boxes(m, details),
            }).ToArray();
        }, TimeSpan.FromMinutes(5));

    // ------------------------------------------------------------------ helpers

    private sealed record CoeContext(string Path, SlaveInfo Info, IReadOnlyList<CoeInitCmd> StartupList, string? NetId);

    private static object Shape(CoeContext ctx, List<object>? online) => new
    {
        treePath = ctx.Path,
        ctx.Info.ProductRevision,
        hasCoe = ctx.Info.MailboxProtocols.Contains("CoE", StringComparer.OrdinalIgnoreCase),
        startupList = ctx.StartupList.Select(c => new { @object = c.IndexHex, c.Transition, c.Data, c.Comment, c.Fixed, c.CompleteAccess }),
        online,
        onlineTarget = online is null ? null : new { netId = ctx.NetId, port = ctx.Info.PhysAddr },
    };

    private static (int Index, int SubIndex) ParseObject(string obj)
    {
        var parts = obj.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            throw new TcException($"Invalid CoE object '{obj}'.") { Hint = "Use 'index:subindex' in hex, e.g. '0x8010:01'." };
        }

        static int Hex(string t) => (int)ProductType.ParseEsiNumber(t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? t : "0x" + t);
        return (Hex(parts[0]), Hex(parts[1]));
    }

    /// <summary>Loads the ESI catalog (off the STA thread) before running a tool that needs it; tolerates a missing catalog.</summary>
    private static async Task<CallToolResult> RunWithCatalog(Func<IReadOnlyList<EsiDevice>?, Task<CallToolResult>> work)
    {
        IReadOnlyList<EsiDevice>? catalog;
        try
        {
            catalog = (await EsiCatalogStore.GetAsync().ConfigureAwait(false)).Devices;
        }
        catch
        {
            catalog = null; // TwinCAT resolves short names itself
        }

        return await work(catalog).ConfigureAwait(false);
    }

    private static ITcSmTreeItem CreateBox(ITcSmTreeItem parent, string product, string? name, string? before, IReadOnlyList<EsiDevice>? catalog)
    {
        if (string.IsNullOrWhiteSpace(product))
        {
            throw new TcException("Product must not be empty.");
        }

        var resolved = catalog is null ? null : EsiCatalog.Resolve(catalog, product);
        if (catalog is not null && resolved is null && !ProductType.IsProductName(product))
        {
            throw new TcException($"Product '{product}' not found in the ESI catalog.") { Hint = "Use ecat_search_catalog to find the type string." };
        }

        var info = resolved?.CreateInfo ?? product.Trim();
        var subType = ProductType.BoxSubType(resolved?.Type ?? product);
        var boxName = string.IsNullOrWhiteSpace(name) ? ProductType.DefaultBoxName(NextBoxNumber(parent), resolved?.Type ?? product) : name.Trim();
        try
        {
            return parent.CreateChild(boxName, subType, before ?? "", info);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            throw new TcException($"Creating '{product}' ({info}, sub type {subType}) below '{parent.PathName}' failed: {ex.Message}", ex)
            {
                Hint = resolved is null
                    ? "Check the product with ecat_search_catalog. The parent must be an EtherCAT master or a box with an E-Bus/EtherCAT port."
                    : "The parent must be an EtherCAT master or a coupler/box with a free port (e.g. EK1100). Terminals cannot be added below terminals without E-Bus out.",
            };
        }
    }

    /// <summary>XAE numbers boxes per EtherCAT segment: count all boxes of the owning master + 1.</summary>
    private static int NextBoxNumber(ITcSmTreeItem parent)
    {
        var device = parent.ItemType == IoTreeHelpers.ItemTypeDevice ? parent : IoTreeHelpers.OwningDevice(parent);
        if (device is null)
        {
            return 1;
        }

        var used = TreeInfo.Children(device).Count(IoTreeHelpers.IsBox);
        var names = TreeInfo.Children(device).Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var n = used + 1;
        while (names.Any(x => x.StartsWith($"Term {n} (", StringComparison.OrdinalIgnoreCase)))
        {
            n++;
        }

        return n;
    }

    private static bool IsCoupler(string product)
    {
        var t = ProductType.Parse(product).BaseName;
        return t.StartsWith("EK1", StringComparison.OrdinalIgnoreCase)
               || t.StartsWith("CU11", StringComparison.OrdinalIgnoreCase)
               || t.StartsWith("BK1", StringComparison.OrdinalIgnoreCase);
    }

    private static object DescribeCreated(ITcSmTreeItem box)
    {
        var slave = box as ITcEtherCATSlave;
        uint? revision = null, vendor = null, productCode = null;
        try
        {
            if (slave is not null)
            {
                revision = slave.RevisionNumber;
                vendor = slave.vendorId;
                productCode = slave.ProductCode;
            }
        }
        catch
        {
            // not available
        }

        var type = (box.ItemSubTypeName ?? "").Split(' ', 2)[0];
        return new
        {
            name = box.Name,
            path = box.PathName,
            description = box.ItemSubTypeName,
            productRevision = revision is not null && vendor == ProductType.BeckhoffVendorId && ProductType.IsProductName(type) ? ProductType.FormatRevision(type, revision.Value) : null,
            identity = revision is not null ? ProductType.IdentityString(vendor ?? 0, productCode ?? 0, revision.Value) : null,
        };
    }

    private static List<object> Boxes(ITcSmTreeItem parent, bool details)
        => IoTreeHelpers.DirectChildren(parent).Where(IoTreeHelpers.IsBox).Select(b =>
        {
            object? addresses = null;
            if (details && IoXml.ParseSlaveInfo(b.ProduceXml(false)) is { } info)
            {
                addresses = new { ethercatAddress = info.PhysAddr, previousPort = info.PreviousPhysAddr is null ? null : $"{info.PreviousPort} of {info.PreviousPhysAddr}" };
            }

            var created = DescribeCreated(b);
            return (object)new { box = created, details = addresses, children = Boxes(b, details) };
        }).ToList();
}
