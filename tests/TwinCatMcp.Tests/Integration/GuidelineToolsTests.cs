using System.Text.Json;

namespace TwinCatMcp.Tests.Integration;

/// <summary>
/// Coding guidelines over the MCP surface with a real XAE: the solution gets a .twincat-mcp.json that extends the shipped
/// Syntegon example, and the pou_* tools enforce it. Each test removes the config again (shared PLC fixture).
/// </summary>
[Collection(PlcCollection.Name)]
[Trait("Category", "Integration")]
public sealed class GuidelineToolsTests(PlcProjectFixture fx)
{
    private string ConfigFile => Path.Combine(fx.WorkDir, XaeProjectFixture.SolutionName, ".twincat-mcp.json");

    private async Task WithConfig(string json, Func<Task> test)
    {
        await File.WriteAllTextAsync(ConfigFile, json);
        try
        {
            await test();
        }
        finally
        {
            File.Delete(ConfigFile);
        }
    }

    private Task WithSyntegon(Func<Task> test) => WithConfig("""{ "extends": "builtin:syntegon" }""", test);

    private static string[] Rules(JsonElement json)
        => json.TryGetProperty("guidelineViolations", out var v) ? v.EnumerateArray().Select(x => x.GetProperty("rule").GetString()!).ToArray() : [];

    [Fact]
    public Task Guidelines_get_shows_the_active_config() => WithSyntegon(async () =>
    {
        var info = await fx.Call("guidelines_get", new { includeGuide = false });

        Assert.True(info.GetProperty("active").GetBoolean());
        Assert.Contains("Syntegon", info.GetProperty("name").GetString());
        Assert.Equal("error", info.GetProperty("rules").GetProperty("naming.var.methodLocal").GetString());
    });

    [Fact]
    public Task Creating_a_function_block_inserts_the_header_and_warns_about_the_description() => WithSyntegon(async () =>
    {
        var name = PlcProjectFixture.Unique("_FB_Gl");

        var created = await fx.Call("pou_create", new { name, type = "FUNCTION_BLOCK" });

        Assert.Contains("© Syntegon Group.<br>", created.GetProperty("declaration").GetString());
        Assert.Contains("header inserted into the declaration", created.GetProperty("guidelineFixes").EnumerateArray().Select(f => f.GetString()));
        Assert.Equal(["header.description"], Rules(created));
    });

    [Fact]
    public Task A_name_against_a_warn_rule_is_created_with_a_warning() => WithSyntegon(async () =>
    {
        var name = PlcProjectFixture.Unique("FB_Warned");

        var created = await fx.Call("pou_create", new { name, type = "FUNCTION_BLOCK" });

        Assert.Contains("naming.pou.functionBlock", Rules(created));
    });

    [Fact]
    public Task A_project_override_to_error_rejects_the_create() => WithConfig(
        """{ "extends": "builtin:syntegon", "rules": { "naming.pou.functionBlock": "error" } }""", async () =>
    {
        var name = PlcProjectFixture.Unique("FB_Wrong");

        var (result, isError) = await fx.CallRaw("pou_create", new { name, type = "FUNCTION_BLOCK" });

        Assert.True(isError);
        Assert.Equal(["naming.pou.functionBlock"], Rules(result));
        var found = await fx.Call("plc_find_elements", new { namePattern = name });
        Assert.Equal(0, found.GetProperty("count").GetInt32());
    });

    [Fact]
    public Task A_method_local_without_temp_is_rejected_with_a_suggestion() => WithSyntegon(async () =>
    {
        var fb = (await fx.Call("pou_create", new { name = PlcProjectFixture.Unique("_FB_Gl"), type = "FUNCTION_BLOCK" })).GetProperty("path").GetString()!;

        var (result, isError) = await fx.CallRaw("pou_create_method", new
        {
            parentPath = fb,
            name = "m_GetAngle",
            returnType = "LREAL",
            declaration = "METHOD m_GetAngle : LREAL\nVAR\n\tlr_Angle : LREAL;\nEND_VAR\n",
        });

        Assert.True(isError);
        var violation = result.GetProperty("guidelineViolations").EnumerateArray().Single(v => v.GetProperty("severity").GetString() == "error");
        Assert.Equal("lr_TempAngle", violation.GetProperty("suggestion").GetString());
        var read = await fx.Call("pou_read_code", new { paths = new[] { fb } });
        Assert.False(read.GetProperty("objects")[0].TryGetProperty("members", out var members) && members.ValueKind == JsonValueKind.Array);
    });

    [Fact]
    public Task Replace_code_rejects_a_new_input_without_prefix_and_writes_nothing() => WithConfig(
        """{ "extends": "builtin:syntegon", "rules": { "naming.var.input": "error" } }""", async () =>
    {
        var fb = (await fx.Call("pou_create", new { name = PlcProjectFixture.Unique("_FB_Gl"), type = "FUNCTION_BLOCK" })).GetProperty("path").GetString()!;
        var before = (await fx.Call("pou_read_code", new { paths = new[] { fb } })).GetProperty("objects")[0].GetProperty("declaration").GetString()!;

        var (result, isError) = await fx.CallRaw("pou_replace_code", new { path = fb, oldText = "VAR_INPUT", newText = "VAR_INPUT\n\tbStart : BOOL;", section = "declaration" });

        Assert.True(isError);
        Assert.Equal("vi_b_Start", result.GetProperty("guidelineViolations")[0].GetProperty("suggestion").GetString());
        var after = (await fx.Call("pou_read_code", new { paths = new[] { fb } })).GetProperty("objects")[0].GetProperty("declaration").GetString();
        Assert.Equal(before, after);

        var ok = await fx.Call("pou_replace_code", new { path = fb, oldText = "VAR_INPUT", newText = "VAR_INPUT\n\tvi_b_Start : BOOL;", section = "declaration" });
        Assert.Empty(Rules(ok));
    });

    [Fact]
    public Task Guidelines_check_audits_existing_code() => WithSyntegon(async () =>
    {
        var check = await fx.Call("guidelines_check", new { path = fx.Pous + "^MAIN" });

        Assert.True(check.GetProperty("objects").GetInt32() >= 1);
        Assert.True(check.GetProperty("findings").GetArrayLength() >= 0);
    });

    [Fact]
    public Task A_broken_config_blocks_code_changes() => WithConfig("""{ "rules": { "no.such.rule": "error" } }""", async () =>
    {
        var name = PlcProjectFixture.Unique("FB_Blocked");

        var (result, isError) = await fx.CallRaw("pou_create", new { name, type = "FUNCTION_BLOCK" });

        Assert.True(isError);
        Assert.Contains("no.such.rule", result.GetProperty("error").GetString());
        Assert.Equal(0, (await fx.Call("plc_find_elements", new { namePattern = name })).GetProperty("count").GetInt32());
    });

    [Fact]
    public async Task Without_config_nothing_is_checked()
    {
        var name = PlcProjectFixture.Unique("AnyName");

        var created = await fx.Call("pou_create", new { name, type = "FUNCTION_BLOCK" });

        Assert.Empty(Rules(created));
        Assert.False(created.TryGetProperty("guidelineFixes", out _));
    }
}
