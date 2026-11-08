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
    /// <remarks>
    /// Delegates to the single canonical resolver <see cref="Plc.PlcTree.ResolveProject"/>: an empty name is only accepted
    /// when exactly one PLC project exists, so online operations can never silently pick the wrong PLC.
    /// </remarks>
    public static string NestedProjectPath(XaeSession s, string plcProject)
        => Plc.PlcTree.ResolveProject(s, plcProject).ProjectPath;

    /// <summary>"Plc1" or any path below it -> "TIPC^Plc1" (project root, implements ITcPlcProject).</summary>
    public static string PlcRootPath(XaeSession s, string plcProject)
        => Plc.PlcTree.ResolveProject(s, plcProject).RootPath;

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

    /// <summary>
    /// Runs an ITcSysManager runtime command (activate / restart), retrying E_ACCESSDENIED for a while: right after the
    /// target was restarted by someone else (e.g. via ADS) the XAE refuses these commands until it has noticed the new
    /// runtime state (verified on 4026: StartRestartTwinCAT directly after an ADS Reset -> E_ACCESSDENIED).
    /// </summary>
    public static void WhenXaeIsReady(Action command, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
        while (true)
        {
            try
            {
                command();
                return;
            }
            catch (Exception ex) when (IsAccessDenied(ex) && DateTime.UtcNow < deadline)
            {
                System.Threading.Thread.Sleep(1000);
            }
        }
    }

    private static bool IsAccessDenied(Exception ex)
        => ex is UnauthorizedAccessException || (ex is COMException com && unchecked((uint)com.HResult) == 0x80070005);

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
