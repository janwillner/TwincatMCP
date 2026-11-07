using TwinCatMcp.Core;
using TwinCatMcp.Motion;

namespace TwinCatMcp.Tests.Unit;

/// <summary>
/// The 4026 "Simulation" checkbox of an NC axis is only persisted as attribute SimulationMode="true" of the
/// &lt;Axis&gt; element in the exported .xti (ProduceXml/ConsumeXml ignore it). Snippet taken from a real export.
/// </summary>
public sealed class MotionAxisXtiTests
{
    private const string Xti =
        "<?xml version=\"1.0\"?>\r\n" +
        "<TcSmItem xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" TcSmVersion=\"1.0\" TcVersion=\"3.1.4026.22\" ClassName=\"CNcAxisDef\" SubType=\"1\">\r\n" +
        "\t<DataTypes>\r\n\t\t<DataType><Name>NCAXLESTRUCT_TOPLC</Name></DataType>\r\n\t</DataTypes>\r\n" +
        "\t<Axis Id=\"4\" CreateSymbols=\"true\" AxisType=\"1\">\r\n" +
        "\t\t<Name>__FILENAME__</Name>\r\n\t\t<AxisPara>\r\n\t\t\t<OtherSettings AllowMotionCmdToSlave=\"true\"/>\r\n\t\t</AxisPara>\r\n" +
        "\t</Axis>\r\n</TcSmItem>\r\n";

    [Fact]
    public void A_new_axis_is_not_in_simulation_mode()
    {
        Assert.False(AxisXti.GetSimulationMode(Xti));
    }

    [Fact]
    public void Enabling_adds_the_attribute_to_the_Axis_element_only()
    {
        var patched = AxisXti.SetSimulationMode(Xti, true);

        Assert.Contains("<Axis Id=\"4\" CreateSymbols=\"true\" AxisType=\"1\" SimulationMode=\"true\">", patched);
        Assert.True(AxisXti.GetSimulationMode(patched));
        Assert.Contains("<AxisPara>", patched);
        Assert.Equal(Xti.Length + " SimulationMode=\"true\"".Length, patched.Length);
    }

    [Fact]
    public void Enabling_twice_or_disabling_is_idempotent()
    {
        var on = AxisXti.SetSimulationMode(AxisXti.SetSimulationMode(Xti, true), true);
        var off = AxisXti.SetSimulationMode(on, false);

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(on, "SimulationMode="));
        Assert.Equal(Xti, off);
        Assert.False(AxisXti.GetSimulationMode(off));
    }

    [Fact]
    public void Throws_when_the_file_contains_no_axis()
    {
        Assert.Throws<TcException>(() => AxisXti.SetSimulationMode("<TcSmItem/>", true));
    }
}
