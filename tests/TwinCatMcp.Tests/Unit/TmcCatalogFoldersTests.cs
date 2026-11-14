using TwinCatMcp.Motion;

namespace TwinCatMcp.Tests.Unit;

/// <summary>TMC search folders: TwinCAT 4026 keeps the module repository below ProgramData, 4024 below the installation folder.</summary>
public sealed class TmcCatalogFoldersTests
{
    [Fact]
    public void Both_the_4026_and_the_4024_module_repository_are_searched()
    {
        var folders = TmcCatalog.DefaultFolders(@"C:\TwinCAT\3.1\", @"C:\ProgramData").ToList();

        Assert.Contains(@"C:\ProgramData\Beckhoff\TwinCAT\3.1\Repository", folders);
        Assert.Contains(@"C:\TwinCAT\3.1\Repository", folders);
    }

    [Fact]
    public void Config_and_custom_config_module_folders_of_the_installation_are_searched()
    {
        var folders = TmcCatalog.DefaultFolders(@"C:\TwinCAT\3.1\", @"C:\ProgramData").ToList();

        Assert.Contains(@"C:\TwinCAT\3.1\Config\Modules", folders);
        Assert.Contains(@"C:\TwinCAT\3.1\CustomConfig\Modules", folders);
    }
}
