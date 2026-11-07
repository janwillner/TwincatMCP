using TwinCatMcp.Core;
using TwinCatMcp.Runtime;

namespace TwinCatMcp.Tests.Unit;

public sealed class RuntimePlcOnlineTests
{
    // Values of enum PLC_LOGIN_FLAGS in TCatSysManagerLib 3.4: NONE=0, REGULAR=1, FORCEONLINECHANGE=2, FORCEDOWNLOAD=4,
    // FORCECOMPILECODE=16, FORCEGENERATECODE=32, SILENT=256.
    [Theory]
    [InlineData("auto", false, 1 | 256)]
    [InlineData("download", false, 4 | 256)]
    [InlineData("onlinechange", false, 2 | 256)]
    [InlineData("onlineChange", true, 2 | 16 | 256)]
    [InlineData("Download", true, 4 | 16 | 256)]
    public void Maps_login_mode_to_PLC_LOGIN_FLAGS(string mode, bool rebuild, int expected)
        => Assert.Equal(expected, PlcOnline.LoginFlags(mode, rebuild));

    [Fact]
    public void Rejects_unknown_login_modes()
        => Assert.Throws<TcException>(() => PlcOnline.LoginFlags("upload", false));

    [Theory]
    [InlineData("cold", 1)]
    [InlineData("origin", 2)]
    [InlineData("Cold", 1)]
    public void Maps_reset_type_to_PLC_RESET_TYPE(string type, int expected)
        => Assert.Equal(expected, PlcOnline.ResetType(type));

    [Fact]
    public void Rejects_unknown_reset_types()
        => Assert.Throws<TcException>(() => PlcOnline.ResetType("warm"));

    [Fact]
    public void Decodes_operation_state_flags()
        // PLC_OPSTATE_PROGRAMLOADED=1, PLC_OPSTATE_BOOTPROJECTVALID=4096
        => Assert.Equal(["ProgramLoaded", "BootProjectValid"], PlcOnline.DescribeOpState(1 | 4096));

    [Fact]
    public void Parses_online_settings_from_nested_project_xml()
    {
        const string xml = "<TreeItem><IECProjectDef><OnlineSettings><LoggedIn>true</LoggedIn><PlcAppState>Run</PlcAppState><PlcOpState>None</PlcOpState>" +
                           "<OnlineAppInfo /><Commands><LoginCmd>false</LoginCmd></Commands></OnlineSettings></IECProjectDef></TreeItem>";

        var s = PlcOnline.ParseOnlineSettings(xml);

        Assert.True(s.LoggedIn);
        Assert.Equal("Run", s.AppState);
        Assert.Equal("None", s.OpState);
    }

    [Fact]
    public void Builds_online_command_xml()
        => Assert.Equal(
            "<TreeItem><IECProjectDef><OnlineSettings><Commands><ResetOriginCmd>true</ResetOriginCmd></Commands></OnlineSettings></IECProjectDef></TreeItem>",
            PlcOnline.CommandXml("ResetOriginCmd"));
}
