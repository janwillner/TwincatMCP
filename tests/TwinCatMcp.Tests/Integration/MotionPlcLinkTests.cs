namespace TwinCatMcp.Tests.Integration;

[Collection(MotionCollection.Name)]
[Trait("Category", "Integration")]
public sealed class MotionPlcLinkTests(MotionFixture fx)
{
    [Fact]
    public async Task Two_axes_are_linked_to_PLC_AXIS_REFs_and_the_solution_builds_without_errors()
    {
        await fx.EnsurePlcWithAxisRefsAsync();
        await fx.AddAxisAsync("PlcX");
        await fx.AddAxisAsync("PlcY");

        var refs = await fx.Call("motion_find_plc_axis_refs", new { plc = MotionFixture.McPlc });
        var x = await fx.Call("motion_link_axis_to_plc", new { axis = "PlcX", plcAxisRef = "GVL_Motion.axX" });
        var y = await fx.Call("motion_link_axis_to_plc", new { axis = "PlcY", plcAxisRef = "GVL_Motion.axY", plc = MotionFixture.McPlc });
        var build = await fx.Call("build_solution", new { includeWarnings = false }, TimeSpan.FromMinutes(10));

        var symbols = refs.GetProperty("axisRefs").EnumerateArray().Select(r => r.GetProperty("symbol").GetString()).ToList();
        Assert.Contains("GVL_Motion.axX", symbols);
        Assert.Contains("GVL_Motion.axY", symbols);
        Assert.Equal(2, x.GetProperty("linkCount").GetInt32());
        Assert.Equal(2, y.GetProperty("linkCount").GetInt32());
        Assert.EndsWith("GVL_Motion.axX.NcToPlc", x.GetProperty("links")[0][1].GetString());
        Assert.Equal(0, build.GetProperty("diagnostics").GetProperty("errorCount").GetInt32());
    }

    [Fact]
    public async Task Plc_link_survives_switching_the_axis_to_simulation_and_can_be_removed()
    {
        await fx.EnsurePlcWithAxisRefsAsync();
        var plcAxis = await fx.AddAxisAsync("SimLinked");

        var link = await fx.CallRaw("motion_link_axis_to_plc", new { axis = plcAxis, plcAxisRef = "GVL_Motion.axSim" });
        Assert.False(link.IsError, link.Json.ToString());
        var sim = await fx.Call("motion_set_axis_simulation", new { axes = new[] { plcAxis }, enabled = true });
        Assert.Equal(2, sim.GetProperty("axes")[0].GetProperty("plcLinkCount").GetInt32());
        var mapping = await fx.Call("tree_produce_xml", new { treePath = plcAxis }); // axis still exists at the same path
        var unlink = await fx.Call("motion_unlink_axis_from_plc", new { axis = plcAxis });

        Assert.Equal(plcAxis, mapping.GetProperty("treePath").GetString());
        Assert.Equal(0, unlink.GetProperty("linkCount").GetInt32());
    }

    [Fact]
    public async Task Unknown_AXIS_REF_gives_the_available_symbols_as_hint()
    {
        await fx.EnsurePlcWithAxisRefsAsync();
        await fx.AddAxisAsync("ErrAxis");

        var (json, isError) = await fx.CallRaw("motion_link_axis_to_plc", new { axis = "ErrAxis", plcAxisRef = "GVL_Motion.axUnknown" });

        Assert.True(isError);
        Assert.Contains("GVL_Motion.axX", json.GetProperty("hint").GetString());
    }
}
