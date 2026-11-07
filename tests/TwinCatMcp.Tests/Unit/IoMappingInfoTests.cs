using TwinCatMcp.Io;

namespace TwinCatMcp.Tests.Unit;

public sealed class IoMappingInfoTests
{
    // Real ITcSysManager.ProduceMappingInfo() output (TwinCAT 3.1.4026) after two LinkVariables calls.
    private const string Produced =
        "<?xml version=\"1.0\"?><VarLinks><OwnerA Name=\"TIPC^Plc1^Plc1 Instance\">" +
        "<OwnerB Name=\"TIID^M_Link^Term 1 (EK1100)^Term 2 (EL1008)\"><Link VarA=\"PlcTask Inputs^GVL_IO.bIn1\" VarB=\"Channel 1^Input\"/></OwnerB>" +
        "<OwnerB Name=\"TIID^M_Link^Term 1 (EK1100)^Term 3 (EL2008)\"><Link VarA=\"PlcTask Outputs^GVL_IO.bOut1\" VarB=\"Channel 1^Output\"/></OwnerB>" +
        "</OwnerA></VarLinks>";

    [Fact]
    public void Parses_produced_mapping_into_full_paths()
    {
        var links = MappingInfo.Parse(Produced);

        Assert.Equal(2, links.Count);
        Assert.Equal("TIPC^Plc1^Plc1 Instance^PlcTask Inputs^GVL_IO.bIn1", links[0].A);
        Assert.Equal("TIID^M_Link^Term 1 (EK1100)^Term 2 (EL1008)^Channel 1^Input", links[0].B);
        Assert.Equal("TIID^M_Link^Term 1 (EK1100)^Term 3 (EL2008)", links[1].OwnerB);
        Assert.Equal("PlcTask Outputs^GVL_IO.bOut1", links[1].VarA);
    }

    [Fact]
    public void Empty_or_missing_mapping_yields_no_links()
    {
        Assert.Empty(MappingInfo.Parse(""));
        Assert.Empty(MappingInfo.Parse("<?xml version=\"1.0\"?><VarLinks/>"));
    }

    [Fact]
    public void Build_produces_the_same_structure_as_TwinCAT()
    {
        var links = MappingInfo.Parse(Produced);

        var xml = MappingInfo.Build(links);

        Assert.True(System.Xml.Linq.XNode.DeepEquals(System.Xml.Linq.XElement.Parse(Produced), System.Xml.Linq.XElement.Parse(xml)), xml);
    }

    [Fact]
    public void Offsets_and_size_are_kept()
    {
        var xml = MappingInfo.Build([new VarLink("a^x", "b^y", "a", "x", "b", "y", 8, 0, 8)]);

        var l = Assert.Single(MappingInfo.Parse(xml));
        Assert.Equal((8, 0, 8), (l.OffsetA, l.OffsetB, l.BitSize));
    }

    [Fact]
    public void LinkedTo_finds_the_other_side_in_both_directions()
    {
        var links = MappingInfo.Parse(Produced);

        Assert.Equal(["TIPC^Plc1^Plc1 Instance^PlcTask Inputs^GVL_IO.bIn1"], MappingInfo.LinkedTo(links, "TIID^M_Link^Term 1 (EK1100)^Term 2 (EL1008)^Channel 1^Input"));
        Assert.Equal(["TIID^M_Link^Term 1 (EK1100)^Term 3 (EL2008)^Channel 1^Output"], MappingInfo.LinkedTo(links, "TIPC^Plc1^Plc1 Instance^PlcTask Outputs^GVL_IO.bOut1"));
    }

    [Theory]
    [InlineData("BOOL", 8, "BIT", 1, true)]
    [InlineData("INT", 16, "INT", 16, true)]
    [InlineData("INT", 16, "UINT", 16, true)]
    [InlineData("DINT", 32, "INT", 16, false)]
    [InlineData("BYTE", 8, "BIT", 1, false)]
    public void Link_compatibility_treats_BOOL_and_BIT_as_equal(string ta, int sa, string tb, int sb, bool expected)
        => Assert.Equal(expected, IoXml.AreLinkCompatible(
            new VarInfo("a", "a", ta, sa, null, "input", null, null, null),
            new VarInfo("b", "b", tb, sb, null, "input", null, null, null)));
}
