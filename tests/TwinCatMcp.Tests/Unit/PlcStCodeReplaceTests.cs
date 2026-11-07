using TwinCatMcp.Core;
using TwinCatMcp.Plc;

namespace TwinCatMcp.Tests.Unit;

public sealed class PlcStCodeReplaceTests
{
    private const string Impl = "IF bEnable THEN\n\tnCount := nCount + 1;\nEND_IF\nnCount := 0;\n";

    [Fact]
    public void Replaces_a_unique_occurrence_and_reports_its_line()
    {
        var r = StCode.Replace(Impl, "nCount + 1", "nCount + 2", replaceAll: false);

        Assert.Equal("IF bEnable THEN\n\tnCount := nCount + 2;\nEND_IF\nnCount := 0;\n", r.Text);
        Assert.Equal(1, r.Count);
        Assert.Equal([2], r.Lines);
    }

    [Fact]
    public void Fails_when_the_old_text_is_not_found()
    {
        var ex = Assert.Throws<TcException>(() => StCode.Replace(Impl, "bStart", "bGo", false));

        Assert.Contains("not found", ex.Message);
        Assert.NotNull(ex.Hint);
    }

    [Fact]
    public void Fails_when_the_old_text_is_ambiguous_and_names_the_lines()
    {
        var ex = Assert.Throws<TcException>(() => StCode.Replace(Impl, "nCount :=", "nValue :=", false));

        Assert.Contains("2 times", ex.Message);
        Assert.Contains("2, 4", ex.Message);
    }

    [Fact]
    public void Replace_all_replaces_every_occurrence()
    {
        var r = StCode.Replace(Impl, "nCount", "nValue", replaceAll: true);

        Assert.Equal(3, r.Count);
        Assert.DoesNotContain("nCount", r.Text);
        Assert.Equal([2, 4], r.Lines);
    }

    [Fact]
    public void Matches_LF_search_text_against_CRLF_code_and_keeps_CRLF()
    {
        var crlf = "VAR\r\n\tbOld : BOOL;\r\nEND_VAR\r\n";

        var r = StCode.Replace(crlf, "VAR\n\tbOld : BOOL;", "VAR\n\tbNew : BOOL;\n\tnCount : INT;", false);

        Assert.Equal("VAR\r\n\tbNew : BOOL;\r\n\tnCount : INT;\r\nEND_VAR\r\n", r.Text);
    }

    [Fact]
    public void Hints_at_a_whitespace_tolerant_match_when_exact_match_fails()
    {
        var ex = Assert.Throws<TcException>(() => StCode.Replace(Impl, "nCount:=nCount+1;", "x", false));

        Assert.Contains("line 2", ex.Hint);
    }

    [Fact]
    public void Diff_shows_removed_and_added_lines()
    {
        var r = StCode.Replace(Impl, "nCount + 1", "nCount + 2", false);

        Assert.Contains("-\tnCount := nCount + 1;", r.Diff);
        Assert.Contains("+\tnCount := nCount + 2;", r.Diff);
    }

    [Fact]
    public void Empty_old_text_is_rejected()
    {
        Assert.Throws<TcException>(() => StCode.Replace(Impl, "", "x", false));
    }
}
