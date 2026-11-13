using System.Reflection;
using ModelContextProtocol.Server;
using TwinCatMcp.Core;

namespace TwinCatMcp.Tests.Unit;

public sealed class OnlineGuardTests
{
    [Theory]
    [InlineData("build_solution")]
    [InlineData("build_project")]
    [InlineData("plc_compile")]
    [InlineData("plc_check_all_objects")]
    [InlineData("pou_write_code")]
    [InlineData("pou_replace_code")]
    [InlineData("pou_create")]
    [InlineData("tree_consume_xml")]
    [InlineData("system_activate_configuration")]
    [InlineData("solution_save")]
    [InlineData("xae_execute_command")]
    public void Changing_and_building_tools_log_out_first(string tool)
        => Assert.True(OnlineGuard.RequiresLogout(tool, readOnly: false));

    [Theory]
    [InlineData("plc_online_login")]
    [InlineData("plc_online_logout")]
    [InlineData("plc_online_start")]
    [InlineData("plc_online_stop")]
    [InlineData("plc_online_reset")]
    [InlineData("ads_write_symbols")]
    [InlineData("ads_write_control")]
    [InlineData("route_add")]
    [InlineData("xae_start")]
    [InlineData("xae_attach")]
    [InlineData("xae_set_visible")]
    [InlineData("xae_set_silent_mode")]
    public void Online_ADS_and_IDE_session_tools_stay_logged_in(string tool)
        => Assert.False(OnlineGuard.RequiresLogout(tool, readOnly: false));

    [Fact]
    public void Read_only_tools_stay_logged_in()
    {
        var readOnly = typeof(XaeSession).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>())
            .Where(a => a is { Name: not null, ReadOnly: true })
            .Select(a => a!.Name!)
            .ToList();

        Assert.Contains("pou_read_code", readOnly);
        Assert.Contains("plc_online_state", readOnly);
        Assert.All(readOnly, name => Assert.False(OnlineGuard.RequiresLogout(name, readOnly: true)));
    }
}
