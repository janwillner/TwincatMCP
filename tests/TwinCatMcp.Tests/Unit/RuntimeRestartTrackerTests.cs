using TwinCatMcp.Runtime;

namespace TwinCatMcp.Tests.Unit;

/// <summary>
/// A restart of a target that is already in Run mode must not be reported as finished by the first "Run" reading -
/// that is still the state before the restart. Verified on 4026 (UmRT): activation + restart goes Run -> Stop (~1 s) -> Run,
/// while the system service restart counter already changes during the activation (so it is no completion signal).
/// </summary>
public sealed class RuntimeRestartTrackerTests
{
    [Fact]
    public void Run_before_the_restart_is_not_completion()
    {
        var t = new RestartTracker(stateBefore: "Run");

        Assert.False(t.Observe("Run"));
        Assert.False(t.Observe("Run"));
    }

    [Fact]
    public void Leaving_run_and_coming_back_completes()
    {
        var t = new RestartTracker(stateBefore: "Run");

        Assert.False(t.Observe("Run"));
        Assert.False(t.Observe("Stop"));
        Assert.False(t.Observe(null));       // system service briefly unreachable
        Assert.True(t.Observe("Run"));
    }

    [Fact]
    public void Starting_from_config_completes_on_the_first_run()
    {
        var t = new RestartTracker(stateBefore: "Config");

        Assert.False(t.Observe("Config"));
        Assert.True(t.Observe("Run"));
    }

    [Fact]
    public void Unknown_state_before_counts_as_not_running()
    {
        var t = new RestartTracker(stateBefore: null);

        Assert.True(t.Observe("Run"));
    }
}
