namespace TwinCatMcp.Tests.Integration;

[Collection(MotionCollection.Name)]
[Trait("Category", "Integration")]
public sealed class MotionTcComTests(MotionFixture fx)
{
    // TcJobPool ships with every TwinCAT 3.1 installation (Config\Modules\TcJobPool.tmc).
    private const string JobPoolClassId = "{0102000C-0000-0000-F000-000000000064}";

    [Fact]
    public async Task Installed_module_classes_are_listed_from_the_tmc_files()
    {
        var r = await fx.Call("tccom_list_classes", new { filter = "TcJobPool", includeParameters = true });

        var c = r.GetProperty("classes").EnumerateArray().Single(x => x.GetProperty("name").GetString() == "TcJobPool");
        Assert.Equal(JobPoolClassId, c.GetProperty("classId").GetString());
        Assert.Contains("PoolId", MotionFixture.Strings(c.GetProperty("parameters")));
    }

    [Fact]
    public async Task Object_is_created_by_class_id_listed_parameterized_and_deleted()
    {
        var created = await fx.Call("tccom_create_object", new { name = "PoolA", classIdOrName = JobPoolClassId });
        var listed = await fx.Call("tccom_list_objects");
        var set = await fx.Call("tccom_set_parameters", new { obj = "PoolA", values = new Dictionary<string, string> { ["PoolId"] = "3" } });
        var got = await fx.Call("tccom_get_parameters", new { obj = "PoolA" });
        await fx.Call("tccom_delete_object", new { objects = new[] { "PoolA" } });
        var after = await fx.Call("tccom_list_objects");

        Assert.Equal("TIRC^TcCOM Objects^PoolA (TcJobPool)", created.GetProperty("path").GetString());
        Assert.Equal("TcJobPool", created.GetProperty("className").GetString());
        Assert.Equal(JobPoolClassId, created.GetProperty("classId").GetString());
        Assert.Contains(listed.GetProperty("objects").EnumerateArray(), o => o.GetProperty("path").GetString() == "TIRC^TcCOM Objects^PoolA (TcJobPool)");
        Assert.Equal("3", set.GetProperty("values").GetProperty("PoolId").GetString());
        Assert.Equal("3", got.GetProperty("parameters").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "PoolId").GetProperty("value").GetString());
        Assert.DoesNotContain(after.GetProperty("objects").EnumerateArray(), o => o.GetProperty("path").GetString()!.Contains("PoolA"));
    }

    [Fact]
    public async Task Object_is_created_by_class_name_and_assigned_to_a_task_context()
    {
        var task = await fx.Call("tree_create_child", new { parentPath = "TIRT", name = "TcComTask", subType = 0 });
        var taskXml = await fx.Call("tree_produce_xml", new { treePath = task.GetProperty("path").GetString() });
        var taskOid = System.Text.RegularExpressions.Regex.Match(taskXml.GetProperty("xml").GetString()!, "<ObjectId>#x([0-9a-fA-F]+)</ObjectId>").Groups[1].Value;

        var created = await fx.Call("tccom_create_object", new { name = "PoolB", classIdOrName = "TcJobPool" });
        var ctx = await fx.Call("tccom_set_context", new { obj = created.GetProperty("path").GetString(), taskPath = "TIRT^TcComTask" });
        var got = await fx.Call("tccom_get_parameters", new { obj = "PoolB" });

        Assert.Equal("#x" + taskOid.ToLowerInvariant().PadLeft(8, '0'), ctx.GetProperty("taskObjectId").GetString());
        Assert.Equal("#x" + taskOid.ToLowerInvariant().PadLeft(8, '0'), got.GetProperty("contexts")[0].GetProperty("taskObjectId").GetString());
    }
}
