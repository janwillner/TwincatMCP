using TwinCatMcp.Plc;
using TwinCatMcp.Tools;

namespace TwinCatMcp.Tests.Unit;

public sealed class BuildDiagnosticsTests
{
    private const string FailedBuildPane = """
        ------ Build started: Project: Plc1, Configuration: Debug TwinCAT RT (x64) ------
        C:\Temp\McpIt\Plc1\POUs\FB_Broken.TcPOU(8) : error: C0007: Expression expected instead of ';'
        Compile complete -- 1 errors, 0 warnings
        """;

    [Fact]
    public void Compiler_errors_from_the_build_pane_are_reported_when_the_error_list_is_empty()
    {
        var result = BuildDiagnostics.Combine(PlcBuildOutput.Parse(FailedBuildPane), [], includeWarnings: true, includeMessages: false);

        Assert.Equal(1, result.ErrorCount);
        Assert.Equal("C0007", result.Errors[0].Code);
        Assert.Equal("FB_Broken", result.Errors[0].Object);
        Assert.Equal(8, result.Errors[0].FileLine);
        Assert.Equal("buildOutput", result.Source);
    }

    [Fact]
    public void Error_list_is_used_when_the_build_pane_has_no_diagnostics()
    {
        var listError = new PlcDiagnostic("error", null, "Some project error", "Proj", null, null);

        var result = BuildDiagnostics.Combine(PlcBuildOutput.Parse(""), [listError], includeWarnings: true, includeMessages: false);

        Assert.Equal(1, result.ErrorCount);
        Assert.Equal("Some project error", result.Errors[0].Message);
        Assert.Equal("errorList", result.Source);
    }

    [Fact]
    public void Warnings_are_omitted_when_not_requested_but_still_counted()
    {
        var pane = """
            C:\P\MAIN.TcPOU(3) : warning: C0373: Unused variable 'n'
            Compile complete -- 0 errors, 1 warnings
            """;

        var result = BuildDiagnostics.Combine(PlcBuildOutput.Parse(pane), [], includeWarnings: false, includeMessages: false);

        Assert.Equal(0, result.ErrorCount);
        Assert.Equal(1, result.WarningCount);
        Assert.Null(result.Warnings);
    }

    [Fact]
    public void Last_build_section_ignores_earlier_builds_in_the_pane()
    {
        var pane = FailedBuildPane + "\n------ Build started: Project: Plc1 ------\nCompile complete -- 0 errors, 0 warnings\n";

        var section = BuildDiagnostics.LastBuildSection(pane);

        Assert.Equal(0, PlcBuildOutput.Parse(section).ErrorCount);
        Assert.StartsWith("------ Build started", section);
    }
}
