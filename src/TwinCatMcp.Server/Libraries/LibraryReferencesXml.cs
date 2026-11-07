using System.Xml.Linq;

namespace TwinCatMcp.Libraries;

/// <summary>One entry below the "References" node of a PLC project: a library or a placeholder reference.</summary>
public sealed record LibraryReference(
    string Kind,
    string ItemName,
    string? PlaceholderName,
    string? LibraryName,
    string? Version,
    string? EffectiveVersion,
    string? Distributor,
    string? Namespace,
    string? DefaultResolution,
    string? Redirection,
    bool SystemLibrary,
    bool Unresolved,
    List<string>? Dependencies);

public sealed record LibraryRepository(int Index, string Name, string Folder);

public sealed record DevicePlaceholder(string Name, string DefaultResolution);

public sealed class AmbiguousLibraryException(string message) : Exception(message);

/// <summary>
/// Parsed form of <c>ITcSmTreeItem.ProduceXml()</c> on a PLC library manager ("References") node
/// (&lt;TreeItem&gt;&lt;PlcLibDef&gt; with Repositories, DevicePlaceholders, PlaceholderRedirections, References).
/// </summary>
public sealed class LibraryReferencesXml
{
    public required List<LibraryRepository> Repositories { get; init; }

    public required List<LibraryReference> References { get; init; }

    public required List<DevicePlaceholder> DevicePlaceholders { get; init; }

    /// <summary>Placeholder name -> library display name (set by SetEffectiveResolution / FreezePlaceholder).</summary>
    public required Dictionary<string, string> Redirections { get; init; }

    public static LibraryReferencesXml Parse(string xml, bool includeDependencies = false)
    {
        var def = XDocument.Parse(xml).Descendants("PlcLibDef").FirstOrDefault()
                  ?? throw new FormatException("XML does not contain a <PlcLibDef> element (not a PLC References node).");

        var redirections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in def.Element("PlaceholderRedirections")?.Elements("Redirection") ?? [])
        {
            var ph = Text(r, "Placeholder");
            var lib = Text(r, "Library");
            if (ph is not null && lib is not null)
            {
                redirections[ph] = lib;
            }
        }

        var repositories = (def.Element("Repositories")?.Elements("Repository") ?? [])
            .Select((r, i) => new LibraryRepository(i, Text(r, "Name") ?? "", Text(r, "RootFolder") ?? ""))
            .ToList();

        var devicePlaceholders = (def.Element("DevicePlaceholders")?.Elements("Placeholder") ?? [])
            .Select(p => new DevicePlaceholder(Text(p, "PlaceholderName") ?? "", FormatResolution(p.Element("DefaultResolution")) ?? ""))
            .ToList();

        var references = new List<LibraryReference>();
        foreach (var e in def.Element("References")?.Elements() ?? [])
        {
            var deps = includeDependencies ? Dependencies(e) : null;
            if (e.Name.LocalName == "PlaceholderReference")
            {
                var ph = Text(e, "PlaceholderName");
                var eff = e.Element("EffectiveResolution");
                references.Add(new LibraryReference(
                    Kind: "placeholder",
                    ItemName: Text(e, "LibItemName") ?? "#" + ph,
                    PlaceholderName: ph,
                    LibraryName: Text(eff, "LibraryName"),
                    Version: null,
                    EffectiveVersion: Text(eff, "Version"),
                    Distributor: Text(eff, "Distributor"),
                    Namespace: Text(e, "Namespace"),
                    DefaultResolution: Text(e, "DefaultResolution"),
                    Redirection: ph is not null && redirections.TryGetValue(ph, out var red) ? red : null,
                    SystemLibrary: IsTrue(e.Attribute("systemLibrary")?.Value),
                    Unresolved: Text(eff, "LibraryName") is null,
                    Dependencies: includeDependencies ? Dependencies(eff) : null));
            }
            else if (e.Name.LocalName == "Library")
            {
                references.Add(new LibraryReference(
                    Kind: "library",
                    ItemName: Text(e, "LibItemName") ?? Text(e, "LibraryName") ?? "",
                    PlaceholderName: null,
                    LibraryName: Text(e, "LibraryName"),
                    Version: Text(e, "Version"),
                    EffectiveVersion: Text(e, "EffectiveVersion"),
                    Distributor: Text(e, "Distributor"),
                    Namespace: Text(e, "Namespace"),
                    DefaultResolution: null,
                    Redirection: null,
                    SystemLibrary: IsTrue(e.Attribute("systemLibrary")?.Value),
                    Unresolved: Text(e, "EffectiveVersion") is null,
                    Dependencies: deps));
            }
        }

        return new LibraryReferencesXml
        {
            Repositories = repositories,
            References = references,
            DevicePlaceholders = devicePlaceholders,
            Redirections = redirections,
        };
    }

    /// <summary>
    /// Determines the arguments for <c>ITcPlcLibraryManager.RemoveReference(name, version, distributor)</c>.
    /// Placeholders are removed by placeholder name; libraries by name + effective version + distributor
    /// (passing "*" does not work - verified on 4026). Returns null when nothing matches.
    /// </summary>
    public LibraryId? FindForRemoval(string request)
    {
        var req = LibraryId.Parse(request.TrimStart('#'));
        var placeholder = References.FirstOrDefault(r => r.Kind == "placeholder" &&
            (Eq(r.PlaceholderName, req.Name) || Eq(r.ItemName, request.Trim())));
        if (placeholder is not null)
        {
            return new LibraryId(placeholder.PlaceholderName!, null, null);
        }

        var candidates = References
            .Where(r => r.Kind == "library" && (Eq(r.LibraryName, req.Name) || Eq(r.ItemName, request.Trim())))
            .Where(r => req.Distributor is null || Eq(r.Distributor, req.Distributor))
            .ToList();
        if (req.Version is not null)
        {
            var exact = candidates.Where(r => LibraryVersion.IsLatest(req.Version)
                    ? LibraryVersion.IsLatest(r.Version)
                    : Eq(r.Version, req.Version))
                .ToList();
            candidates = exact.Count > 0 || LibraryVersion.IsLatest(req.Version)
                ? exact
                : candidates.Where(r => Eq(r.EffectiveVersion, req.Version)).ToList();
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        if (candidates.Count > 1)
        {
            throw new AmbiguousLibraryException(
                $"'{request}' matches several references: {string.Join("; ", candidates.Select(c => c.ItemName))}. Specify the version, e.g. '{candidates[^1].ItemName}'.");
        }

        var lib = candidates[0];
        return lib.EffectiveVersion is null
            ? new LibraryId(lib.LibraryName!, null, null)
            : new LibraryId(lib.LibraryName!, lib.EffectiveVersion, lib.Distributor);
    }

    private static List<string>? Dependencies(XElement? e)
        => e?.Element("Dependencies")?.Elements("Dependency")
            .Select(d => d.Attribute("isPlaceholder") is not null
                ? FormatResolution(d.Element("EffectiveResolution")) ?? Text(d, "PlaceholderName") ?? "?"
                : FormatResolution(d) ?? "?")
            .ToList();

    private static string? FormatResolution(XElement? e)
    {
        var name = Text(e, "LibraryName");
        return name is null ? null : new LibraryId(name, Text(e, "Version"), Text(e, "Distributor")).DisplayName;
    }

    private static string? Text(XElement? e, string child)
    {
        var v = e?.Element(child)?.Value;
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }

    private static bool IsTrue(string? v) => string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);

    private static bool Eq(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
