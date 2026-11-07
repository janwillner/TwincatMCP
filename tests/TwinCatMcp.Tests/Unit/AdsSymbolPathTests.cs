using TwinCatMcp.Ads;
using TwinCatMcp.Core;

namespace TwinCatMcp.Tests.Unit;

public sealed class AdsSymbolPathTests
{
    [Theory]
    [InlineData("MAIN.st", "MAIN.st.b", true)]
    [InlineData("MAIN.arr", "MAIN.arr[3]", true)]
    [InlineData("MAIN.arr", "MAIN.arr[3].x", true)]
    [InlineData("main.ST", "MAIN.st.b", true)]
    [InlineData("MAIN.st", "MAIN.st", true)]
    [InlineData("MAIN.s", "MAIN.st", false)]
    [InlineData("MAIN.st.b", "MAIN.st", false)]
    public void Prefix_matches_only_at_member_or_index_boundaries(string prefix, string path, bool expected)
    {
        Assert.Equal(expected, SymbolPath.IsSelfOrDescendant(prefix, path));
    }

    [Theory]
    [InlineData(" MAIN.arr[ 1 , 2 ] ", "MAIN.arr[1,2]")]
    [InlineData("GVL.x", "GVL.x")]
    public void Normalize_removes_whitespace(string input, string expected)
    {
        Assert.Equal(expected, SymbolPath.Normalize(input));
    }

    [Fact]
    public void Empty_path_is_rejected()
    {
        Assert.Throws<TcException>(() => SymbolPath.Normalize("  "));
    }

    [Theory]
    [InlineData(null, "MAIN.nCounter", true)]
    [InlineData("MAIN", "MAIN.nCounter", true)]
    [InlineData("main.n", "MAIN.nCounter", true)]
    [InlineData("GVL", "MAIN.nCounter", false)]
    [InlineData("*Counter", "MAIN.nCounter", true)]
    [InlineData("MAIN.?Counter", "MAIN.nCounter", true)]
    [InlineData("*.b*", "GVL.bStart", true)]
    [InlineData("*.b*", "GVL.nStart", false)]
    [InlineData("re:^GVL\\.(b|n)Start$", "GVL.nStart", true)]
    [InlineData("/start$/", "GVL.bStart", true)]
    [InlineData("/^MAIN/", "GVL.bStart", false)]
    public void Filter_supports_prefix_glob_and_regex(string? pattern, string name, bool expected)
    {
        Assert.Equal(expected, SymbolFilter.Create(pattern).IsMatch(name));
    }

    [Fact]
    public void Invalid_regex_gives_a_tool_error()
    {
        var ex = Assert.Throws<TcException>(() => SymbolFilter.Create("re:(unclosed"));
        Assert.NotNull(ex.Hint);
    }

    [Fact]
    public void Suggestions_prefer_names_containing_the_last_segment()
    {
        var names = new[] { "MAIN.nCounter", "MAIN.bStart", "GVL.nCounterMax", "GVL.fTemp" };
        var s = SymbolPath.Suggest("MAIN.nCountr", names, 3);
        Assert.Contains("MAIN.nCounter", s);
        Assert.DoesNotContain("GVL.fTemp", s);
    }
}
