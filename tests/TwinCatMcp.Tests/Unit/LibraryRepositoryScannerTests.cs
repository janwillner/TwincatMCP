using TwinCatMcp.Libraries;

namespace TwinCatMcp.Tests.Unit;

/// <summary>
/// Repository layout as found on this PC (C:\ProgramData\Beckhoff\TwinCAT\PlcEngineering\Managed Libraries)
/// and as created by ITcPlcLibraryManager.InstallLibrary into a fresh repository:
/// &lt;root&gt;\&lt;distributor&gt;\&lt;library&gt;\&lt;version&gt;\{&lt;file&gt;.library|.compiled-library|.compiled-library-ge33, projectinfo, dependencies, browsercache}
/// plus top-level files "cache", "profile", "LibraryCategoryBase.libcat.xml".
/// </summary>
public sealed class LibraryRepositoryScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "repo-scan-" + Guid.NewGuid().ToString("N")[..8]);

    public LibraryRepositoryScannerTests()
    {
        Lib(@"Beckhoff Automation GmbH\Tc2_MC2\3.3.69.0", "tc2_mc2.compiled-library-ge33");
        Lib(@"Beckhoff Automation GmbH\Tc2_MC2\3.3.72.0", "Tc2_MC2.compiled-library-ge33");
        Lib(@"Beckhoff Automation GmbH\Tc2_Standard\3.4.5.0", "Tc2_Standard.compiled-library-ge33");
        Lib(@"www.tcunit.org\TcUnit\1.3.1", "TcUnit.library");
        Lib(@"Acme\AcmeLib\1.2.3.4", "AcmeLib.compiled-library");
        Directory.CreateDirectory(Path.Combine(_root, @"Acme\Empty\1.0.0.0")); // no library file -> ignored
        File.WriteAllText(Path.Combine(_root, "cache"), "");
        File.WriteAllText(Path.Combine(_root, "LibraryCategoryBase.libcat.xml"), "<x/>");
    }

    private void Lib(string rel, string file)
    {
        var dir = Path.Combine(_root, rel);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, file), "x");
        File.WriteAllText(Path.Combine(dir, "projectinfo"), "x");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* ignore */ }
    }

    [Fact]
    public void Lists_every_installed_version_with_distributor_and_file_kind()
    {
        var libs = LibraryRepositoryScanner.Scan("System", _root);

        Assert.Equal(5, libs.Count);
        var tcunit = libs.Single(l => l.Name == "TcUnit");
        Assert.Equal("1.3.1", tcunit.Version);
        Assert.Equal("www.tcunit.org", tcunit.Distributor);
        Assert.Equal("library", tcunit.FileKind);
        Assert.Equal("System", tcunit.Repository);
        Assert.Equal("compiled-library", libs.Single(l => l.Name == "AcmeLib").FileKind);
        Assert.Equal("compiled-library", libs.First(l => l.Name == "Tc2_MC2").FileKind);
    }

    [Fact]
    public void Missing_root_yields_empty_list()
    {
        Assert.Empty(LibraryRepositoryScanner.Scan("X", Path.Combine(_root, "nope")));
    }

    [Fact]
    public void Filter_by_name_substring_distributor_and_latest_only()
    {
        var libs = LibraryRepositoryScanner.Scan("System", _root);

        var mc2 = LibraryRepositoryScanner.Filter(libs, nameContains: "mc2", distributor: null, latestOnly: true);

        var only = Assert.Single(mc2);
        Assert.Equal("3.3.72.0", only.Version);
        Assert.Equal(["AcmeLib"], LibraryRepositoryScanner.Filter(libs, null, "acme", false).Select(l => l.Name));
    }

    [Fact]
    public void Results_are_sorted_by_distributor_name_and_descending_version()
    {
        var libs = LibraryRepositoryScanner.Scan("System", _root);

        Assert.Equal(
            ["AcmeLib 1.2.3.4", "Tc2_MC2 3.3.72.0", "Tc2_MC2 3.3.69.0", "Tc2_Standard 3.4.5.0", "TcUnit 1.3.1"],
            libs.Select(l => $"{l.Name} {l.Version}"));
    }
}
