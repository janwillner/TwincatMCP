using TwinCatMcp.Plc;

namespace TwinCatMcp.Tests.Unit;

public sealed class PlcStDeclarationTests
{
    [Fact]
    public void Parses_function_block_header_with_extends_and_implements()
    {
        var d = StDeclaration.Parse("FUNCTION_BLOCK FB_C EXTENDS FB_A IMPLEMENTS I_Derived, I_Other\r\nVAR\r\nEND_VAR\r\n");

        Assert.Equal("FUNCTION_BLOCK", d.Kind);
        Assert.Equal("FB_C", d.Name);
        Assert.Equal("FB_A", d.Extends);
        Assert.Equal(["I_Derived", "I_Other"], d.Implements);
    }

    [Fact]
    public void Parses_function_return_type_and_method_access()
    {
        var f = StDeclaration.Parse("FUNCTION F_Add :DINT\nVAR_INPUT\nEND_VAR");
        var m = StDeclaration.Parse("METHOD PROTECTED M_Do : BOOL");
        var p = StDeclaration.Parse("PROPERTY PUBLIC P_Value : INT");

        Assert.Equal(("FUNCTION", "F_Add", "DINT"), (f.Kind, f.Name, f.ReturnType));
        Assert.Equal(("METHOD", "M_Do", "BOOL", "PROTECTED"), (m.Kind, m.Name, m.ReturnType, m.Access));
        Assert.Equal(("PROPERTY", "P_Value", "INT", "PUBLIC"), (p.Kind, p.Name, p.ReturnType, p.Access));
    }

    [Fact]
    public void Skips_attribute_pragmas_and_comments_before_the_header()
    {
        var d = StDeclaration.Parse("// comment\n{attribute 'hide'}\n(* block *)\nFUNCTION_BLOCK ABSTRACT FB_Base\n");

        Assert.Equal(("FUNCTION_BLOCK", "FB_Base"), (d.Kind, d.Name));
    }

    [Fact]
    public void Parses_variables_per_section_with_type_init_and_comment()
    {
        const string decl = """
            FUNCTION_BLOCK FB_Counter
            VAR_INPUT
            	bEnable : BOOL := TRUE; // start counting
            	nLimit, nStep : INT;
            END_VAR
            VAR_OUTPUT
            	nCount : INT;
            END_VAR
            VAR RETAIN
            	{attribute 'hide'}
            	fTotal : LREAL := 0.0;
            	aBuffer : ARRAY[0..9] OF BYTE;
            	(* old : INT; *)
            END_VAR
            """;

        var d = StDeclaration.Parse(decl);

        Assert.Equal(["VAR_INPUT", "VAR_OUTPUT", "VAR RETAIN"], d.Sections.Select(s => s.Kind));
        var input = d.Sections[0].Variables;
        Assert.Equal(["bEnable", "nLimit", "nStep"], input.Select(v => v.Name));
        Assert.Equal(("BOOL", "TRUE", "start counting"), (input[0].Type, input[0].Init, input[0].Comment));
        Assert.Equal("INT", input[2].Type);
        var retain = d.Sections[2].Variables;
        Assert.Equal(["fTotal", "aBuffer"], retain.Select(v => v.Name));
        Assert.Equal("ARRAY[0..9] OF BYTE", retain[1].Type);
    }

    [Fact]
    public void Parses_gvl_and_located_variables()
    {
        var d = StDeclaration.Parse("{attribute 'qualified_only'}\nVAR_GLOBAL\n\tbInput AT %I* : BOOL;\nEND_VAR\n");

        Assert.Equal("VAR_GLOBAL", d.Sections.Single().Kind);
        var v = d.Sections[0].Variables.Single();
        Assert.Equal(("bInput", "BOOL", "%I*"), (v.Name, v.Type, v.Address));
    }

    [Fact]
    public void Parses_struct_members_of_a_dut()
    {
        var d = StDeclaration.Parse("TYPE ST_Data :\nSTRUCT\n\tnValue : INT;\n\tsName : STRING(80);\nEND_STRUCT\nEND_TYPE\n");

        Assert.Equal(("TYPE", "ST_Data"), (d.Kind, d.Name));
        Assert.Equal("STRUCT", d.Sections.Single().Kind);
        Assert.Equal(["nValue", "sName"], d.Sections[0].Variables.Select(v => v.Name));
        Assert.Equal("STRING(80)", d.Sections[0].Variables[1].Type);
    }
}
