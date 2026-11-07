using System.Diagnostics;
using Microsoft.Win32;

namespace TwinCatMcp.Tests.Integration;

/// <summary>
/// XAE fixture for work package 3 (system / runtime / tasks): solution "McpIt" plus a PLC project "Plc1"
/// created from the "Standard PLC Template" via tree_create_child (independent of the plc_ tools of package 1).
/// <para>
/// The IDE is launched as a plain process and attached by PID (xae_attach) instead of COM activation (xae_start):
/// a running TcXaeShell registers its class object, so CreateInstance("TcXaeShell.DTE.17.0") is routed to that
/// instance and blocks while it is busy (seen with several IDEs from parallel test runs). The fixture quits - and if
/// necessary kills - only the process it started.
/// </para>
/// </summary>
public class RuntimeProjectFixture : McpServerFixture
{
    public string WorkDir { get; } = Path.Combine(Path.GetTempPath(), "twincat-mcp-it", "wp3-" + Guid.NewGuid().ToString("N")[..8]);

    public string PlcProjectPath => $"TIPC^{XaeProjectFixture.PlcName}^{XaeProjectFixture.PlcName} Project";

    /// <summary>Tree path of the PLC task reference of the standard template (item type 621, XML PlcTaskDef/LinkedTask).</summary>
    public string PlcTaskReferencePath => $"{PlcProjectPath}^PlcTask";

    private Process? _ide;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Directory.CreateDirectory(WorkDir);
        _ide = Process.Start(new ProcessStartInfo(XaeShellExe) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Minimized })
               ?? throw new InvalidOperationException("TcXaeShell.exe could not be started.");

        // The IDE registers "!TcXaeShell.DTE.17.0:<pid>" in the ROT once it is ready.
        var deadline = DateTime.UtcNow.AddMinutes(3);
        while (true)
        {
            var (json, isError) = await CallRaw("xae_attach", new { solutionPathOrProcessId = _ide.Id.ToString() });
            if (!isError)
            {
                break;
            }

            if (DateTime.UtcNow > deadline || _ide.HasExited)
            {
                throw new ToolFailedException("xae_attach", json.ToString());
            }

            await Task.Delay(2000);
        }

        await CallRaw("xae_set_visible", new { visible = false });
        await Call("solution_create", new { directory = WorkDir, name = XaeProjectFixture.SolutionName });
        await Call("tree_create_child", new { parentPath = "TIPC", name = XaeProjectFixture.PlcName, subType = 0, info = "Standard PLC Template" }, TimeSpan.FromMinutes(3));
    }

    public override async Task DisposeAsync()
    {
        try
        {
            await CallRaw("xae_close", new { saveAll = false, quitIde = true }, TimeSpan.FromMinutes(2));
        }
        catch
        {
            // ignore
        }

        await base.DisposeAsync();
        if (_ide is not null)
        {
            if (!_ide.WaitForExit(30_000))
            {
                _ide.Kill(); // only the instance this fixture started
            }

            _ide.Dispose();
        }

        try
        {
            Directory.Delete(WorkDir, true);
        }
        catch
        {
            // files may still be locked briefly
        }
    }

    private static string XaeShellExe
    {
        get
        {
            using var key = Registry.ClassesRoot.OpenSubKey(@"CLSID\{47f09213-63bf-4966-9a9f-cf03a1e94334}\LocalServer32");
            var path = (key?.GetValue(null) as string)?.Trim('"');
            return File.Exists(path) ? path! : @"C:\Program Files\Beckhoff\TcXaeShell\Common7\IDE\TcXaeShell.exe";
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)] // changes machine-wide state (library repositories / runtimes)
public sealed class RuntimeCollection : ICollectionFixture<RuntimeProjectFixture>
{
    public const string Name = "Runtime";
}

/// <summary>
/// Runtime tests (activation, PLC login/start, starting/stopping Usermode Runtimes) need a licensed, reachable TwinCAT runtime.
/// They run only when TWINCAT_MCP_RUNTIME_TESTS=1. The target AMS Net ID comes from TWINCAT_MCP_RUNTIME_NETID
/// (default: the Usermode Runtime instance, see docs/runtime-setup.md).
/// </summary>
public sealed class RuntimeFactAttribute : FactAttribute
{
    public const string EnableVariable = "TWINCAT_MCP_RUNTIME_TESTS";
    public const string NetIdVariable = "TWINCAT_MCP_RUNTIME_NETID";

    public RuntimeFactAttribute()
    {
        if (!Enabled)
        {
            Skip = $"Runtime test: set {EnableVariable}=1 (and optionally {NetIdVariable}) after following docs/runtime-setup.md.";
        }
    }

    public static bool Enabled => Environment.GetEnvironmentVariable(EnableVariable) == "1";

    public static string? TargetNetId => Environment.GetEnvironmentVariable(NetIdVariable) is { Length: > 0 } id ? id : null;
}
