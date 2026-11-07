using TwinCatMcp.Runtime;

namespace TwinCatMcp.Tests.Unit;

public sealed class RuntimeTaskXmlTests
{
    // Shortened ProduceXml() output of a task created with CreateChild(name, 1) on TIRT (TwinCAT 3.1.4026.22).
    private const string ProducedTask =
        "<?xml version=\"1.0\"?><TreeItem><ItemName>ProbeTask</ItemName><PathName>TIRT^ProbeTask</PathName><ItemType>1</ItemType><ItemId>4</ItemId>" +
        "<ObjectId>#x02010040</ObjectId><ItemSubType>1</ItemSubType><ChildCount>0</ChildCount><Disabled>false</Disabled><TreeImageId>4</TreeImageId>" +
        "<TaskDef><Priority>1</Priority><AmsPort>351</AmsPort><CycleTime>100000</CycleTime><AutoStart>true</AutoStart><IoAtBegin>false</IoAtBegin>" +
        "<ExceedWarning>0</ExceedWarning><CreateSymbols>false</CreateSymbols><ExtSync>false</ExtSync><TickModulo>0</TickModulo>" +
        "<InputUpdatePreTicks>0</InputUpdatePreTicks><DisableFpExceptions>false</DisableFpExceptions><WatchdogStackCapacity>0</WatchdogStackCapacity>" +
        "<CpuAffinity>#x0</CpuAffinity></TaskDef><TcModuleInstance><Module><Name>ADT</Name></Module></TcModuleInstance></TreeItem>";

    [Fact]
    public void Parses_task_definition_from_ProduceXml()
    {
        var t = TaskXml.Parse(ProducedTask);

        Assert.Equal("ProbeTask", t.Name);
        Assert.Equal("TIRT^ProbeTask", t.Path);
        Assert.Equal(1, t.Priority);
        Assert.Equal(351, t.AmsPort);
        Assert.Equal(100000u, t.CycleTime.Ticks100ns);
        Assert.Equal(10.0, t.CycleTime.Milliseconds);
        Assert.True(t.AutoStart);
        Assert.False(t.IoAtBegin);
        Assert.False(t.WithImage);
        Assert.Equal("#x0", t.CpuAffinity);
        Assert.Empty(t.Cores);
        Assert.False(t.Disabled);
    }

    [Fact]
    public void Task_created_with_image_has_no_ItemSubType_element()
    {
        // ProduceXml() of a task created with CreateChild(name, 0) (with image) has no <ItemSubType>; tree_get_item reports sub type 0.
        const string withImage = "<TreeItem><ItemName>ItImg</ItemName><PathName>TIRT^ItImg</PathName><ItemType>1</ItemType><ItemId>4</ItemId>" +
                                 "<ObjectId>#x02010040</ObjectId><ChildCount>0</ChildCount><Disabled>false</Disabled><TaskDef><Priority>1</Priority>" +
                                 "<AmsPort>301</AmsPort><CycleTime>100000</CycleTime><AutoStart>true</AutoStart></TaskDef></TreeItem>";

        Assert.True(TaskXml.Parse(withImage).WithImage);
    }

    [Fact]
    public void Builds_minimal_TaskDef_fragment_with_only_the_given_values()
    {
        var xml = TaskXml.BuildTaskDef(priority: 5, cycleTimeTicks: 5000, autoStart: null, cpuAffinity: null);

        Assert.Equal("<TreeItem><TaskDef><Priority>5</Priority><CycleTime>5000</CycleTime></TaskDef></TreeItem>", xml);
    }

    [Fact]
    public void Builds_TaskDef_fragment_with_autostart_and_affinity()
    {
        // Format of the CpuAffinity element as in the Beckhoff sample "Assigning CPU cores" (#x + 16 hex digits).
        var xml = TaskXml.BuildTaskDef(priority: null, cycleTimeTicks: null, autoStart: false, cpuAffinity: 0x4);

        Assert.Equal("<TreeItem><TaskDef><AutoStart>false</AutoStart><CpuAffinity>#x0000000000000004</CpuAffinity></TaskDef></TreeItem>", xml);
    }

    [Fact]
    public void Builds_LinkedTask_fragment_for_a_PLC_task_reference()
        => Assert.Equal(
            "<TreeItem><PlcTaskDef><LinkedTask>TIRT^Fast &amp; Slow</LinkedTask></PlcTaskDef></TreeItem>",
            TaskXml.BuildLinkedTask("TIRT^Fast & Slow"));

    [Fact]
    public void Parses_LinkedTask_of_a_PLC_task_reference()
    {
        const string produced = "<TreeItem><ItemName>PlcTask</ItemName><PathName>TIPC^Plc1^Plc1 Project^PlcTask</PathName><ItemType>621</ItemType>" +
                                "<PlcTaskDef><LinkedTask>TIRT^PlcTask</LinkedTask></PlcTaskDef><VSProperties /></TreeItem>";

        Assert.Equal("TIRT^PlcTask", TaskXml.ParseLinkedTask(produced));
    }
}
