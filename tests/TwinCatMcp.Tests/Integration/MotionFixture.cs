using System.Text.Json;
using TwinCatMcp.Core;

namespace TwinCatMcp.Tests.Integration;

/// <summary>
/// XAE fixture for the motion / TcCOM tests. On demand it adds a PLC project "McPlc" whose GVL declares two AXIS_REFs
/// (library Tc2_MC2). Library + GVL declaration have no MCP tool in this package, so they are done directly via the
/// Automation Interface (late bound) on the IDE instance started by the server.
/// </summary>
public sealed class MotionFixture : XaeProjectFixture
{
    public const string McPlc = "McPlc";
    public const string GvlName = "GVL_Motion";
    private readonly SemaphoreSlim _plcLock = new(1, 1);
    private bool _plcReady;

    public string McPlcProject => $"TIPC^{McPlc}^{McPlc} Project";

    /// <summary>The single NC task of the project (TwinCAT allows only one), created on first use: 1000 us, priority 5.</summary>
    public const string NcTask = "NC-Task 1";
    public const string NcTaskPath = "TINC^NC-Task 1 SAF";

    public async Task EnsureNcTaskAsync()
    {
        await _plcLock.WaitAsync();
        try
        {
            var exists = await Call("tree_exists", new { treePaths = new[] { NcTaskPath } });
            if (!exists.GetProperty(NcTaskPath).GetBoolean())
            {
                await Call("motion_create_nc_task", new { name = NcTask, cycleTimeUs = 1000, priority = 5 });
            }
        }
        finally
        {
            _plcLock.Release();
        }
    }

    /// <summary>Adds an axis to the NC task and returns its path.</summary>
    public async Task<string> AddAxisAsync(string name, string axisType = "continuous")
    {
        await EnsureNcTaskAsync();
        var r = await Call("motion_add_axis", new { names = new[] { name }, axisType });
        return r.GetProperty("created")[0].GetProperty("path").GetString()!;
    }

    /// <summary>Creates PLC project McPlc with Tc2_MC2 and GVL_Motion { axX, axY, axSim : AXIS_REF } and builds the solution.</summary>
    public async Task EnsurePlcWithAxisRefsAsync()
    {
        await _plcLock.WaitAsync();
        try
        {
            if (_plcReady)
            {
                return;
            }

            var gvlPath = McPlcProject + "^GVLs^" + GvlName;
            var exists = await Call("tree_exists", new { treePaths = new[] { "TIPC^" + McPlc, gvlPath } });
            if (!exists.GetProperty("TIPC^" + McPlc).GetBoolean())
            {
                await Call("tree_create_child", new { parentPath = "TIPC", name = McPlc, subType = 0, info = "Standard PLC Template" });
            }

            if (!exists.GetProperty(gvlPath).GetBoolean())
            {
                await Call("tree_create_child", new { parentPath = McPlcProject + "^GVLs", name = GvlName, subType = 615 });
            }

            var solution = await SolutionPathAsync();
            using (var sta = new StaComThread())
            {
                await sta.InvokeAsync(() =>
                {
                    // Identify the IDE by its open solution: xae_status.processId is unreliable when several IDEs start at the same time.
                    var entry = RunningObjectTable.GetDteInstances().FirstOrDefault(e => SolutionOf(e.Dte) is { } s && string.Equals(s, solution, StringComparison.OrdinalIgnoreCase))
                                ?? throw new InvalidOperationException($"No IDE with solution '{solution}' found in ROT");
                    dynamic dte = entry.Dte;
                    dynamic sm = null!;
                    foreach (dynamic p in dte.Solution.Projects)
                    {
                        if (p.Name == SolutionName)
                        {
                            sm = p.Object;
                        }
                    }

                    dynamic refs = sm.LookupTreeItem(McPlcProject + "^References");
                    refs.AddLibrary("Tc2_MC2", "*", "Beckhoff Automation GmbH");
                    dynamic gvl = sm.LookupTreeItem(McPlcProject + "^GVLs^" + GvlName);
                    gvl.DeclarationText = "VAR_GLOBAL\r\n\taxX : AXIS_REF;\r\n\taxY : AXIS_REF;\r\n\taxSim : AXIS_REF;\r\nEND_VAR\r\n";

                    // A GVL that no POU references is not linked into the application (LinkAlways=False) and its
                    // AXIS_REFs would get no process image -> reference them from MAIN.
                    dynamic main = sm.LookupTreeItem(McPlcProject + "^POUs^MAIN");
                    main.ImplementationText = "GVL_Motion.axX.ReadStatus();\r\nGVL_Motion.axY.ReadStatus();\r\nGVL_Motion.axSim.ReadStatus();\r\n";
                }, TimeSpan.FromMinutes(3));
            }

            var build = await Call("build_solution", new { rebuild = false, includeWarnings = false }, TimeSpan.FromMinutes(10));
            if (build.GetProperty("diagnostics").GetProperty("errorCount").GetInt32() != 0)
            {
                throw new InvalidOperationException("PLC build failed: " + build);
            }

            _plcReady = true;
        }
        finally
        {
            _plcLock.Release();
        }
    }

    /// <summary>Full path of the solution opened by the server (xae_status occasionally fails transiently, so retry).</summary>
    private async Task<string> SolutionPathAsync()
    {
        for (var attempt = 1; ; attempt++)
        {
            var (json, isError) = await CallRaw("xae_status");
            if (!isError && json.TryGetProperty("solution", out var sln) && sln.ValueKind == JsonValueKind.String)
            {
                return Path.GetFullPath(sln.GetString()!);
            }

            if (attempt >= 5)
            {
                throw new InvalidOperationException("xae_status did not return the solution path: " + json);
            }

            await Task.Delay(2000);
        }
    }

    private static string? SolutionOf(object dte)
    {
        try
        {
            dynamic d = dte;
            string full = d.Solution.FullName;
            return string.IsNullOrEmpty(full) ? null : Path.GetFullPath(full);
        }
        catch
        {
            return null; // other / busy IDE instances
        }
    }

    public static string[] Strings(JsonElement array) => array.EnumerateArray().Select(e => e.GetString()!).ToArray();
}

[CollectionDefinition(Name)]
public sealed class MotionCollection : ICollectionFixture<MotionFixture>
{
    public const string Name = "Motion";
}
