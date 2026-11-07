using System.Xml.Linq;
using TwinCatMcp.Core;

namespace TwinCatMcp.Plc;

/// <summary>
/// Helpers for PLCopen XML files produced by ITcPlcIECProject.PlcOpenExport. TwinCAT stores the folder layout in
/// addData "projectstructure" (&lt;ProjectStructure&gt;&lt;Folder Name&gt;&lt;Object Name ObjectId/&gt;). On import with
/// bSubTree=true the folders are created/merged relative to the project root, which allows moving objects.
/// </summary>
public static class PlcOpenXml
{
    /// <summary>Wraps all top-level entries of the ProjectStructure into the folder chain (e.g. ["POUs","Sub"]).</summary>
    public static string PlaceInFolder(string xml, IReadOnlyList<string> folderChain)
    {
        var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var ps = ProjectStructure(doc);
        if (folderChain.Count == 0)
        {
            return doc.Declaration is null ? doc.ToString(SaveOptions.DisableFormatting) : doc.Declaration + doc.ToString(SaveOptions.DisableFormatting);
        }

        var ns = ps.Name.Namespace;
        var entries = ps.Elements().ToList();
        foreach (var e in entries)
        {
            e.Remove();
        }

        XElement? outer = null;
        XElement? inner = null;
        foreach (var name in folderChain)
        {
            var f = new XElement(ns + "Folder", new XAttribute("Name", name));
            if (outer is null)
            {
                outer = f;
            }
            else
            {
                inner!.Add(f);
            }

            inner = f;
        }

        inner!.Add(entries);
        ps.Add(outer);
        var text = doc.ToString(SaveOptions.DisableFormatting);
        return doc.Declaration is null ? text : doc.Declaration + text;
    }

    /// <summary>Lists the top-level objects (with folder path, '/' separated) contained in a PLCopen XML export.</summary>
    public static List<string> ListObjects(string xml)
    {
        var doc = XDocument.Parse(xml);
        var result = new List<string>();
        Walk(ProjectStructure(doc), "", result);
        return result;
    }

    private static void Walk(XElement parent, string prefix, List<string> result)
    {
        foreach (var e in parent.Elements())
        {
            var name = (string?)e.Attribute("Name") ?? "";
            if (e.Name.LocalName == "Folder")
            {
                Walk(e, prefix + name + "/", result);
            }
            else if (e.Name.LocalName == "Object")
            {
                result.Add(prefix + name);
            }
        }
    }

    private static XElement ProjectStructure(XDocument doc)
        => doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "ProjectStructure")
           ?? throw new TcException("The PLCopen XML has no TwinCAT ProjectStructure (addData projectstructure).")
           {
               Hint = "Only PLCopen XML exported by TwinCAT 3 (plc_export_plcopen) can be placed into folders.",
           };

    /// <summary>Maps conflict handling names to PLCIMPORTOPTIONS (NONE=0, REPLACE=1, RENAME=2, SKIP=3).</summary>
    public static int ParseImportOption(string? option) => (option ?? "error").Trim().ToLowerInvariant() switch
    {
        "error" or "none" or "fail" => 0,
        "replace" or "overwrite" => 1,
        "rename" => 2,
        "skip" => 3,
        _ => throw new TcException($"Unknown conflict option '{option}'.") { Hint = "Use error, replace, rename or skip." },
    };
}
