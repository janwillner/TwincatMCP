using System.Text.Json;
using System.Text.Json.Nodes;
using TwinCatMcp.Core;

namespace TwinCatMcp.Guidelines;

/// <summary>A write broke a guideline rule with severity 'error'; nothing was written.</summary>
public sealed class GuidelineViolationException(IReadOnlyList<GuidelineViolation> violations)
    : TcException(Describe(violations))
{
    public IReadOnlyList<GuidelineViolation> Violations { get; } = violations;

    private static string Describe(IReadOnlyList<GuidelineViolation> violations)
    {
        var errors = violations.Where(v => v.Severity == "error").ToArray();
        var lines = errors.Select(v => $"[{v.Rule}] {v.Message}" + (v.Suggestion is null ? "" : $" Suggestion: {v.Suggestion}"));
        return $"{errors.Length} coding guideline violation(s). Nothing was written. " + string.Join(" ", lines);
    }
}

/// <summary>Adds guideline findings to tool results.</summary>
public static class GuidelineReport
{
    public const string Hint =
        "Fix the names/declarations (see guidelineViolations, incl. suggestions) and retry. The rules come from the solution's .twincat-mcp.json " +
        "(guidelines_get). Do not edit that file and do not add {attribute 'mcp-ignore'} / {attribute 'naming'} pragmas to get around a rule " +
        "unless the user asks for it.";

    /// <summary>The payload with 'guidelineViolations' / 'guidelineFixes' added (JSON text), or the payload itself without findings.</summary>
    public static object? Attach(object? payload, IReadOnlyList<GuidelineViolation> violations, IReadOnlyList<string> fixes)
    {
        if (violations.Count == 0 && fixes.Count == 0)
        {
            return payload;
        }

        var node = payload is string s ? JsonNode.Parse(s) : JsonSerializer.SerializeToNode(payload, Json.Options);
        var obj = node as JsonObject ?? new JsonObject { ["result"] = node };
        if (violations.Count > 0)
        {
            obj["guidelineViolations"] = JsonSerializer.SerializeToNode(violations, Json.Options);
        }

        if (fixes.Count > 0)
        {
            obj["guidelineFixes"] = JsonSerializer.SerializeToNode(fixes, Json.Options);
        }

        return obj.ToJsonString(Json.Options);
    }
}

/// <summary>Writes guideline findings as a SARIF 2.1.0 log (like plc_run_static_analysis, e.g. for code scanning).</summary>
public static class GuidelineSarif
{
    public static void Write(string path, IReadOnlyList<GuidelineViolation> findings, string toolVersion)
    {
        var rules = findings.Select(f => f.Rule).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(r => new { id = r, shortDescription = new { text = r } }).ToArray();
        var log = new Dictionary<string, object>
        {
            ["$schema"] = "https://json.schemastore.org/sarif-2.1.0.json",
            ["version"] = "2.1.0",
            ["runs"] = new[]
            {
                new
                {
                    tool = new { driver = new { name = "twincat-mcp-guidelines", version = toolVersion, rules } },
                    results = findings.Select(f => new
                    {
                        ruleId = f.Rule,
                        level = f.Severity == "error" ? "error" : "warning",
                        message = new { text = f.Suggestion is null ? f.Message : $"{f.Message} Suggestion: {f.Suggestion}" },
                        locations = new[]
                        {
                            new
                            {
                                logicalLocations = new[]
                                {
                                    new { fullyQualifiedName = f.Object, name = f.Identifier ?? f.Object.Split('^')[^1], kind = f.Identifier is null ? "type" : "member" },
                                },
                            },
                        },
                    }).ToArray(),
                },
            },
        };

        var json = JsonSerializer.Serialize(log, Json.Options);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, json);
    }
}
