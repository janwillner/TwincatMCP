using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TwinCatMcp.Core;
using TwinCatMcp.Guidelines;
using TwinCatMcp.Plc;

namespace TwinCatMcp.Tools;

/// <summary>Coding guidelines of the open solution (prefix guidelines_): read the effective rules, audit existing code.</summary>
[McpServerToolType]
public sealed class GuidelineTools(XaeSession session)
{
    [McpServerTool(Name = "guidelines_get", ReadOnly = true), Description(
        "Shows the coding guidelines of the open solution (.twincat-mcp.json next to the .sln, incl. files it extends): name, active rules " +
        "with severity, naming templates/prefixes, declaration and header settings, and the prose style guide. Read it before writing PLC code - " +
        "pou_* write tools reject new/changed code that breaks an 'error' rule. Without config nothing is checked.")]
    public Task<CallToolResult> Get(
        [Description("Include the prose style guide (Markdown).")] bool includeGuide = true,
        [Description("Include the merged configuration (naming templates, prefixes, declarations, headers).")] bool includeEffective = true,
        [Description("Include which file set each value (useful when a project overrides a company base).")] bool includeSources = false)
        => session.RunAsync(s => Describe(s, includeGuide, includeEffective, includeSources), TimeSpan.FromSeconds(60));

    /// <summary>Guideline status of the open solution (also served as resource twincat://guidelines/effective).</summary>
    public static object Describe(XaeSession s, bool includeGuide, bool includeEffective, bool includeSources)
    {
        var solution = s.SolutionFile;
        var config = GuidelineProvider.ConfigPathOf(solution);
        var set = s.GuidelineProvider.TryFor(solution, out var error);
        if (error is not null)
        {
            return new
            {
                solution,
                config,
                active = false,
                error = error.Message,
                hint = "The coding guideline config is broken; pou_* write tools refuse to change PLC code until the user fixes it.",
            };
        }

        if (set is null)
        {
            return new
            {
                solution,
                config,
                active = false,
                builtin = s.GuidelineProvider.BuiltinNames.Select(n => GuidelineConfigFile.BuiltinPrefix + n).ToArray(),
                hint = solution is null
                    ? "No solution is open."
                    : "No coding guidelines for this solution, nothing is checked. Ask the user whether to add .twincat-mcp.json next to the .sln, " +
                      "e.g. { \"extends\": \"builtin:beckhoff\" }.",
            };
        }

        return new
        {
            solution,
            config,
            active = true,
            name = set.Name,
            description = set.Description,
            files = set.Files,
            rules = set.ActiveRules,
            autoFix = new { headers = set.AutoFixHeaders, requiredAttributes = set.AutoFixAttributes },
            staticAnalysisSettingsFile = set.StaticAnalysisSettingsFile,
            effective = includeEffective ? set.Effective : null,
            sources = includeSources ? set.Sources : null,
            guide = includeGuide ? set.GuideMarkdown : null,
        };
    }

    [McpServerTool(Name = "guidelines_check", ReadOnly = true), Description(
        "Audits existing PLC code against the solution's coding guidelines: checks every name and declaration of one object, a folder or the " +
        "whole PLC project (not only changes, unlike the write-time check). Returns counts per rule and the findings; optionally writes a SARIF " +
        "report. Use it after imports (plc_import_plcopen, archives, tree_consume_xml), which are not checked while writing.")]
    public Task<CallToolResult> Check(
        [Description("Tree path of an object or folder. Empty = the whole PLC project.")] string? path = null,
        [Description("PLC project (name or path) when path is empty. Empty = the only PLC project.")] string? project = null,
        [Description("Optional path of a SARIF file (*.sarif.json) to write all findings to.")] string? sarifPath = null,
        [Description("Maximum number of findings to return (all of them go to the SARIF file).")] int maxFindings = 200,
        int timeoutSeconds = 600)
        => session.RunAsync(s =>
        {
            var set = s.Guidelines ?? throw new TcException("The open solution has no coding guidelines (.twincat-mcp.json next to the .sln).")
            {
                Hint = "Call guidelines_get for details; the user decides whether to add a config.",
            };

            var root = string.IsNullOrWhiteSpace(path) ? PlcTree.ResolveProject(s, project).Nested : PlcTree.ResolveItem(s, path);
            var rootType = PlcTree.TypeOf(root);
            var items = rootType is PlcObjects.App or PlcObjects.Folder
                ? PlcTree.Elements(root)
                : new[] { root }.Concat(TreeInfo.Descendants(root));

            var findings = new List<GuidelineViolation>();
            var objects = 0;
            foreach (var item in items)
            {
                var kind = PlcObjects.KindOf(PlcTree.TypeOf(item));
                var isCode = PlcObjects.IsCode(PlcTree.TypeOf(item));
                if (!isCode && GuidelineSet.NameRuleOf(kind) is null)
                {
                    continue;
                }

                objects++;
                var obj = GuidelineGate.ObjectOf(item);
                var text = isCode ? PlcTree.ReadText(item) : new PouText(null, null);
                findings.AddRange(set.CheckName(obj, text.Declaration));
                if (isCode)
                {
                    findings.AddRange(set.CheckText(obj, null, text));
                }
            }

            string? sarif = null;
            if (!string.IsNullOrWhiteSpace(sarifPath))
            {
                sarif = Path.GetFullPath(sarifPath);
                GuidelineSarif.Write(sarif, findings, typeof(GuidelineTools).Assembly.GetName().Version?.ToString() ?? "0");
            }

            return new
            {
                root = root.PathName,
                guidelines = set.Name,
                objects,
                errors = findings.Count(f => f.Severity == "error"),
                warnings = findings.Count(f => f.Severity == "warn"),
                byRule = findings.GroupBy(f => f.Rule).OrderByDescending(g => g.Count()).ToDictionary(g => g.Key, g => g.Count()),
                findings = findings.Take(Math.Max(0, maxFindings)).ToArray(),
                truncated = findings.Count > maxFindings ? true : (bool?)null,
                sarifFile = sarif,
            };
        }, TimeSpan.FromSeconds(timeoutSeconds));
}
