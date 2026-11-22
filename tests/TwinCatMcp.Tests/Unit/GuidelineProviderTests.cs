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
