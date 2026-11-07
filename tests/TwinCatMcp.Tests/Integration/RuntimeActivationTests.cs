namespace TwinCatMcp.Tests.Integration;

/// <summary>
/// Own IDE + project for the activation test; does nothing (no IDE start) unless TWINCAT_MCP_RUNTIME_TESTS=1.
/// Target: TWINCAT_MCP_RUNTIME_NETID, default = the Usermode Runtime instance TWINCAT_MCP_UMRT_INSTANCE (default 'UmRT_McpTest'),
/// which is started via rt_umrt_start.
/// </summary>
public sealed class RuntimeActivationFixture : RuntimeProjectFixture
{
    public const string InstanceVariable = "TWINCAT_MCP_UMRT_INSTANCE";

    public static string Instance => Environment.GetEnvironmentVariable(InstanceVariable) is { Length: > 0 } i ? i : "UmRT_McpTest";

    public override Task InitializeAsync() => RuntimeFactAttribute.Enabled ? base.InitializeAsync() : Task.CompletedTask;

    public override Task DisposeAsync() => RuntimeFactAttribute.Enabled ? base.DisposeAsync() : Task.CompletedTask;
}

[CollectionDefinition(Name, DisableParallelization = true)] // changes machine-wide state (library repositories / runtimes)
public sealed class RuntimeActivationCollection : ICollectionFixture<RuntimeActivationFixture>
{
    public const string Name = "RuntimeActivation";
}

[Collection(RuntimeActivationCollection.Name)]
[Trait("Category", "Runtime")]
public sealed class RuntimeActivationTests(RuntimeActivationFixture fx)
{
    [RuntimeFact]
    public async Task Activate_login_start_stop_and_back_to_config_mode()
    {
        // 1. Target: explicit Net ID, or the (started) Usermode Runtime instance.
        var netId = RuntimeFactAttribute.TargetNetId;
        if (netId is null)
        {
            var started = await fx.Call("rt_umrt_start", new { name = RuntimeActivationFixture.Instance, waitSeconds = 60 });
            Assert.True(started.GetProperty("reachable").GetBoolean(), started.ToString());
            netId = started.GetProperty("netId").GetString()!;
        }

        var target = await fx.Call("system_set_target", new { target = netId });
        Assert.Equal(netId, target.GetProperty("netId").GetString());

        // 2. Platform must match the runtime (Usermode Runtime = 'TwinCAT OS (x64)').
        var info = await fx.Call("system_get_info", new { netId });
        var platform = info.TryGetProperty("platform", out var p) && p.GetString() is { Length: > 0 } pl ? pl : "TwinCAT OS (x64)";
        await fx.Call("system_set_target_platform", new { platform });

        // 3. Build, activate, restart in Run mode.
        var build = await fx.Call("build_solution", new { includeWarnings = false });
        Assert.True(build.GetProperty("success").GetBoolean(), build.ToString());
        var activation = await fx.Call("system_activate_configuration", new { restart = true, waitSeconds = 90 });
        Assert.True(activation.GetProperty("runMode").GetBoolean(), activation.ToString());
        var state = await fx.Call("system_get_state");
        Assert.Equal("Run", state.GetProperty("ads").GetProperty("adsState").GetString());

        // 4. PLC online: login with download, start, stop, reset, logout.
        var login = await fx.Call("plc_online_login", new { plcProject = XaeProjectFixture.PlcName, mode = "download" });
        Assert.True(login.GetProperty("loggedIn").GetBoolean(), login.ToString());
        var run = await fx.Call("plc_online_start", new { plcProject = XaeProjectFixture.PlcName });
        Assert.Equal("RUN", run.GetProperty("appState").GetString());
        var stop = await fx.Call("plc_online_stop", new { plcProject = XaeProjectFixture.PlcName });
        Assert.Equal("STOP", stop.GetProperty("appState").GetString());
        await fx.Call("plc_online_reset", new { plcProject = XaeProjectFixture.PlcName, type = "cold" });
        var logout = await fx.Call("plc_online_logout", new { plcProject = XaeProjectFixture.PlcName });
        Assert.False(logout.GetProperty("loggedIn").GetBoolean());

        // 5. Leave the target in Config mode.
        var config = await fx.Call("system_set_config_mode", new { waitSeconds = 60 });
        Assert.True(config.GetProperty("result").GetProperty("reached").GetBoolean(), config.ToString());
    }
}
