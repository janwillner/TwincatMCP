using TwinCatMcp.Motion;

namespace TwinCatMcp.Tests.Unit;

public sealed class MotionNcTaskXmlTests
{
    [Fact]
    public void Reads_cycle_time_priority_and_object_id_of_the_SAF_task()
    {
        var t = NcTaskXml.Read(MotionTestData.Read("nc_task.xml"));

        Assert.Equal("NC-Task 1 SAF", t.Name);
        Assert.Equal("TINC^NC-Task 1 SAF", t.Path);
        Assert.Equal(2000, t.CycleTimeUs);
        Assert.Equal(4, t.Priority);
        Assert.Equal(501, t.AmsPort);
        Assert.Equal(0x05000010u, t.ObjectId);
    }

    [Fact]
    public void Builds_a_TaskDef_update_in_100ns_ticks()
    {
        Assert.Equal(
            "<TreeItem><TaskDef><CycleTime>10000</CycleTime><Priority>5</Priority></TaskDef></TreeItem>",
            NcTaskXml.BuildUpdate(cycleTimeUs: 1000, priority: 5));
    }

    [Fact]
    public void Omits_unset_values()
    {
        Assert.Equal("<TreeItem><TaskDef><Priority>3</Priority></TaskDef></TreeItem>", NcTaskXml.BuildUpdate(null, 3));
        Assert.Null(NcTaskXml.BuildUpdate(null, null));
    }

    [Theory]
    [InlineData("NC-Task 1", "NC-Task 1 SAF")]
    [InlineData("NC-Task 1 SAF", "NC-Task 1 SAF")]
    [InlineData("Motion", "Motion SAF")]
    public void The_SAF_suffix_is_appended_like_TwinCAT_does(string requested, string expected)
    {
        Assert.Equal(expected, NcTaskXml.SafName(requested));
    }
}
