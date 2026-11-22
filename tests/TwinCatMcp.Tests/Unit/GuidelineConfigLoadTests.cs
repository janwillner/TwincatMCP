using TwinCatMcp.Guidelines;

namespace TwinCatMcp.Tests.Unit;

/// <summary>Loading the .twincat-mcp.json chain: extends, merge, provenance, errors.</summary>
public sealed class GuidelineConfigLoadTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("twincat-mcp-guidelines-").FullName;
    private readonly string _builtin;

    public GuidelineConfigLoadTests()
    {
        _builtin = Path.Combine(_dir, "builtin");
        Directory.CreateDirectory(_builtin);
    }

    public void Dispose() => Directory.Delete(_dir, true);

    private string Write(string relative, string json)
    {
        var path = Path.GetFullPath(Path.Combine(_dir, relative));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Project_values_override_the_base_and_remember_their_file()
    {
        var baseFile = Write("company/base.json", """
            { "name": "Company", "naming": { "typePrefixes": { "BOOL": "b_", "INT": "n_" } } }
            """);
        var project = Write(".twincat-mcp.json", """
            // comments and trailing commas are allowed
            { "extends": "company/base.json", "naming": { "typePrefixes": { "INT": "i_", }, }, }
            """);

        var set = GuidelineSet.Load(project, _builtin);

        Assert.Equal([baseFile, project], set.Files);
        Assert.Equal("i_", set.Effective["naming"]!["typePrefixes"]!["INT"]!.GetValue<string>());
        Assert.Equal("b_", set.Effective["naming"]!["typePrefixes"]!["BOOL"]!.GetValue<string>());
        Assert.Equal(project, set.Sources["naming.typePrefixes.INT"]);
        Assert.Equal(baseFile, set.Sources["naming.typePrefixes.BOOL"]);
        Assert.Equal("Company", set.Name);
        Assert.False(set.Effective.ContainsKey("extends"));
    }

    [Fact]
    public void Arrays_are_replaced_not_appended()
    {
        Write("base.json", """{ "exclude": ["A*", "B*"] }""");
        var project = Write(".twincat-mcp.json", """{ "extends": "base.json", "exclude": ["C*"] }""");

        var set = GuidelineSet.Load(project, _builtin);

        Assert.Equal(["C*"], set.Effective["exclude"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    [Fact]
    public void Builtin_names_resolve_to_the_shipped_example_files()
    {
        var shipped = Path.Combine(_builtin, ".twincat-mcp.Beckhoff.json");
        File.WriteAllText(shipped, """{ "name": "Beckhoff" }""");
        var project = Write(".twincat-mcp.json", """{ "extends": "builtin:beckhoff" }""");

        var set = GuidelineSet.Load(project, _builtin);

        Assert.Equal([shipped, project], set.Files);
        Assert.Equal("Beckhoff", set.Name);
    }

    [Fact]
    public void An_extends_cycle_is_reported()
    {
        Write("a.json", """{ "extends": ".twincat-mcp.json" }""");
        var project = Write(".twincat-mcp.json", """{ "extends": "a.json" }""");

        var ex = Assert.Throws<GuidelineConfigException>(() => GuidelineSet.Load(project, _builtin));

        Assert.Contains("cycle", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_missing_extends_target_is_reported_with_its_path()
    {
        var project = Write(".twincat-mcp.json", """{ "extends": "nowhere.json" }""");

        var ex = Assert.Throws<GuidelineConfigException>(() => GuidelineSet.Load(project, _builtin));

        Assert.Contains("nowhere.json", ex.Message);
    }

    [Fact]
    public void Broken_json_names_the_file()
    {
        var project = Write(".twincat-mcp.json", """{ "rules": { """);

        var ex = Assert.Throws<GuidelineConfigException>(() => GuidelineSet.Load(project, _builtin));

        Assert.Contains(".twincat-mcp.json", ex.Message);
    }

    [Fact]
    public void Unknown_top_level_keys_are_rejected_to_catch_typos()
    {
        var project = Write(".twincat-mcp.json", """{ "namming": {} }""");

        var ex = Assert.Throws<GuidelineConfigException>(() => GuidelineSet.Load(project, _builtin));

        Assert.Contains("namming", ex.Message);
    }

    [Fact]
    public void Unknown_rule_keys_and_severities_are_rejected()
    {
        var badKey = Write("a/.twincat-mcp.json", """{ "rules": { "naming.var.inptu": "error" } }""");
        var badSeverity = Write("b/.twincat-mcp.json", """{ "rules": { "naming.var.input": "fatal" } }""");

        Assert.Contains("naming.var.inptu", Assert.Throws<GuidelineConfigException>(() => GuidelineSet.Load(badKey, _builtin)).Message);
        Assert.Contains("fatal", Assert.Throws<GuidelineConfigException>(() => GuidelineSet.Load(badSeverity, _builtin)).Message);
    }

    [Fact]
    public void The_guide_markdown_is_read_relative_to_the_file_that_names_it()
    {
        Write("company/style.md", "# Company style");
        Write("company/base.json", """{ "guide": "style.md" }""");
        var project = Write(".twincat-mcp.json", """{ "extends": "company/base.json" }""");

        var set = GuidelineSet.Load(project, _builtin);

        Assert.Equal("# Company style", set.GuideMarkdown);
    }
}
