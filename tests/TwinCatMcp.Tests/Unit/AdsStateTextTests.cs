using TwinCAT.Ads;
using TwinCatMcp.Ads;
using TwinCatMcp.Core;

namespace TwinCatMcp.Tests.Unit;

/// <summary>ADS states per TcAdsDef.h (ADSSTATE_*): INVALID=0 ... RUN=5, STOP=6 ... CONFIG=15, RECONFIG=16.</summary>
public sealed class AdsStateTextTests
{
    [Theory]
    [InlineData(5, "Run")]
    [InlineData(6, "Stop")]
    [InlineData(15, "Config")]
    [InlineData(16, "Reconfig")]
    [InlineData(11, "Error")]
    public void Names_states(int value, string name)
    {
        Assert.Equal(name, AdsStateText.Name((AdsState)value));
        Assert.False(string.IsNullOrEmpty(AdsStateText.Describe((AdsState)value)));
    }

    [Theory]
    [InlineData("run", false, AdsState.Run)]
    [InlineData("STOP", false, AdsState.Stop)]
    [InlineData("reset", false, AdsState.Reset)]
    [InlineData("5", false, AdsState.Run)]
    [InlineData("config", true, AdsState.Reconfig)]
    [InlineData("restart", true, AdsState.Reset)]
    [InlineData("run", true, AdsState.Reset)]
    public void Parses_requested_state(string text, bool systemService, AdsState expected)
    {
        Assert.Equal(expected, AdsStateText.ParseCommand(text, systemService));
    }

    [Fact]
    public void Unknown_state_lists_valid_values()
    {
        var ex = Assert.Throws<TcException>(() => AdsStateText.ParseCommand("fly", false));
        Assert.Contains("run", ex.Hint);
    }
}
