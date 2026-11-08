namespace TwinCatMcp.Tests.Integration;

/// <summary>Read-only ADS queries against this PC's TwinCAT system service (no XAE).</summary>
[Collection(PlainServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SystemInfoTests(McpServerFixture fx)
{
    [Fact]
    public async Task System_info_reports_the_same_platform_as_the_ads_system_info()
    {
        var ads = await fx.Call("ads_get_system_info", new { netId = "local" });
        var sys = await fx.Call("system_get_info", new { netId = "local" });

        var platform = ads.GetProperty("platform").GetString();
        Assert.False(string.IsNullOrEmpty(platform), ads.ToString());
        Assert.Equal(platform, sys.GetProperty("platform").GetString());
        Assert.Equal(ads.GetProperty("usermodeRuntime").GetBoolean(), sys.GetProperty("usermodeRuntime").GetBoolean());
        Assert.False(sys.TryGetProperty("note", out _), sys.ToString());
    }
}
