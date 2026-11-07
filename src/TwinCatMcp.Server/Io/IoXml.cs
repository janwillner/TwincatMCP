using System.Globalization;
using System.Xml.Linq;
using TwinCatMcp.Core;

namespace TwinCatMcp.Io;

/// <summary>Process image variable (from ProduceXml of a TREEITEMTYPE_VAR item).</summary>
public sealed record VarInfo(
    string Name,
    string Path,
    string Type,
    int BitSize,
    int? BitAddress,
    string Direction,
    int? AdsPort,
    uint? AdsIndexGroup,
    uint? AdsIndexOffset);

/// <summary>EtherCAT slave identity + addresses (from the &lt;EtherCAT&gt;&lt;Slave&gt;&lt;Info&gt; XML of a box).</summary>
public sealed record SlaveInfo(
    string? Name,
    int? PhysAddr,
    int? AutoIncAddr,
    uint VendorId,
    uint ProductCode,
    uint RevisionNo,
    uint SerialNo,
    string? ProductRevision,
    string? PreviousPort,
    int? PreviousPhysAddr,
    IReadOnlyList<string> MailboxProtocols);

/// <summary>One CoE startup (init) command. Index/SubIndex as numbers, Data as little-endian hex.</summary>
public sealed record CoeInitCmd(
    string Transition,
    int Index,
    int SubIndex,
    string Data,
    string? Comment,
    bool Fixed,
    bool CompleteAccess,
    int Timeout,
    int Ccs)
{
    public string IndexHex => $"0x{Index:X4}:{SubIndex:X2}";
}

/// <summary>Address of an I/O device: target AMS Net ID and the bound network adapter (Pnp AddressInfo).</summary>
public sealed record DeviceAddress(string? AmsNetId, string? DeviceDesc, string? DeviceName, string? Mac)
{
    public bool IsBound => !string.IsNullOrEmpty(DeviceName) || (!string.IsNullOrEmpty(Mac) && Mac.Trim('0').Length > 0);
}

/// <summary>A device reported by a device scan (TIID ProduceXml: TreeItem/DeviceGrpDef/FoundDevices/Device).</summary>
public sealed record FoundDevice(string? Name, int SubType, string? SubTypeName, string? Adapter, string? AddressInfoXml);

/// <summary>Pure XML parsing/building for I/O tree items (no COM).</summary>
public static class IoXml
{
    /// <summary>XML that triggers a box scan below a device (ConsumeXml on the device).</summary>
    public const string ScanBoxesXml = "<TreeItem><DeviceDef><ScanBoxes>1</ScanBoxes></DeviceDef></TreeItem>";

    /// <summary>XML that triggers "Reload devices" on TIID (needed before scans on CX/K-bus devices).</summary>
    public const string ReloadDevicesXml = "<TreeItem><DeviceGrpDef><ReloadDevices>true</ReloadDevices></DeviceGrpDef></TreeItem>";

    public static VarInfo? ParseVarDef(string xml)
    {
        var root = XElement.Parse(xml);
        var def = root.Element("VarDef");
        if (def is null)
        {
            return null;
        }

        var ads = def.Element("AdsInfo");
        return new VarInfo(
            (string?)root.Element("ItemName") ?? "",
            (string?)root.Element("PathName") ?? "",
            ((string?)def.Element("VarType") ?? "").Trim(),
            IntOrNull(def.Element("VarBitSize")) ?? 0,
            IntOrNull(def.Element("VarBitAddr")),
            IntOrNull(def.Element("VarInOut")) == 1 ? "output" : "input",
            IntOrNull(ads?.Element("AmsAddress")?.Element("AmsPort")),
            UIntOrNull(ads?.Element("IndexGroup")),
            UIntOrNull(ads?.Element("IndexOffset")));
    }

    public static SlaveInfo? ParseSlaveInfo(string xml)
    {
        var slave = XElement.Parse(xml).Element("EtherCAT")?.Element("Slave");
        var info = slave?.Element("Info");
        if (info is null)
        {
            return null;
        }

        var prev = slave!.Elements("PreviousPort").FirstOrDefault(p => (string?)p.Attribute("Selected") is "true" or "1")
                   ?? slave.Elements("PreviousPort").FirstOrDefault();
        return new SlaveInfo(
            ((string?)info.Element("Name"))?.Trim(),
            IntOrNull(info.Element("PhysAddr")),
            IntOrNull(info.Element("AutoIncAddr")),
            UIntOrNull(info.Element("VendorId")) ?? 0,
            UIntOrNull(info.Element("ProductCode")) ?? 0,
            UIntOrNull(info.Element("RevisionNo")) ?? 0,
            UIntOrNull(info.Element("SerialNo")) ?? 0,
            (string?)info.Element("ProductRevision"),
            (string?)prev?.Element("Port"),
            IntOrNull(prev?.Element("PhysAddr")),
            slave.Element("Mailbox")?.Elements("Protocol").Select(p => p.Value).ToList() ?? []);
    }

    // ------------------------------------------------------------------ CoE startup list

    public static IReadOnlyList<CoeInitCmd> ParseCoeInitCmds(string xml)
        => CoeInitCmdsElement(XElement.Parse(xml))?.Elements("InitCmd").Select(ToCmd).ToList() ?? [];

    /// <summary>Builds a ConsumeXml fragment that sets the complete CoE startup list of a slave.</summary>
    public static string BuildCoeInitCmdsXml(IEnumerable<CoeInitCmd> cmds)
        => Wrap(new XElement("InitCmds", cmds.Select(ToElement)));

    /// <summary>
    /// Adds a startup entry (or replaces a non-fixed entry with the same transition/index/subindex) and returns the ConsumeXml
    /// fragment with the complete list. Existing entries are kept verbatim (incl. attributes such as AdaptAutomatically).
    /// </summary>
    public static string UpsertCoeInitCmd(string slaveXml, CoeInitCmd cmd)
    {
        var list = new XElement(CoeInitCmdsElement(XElement.Parse(slaveXml)) ?? new XElement("InitCmds"));
        var same = list.Elements("InitCmd").Where(e => Matches(ToCmd(e), cmd.Index, cmd.SubIndex, cmd.Transition)).ToList();
        if (same.Any(e => ToCmd(e).Fixed))
        {
            throw new TcException($"CoE entry {cmd.IndexHex} ({cmd.Transition}) is generated by TwinCAT (fixed, e.g. PDO assignment) and cannot be overwritten.")
            {
                Hint = "Change the PDO assignment / process data of the terminal instead, or use another index.",
            };
        }

        same.ForEach(e => e.Remove());
        list.Add(ToElement(cmd));
        return Wrap(list);
    }

    /// <summary>Removes non-fixed entries matching index/subindex (and transition if given). Returns the full list fragment.</summary>
    public static (string Xml, int Removed) RemoveCoeInitCmd(string slaveXml, int index, int subIndex, string? transition)
    {
        var list = new XElement(CoeInitCmdsElement(XElement.Parse(slaveXml)) ?? new XElement("InitCmds"));
        var victims = list.Elements("InitCmd").Where(e =>
        {
            var c = ToCmd(e);
            return !c.Fixed && Matches(c, index, subIndex, transition);
        }).ToList();
        victims.ForEach(e => e.Remove());
        return (Wrap(list), victims.Count);
    }

    private static bool Matches(CoeInitCmd c, int index, int subIndex, string? transition)
        => c.Index == index && c.SubIndex == subIndex
           && (string.IsNullOrWhiteSpace(transition) || string.Equals(c.Transition, transition.Trim(), StringComparison.OrdinalIgnoreCase));

    private static XElement? CoeInitCmdsElement(XElement root)
    {
        var slave = root.Name == "TreeItem" ? root.Element("EtherCAT")?.Element("Slave") : root;
        return slave?.Element("Mailbox")?.Element("CoE")?.Element("InitCmds");
    }

    private static string Wrap(XElement initCmds)
        => new XElement("TreeItem", new XElement("EtherCAT", new XElement("Slave", new XElement("Mailbox", new XElement("CoE", initCmds)))))
            .ToString(SaveOptions.DisableFormatting);

    private static CoeInitCmd ToCmd(XElement e) => new(
        string.Join(",", e.Elements("Transition").Select(t => t.Value.Trim())),
        IntOrNull(e.Element("Index")) ?? 0,
        IntOrNull(e.Element("SubIndex")) ?? 0,
        ((string?)e.Element("Data") ?? "").Trim().ToLowerInvariant(),
        ((string?)e.Element("Comment"))?.Trim() is { Length: > 0 } c ? c : null,
        Bool(e.Attribute("Fixed")),
        Bool(e.Attribute("CompleteAccess")),
        IntOrNull(e.Element("Timeout")) ?? 0,
        IntOrNull(e.Element("Ccs")) ?? 1);

    private static XElement ToElement(CoeInitCmd c)
    {
        var e = new XElement("InitCmd");
        if (c.Fixed) e.Add(new XAttribute("Fixed", "true"));
        if (c.CompleteAccess) e.Add(new XAttribute("CompleteAccess", "true"));
        foreach (var t in c.Transition.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            e.Add(new XElement("Transition", t.ToUpperInvariant()));
        }

        if (!string.IsNullOrEmpty(c.Comment)) e.Add(new XElement("Comment", new XCData(c.Comment)));
        e.Add(
            new XElement("Timeout", c.Timeout),
            new XElement("Ccs", c.Ccs),
            new XElement("Index", c.Index),
            new XElement("SubIndex", c.SubIndex),
            new XElement("Data", c.Data.ToLowerInvariant()));
        return e;
    }

    // ------------------------------------------------------------------ devices / adapters / scan

    public static DeviceAddress? ParseDeviceAddress(string deviceXml)
    {
        var def = XElement.Parse(deviceXml).Element("DeviceDef");
        if (def is null)
        {
            return null;
        }

        var pnp = def.Element("AddressInfo")?.Element("Pnp");
        return new DeviceAddress(
            (string?)def.Element("AmsNetId"),
            (string?)pnp?.Element("DeviceDesc"),
            (string?)pnp?.Element("DeviceName"),
            (string?)pnp?.Element("DeviceData"));
    }

    /// <summary>ConsumeXml fragment that binds a device to a network adapter (Pnp: description, \DEVICE\{GUID}, MAC).</summary>
    public static string BuildAdapterXml(string description, string deviceName, string mac)
        => new XElement("TreeItem", new XElement("DeviceDef", new XElement("AddressInfo", new XElement("Pnp",
                new XElement("DeviceDesc", description),
                new XElement("DeviceName", deviceName),
                new XElement("DeviceData", NormalizeMac(mac))))))
            .ToString(SaveOptions.DisableFormatting);

    /// <summary>"10-FF-E0-63-D7-15" / "10:ff:.." / "10FFE0.." -> "10ffe063d715".</summary>
    public static string NormalizeMac(string mac)
    {
        var hex = new string((mac ?? "").Where(Uri.IsHexDigit).ToArray()).ToLowerInvariant();
        if (hex.Length != 12)
        {
            throw new TcException($"Invalid MAC address '{mac}'.") { Hint = "Use 6 bytes, e.g. 00-01-05-69-BC-0A." };
        }

        return hex;
    }

    public static IReadOnlyList<FoundDevice> ParseFoundDevices(string tiidXml)
    {
        var found = XElement.Parse(tiidXml).Element("DeviceGrpDef")?.Element("FoundDevices");
        if (found is null)
        {
            return [];
        }

        return found.Elements("Device").Select(d =>
        {
            var address = d.Element("AddressInfo");
            return new FoundDevice(
                (string?)d.Element("ItemName"),
                IntOrNull(d.Element("ItemSubType")) ?? 0,
                (string?)d.Element("ItemSubTypeName"),
                (string?)address?.Element("Pnp")?.Element("DeviceDesc"),
                address?.ToString(SaveOptions.DisableFormatting));
        }).ToList();
    }

    public static string WrapDeviceDef(string innerXml) => $"<TreeItem><DeviceDef>{innerXml}</DeviceDef></TreeItem>";

    // ------------------------------------------------------------------ helpers

    private static bool Bool(XAttribute? a) => a is not null && (a.Value == "1" || a.Value.Equals("true", StringComparison.OrdinalIgnoreCase));

    private static int? IntOrNull(XElement? e)
        => e is not null && long.TryParse(e.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? unchecked((int)v)
            : e is not null && ProductType.TryParseEsiNumber(e.Value, out var u) ? unchecked((int)u) : null;

    private static uint? UIntOrNull(XElement? e)
        => e is not null && ProductType.TryParseEsiNumber(e.Value, out var v) ? v : null;
}
