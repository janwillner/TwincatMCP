using TwinCatMcp.Plc;

namespace TwinCatMcp.Tests.Unit;

public sealed class PlcBuildOutputTests
{
    // Captured live from the "Build" output pane of TcXaeShell 3.1.4026 after CheckAllObjects / build.
    private const string CheckOutput = """
        ------ Build started: Application: McpIt.Plc1 -------
        Typify code...
        C:\Temp\McpIt\McpIt\Plc1\POUs\FB_Probe.TcPOU(8) : error: C0007: Expression expected instead of ';'
        Compile complete -- 1 errors, 0 warnings
        """;

    private const string BuildOutput = """
        ------ Build started: Application: McpIt.Plc1 -------
        Typify code...
        Compile complete -- 1 errors, 0 warnings
        Build complete -- 1 errors, 0 warnings : No download possible
        ------ Build started: Project: McpIt, Configuration: Release TwinCAT OS (x64) ------
        Error: Value does not fall within the expected range.
        ========== Build: 0 succeeded or up-to-date, 2 failed, 0 skipped ==========
        """;

    // Captured live from a German TcXaeShell 3.1.4024.79 after CheckAllObjects: no error codes, localized summary.
    private const string CheckOutput4024German = """
        ------ Build started: Application: Tc4024Test.Plc1 -------
        typify code ...
        C:\Temp\Tc4024Test\Tc4024Test\Plc1\POUs\MAIN.TcPOU(9) : error: Cannot convert type 'Unknown type: 'bUnknownVariable'' to type 'UDINT'
        C:\Temp\Tc4024Test\Tc4024Test\Plc1\POUs\MAIN.TcPOU(9) : error: Identifier 'bUnknownVariable' not defined
        Kompilierung abgeschlossen -- 2 Fehler, 0 Warnungen
        """;

    [Fact]
    public void Parses_4024_compiler_errors_without_error_code()
    {
        var r = PlcBuildOutput.Parse(CheckOutput4024German);

        Assert.Equal(2, r.Errors.Count);
        var e = r.Errors[1];
        Assert.Equal("MAIN", e.Object);
        Assert.Null(e.Code);
        Assert.Equal("Identifier 'bUnknownVariable' not defined", e.Message);
        Assert.Equal(9, e.FileLine);
    }

    [Fact]
    public void Reads_totals_from_the_German_summary_line()
    {
        var r = PlcBuildOutput.Parse(CheckOutput4024German);

        Assert.Equal((2, 0), (r.ErrorCount, r.WarningCount));
        Assert.True(PlcBuildOutput.HasSummary(CheckOutput4024German));
    }

    [Fact]
    public void Parses_compiler_errors_with_object_code_and_message()
    {
        var r = PlcBuildOutput.Parse(CheckOutput);

        var e = Assert.Single(r.Errors);
        Assert.Equal("FB_Probe", e.Object);
        Assert.Equal("C0007", e.Code);
        Assert.Equal("Expression expected instead of ';'", e.Message);
        Assert.Equal(8, e.FileLine);
        Assert.EndsWith("FB_Probe.TcPOU", e.File);
    }

    [Fact]
    public void Reads_error_and_warning_totals_from_the_summary_line()
    {
        var r = PlcBuildOutput.Parse(CheckOutput);

        Assert.Equal((1, 0), (r.ErrorCount, r.WarningCount));
    }

    [Fact]
    public void Generic_error_lines_are_reported_too()
    {
        var r = PlcBuildOutput.Parse(BuildOutput);

        Assert.Contains(r.Errors, e => e.Message == "Value does not fall within the expected range.");
        Assert.Equal(1, r.ErrorCount);
    }

    [Fact]
    public void Parses_warnings_and_static_analysis_messages()
    {
        const string text = """
            C:\P\POUs\MAIN.TcPOU(12) : warning: C0373: Unused variable 'nX'
            C:\P\POUs\FB_A.TcPOU(3) : warning SA0033: Unused variable 'bOld'
            Compile complete -- 0 errors, 2 warnings
            """;

        var r = PlcBuildOutput.Parse(text);

        Assert.Equal(["C0373", "SA0033"], r.Warnings.Select(w => w.Code));
        Assert.Equal("Unused variable 'bOld'", r.Warnings[1].Message);
        Assert.Equal("FB_A", r.Warnings[1].Object);
        Assert.Equal(2, r.WarningCount);
    }

    [Fact]
    public void Clean_compile_has_no_diagnostics()
    {
        var r = PlcBuildOutput.Parse("------ Build started: Application: McpIt.Plc1 -------\nTypify code...\nCompile complete -- 0 errors, 0 warnings\n");

        Assert.Empty(r.Errors);
        Assert.Equal(0, r.ErrorCount);
    }

    [Fact]
    public void Delta_returns_only_text_appended_since_the_snapshot()
    {
        Assert.Equal("new", PlcBuildOutput.Delta("old\n", "old\nnew"));
        Assert.Equal("fresh", PlcBuildOutput.Delta("old text that was cleared", "fresh"));
    }
}
