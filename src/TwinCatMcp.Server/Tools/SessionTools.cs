using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TwinCatMcp.Core;

namespace TwinCatMcp.Tools;

[McpServerToolType]
public sealed class SessionTools(XaeSession session)
{
    [McpServerTool(Name = "xae_start"), Description(
        "Starts a new TwinCAT XAE IDE instance (TcXaeShell 4026 by default) and connects to it. " +
        "Takes 20-60 s. Dialogs are suppressed (Automation Interface SilentMode).")]
    public Task<CallToolResult> Start(
        [Description("COM ProgID. Default 'TcXaeShell.DTE.17.0'. Alternatives: 'VisualStudio.DTE.17.0' (VS 2022), 'VisualStudio.DTE.18.0' (VS 2026).")] string? progId = null,
        [Description("Show the IDE main window (useful for watching what happens).")] bool visible = false,
        [Description("Keep the IDE running after the MCP server exits.")] bool keepOpen = false)
        => session.RunAsync(s => s.Start(progId, visible, keepOpen), TimeSpan.FromMinutes(5));

    [McpServerTool(Name = "xae_attach"), Description(
        "Attaches to an already running Visual Studio / TcXaeShell instance (Running Object Table). " +
        "The first TwinCAT project of its solution is selected automatically.")]
    public Task<CallToolResult> Attach(
        [Description("Optional: solution file path (.sln) or process id of the IDE. Empty = first TcXaeShell instance.")] string? solutionPathOrProcessId = null)
        => session.RunAsync(s => s.Attach(solutionPathOrProcessId), TimeSpan.FromMinutes(1));

    [McpServerTool(Name = "xae_list_instances", ReadOnly = true), Description("Lists running Visual Studio / TcXaeShell instances with their open solution.")]
    public Task<CallToolResult> ListInstances()
        => session.RunAsync(_ => RunningObjectTable.GetDteInstances().Select(i =>
        {
            string? sln = null;
            try { sln = ((EnvDTE80.DTE2)i.Dte).Solution.FullName; } catch { /* busy */ }
            return new { progId = i.ProgId, processId = i.ProcessId, solution = string.IsNullOrEmpty(sln) ? null : sln };
        }).ToArray(), TimeSpan.FromSeconds(30));

    [McpServerTool(Name = "xae_status", ReadOnly = true), Description("Shows the connected IDE, open solution, selected TwinCAT project and target AMS Net ID.")]
    public Task<CallToolResult> Status()
        => session.RunAsync(s => s.Status(), TimeSpan.FromSeconds(30));

    [McpServerTool(Name = "xae_set_visible"), Description("Shows or hides the IDE main window.")]
    public Task<CallToolResult> SetVisible(bool visible)
        => session.RunAsync(s =>
        {
            s.Dte.MainWindow.Visible = visible;
            s.Dte.SuppressUI = !visible;
            return new { visible };
        });

    [McpServerTool(Name = "xae_set_silent_mode"), Description(
        "Enables/disables the Automation Interface SilentMode (suppresses message boxes in the IDE). Enabled by default.")]
    public Task<CallToolResult> SetSilentMode(bool silent)
        => session.RunAsync(s => { s.ConfigureSilentMode(silent); return new { silent }; });

    [McpServerTool(Name = "xae_close", Destructive = true), Description(
        "Disconnects from the IDE. An IDE started by xae_start is closed; an attached IDE is only closed when quitIde=true.")]
    public Task<CallToolResult> Close(
        [Description("Save all files before closing.")] bool saveAll = true,
        [Description("Also quit an attached (not owned) IDE.")] bool quitIde = false)
        => session.RunAsync(s => s.Close(saveAll, quitIde), TimeSpan.FromMinutes(2));

    [McpServerTool(Name = "xae_execute_command"), Description(
        "Executes a Visual Studio IDE command by name (e.g. 'File.SaveAll', 'Build.RebuildSolution'). Advanced escape hatch.")]
    public Task<CallToolResult> ExecuteCommand(string command, string? args = null)
        => session.RunAsync(s => { s.Dte.ExecuteCommand(command, args ?? ""); return new { executed = command }; });

    // ---------------------------------------------------------------- solution

    [McpServerTool(Name = "solution_create"), Description(
        "Creates a new solution with an empty TwinCAT XAE project (<directory>\\<name>\\<name>.sln and <name>\\<name>.tsproj). " +
        "Next step usually: plc_create_project.")]
    public Task<CallToolResult> CreateSolution(
        [Description("Parent directory, e.g. C:\\TwinCAT\\Projects")] string directory,
        [Description("Solution and project name")] string name,
        [Description("Delete an existing non-empty target folder.")] bool overwrite = false)
        => session.RunAsync(s => s.CreateSolution(directory, name, overwrite), TimeSpan.FromMinutes(3));

    [McpServerTool(Name = "solution_open"), Description("Opens an existing .sln (or a standalone .tsproj) and selects its first TwinCAT project.")]
    public Task<CallToolResult> OpenSolution([Description("Full path to .sln or .tsproj")] string path)
        => session.RunAsync(s => s.OpenSolution(path), TimeSpan.FromMinutes(5));

    [McpServerTool(Name = "solution_save"), Description("Saves all projects and the solution.")]
    public Task<CallToolResult> Save()
        => session.RunAsync(s => { s.SaveAll(); return new { saved = s.Dte.Solution.FullName }; }, TimeSpan.FromMinutes(2));

    [McpServerTool(Name = "solution_close", Destructive = true), Description("Closes the open solution.")]
    public Task<CallToolResult> CloseSolution(bool save = true)
        => session.RunAsync(s => { s.CloseSolution(save); return new { closed = true }; }, TimeSpan.FromMinutes(2));

    [McpServerTool(Name = "solution_list_projects", ReadOnly = true), Description("Lists all projects in the solution (TwinCAT and others) and marks the selected one.")]
    public Task<CallToolResult> ListProjects()
        => session.RunAsync(s =>
        {
            var selected = s.HasProject ? s.Project.FullName : null;
            var tc = s.EnumerateTwinCatProjects().Select(p => p.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return s.EnumerateAllProjects()
                .Where(p => p.Kind != EnvDTE80.ProjectKinds.vsProjectKindSolutionFolder)
                .Select(p => new
                {
                    name = p.Name,
                    file = p.FullName,
                    isTwinCat = tc.Contains(p.FullName),
                    selected = string.Equals(p.FullName, selected, StringComparison.OrdinalIgnoreCase),
                }).ToArray();
        });

    [McpServerTool(Name = "solution_select_project"), Description("Selects which TwinCAT project (.tsproj) subsequent tools operate on.")]
    public Task<CallToolResult> SelectProject([Description("Project name or .tsproj file name")] string name)
        => session.RunAsync(s => s.SelectProject(name));

    [McpServerTool(Name = "solution_add_twincat_project"), Description("Adds another TwinCAT XAE project (.tsproj) to the open solution and selects it.")]
    public Task<CallToolResult> AddTwinCatProject(string name)
        => session.RunAsync(s => s.AddTwinCatProject(name), TimeSpan.FromMinutes(3));
}
