using System.Text.Json;

namespace TwinCatMcp.Tests.Integration;

/// <summary>
/// Read-only / reversible tests against the local TwinCAT system service (AMS port 10000).
/// Never changes the system's ADS state; the only route added is a temporary dummy route that is removed again.
/// </summary>
[Collection(AdsCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AdsSystemServiceTests(AdsFixture fx)
{
    [Fact]
    public async Task Reads_state_and_device_info_of_the_local_system_service()
    {
        var state = await fx.Call("ads_read_state", new { netId = "local", port = 10000 });
        var info = await fx.Call("ads_read_device_info", new { port = 10000 });

        Assert.False(string.IsNullOrEmpty(state.GetProperty("adsState").GetString()));
        Assert.Equal("TwinCAT System", info.GetProperty("name").GetString());
        Assert.StartsWith("3.1.", info.GetProperty("version").GetString());
    }

    [Fact]
    public async Task System_info_reports_version_platform_and_mode()
    {
        var r = await fx.Call("ads_get_system_info");

        Assert.StartsWith("3.1.40", r.GetProperty("twinCatVersion").GetString());
        Assert.False(string.IsNullOrEmpty(r.GetProperty("platform").GetString()));
        Assert.False(string.IsNullOrEmpty(r.GetProperty("netId").GetString()));
        Assert.False(string.IsNullOrEmpty(r.GetProperty("hostName").GetString()));
    }

    [Fact]
    public async Task Missing_port_gives_a_helpful_hint()
    {
        var (json, isError) = await fx.CallRaw("ads_read_state", new { netId = "local", port = 859 });

        Assert.True(isError);
        Assert.Contains("port", json.GetProperty("hint").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task System_service_state_change_requires_confirmation()
    {
        var (json, isError) = await fx.CallRaw("ads_write_control", new { netId = "local", port = 10000, state = "config" });

        Assert.True(isError);
        Assert.Contains("confirmSystemService", json.GetProperty("hint").GetString());
    }

    [Fact]
    public async Task Plc_runtime_probe_lists_answering_ports()
    {
        var r = await fx.Call("ads_list_plc_runtimes", new { ports = new[] { 851, fx.Port }, timeoutMs = 1000 });

        // the symbolic test server answers like a PLC runtime
        Assert.Contains(r.GetProperty("runtimes").EnumerateArray(), x => x.GetProperty("port").GetInt32() == fx.Port);
    }

    [Fact]
    public async Task File_write_read_list_and_delete_roundtrip()
    {
        // The system service may run under a service account (Usermode Runtime) that cannot see user profile folders,
        // so use ProgramData; the directory is created by the service (ensureDirectory) and removed again via ADS.
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "twincat-mcp-it-" + Guid.NewGuid().ToString("N")[..8]);
        var file = Path.Combine(dir, "hello.txt");
        try
        {
            await fx.Call("ads_write_file", new { path = file, content = "Hallo ADS\nZeile 2", allowLocalSystem = true });
            await fx.Call("ads_write_file", new { path = file, content = "!", append = true, allowLocalSystem = true });
            Assert.Equal("Hallo ADS\nZeile 2!", File.ReadAllText(file));

            var read = await fx.Call("ads_read_file", new { path = file });
            Assert.Equal("Hallo ADS\nZeile 2!", read.GetProperty("content").GetString());
            Assert.False(read.GetProperty("truncated").GetBoolean());

            var list = await fx.Call("ads_list_dir", new { path = dir });
            var entry = list.GetProperty("entries").EnumerateArray().Single();
            Assert.Equal("hello.txt", entry.GetProperty("name").GetString());
            Assert.Equal(18, entry.GetProperty("size").GetInt64());

            await fx.Call("ads_delete_file", new { path = file, allowLocalSystem = true });
            Assert.False(File.Exists(file));
            await fx.Call("ads_delete_file", new { path = dir, isDirectory = true, allowLocalSystem = true });
            Assert.False(Directory.Exists(dir));
        }
        finally
        {
            await fx.CallRaw("ads_delete_file", new { path = file, allowLocalSystem = true });
            await fx.CallRaw("ads_delete_file", new { path = dir, isDirectory = true, allowLocalSystem = true });
        }
    }

    [Fact]
    public async Task Lists_the_boot_folder()
    {
        var r = await fx.Call("ads_list_dir", new { path = "", root = "boot" });

        Assert.True(r.GetProperty("count").GetInt32() >= 0);
    }

    [Fact]
    public async Task Reading_a_missing_file_is_a_tool_error()
    {
        var (json, isError) = await fx.CallRaw("ads_read_file", new { path = @"C:\does\not\exist\nothing.txt" });

        Assert.True(isError);
        Assert.False(string.IsNullOrEmpty(json.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task Temporary_dummy_route_can_be_added_listed_and_removed()
    {
        const string name = "MCP_IT_DUMMY";
        try
        {
            await fx.Call("route_add", new { name, address = "10.255.255.2", routeNetId = "10.255.255.2.1.1", temporary = true });

            var routes = await fx.Call("route_list");
            var route = routes.GetProperty("routes").EnumerateArray().Single(r => r.GetProperty("name").GetString() == name);
            Assert.Equal("10.255.255.2.1.1", route.GetProperty("netId").GetString());
            Assert.True(route.GetProperty("temporary").GetBoolean());
        }
        finally
        {
            await fx.CallRaw("route_remove", new { name });
        }

        var after = await fx.Call("route_list");
        Assert.DoesNotContain(after.GetProperty("routes").EnumerateArray(), r => r.GetProperty("name").GetString() == name);
    }

    [Fact]
    public async Task Static_routes_file_can_be_read()
    {
        var (json, isError) = await fx.CallRaw("route_list", new { source = "file" });

        // A PC without StaticRoutes.xml returns a tool error with a hint - both are acceptable here.
        if (isError)
        {
            Assert.False(string.IsNullOrEmpty(json.GetProperty("hint").GetString()));
        }
        else
        {
            Assert.True(json.GetProperty("files").GetArrayLength() > 0);
        }
    }

    [Fact]
    public async Task Broadcast_search_completes_within_the_timeout()
    {
        var r = await fx.Call("route_broadcast_search", new { timeoutMs = 800 });

        Assert.Equal(JsonValueKind.Array, r.GetProperty("systems").ValueKind);
    }
}
