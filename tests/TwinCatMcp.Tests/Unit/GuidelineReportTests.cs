using System.Text.Json;
using System.Text.Json.Nodes;
using TwinCatMcp.Core;
using TwinCatMcp.Guidelines;

namespace TwinCatMcp.Tests.Unit;

/// <summary>How findings reach the client: result fields, the rejecting error and SARIF.</summary>
public sealed class GuidelineReportTests
{
    private static readonly GuidelineViolation Warn = new("naming.var.local", "warn", "'x' does not match '{type}{Name}'.", "POUs^FB_A", "x", 3, "nX");
    private static readonly GuidelineViolation Error = new("naming.var.input", "error", "'In' does not match 'vi_{type}{Name}'.", "POUs^FB_A", "In", 2, "vi_b_In");

    [Fact]
    public void Warnings_and_fixes_are_added_to_an_object_result()
    {
        var text = (string)GuidelineReport.Attach(new { path = "POUs^FB_A" }, [Warn], ["header inserted into the declaration"])!;

        var json = JsonNode.Parse(text)!;
        Assert.Equal("POUs^FB_A", json["path"]!.GetValue<string>());
        Assert.Equal("vi_b_In", JsonNode.Parse((string)GuidelineReport.Attach(new { }, [Error], [])!)!["guidelineViolations"]![0]!["suggestion"]!.GetValue<string>());
        Assert.Equal("nX", json["guidelineViolations"]![0]!["suggestion"]!.GetValue<string>());
        Assert.Equal("header inserted into the declaration", json["guidelineFixes"]![0]!.GetValue<string>());
    }

    [Fact]
    public void A_result_without_findings_is_returned_unchanged()
    {
        var payload = new { path = "x" };

        Assert.Same(payload, GuidelineReport.Attach(payload, [], []));
    }

    [Fact]
    public void Errors_reject_the_write_and_list_every_finding()
    {
        var ex = new GuidelineViolationException([Error, Warn]);

        Assert.Contains("Nothing was written", ex.Message);
        Assert.Contains("vi_b_In", ex.Message);
        Assert.Equal(2, ex.Violations.Count);

        var body = JsonNode.Parse(ToolResult.Error(ex).Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Single().Text)!;
        Assert.Equal(2, body["guidelineViolations"]!.AsArray().Count);
    }

    [Fact]
    public void Sarif_report_lists_findings_with_rule_level_and_location()
    {
        var file = Path.Combine(Path.GetTempPath(), $"guidelines-{Guid.NewGuid():N}.sarif.json");
        try
        {
            GuidelineSarif.Write(file, [Error, Warn], "1.2.3");

            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var run = doc.RootElement.GetProperty("runs")[0];
            Assert.Equal("2.1.0", doc.RootElement.GetProperty("version").GetString());
            Assert.Equal("twincat-mcp-guidelines", run.GetProperty("tool").GetProperty("driver").GetProperty("name").GetString());
            var first = run.GetProperty("results")[0];
            Assert.Equal(("naming.var.input", "error"), (first.GetProperty("ruleId").GetString(), first.GetProperty("level").GetString()));
            Assert.Equal("warning", run.GetProperty("results")[1].GetProperty("level").GetString());
            Assert.Equal("POUs^FB_A",
                first.GetProperty("locations")[0].GetProperty("logicalLocations")[0].GetProperty("fullyQualifiedName").GetString());
        }
        finally
        {
            File.Delete(file);
        }
    }
}
