using TwinCatMcp.Guidelines;
using TwinCatMcp.Plc;

namespace TwinCatMcp.Tests.Unit;

/// <summary>
/// Write-time checks of a guideline set. The expected names are the examples of the Syntegon guideline
/// (src/TwinCatMcp.Server/GuidelineExamples/plc-coding-guidelines.Syntegon.md) and the Beckhoff naming conventions.
/// </summary>
public sealed class GuidelineCheckTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("twincat-mcp-check-").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private const string Syntegon = """
        {
          "rules": { "naming.*": "error", "naming.var.member": "warn" },
          "naming": {
            "typePrefixes": { "BOOL": "b_", "INT": "n_", "DINT": "dn_", "REAL": "r_", "LREAL": "lr_", "STRING": "str_", "POINTER": "p_" },
            "kindPrefixes": { "functionBlock": "fb_", "struct": "", "enum": ["e_", ""] },
            "typeKinds": [ { "match": "^_FB_", "kind": "functionBlock" }, { "match": "^T[A-Z]", "kind": "struct" } ],
            "types": { "TON": "ton_", "R_TRIG": "osr_", "MC_*": "fb_" },
            "templates": {
              "var.input": "vi_{type}{Name}",
              "var.output": "vo_{type}{Name}",
              "var.local": "{type}{Name}",
              "var.methodLocal": "{type}Temp{Name}",
              "const.local": "{type}{NAME}",
              "const.global": "{*}{type}{NAME}",
              "pou.functionBlock": "_FB_{Name}",
              "pou.method": "m_{Name}"
            }
          }
        }
        """;

    private GuidelineSet Load(string json)
    {
        var file = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(file, json);
        return GuidelineSet.Load(file, _dir);
    }

    private static readonly GuidelineObject Fb = new("FUNCTION_BLOCK", "_FB_Conveyor", "POUs^_FB_Conveyor");
    private static readonly GuidelineObject Method = new("METHOD", "m_GetPivotAngle", "POUs^_FB_Conveyor^m_GetPivotAngle", "_FB_Conveyor", "FUNCTION_BLOCK");

    private static PouText Decl(string declaration) => new(declaration, "");

    [Fact]
    public void Inputs_need_scope_and_type_prefix_and_get_a_suggestion()
    {
        var set = Load(Syntegon);

        var findings = set.CheckText(Fb, null, Decl("FUNCTION_BLOCK _FB_Conveyor\nVAR_INPUT\n\tvi_b_Init : BOOL;\n\tbEnable : BOOL;\nEND_VAR\n"));

        var v = Assert.Single(findings);
        Assert.Equal(("naming.var.input", "error", "bEnable", 4), (v.Rule, v.Severity, v.Identifier, v.Line));
        Assert.Equal("vi_b_Enable", v.Suggestion);
    }

    [Fact]
    public void Method_locals_need_temp_after_the_type_prefix()
    {
        var set = Load(Syntegon);

        var findings = set.CheckText(Method, null, Decl("METHOD m_GetPivotAngle : LREAL\nVAR\n\tlr_TempAngle : LREAL;\n\tlr_Distance : LREAL;\nEND_VAR\n"));

        var v = Assert.Single(findings);
        Assert.Equal(("lr_Distance", "lr_TempDistance"), (v.Identifier, v.Suggestion));
    }

    [Fact]
    public void Instance_prefixes_come_from_the_type_table_and_the_type_name_heuristic()
    {
        var set = Load(Syntegon);
        const string decl = """
            FUNCTION_BLOCK _FB_Conveyor
            VAR
            	fb_CrashCheck : _FB_EmtCrashCheck;
            	ton_Delay : TON;
            	osr_Start : R_TRIG;
            	fb_Power : MC_Power;
            	CamOut : TCamAnalyse_Out;
            	fb_Wrong : TON;
            END_VAR
            """;

        var findings = set.CheckText(Fb, null, Decl(decl));

        var v = Assert.Single(findings);
        Assert.Equal(("fb_Wrong", "ton_Wrong"), (v.Identifier, v.Suggestion));
    }

    [Fact]
    public void Unknown_types_skip_the_type_part_unless_the_rule_is_switched_on()
    {
        const string decl = "FUNCTION_BLOCK _FB_Conveyor\nVAR\n\tx_Mystery : MysteryType;\nEND_VAR\n";

        Assert.Empty(Load(Syntegon).CheckText(Fb, null, Decl(decl)));

        var strict = Load(Syntegon.Replace("\"naming.*\": \"error\"", "\"naming.*\": \"error\", \"naming.unknownType\": \"warn\""));
        var v = Assert.Single(strict.CheckText(Fb, null, Decl(decl)));
        Assert.Equal(("naming.unknownType", "warn"), (v.Rule, v.Severity));
    }

    [Fact]
    public void Constants_are_upper_snake_and_globals_may_start_with_free_text()
    {
        var set = Load(Syntegon);
        var gvl = new GuidelineObject("GVL", "MATH_GlobalConst");

        Assert.Empty(set.CheckText(gvl, null, Decl("VAR_GLOBAL CONSTANT\n\tMATH_lr_PI : LREAL := 3.14159;\nEND_VAR\n")));
        Assert.Empty(set.CheckText(Fb, null, Decl("FUNCTION_BLOCK _FB_Conveyor\nVAR CONSTANT\n\tdn_MAX_LIGHT_CYCLES : DINT := 10;\nEND_VAR\n")));

        var v = Assert.Single(set.CheckText(gvl, null, Decl("VAR_GLOBAL CONSTANT\n\tPi : LREAL := 3.14159;\nEND_VAR\n")));
        Assert.Equal("naming.const.global", v.Rule);
    }

    [Fact]
    public void Object_names_are_checked_on_create_and_rename()
    {
        var set = Load(Syntegon);

        Assert.Empty(set.CheckName(new GuidelineObject("FUNCTION_BLOCK", "_FB_VAx_Main")));
        var v = Assert.Single(set.CheckName(new GuidelineObject("FUNCTION_BLOCK", "FB_Motor")));
        Assert.Equal(("naming.pou.functionBlock", "_FB_Motor"), (v.Rule, v.Suggestion));
        Assert.Empty(set.CheckName(new GuidelineObject("PROGRAM", "AnyName")));
    }

    [Fact]
    public void Only_new_or_changed_declarations_are_reported()
    {
        var set = Load(Syntegon);
        var before = Decl("FUNCTION_BLOCK _FB_Conveyor\nVAR\n\tLegacy : INT;\nEND_VAR\n");
        var after = Decl("FUNCTION_BLOCK _FB_Conveyor\nVAR\n\tLegacy : INT;\n\tAlsoBad : INT;\nEND_VAR\n");

        var v = Assert.Single(set.CheckText(Fb, before, after));
        Assert.Equal("AlsoBad", v.Identifier);
    }

    [Fact]
    public void A_changed_type_is_checked_again()
    {
        var set = Load(Syntegon);
        var before = Decl("FUNCTION_BLOCK _FB_Conveyor\nVAR\n\tn_Count : INT;\nEND_VAR\n");
        var after = Decl("FUNCTION_BLOCK _FB_Conveyor\nVAR\n\tn_Count : DINT;\nEND_VAR\n");

        Assert.Equal("dn_Count", Assert.Single(set.CheckText(Fb, before, after)).Suggestion);
    }

    [Fact]
    public void Composite_prefixes_concatenate_from_outer_to_inner()
    {
        var set = Load("""
            { "naming": { "typePrefixes": { "INT": "n", "BOOL": "b", "POINTER": "p", "ARRAY": "a" },
                          "templates": { "var.local": "{type}{Name}" } } }
            """);
        const string decl = "PROGRAM MAIN\nVAR\n\tpanValues : POINTER TO ARRAY[0..9] OF INT;\n\taabGrid : ARRAY[0..1] OF ARRAY[0..1] OF BOOL;\n\tpValues : POINTER TO INT;\nEND_VAR\n";

        var v = Assert.Single(set.CheckText(new GuidelineObject("PROGRAM", "MAIN"), null, Decl(decl)));
        Assert.Equal(("pValues", "warn"), (v.Identifier, v.Severity));
    }

    [Fact]
    public void Naming_pragmas_mcp_ignore_and_excludes_suppress_findings()
    {
        var set = Load(Syntegon.Replace("\"rules\":", "\"exclude\": [\"ST_Visu*\", \"_FB_Conveyor.Contract*\"], \"rules\":"));
        const string decl = """
            FUNCTION_BLOCK _FB_Conveyor
            VAR
            	{attribute 'naming' := 'omit'}
            	Omitted : INT;
            	{attribute 'mcp-ignore' := 'naming.var.*'}
            	Ignored : INT;
            	ContractValue : INT;
            END_VAR
            """;

        Assert.Empty(set.CheckText(Fb, null, Decl(decl)));
        Assert.Empty(set.CheckText(new GuidelineObject("STRUCT", "ST_VisuConfig"), null, Decl("TYPE ST_VisuConfig :\nSTRUCT\n\tbActive : BOOL;\nEND_STRUCT\nEND_TYPE\n")));
    }

    [Fact]
    public void No_analysis_attribute_skips_the_whole_object()
    {
        var set = Load(Syntegon);

        Assert.Empty(set.CheckText(Fb, null, Decl("{attribute 'no-analysis'}\nFUNCTION_BLOCK _FB_Conveyor\nVAR\n\tBad : INT;\nEND_VAR\n")));
    }
}
