namespace TwinCatMcp.Tests.Integration;

/// <summary>
/// Usermode Runtime lifecycle via MCP (no XAE needed). Starts/stops processes, therefore gated like the other runtime tests.
/// Uses the instance TWINCAT_MCP_UMRT_INSTANCE (default 'UmRT_McpTest'); creates it if missing and leaves it running.
/// </summary>
[Trait("Category", "Runtime")]
public sealed class RuntimeUmrtToolsTests(McpServerFixture fx) : IClassFixture<McpServerFixture>
{
    [RuntimeFact]
    public async Task Instance_can_be_stopped_and_started_and_answers_via_ADS()
    {
        var name = RuntimeActivationFixture.Instance;
        var list = await fx.Call("rt_umrt_list");
        if (!list.EnumerateArray().Any(i => i.GetProperty("instance").GetProperty("name").GetString() == name))
        {
            await fx.Call("rt_umrt_create", new { name });
        }

        var stopped = await fx.Call("rt_umrt_stop", new { name, waitSeconds = 20 });
        Assert.True(stopped.GetProperty("stopped").GetBoolean(), stopped.ToString());

        var started = await fx.Call("rt_umrt_start", new { name, waitSeconds = 60 });
        Assert.True(started.GetProperty("reachable").GetBoolean(), started.ToString());
        var netId = started.GetProperty("netId").GetString();

        var state = await fx.Call("system_get_state", new { netId });
        Assert.True(state.GetProperty("ads").GetProperty("reachable").GetBoolean());
        var info = await fx.Call("system_get_info", new { netId });
        Assert.Equal("TwinCAT System", info.GetProperty("deviceName").GetString());

        var again = await fx.Call("rt_umrt_list");
        var inst = again.EnumerateArray().Single(i => i.GetProperty("instance").GetProperty("name").GetString() == name).GetProperty("instance");
        Assert.True(inst.GetProperty("running").GetBoolean());
        Assert.False(inst.GetProperty("runsAsWindowsService").GetBoolean());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Creating_an_instance_with_an_invalid_name_is_rejected()
    {
        var (json, isError) = await fx.CallRaw("rt_umrt_create", new { name = "..\\evil" });

        Assert.True(isError);
        Assert.Contains("letters, digits", json.GetProperty("hint").GetString());
    }
}
