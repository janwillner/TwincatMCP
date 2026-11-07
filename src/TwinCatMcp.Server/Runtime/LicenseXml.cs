namespace TwinCatMcp.Runtime;

/// <summary>
/// Manual licenses of a PLC project (nested project item, IECProjectDef/LicenseSettings). Verified on TwinCAT 3.1.4026.22:
/// Commands/AddLicenses (RemoveLicenses) accept &lt;License&gt;&lt;Guid&gt;{guid}&lt;/Guid&gt;&lt;/License&gt;; the list is returned as
/// Licenses/License/Guid (lower case, without braces).
/// </summary>
public static class LicenseXml
{
    public static string PlcLicenseCommand(string licenseGuid, bool remove)
    {
        var cmd = remove ? "RemoveLicenses" : "AddLicenses";
        return $"<TreeItem><IECProjectDef><LicenseSettings><Commands><{cmd}><License><Guid>{XmlText.Escape(licenseGuid)}</Guid></License></{cmd}>" +
               "</Commands></LicenseSettings></IECProjectDef></TreeItem>";
    }

    public static string[] ParsePlcLicenses(string nestedProjectXml)
        => XmlText.Root(nestedProjectXml).Element("IECProjectDef")?.Element("LicenseSettings")?.Element("Licenses")?.Elements("License")
               .Select(l => l.Str("Guid"))
               .Where(g => !string.IsNullOrWhiteSpace(g))
               .Select(g => Guid.TryParse(g, out var id) ? id.ToString("B").ToUpperInvariant() : g!)
               .ToArray()
           ?? [];
}
