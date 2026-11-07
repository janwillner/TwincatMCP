using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TCatSysManagerLib;
using TwinCatMcp.Core;
using TwinCatMcp.Runtime;

namespace TwinCatMcp.Tools;

/// <summary>
/// PLC online operations through the XAE (ITcPlcOnline on the nested PLC project "TIPC^P^P Project") and boot project settings
/// (ITcPlcProject on "TIPC^P") - work package 3. The target must be in Run mode (system_activate_configuration).
/// </summary>
[McpServerToolType]
public sealed class PlcOnlineTools(XaeSession session)
{
    private const string PlcProjectDescription = "PLC project name (e.g. 'Plc1') or its tree path. Empty = first PLC project.";

    [McpServerTool(Name = "plc_online_login"), Description(
        "Logs the XAE into the PLC runtime of the target and downloads the application if needed (no dialogs). " +
        "mode: 'auto' = online change when possible, otherwise download; 'download' = full download (PLC goes to Stop, values reset); " +
        "'onlineChange' = only online change. The target must be in Run mode (system_activate_configuration). Start the PLC with plc_online_start.")]
    public Task<CallToolResult> Login(
        [Description(PlcProjectDescription)] string? plcProject = null,
        [Description("'auto', 'download' or 'onlineChange'.")] string mode = "auto",
        [Description("Compile the code before login (PLC_LOGIN_FLAGS_FORCECOMPILECODE).")] bool rebuild = false,
        int timeoutSeconds = 300)
        => session.RunAsync(s =>
        {
            var flags = PlcOnline.LoginFlags(mode, rebuild);
            var (path, online) = Online(s, plcProject);
            online.Login((PLC_LOGIN_FLAGS)flags);
            return State(path, online, s);
        }, timeoutSeconds);

    [McpServerTool(Name = "plc_online_logout"), Description("Logs the XAE out of the PLC runtime (the PLC keeps running).")]
    public Task<CallToolResult> Logout([Description(PlcProjectDescription)] string? plcProject = null)
        => session.RunAsync(s =>
        {
            var (path, online) = Online(s, plcProject);
            if (online.IsLoggedIn)
            {
                online.Logoff();
            }

            return State(path, online, s);
        });

    [McpServerTool(Name = "plc_online_start"), Description("Starts the PLC application (requires plc_online_login).")]
    public Task<CallToolResult> Start([Description(PlcProjectDescription)] string? plcProject = null)
        => session.RunAsync(s =>
        {
            var (path, online) = Online(s, plcProject, requireLogin: true);
            online.Start();
            return State(path, online, s);
        });

    [McpServerTool(Name = "plc_online_stop"), Description("Stops the PLC application (requires plc_online_login).")]
    public Task<CallToolResult> Stop([Description(PlcProjectDescription)] string? plcProject = null)
        => session.RunAsync(s =>
        {
            var (path, online) = Online(s, plcProject, requireLogin: true);
            online.Stop();
            return State(path, online, s);
        });

    [McpServerTool(Name = "plc_online_reset", Destructive = true), Description(
        "Resets the PLC application (requires plc_online_login): 'cold' = all variables except RETAIN/PERSISTENT to initial values, " +
        "'origin' = everything incl. RETAIN/PERSISTENT and the application is removed from the runtime (login with download afterwards).")]
    public Task<CallToolResult> Reset(
        [Description("'cold' or 'origin'.")] string type = "cold",
        [Description(PlcProjectDescription)] string? plcProject = null)
        => session.RunAsync(s =>
        {
            var resetType = PlcOnline.ResetType(type);
            var (path, online) = Online(s, plcProject, requireLogin: true);
            online.Reset((PLC_RESET_TYPE)resetType);
            return State(path, online, s);
        });

    [McpServerTool(Name = "plc_online_state", ReadOnly = true), Description(
        "Returns the online state of the PLC project in the XAE: logged in, application state (Run/Stop/...), operation state flags " +
        "(e.g. BootProjectValid, Exception) and the boot project autostart setting.")]
    public Task<CallToolResult> GetState([Description(PlcProjectDescription)] string? plcProject = null)
        => session.RunAsync(s =>
        {
            var (path, online) = Online(s, plcProject);
            return State(path, online, s);
        });

    [McpServerTool(Name = "plc_online_generate_boot_project"), Description(
        "Generates the PLC boot project (ITcPlcProject.GenerateBootProject). activate=true writes it to the target so the PLC starts " +
        "from it after a TwinCAT restart (with autostart, see plc_online_set_autostart).")]
    public Task<CallToolResult> GenerateBootProject(
        [Description(PlcProjectDescription)] string? plcProject = null,
        [Description("Activate the boot project on the target.")] bool activate = true,
        int timeoutSeconds = 300)
        => session.RunAsync(s =>
        {
            var root = XaeRuntime.PlcRootPath(s, plcProject ?? "");
            var project = AsPlcProject(s.Lookup(root), root);
            project.GenerateBootProject(activate);
            return new { plcProject = root, bootProjectGenerated = true, activated = activate, autostart = project.BootProjectAutostart };
        }, timeoutSeconds);

    [McpServerTool(Name = "plc_online_set_autostart", Idempotent = true), Description(
        "Enables/disables 'Autostart Boot Project' of a PLC project (ITcPlcProject.BootProjectAutostart). Takes effect with the next activation / boot project.")]
    public Task<CallToolResult> SetAutostart(
        [Description("true = the PLC starts automatically from its boot project.")] bool enabled,
        [Description(PlcProjectDescription)] string? plcProject = null)
        => session.RunAsync(s =>
        {
            var root = XaeRuntime.PlcRootPath(s, plcProject ?? "");
            var project = AsPlcProject(s.Lookup(root), root);
            project.BootProjectAutostart = enabled;
            return new { plcProject = root, autostart = project.BootProjectAutostart };
        });

    // ------------------------------------------------------------------ helpers

    private static (string Path, ITcPlcOnline Online) Online(XaeSession s, string? plcProject, bool requireLogin = false)
    {
        var path = XaeRuntime.NestedProjectPath(s, plcProject ?? "");
        var online = s.Lookup(path) as ITcPlcOnline
                     ?? throw new TcException($"'{path}' does not support PLC online access (ITcPlcOnline).")
                     {
                         Hint = "Pass the PLC project name ('Plc1') or the nested project path 'TIPC^Plc1^Plc1 Project'.",
                     };
        if (requireLogin && !online.IsLoggedIn)
        {
            throw new TcException($"Not logged in to '{path}'.") { Hint = "Call plc_online_login first." };
        }

        return (path, online);
    }

    private static ITcPlcProject AsPlcProject(ITcSmTreeItem item, string path)
        => item as ITcPlcProject ?? throw new TcException($"'{path}' is not a PLC project root (ITcPlcProject).") { Hint = "Use the project root 'TIPC^Plc1'." };

    private static object State(string path, ITcPlcOnline online, XaeSession s)
    {
        var op = XaeRuntime.Try(() => (int)online.OnlineOperationState);
        var root = XaeRuntime.Try(() => XaeRuntime.PlcRootPath(s, path));
        return new
        {
            plcProject = path,
            loggedIn = online.IsLoggedIn,
            appState = XaeRuntime.Try(() => online.OnlineApplicationState.ToString().Replace("PLC_APPSTATE_", "")),
            opState = PlcOnline.DescribeOpState(op),
            autostart = root is null ? null : XaeRuntime.Try<bool?>(() => (s.Lookup(root) as ITcPlcProject)?.BootProjectAutostart),
        };
    }
}
