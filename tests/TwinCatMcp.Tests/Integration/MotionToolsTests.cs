using System.Text.Json;

namespace TwinCatMcp.Tests.Integration;

[Collection(MotionCollection.Name)]
[Trait("Category", "Integration")]
public sealed class MotionToolsTests(MotionFixture fx)
{
    private const string TaskPath = MotionFixture.NcTaskPath;

    private Task EnsureTaskAsync() => fx.EnsureNcTaskAsync();

    private Task<string> AddAxisAsync(string name) => fx.AddAxisAsync(name);

    [Fact]
    public async Task Created_NC_task_gets_SAF_suffix_cycle_time_and_priority()
    {
        await EnsureTaskAsync();

        var list = await fx.Call("motion_list_axes", new { ncTask = MotionFixture.NcTask });

        var task = list.GetProperty("ncTasks")[0].GetProperty("task");
        Assert.Equal(TaskPath, task.GetProperty("path").GetString());
        Assert.Equal(1000, task.GetProperty("cycleTimeUs").GetInt32());
        Assert.Equal(5, task.GetProperty("priority").GetInt32());
    }

    [Fact]
    public async Task Changing_the_NC_task_cycle_time_is_read_back()
    {
        await EnsureTaskAsync();

        var r = await fx.Call("motion_set_nc_task", new { cycleTimeUs = 4000, priority = 7 });
        var back = await fx.Call("motion_set_nc_task", new { ncTask = MotionFixture.NcTask, cycleTimeUs = 1000, priority = 5 });

        Assert.Equal(4000, r.GetProperty("task").GetProperty("cycleTimeUs").GetInt32());
        Assert.Equal(7, r.GetProperty("task").GetProperty("priority").GetInt32());
        Assert.Equal(1000, back.GetProperty("task").GetProperty("cycleTimeUs").GetInt32());
    }

    [Fact]
    public async Task A_second_NC_task_is_rejected_with_a_hint()
    {
        await EnsureTaskAsync();

        var (json, isError) = await fx.CallRaw("motion_create_nc_task", new { name = "Another" });

        Assert.True(isError);
        Assert.Contains(TaskPath, json.GetProperty("error").GetString());
        Assert.Contains("motion_add_axis", json.GetProperty("hint").GetString());
    }

    [Fact]
    public async Task Two_axes_are_added_as_continuous_simulation_axes_and_listed()
    {
        await EnsureTaskAsync();

        var r = await fx.Call("motion_add_axis", new { names = new[] { "ListA", "ListB" }, ncTask = MotionFixture.NcTask });
        var list = await fx.Call("motion_list_axes", new { ncTask = MotionFixture.NcTask });

        Assert.Equal(2, r.GetProperty("created").GetArrayLength());
        var axes = list.GetProperty("ncTasks")[0].GetProperty("axes").EnumerateArray().ToList();
        var a = axes.Single(x => x.GetProperty("name").GetString() == "ListA");
        Assert.Equal(TaskPath + "^Axes^ListA", a.GetProperty("path").GetString());
        Assert.Equal("Continuous Axis", a.GetProperty("axisType").GetString());
        Assert.StartsWith("1:", a.GetProperty("encoder").GetString()); // 1 = simulation encoder
        Assert.Contains(axes, x => x.GetProperty("name").GetString() == "ListB");
    }

    [Fact]
    public async Task Other_axis_types_can_be_created()
    {
        await EnsureTaskAsync();

        var r = await fx.Call("motion_add_axis", new { names = new[] { "EncOnly" }, ncTask = MotionFixture.NcTask, axisType = "encoder" });

        Assert.Equal("Encoder Axis", r.GetProperty("created")[0].GetProperty("axisType").GetString());
    }

    [Fact]
    public async Task Axis_parameters_are_set_partially_and_read_back()
    {
        var axis = await AddAxisAsync("ParamAxis");

        var set = await fx.Call("motion_set_axis_parameters", new
        {
            axis,
            parameters = new Dictionary<string, object>
            {
                ["maxVelocity"] = 500,
                ["acceleration"] = 2000,
                ["deceleration"] = 2500,
                ["jerk"] = 20000,
                ["referenceVelocity"] = 600,
                ["softLimitMaxEnabled"] = true,
                ["softLimitMax"] = 800,
                ["positionLagMax"] = 2,
                ["unit"] = "mm",
            },
            feedPerRevolution = 10,
            incrementsPerRevolution = 65536,
        });
        var get = await fx.Call("motion_get_axis_parameters", new { axes = new[] { "ParamAxis" } });

        Assert.False(set.TryGetProperty("notApplied", out _), set.ToString());
        var p = get[0].GetProperty("parameters");
        Assert.Equal(500, p.GetProperty("maxVelocity").GetDouble());
        Assert.Equal(2000, p.GetProperty("acceleration").GetDouble());
        Assert.Equal(2500, p.GetProperty("deceleration").GetDouble());
        Assert.Equal(20000, p.GetProperty("jerk").GetDouble());
        Assert.Equal(600, p.GetProperty("referenceVelocity").GetDouble());
        Assert.True(p.GetProperty("softLimitMaxEnabled").GetBoolean());
        Assert.Equal(800, p.GetProperty("softLimitMax").GetDouble());
        Assert.Equal(2, p.GetProperty("positionLagMax").GetDouble());
        Assert.Equal(0.000152587890625, p.GetProperty("scalingFactor").GetDouble(), 15);
        // untouched values keep their defaults
        Assert.Equal(600, p.GetProperty("manualVelocityFast").GetDouble());
        Assert.True(p.GetProperty("positionLagMonitoring").GetBoolean());
    }

    [Fact]
    public async Task Unknown_axis_parameter_returns_an_error_with_valid_names()
    {
        var axis = await AddAxisAsync("BadParamAxis");

        var (json, isError) = await fx.CallRaw("motion_set_axis_parameters", new { axis, parameters = new Dictionary<string, object> { ["topSpeed"] = 1 } });

        Assert.True(isError);
        Assert.Contains("topSpeed", json.GetProperty("error").GetString());
        Assert.Contains("maxVelocity", json.GetProperty("hint").GetString());
    }

    [Fact]
    public async Task Raw_axis_xml_section_is_available()
    {
        var axis = await AddAxisAsync("XmlAxis");

        var r = await fx.Call("motion_get_axis_xml", new { axis, section = "Encoder" });

        Assert.StartsWith("<Encoder>", r.GetProperty("xml").GetString());
        Assert.Contains("<ScaleFactorNumerator>", r.GetProperty("xml").GetString());
    }

    [Fact]
    public async Task Simulation_mode_is_switched_on_and_off()
    {
        var axis = await AddAxisAsync("SimAxis");
        await fx.Call("motion_set_axis_parameters", new { axis, parameters = new Dictionary<string, object> { ["maxVelocity"] = 1234 } });

        var on = await fx.Call("motion_set_axis_simulation", new { axes = new[] { axis }, enabled = true });
        var read = await fx.Call("motion_get_axis_parameters", new { axes = new[] { axis }, includeSimulationMode = true });
        var off = await fx.Call("motion_set_axis_simulation", new { axes = new[] { axis }, enabled = false });

        Assert.True(on.GetProperty("axes")[0].GetProperty("simulationMode").GetBoolean());
        Assert.True(on.GetProperty("axes")[0].GetProperty("changed").GetBoolean());
        Assert.True(read[0].GetProperty("simulationMode").GetBoolean());
        Assert.Equal(1234, read[0].GetProperty("parameters").GetProperty("maxVelocity").GetDouble()); // parameters survive re-import
        Assert.Equal(axis, read[0].GetProperty("axis").GetProperty("path").GetString());
        Assert.False(off.GetProperty("axes")[0].GetProperty("simulationMode").GetBoolean());
    }

    [Fact]
    public async Task Axis_is_linked_to_an_EL7041_stepper_terminal_offline_and_unlinked_again()
    {
        var axis = await AddAxisAsync("StepperAxis");
        var master = await fx.Call("tree_create_child", new { parentPath = "TIID", name = "EcMotion", subType = 111 });
        var ek = await fx.Call("tree_create_child", new { parentPath = master.GetProperty("path").GetString(), name = "Term 1 (EK1100)", subType = 9099, info = "EK1100-0000-0017" });
        var el = await fx.Call("tree_create_child", new { parentPath = ek.GetProperty("path").GetString(), name = "Term 2 (EL7041)", subType = 9099, info = "EL7041-0000-0016" });
        var elPath = el.GetProperty("path").GetString()!;

        var link = await fx.Call("motion_link_axis_to_drive", new { axis, ioPath = elPath });
        var unlink = await fx.Call("motion_link_axis_to_drive", new { axis, ioPath = "" });

        var a = link.GetProperty("axis");
        Assert.Equal(elPath, a.GetProperty("ioLink").GetString());
        Assert.Equal("MDP-703", a.GetProperty("axisIoType").GetString());
        Assert.StartsWith("4:", a.GetProperty("encoder").GetString());
        Assert.StartsWith("24:", a.GetProperty("drive").GetString());
        Assert.True(link.GetProperty("ioLinkCount").GetInt32() > 10);
        Assert.False(unlink.GetProperty("axis").TryGetProperty("ioLink", out _), unlink.ToString());
        Assert.Equal(0, unlink.GetProperty("ioLinkCount").GetInt32());
    }

    [Fact]
    public async Task Axis_is_linked_to_the_second_channel_of_an_AX5203_servo_drive_offline()
    {
        var axis = await AddAxisAsync("ServoAxis");
        var master = await fx.Call("tree_create_child", new { parentPath = "TIID", name = "EcServo", subType = 111 });
        var drive = await fx.Call("tree_create_child", new { parentPath = master.GetProperty("path").GetString(), name = "Drive 1 (AX5203)", subType = 9099, info = "AX5203-0000-0214" });
        var drivePath = drive.GetProperty("path").GetString()!;

        var link = await fx.Call("motion_link_axis_to_drive", new { axis, ioPath = drivePath, channel = 1 });

        var a = link.GetProperty("axis");
        Assert.Equal(drivePath, a.GetProperty("ioLink").GetString());
        Assert.Equal(1, a.GetProperty("ioChannel").GetInt32());
        Assert.Equal("Sercos", a.GetProperty("axisIoType").GetString());
        Assert.StartsWith("9:", a.GetProperty("drive").GetString());
        Assert.True(link.GetProperty("ioLinkCount").GetInt32() > 0, link.ToString());
    }

    [Fact]
    public async Task Axes_are_removed()
    {
        await AddAxisAsync("Doomed");

        await fx.Call("motion_remove_axis", new { axes = new[] { "Doomed" } });
        var exists = await fx.Call("tree_exists", new { treePaths = new[] { TaskPath + "^Axes^Doomed" } });

        Assert.False(exists.GetProperty(TaskPath + "^Axes^Doomed").GetBoolean());
    }
}
