using TwinCatMcp.Libraries;

namespace TwinCatMcp.Tests.Unit;

/// <summary>
/// Fallback "System" library repository when no PLC project is open: TwinCAT 4026 keeps it below ProgramData,
/// TwinCAT 4024 below the installation folder (%TWINCAT3DIR%Components\Plc\Managed Libraries).
/// </summary>
public sealed class LibrarySystemRepositoryTests
{
    private const string ProgramData = @"C:\ProgramData";
    private const string TwinCatDir = @"C:\TwinCAT\3.1\";
    private const string Repo4026 = @"C:\ProgramData\Beckhoff\TwinCAT\PlcEngineering\Managed Libraries";
    private const string Repo4024 = @"C:\TwinCAT\3.1\Components\Plc\Managed Libraries";

    [Fact]
    public void The_4026_repository_is_used_when_it_exists()
    {
        var folder = LibraryRepositoryScanner.SystemRepositoryFolder(ProgramData, TwinCatDir, _ => true);

        Assert.Equal(Repo4026, folder);
    }

    [Fact]
    public void The_4024_repository_below_the_installation_folder_is_used_when_only_it_exists()
    {
        var folder = LibraryRepositoryScanner.SystemRepositoryFolder(ProgramData, TwinCatDir, dir => dir == Repo4024);

        Assert.Equal(Repo4024, folder);
    }

    [Fact]
    public void Without_any_repository_the_4026_location_is_reported()
    {
        var folder = LibraryRepositoryScanner.SystemRepositoryFolder(ProgramData, TwinCatDir, _ => false);

        Assert.Equal(Repo4026, folder);
    }
}
