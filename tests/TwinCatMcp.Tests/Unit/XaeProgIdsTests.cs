using TwinCatMcp.Core;

namespace TwinCatMcp.Tests.Unit;

/// <summary>xae_start without a ProgID picks the TcXaeShell that is installed (4026: DTE.17.0, 4024: DTE.15.0).</summary>
public sealed class XaeProgIdsTests
{
    [Fact]
    public void The_64_bit_shell_of_4026_wins_when_both_shells_are_registered()
    {
        Assert.Equal("TcXaeShell.DTE.17.0", XaeProgIds.SelectDefault(_ => true));
    }

    [Fact]
    public void The_VS_2017_based_shell_of_4024_is_used_when_it_is_the_only_one()
    {
        Assert.Equal("TcXaeShell.DTE.15.0", XaeProgIds.SelectDefault(id => id == "TcXaeShell.DTE.15.0"));
    }

    [Fact]
    public void Without_any_registered_shell_the_4026_ProgID_is_returned_so_the_start_reports_it_as_missing()
    {
        Assert.Equal("TcXaeShell.DTE.17.0", XaeProgIds.SelectDefault(_ => false));
    }
}
