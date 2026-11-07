using TwinCatMcp.Core;
using TwinCatMcp.Plc;

namespace TwinCatMcp.Tests.Unit;

/// <summary>Expected numbers come from the Beckhoff "Creating PLC objects" table and were verified live on 3.1.4026.</summary>
public sealed class PlcObjectModelTests
{
    [Theory]
    [InlineData(602, "PROGRAM")]
    [InlineData(603, "FUNCTION")]
    [InlineData(604, "FUNCTION_BLOCK")]
    [InlineData(605, "ENUM")]
    [InlineData(606, "STRUCT")]
    [InlineData(607, "UNION")]
    [InlineData(608, "ACTION")]
    [InlineData(609, "METHOD")]
    [InlineData(610, "METHOD")]
    [InlineData(611, "PROPERTY")]
    [InlineData(612, "PROPERTY")]
    [InlineData(615, "GVL")]
    [InlineData(616, "TRANSITION")]
    [InlineData(618, "INTERFACE")]
    [InlineData(623, "ALIAS")]
    [InlineData(601, "FOLDER")]
    public void Maps_item_types_to_kinds(int itemType, string kind)
    {
        Assert.Equal(kind, PlcObjects.KindOf(itemType));
    }

    [Theory]
    [InlineData("pou", 602, true)]
    [InlineData("pou", 618, false)]
    [InlineData("dut", 606, true)]
    [InlineData("dut", 623, true)]
    [InlineData("fb", 604, true)]
    [InlineData("method", 610, true)]
    [InlineData("itf", 618, true)]
    [InlineData("gvl", 629, true)]
    public void Kind_filters_accept_aliases_and_groups(string filter, int itemType, bool expected)
    {
        Assert.Equal(expected, PlcObjects.MatchesKind(itemType, [filter]));
    }

    [Theory]
    [InlineData("ST", 1)]
    [InlineData("il", 2)]
    [InlineData("SFC", 3)]
    [InlineData("FBD", 4)]
    [InlineData("CFC", 5)]
    [InlineData("LD", 6)]
    [InlineData(null, 1)]
    public void Parses_languages_to_IECLANGUAGETYPES(string? language, int expected)
    {
        Assert.Equal(expected, PlcObjects.ParseLanguage(language));
    }

    [Fact]
    public void Unknown_language_is_rejected()
    {
        Assert.Throws<TcException>(() => PlcObjects.ParseLanguage("Python"));
    }

    [Fact]
    public void Fb_vinfo_without_inheritance_is_the_plain_language()
    {
        Assert.Equal(1, PlcObjects.PouVInfo("FUNCTION_BLOCK", 1, null, null, null));
    }

    [Fact]
    public void Fb_vinfo_with_extends_and_implements_has_five_slots()
    {
        var v = PlcObjects.PouVInfo("FUNCTION_BLOCK", 1, null, "FB_Base", "I_A, I_B");

        Assert.Equal(new[] { "1", "Extends", "FB_Base", "Implements", "I_A, I_B" }, v);
    }

    [Fact]
    public void Fb_vinfo_with_implements_only_has_three_slots()
    {
        Assert.Equal(new[] { "1", "Implements", "I_A" }, PlcObjects.PouVInfo("FUNCTION_BLOCK", 1, null, null, "I_A"));
    }

    [Fact]
    public void Function_vinfo_is_language_and_return_type_and_return_type_is_required()
    {
        Assert.Equal(new[] { "6", "BOOL" }, PlcObjects.PouVInfo("FUNCTION", 6, "BOOL", null, null));
        Assert.Throws<TcException>(() => PlcObjects.PouVInfo("FUNCTION", 1, null, null, null));
    }

    [Theory]
    [InlineData("PROGRAM", 602)]
    [InlineData("program", 602)]
    [InlineData("FB", 604)]
    [InlineData("FUNCTION_BLOCK", 604)]
    [InlineData("FUNCTION", 603)]
    public void Pou_type_names_map_to_sub_types(string type, int subType)
    {
        Assert.Equal(subType, PlcObjects.PouSubType(type));
    }

    [Theory]
    [InlineData("STRUCT", 606)]
    [InlineData("ENUM", 605)]
    [InlineData("UNION", 607)]
    [InlineData("ALIAS", 623)]
    public void Dut_type_names_map_to_sub_types(string type, int subType)
    {
        Assert.Equal(subType, PlcObjects.DutSubType(type));
    }

    [Fact]
    public void Default_dut_declarations_follow_the_TwinCAT_layout()
    {
        Assert.Equal("TYPE T_Speed : LREAL;\r\nEND_TYPE\r\n", PlcObjects.DefaultDutDeclaration("ALIAS", "T_Speed", "LREAL"));
        Assert.Throws<TcException>(() => PlcObjects.DefaultDutDeclaration("ALIAS", "T_X", null));
        Assert.Null(PlcObjects.DefaultDutDeclaration("STRUCT", "ST_X", null));
    }

    [Theory]
    [InlineData("TIPC^Plc1^Plc1 Project^POUs^MAIN", "POUs.MAIN")]
    [InlineData("TIPC^Plc1^Plc1 Project^POUs^Sub^FB_A", "POUs.Sub.FB_A")]
    public void Converts_tree_paths_to_PLCopen_selections(string path, string selection)
    {
        Assert.Equal(selection, PlcObjects.ToSelection("TIPC^Plc1^Plc1 Project", path));
    }

    [Fact]
    public void Selection_outside_the_project_is_rejected()
    {
        Assert.Throws<TcException>(() => PlcObjects.ToSelection("TIPC^Plc1^Plc1 Project", "TIPC^Plc2^Plc2 Project^POUs^MAIN"));
    }
}
