using TwinCatMcp.Guidelines;
using TwinCatMcp.Plc;

namespace TwinCatMcp.Tests.Unit;

/// <summary>
/// The example configs shipped in the guidelines folder of every deployment. Expected names are the examples of
/// src/TwinCatMcp.Server/GuidelineExamples/plc-coding-guidelines.Syntegon.md (Syntegon) and of the Beckhoff conventions.
/// </summary>
public sealed class GuidelineShippedExamplesTests : IDisposable
{
    private static readonly string Builtin = Path.Combine(AppContext.BaseDirectory, "guidelines");
    private readonly string _dir = Directory.CreateTempSubdirectory("twincat-mcp-shipped-").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private GuidelineSet Extend(string builtin)
    {
        var file = Path.Combine(_dir, ".twincat-mcp.json");
        File.WriteAllText(file, $$"""{ "extends": "builtin:{{builtin}}" }""");
        return GuidelineSet.Load(file, Builtin);
    }

    [Theory]
    [InlineData("syntegon")]
    [InlineData("beckhoff")]
    public void Shipped_configs_load_with_their_style_guide(string name)
    {
        var set = Extend(name);

        Assert.NotNull(set.Name);
        Assert.False(string.IsNullOrWhiteSpace(set.GuideMarkdown));
        Assert.NotEmpty(set.ActiveRules);
    }

    [Fact]
    public void Syntegon_accepts_the_examples_of_the_guideline()
    {
        var set = Extend("syntegon");
        var fb = new GuidelineObject("FUNCTION_BLOCK", "_FB_EmtCrashCheck");
        const string decl = """
            FUNCTION_BLOCK _FB_EmtCrashCheck
            (*
            © Syntegon Group.<br>
            All rights reserved, also regarding any disposal, exploitation, reproduction, editing, distribution,<br>
            as well as in the event of applications for industrial property rights.<br>
            DESCRIPTION:<br>
            	Checks the EMT for crashes.<br>
            *)
            VAR_INPUT
            	vi_b_Init		: BOOL;
            	vi_Data			: TCamAnalyse_Out;
            END_VAR
            VAR_OUTPUT
            	vo_str_ErrorText	: STRING(80);
            END_VAR
            VAR_IN_OUT
            	vio_ax			: AXIS_REF;
            END_VAR
            VAR
            	b_EnableAxis	: BOOL;
            	lr_DistXyz		: LREAL;
            	fb_CylinderBrake	: _FB_CylinderBrake;
            	ton_Delay		: TON;
            	osr_Start		: R_TRIG;
            END_VAR
            VAR CONSTANT
            	dn_MAX_LIGHT_CYCLES	: DINT := 10;
            END_VAR
            """;

        Assert.Empty(set.CheckName(fb));
        Assert.Empty(set.CheckText(fb, null, new PouText(decl, "")));
        Assert.Empty(set.CheckName(new GuidelineObject("METHOD", "m_GetPivotAngle")));
        Assert.Empty(set.CheckName(new GuidelineObject("ACTION", "act_Sequencer")));
        Assert.Empty(set.CheckName(new GuidelineObject("FUNCTION", "_FC_ErrorEntry")));
        Assert.Empty(set.CheckName(new GuidelineObject("PROGRAM", "INF1_Program_Slow")));
        Assert.Empty(set.CheckName(new GuidelineObject("STRUCT", "TVAx_Iface")));
        Assert.Empty(set.CheckName(new GuidelineObject("ENUM", "SM_STATE")));
        Assert.Empty(set.CheckName(new GuidelineObject("ENUM", "EmtCatchUp_SEQ")));
        Assert.Empty(set.CheckText(new GuidelineObject("GVL", "MATH_GlobalConst"), null,
            new PouText("VAR_GLOBAL CONSTANT\n\tMATH_lr_PI : LREAL := 3.14159;\nEND_VAR\n", null)));
        Assert.Empty(set.CheckText(new GuidelineObject("GVL", "INF1_PGR1_GlobalVar"), null,
            new PouText("VAR_GLOBAL\n\tINF1_PGR1_b_iProductSensor : BOOL;\n\tINF1_PGR1_Hmi : TPGR_Hmi;\nEND_VAR\n", null)));
    }

    [Fact]
    public void Syntegon_rejects_a_method_local_without_temp_and_names_the_fix()
    {
        var set = Extend("syntegon");
        var method = new GuidelineObject("METHOD", "m_GetPivotAngle", null, "_FB_EmtCrashCheck", "FUNCTION_BLOCK");

        var result = set.PrepareCreate(method, new PouText("METHOD m_GetPivotAngle : LREAL\nVAR\n\tlr_Angle : LREAL;\nEND_VAR\n", ""), new DateTime(2026, 11, 22));

        var error = Assert.Single(result.Violations, v => v.Severity == "error");
        Assert.Equal(("naming.var.methodLocal", "lr_TempAngle"), (error.Rule, error.Suggestion));
        Assert.Contains("© Syntegon Group.<br>", result.Text.Declaration);
        Assert.DoesNotContain("VERSION HISTORY", result.Text.Declaration);
    }

    [Fact]
    public void Syntegon_function_blocks_get_the_header_with_version_history()
    {
        var set = Extend("syntegon");
        var fb = new GuidelineObject("FUNCTION_BLOCK", "_FB_Conveyor");

        var result = set.PrepareCreate(fb, new PouText("FUNCTION_BLOCK _FB_Conveyor\nVAR\nEND_VAR\n", ""), new DateTime(2026, 11, 22));

        Assert.Contains("VERSION HISTORY:<br>", result.Text.Declaration);
        Assert.Contains("2026-11-22\tC1.001\t_FB_Conveyor created<br>", result.Text.Declaration);
        Assert.Equal(["header.description"], result.Violations.Select(v => v.Rule));
    }

    [Fact]
    public void Beckhoff_accepts_hungarian_notation_with_composite_prefixes()
    {
        var set = Extend("beckhoff");
        var fb = new GuidelineObject("FUNCTION_BLOCK", "FB_Motor");
        const string decl = """
            FUNCTION_BLOCK FB_Motor
            VAR_INPUT
            	bEnable : BOOL;
            	astAxes : ARRAY[1..4] OF ST_AxisData;
            	pnValue : POINTER TO INT;
            END_VAR
            VAR
            	fbTimer : TON;
            	eState : E_MachineState;
            	sName : STRING(80);
            END_VAR
            """;

        Assert.Empty(set.CheckName(fb));
        Assert.Empty(set.CheckText(fb, null, new PouText(decl, "")));
        Assert.Equal("fSpeed", Assert.Single(set.CheckText(fb, null,
            new PouText("FUNCTION_BLOCK FB_Motor\nVAR\n\tnSpeed : LREAL;\nEND_VAR\n", ""))).Suggestion);
    }

    [Fact]
    public void Beckhoff_adds_qualified_only_to_new_gvls()
    {
        var set = Extend("beckhoff");

        var result = set.PrepareCreate(new GuidelineObject("GVL", "GVL_IO"), new PouText("VAR_GLOBAL\nEND_VAR\n", null), DateTime.Now);

        Assert.StartsWith("{attribute 'qualified_only'}", result.Text.Declaration);
        Assert.Empty(result.Violations);
    }
}
