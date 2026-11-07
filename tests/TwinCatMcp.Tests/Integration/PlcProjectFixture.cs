using System.Text.Json;

namespace TwinCatMcp.Tests.Integration;

/// <summary>
/// XAE fixture (solution "McpIt") plus a PLC project "Plc1" created through plc_create_project
/// from the Standard PLC Template. Shared by all PLC integration tests.
/// </summary>
public sealed class PlcProjectFixture : XaeProjectFixture
{
    public string Pous => PlcProjectPath + "^POUs";

    public string Duts => PlcProjectPath + "^DUTs";

    public string Gvls => PlcProjectPath + "^GVLs";

    public JsonElement CreatedProject { get; private set; }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        CreatedProject = await Call("plc_create_project", new { name = PlcName });
    }

    /// <summary>Unique object name per test to keep tests independent inside the shared project.</summary>
    public static string Unique(string prefix) => prefix + "_" + Guid.NewGuid().ToString("N")[..6];
}

[CollectionDefinition(Name)]
public sealed class PlcCollection : ICollectionFixture<PlcProjectFixture>
{
    public const string Name = "PLC";
}
