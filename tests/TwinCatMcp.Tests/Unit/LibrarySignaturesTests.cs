using TwinCatMcp.Libraries;

namespace TwinCatMcp.Tests.Unit;

public sealed class LibrarySignaturesTests
{
    // Trimmed from live ITcPlcLibraryManager2.ProduceLibrarySignatures / ProduceAllLibrarySignatures (4026.22).
    // ProduceAllLibrarySignatures returns several <Library> roots concatenated (an XML fragment, not a document).
    private const string Xml = """
        <Library><LibraryName>Tc2_Standard</LibraryName><Version>3.4.5.0</Version><Distributor>Beckhoff Automation GmbH</Distributor><TypeSignatures><TypeSignature type="Function"><Name>CONCAT</Name><Comment>
        	Concatenation of two strings.
        </Comment><Inputs><Input><Name>STR1</Name><DataType>STRING(255)</DataType><Comment> Head part of the concatenated result</Comment></Input><Input><Name>STR2</Name><DataType>STRING(255)</DataType></Input></Inputs><Outputs><Output><Name>CONCAT</Name><DataType>STRING(255)</DataType></Output></Outputs></TypeSignature><TypeSignature type="VarGlobal"><Name>Global_Version</Name><Comment> This function has been automatically generated from the project information.</Comment><Constants><Constant><Name>stLibVersion_Tc2_Standard</Name><DataType>ST_LibVersion</DataType></Constant></Constants></TypeSignature></TypeSignatures></Library><Library><LibraryName>Tc2_MC2</LibraryName><Version>3.3.72.0</Version><Distributor>Beckhoff Automation GmbH</Distributor><TypeSignatures><TypeSignature type="Type"><Name>ST_MoveOptions</Name></TypeSignature><TypeSignature type="FunctionBlock"><Name>MC_MoveAbsolute</Name><Inputs><Input><Name>Execute</Name><DataType>BOOL</DataType><Comment> Start the motion at rising edge </Comment></Input><Input><Name>Position</Name><DataType>LREAL</DataType><Comment> Target position for the motion </Comment></Input><Input><Name>BufferMode</Name><DataType>MC_BufferMode</DataType><Comment>	Direction			:	MC_Direction;
         not available </Comment></Input></Inputs><Outputs><Output><Name>Done</Name><DataType>BOOL</DataType></Output><Output><Name>ErrorID</Name><DataType>UDINT</DataType></Output></Outputs><InOuts><InOut><Name>Axis</Name><DataType>AXIS_REF</DataType><Comment>Reference to an axis</Comment></InOut></InOuts></TypeSignature><TypeSignature type="FunctionBlock"><Name>MC_MoveAdditive</Name></TypeSignature><TypeSignature type="Interface"><Name>I_TcMC_Axis</Name></TypeSignature></TypeSignatures></Library>
        """;

    [Fact]
    public void Parses_all_libraries_of_a_fragment()
    {
        var libs = LibrarySignatures.Parse(Xml);

        Assert.Equal(["Tc2_Standard", "Tc2_MC2"], libs.Select(l => l.LibraryName));
        Assert.Equal("3.3.72.0", libs[1].Version);
        Assert.Equal("Beckhoff Automation GmbH", libs[1].Distributor);
        Assert.Equal(4, libs[1].Entries.Count);
    }

    [Fact]
    public void Parses_function_block_variables()
    {
        var fb = LibrarySignatures.Parse(Xml)[1].Entries.Single(e => e.Name == "MC_MoveAbsolute");

        Assert.Equal("FunctionBlock", fb.Kind);
        Assert.Equal(["Execute", "Position", "BufferMode"], fb.Inputs!.Select(v => v.Name));
        Assert.Equal("LREAL", fb.Inputs![1].DataType);
        Assert.Equal("Target position for the motion", fb.Inputs[1].Comment);
        Assert.Equal(["Done", "ErrorID"], fb.Outputs!.Select(v => v.Name));
        Assert.Equal("AXIS_REF", fb.InOuts!.Single().DataType);
        Assert.Null(fb.ReturnType);
    }

    [Fact]
    public void Function_return_value_is_the_output_named_like_the_function()
    {
        var f = LibrarySignatures.Parse(Xml)[0].Entries.Single(e => e.Name == "CONCAT");

        Assert.Equal("STRING(255)", f.ReturnType);
        Assert.Null(f.Outputs);
        Assert.Equal("Concatenation of two strings.", f.Comment);
    }

    [Fact]
    public void Multi_line_comments_are_collapsed_to_one_line()
    {
        var fb = LibrarySignatures.Parse(Xml)[1].Entries.Single(e => e.Name == "MC_MoveAbsolute");

        Assert.Equal("Direction : MC_Direction; not available", fb.Inputs![2].Comment);
    }

    [Fact]
    public void Find_matches_exact_names_case_insensitively_across_libraries()
    {
        var hits = LibrarySignatures.Find(LibrarySignatures.Parse(Xml), "mc_moveabsolute");

        var (lib, entry) = Assert.Single(hits);
        Assert.Equal("Tc2_MC2", lib.LibraryName);
        Assert.Equal("MC_MoveAbsolute", entry.Name);
    }

    [Fact]
    public void Find_supports_wildcards_and_kind_filter()
    {
        var all = LibrarySignatures.Parse(Xml);

        Assert.Equal(["MC_MoveAbsolute", "MC_MoveAdditive"], LibrarySignatures.Find(all, "MC_Move*").Select(h => h.Entry.Name));
        Assert.Equal(["I_TcMC_Axis"], LibrarySignatures.Find(all, "*", kind: "interface").Select(h => h.Entry.Name));
    }

    [Fact]
    public void Renders_function_block_as_IEC_declaration()
    {
        var fb = LibrarySignatures.Parse(Xml)[1].Entries.Single(e => e.Name == "MC_MoveAbsolute");

        var expected = """
            FUNCTION_BLOCK MC_MoveAbsolute
            VAR_INPUT
                Execute : BOOL; // Start the motion at rising edge
                Position : LREAL; // Target position for the motion
                BufferMode : MC_BufferMode; // Direction : MC_Direction; not available
            END_VAR
            VAR_OUTPUT
                Done : BOOL;
                ErrorID : UDINT;
            END_VAR
            VAR_IN_OUT
                Axis : AXIS_REF; // Reference to an axis
            END_VAR
            END_FUNCTION_BLOCK
            """;
        Assert.Equal(expected.ReplaceLineEndings("\n"), LibrarySignatures.ToIec(fb));
    }

    [Fact]
    public void Renders_function_with_return_type_and_leading_comment()
    {
        var f = LibrarySignatures.Parse(Xml)[0].Entries.Single(e => e.Name == "CONCAT");

        var expected = """
            // Concatenation of two strings.
            FUNCTION CONCAT : STRING(255)
            VAR_INPUT
                STR1 : STRING(255); // Head part of the concatenated result
                STR2 : STRING(255);
            END_VAR
            END_FUNCTION
            """;
        Assert.Equal(expected.ReplaceLineEndings("\n"), LibrarySignatures.ToIec(f));
    }

    [Fact]
    public void Renders_global_variable_list_constants()
    {
        var g = LibrarySignatures.Parse(Xml)[0].Entries.Single(e => e.Name == "Global_Version");

        var expected = """
            // This function has been automatically generated from the project information.
            // GVL Global_Version
            VAR_GLOBAL CONSTANT
                stLibVersion_Tc2_Standard : ST_LibVersion;
            END_VAR
            """;
        Assert.Equal(expected.ReplaceLineEndings("\n"), LibrarySignatures.ToIec(g));
    }

    [Fact]
    public void Types_without_members_render_a_note()
    {
        var t = LibrarySignatures.Parse(Xml)[1].Entries.Single(e => e.Name == "ST_MoveOptions");

        Assert.Equal("TYPE ST_MoveOptions (* members are not part of the library signature *)", LibrarySignatures.ToIec(t));
    }
}
