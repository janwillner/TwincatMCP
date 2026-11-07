namespace TwinCatMcp.Tests.Integration;

/// <summary>
/// XAE fixture for the library/packaging tests: solution "McpIt" plus a PLC project "Plc1" created from the
/// "Standard PLC Template" (the plc_ tools are built by another package, so the project is created via tree_create_child,
/// as documented in InfoSys "Accessing, creating and handling PLC projects").
/// </summary>
public sealed class LibraryFixture : XaeProjectFixture
{
    public string ReferencesPath => PlcProjectPath + "^References";

    public string OutDir => Path.Combine(WorkDir, "out");

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Directory.CreateDirectory(OutDir);
        await Call("tree_create_child", new { parentPath = "TIPC", name = PlcName, subType = 0, info = "Standard PLC Template" }, TimeSpan.FromMinutes(3));
    }

    /// <summary>A fresh, not yet existing file path below the fixture's output folder.</summary>
    public string NewPath(string fileName) => Path.Combine(OutDir, Guid.NewGuid().ToString("N")[..6], fileName);
}

[CollectionDefinition(Name)]
public sealed class LibraryCollection : ICollectionFixture<LibraryFixture>
{
    public const string Name = "XAE-Libraries";
}
