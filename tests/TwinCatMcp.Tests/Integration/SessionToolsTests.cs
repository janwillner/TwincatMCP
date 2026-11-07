namespace TwinCatMcp.Tests.Integration;

[Collection(XaeCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SessionToolsTests(XaeProjectFixture fx)
{
    [Fact]
    public async Task Created_solution_is_open_and_its_TwinCAT_project_is_selected()
    {
        var status = await fx.Call("xae_status");

        Assert.True(status.GetProperty("connected").GetBoolean());
        Assert.True(status.GetProperty("solutionOpen").GetBoolean());
        Assert.Equal(XaeProjectFixture.SolutionName, status.GetProperty("project").GetProperty("name").GetString());
    }

    [Fact]
    public async Task New_project_exposes_the_standard_tree_roots()
    {
        var exists = await fx.Call("tree_exists", new { treePaths = new[] { "TIRC", "TIRT", "TIPC", "TINC", "TIID" } });

        foreach (var root in new[] { "TIRC", "TIRT", "TIPC", "TINC", "TIID" })
        {
            Assert.True(exists.GetProperty(root).GetBoolean(), root);
        }
    }

    [Fact]
    public async Task Started_IDE_is_a_dedicated_process_owned_by_the_server()
    {
        var status = await fx.Call("xae_status");

        Assert.True(status.GetProperty("ownsInstance").GetBoolean());
        var pid = status.GetProperty("processId").GetInt32();
        using var process = System.Diagnostics.Process.GetProcessById(pid);
        Assert.Equal("TcXaeShell", process.ProcessName);
    }

    [Fact]
    public async Task Looking_up_an_unknown_path_returns_a_tool_error_with_hint()
    {
        var (json, isError) = await fx.CallRaw("tree_get_item", new { treePath = "TIPC^DoesNotExist" });

        Assert.True(isError);
        Assert.Contains("not found", json.GetProperty("error").GetString());
        Assert.False(string.IsNullOrEmpty(json.GetProperty("hint").GetString()));
    }
}
