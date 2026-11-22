using System.ComponentModel;
using ModelContextProtocol.Server;
using TwinCatMcp.Core;

namespace TwinCatMcp.Resources;

/// <summary>Read-only MCP resources: guides for LLM clients and live views of the session / tree.</summary>
[McpServerResourceType]
public sealed class TwinCatResources(XaeSession session)
{
    [McpServerResource(UriTemplate = "twincat://guide/workflow", Name = "workflow-guide", MimeType = "text/markdown"),
     Description("Step-by-step workflow for creating, programming, building and deploying a complete TwinCAT 3.1.4026 project with this server.")]
    public static string WorkflowGuide() => Guides.Workflow;

    [McpServerResource(UriTemplate = "twincat://guide/tree-paths", Name = "tree-path-guide", MimeType = "text/markdown"),
     Description("Cheat sheet: System Manager tree path syntax, root shortcuts and common item paths/sub types.")]
    public static string TreePathGuide() => Guides.TreePaths;

    [McpServerResource(UriTemplate = "twincat://guide/st-conventions", Name = "st-conventions", MimeType = "text/markdown"),
     Description("Structured Text naming conventions and code style to use when generating PLC code: the style guide of the open solution's " +
                 "coding guidelines (.twincat-mcp.json), otherwise the Beckhoff conventions.")]
    public async Task<string> StConventions()
    {
        try
        {
            var guide = await session.Sta.InvokeAsync(
                () => session.GuidelineProvider.TryFor(session.SolutionFile, out _)?.GuideMarkdown, TimeSpan.FromSeconds(30));
            return guide ?? Guides.StConventions;
        }
        catch (Exception)
        {
            return Guides.StConventions;
        }
    }

    [McpServerResource(UriTemplate = "twincat://guidelines/effective", Name = "coding-guidelines", MimeType = "application/json"),
     Description("Coding guidelines of the open solution (.twincat-mcp.json): active rules, naming templates, declaration/header settings and style guide. " +
                 "pou_* write tools enforce them.")]
    public async Task<string> CodingGuidelines()
        => Json.Serialize(await session.Sta.InvokeAsync(
            () => TwinCatMcp.Tools.GuidelineTools.Describe(session, includeGuide: true, includeEffective: true, includeSources: false),
            TimeSpan.FromSeconds(30)));

    [McpServerResource(UriTemplate = "twincat://session/status", Name = "session-status", MimeType = "application/json"),
     Description("Live status of the XAE session (IDE, solution, selected TwinCAT project, target).")]
    public async Task<string> SessionStatus()
        => Json.Serialize(await session.Sta.InvokeAsync(session.Status, TimeSpan.FromSeconds(30)));

    [McpServerResource(UriTemplate = "twincat://tree/{path}", Name = "tree-item", MimeType = "application/json"),
     Description("A System Manager tree item with two levels of children. 'path' uses '^' as separator, e.g. TIPC or TIRT.")]
    public async Task<string> TreeItem(string path)
        => Json.Serialize(await session.Sta.InvokeAsync(() => TreeInfo.Describe(session.Lookup(Uri.UnescapeDataString(path)), 2), TimeSpan.FromSeconds(60)));
}
