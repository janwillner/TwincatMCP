using TwinCatMcp.Core;
using TwinCatMcp.Motion;

namespace TwinCatMcp.Tests.Unit;

public sealed class MotionTcComXmlTests
{
    private static readonly string DataLogger = MotionTestData.Read("tccom_datalogger.xml");

    [Fact]
    public void Reads_class_identity_and_object_id_of_a_TcCOM_instance()
    {
        var m = TcComXml.Read(DataLogger);

        Assert.Equal("DataLogger1 (DataLogger)", m.Name);
        Assert.Equal("TIRC^TcCOM Objects^DataLogger1 (DataLogger)", m.Path);
        Assert.Equal("DataLogger", m.ClassName);
        Assert.Equal("{AF60AC1D-5CDE-4423-B485-94BB0587928B}", m.ClassId);
        Assert.Equal("TcAnalytics", m.ClassFactory);
        Assert.Equal(0x01010010u, m.ObjectId);
    }

    [Fact]
    public void Reads_parameters_with_types_and_current_values()
    {
        var m = TcComXml.Read(DataLogger);

        var client = Assert.Single(m.Parameters, p => p.Name == "MqttClient");
        Assert.Equal("OTCID", client.Type);
        Assert.Equal("0", client.Value);
        var topic = Assert.Single(m.Parameters, p => p.Name == "MqttTopic");
        Assert.Equal("STRING(80)", topic.Type);
        Assert.Equal("", topic.Value);
    }

    [Fact]
    public void Reads_contexts_and_data_areas()
    {
        var m = TcComXml.Read(DataLogger);

        var ctx = Assert.Single(m.Contexts);
        Assert.Equal(1, ctx.Id);
        Assert.Null(ctx.TaskObjectId);
        Assert.Equal(["Inputs", "Outputs"], m.DataAreas.Select(d => d.Name));
        Assert.Equal(["Value", "Status", "Data"], m.DataAreas[0].Symbols);
    }

    [Fact]
    public void Builds_a_ParameterValues_update_using_String_elements_for_string_parameters()
    {
        var m = TcComXml.Read(DataLogger);

        var xml = TcComXml.BuildParameterUpdate(m, new Dictionary<string, string> { ["MqttTopic"] = "Line <1>", ["mqttclient"] = "#x01010020" });

        Assert.Equal(
            "<TreeItem><TcModuleInstance><Module><ParameterValues>" +
            "<Value><Name>MqttClient</Name><Value>#x01010020</Value></Value>" +
            "<Value><Name>MqttTopic</Name><String>Line &lt;1&gt;</String></Value>" +
            "</ParameterValues></Module></TcModuleInstance></TreeItem>",
            xml);
    }

    [Fact]
    public void Unknown_parameters_are_rejected_with_the_list_of_valid_ones()
    {
        var m = TcComXml.Read(DataLogger);

        var ex = Assert.Throws<TcException>(() => TcComXml.BuildParameterUpdate(m, new Dictionary<string, string> { ["Foo"] = "1" }));

        Assert.Contains("Foo", ex.Message);
        Assert.Contains("MqttTopic", ex.Hint);
    }

    [Fact]
    public void Resolves_a_short_object_name_to_the_tree_name_with_class_suffix()
    {
        string[] children = ["JobPool (TcJobPool)", "DataLogger1 (DataLogger)", "DataLogger10 (DataLogger)"];

        Assert.Equal("DataLogger1 (DataLogger)", TcComXml.ResolveChildName(children, "DataLogger1"));
        Assert.Equal("DataLogger1 (DataLogger)", TcComXml.ResolveChildName(children, "DataLogger1 (DataLogger)"));
        Assert.Null(TcComXml.ResolveChildName(children, "Missing"));
    }

    [Fact]
    public void Reads_a_class_catalog_from_a_tmc_file()
    {
        var classes = TmcCatalog.Parse(MotionTestData.Read("TcJobPool.tmc"), @"C:\x\TcJobPool.tmc");

        var c = Assert.Single(classes);
        Assert.Equal("TcJobPool", c.Name);
        Assert.Equal("{0102000C-0000-0000-F000-000000000064}", c.ClassId);
        Assert.Equal("TcRtsObjects", c.ClassFactory);
        Assert.Equal("JobObjects", c.Group);
        Assert.Equal("Beckhoff Automation GmbH", c.Vendor);
        Assert.Equal(["JobTasks", "PoolId"], c.Parameters);
    }

    [Fact]
    public void Task_object_id_is_read_from_the_task_xml()
    {
        Assert.Equal(0x05000010u, TcComXml.ReadObjectId(MotionTestData.Read("nc_task.xml")));
    }
}
