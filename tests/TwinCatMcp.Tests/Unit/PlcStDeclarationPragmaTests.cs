using TwinCatMcp.Plc;

namespace TwinCatMcp.Tests.Unit;

/// <summary>Parser details the coding guideline checks need: attributes, naming regions, enum values, block comments.</summary>
public sealed class PlcStDeclarationPragmaTests
{
    [Fact]
    public void Attributes_before_the_header_belong_to_the_object()
    {
        var d = StDeclaration.Parse("{attribute 'qualified_only'}\n{attribute 'strict'}\nTYPE E_State :\n(\n\tIDLE := 0\n);\nEND_TYPE\n");

        Assert.True(d.Attributes.ContainsKey("qualified_only"));
        Assert.True(d.Attributes.ContainsKey("strict"));
    }

    [Fact]
    public void Attributes_of_a_gvl_come_before_var_global()
    {
        var d = StDeclaration.Parse("{attribute 'qualified_only'}\nVAR_GLOBAL\n\tbStart : BOOL;\nEND_VAR\n");

        Assert.Equal(["qualified_only"], d.Attributes.Keys);
        Assert.Empty(d.Sections[0].Variables[0].Attributes);
    }

    [Fact]
    public void Attribute_with_value_applies_to_the_next_variable_only()
    {
        var d = StDeclaration.Parse("FUNCTION_BLOCK FB_A\nVAR\n\t{attribute 'naming' := 'omit'}\n\tBad : INT;\n\tbGood : BOOL;\nEND_VAR\n");

        var vars = d.Sections[0].Variables;
        Assert.Equal("omit", vars[0].Attributes["naming"]);
        Assert.False(vars[1].Attributes.ContainsKey("naming"));
    }

    [Fact]
    public void Naming_off_covers_every_declaration_until_naming_on()
    {
        var decl = """
            {attribute 'naming' := 'off'}
            FUNCTION_BLOCK fbLegacy
            VAR
            	a : INT;
            	{attribute 'naming' := 'on'}
            	nChecked : INT;
            END_VAR
            """;

        var d = StDeclaration.Parse(decl);

        Assert.Equal("off", d.Attributes["naming"]);
        Assert.Equal("off", d.Sections[0].Variables[0].Attributes["naming"]);
        Assert.False(d.Sections[0].Variables[1].Attributes.ContainsKey("naming"));
    }

    [Fact]
    public void Variables_know_their_line_in_the_declaration()
    {
        var d = StDeclaration.Parse("FUNCTION_BLOCK FB_A\nVAR_INPUT\n\tbA : BOOL;\n\n\tnB : INT;\nEND_VAR\n");

        Assert.Equal([3, 5], d.Sections[0].Variables.Select(v => v.Line));
    }

    [Fact]
    public void Parses_enum_values_with_and_without_explicit_value()
    {
        var decl = """
            {attribute 'qualified_only'}
            TYPE SM_STATE :
            (
            	INIT := 0,    // start
            	RUN := 10,
            	(* comment *) STOP
            ) INT;
            END_TYPE
            """;

        var d = StDeclaration.Parse(decl);

        Assert.Equal(["INIT", "RUN", "STOP"], d.EnumValues.Select(v => v.Name));
        Assert.Equal(["0", "10", null], d.EnumValues.Select(v => v.Value));
        Assert.Equal("INT", d.EnumBaseType);
    }

    [Fact]
    public void Parses_a_single_line_enum()
    {
        var d = StDeclaration.Parse("TYPE E_Mode : (eAuto := 1, eManual := 2);\nEND_TYPE");

        Assert.Equal(["eAuto", "eManual"], d.EnumValues.Select(v => v.Name));
        Assert.Equal(["1", "2"], d.EnumValues.Select(v => v.Value));
    }

    [Fact]
    public void Block_comments_with_slash_star_are_ignored()
    {
        var d = StDeclaration.Parse("FUNCTION_BLOCK FB_A\nVAR\n\t/* nOld : INT; */\n\tnNew : INT;\nEND_VAR\n");

        Assert.Equal(["nNew"], d.Sections[0].Variables.Select(v => v.Name));
    }
}
