using TwinCatMcp.Motion;

namespace TwinCatMcp.Tests.Unit;

public sealed class MotionMappingTests
{
    private static readonly string Mapping = MotionTestData.Read("mapping_el7041.xml");
    private const string Axis = "TINC^NC-Task 1 SAF^Axes^Axis 1";

    [Fact]
    public void Counts_all_links_between_the_axis_and_the_IO_terminal()
    {
        Assert.Equal(34, NcTree.CountLinks(Mapping, Axis, "TIID"));
    }

    [Fact]
    public void Counts_nothing_for_other_owners_or_prefixes()
    {
        Assert.Equal(0, NcTree.CountLinks(Mapping, "TINC^NC-Task 1 SAF^Axes^Axis 2", "TIID"));
        Assert.Equal(0, NcTree.CountLinks(Mapping, Axis, "TIPC"));
        Assert.Equal(0, NcTree.CountLinks("<?xml version=\"1.0\"?><VarLinks/>", Axis));
    }
}
