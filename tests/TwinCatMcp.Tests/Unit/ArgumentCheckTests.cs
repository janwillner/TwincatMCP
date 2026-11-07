using System.Text.Json;
using TwinCatMcp.Core;

namespace TwinCatMcp.Tests.Unit;

public sealed class ArgumentCheckTests
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "axes": { "type": "array", "items": { "type": "string" }, "description": "Axis paths or names." },
            "includeSimulationMode": { "type": "boolean" }
          },
          "required": ["axes"]
        }
        """).RootElement;

    [Fact]
    public void A_misspelled_parameter_is_reported_as_missing_and_unknown()
    {
        var result = ArgumentCheck.Analyze(Schema, ["axis"]);

        Assert.Equal(["axes"], result.Missing);
        Assert.Equal(["axis"], result.Unknown);
    }

    [Fact]
    public void A_near_miss_parameter_name_gets_a_suggestion()
    {
        var result = ArgumentCheck.Analyze(Schema, ["axis"]);

        Assert.Equal("axes", result.Suggestions["axis"]);
    }

    [Fact]
    public void Expected_parameters_list_names_types_and_required_flag()
    {
        var result = ArgumentCheck.Analyze(Schema, ["axes"]);

        Assert.Empty(result.Missing);
        Assert.Empty(result.Unknown);
        Assert.Contains("axes: array (required)", result.Expected);
        Assert.Contains("includeSimulationMode: boolean", result.Expected);
    }

    [Fact]
    public void Parameter_names_are_matched_case_insensitively_as_a_suggestion()
    {
        var result = ArgumentCheck.Analyze(Schema, ["Axes"]);

        Assert.Equal(["Axes"], result.Unknown);
        Assert.Equal("axes", result.Suggestions["Axes"]);
    }
}
