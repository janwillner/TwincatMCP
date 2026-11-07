using System.Security;
using System.Text;
using System.Xml.Linq;

namespace TwinCatMcp.Libraries;

/// <summary>
/// Library properties of a PLC project ("Project information": Company, Title, Version, ...) as exposed by
/// <c>ProduceXml()</c> / <c>ConsumeXml()</c> of the nested PLC project node:
/// <c>&lt;TreeItem&gt;&lt;IECProjectDef&gt;&lt;ProjectInfo&gt;...</c> (verified on 4026).
/// </summary>
public static class LibraryProjectInfoXml
{
    /// <summary>Writable fields, in the order TwinCAT emits them.</summary>
    public static readonly string[] Fields =
    [
        "Company", "Title", "Version", "LibraryCategories", "LibraryCategoryIds", "DefaultNamespace",
        "Author", "Description", "Released", "DocFormat", "AllowChecks", "LanguageModelAttribute",
    ];

    public static Dictionary<string, string?> Parse(string xml)
    {
        var info = XDocument.Parse(xml).Descendants("ProjectInfo").FirstOrDefault()
                   ?? throw new FormatException("XML does not contain <IECProjectDef><ProjectInfo> (not a nested PLC project node).");
        return info.Elements().ToDictionary(
            e => e.Name.LocalName,
            e => string.IsNullOrWhiteSpace(e.Value) ? null : e.Value.Trim());
    }

    /// <summary>Builds the ConsumeXml fragment. Null values are left unchanged, "" clears a field.</summary>
    public static string BuildConsumeXml(IReadOnlyDictionary<string, string?> values)
    {
        var sb = new StringBuilder("<TreeItem><IECProjectDef><ProjectInfo>");
        foreach (var (key, value) in values)
        {
            var field = Fields.FirstOrDefault(f => string.Equals(f, key, StringComparison.OrdinalIgnoreCase))
                        ?? throw new ArgumentException($"Unknown project info field '{key}'. Valid: {string.Join(", ", Fields)}.");
            if (value is null)
            {
                continue;
            }

            sb.Append('<').Append(field).Append('>').Append(SecurityElement.Escape(value)).Append("</").Append(field).Append('>');
        }

        return sb.Append("</ProjectInfo></IECProjectDef></TreeItem>").ToString();
    }
}
