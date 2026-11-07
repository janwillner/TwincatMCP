namespace TwinCatMcp.Libraries;

/// <summary>
/// Identifies a PLC library by name, version and distributor (company / vendor).
/// TwinCAT's textual form ("display name") is <c>"Tc2_MC2, 3.3.72.0 (Beckhoff Automation GmbH)"</c>;
/// version <c>"*"</c> means "always the newest installed version".
/// </summary>
public sealed record LibraryId(string Name, string? Version, string? Distributor)
{
    /// <summary>"Name, Version (Distributor)" with "*" for a missing version.</summary>
    public string DisplayName => Distributor is null
        ? $"{Name}, {VersionOrStar}"
        : $"{Name}, {VersionOrStar} ({Distributor})";

    public string VersionOrStar => LibraryVersion.IsLatest(Version) ? "*" : Version!;

    /// <summary>
    /// Parses "Name", "Name, Version", "Name (Distributor)" or "Name, Version (Distributor)".
    /// </summary>
    public static LibraryId Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Library name must not be empty.", nameof(text));
        }

        var s = text.Trim();
        string? distributor = null;
        if (s.EndsWith(')'))
        {
            var open = s.LastIndexOf('(');
            if (open > 0)
            {
                distributor = NullIfEmpty(s[(open + 1)..^1]);
                s = s[..open].Trim();
            }
        }

        string? version = null;
        var comma = s.LastIndexOf(',');
        if (comma >= 0)
        {
            version = NullIfEmpty(s[(comma + 1)..]);
            s = s[..comma].Trim();
        }

        return new LibraryId(s, version, distributor);
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public static class LibraryVersion
{
    /// <summary>True for "*", "latest", empty or null - all meaning "newest installed version".</summary>
    public static bool IsLatest(string? version)
        => string.IsNullOrWhiteSpace(version) || version.Trim() == "*" || version.Trim().Equals("latest", StringComparison.OrdinalIgnoreCase);

    /// <summary>Numeric comparison of dotted versions ("3.3.9.0" &lt; "3.3.10.0"); missing parts count as 0.</summary>
    public static int Compare(string? a, string? b)
    {
        var pa = Parts(a);
        var pb = Parts(b);
        for (var i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            var x = i < pa.Length ? pa[i] : 0;
            var y = i < pb.Length ? pb[i] : 0;
            if (x != y)
            {
                return x.CompareTo(y);
            }
        }

        return 0;
    }

    /// <summary>Returns the entry with the highest version whose name matches (case-insensitive), or null.</summary>
    public static LibraryId? PickNewest(IEnumerable<LibraryId> candidates, string name)
        => candidates
            .Where(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.Version, Comparer<string?>.Create(Compare))
            .FirstOrDefault();

    private static long[] Parts(string? v)
        => (v ?? "").Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => long.TryParse(p, out var n) ? n : 0)
            .ToArray();
}
