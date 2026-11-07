using TwinCatMcp.Core;
using TwinCatMcp.Motion;

namespace TwinCatMcp.Tests.Unit;

public sealed class MotionAxisXmlTests
{
    private static readonly string DefaultAxis = MotionTestData.Read("axis_default.xml");
    private static readonly string LinkedAxis = MotionTestData.Read("axis_linked_el7041.xml");

    [Fact]
    public void Reads_the_compact_parameter_set_of_a_new_continuous_axis()
    {
        var p = AxisXml.ReadParameters(DefaultAxis);

        Assert.Equal("mm", p["unit"]);
        Assert.Equal(2000.0, p["maxVelocity"]);
        Assert.Equal(2000.0, p["rapidTraverseVelocity"]);
        Assert.Equal(600.0, p["manualVelocityFast"]);
        Assert.Equal(100.0, p["manualVelocitySlow"]);
        Assert.Equal(1500.0, p["acceleration"]);
        Assert.Equal(1500.0, p["deceleration"]);
        Assert.Equal(2250.0, p["jerk"]);
        Assert.Equal(0.0001, p["scalingFactor"]);
        Assert.Equal(1.0, p["scalingFactorDenominator"]);
        Assert.Equal(2, p["encoderMode"]);
        Assert.Equal(false, p["encoderInverse"]);
        Assert.Equal(360.0, p["moduloFactor"]);
        Assert.Equal(false, p["softLimitMinEnabled"]);
        Assert.Equal(0.0, p["softLimitMin"]);
        Assert.Equal(false, p["softLimitMaxEnabled"]);
        Assert.Equal(true, p["positionLagMonitoring"]);
        Assert.Equal(5.0, p["positionLagMax"]);
        Assert.Equal(0.02, p["positionLagFilterTime"]);
        Assert.Equal(2200.0, p["referenceVelocity"]);
        Assert.Equal(1.0, p["positionControlKp"]);
        Assert.Equal(true, p["targetPositionMonitoring"]);
        Assert.Equal(2.0, p["targetPositionWindow"]);
    }

    [Fact]
    public void Reads_the_axis_summary_of_an_unlinked_axis()
    {
        var s = AxisXml.ReadSummary(DefaultAxis);

        Assert.Equal("Axis 1", s.Name);
        Assert.Equal("TINC^NC-Task 1 SAF^Axes^Axis 1", s.Path);
        Assert.Equal(1, s.SubType);
        Assert.Equal("Continuous Axis", s.SubTypeName);
        Assert.Equal(1, s.EncoderType);
        Assert.Equal(5, s.DriveType);
        Assert.Equal(1, s.ControllerType);
        Assert.Null(s.IoLink);
        Assert.Null(s.AxisIoType);
        Assert.Equal(0x05010010u, s.ObjectId);
    }

    [Fact]
    public void Reads_the_io_link_of_an_axis_linked_to_an_EL7041()
    {
        var s = AxisXml.ReadSummary(LinkedAxis);

        Assert.Equal("TIID^Device 1 (EtherCAT)^Term 1 (EK1100)^Term 2 (EL7041)", s.IoLink);
        Assert.Equal(0, s.IoChannel);
        Assert.Equal("MDP-703", s.AxisIoType);
        Assert.Equal(4, s.EncoderType);
        Assert.Equal(24, s.DriveType);
    }

    [Fact]
    public void Builds_a_minimal_ConsumeXml_fragment_for_a_partial_update()
    {
        var xml = AxisXml.BuildParameterUpdate(new Dictionary<string, object?>
        {
            ["maxVelocity"] = 3000.0,
            ["acceleration"] = "2500",
            ["softLimitMaxEnabled"] = true,
            ["softLimitMax"] = 500,
        });

        Assert.Equal(
            "<TreeItem><NcAxisDef>" +
            "<AxisParam><Dynamic><Acceleration>2500</Acceleration></Dynamic><Velo><Maximum>3000</Maximum></Velo></AxisParam>" +
            "<Encoder><EncParam><SoftEndMaxControl><Enable>true</Enable><Range>500</Range></SoftEndMaxControl></EncParam></Encoder>" +
            "</NcAxisDef></TreeItem>",
            xml);
    }

    [Fact]
    public void Parameter_names_are_case_insensitive_and_values_use_invariant_culture()
    {
        var xml = AxisXml.BuildParameterUpdate(new Dictionary<string, object?> { ["ScalingFactor"] = 0.000152587890625, ["UNIT"] = "deg" });

        Assert.Equal(
            "<TreeItem><NcAxisDef><AxisParam><Unit>deg</Unit></AxisParam>" +
            "<Encoder><EncParam><ScaleFactorNumerator>0.000152587890625</ScaleFactorNumerator></EncParam></Encoder>" +
            "</NcAxisDef></TreeItem>",
            xml);
    }

    [Fact]
    public void Unknown_parameter_names_are_rejected_with_the_list_of_valid_names()
    {
        var ex = Assert.Throws<TcException>(() => AxisXml.BuildParameterUpdate(new Dictionary<string, object?> { ["maxSpeed"] = 1 }));

        Assert.Contains("maxSpeed", ex.Message);
        Assert.Contains("maxVelocity", ex.Hint);
    }

    [Fact]
    public void Non_numeric_values_for_numeric_parameters_are_rejected()
    {
        var ex = Assert.Throws<TcException>(() => AxisXml.BuildParameterUpdate(new Dictionary<string, object?> { ["jerk"] = "fast" }));

        Assert.Contains("jerk", ex.Message);
    }

    [Fact]
    public void Builds_the_io_link_fragment()
    {
        Assert.Equal(
            "<TreeItem><NcAxisDef><IoItem><PathName>TIID^Device 1 (EtherCAT)^Term 1 (EK1100)^Term 2 (EL7041)</PathName><Channel>1</Channel></IoItem></NcAxisDef></TreeItem>",
            AxisXml.BuildIoLink("TIID^Device 1 (EtherCAT)^Term 1 (EK1100)^Term 2 (EL7041)", 1));
    }

    [Fact]
    public void Io_link_for_SoE_drives_carries_the_axis_io_type_before_the_io_item()
    {
        // AX5000 (SoE) is only linked when AxisIoType 'Sercos' is given; TwinCAT then selects drive type 9.
        Assert.Equal(
            "<TreeItem><NcAxisDef><AxisIoType>Sercos</AxisIoType><IoItem><PathName>TIID^EcAx^Drive 1</PathName><Channel>0</Channel></IoItem></NcAxisDef></TreeItem>",
            AxisXml.BuildIoLink("TIID^EcAx^Drive 1", 0, "Sercos"));
    }

    [Fact]
    public void Io_link_paths_are_xml_escaped()
    {
        Assert.Contains("<PathName>TIID^A &amp; B</PathName>", AxisXml.BuildIoLink("TIID^A & B", 0));
    }
}
