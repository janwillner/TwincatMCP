using System.ComponentModel;
using EnvDTE;
using EnvDTE80;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TwinCatMcp.Core;

namespace TwinCatMcp.Tools;

[McpServerToolType]
public sealed class BuildTools(XaeSession session)
{
    [McpServerTool(Name = "build_solution"), Description(
        "Builds (or rebuilds) the whole solution incl. all PLC projects and returns errors/warnings from the Error List. " +
        "This is the authoritative compile check after editing ST code.")]
    public Task<CallToolResult> BuildSolution(
        [Description("Clean + build instead of incremental build.")] bool rebuild = false,
        [Description("Include warnings in the result.")] bool includeWarnings = true,
        [Description("Include messages in the result.")] bool includeMessages = false,
        int timeoutSeconds = 600)
        => session.RunAsync(s =>
        {
            var sb = s.Dte.Solution.SolutionBuild;
            if (rebuild)
            {
                sb.Clean(true);
            }

            sb.Build(true);
            var failedProjects = sb.LastBuildInfo;
            return new
            {
                success = failedProjects == 0,
                failedProjects,
                diagnostics = ErrorList.Read(s.Dte, includeWarnings, includeMessages),
            };
        }, timeoutSeconds);

    [McpServerTool(Name = "build_project"), Description("Builds a single project of the solution (e.g. a PLC project's .plcproj) by its unique name or file name.")]
    public Task<CallToolResult> BuildProject(
        [Description("Project unique name / file name as listed by solution_list_projects (e.g. 'MyPlc\\MyPlc.plcproj' or the .tsproj).")] string project,
        string configuration = "Release|TwinCAT RT (x64)",
        bool includeWarnings = true,
        int timeoutSeconds = 600)
        => session.RunAsync(s =>
        {
            var sb = s.Dte.Solution.SolutionBuild;
            var match = s.EnumerateAllProjects().FirstOrDefault(p =>
                            string.Equals(p.UniqueName, project, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(p.Name, project, StringComparison.OrdinalIgnoreCase) ||
                            p.FullName.EndsWith(project, StringComparison.OrdinalIgnoreCase))
                        ?? throw new TcException($"Project '{project}' not found.") { Hint = "Use solution_list_projects." };
            sb.BuildProject(configuration, match.UniqueName, true);
            return new
            {
                project = match.UniqueName,
                success = sb.LastBuildInfo == 0,
                diagnostics = ErrorList.Read(s.Dte, includeWarnings, false),
            };
        }, timeoutSeconds);

    [McpServerTool(Name = "build_clean"), Description("Cleans the solution.")]
    public Task<CallToolResult> Clean()
        => session.RunAsync(s => { s.Dte.Solution.SolutionBuild.Clean(true); return new { cleaned = true }; });

    [McpServerTool(Name = "build_get_errors", ReadOnly = true), Description("Reads the current Error List (errors, warnings, optionally messages) without building.")]
    public Task<CallToolResult> GetErrors(bool includeWarnings = true, bool includeMessages = false)
        => session.RunAsync(s => ErrorList.Read(s.Dte, includeWarnings, includeMessages));

    [McpServerTool(Name = "build_get_output", ReadOnly = true), Description("Returns the text of an Output window pane (default 'Build'), optionally only the last N lines.")]
    public Task<CallToolResult> GetOutput(string pane = "Build", int lastLines = 200)
        => session.RunAsync(s => new { pane, text = OutputWindow.Read(s.Dte, pane, lastLines) });

    [McpServerTool(Name = "build_list_configurations", ReadOnly = true), Description("Lists solution configurations/platforms and the active one.")]
    public Task<CallToolResult> ListConfigurations()
        => session.RunAsync(s =>
        {
            var sb = (SolutionBuild2)s.Dte.Solution.SolutionBuild;
            var list = new List<string>();
            foreach (SolutionConfiguration2 c in sb.SolutionConfigurations)
            {
                list.Add($"{c.Name}|{c.PlatformName}");
            }

            var active = (SolutionConfiguration2)sb.ActiveConfiguration;
            return new { active = $"{active.Name}|{active.PlatformName}", configurations = list.Distinct().ToArray() };
        });

    [McpServerTool(Name = "build_set_configuration"), Description("Activates a solution configuration, e.g. name='Release', platform='TwinCAT RT (x64)'.")]
    public Task<CallToolResult> SetConfiguration(string name, string platform = "TwinCAT RT (x64)")
        => session.RunAsync(s =>
        {
            var sb = (SolutionBuild2)s.Dte.Solution.SolutionBuild;
            foreach (SolutionConfiguration2 c in sb.SolutionConfigurations)
            {
                if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase) && string.Equals(c.PlatformName, platform, StringComparison.OrdinalIgnoreCase))
                {
                    c.Activate();
                    return new { active = $"{c.Name}|{c.PlatformName}" };
                }
            }

            throw new TcException($"Configuration '{name}|{platform}' not found.") { Hint = "Use build_list_configurations." };
        });
}

public static class ErrorList
{
    public sealed record Diagnostic(string Level, string Description, string? File, int Line, int Column, string? Project);

    public static object Read(DTE2 dte, bool includeWarnings, bool includeMessages)
    {
        var errors = new List<Diagnostic>();
        var warnings = new List<Diagnostic>();
        var messages = new List<Diagnostic>();
        var window = dte.ToolWindows.ErrorList;
        try
        {
            window.ShowErrors = true;
            window.ShowWarnings = true;
            window.ShowMessages = true;
        }
        catch
        {
            // not supported in all shells
        }

        var items = window.ErrorItems;
        for (var i = 1; i <= items.Count; i++)
        {
            var e = items.Item(i);
            var level = e.ErrorLevel switch
            {
                vsBuildErrorLevel.vsBuildErrorLevelHigh => "error",
                vsBuildErrorLevel.vsBuildErrorLevelMedium => "warning",
                _ => "message",
            };
            var d = new Diagnostic(level, e.Description, NullIfEmpty(Safe(() => e.FileName)), Safe(() => e.Line), Safe(() => e.Column), NullIfEmpty(Safe(() => e.Project)));
            (level == "error" ? errors : level == "warning" ? warnings : messages).Add(d);
        }

        return new
        {
            errorCount = errors.Count,
            warningCount = warnings.Count,
            messageCount = messages.Count,
            errors,
            warnings = includeWarnings ? warnings : null,
            messages = includeMessages ? messages : null,
        };
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static T Safe<T>(Func<T> f)
    {
        try { return f(); } catch { return default!; }
    }
}

public static class OutputWindow
{
    public static string Read(DTE2 dte, string paneName, int lastLines)
    {
        var panes = dte.ToolWindows.OutputWindow.OutputWindowPanes;
        foreach (OutputWindowPane pane in panes)
        {
            if (!string.Equals(pane.Name, paneName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var doc = pane.TextDocument;
            var sel = doc.Selection;
            sel.StartOfDocument();
            sel.EndOfDocument(true);
            var text = sel.Text ?? "";
            if (lastLines > 0)
            {
                var lines = text.Split('\n');
                if (lines.Length > lastLines)
                {
                    text = string.Join('\n', lines[^lastLines..]);
                }
            }

            return text;
        }

        var names = new List<string>();
        foreach (OutputWindowPane p in panes)
        {
            names.Add(p.Name);
        }

        throw new TcException($"Output pane '{paneName}' not found. Available: {string.Join(", ", names)}");
    }
}
