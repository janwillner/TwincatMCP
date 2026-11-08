namespace TwinCatMcp.Tests.Integration;

[Collection(PlcCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PlcProjectToolsTests(PlcProjectFixture fx)
{
    [Fact]
    public void Created_project_reports_root_nested_and_instance_paths()
    {
        var p = fx.CreatedProject;

        Assert.Equal("TIPC^Plc1", p.GetProperty("rootPath").GetString());
        Assert.Equal("TIPC^Plc1^Plc1 Project", p.GetProperty("projectPath").GetString());
        Assert.Equal("TIPC^Plc1^Plc1 Instance", p.GetProperty("instancePath").GetString());
    }

    [Fact]
    public async Task List_projects_returns_the_nested_project_path()
    {
        var list = await fx.Call("plc_list_projects");

        var plc = list.GetProperty("projects").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "Plc1");
        Assert.Equal(fx.PlcProjectPath, plc.GetProperty("projectPath").GetString());
    }

    [Fact]
    public async Task Build_succeeds_with_valid_code_and_reports_a_deliberate_syntax_error()
    {
        var fb = PlcProjectFixture.Unique("FB_Broken");
        var path = (await fx.Call("pou_create", new { name = fb })).GetProperty("path").GetString()!;
        await fx.Call("pou_write_code", new { path = "POUs^MAIN", declaration = $"PROGRAM MAIN\nVAR\n\tfbTest : {fb};\nEND_VAR\n", implementation = "fbTest();" });
        try
        {
            var good = await fx.Call("build_solution", new { rebuild = false });
            Assert.True(good.GetProperty("success").GetBoolean(), good.ToString());
            Assert.Equal(0, good.GetProperty("diagnostics").GetProperty("errorCount").GetInt32());

            await fx.Call("pou_write_code", new { path, implementation = "nUnknown := ;" });
            var bad = await fx.Call("build_solution", new { rebuild = false });
            Assert.False(bad.GetProperty("success").GetBoolean());
            // The agent must see WHAT is wrong, not just that the build failed (hidden IDE: Error List stays empty).
            var diagnostics = bad.GetProperty("diagnostics");
            Assert.True(diagnostics.GetProperty("errorCount").GetInt32() > 0, bad.ToString());
            var buildError = diagnostics.GetProperty("errors")[0];
            Assert.Equal("C0007", buildError.GetProperty("code").GetString());
            Assert.Equal(fb, buildError.GetProperty("object").GetString());

            var check = await fx.Call("plc_check_all_objects", new { includeOutput = true });
            Assert.False(check.GetProperty("ok").GetBoolean());
            Assert.True(check.GetProperty("errors").GetArrayLength() > 0, check.ToString());
            var error = check.GetProperty("errors")[0];
            Assert.Equal(fb, error.GetProperty("object").GetString());
            Assert.Equal("C0007", error.GetProperty("code").GetString()); // "Expression expected instead of ';'"

            var compile = await fx.Call("plc_compile");
            Assert.False(compile.GetProperty("ok").GetBoolean(), compile.ToString());
        }
        finally
        {
            await fx.CallRaw("pou_write_code", new { path = "POUs^MAIN", declaration = "PROGRAM MAIN\nVAR\nEND_VAR\n", implementation = "" });
            await fx.CallRaw("pou_delete", new { paths = new[] { path } });
        }

        var fixedCheck = await fx.Call("plc_check_all_objects");
        Assert.True(fixedCheck.GetProperty("ok").GetBoolean(), fixedCheck.ToString());
    }

    [Fact]
    public async Task PlcOpen_export_and_import_round_trip_into_a_folder()
    {
        var fb = PlcProjectFixture.Unique("FB_Xml");
        var path = (await fx.Call("pou_create", new { name = fb, implementation = "; // exported" })).GetProperty("path").GetString()!;
        await fx.Call("pou_create_method", new { parentPath = path, name = "M_Run", implementation = "; // method body" });
        var file = Path.Combine(fx.WorkDir, fb + ".xml");

        var export = await fx.Call("plc_export_plcopen", new { filePath = file, selection = new[] { path } });
        Assert.Equal([fb], export.GetProperty("objects").EnumerateArray().Select(o => o.GetString()));
        Assert.True(File.Exists(file));

        var (_, conflict) = await fx.CallRaw("plc_import_plcopen", new { filePath = file });
        Assert.True(conflict); // default conflict handling 'error': names are unique in a PLC project

        await fx.Call("pou_delete", new { paths = new[] { path } });
        var folder = PlcProjectFixture.Unique("Imported");
        await fx.Call("plc_import_plcopen", new { filePath = file, targetFolder = $"POUs^{folder}" });

        var read = await fx.Call("pou_read_code", new { paths = new[] { $"{fx.Pous}^{folder}^{fb}^M_Run" } });
        Assert.Contains("method body", read.GetProperty("objects")[0].GetProperty("implementation").GetString());

        var renamed = await fx.Call("plc_import_plcopen", new { filePath = file, targetFolder = $"POUs^{folder}", conflict = "rename" });
        var exists = await fx.Call("tree_exists", new { treePaths = new[] { $"{fx.Pous}^{folder}^{fb}_1" } });
        Assert.True(exists.GetProperty($"{fx.Pous}^{folder}^{fb}_1").GetBoolean(), renamed.ToString());
        await fx.Call("pou_delete", new { paths = new[] { $"{fx.Pous}^{folder}" } });
    }

    [Fact]
    public async Task Settings_can_be_read_and_changed()
    {
        // Independent of the starting state (the shared fixture project may have been changed by other tests).
        var before = await fx.Call("plc_get_settings");
        var autostart = before.GetProperty("bootProjectAutostart").GetBoolean();
        var tmcCopy = before.GetProperty("tmcFileCopy").GetBoolean();
        try
        {
            var after = await fx.Call("plc_set_settings", new { bootProjectAutostart = !autostart, tmcFileCopy = !tmcCopy });
            var reread = await fx.Call("plc_get_settings");

            Assert.Equal(!autostart, after.GetProperty("bootProjectAutostart").GetBoolean());
            Assert.Equal(!tmcCopy, after.GetProperty("tmcFileCopy").GetBoolean());
            Assert.Equal("None", after.GetProperty("bootProjectEncryption").GetString());
            Assert.Equal(!autostart, reread.GetProperty("bootProjectAutostart").GetBoolean());
        }
        finally
        {
            await fx.Call("plc_set_settings", new { bootProjectAutostart = autostart, tmcFileCopy = tmcCopy });
        }
    }

    [Fact]
    public async Task Task_reference_links_PlcTask_to_the_realtime_task()
    {
        var get = await fx.Call("plc_get_task_reference");
        var task = get.GetProperty("tasks")[0];
        Assert.Equal($"{fx.PlcProjectPath}^PlcTask", task.GetProperty("path").GetString());
        Assert.Equal("TIRT^PlcTask", task.GetProperty("linkedTask").GetString());
        Assert.Contains("MAIN", task.GetProperty("programs").EnumerateArray().Select(p => p.GetString()));

        var set = await fx.Call("plc_set_task_reference", new { linkedTask = "TIRT^PlcTask" });
        Assert.Equal("TIRT^PlcTask", set.GetProperty("linkedTask").GetString());

        var (bad, isError) = await fx.CallRaw("plc_set_task_reference", new { linkedTask = "TIRT^NoSuchTask" });
        Assert.True(isError, bad.ToString());
    }

    [Fact]
    public async Task Second_project_from_empty_template_is_searchable_and_deletable()
    {
        var name = PlcProjectFixture.Unique("PlcE");
        var created = await fx.Call("plc_create_project", new { name, template = "Empty PLC Template" });
        try
        {
            var projectPath = created.GetProperty("projectPath").GetString()!;
            Assert.Equal($"TIPC^{name}^{name} Project", projectPath);

            var gvl = await fx.Call("pou_create_gvl", new { name = "GVL_Empty", project = name });
            Assert.StartsWith(projectPath + "^", gvl.GetProperty("path").GetString());

            var (ambiguous, isError) = await fx.CallRaw("plc_grep", new { pattern = "x" });
            Assert.True(isError, ambiguous.ToString()); // two PLC projects -> project must be named

            var found = await fx.Call("plc_find_elements", new { projects = new[] { "Plc1", name }, kinds = new[] { "gvl", "program" } });
            var projects = found.GetProperty("elements").EnumerateArray().Select(e => e.GetProperty("project").GetString()).Distinct().ToArray();
            Assert.Contains(name, projects);
            Assert.Contains("Plc1", projects);
        }
        finally
        {
            await fx.Call("plc_delete_project", new { project = name });
        }

        var list = await fx.Call("plc_list_projects");
        Assert.DoesNotContain(list.GetProperty("projects").EnumerateArray(), p => p.GetProperty("name").GetString() == name);
    }

    [Fact]
    public async Task Boot_project_is_generated_locally_without_activation()
    {
        var r = await fx.Call("plc_generate_boot_project", new { activate = false });

        Assert.True(r.GetProperty("generated").GetBoolean(), r.ToString());
        Assert.False(r.GetProperty("activated").GetBoolean());
    }

    [Fact]
    public async Task Static_analysis_and_compile_run()
    {
        var sarif = Path.Combine(fx.WorkDir, "san.sarif.json");

        var san = await fx.Call("plc_run_static_analysis", new { sarifPath = sarif });
        var compile = await fx.Call("plc_compile");

        Assert.True(san.GetProperty("ok").GetBoolean(), san.ToString());
        Assert.Equal(sarif, san.GetProperty("sarifFile").GetString());
        Assert.True(compile.GetProperty("ok").GetBoolean(), compile.ToString());
    }
}
