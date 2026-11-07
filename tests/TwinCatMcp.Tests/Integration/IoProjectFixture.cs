namespace TwinCatMcp.Tests.Integration;

/// <summary>
/// XAE fixture for the I/O package: solution "McpIt" + PLC project "Plc1" (Standard PLC Template) with a GVL that declares
/// I/O variables (AT %I* / AT %Q*) and a successful build, so that "Plc1 Instance^PlcTask Inputs/Outputs" exist for linking.
/// </summary>
public sealed class IoProjectFixture : XaeProjectFixture
{
    public const string PlcInstance = "TIPC^Plc1^Plc1 Instance";
    public const string PlcInputs = PlcInstance + "^PlcTask Inputs";
    public const string PlcOutputs = PlcInstance + "^PlcTask Outputs";

    // {attribute 'linkalways'}: TwinCAT only compiles (and maps) GVLs that are referenced, unless forced.
    public const string GvlDeclaration =
        "{attribute 'linkalways'}\r\nVAR_GLOBAL\r\n" +
        "\tbIn1 AT %I* : BOOL;\r\n\tbIn2 AT %I* : BOOL;\r\n\tbIn3 AT %I* : BOOL;\r\n\tbIn4 AT %I* : BOOL;\r\n" +
        "\tbOut1 AT %Q* : BOOL;\r\n\tbOut2 AT %Q* : BOOL;\r\n\tbOut3 AT %Q* : BOOL;\r\n\tbOut4 AT %Q* : BOOL;\r\n" +
        "\tnAnalog AT %I* : INT;\r\nEND_VAR\r\n";

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await Call("tree_create_child", new { parentPath = "TIPC", name = PlcName, subType = 0, info = "Standard PLC Template" });
        await Call("tree_create_child", new { parentPath = PlcProjectPath + "^GVLs", name = "GVL_IO", subType = 615, info = GvlDeclaration });
        var build = await Call("build_solution", new { includeWarnings = false });
        if (!build.GetProperty("success").GetBoolean())
        {
            throw new InvalidOperationException("PLC build failed in fixture: " + build);
        }
    }
}

[CollectionDefinition(Name)]
public sealed class IoCollection : ICollectionFixture<IoProjectFixture>
{
    public const string Name = "XAE-IO";
}

/// <summary>Server only (no IDE) - for tools that do not need an XAE session (ESI catalog, network adapters).</summary>
[CollectionDefinition(Name)]
public sealed class IoServerOnlyCollection : ICollectionFixture<McpServerFixture>
{
    public const string Name = "IO-ServerOnly";
}
