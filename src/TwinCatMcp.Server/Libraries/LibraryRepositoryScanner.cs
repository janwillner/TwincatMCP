namespace TwinCatMcp.Libraries;

public sealed record InstalledLibrary(string Repository, string Distributor, string Name, string Version, string FileKind, string File)
{
    public string DisplayName => new LibraryId(Name, Version, Distributor).DisplayName;
}

/// <summary>
/// Read-only scan of a PLC library repository folder. Layout (verified on 4026):
/// <c>&lt;root&gt;\&lt;distributor&gt;\&lt;library&gt;\&lt;version&gt;\&lt;file&gt;.library | .compiled-library | .compiled-library-ge33</c>.
/// </summary>
public static class LibraryRepositoryScanner
{
    /// <summary>Default "System" repository of TwinCAT 3.1.4026.</summary>
    public static string DefaultSystemRepositoryFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Beckhoff\TwinCAT\PlcEngineering\Managed Libraries");

    public static List<InstalledLibrary> Scan(string repositoryName, string rootFolder)
    {
        var result = new List<InstalledLibrary>();
        if (!Directory.Exists(rootFolder))
        {
            return result;
        }

        foreach (var distributorDir in SafeDirs(rootFolder))
        {
            foreach (var libDir in SafeDirs(distributorDir))
            {
                foreach (var versionDir in SafeDirs(libDir))
                {
                    var file = SafeFiles(versionDir).FirstOrDefault(f => Kind(f) is not null);
                    if (file is null)
                    {
                        continue;
                    }

                    result.Add(new InstalledLibrary(
                        repositoryName,
                        Path.GetFileName(distributorDir),
                        Path.GetFileName(libDir),
                        Path.GetFileName(versionDir),
                        Kind(file)!,
                        file));
                }
            }
        }

        return Sort(result);
    }

    public static List<InstalledLibrary> Filter(IEnumerable<InstalledLibrary> libs, string? nameContains, string? distributor, bool latestOnly)
    {
        var q = libs
            .Where(l => string.IsNullOrWhiteSpace(nameContains) || l.Name.Contains(nameContains.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(l => string.IsNullOrWhiteSpace(distributor) || l.Distributor.Contains(distributor.Trim(), StringComparison.OrdinalIgnoreCase));
        if (latestOnly)
        {
            q = q.GroupBy(l => (l.Distributor.ToUpperInvariant(), l.Name.ToUpperInvariant()))
                .Select(g => g.OrderByDescending(l => l.Version, Comparer<string>.Create((a, b) => LibraryVersion.Compare(a, b))).First());
        }

        return Sort(q);
    }

    private static List<InstalledLibrary> Sort(IEnumerable<InstalledLibrary> libs)
        => libs
            .OrderBy(l => l.Distributor, StringComparer.OrdinalIgnoreCase)
            .ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(l => l.Version, Comparer<string>.Create((a, b) => LibraryVersion.Compare(a, b)))
            .ToList();

    private static string? Kind(string file)
    {
        var name = Path.GetFileName(file).ToLowerInvariant();
        if (name.EndsWith(".library"))
        {
            return "library";
        }

        return name.Contains(".compiled-library") ? "compiled-library" : null;
    }

    private static IEnumerable<string> SafeDirs(string dir)
    {
        try
        {
            return Directory.GetDirectories(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<string> SafeFiles(string dir)
    {
        try
        {
            return Directory.GetFiles(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
