using System.Xml.Linq;
using TwinCatMcp.Core;

namespace TwinCatMcp.Motion;

public sealed record TcComClassInfo(
    string Name,
    string? ClassId,
    string? ClassFactory,
    string? Group,
    string? Vendor,
    List<string> Parameters,
    string SourceFile);

/// <summary>
/// Reads TcCOM module classes from TMC files (TcModuleClass/Modules/Module). TwinCAT looks for them in
/// %TWINCAT3DIR%\Config\Modules, %TWINCAT3DIR%\CustomConfig\Modules and the module repository
/// C:\ProgramData\Beckhoff\TwinCAT\3.1\Repository on 4026 or %TWINCAT3DIR%Repository on 4024 (vendor\module\version\*.tmc).
/// </summary>
public static class TmcCatalog
{
    public static List<TcComClassInfo> Parse(string tmcXml, string sourceFile)
    {
        var root = XDocument.Parse(tmcXml).Root;
        if (root is null)
        {
            return [];
        }

        var vendor = root.Element("Vendor")?.Element("Name")?.Value;
        return root.Element("Modules")?.Elements("Module").Select(m => new TcComClassInfo(
            Name: m.Element("Name")?.Value ?? "",
            ClassId: TcComXml.NormalizeGuid(m.Element("CLSID")?.Value) ?? TcComXml.NormalizeGuid(m.Attribute("GUID")?.Value),
            ClassFactory: NullIfEmpty(m.Element("CLSID")?.Attribute("ClassFactory")?.Value),
            Group: m.Attribute("Group")?.Value,
            Vendor: vendor,
            Parameters: m.Element("Parameters")?.Elements("Parameter").Select(p => p.Element("Name")?.Value ?? "").ToList() ?? [],
            SourceFile: sourceFile)).ToList() ?? [];
    }

    /// <summary>Default folders that contain TMC files on this PC.</summary>
    public static IEnumerable<string> DefaultFolders()
        => DefaultFolders(XaeSession.TwinCatDir, Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));

    /// <summary>
    /// TMC folders below the installation folder and ProgramData. The module repository lives below ProgramData on
    /// TwinCAT 3.1.4026 and below the installation folder (C:\TwinCAT\3.1\Repository) on 4024.
    /// </summary>
    public static IEnumerable<string> DefaultFolders(string twinCatDir, string commonAppData)
    {
        yield return Path.Combine(twinCatDir, "Config", "Modules");
        yield return Path.Combine(twinCatDir, "CustomConfig", "Modules");
        yield return Path.Combine(twinCatDir, "Repository");
        yield return Path.Combine(commonAppData, "Beckhoff", "TwinCAT", "3.1", "Repository");
        yield return Path.Combine(commonAppData, "Beckhoff", "TwinCAT", "3.1", "CustomConfig", "Modules");
    }

    /// <summary>Scans folders recursively; for duplicate class ids the newest file wins.</summary>
    public static List<TcComClassInfo> Scan(IEnumerable<string> folders)
    {
        var all = new List<(TcComClassInfo Info, DateTime Time)>();
        foreach (var folder in folders.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.tmc", SearchOption.AllDirectories))
            {
                try
                {
                    var time = File.GetLastWriteTimeUtc(file);
                    all.AddRange(Parse(File.ReadAllText(file), file).Select(c => (c, time)));
                }
                catch
                {
                    // ignore unreadable / foreign tmc files
                }
            }
        }

        return all
            .GroupBy(x => (x.Info.ClassId ?? x.Info.Name) + "|" + x.Info.Name)
            .Select(g => g.OrderByDescending(x => x.Info.ClassFactory is not null).ThenByDescending(x => x.Time).First().Info)
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
