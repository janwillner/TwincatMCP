using System.Runtime.InteropServices;
using EnvDTE;
using EnvDTE80;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using TCatSysManagerLib;

namespace TwinCatMcp.Core;

/// <summary>
/// Owns the connection to one TwinCAT XAE (TcXaeShell / Visual Studio) instance and
/// the currently selected TwinCAT (.tsproj) project inside its solution.
/// All members that touch COM must be called on the STA thread; use
/// <see cref="RunAsync"/> from tools, which handles marshalling and error reporting.
/// </summary>
public sealed class XaeSession : IDisposable
{
    public const string DefaultProgId = "TcXaeShell.DTE.17.0";
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    private readonly StaComThread _sta;
    private readonly ILogger<XaeSession> _logger;
    private DTE2? _dte;
    private Project? _project;

    public XaeSession(StaComThread sta, ILogger<XaeSession> logger)
    {
        _sta = sta;
        _logger = logger;
    }

    public StaComThread Sta => _sta;

    /// <summary>ProgID used to start/attach the IDE, e.g. TcXaeShell.DTE.17.0.</summary>
    public string? ProgId { get; private set; }

    /// <summary>True when this server started the IDE process (and may quit it).</summary>
    public bool OwnsInstance { get; private set; }

    public int? ProcessId { get; private set; }

    public bool IsConnected => _dte is not null;

    // ------------------------------------------------------------------ execution

    /// <summary>
    /// Runs <paramref name="work"/> on the COM STA thread and converts the outcome into a tool result.
    /// Exceptions become error results that include the TwinCAT System Manager error log.
    /// </summary>
    public async Task<CallToolResult> RunAsync(Func<XaeSession, object?> work, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        try
        {
            var payload = await _sta.InvokeAsync(() => work(this), timeout ?? DefaultTimeout, ct).ConfigureAwait(false);
            return ToolResult.Ok(payload);
        }
        catch (Exception ex) when (IsIdeGone(ex))
        {
            _logger.LogWarning(ex, "IDE process is no longer reachable - dropping session");
            await _sta.InvokeAsync(ReleaseDte, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            return ToolResult.Error(new TcException("The TwinCAT XAE process is no longer reachable (crashed or closed). The session was reset.", ex)
            {
                Hint = "Call xae_start (or xae_attach) and solution_open again.",
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tool execution failed");
            string? tcLog = null;
            if (ex is not TimeoutException)
            {
                try
                {
                    tcLog = await _sta.InvokeAsync(TryGetLastErrorMessages, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
                catch
                {
                    // ignore - best effort
                }
            }

            return ToolResult.Error(ex, tcLog);
        }
    }

    private static bool IsIdeGone(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is COMException com && unchecked((uint)com.HResult) is 0x800706BA or 0x800706BE or 0x80010108)
            {
                return true; // RPC_S_SERVER_UNAVAILABLE, RPC_S_CALL_FAILED, RPC_E_DISCONNECTED
            }

            if (e is InvalidComObjectException)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Like <see cref="RunAsync"/> but takes a timeout in seconds (0 or less = default).</summary>
    public Task<CallToolResult> RunAsync(Func<XaeSession, object?> work, int timeoutSeconds, CancellationToken ct = default)
        => RunAsync(work, timeoutSeconds > 0 ? TimeSpan.FromSeconds(timeoutSeconds) : null, ct);

    // ------------------------------------------------------------------ accessors (STA only)

    public DTE2 Dte => _dte ?? throw NotConnected();

    private static TcException NotConnected() => new("No TwinCAT XAE instance is connected.")
    {
        Hint = "Call xae_start (new IDE) or xae_attach (running IDE) first, then solution_open or solution_create.",
    };

    public Solution2 Solution => (Solution2)Dte.Solution;

    public Project Project => _project ?? (_dte is null ? throw NotConnected() : (Project?)null) ?? throw new TcException("No TwinCAT project (.tsproj) is selected.")
    {
        Hint = "Call solution_open or solution_create, or solution_select_project if the solution has several TwinCAT projects.",
    };

    public ITcSysManager18 SysManager => (ITcSysManager18)Project.Object;

    public bool HasProject => _project is not null;

    /// <summary>Looks up a tree item by its path (e.g. "TIPC^MyPlc^MyPlc Project^POUs^MAIN").</summary>
    public ITcSmTreeItem Lookup(string treePath)
    {
        if (string.IsNullOrWhiteSpace(treePath))
        {
            throw new TcException("treePath must not be empty.");
        }

        if (!SysManager.TryLookupTreeItem(treePath, out var item) || item is null)
        {
            throw new TcException($"Tree item not found: '{treePath}'.")
            {
                Hint = "Use tree_get_children / plc_find_elements to discover valid paths. Separator is '^'. " +
                       "Root shortcuts: TIRC (real-time), TIRT (tasks), TIPC (PLC), TINC (motion), TIID (I/O devices), TIRR (routes).",
            };
        }

        return item;
    }

    public string? TryGetLastErrorMessages()
    {
        try
        {
            return _project is null ? null : SysManager.GetLastErrorMessages();
        }
        catch
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ lifecycle (STA only)

    public object Start(string? progId, bool visible, bool keepOpenAfterExit)
    {
        if (_dte is not null)
        {
            throw new TcException("An XAE instance is already connected.") { Hint = "Call xae_close first." };
        }

        progId = string.IsNullOrWhiteSpace(progId) ? DefaultProgId : progId;
        var type = Type.GetTypeFromProgID(progId)
                   ?? throw new TcException($"ProgID '{progId}' is not registered.")
                   {
                       Hint = "Use TcXaeShell.DTE.17.0 (TwinCAT XAE Shell 4026) or VisualStudio.DTE.17.0/18.0 (VS integration).",
                   };

        _logger.LogInformation("Starting {ProgId} (visible={Visible})", progId, visible);
        var before = System.Diagnostics.Process.GetProcesses().Select(p => p.Id).ToHashSet();
        var dte = (DTE2)(Activator.CreateInstance(type) ?? throw new TcException($"Could not create {progId}."));

        // Determine the IDE process from its main window - robust even when several IDEs start concurrently.
        var pid = ProcessIdOf(dte);
        var isNewProcess = pid is > 0 && !before.Contains(pid.Value);

        _dte = dte;
        ProgId = progId;
        ProcessId = pid;

        // Only claim ownership (and therefore the right to Quit() it later) for a freshly spawned process.
        OwnsInstance = isNewProcess;
        if (isNewProcess)
        {
            dte.SuppressUI = !visible;
            dte.MainWindow.Visible = visible;
            dte.UserControl = keepOpenAfterExit;
        }
        else
        {
            _logger.LogWarning("CreateInstance returned an already running IDE (pid {Pid}); it is treated as attached, not owned.", pid);
        }

        ConfigureSilentMode(true);
        return Status();
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private static int? ProcessIdOf(DTE2 dte)
    {
        try
        {
            var hwnd = new IntPtr(dte.MainWindow.HWnd);
            return GetWindowThreadProcessId(hwnd, out var pid) != 0 && pid != 0 ? (int)pid : null;
        }
        catch
        {
            return null;
        }
    }

    public object Attach(string? solutionPathOrPid)
    {
        if (_dte is not null)
        {
            throw new TcException("An XAE instance is already connected.") { Hint = "Call xae_close first." };
        }

        var instances = RunningObjectTable.GetDteInstances();
        if (instances.Count == 0)
        {
            throw new TcException("No running Visual Studio / TcXaeShell instance found in the Running Object Table.")
            {
                Hint = "Start one with xae_start.",
            };
        }

        RunningObjectTable.DteEntry? match = null;
        if (string.IsNullOrWhiteSpace(solutionPathOrPid))
        {
            match = instances.FirstOrDefault(i => i.ProgId.StartsWith("TcXaeShell", StringComparison.OrdinalIgnoreCase)) ?? instances[0];
        }
        else if (int.TryParse(solutionPathOrPid, out var pid))
        {
            match = instances.FirstOrDefault(i => i.ProcessId == pid);
        }
        else
        {
            var wanted = Path.GetFullPath(solutionPathOrPid);
            match = instances.FirstOrDefault(i =>
            {
                try
                {
                    var full = ((DTE2)i.Dte).Solution.FullName;
                    return !string.IsNullOrEmpty(full) && string.Equals(Path.GetFullPath(full), wanted, StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return false;
                }
            });
        }

        if (match is null)
        {
            throw new TcException($"No running IDE matches '{solutionPathOrPid}'.") { Hint = "Call xae_list_instances." };
        }

        _dte = (DTE2)match.Dte;
        ProgId = match.ProgId;
        ProcessId = match.ProcessId;
        OwnsInstance = false;
        ConfigureSilentMode(true);
        TrySelectFirstTwinCatProject();
        return Status();
    }

    public object Close(bool saveAll, bool quitIde)
    {
        if (_dte is null)
        {
            return new { closed = false, message = "No XAE instance connected." };
        }

        try
        {
            if (saveAll)
            {
                SaveAll();
            }

            if (quitIde || OwnsInstance)
            {
                // Never close the user's solution of an attached IDE without explicit quitIde.
                if (_dte.Solution.IsOpen)
                {
                    _dte.Solution.Close(saveAll);
                }

                _dte.Quit();
            }
        }
        finally
        {
            ReleaseDte();
        }

        return new { closed = true };
    }

    public void ReleaseDte()
    {
        if (_dte is not null)
        {
            try
            {
                Marshal.FinalReleaseComObject(_dte);
            }
            catch
            {
                // ignore
            }
        }

        _dte = null;
        _project = null;
        ProgId = null;
        ProcessId = null;
        OwnsInstance = false;
    }

    public void ConfigureSilentMode(bool silent)
    {
        try
        {
            var settings = (ITcAutomationSettings)Dte.GetObject("TcAutomationSettings");
            settings.SilentMode = silent;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "TcAutomationSettings not available");
        }
    }

    public object Status()
    {
        if (_dte is null)
        {
            return new { connected = false };
        }

        string? solution = null;
        bool solutionOpen = false;
        try
        {
            solutionOpen = _dte.Solution.IsOpen;
            solution = solutionOpen ? _dte.Solution.FullName : null;
        }
        catch (COMException ex)
        {
            return new { connected = false, error = "IDE not reachable: " + ex.Message };
        }

        string? targetNetId = null;
        if (_project is not null)
        {
            try { targetNetId = SysManager.GetTargetNetId(); } catch { /* ignore */ }
        }

        return new
        {
            connected = true,
            progId = ProgId,
            processId = ProcessId,
            ownsInstance = OwnsInstance,
            version = _dte.Version,
            visible = SafeGet(() => _dte.MainWindow.Visible),
            solutionOpen,
            solution,
            project = _project is null ? null : new { name = _project.Name, file = _project.FullName },
            targetNetId,
            twinCatProjects = solutionOpen ? EnumerateTwinCatProjects().Select(p => p.Name).ToArray() : [],
        };
    }

    // ------------------------------------------------------------------ solution handling (STA only)

    public static string TwinCatDir =>
        Environment.GetEnvironmentVariable("TWINCAT3DIR") is { Length: > 0 } dir
            ? dir
            : @"C:\Program Files (x86)\Beckhoff\TwinCAT\3.1\";

    public static string TwinCatProjectTemplate => Path.Combine(TwinCatDir, @"Components\Base\PrjTemplate\TwinCAT Project.tsproj");

    public object OpenSolution(string solutionPath)
    {
        var full = Path.GetFullPath(solutionPath);
        if (!File.Exists(full))
        {
            throw new TcException($"File not found: {full}");
        }

        if (full.EndsWith(".tsproj", StringComparison.OrdinalIgnoreCase) || full.EndsWith(".tspproj", StringComparison.OrdinalIgnoreCase))
        {
            // Open a standalone TwinCAT project: create a throw-away solution around it.
            if (Dte.Solution.IsOpen)
            {
                Dte.Solution.Close(false);
            }

            Dte.Solution.Create(Path.GetDirectoryName(full)!, Path.GetFileNameWithoutExtension(full));
            Dte.Solution.AddFromFile(full);
        }
        else
        {
            Dte.Solution.Open(full);
        }

        _project = null;
        TrySelectFirstTwinCatProject();
        return new
        {
            solution = Dte.Solution.FullName,
            selectedProject = _project?.Name,
            twinCatProjects = EnumerateTwinCatProjects().Select(p => new { name = p.Name, file = p.FullName }).ToArray(),
        };
    }

    public object CreateSolution(string directory, string name, bool overwrite)
    {
        var root = Path.GetFullPath(directory);
        var slnDir = Path.Combine(root, name);
        if (Directory.Exists(slnDir) && Directory.EnumerateFileSystemEntries(slnDir).Any())
        {
            if (!overwrite)
            {
                throw new TcException($"Directory '{slnDir}' already exists and is not empty.") { Hint = "Pass overwrite=true or choose another name." };
            }

            Directory.Delete(slnDir, true);
        }

        Directory.CreateDirectory(slnDir);
        if (Dte.Solution.IsOpen)
        {
            Dte.Solution.Close(false);
        }

        Dte.Solution.Create(slnDir, name);
        var template = TwinCatProjectTemplate;
        if (!File.Exists(template))
        {
            throw new TcException($"TwinCAT project template not found: {template}");
        }

        var projectDir = Path.Combine(slnDir, name);
        _project = Dte.Solution.AddFromTemplate(template, projectDir, name);
        _project ??= EnumerateTwinCatProjects().FirstOrDefault();
        var slnFile = Path.Combine(slnDir, name + ".sln");
        Dte.Solution.SaveAs(slnFile);
        return new
        {
            solution = slnFile,
            project = _project?.FullName,
            projectName = _project?.Name,
        };
    }

    public object AddTwinCatProject(string name)
    {
        if (!Dte.Solution.IsOpen)
        {
            throw new TcException("No solution is open.");
        }

        var slnDir = Path.GetDirectoryName(Dte.Solution.FullName)!;
        var project = Dte.Solution.AddFromTemplate(TwinCatProjectTemplate, Path.Combine(slnDir, name), name);
        _project = project ?? EnumerateTwinCatProjects().First(p => p.Name == name);
        Dte.Solution.SaveAs(Dte.Solution.FullName);
        return new { project = _project.FullName, projectName = _project.Name };
    }

    public void SaveAll()
    {
        if (!Dte.Solution.IsOpen)
        {
            return;
        }

        foreach (var project in EnumerateAllProjects())
        {
            try
            {
                project.Save();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Saving project {Project} failed", SafeGet(() => project.Name));
            }
        }

        Dte.ExecuteCommandSafe("File.SaveAll");
        Dte.Solution.SaveAs(Dte.Solution.FullName);
    }

    public object SelectProject(string name)
    {
        var project = EnumerateTwinCatProjects().FirstOrDefault(p =>
                          string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(Path.GetFileName(p.FullName), name, StringComparison.OrdinalIgnoreCase))
                      ?? throw new TcException($"TwinCAT project '{name}' not found in solution.");
        _project = project;
        return new { selectedProject = project.Name, file = project.FullName };
    }

    public void CloseSolution(bool save)
    {
        if (Dte.Solution.IsOpen)
        {
            if (save)
            {
                SaveAll();
            }

            Dte.Solution.Close(save);
        }

        _project = null;
    }

    private void TrySelectFirstTwinCatProject()
    {
        try
        {
            _project ??= EnumerateTwinCatProjects().FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not enumerate TwinCAT projects");
        }
    }

    public IEnumerable<Project> EnumerateAllProjects()
    {
        if (_dte is null || !_dte.Solution.IsOpen)
        {
            yield break;
        }

        var stack = new Stack<Project>();
        foreach (Project p in _dte.Solution.Projects)
        {
            stack.Push(p);
        }

        while (stack.Count > 0)
        {
            var p = stack.Pop();
            yield return p;
            if (p.Kind == ProjectKinds.vsProjectKindSolutionFolder && p.ProjectItems is not null)
            {
                foreach (ProjectItem item in p.ProjectItems)
                {
                    if (item.SubProject is not null)
                    {
                        stack.Push(item.SubProject);
                    }
                }
            }
        }
    }

    public IEnumerable<Project> EnumerateTwinCatProjects()
        => EnumerateAllProjects().Where(p =>
        {
            try
            {
                return p.Object is ITcSysManager;
            }
            catch
            {
                return false;
            }
        });

    private static T? SafeGet<T>(Func<T> f)
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

    public void Dispose()
    {
        try
        {
            _sta.InvokeAsync(() =>
            {
                if (OwnsInstance && _dte is not null && !_dte.UserControl)
                {
                    try { _dte.Quit(); } catch { /* ignore */ }
                }

                ReleaseDte();
            }, TimeSpan.FromSeconds(20)).Wait();
        }
        catch
        {
            // ignore on shutdown
        }
    }
}

public static class DteExtensions
{
    /// <summary>Executes a DTE command and swallows "command not available" errors.</summary>
    public static bool ExecuteCommandSafe(this DTE2 dte, string command, string args = "")
    {
        try
        {
            dte.ExecuteCommand(command, args);
            return true;
        }
        catch (COMException)
        {
            return false;
        }
    }
}
