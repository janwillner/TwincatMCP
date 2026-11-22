using TwinCatMcp.Guidelines;
using TwinCatMcp.Plc;

namespace TwinCatMcp.Tests.Unit;

/// <summary>Declaration rules (enum attributes/values, method input defaults) and header templates incl. create fixes.</summary>
public sealed class GuidelineDeclarationAndHeaderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("twincat-mcp-decl-").FullName;

    private static readonly DateTime Now = new(2026, 11, 22);

    public void Dispose() => Directory.Delete(_dir, true);

    private GuidelineSet Load(string json)
    {
        File.WriteAllText(Path.Combine(_dir, "header.txt"), "(*\n© Syntegon Group.<br>\nAll rights reserved.<br>\nDESCRIPTION:<br>\n\t{description}<br>\nNAME: {name}<br>\n*)");
        var file = Path.Combine(_dir, ".twincat-mcp.json");
        File.WriteAllText(file, json);
        return GuidelineSet.Load(file, _dir);
    }

    private const string Rules = """
        {
          "rules": {
            "decl.requiredAttributes": "error",
            "decl.enum.explicitValues": "error",
            "decl.enum.valueRange": "warn",
            "decl.method.inputDefaults": "error",
            "header.copyright": "error",
            "header.description": "error"
          },
          "declarations": {
            "requiredAttributes": { "ENUM": ["qualified_only", "strict"] },
            "enum": { "min": 0, "max": 99, "extensionMin": 100 }
          },
          "headers": {
            "templates": { "pou": "header.txt" },
            "assign": [ { "kinds": ["FUNCTION_BLOCK", "METHOD"], "template": "pou" },
                        { "kinds": ["ACTION"], "parent": "_Main$", "template": "pou" } ],
            "copyright": "© Syntegon Group.\nAll rights reserved.",
            "description": "DESCRIPTION"
          }
        }
        """;

    private static readonly GuidelineObject Enum = new("ENUM", "SM_STATE");

    [Fact]
    public void Enums_need_the_required_attributes_and_explicit_values()
    {
        var set = Load(Rules);

        var findings = set.CheckText(Enum, null, new PouText("{attribute 'qualified_only'}\nTYPE SM_STATE :\n(\n\tINIT := 0,\n\tRUN\n) INT;\nEND_TYPE\n", null));

        Assert.Equal(
            [("decl.enum.explicitValues", "RUN"), ("decl.requiredAttributes", (string?)null)],
            findings.Select(f => (f.Rule, f.Identifier)).OrderBy(f => f.Rule, StringComparer.Ordinal));
    }

    [Fact]
    public void Values_outside_the_range_are_allowed_only_as_named_local_extensions()
    {
        var set = Load(Rules);
        const string decl = "{attribute 'qualified_only'}\n{attribute 'strict'}\nTYPE SM_STATE :\n(\n\tINIT := 0,\n\tSM_STATE_CLEAN := 100,\n\tSTRAY := 150\n) INT;\nEND_TYPE\n";

        var v = Assert.Single(set.CheckText(Enum, null, new PouText(decl, null)));
        Assert.Equal(("decl.enum.valueRange", "warn", "STRAY"), (v.Rule, v.Severity, v.Identifier));
    }

    [Fact]
    public void Removing_a_required_attribute_from_an_existing_enum_is_reported_but_a_legacy_gap_is_not()
    {
        var set = Load(Rules);
        const string compliant = "{attribute 'qualified_only'}\n{attribute 'strict'}\nTYPE SM_STATE :\n(\n\tINIT := 0\n) INT;\nEND_TYPE\n";
        const string withoutStrict = "{attribute 'qualified_only'}\nTYPE SM_STATE :\n(\n\tINIT := 0\n) INT;\nEND_TYPE\n";
        const string legacyEdited = "{attribute 'qualified_only'}\nTYPE SM_STATE :\n(\n\tINIT := 0,\n\tRUN := 1\n) INT;\nEND_TYPE\n";

        Assert.Single(set.CheckText(Enum, new PouText(compliant, null), new PouText(withoutStrict, null)));
        Assert.Empty(set.CheckText(Enum, new PouText(withoutStrict, null), new PouText(legacyEdited, null)));
    }

    [Fact]
    public void Method_inputs_must_not_have_default_values()
    {
        var set = Load(Rules);
        var method = new GuidelineObject("METHOD", "m_Move", null, "_FB_Axis", "FUNCTION_BLOCK");
        const string header = "METHOD m_Move : BOOL\n(*\n© Syntegon Group.<br>\nAll rights reserved.<br>\nDESCRIPTION: moves<br>\n*)\n";

        var v = Assert.Single(set.CheckText(method, null, new PouText(header + "VAR_INPUT\n\tvi_lr_Y : LREAL := 0;\n\tvi_lr_X : LREAL;\nEND_VAR\n", "")));
        Assert.Equal(("decl.method.inputDefaults", "vi_lr_Y"), (v.Rule, v.Identifier));
    }

    [Fact]
    public void Creating_a_function_block_inserts_the_header_below_the_header_line()
    {
        var set = Load(Rules);
        var fb = new GuidelineObject("FUNCTION_BLOCK", "_FB_Conveyor");

        var result = set.PrepareCreate(fb, new PouText("FUNCTION_BLOCK _FB_Conveyor\nVAR\nEND_VAR\n", null), Now);

        var lines = result.Text.Declaration!.Split('\n');
        Assert.Equal(["FUNCTION_BLOCK _FB_Conveyor", "(*", "© Syntegon Group.<br>"], lines.Take(3));
        Assert.Contains("NAME: _FB_Conveyor<br>", lines);
        Assert.Contains("header inserted into the declaration", result.Applied);
        var v = Assert.Single(result.Violations);
        Assert.Equal(("header.description", "warn"), (v.Rule, v.Severity));
    }

    [Fact]
    public void Creating_an_enum_adds_the_missing_required_attributes()
    {
        var set = Load(Rules);

        var result = set.PrepareCreate(Enum, new PouText("TYPE SM_STATE :\n(\n\tINIT := 0\n) INT;\nEND_TYPE\n", null), Now);

        Assert.StartsWith("{attribute 'qualified_only'}\n{attribute 'strict'}\nTYPE SM_STATE :", result.Text.Declaration);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void Actions_get_the_header_in_their_implementation_only_below_matching_parents()
    {
        var set = Load(Rules);
        var mainAction = new GuidelineObject("ACTION", "act_Prod", null, "_FB_VAx_Main", "FUNCTION_BLOCK");
        var otherAction = mainAction with { ParentName = "_FB_Helper" };

        var fixedMain = set.ApplyCreateFixes(mainAction, new PouText(null, ""), Now);
        var fixedOther = set.ApplyCreateFixes(otherAction, new PouText(null, ""), Now);

        Assert.StartsWith("(*\n© Syntegon Group.", fixedMain.Text.Implementation);
        Assert.Equal("", fixedOther.Text.Implementation);
    }

    [Fact]
    public void Removing_the_copyright_is_an_error_but_editing_code_without_one_is_not()
    {
        var set = Load(Rules);
        var fb = new GuidelineObject("FUNCTION_BLOCK", "_FB_Conveyor");
        var withHeader = set.PrepareCreate(fb, new PouText("FUNCTION_BLOCK _FB_Conveyor\nVAR\nEND_VAR\n", null), Now).Text;
        var described = withHeader with { Declaration = withHeader.Declaration!.Replace("TODO", "Moves boxes") };
        var stripped = new PouText("FUNCTION_BLOCK _FB_Conveyor\nVAR\nEND_VAR\n", null);
        var legacy = new PouText("FUNCTION_BLOCK _FB_Conveyor\nVAR\n\tn_A : INT;\nEND_VAR\n", null);

        Assert.Empty(set.CheckText(fb, withHeader, described));
        Assert.Equal(["header.copyright", "header.description"], set.CheckText(fb, described, stripped).Select(v => v.Rule));
        Assert.Empty(set.CheckText(fb, stripped, legacy));
    }

    [Fact]
    public void Completing_a_generated_skeleton_reminds_to_fill_in_the_description()
    {
        var set = Load(Rules);

        var result = set.CompleteCreate(new GuidelineObject("FUNCTION_BLOCK", "_FB_Conveyor"), new PouText("FUNCTION_BLOCK _FB_Conveyor\r\nVAR\r\nEND_VAR\r\n", null), Now);

        Assert.Contains("\r\n© Syntegon Group.<br>\r\n", result.Text.Declaration);
        var v = Assert.Single(result.Violations);
        Assert.Equal(("header.description", "warn"), (v.Rule, v.Severity));
    }

    [Fact]
    public void Creating_a_function_block_with_only_an_implementation_is_not_rejected_for_its_header()
    {
        var set = Load(Rules);

        var result = set.PrepareCreate(new GuidelineObject("FUNCTION_BLOCK", "_FB_Conveyor"), new PouText(null, "x := 1;"), Now);

        Assert.Empty(result.Violations);
    }

    [Theory]
    [InlineData("DESCRIPTION:<br>\n\tMOTOR CONTROL<br>\n<br>\nPARAMETERS:<br>\n*)", true)]
    [InlineData("DESCRIPTION:<br>\n<br>\n****************<br>\n*)", false)]
    [InlineData("DESCRIPTION:<br>\n<br>\nPARAMETERS:<br>\n\tnone<br>\n*)", false)]
    [InlineData("DESCRIPTION: Moves boxes<br>\n*)", true)]
    public void A_description_is_the_text_between_the_keyword_and_the_next_section(string body, bool described)
    {
        var set = Load(Rules);
        var fb = new GuidelineObject("FUNCTION_BLOCK", "_FB_Conveyor");
        var decl = "FUNCTION_BLOCK _FB_Conveyor\n(*\n© Syntegon Group.<br>\nAll rights reserved.<br>\n" + body + "\nVAR\nEND_VAR\n";

        var findings = set.CheckText(fb, null, new PouText(decl, null));

        Assert.Equal(described, !findings.Any(f => f.Rule == "header.description"));
    }

    [Fact]
    public void Without_copyright_setting_a_comment_in_the_code_does_not_count_as_header()
    {
        var set = Load(Rules.Replace("\"copyright\": \"© Syntegon Group.\\nAll rights reserved.\",", ""));
        var fb = new GuidelineObject("FUNCTION_BLOCK", "_FB_Conveyor");

        var fix = set.ApplyCreateFixes(fb, new PouText("FUNCTION_BLOCK _FB_Conveyor\nVAR\n\tn_Speed : INT; (* rpm *)\nEND_VAR\n", null), Now);

        Assert.Contains("header inserted into the declaration", fix.Applied);
    }

    [Fact]
    public void A_header_that_is_already_there_is_not_inserted_twice()
    {
        var set = Load(Rules);
        var fb = new GuidelineObject("FUNCTION_BLOCK", "_FB_Conveyor");
        var once = set.PrepareCreate(fb, new PouText("FUNCTION_BLOCK _FB_Conveyor\nVAR\nEND_VAR\n", null), Now).Text;

        var twice = set.ApplyCreateFixes(fb, once, Now);

        Assert.Empty(twice.Applied);
        Assert.Equal(once.Declaration, twice.Text.Declaration);
    }
}
