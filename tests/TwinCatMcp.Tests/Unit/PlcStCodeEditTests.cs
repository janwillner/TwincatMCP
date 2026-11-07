using System.Text.RegularExpressions;
using TwinCatMcp.Core;
using TwinCatMcp.Plc;

namespace TwinCatMcp.Tests.Unit;

public sealed class PlcStCodeEditTests
{
    private static readonly PouText Fb = new(
        "FUNCTION_BLOCK FB_Counter\nVAR_INPUT\n\tbEnable : BOOL;\nEND_VAR\nVAR\n\tnCount : INT;\nEND_VAR\n",
        "IF bEnable THEN\n\tnCount := nCount + 1;\nEND_IF\n");

    [Fact]
    public void Auto_section_picks_the_area_that_contains_the_text()
    {
        var (text, applied) = StCode.ApplyEdit(Fb, new CodeEdit("nCount + 1", "nCount + 5"));

        Assert.Equal("implementation", applied.Section);
        Assert.Contains("nCount + 5", text.Implementation);
        Assert.Equal(Fb.Declaration, text.Declaration);
    }

    [Fact]
    public void Auto_section_fails_when_text_occurs_in_both_areas()
    {
        var ex = Assert.Throws<TcException>(() => StCode.ApplyEdit(Fb, new CodeEdit("bEnable", "bRun")));

        Assert.Contains("declaration and implementation", ex.Message);
    }

    [Fact]
    public void Explicit_section_limits_the_search()
    {
        var (text, applied) = StCode.ApplyEdit(Fb, new CodeEdit("bEnable", "bRun", Section: "declaration"));

        Assert.Equal("declaration", applied.Section);
        Assert.Contains("bRun : BOOL", text.Declaration);
        Assert.Contains("IF bEnable", text.Implementation);
    }

    [Fact]
    public void Edit_on_a_missing_area_fails()
    {
        var gvl = new PouText("VAR_GLOBAL\nEND_VAR\n", null);

        Assert.Throws<TcException>(() => StCode.ApplyEdit(gvl, new CodeEdit("x", "y", Section: "implementation")));
    }

    [Fact]
    public void Sequential_edits_apply_on_top_of_each_other()
    {
        var edits = new[]
        {
            new CodeEdit("bEnable", "bRun", Section: "declaration"),
            new CodeEdit("IF bEnable", "IF bRun", Section: "implementation"),
        };

        var (text, applied) = StCode.ApplyEdits(Fb, edits);

        Assert.DoesNotContain("bEnable", text.Declaration + text.Implementation);
        Assert.Equal(2, applied.Count);
    }

    [Fact]
    public void Grep_returns_line_numbers_and_lines()
    {
        var hits = StCode.Grep(Fb.Implementation!, new Regex(@"nCount\s*:="));

        var hit = Assert.Single(hits);
        Assert.Equal(2, hit.Line);
        Assert.Equal("\tnCount := nCount + 1;", hit.Text);
    }

    [Fact]
    public void Grep_handles_CRLF_text()
    {
        var hits = StCode.Grep("A\r\nB\r\nA", new Regex("^A$"));

        Assert.Equal([1, 3], hits.Select(h => h.Line));
        Assert.Equal("A", hits[0].Text);
    }
}
