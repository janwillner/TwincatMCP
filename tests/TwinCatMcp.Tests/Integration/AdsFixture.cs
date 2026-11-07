namespace TwinCatMcp.Tests.Integration;

/// <summary>
/// MCP server (stdio) + in-process symbolic ADS test server on the local AMS router.
/// Needs a running TwinCAT system service / router, no IDE and no PLC.
/// </summary>
public sealed class AdsFixture : McpServerFixture
{
    public AdsTestSymbolServer Server { get; private set; } = null!;

    public int Port => Server.Port;

    public override async Task InitializeAsync()
    {
        Server = AdsTestSymbolServer.Start();
        await base.InitializeAsync();
    }

    public override async Task DisposeAsync()
    {
        await base.DisposeAsync();
        try
        {
            Server.Disconnect();
        }
        catch
        {
            // ignore
        }

        Server.Dispose();
    }
}

[CollectionDefinition(Name)]
public sealed class AdsCollection : ICollectionFixture<AdsFixture>
{
    public const string Name = "ADS";
}
