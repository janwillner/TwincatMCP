using System.Text.Json;

namespace TwinCatMcp.Tests.Integration;

[Collection(IoServerOnlyCollection.Name)]
[Trait("Category", "Integration")]
public sealed class IoCatalogTests(McpServerFixture fx)
{
    [Fact]
    public async Task Catalog_search_resolves_EL1008_with_its_installed_revisions()
    {
        var result = await fx.Call("ecat_search_catalog", new { query = "EL1008" }, TimeSpan.FromMinutes(5));

        var hit = result.GetProperty("results").EnumerateArray().First();
        Assert.Equal("EL1008", hit.GetProperty("type").GetString());
        Assert.Equal(2u, hit.GetProperty("vendorId").GetUInt32());
        // Latest revision in the TwinCAT 4026 ESI files (#x00120000), same as the XAE uses for 'EL1008'.
        Assert.Equal("EL1008-0000-0018", hit.GetProperty("latest").GetString());
        Assert.Contains("EL1008-0000-0016", hit.GetProperty("revisions").EnumerateArray().Select(r => r.GetString()));
        Assert.True(result.GetProperty("catalog").GetProperty("devices").GetInt32() > 1000);
    }

    [Fact]
    public async Task Catalog_search_by_description_words()
    {
        var result = await fx.Call("ecat_search_catalog", new { query = "stepper 50V", vendor = "Beckhoff", maxResults = 50 }, TimeSpan.FromMinutes(5));

        var types = result.GetProperty("results").EnumerateArray().Select(r => r.GetProperty("type").GetString()).ToList();
        Assert.Contains("EL7041", types);
    }

    [Fact]
    public async Task Network_adapters_are_listed_with_TwinCAT_device_names()
    {
        var adapters = await fx.Call("io_list_network_adapters", new { includeVirtual = true });

        Assert.Equal(JsonValueKind.Array, adapters.ValueKind);
        foreach (var a in adapters.EnumerateArray())
        {
            Assert.StartsWith(@"\DEVICE\{", a.GetProperty("deviceName").GetString());
            Assert.Matches("^([0-9A-F]{2}-){5}[0-9A-F]{2}$", a.GetProperty("mac").GetString()!);
        }
    }
}
