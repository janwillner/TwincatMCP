using System.Text.Json;

namespace TwinCatMcp.Tests.Integration;

/// <summary>Real-time tasks and PLC task links (offline, no activation).</summary>
[Collection(RuntimeCollection.Name)]
[Trait("Category", "Integration")]
public sealed class TaskToolsTests(RuntimeProjectFixture fx)
{
    private static JsonElement FindTask(JsonElement list, string name)
        => list.EnumerateArray().Single(t => t.GetProperty("task").GetProperty("name").GetString() == name);

    [Fact]
    public async Task Created_task_has_the_requested_cycle_time_and_priority()
    {
        var created = await fx.Call("task_create", new { name = "ItFast", cycleTime = 1, unit = "ms", priority = 7 });

        Assert.Equal(10000u, created.GetProperty("task").GetProperty("cycleTime").GetProperty("ticks100ns").GetUInt32());
        var t = FindTask(await fx.Call("task_list"), "ItFast").GetProperty("task");
        Assert.Equal("TIRT^ItFast", t.GetProperty("path").GetString());
        Assert.Equal(7, t.GetProperty("priority").GetInt32());
        Assert.Equal(1.0, t.GetProperty("cycleTime").GetProperty("milliseconds").GetDouble());
        Assert.False(t.GetProperty("withImage").GetBoolean());
    }

    [Fact]
    public async Task Configure_changes_cycle_time_core_and_autostart()
    {
        await fx.Call("task_create", new { name = "ItConf", cycleTime = 10, unit = "ms" });

        var r = await fx.Call("task_configure", new { task = "ItConf", cycleTime = 2000, unit = "us", autoStart = false, cores = new[] { 0 } });

        var t = r.GetProperty("task");
        Assert.Equal(20000u, t.GetProperty("cycleTime").GetProperty("ticks100ns").GetUInt32());
        Assert.False(t.GetProperty("autoStart").GetBoolean());
        Assert.Equal([0], t.GetProperty("cores").EnumerateArray().Select(c => c.GetInt32()));
        Assert.Empty(r.GetProperty("warnings").EnumerateArray());
    }

    [Fact]
    public async Task Task_with_image_gets_the_image_sub_type()
    {
        var created = await fx.Call("task_create", new { name = "ItImage", withImage = true });

        Assert.True(created.GetProperty("task").GetProperty("withImage").GetBoolean());
    }

    [Fact]
    public async Task Plc_task_reference_can_be_linked_to_another_task_and_back()
    {
        await fx.Call("task_create", new { name = "ItPlcTask", cycleTime = 5, unit = "ms", priority = 15 });

        var linked = await fx.Call("task_link_plc", new { task = "ItPlcTask", plcTask = "PlcTask", plcProject = XaeProjectFixture.PlcName });

        Assert.Equal("TIRT^ItPlcTask", linked.GetProperty("linkedTask").GetString());
        var list = await fx.Call("task_list");
        Assert.Contains(fx.PlcTaskReferencePath, FindTask(list, "ItPlcTask").GetProperty("linkedPlcTasks").EnumerateArray().Select(e => e.GetString()));

        var back = await fx.Call("task_link_plc", new { task = "TIRT^PlcTask", plcTask = fx.PlcTaskReferencePath });
        Assert.Equal("TIRT^PlcTask", back.GetProperty("linkedTask").GetString());
    }

    [Fact]
    public async Task Deleted_task_disappears()
    {
        await fx.Call("task_create", new { name = "ItGone" });

        await fx.Call("task_delete", new { task = "ItGone" });

        var list = await fx.Call("task_list");
        Assert.DoesNotContain(list.EnumerateArray(), t => t.GetProperty("task").GetProperty("name").GetString() == "ItGone");
    }

    [Fact]
    public async Task Invalid_cycle_time_is_rejected_with_a_hint()
    {
        var (json, isError) = await fx.CallRaw("task_create", new { name = "ItBad", cycleTime = 150, unit = "ns" });

        Assert.True(isError);
        Assert.Contains("100 ns", json.GetProperty("error").GetString());
    }
}
