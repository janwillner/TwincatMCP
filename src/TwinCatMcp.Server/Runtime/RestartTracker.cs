namespace TwinCatMcp.Runtime;

/// <summary>
/// Decides when a TwinCAT restart into Run mode has finished. A target that was in Run before the restart still reports
/// Run for a moment after the restart was requested (StartRestartTwinCAT / ADS Reset return before the restart begins),
/// so the target must first leave Run (Stop/Config/unreachable) and then come back.
/// Verified on 4026: Run -> Stop (about 1 s on a Usermode Runtime) -> Run. The system service restart counter is no usable
/// signal - it already changes during ActivateConfiguration, before the restart.
/// </summary>
public sealed class RestartTracker(string? stateBefore)
{
    private bool _leftRun = !IsRun(stateBefore);

    /// <summary>Feeds one reading of the system state (null = system service not reachable); true once the restart is complete.</summary>
    public bool Observe(string? adsState)
    {
        if (!IsRun(adsState))
        {
            _leftRun = true;
            return false;
        }

        return _leftRun;
    }

    private static bool IsRun(string? state) => string.Equals(state, "Run", StringComparison.OrdinalIgnoreCase);
}
