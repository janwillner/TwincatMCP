using System.Text.Json;

namespace TwinCatMcp.Tests.Integration;

[CollectionDefinition(Name)]
public sealed class CompleteProjectCollection : ICollectionFixture<XaeProjectFixture>
{
    public const string Name = "E2E-CompleteProject";
}

/// <summary>
/// End-to-end acceptance test: a complete machine project is created exclusively through MCP tools –
/// PLC code, libraries, EtherCAT I/O with links, an NC axis linked to the PLC, build, save, archive and reopen.
/// (Activation/download on a runtime is covered by the license-gated Runtime* tests.)
/// </summary>
[Collection(CompleteProjectCollection.Name)]
[Trait("Category", "Integration")]
public sealed class CompleteProjectTests(XaeProjectFixture fx)
{
    private const string Plc = "Plc1";
    private const string Instance = "TIPC^Plc1^Plc1 Instance";
    private const string Master = "TIID^EtherCAT Master";
    private const string Coupler = Master + "^Term 1 (EK1100)";

    [Fact]
    public async Task A_complete_machine_project_can_be_built_wired_saved_and_reopened_with_mcp_tools_only()
    {
        // ---- PLC project + libraries
        await fx.Call("plc_create_project", new { name = Plc }, TimeSpan.FromMinutes(5));
        await fx.Call("lib_add", new { libraries = new[] { "Tc2_MC2", "Tc2_Standard" } });
        var decl = await fx.Call("lib_get_declaration", new { objects = new[] { "MC_Power" } });
        Assert.Contains("MC_Power", decl.ToString());

        // ---- data model
        await fx.Call("pou_create_dut", new
        {
            name = "E_MachineState",
            type = "ENUM",
            declaration = "{attribute 'qualified_only'}\n{attribute 'strict'}\nTYPE E_MachineState :\n(\n\tIdle := 0,\n\tRunning,\n\tError\n);\nEND_TYPE\n",
        });
        await fx.Call("pou_create_gvl", new
        {
            name = "GVL_IO",
            declaration = "{attribute 'qualified_only'}\n{attribute 'linkalways'}\nVAR_GLOBAL\n\tbStartButton AT %I* : BOOL;\n\tbRunLamp AT %Q* : BOOL;\nEND_VAR\n",
        });
        await fx.Call("pou_create_gvl", new
        {
            name = "GVL_Motion",
            declaration = "{attribute 'qualified_only'}\nVAR_GLOBAL\n\taxX : AXIS_REF;\nEND_VAR\n",
        });

        // ---- function block with a method
        var fb = await fx.Call("pou_create", new
        {
            name = "FB_Machine",
            type = "FUNCTION_BLOCK",
            declaration = "FUNCTION_BLOCK FB_Machine\nVAR_INPUT\n\tbStart : BOOL;\nEND_VAR\nVAR_OUTPUT\n\tbRunning : BOOL;\nEND_VAR\nVAR\n\teState : E_MachineState;\n\tfbPower : MC_Power;\nEND_VAR\n",
            implementation = "CASE eState OF\n\tE_MachineState.Idle:\n\t\tIF bStart THEN\n\t\t\teState := E_MachineState.Running;\n\t\tEND_IF\n\tE_MachineState.Running:\n\t\tbRunning := TRUE;\nEND_CASE\n\nfbPower(Axis := GVL_Motion.axX, Enable := bRunning, Enable_Positive := bRunning, Enable_Negative := bRunning);\n",
        });
        var fbPath = fb.GetProperty("path").GetString()!;
        await fx.Call("pou_create_method", new
        {
            parentPath = fbPath,
            name = "M_Reset",
            returnType = "BOOL",
            implementation = "eState := E_MachineState.Idle;\nbRunning := FALSE;\nM_Reset := TRUE;\n",
        });

        await fx.Call("pou_write_code", new
        {
            path = "POUs^MAIN",
            declaration = "PROGRAM MAIN\nVAR\n\tfbMachine : FB_Machine;\nEND_VAR\n",
            implementation = "fbMachine(bStart := GVL_IO.bStartButton);\nGVL_IO.bRunLamp := fbMachine.bRunning;\n",
        });

        await AssertBuildIsClean();

        // ---- real-time task
        await fx.Call("task_configure", new { task = "PlcTask", cycleTime = 5, unit = "ms" });

        // ---- EtherCAT I/O offline + links
        await fx.Call("io_add_device", new { type = "ethercat-master", name = "EtherCAT Master" });
        await fx.Call("ecat_add_terminals", new { parent = Master, products = new[] { "EK1100", "EL1008", "EL2008" } });
        var topology = await fx.Call("ecat_get_topology");
        Assert.Contains("EL2008", topology.ToString());

        // ---- NC axis linked to the PLC AXIS_REF
        await fx.Call("motion_create_nc_task");
        await fx.Call("motion_add_axis", new { names = new[] { "Axis X" } });
        await fx.Call("motion_set_axis_parameters", new { axis = "Axis X", parameters = new Dictionary<string, object> { ["maxVelocity"] = 500.0 } });
        var axisParams = await fx.Call("motion_get_axis_parameters", new { axes = new[] { "Axis X" } });
        Assert.Contains("500", axisParams.ToString());

        await AssertBuildIsClean(); // PLC process image must exist before linking
        var linked = await fx.Call("mapping_link", new
        {
            links = new[]
            {
                new { a = $"{Instance}^PlcTask Inputs^GVL_IO.bStartButton", b = $"{Coupler}^Term 2 (EL1008)^Channel 1^Input" },
                new { a = $"{Instance}^PlcTask Outputs^GVL_IO.bRunLamp", b = $"{Coupler}^Term 3 (EL2008)^Channel 1^Output" },
            },
        });
        Assert.DoesNotContain("\"error\"", linked.ToString());
        await fx.Call("motion_link_axis_to_plc", new { axis = "Axis X", plcAxisRef = "GVL_Motion.axX", plc = Plc });

        var links = await fx.Call("mapping_list_links");
        Assert.True(links.GetProperty("count").GetInt32() >= 4, links.ToString()); // 2 I/O + 2 axis links
        await AssertBuildIsClean();

        // ---- save, archive, reopen
        await fx.Call("solution_save");
        var archive = Path.Combine(fx.WorkDir, "Machine.tnzip");
        await fx.Call("archive_save_solution", new { filePath = archive });
        Assert.True(File.Exists(archive));

        var sln = Path.Combine(fx.WorkDir, XaeProjectFixture.SolutionName, XaeProjectFixture.SolutionName + ".sln");
        await fx.Call("solution_close", new { save = true });
        await fx.Call("solution_open", new { path = sln });

        var elements = await fx.Call("plc_find_elements", new { namePattern = "FB_Machine" });
        Assert.True(elements.GetProperty("count").GetInt32() >= 1, elements.ToString());
        var linksAfterReopen = await fx.Call("mapping_list_links");
        Assert.Equal(links.GetProperty("count").GetInt32(), linksAfterReopen.GetProperty("count").GetInt32());
    }

    private async Task AssertBuildIsClean()
    {
        var build = await fx.Call("build_solution", null, TimeSpan.FromMinutes(10));
        Assert.True(build.GetProperty("success").GetBoolean(), build.ToString());
        Assert.Equal(0, build.GetProperty("diagnostics").GetProperty("errorCount").GetInt32());
    }
}
