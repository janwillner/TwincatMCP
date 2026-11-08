using TwinCatMcp.Runtime;

namespace TwinCatMcp.Tests.Unit;

/// <summary>
/// TcSystemServiceUm.exe determines its AMS Net ID on the first start from "-i" (TC170x manual, command line parameters);
/// Start.bat passes "-i path", which ignores an AmsNetId preset in TcRegistry.xml.
/// </summary>
public sealed class RuntimeUmrtStartTests
{
    [Fact]
    public void Configured_net_id_is_passed_on_the_command_line()
    {
        var args = UmrtManager.StartArguments("UmRT_X", "199.4.99.1.1.1");

        Assert.Equal(["-t", "bin", "-i", "199.4.99.1.1.1", "-n", "UmRT_X", "-c", @".\3.1"], args);
    }

    [Fact]
    public void Without_a_configured_net_id_the_runtime_chooses_one_like_Start_bat()
    {
        var args = UmrtManager.StartArguments("UmRT_X", null);

        Assert.Equal(["-t", "bin", "-i", "path", "-n", "UmRT_X", "-c", @".\3.1"], args);
    }
}
