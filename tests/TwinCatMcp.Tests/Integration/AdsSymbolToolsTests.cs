using System.Text.Json;

namespace TwinCatMcp.Tests.Integration;

/// <summary>Symbol access against the in-process symbolic ADS test server (see <see cref="AdsTestSymbolServer"/>).</summary>
[Collection(AdsCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AdsSymbolToolsTests(AdsFixture fx)
{
    private static JsonElement Value(JsonElement read, string symbol) =>
        read.GetProperty("values").EnumerateArray().First(v => v.GetProperty("symbol").GetString() == symbol).GetProperty("value");

    [Fact]
    public async Task Connect_returns_device_info_and_state()
    {
        var r = await fx.Call("ads_connect", new { netId = "local", port = fx.Port });

        Assert.Equal("McpTestServer", r.GetProperty("deviceInfo").GetProperty("name").GetString());
        Assert.Equal("Run", r.GetProperty("state").GetProperty("adsState").GetString());

        var list = await fx.Call("ads_list_connections");
        Assert.Contains(list.GetProperty("connections").EnumerateArray(), c => c.GetProperty("port").GetInt32() == fx.Port);
    }

    [Fact]
    public async Task Browse_lists_symbols_with_types_and_paging()
    {
        var r = await fx.Call("ads_browse_symbols", new { netId = "local", port = fx.Port, filter = "MAIN.", includeTypes = true, limit = 3 });

        Assert.True(r.GetProperty("total").GetInt32() >= 10);
        Assert.Equal(3, r.GetProperty("symbols").GetArrayLength());
        Assert.True(r.GetProperty("hasMore").GetBoolean());

        var all = await fx.Call("ads_browse_symbols", new { netId = "local", port = fx.Port, filter = "*stData*", depth = 1 });
        var names = all.GetProperty("symbols").EnumerateArray().Select(s => s.GetProperty("name").GetString()).ToList();
        Assert.Contains("MAIN.stData", names);
        Assert.Contains("MAIN.stData.nCount", names);
    }

    [Fact]
    public async Task Symbol_info_describes_type_size_address_and_members()
    {
        var r = await fx.Call("ads_get_symbol_info", new { netId = "local", port = fx.Port, symbols = new[] { "MAIN.stData", "MAIN.eColor" } });
        var st = r.GetProperty("symbols")[0];

        Assert.Equal("ST_Data", st.GetProperty("type").GetString());
        Assert.Equal("Struct", st.GetProperty("category").GetString());
        Assert.Equal("0x4020", st.GetProperty("indexGroup").GetString());
        Assert.Contains(st.GetProperty("dataType").GetProperty("members").EnumerateArray(), m => m.GetProperty("name").GetString() == "fTemp");

        var en = r.GetProperty("symbols")[1];
        Assert.Contains(en.GetProperty("dataType").GetProperty("enumValues").EnumerateArray(), e => e.GetProperty("name").GetString() == "Green");
    }

    [Fact]
    public async Task Reads_primitive_struct_array_enum_string_and_time_in_one_sum_read()
    {
        // other tests in this collection change some values - set the ones asserted here
        fx.Server.SetRaw("MAIN.fValue", BitConverter.GetBytes(1.25));
        fx.Server.SetRaw("MAIN.tDelay", BitConverter.GetBytes(1500u));
        fx.Server.SetRaw("MAIN.eColor", BitConverter.GetBytes((short)2));
        var r = await fx.Call("ads_read_symbols", new
        {
            netId = "local",
            port = fx.Port,
            symbols = new[] { "MAIN.bFlag", "MAIN.fValue", "MAIN.sText", "MAIN.tDelay", "MAIN.stData", "MAIN.aMatrix", "MAIN.eColor", "GVL.wsText", "MAIN.stData.fTemp", "MAIN.aValues[2]" },
        });

        Assert.True(Value(r, "MAIN.bFlag").GetBoolean());
        Assert.Equal(1.25, Value(r, "MAIN.fValue").GetDouble());
        Assert.Equal("Hello PLC", Value(r, "MAIN.sText").GetString());
        Assert.Equal("T#1s500ms", Value(r, "MAIN.tDelay").GetString());
        Assert.True(Value(r, "MAIN.stData").GetProperty("bOk").GetBoolean());
        Assert.Equal(21.5, Value(r, "MAIN.stData").GetProperty("fTemp").GetDouble());
        Assert.Equal(6, Value(r, "MAIN.aMatrix")[1][2].GetInt32());
        Assert.Equal("Green", Value(r, "MAIN.eColor").GetProperty("name").GetString());
        Assert.Equal("Grüße", Value(r, "GVL.wsText").GetString());
        Assert.Equal(21.5, Value(r, "MAIN.stData.fTemp").GetDouble());
        Assert.Equal(20, Value(r, "MAIN.aValues[2]").GetInt32());
    }

    [Fact]
    public async Task Writes_values_converted_to_the_symbol_types()
    {
        var w = await fx.Call("ads_write_symbols", new
        {
            netId = "local",
            port = fx.Port,
            values = new Dictionary<string, object>
            {
                ["GVL.nSetpoint"] = 4711,
                ["MAIN.stData"] = new { nCount = 99, sName = "Pump" },
                ["MAIN.aValues[4]"] = -1,
                ["MAIN.eColor"] = "Blue",
                ["MAIN.tDelay"] = "T#2m",
            },
        });
        Assert.Equal(5, w.GetProperty("written").GetInt32());

        var r = await fx.Call("ads_read_symbols", new
        {
            netId = "local", port = fx.Port, symbols = new[] { "GVL.nSetpoint", "MAIN.stData", "MAIN.aValues", "MAIN.eColor", "MAIN.tDelay" },
        });
        Assert.Equal(4711, Value(r, "GVL.nSetpoint").GetInt32());
        Assert.Equal(99, Value(r, "MAIN.stData").GetProperty("nCount").GetInt32());
        Assert.Equal(21.5, Value(r, "MAIN.stData").GetProperty("fTemp").GetDouble()); // untouched member kept
        Assert.Equal("Pump", Value(r, "MAIN.stData").GetProperty("sName").GetString());
        Assert.Equal(-1, Value(r, "MAIN.aValues")[3].GetInt32());
        Assert.Equal(3, Value(r, "MAIN.eColor").GetProperty("value").GetInt32());
        Assert.Equal("T#2m", Value(r, "MAIN.tDelay").GetString());
    }

    [Fact]
    public async Task Unknown_symbol_returns_error_with_suggestions()
    {
        var (json, isError) = await fx.CallRaw("ads_read_symbols", new { netId = "local", port = fx.Port, symbols = new[] { "MAIN.nCountr" } });

        Assert.True(isError);
        Assert.Contains("MAIN.nCountr", json.GetProperty("error").GetString());
        Assert.Contains("MAIN.nCounter", json.GetProperty("hint").GetString());
    }

    [Fact]
    public async Task Invalid_value_is_reported_without_writing()
    {
        var (json, isError) = await fx.CallRaw("ads_write_symbols", new { netId = "local", port = fx.Port, values = new Dictionary<string, object> { ["MAIN.nCounter"] = 100000 } });

        Assert.True(isError);
        Assert.Contains("out of range", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Monitor_polls_a_time_series_with_statistics()
    {
        fx.Server.SetRaw("MAIN.fValue", BitConverter.GetBytes(1.0));
        var changer = Task.Run(async () =>
        {
            for (var i = 2; i <= 4; i++)
            {
                await Task.Delay(250);
                fx.Server.SetRaw("MAIN.fValue", BitConverter.GetBytes((double)i));
            }
        });

        var r = await fx.Call("ads_monitor", new { netId = "local", port = fx.Port, symbols = new[] { "MAIN.fValue" }, durationSeconds = 1.5, intervalMs = 50 });
        await changer;

        var s = r.GetProperty("series")[0];
        Assert.Equal("MAIN.fValue", s.GetProperty("symbol").GetString());
        Assert.True(s.GetProperty("changes").GetInt32() >= 3);
        Assert.Equal(1.0, s.GetProperty("min").GetDouble());
        Assert.Equal(4.0, s.GetProperty("max").GetDouble());
        Assert.True(s.GetProperty("samples").GetArrayLength() >= 4);
    }

    [Fact]
    public async Task Monitor_with_notifications_records_changes()
    {
        fx.Server.SetRaw("GVL.nSetpoint", BitConverter.GetBytes(0));
        var changer = Task.Run(async () =>
        {
            await Task.Delay(400);
            fx.Server.SetRaw("GVL.nSetpoint", BitConverter.GetBytes(7));
            await Task.Delay(200);
            fx.Server.SetRaw("GVL.nSetpoint", BitConverter.GetBytes(8));
        });

        var r = await fx.Call("ads_monitor", new { netId = "local", port = fx.Port, symbols = new[] { "GVL.nSetpoint" }, durationSeconds = 1.5, intervalMs = 10, mode = "notification" });
        await changer;

        var s = r.GetProperty("series")[0];
        Assert.Equal(8, s.GetProperty("last").GetInt32());
        Assert.True(s.GetProperty("changes").GetInt32() >= 2);
    }

    [Fact]
    public async Task Wait_for_value_returns_when_the_value_is_reached()
    {
        fx.Server.SetRaw("MAIN.nCounter", BitConverter.GetBytes((short)0));
        _ = Task.Run(async () =>
        {
            await Task.Delay(500);
            fx.Server.SetRaw("MAIN.nCounter", BitConverter.GetBytes((short)123));
        });

        var r = await fx.Call("ads_wait_for_value", new { netId = "local", port = fx.Port, symbol = "MAIN.nCounter", value = 123, timeoutSeconds = 10 });

        Assert.True(r.GetProperty("reached").GetBoolean());
        Assert.Equal(123, r.GetProperty("value").GetInt32());
    }

    [Fact]
    public async Task Wait_for_value_supports_comparison_and_times_out()
    {
        fx.Server.SetRaw("MAIN.fValue", BitConverter.GetBytes(5.0));
        var ok = await fx.Call("ads_wait_for_value", new { netId = "local", port = fx.Port, symbol = "MAIN.fValue", value = 4.5, @operator = ">", timeoutSeconds = 2 });
        Assert.True(ok.GetProperty("reached").GetBoolean());

        var timeout = await fx.Call("ads_wait_for_value", new { netId = "local", port = fx.Port, symbol = "MAIN.eColor", value = "Red", timeoutSeconds = 0.5 });
        Assert.False(timeout.GetProperty("reached").GetBoolean());
    }

    [Fact]
    public async Task Invokes_rpc_method()
    {
        var r = await fx.Call("ads_invoke_rpc", new { netId = "local", port = fx.Port, symbol = "MAIN.fbCalc", method = "Add", args = new { a = 2, b = 40 } });

        Assert.Equal(42, r.GetProperty("returnValue").GetInt32());
    }

    [Fact]
    public async Task Raw_access_by_index_group_and_offset()
    {
        var (ig, io, _) = fx.Server.AddressOf("GVL.nSetpoint");
        await fx.Call("ads_write_raw", new { netId = "local", port = fx.Port, indexGroup = $"0x{ig:X}", indexOffset = io.ToString(), data = "0A000000" });

        var r = await fx.Call("ads_read_raw", new { netId = "local", port = fx.Port, indexGroup = $"16#{ig:X}", indexOffset = $"0x{io:X}", length = 4 });

        Assert.Equal("0A000000", r.GetProperty("hex").GetString());
        Assert.Equal(10, BitConverter.ToInt32(fx.Server.GetRaw("GVL.nSetpoint")));
    }

    [Fact]
    public async Task Write_control_changes_the_state_of_a_non_system_port()
    {
        await fx.Call("ads_write_control", new { netId = "local", port = fx.Port, state = "stop" });
        var stopped = await fx.Call("ads_read_state", new { netId = "local", port = fx.Port });
        await fx.Call("ads_write_control", new { netId = "local", port = fx.Port, state = "run" });
        var running = await fx.Call("ads_read_state", new { netId = "local", port = fx.Port });

        Assert.Equal("Stop", stopped.GetProperty("adsState").GetString());
        Assert.Equal("Run", running.GetProperty("adsState").GetString());
    }
}
