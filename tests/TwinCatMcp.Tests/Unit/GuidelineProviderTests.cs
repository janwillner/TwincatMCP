using TwinCatMcp.Guidelines;

namespace TwinCatMcp.Tests.Unit;

/// <summary>On-demand loading per solution: no config, reload on change, broken config.</summary>
public sealed class GuidelineProviderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("twincat-mcp-provider-").FullName;
    private readonly string _sln;
    private readonly GuidelineProvider _provider;

    public GuidelineProviderTests()
    {
        _sln = Path.Combine(_dir, "Machine.sln");
        File.WriteAllText(_sln, "");
        _provider = new GuidelineProvider(Path.Combine(_dir, "builtin"));
    }

    public void Dispose() => Directory.Delete(_dir, true);

    private void WriteConfig(string json, DateTime stamp)
    {
        var file = Path.Combine(_dir, ".twincat-mcp.json");
        File.WriteAllText(file, json);
        File.SetLastWriteTimeUtc(file, stamp);
    }

    [Fact]
    public void A_solution_without_config_has_no_guidelines()
    {
        Assert.Null(_provider.For(_sln));
        Assert.Null(_provider.For(null));
    }

    [Fact]
    public void A_changed_config_is_read_again()
    {
        WriteConfig("""{ "name": "first" }""", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal("first", _provider.For(_sln)!.Name);

        WriteConfig("""{ "name": "second" }""", new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal("second", _provider.For(_sln)!.Name);
    }

    [Fact]
    public void A_changed_guide_is_read_again()
    {
        var guide = Path.Combine(_dir, "style.md");
        File.WriteAllText(guide, "old");
        WriteConfig("""{ "guide": "style.md" }""", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal("old", _provider.For(_sln)!.GuideMarkdown);

        File.WriteAllText(guide, "new");
        File.SetLastWriteTimeUtc(guide, new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal("new", _provider.For(_sln)!.GuideMarkdown);
    }

    private GuidelineProvider WithDefault(string reference)
    {
        var builtin = Path.Combine(_dir, "builtin");
        Directory.CreateDirectory(builtin);
        File.WriteAllText(Path.Combine(builtin, ".twincat-mcp.Syntegon.json"), """{ "name": "Syntegon" }""");
        return new GuidelineProvider(builtin, reference);
    }

    [Fact]
    public void A_solution_without_config_uses_the_server_default()
    {
        var provider = WithDefault("builtin:syntegon");

        Assert.Equal("Syntegon", provider.For(_sln)!.Name);
        Assert.True(provider.IsServerDefault);
    }

    [Fact]
    public void The_solution_config_wins_over_the_server_default()
    {
        var provider = WithDefault("builtin:syntegon");
        WriteConfig("""{ "name": "project" }""", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal("project", provider.For(_sln)!.Name);
        Assert.False(provider.IsServerDefault);
    }

    [Fact]
    public void The_server_default_can_be_a_file_path()
    {
        var company = Path.Combine(_dir, "company.json");
        File.WriteAllText(company, """{ "name": "company", "extends": "builtin:syntegon" }""");
        var provider = WithDefault(company);

        var set = provider.For(_sln)!;

        Assert.Equal("company", set.Name);
        Assert.Equal(2, set.Files.Count);
    }

    [Fact]
    public void An_unknown_server_default_is_a_config_error_naming_the_setting()
    {
        var provider = WithDefault("builtin:nope");

        var ex = Assert.Throws<GuidelineConfigException>(() => provider.For(_sln));

        Assert.Contains(GuidelineProvider.DefaultVariable, ex.Message);
    }

    [Fact]
    public void Without_open_solution_the_server_default_does_not_apply()
    {
        Assert.Null(WithDefault("builtin:syntegon").For(null));
    }

    [Fact]
    public void Invalid_regexes_and_values_are_config_errors_that_stay_until_fixed()
    {
        File.WriteAllText(Path.Combine(_dir, "h.txt"), "(* header *)");
        WriteConfig("""{ "headers": { "templates": { "t": "h.txt" }, "assign": [ { "kinds": ["METHOD"], "parent": "(", "template": "t" } ] } }""",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Throws<GuidelineConfigException>(() => _provider.For(_sln));
        Assert.Throws<GuidelineConfigException>(() => _provider.For(_sln));

        WriteConfig("""{ "declarations": { "enum": { "min": "zero" } } }""", new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        Assert.Throws<GuidelineConfigException>(() => _provider.For(_sln));
    }

    [Fact]
    public void Creating_a_missing_guide_clears_the_error()
    {
        WriteConfig("""{ "guide": "later.md" }""", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Throws<GuidelineConfigException>(() => _provider.For(_sln));

        File.WriteAllText(Path.Combine(_dir, "later.md"), "# now there");

        Assert.Equal("# now there", _provider.For(_sln)!.GuideMarkdown);
    }

    [Fact]
    public void A_broken_config_throws_until_it_is_fixed()
    {
        WriteConfig("""{ "rules": { "nope": "error" } }""", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Throws<GuidelineConfigException>(() => _provider.For(_sln));
        Assert.Null(_provider.TryFor(_sln, out var error));
        Assert.Contains("nope", error!.Message);

        WriteConfig("""{ "name": "fixed" }""", new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal("fixed", _provider.For(_sln)!.Name);
    }
}
