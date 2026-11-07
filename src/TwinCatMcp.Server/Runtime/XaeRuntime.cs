using System.Runtime.InteropServices;
using ModelContextProtocol.Protocol;
using TCatSysManagerLib;
using TwinCatMcp.Core;

namespace TwinCatMcp.Runtime;

/// <summary>
/// COM helpers for work package 3 (system/runtime). All members that take an <see cref="XaeSession"/> must run on the STA
/// (inside <c>session.RunAsync</c>).
/// </summary>
public static class XaeRuntime
{
    // Note: ITcSmCommands2 is NOT reachable on TcXaeShell 4026 (DTE.GetObject("TcSmCommands") -> DISP_E_MEMBERNOTFOUND, the coclass is
    // not registered, and IServiceProvider.QueryService blocked the IDE). Config/run mode and state therefore use ADS + ITcSysManager.

    /// <summary>ITcRemoteManager of the IDE (TwinCAT XAE version selection).</summary>
    public static ITcRemoteManager RemoteManager(XaeSession s)
        => GetDteObject<ITcRemoteManager>(s, "TcRemoteManager");

    private static T GetDteObject<T>(XaeSession s, string name) where T : class
    {
        object? obj;
        try
        {
            obj = s.Dte.GetObject(name);
        }
        catch (COMException ex)
        {
            throw new TcException($"The XAE does not provide '{name}' ({ex.Message}).") { Hint = "Use a TwinCAT XAE (TcXaeShell 4026) instance." };
        }

        return obj as T ?? throw new TcException($"DTE object '{name}' does not implement {typeof(T).Name}.");
    }

    public static string TargetNetId(XaeSession s) => s.SysManager.GetTargetNetId();

    /// <summary>"PlcTask" or "TIRT^PlcTask" -> "TIRT^PlcTask".</summary>
    public static string TaskPath(string task)
    {
        if (string.IsNullOrWhiteSpace(task))
        {
            throw new TcException("Task name must not be empty.") { Hint = "Use the task name (e.g. 'PlcTask') or its path 'TIRT^PlcTask'. task_list shows all tasks." };
        }

        return task.Contains('^') ? task.Trim() : "TIRT^" + task.Trim();
    }

    /// <summary>"Plc1" -> "TIPC^Plc1^Plc1 Project" (nested project); full paths are returned unchanged.</summary>
    public static string NestedProjectPath(XaeSession s, string plcProject)
    {
        if (string.IsNullOrWhiteSpace(plcProject))
        {
            return NestedProjects(s).FirstOrDefault()?.PathName ?? throw NoPlc();
        }

        var p = plcProject.Trim();
        var parts = p.Split('^');
        if (parts.Length > 2)
        {
            return p;
        }

        var rootPath = parts.Length == 2 ? p : $"TIPC^{p}";
        var nested = Try(() => (s.Lookup(rootPath) as ITcProjectRoot)?.NestedProject?.PathName);
        return nested ?? $"{rootPath}^{rootPath[(rootPath.LastIndexOf('^') + 1)..]} Project";
    }

    /// <summary>"Plc1" or any path below it -> "TIPC^Plc1" (project root, implements ITcPlcProject).</summary>
    public static string PlcRootPath(XaeSession s, string plcProject)
    {
        if (string.IsNullOrWhiteSpace(plcProject))
        {
            return FirstPlcRoot(s) ?? throw NoPlc();
        }

        var parts = plcProject.Trim().Split('^');
        return parts.Length >= 2 ? $"{parts[0]}^{parts[1]}" : $"TIPC^{parts[0]}";
    }

    /// <summary>
    /// All nested PLC projects ("TIPC^P^P Project"). They are not enumerated as children of the project root
    /// (whose only child is "P Instance"), so ITcProjectRoot.NestedProject is used.
    /// </summary>
    public static IEnumerable<ITcSmTreeItem> NestedProjects(XaeSession s)
    {
        foreach (var root in TreeInfo.Children(s.Lookup("TIPC")))
        {
            var nested = Try(() => (root as ITcProjectRoot)?.NestedProject);
            if (nested is not null)
            {
                yield return nested;
            }
        }
    }

    private static string? FirstPlcRoot(XaeSession s)
        => TreeInfo.Children(s.Lookup("TIPC")).Select(c => c.PathName).FirstOrDefault();

    private static TcException NoPlc() => new("The project has no PLC project.") { Hint = "Create one first (plc_create_project or tree_create_child on TIPC)." };

    public static string? LastXmlError(ITcSmTreeItem item)
    {
        try
        {
            var e = ((ITcSmTreeItem3)item).GetLastXmlError();
            return string.IsNullOrWhiteSpace(e) ? null : e.Trim();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>ConsumeXml + returns the last XML error (null when everything was applied).</summary>
    public static string? Consume(ITcSmTreeItem item, string xml)
    {
        item.ConsumeXml(xml);
        return LastXmlError(item);
    }

    /// <summary>
    /// Runs the COM part on the STA (with the usual error handling) and then an async continuation off the STA
    /// (e.g. waiting for an ADS state). Exceptions of the continuation become tool errors as well.
    /// </summary>
    public static async Task<CallToolResult> RunThenAsync<T>(XaeSession session, Func<XaeSession, T> com, Func<T, Task<object?>> after, int timeoutSeconds = 0)
    {
        T value = default!;
        var first = await session.RunAsync(s => { value = com(s); return null; }, timeoutSeconds).ConfigureAwait(false);
        if (first.IsError == true)
        {
            return first;
        }

        try
        {
            return ToolResult.Ok(await after(value).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            return ToolResult.Error(ex);
        }
    }

    public static T? Try<T>(Func<T> f)
    {
        try
        {
            return f();
        }
        catch
        {
            return default;
        }
    }
}
