using System.Text.Json;
using TwinCatMcp.Core;

namespace TwinCatMcp.Tests.Integration;

/// <summary>
/// XAE fixture for the motion / TcCOM tests. On demand it adds a PLC project "McPlc" whose GVL declares AXIS_REFs
/// (library Tc2_MC2) – created exclusively through MCP tools.
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

            // Everything through MCP tools (seam A) - never touch the IDE directly from the test process:
            // other test collections run their own IDEs in parallel.
            await Call("plc_create_project", new { name = McPlc }, TimeSpan.FromMinutes(5));
            await Call("lib_add", new { libraries = new[] { "Tc2_MC2" }, plcProject = McPlc });
            await Call("pou_create_gvl", new
            {
                name = GvlName,
                project = McPlc,
                declaration = "VAR_GLOBAL\r\n\taxX : AXIS_REF;\r\n\taxY : AXIS_REF;\r\n\taxSim : AXIS_REF;\r\nEND_VAR\r\n",
            });

            // A GVL that no POU references is not linked into the application (LinkAlways=False) and its
            // AXIS_REFs would get no process image -> reference them from MAIN.
            await Call("pou_write_code", new
            {
                path = McPlcProject + "^POUs^MAIN",
                implementation = "GVL_Motion.axX.ReadStatus();\r\nGVL_Motion.axY.ReadStatus();\r\nGVL_Motion.axSim.ReadStatus();\r\n",
            });

            var build = await Call("build_solution", new { rebuild = false, includeWarnings = false }, TimeSpan.FromMinutes(10));
            if (build.GetProperty("diagnostics").GetProperty("errorCount").GetInt32() != 0)
            {
                var (output, _) = await CallRaw("build_get_output", new { pane = "Build", lastLines = 60 });
                throw new InvalidOperationException("PLC build failed: " + build + "\nBuild output:\n" + output);
            }

            _plcReady = true;
        }
        finally
        {
            _plcLock.Release();
        }
    }

    public static string[] Strings(JsonElement array) => array.EnumerateArray().Select(e => e.GetString()!).ToArray();
}

[CollectionDefinition(Name)]
public sealed class MotionCollection : ICollectionFixture<MotionFixture>
{
    public const string Name = "Motion";
}
