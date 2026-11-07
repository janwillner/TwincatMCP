using System.Text.Json;

namespace TwinCatMcp.Tests.Integration;

[Collection(PlcCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PlcCodeToolsTests(PlcProjectFixture fx)
{
    private const string FbDeclaration = """
        FUNCTION_BLOCK {0}
        VAR_INPUT
        	bEnable : BOOL;
        END_VAR
        VAR_OUTPUT
        	nCount : INT;
        END_VAR
        """;

    [Fact]
    public async Task Creates_function_block_with_method_and_property_and_reads_them_back()
    {
        var fb = PlcProjectFixture.Unique("FB_Counter");
        var created = await fx.Call("pou_create", new
        {
            name = fb,
            type = "FUNCTION_BLOCK",
            declaration = string.Format(FbDeclaration, fb),
            implementation = "IF bEnable THEN\n\tnCount := nCount + 1;\nEND_IF",
        });
        var fbPath = created.GetProperty("path").GetString()!;
        Assert.Equal($"{fx.Pous}^{fb}", fbPath);

        var method = await fx.Call("pou_create_method", new { parentPath = fbPath, name = "M_Reset", returnType = "BOOL", access = "PROTECTED" });
        var prop = await fx.Call("pou_create_property", new { parentPath = fbPath, name = "P_Count", type = "INT", readOnly = true, getImplementation = "P_Count := nCount;" });

        Assert.StartsWith("METHOD PROTECTED M_Reset : BOOL", method.GetProperty("declaration").GetString());
        Assert.Equal(["Get"], prop.GetProperty("accessors").EnumerateArray().Select(a => a.GetProperty("name").GetString()));

        var read = await fx.Call("pou_read_code", new { paths = new[] { fbPath, $"{fbPath}^P_Count^Get" } });
        var objects = read.GetProperty("objects").EnumerateArray().ToArray();
        Assert.Equal("ST", objects[0].GetProperty("language").GetString());
        Assert.Contains("nCount := nCount + 1;", objects[0].GetProperty("implementation").GetString());
        Assert.Equal(["M_Reset", "P_Count"], objects[0].GetProperty("members").EnumerateArray().Select(m => m.GetProperty("name").GetString()).Order());
        Assert.Contains("P_Count := nCount;", objects[1].GetProperty("implementation").GetString());
    }

    [Fact]
    public async Task Replace_and_grep_work_on_live_code()
    {
        var fb = await NewFb();

        var replaced = await fx.Call("pou_replace_code", new { path = fb, oldText = "nCount + 1", newText = "nCount + 2" });
        Assert.Equal("implementation", replaced.GetProperty("section").GetString());
        Assert.Equal(2, replaced.GetProperty("lines")[0].GetInt32());

        var (both, bothIsError) = await fx.CallRaw("pou_replace_code", new { path = fb, oldText = "nCount", newText = "nValue" });
        Assert.True(bothIsError);
        Assert.Contains("both declaration and implementation", both.GetProperty("error").GetString());

        var (ambiguous, isError) = await fx.CallRaw("pou_replace_code", new { path = fb, oldText = "nCount", newText = "nValue", section = "implementation" });
        Assert.True(isError);
        Assert.Contains("ambiguous", ambiguous.GetProperty("error").GetString());

        var grep = await fx.Call("pou_grep", new { path = fb, pattern = @"nCount \+ 2" });
        var hit = grep.GetProperty("matches")[0];
        Assert.Equal(("implementation", 2), (hit.GetProperty("section").GetString(), hit.GetProperty("line").GetInt32()));

        var plcGrep = await fx.Call("plc_grep", new { pattern = @"nCount \+ 2", kinds = new[] { "fb" } });
        Assert.Contains(plcGrep.GetProperty("matches").EnumerateArray(), m => m.GetProperty("path").GetString() == fb);
    }

    [Fact]
    public async Task Multi_replace_is_all_or_nothing()
    {
        var fb = await NewFb();
        var edits = new object[]
        {
            new { path = fb, oldText = "bEnable : BOOL;", newText = "bRun : BOOL;" },
            new { path = fb, oldText = "DOES_NOT_EXIST", newText = "x" },
        };

        var (_, isError) = await fx.CallRaw("pou_multi_replace", new { edits });
        Assert.True(isError);
        var unchanged = await ReadDecl(fb);
        Assert.Contains("bEnable : BOOL;", unchanged);

        var ok = await fx.Call("pou_multi_replace", new
        {
            edits = new object[]
            {
                new { path = fb, oldText = "bEnable : BOOL;", newText = "bRun : BOOL;" },
                new { path = fb, oldText = "IF bEnable THEN", newText = "IF bRun THEN" },
            },
        });
        Assert.Equal(2, ok.GetProperty("objects")[0].GetProperty("edits").GetArrayLength());
        Assert.Contains("bRun : BOOL;", await ReadDecl(fb));
    }

    [Fact]
    public async Task Creates_struct_enum_alias_and_gvl()
    {
        var st = PlcProjectFixture.Unique("ST_Data");
        var e = PlcProjectFixture.Unique("E_Mode");
        var t = PlcProjectFixture.Unique("T_Speed");
        var gvl = PlcProjectFixture.Unique("GVL_Main");

        var stR = await fx.Call("pou_create_dut", new { name = st, type = "STRUCT", declaration = $"TYPE {st} :\nSTRUCT\n\tnValue : INT;\n\tsName : STRING(80);\nEND_STRUCT\nEND_TYPE\n" });
        var eR = await fx.Call("pou_create_dut", new { name = e, type = "ENUM", declaration = $"TYPE {e} :\n(\n\tIDLE := 0,\n\tRUN\n);\nEND_TYPE\n" });
        var tR = await fx.Call("pou_create_dut", new { name = t, type = "ALIAS", aliasBaseType = "LREAL" });
        var gR = await fx.Call("pou_create_gvl", new { name = gvl, declaration = $"{{attribute 'qualified_only'}}\nVAR_GLOBAL\n\tstData : {st};\n\teMode : {e};\n\tfSpeed : {t};\nEND_VAR\n" });

        Assert.Equal($"{fx.Duts}^{st}", stR.GetProperty("path").GetString());
        Assert.Equal("ENUM", eR.GetProperty("kind").GetString());
        Assert.Contains("LREAL", tR.GetProperty("declaration").GetString());
        Assert.Equal($"{fx.Gvls}^{gvl}", gR.GetProperty("path").GetString());

        var decl = await fx.Call("pou_get_declaration", new { paths = new[] { $"{fx.Duts}^{st}", $"{fx.Gvls}^{gvl}" } });
        var parsed = decl.GetProperty("objects").EnumerateArray().Select(o => o.GetProperty("parsed")).ToArray();
        Assert.Equal(["nValue", "sName"], parsed[0].GetProperty("sections")[0].GetProperty("variables").EnumerateArray().Select(v => v.GetProperty("name").GetString()));
        Assert.Equal("VAR_GLOBAL", parsed[1].GetProperty("sections")[0].GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Creates_interface_with_method_and_property_and_an_implementing_fb_that_compiles()
    {
        var itf = PlcProjectFixture.Unique("I_Motor");
        var fb = PlcProjectFixture.Unique("FB_Motor");
        var itfPath = (await fx.Call("pou_create_interface", new { name = itf })).GetProperty("path").GetString()!;
        await fx.Call("pou_create_method", new { parentPath = itfPath, name = "M_Start", returnType = "BOOL" });
        var prop = await fx.Call("pou_create_property", new { parentPath = itfPath, name = "P_Speed", type = "LREAL" });
        Assert.Equal(["Get", "Set"], prop.GetProperty("accessors").EnumerateArray().Select(a => a.GetProperty("name").GetString()).Order());

        var fbR = await fx.Call("pou_create", new { name = fb, implements = itf });
        Assert.Contains($"IMPLEMENTS {itf}", fbR.GetProperty("declaration").GetString());
        var fbPath = fbR.GetProperty("path").GetString()!;
        await fx.Call("pou_write_code", new { path = fbPath, declaration = $"FUNCTION_BLOCK {fb} IMPLEMENTS {itf}\nVAR\n\tfSpeed : LREAL;\nEND_VAR\n" });
        await fx.Call("pou_create_method", new { parentPath = fbPath, name = "M_Start", returnType = "BOOL", implementation = "M_Start := TRUE;" });
        await fx.Call("pou_create_property", new { parentPath = fbPath, name = "P_Speed", type = "LREAL", getImplementation = "P_Speed := fSpeed;", setImplementation = "fSpeed := P_Speed;" });

        var check = await fx.Call("plc_check_all_objects");
        Assert.True(check.GetProperty("ok").GetBoolean(), check.ToString());
    }

    [Fact]
    public async Task Function_with_return_type_and_ld_program_are_created()
    {
        var f = PlcProjectFixture.Unique("F_Scale");
        var prg = PlcProjectFixture.Unique("PRG_Ld");

        var fr = await fx.Call("pou_create", new { name = f, type = "FUNCTION", returnType = "LREAL", implementation = $"{f} := 2.0;" });
        var pr = await fx.Call("pou_create", new { name = prg, type = "PROGRAM", language = "LD" });

        Assert.StartsWith($"FUNCTION {f} :", fr.GetProperty("declaration").GetString());
        Assert.Equal("LD", pr.GetProperty("language").GetString());
    }

    [Fact]
    public async Task Actions_folders_rename_move_and_delete()
    {
        var fb = await NewFb();
        var action = await fx.Call("pou_create_action", new { parentPath = fb, name = "A_Reset", implementation = "nCount := 0;" });
        Assert.Equal("ACTION", action.GetProperty("kind").GetString());

        var folder = PlcProjectFixture.Unique("Motion");
        var f = await fx.Call("plc_create_folder", new { parentPath = fx.Pous, name = folder + "^Axes" });
        var folderPath = f.GetProperty("path").GetString()!;
        Assert.Equal($"{fx.Pous}^{folder}^Axes", folderPath);

        var moved = await fx.Call("pou_move", new { path = fb, targetFolder = folderPath });
        var movedPath = moved.GetProperty("path").GetString()!;
        Assert.True(moved.GetProperty("moved").GetBoolean());
        var movedCode = await fx.Call("pou_read_code", new { paths = new[] { movedPath } });
        Assert.Contains("nCount := nCount + 1;", movedCode.GetProperty("objects")[0].GetProperty("implementation").GetString());
        Assert.Contains(movedCode.GetProperty("objects")[0].GetProperty("members").EnumerateArray(), m => m.GetProperty("name").GetString() == "A_Reset");

        var newName = PlcProjectFixture.Unique("FB_Renamed");
        var renamed = await fx.Call("pou_rename", new { path = movedPath, newName });
        Assert.StartsWith($"FUNCTION_BLOCK {newName}", renamed.GetProperty("declaration").GetString());

        await fx.Call("pou_delete", new { paths = new[] { $"{fx.Pous}^{folder}" } });
        var exists = await fx.Call("tree_exists", new { treePaths = new[] { $"{fx.Pous}^{folder}" } });
        Assert.False(exists.GetProperty($"{fx.Pous}^{folder}").GetBoolean());
    }

    [Fact]
    public async Task Find_elements_filters_by_kind_and_name()
    {
        var fb = await NewFb();
        var name = fb.Split('^')[^1];

        var found = await fx.Call("plc_find_elements", new { kinds = new[] { "fb" }, namePattern = name });

        var el = Assert.Single(found.GetProperty("elements").EnumerateArray());
        Assert.Equal(("FUNCTION_BLOCK", fb), (el.GetProperty("kind").GetString(), el.GetProperty("path").GetString()));
        var main = await fx.Call("plc_find_elements", new { kinds = new[] { "program" }, namePattern = "/^MAIN$" });
        Assert.Equal($"{fx.Pous}^MAIN", main.GetProperty("elements")[0].GetProperty("path").GetString());
    }

    [Fact]
    public async Task Relative_paths_resolve_inside_the_single_plc_project()
    {
        var read = await fx.Call("pou_read_code", new { paths = new[] { "POUs^MAIN" } });

        Assert.Equal($"{fx.Pous}^MAIN", read.GetProperty("objects")[0].GetProperty("path").GetString());
    }

    [Fact]
    public async Task Creates_sfc_program_with_transition_and_parameter_and_text_lists()
    {
        var prg = PlcProjectFixture.Unique("PRG_Sfc");
        var prgPath = (await fx.Call("pou_create", new { name = prg, type = "PROGRAM", language = "SFC" })).GetProperty("path").GetString()!;

        var t = await fx.Call("pou_create_transition", new { parentPath = prgPath, name = "T_Go", implementation = "TRUE" });
        var pl = await fx.Call("pou_create_param_list", new { name = PlcProjectFixture.Unique("Param"), declaration = "VAR_GLOBAL CONSTANT\n\tcMaxAxes : UDINT := 4;\nEND_VAR\n" });
        var tl = await fx.Call("pou_create_text_list", new { name = PlcProjectFixture.Unique("TL_Texts") });

        Assert.Equal("TRANSITION", t.GetProperty("kind").GetString());
        Assert.False(t.TryGetProperty("declaration", out _)); // transitions have no declaration area (null is omitted)
        Assert.Equal("PARAMETER_LIST", pl.GetProperty("kind").GetString());
        Assert.Contains("cMaxAxes", pl.GetProperty("declaration").GetString());
        Assert.Equal("TEXT_LIST", tl.GetProperty("kind").GetString());

        var read = await fx.Call("pou_read_code", new { paths = new[] { $"{prgPath}^T_Go" } });
        Assert.Equal("TRUE", read.GetProperty("objects")[0].GetProperty("implementation").GetString());
        await fx.Call("pou_delete", new { paths = new[] { prgPath, pl.GetProperty("path").GetString(), tl.GetProperty("path").GetString() } });
    }

    [Fact]
    public async Task Creating_a_method_below_a_dut_is_rejected_with_a_hint()
    {
        var st = PlcProjectFixture.Unique("ST_X");
        var path = (await fx.Call("pou_create_dut", new { name = st })).GetProperty("path").GetString();

        var (json, isError) = await fx.CallRaw("pou_create_method", new { parentPath = path, name = "M_X" });

        Assert.True(isError);
        Assert.Contains("FUNCTION_BLOCK", json.GetProperty("hint").GetString());
        await fx.Call("pou_delete", new { paths = new[] { path } });
    }

    internal async Task<string> NewFb()
    {
        var fb = PlcProjectFixture.Unique("FB_T");
        var r = await fx.Call("pou_create", new
        {
            name = fb,
            declaration = string.Format(FbDeclaration, fb),
            implementation = "IF bEnable THEN\n\tnCount := nCount + 1;\nEND_IF",
        });
        return r.GetProperty("path").GetString()!;
    }

    private async Task<string> ReadDecl(string path)
        => (await fx.Call("pou_read_code", new { paths = new[] { path } })).GetProperty("objects")[0].GetProperty("declaration").GetString()!;
}
