using System.Text.Json;

namespace TwinCatMcp.Tests.Integration;

/// <summary>
/// Runs only against a real, running PLC:
///   TWINCAT_MCP_RUNTIME_TESTS=1, TWINCAT_MCP_PLC_NETID=&lt;net id or 'local'&gt;,
///   optional TWINCAT_MCP_PLC_PORT (default 851) and TWINCAT_MCP_PLC_WRITE_SYMBOL (a numeric PLC variable that may be
///   changed and is restored afterwards, e.g. MAIN.nTest).
/// All other tests are read-only.
/// </summary>
public sealed class PlcFactAttribute : FactAttribute
{
    public PlcFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("TWINCAT_MCP_RUNTIME_TESTS") != "1" ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TWINCAT_MCP_PLC_NETID")))
        {
            Skip = "Needs a running PLC: set TWINCAT_MCP_RUNTIME_TESTS=1 and TWINCAT_MCP_PLC_NETID (optional TWINCAT_MCP_PLC_PORT).";
        }
    }
}

[Collection(AdsCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Category", "Runtime")]
public sealed class AdsRuntimePlcTests(AdsFixture fx)
{
    private static string NetId => Environment.GetEnvironmentVariable("TWINCAT_MCP_PLC_NETID") ?? "local";

    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("TWINCAT_MCP_PLC_PORT"), out var p) ? p : 851;

    [PlcFact]
    public async Task Plc_is_in_run_and_has_device_info()
    {
        var state = await fx.Call("ads_read_state", new { netId = NetId, port = Port });
        var info = await fx.Call("ads_connect", new { netId = NetId, port = Port });

        Assert.Equal("Run", state.GetProperty("adsState").GetString());
        Assert.False(string.IsNullOrEmpty(info.GetProperty("deviceInfo").GetProperty("name").GetString()));
    }

    [PlcFact]
    public async Task Runtime_probe_finds_the_plc_with_its_project_name()
    {
        var r = await fx.Call("ads_list_plc_runtimes", new { netId = NetId });
        var plc = r.GetProperty("runtimes").EnumerateArray().Single(x => x.GetProperty("port").GetInt32() == Port);

        Assert.False(string.IsNullOrEmpty(plc.GetProperty("projectName").GetString()));
    }

    [PlcFact]
    public async Task App_info_struct_is_read_as_json_object()
    {
        var r = await fx.Call("ads_read_symbols", new { netId = NetId, port = Port, symbols = new[] { "TwinCAT_SystemInfoVarList._AppInfo", "TwinCAT_SystemInfoVarList._TaskInfo[1].CycleCount" } });
        var app = r.GetProperty("values")[0].GetProperty("value");

        Assert.Equal(Port, app.GetProperty("AdsPort").GetInt32());
        Assert.StartsWith("DT#", app.GetProperty("AppTimestamp").GetString());
        Assert.True(r.GetProperty("values")[1].GetProperty("value").GetInt64() > 0);
    }

    [PlcFact]
    public async Task Browsing_and_reading_all_top_level_primitives_succeeds()
    {
        var browse = await fx.Call("ads_browse_symbols", new { netId = NetId, port = Port, limit = 2000, refresh = true });
        Assert.True(browse.GetProperty("total").GetInt32() > 0);

        var primitives = browse.GetProperty("symbols").EnumerateArray()
            .Where(s => s.GetProperty("category").GetString() is "Primitive" or "Enum" or "String")
            .Select(s => s.GetProperty("name").GetString()!)
            .Take(200)
            .ToArray();
        if (primitives.Length == 0)
        {
            return;
        }

        var read = await fx.Call("ads_read_symbols", new { netId = NetId, port = Port, symbols = primitives });
        Assert.All(read.GetProperty("values").EnumerateArray(), v => Assert.False(v.TryGetProperty("error", out _), v.ToString()));
    }

    [PlcFact]
    public async Task Cycle_counter_increases_while_monitoring()
    {
        var r = await fx.Call("ads_monitor", new { netId = NetId, port = Port, symbols = new[] { "TwinCAT_SystemInfoVarList._TaskInfo[1].CycleCount" }, durationSeconds = 0.5, intervalMs = 100 });

        Assert.True(r.GetProperty("series")[0].GetProperty("changes").GetInt32() > 0);
    }

    [PlcFact]
    public async Task Optional_write_roundtrip_restores_the_value()
    {
        var symbol = Environment.GetEnvironmentVariable("TWINCAT_MCP_PLC_WRITE_SYMBOL");
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return; // writing is opt-in
        }

        var before = (await fx.Call("ads_read_symbols", new { netId = NetId, port = Port, symbols = new[] { symbol } })).GetProperty("values")[0].GetProperty("value");
        try
        {
            var target = before.GetDouble() + 1;
            await fx.Call("ads_write_symbols", new { netId = NetId, port = Port, values = new Dictionary<string, object> { [symbol] = target } });
            var wait = await fx.Call("ads_wait_for_value", new { netId = NetId, port = Port, symbol, value = target, timeoutSeconds = 2 });
            Assert.True(wait.GetProperty("reached").GetBoolean());
        }
        finally
        {
            await fx.Call("ads_write_symbols", new { netId = NetId, port = Port, values = new Dictionary<string, JsonElement> { [symbol] = before } });
        }
    }
}
