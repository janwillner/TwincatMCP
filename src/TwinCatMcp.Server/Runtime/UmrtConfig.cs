using System.Globalization;
using System.Xml.Linq;
using TwinCatMcp.Core;

namespace TwinCatMcp.Runtime;

/// <summary>
/// Pure helpers for TC170x Usermode Runtime instance configuration. An instance is a folder below
/// C:\ProgramData\Beckhoff\TwinCAT\3.1\Runtimes (copied from Program Files\...\Runtimes\UmRT_Template) whose
/// 3.1\TcRegistry.xml holds HKLM\Software\Beckhoff\TwinCAT3\System\AmsNetId as a 6-byte BIN value.
/// </summary>
public static class UmrtConfig
{
    public static string NetIdFromBin(string hex)
    {
        hex = hex.Trim();
        if (hex.Length != 12)
        {
            throw new TcException($"AmsNetId BIN value '{hex}' must have 12 hex digits.");
        }

        return string.Join('.', Enumerable.Range(0, 6).Select(i => byte.Parse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
    }

    public static string NetIdToBin(string netId)
    {
        var parts = (netId ?? "").Trim().Split('.');
        var bytes = new byte[6];
        if (parts.Length != 6 || parts.Select((p, i) => byte.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out bytes[i])).Any(ok => !ok))
        {
            throw new TcException($"Invalid AMS Net ID '{netId}'.") { Hint = "Format: 6 bytes 0..255 separated by dots, e.g. 199.4.42.250.1.1." };
        }

        return Convert.ToHexString(bytes);
    }

    public static string? ReadNetId(string registryXml)
    {
        var value = SystemKey(XDocument.Parse(registryXml))?.Elements("Value")
            .FirstOrDefault(v => (string?)v.Attribute("Name") == "AmsNetId");
        return value is null || string.IsNullOrWhiteSpace(value.Value) ? null : NetIdFromBin(value.Value);
    }

    public static string WriteNetId(string registryXml, string netId)
    {
        var bin = NetIdToBin(netId);
        var doc = XDocument.Parse(registryXml, LoadOptions.PreserveWhitespace);
        var system = SystemKey(doc) ?? throw new TcException("TcRegistry.xml has no HKLM\\Software\\Beckhoff\\TwinCAT3\\System key.");
        var value = system.Elements("Value").FirstOrDefault(v => (string?)v.Attribute("Name") == "AmsNetId");
        if (value is null)
        {
            system.Add(new XElement("Value", new XAttribute("Name", "AmsNetId"), new XAttribute("Type", "BIN"), bin));
        }
        else
        {
            value.Value = bin;
        }

        return doc.Declaration is null ? doc.ToString(SaveOptions.DisableFormatting) : doc.Declaration + doc.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>Instance names become folder names below ProgramData\...\Runtimes: letters, digits, '_' and '-' only.</summary>
    public static void ValidateInstanceName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
        {
            throw new TcException($"Invalid Usermode Runtime instance name '{name}'.")
            {
                Hint = "Use 1-64 characters: letters, digits, '_' or '-', e.g. 'UmRT_McpTest'.",
            };
        }
    }

    private static XElement? SystemKey(XDocument doc)
        => doc.Root?.Descendants("Key").FirstOrDefault(k =>
            (string?)k.Attribute("Name") == "System" && (string?)k.Parent?.Attribute("Name") == "TwinCAT3");
}

public sealed record LicenseEntry(string LicenseId, string Name, string? OrderNo);

public sealed record LicenseFileInfo(string? SystemId, DateTime? IssueTime, DateTime? ExpireTime, bool Expired, List<LicenseEntry> Licenses);

/// <summary>Parses TwinCAT license response files (*.tclrs, e.g. Target\License\TrialLicense.tclrs).</summary>
public static class LicenseFile
{
    public static LicenseFileInfo Parse(string xml, DateTime now)
    {
        var info = XmlText.Root(xml).Element("LicenseInfo");
        var expire = Date(info.Str("ExpireTime"));
        return new LicenseFileInfo(
            info.Str("SystemId"),
            Date(info.Str("IssueTime")),
            expire,
            expire is { } e && now >= e,
            info?.Elements("License").Select(l => new LicenseEntry(l.Str("LicenseId") ?? "", l.Str("Name") ?? "", l.Str("OrderNo"))).ToList() ?? []);
    }

    /// <summary>
    /// Why the license files of a local runtime are not sufficient (null = ok). A PLC needs a TC12xx license (TC1200 PLC,
    /// TC1210 PLC/C++, ...). Used before restarting a local Usermode Runtime, because a missing license makes the XAE show a
    /// blocking "Target system reports a fatal error ... license not found" message box.
    /// </summary>
    public static string? MissingLicense(IReadOnlyList<LicenseFileInfo> files, bool needsPlc)
    {
        if (files.Count == 0)
        {
            return "no license file in 3.1\\Target\\License";
        }

        var valid = files.Where(f => !f.Expired).ToList();
        if (valid.Count == 0)
        {
            return "all license files are expired (" + string.Join(", ", files.Select(f => f.ExpireTime?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))) + ")";
        }

        if (needsPlc && !valid.SelectMany(f => f.Licenses).Any(l => l.OrderNo?.StartsWith("TC12", StringComparison.OrdinalIgnoreCase) == true))
        {
            return "no valid PLC license (TC1200)";
        }

        return null;
    }

    private static DateTime? Date(string? s)
        => DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d) ? d : null;
}
