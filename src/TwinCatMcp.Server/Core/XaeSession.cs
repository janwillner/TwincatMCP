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

    /// <summary>How xae_start launched the IDE: "process" (dedicated process) or "com" (COM activation); null when attached.</summary>
    public string? LaunchedAs { get; private set; }

    /// <summary>SilentMode of an attached (foreign) IDE before the server enabled it; restored on detach.</summary>
    private bool? _silentModeBeforeAttach;

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
            IReadOnlyList<object>? dialogs = null;
            if (ex is TimeoutException || IsCallRejected(ex))
            {
                // A modal message box in the IDE blocks every COM call (even in SilentMode) - report its text.
                // Window enumeration is plain Win32, no COM, so it works while the STA is blocked.
                var found = TwinCatMcp.Runtime.XaeWindows.ModalDialogs(ProcessId);
                dialogs = found.Count > 0 ? found : null;
            }
            else
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

            return ToolResult.Error(ex, tcLog, dialogs);
        }
    }

    private static bool IsCallRejected(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is COMException com && unchecked((uint)com.HResult) is 0x80010001 or 0x8001010A)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True for failures of the whole session (IDE crashed/closed, nothing connected). Tools that report errors per item
    /// must let these propagate (<c>catch (Exception ex) when (!XaeSession.IsSessionFatal(ex))</c>) so the client sees a
    /// tool error, and a crashed IDE resets the session.
    /// </summary>
    public static bool IsSessionFatal(Exception ex) => IsIdeGone(ex) || ex is XaeNotConnectedException;

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

    private static TcException NotConnected() => new XaeNotConnectedException("No TwinCAT XAE instance is connected.")
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

    /// <summary>
    /// Starts a new IDE. Default launch mode "com": COM activation lets Windows start a new TcXaeShell (-Embedding);
    /// its process is identified by the main window and owned only if it is new. Launch mode "process" starts the
    /// executable with -Embedding itself and attaches via the ROT - such an unclaimed automation server quits after
    /// ~8 s, so the ROT is polled quickly (verified on 4026: registration after 2-5 s on a cold start).
    /// </summary>
    public object Start(string? progId, bool visible, bool keepOpenAfterExit, string? launchMode = null)
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

        var mode = string.IsNullOrWhiteSpace(launchMode) ? "com" : launchMode.Trim().ToLowerInvariant();
        if (mode is not ("com" or "process"))
        {
            throw new TcException($"Unknown launchMode '{launchMode}'.") { Hint = "Use 'com' (default) or 'process'." };
        }

        var useProcess = mode == "process";
        if (useProcess && ComRegistration.LocalServerExe(type.GUID) is { } exe)
        {
            try
            {
                return StartAsProcess(progId, exe, visible, keepOpenAfterExit);
            }
            catch (Exception ex) when (IsIdeGone(ex) || ex is IdeStartupExitedException)
            {
                // A cold start occasionally ends the first process during initialization - one fresh attempt.
                _logger.LogWarning(ex, "IDE process ended during startup - starting a new one");
                ReleaseDte();
                return StartAsProcess(progId, exe, visible, keepOpenAfterExit);
            }
        }

        if (useProcess)
        {
            _logger.LogWarning("LocalServer32 executable of {ProgId} not found - falling back to COM activation", progId);
        }

        _logger.LogInformation("Starting {ProgId} via COM activation (visible={Visible})", progId, visible);
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
            LaunchedAs = "com";
            dte.SuppressUI = !visible;
            dte.MainWindow.Visible = visible;
            dte.UserControl = keepOpenAfterExit;
        }
        else
        {
            _logger.LogWarning("CreateInstance returned an already running IDE (pid {Pid}); it is treated as attached, not owned.", pid);
            _silentModeBeforeAttach = TryGetSilentMode();
        }

        ConfigureSilentMode(true);
        return Status();
    }

    private System.Diagnostics.Process? _ownedProcess;

    /// <summary>Seconds to wait for a freshly started IDE process to register its DTE in the ROT.</summary>
    public static int StartupTimeoutSeconds { get; set; } = 240;

    private static RunningObjectTable.DteEntry WaitForRotEntry(System.Diagnostics.Process process, DateTime deadline)
    {
        while (true)
        {
            if (process.HasExited)
            {
                throw new IdeStartupExitedException($"The IDE process exited during startup (exit code {process.ExitCode}).") { Hint = "Retry xae_start." };
            }

            if (DateTime.UtcNow > deadline)
            {
                try { process.Kill(); } catch { /* ignore */ }
                throw new TcException($"The IDE did not register in the Running Object Table within {StartupTimeoutSeconds} s.")
                {
                    Hint = "Retry xae_start, or use launchMode='com'.",
                };
            }

            System.Threading.Thread.Sleep(200);
            if (RunningObjectTable.GetDteInstances().FirstOrDefault(e => e.ProcessId == process.Id) is { } entry)
            {
                return entry;
            }
        }
    }

    private object StartAsProcess(string progId, string exe, bool visible, bool keepOpenAfterExit)
    {
        _logger.LogInformation("Starting {Exe} as dedicated process (visible={Visible})", exe, visible);

        // "-Embedding" starts the shell as automation server exactly like COM activation does (not under user control,
        // so the window can be hidden; it exits when the server releases it) - but we know its process id.
        // Started without it, the shell is user-controlled and its window cannot be hidden (DTE refuses).
        var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "-Embedding")
        {
            UseShellExecute = true,
            WindowStyle = visible ? System.Diagnostics.ProcessWindowStyle.Normal : System.Diagnostics.ProcessWindowStyle.Minimized,
        }) ?? throw new TcException($"Could not start {exe}.");

        var deadline = DateTime.UtcNow.AddSeconds(StartupTimeoutSeconds);
        DTE2 dte;
        string entryProgId;
        while (true)
        {
            var entry = WaitForRotEntry(process, deadline);
            try
            {
                // The shell may still be initializing its packages - the message filter retries rejected calls.
                dte = (DTE2)entry.Dte;
                dte.SuppressUI = !visible;
                dte.MainWindow.Visible = visible;
                dte.UserControl = keepOpenAfterExit;
                entryProgId = entry.ProgId;
                break;
            }
            catch (Exception ex) when (IsIdeGone(ex) && !process.HasExited && DateTime.UtcNow < deadline)
            {
                // On a cold start the DTE registered first is disconnected while the shell finishes initializing
                // (RPC_E_DISCONNECTED) - wait for its final registration.
                _logger.LogInformation("DTE of pid {Pid} was disconnected during startup - waiting for re-registration", process.Id);
                System.Threading.Thread.Sleep(200);
            }
        }

        _dte = dte;
        _ownedProcess = process;
        ProgId = entryProgId.Length > 0 ? entryProgId : progId;
        ProcessId = process.Id;
        OwnsInstance = true;
        LaunchedAs = "process";
        ConfigureSilentMode(true);
        return Status();
    }

    /// <summary>After Quit(): make sure a process this server started really ends (never touches foreign IDEs).</summary>
    private void EnsureOwnedProcessExited()
    {
        var p = _ownedProcess;
        _ownedProcess = null;
        if (p is null)
        {
            return;
        }

        try
        {
            if (!p.WaitForExit(30_000))
            {
                _logger.LogWarning("Owned IDE process {Pid} did not exit after Quit - terminating it", p.Id);
                p.Kill();
            }
        }
        catch
        {
            // already gone
        }
        finally
        {
            p.Dispose();
        }
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
                    using var shortRetries = MessageFilter.ShortRetries(3000);
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
        _silentModeBeforeAttach = TryGetSilentMode();
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

        // Never close the user's solution of an attached IDE without explicit quitIde.
        var quit = quitIde || OwnsInstance;
        try
        {
            if (saveAll)
            {
                SaveAll();
            }

            if (quit)
            {
                if (_dte.Solution.IsOpen)
                {
                    _dte.Solution.Close(saveAll);
                }

                _dte.Quit();
            }
            else
            {
                RestoreSilentMode();
            }
        }
        finally
        {
            ReleaseDte(); // keeps _ownedProcess
            if (quit)
            {
                EnsureOwnedProcessExited();
            }
            else
            {
                _ownedProcess?.Dispose();
                _ownedProcess = null;
            }
        }

        return new { closed = true, quit };
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
        LaunchedAs = null;
        _silentModeBeforeAttach = null;
    }

    /// <summary>Gives an attached (foreign) IDE back the SilentMode it had before the server connected.</summary>
    private void RestoreSilentMode()
    {
        if (_silentModeBeforeAttach is { } before)
        {
            ConfigureSilentMode(before);
        }
    }

    private bool? TryGetSilentMode()
    {
        try
        {
            return ((ITcAutomationSettings)Dte.GetObject("TcAutomationSettings")).SilentMode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "TcAutomationSettings not available");
            return null;
        }
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
            launchedAs = LaunchedAs,
            silentMode = TryGetSilentMode(),
            silentModeBeforeAttach = _silentModeBeforeAttach,
            version = _dte.Version,
            visible = SafeGet(() => _dte.MainWindow.Visible),
            solutionOpen,
            solution,
            project = _project is null ? null : new { name = _project.Name, file = _project.FullName },
            targetNetId,
            twinCatProjects = solutionOpen ? SafeGet(() => EnumerateTwinCatProjects().Select(p => p.Name).ToArray()) ?? [] : [],
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
        var loaded = WaitUntilSolutionLoaded(TimeSpan.FromSeconds(120));
        TrySelectFirstTwinCatProject();
        return new
        {
            solution = Dte.Solution.FullName,
            fullyLoaded = loaded,
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
        return SelectProject(project);
    }

    /// <summary>Selects a project object directly (e.g. one just added, before the solution enumerates it).</summary>
    public object SelectProject(Project project)
    {
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

    /// <summary>
    /// Solution.Open returns before all projects are initialized: TwinCAT projects and the nested PLC projects
    /// become available asynchronously. Polls until every TwinCAT project exposes its System Manager and every PLC
    /// project root its nested project (or the timeout elapses).
    /// </summary>
    public bool WaitUntilSolutionLoaded(TimeSpan timeout)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var stableSince = TimeSpan.Zero;
        while (sw.Elapsed < timeout)
        {
            try
            {
                var projects = EnumerateAllProjects().ToList();
                var tcProjects = EnumerateTwinCatProjects().ToList();
                var allReady = projects.Count > 0 && tcProjects.All(p =>
                {
                    var sm = (ITcSysManager18)p.Object;
                    var plcRoot = sm.LookupTreeItem("TIPC");
                    return TreeInfo.Children(plcRoot).All(c => c is not ITcProjectRoot root || SafeGet(() => root.NestedProject) is not null);
                });

                // Solutions without a TwinCAT project: accept once the project list is stable for a moment.
                if (allReady && (tcProjects.Count > 0 || sw.Elapsed - stableSince > TimeSpan.FromSeconds(5)))
                {
                    return true;
                }

                if (!allReady)
                {
                    stableSince = sw.Elapsed;
                }
            }
            catch (Exception ex) when (ex is COMException or NullReferenceException or InvalidCastException)
            {
                stableSince = sw.Elapsed; // still loading
            }

            System.Threading.Thread.Sleep(500);
        }

        _logger.LogWarning("Solution did not report fully loaded within {Timeout}", timeout);
        return false;
    }

    public IEnumerable<Project> EnumerateAllProjects()
    {
        if (_dte is null || !_dte.Solution.IsOpen)
        {
            yield break;
        }

        // While a solution is (re)loading, the collection can contain null entries or half-initialized projects.
        var stack = new Stack<Project>();
        foreach (Project? p in _dte.Solution.Projects)
        {
            if (p is not null)
            {
                stack.Push(p);
            }
        }

        while (stack.Count > 0)
        {
            var p = stack.Pop();
            yield return p;
            if (SafeGet(() => p.Kind) == ProjectKinds.vsProjectKindSolutionFolder && SafeGet(() => p.ProjectItems) is { } items)
            {
                foreach (ProjectItem? item in items)
                {
                    if (SafeGet(() => item?.SubProject) is { } sub)
                    {
                        stack.Push(sub);
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

    public static T? SafeGet<T>(Func<T> f)
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
                var quit = false;
                if (OwnsInstance && _dte is not null)
                {
                    try { quit = !_dte.UserControl; } catch { quit = true; }
                    if (quit)
                    {
                        try { _dte.Quit(); } catch { /* ignore */ }
                    }
                }

                if (!quit && _dte is not null)
                {
                    RestoreSilentMode();
                }

                ReleaseDte();
                if (quit)
                {
                    EnsureOwnedProcessExited();
                }
            }, TimeSpan.FromSeconds(45)).Wait();
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
