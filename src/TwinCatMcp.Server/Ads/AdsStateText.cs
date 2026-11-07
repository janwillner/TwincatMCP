using System.Globalization;
using TwinCAT.Ads;
using TwinCatMcp.Core;

namespace TwinCatMcp.Ads;

/// <summary>Human readable ADS states (ADSSTATE_* from TcAdsDef.h) and parsing of requested state changes.</summary>
public static class AdsStateText
{
    public static string Name(AdsState state) => Enum.IsDefined(state) ? state.ToString() : $"Unknown({(int)state})";

    public static string Describe(AdsState state) => state switch
    {
        AdsState.Invalid => "invalid / not available",
        AdsState.Idle => "idle",
        AdsState.Reset => "reset (restarting)",
        AdsState.Init => "initializing",
        AdsState.Start => "starting",
        AdsState.Run => "running",
        AdsState.Stop => "stopped",
        AdsState.SaveConfig => "saving configuration",
        AdsState.LoadConfig => "loading configuration",
        AdsState.PowerFailure => "power failure",
        AdsState.PowerGood => "power good",
        AdsState.Error => "error",
        AdsState.Shutdown => "shutting down",
        AdsState.Suspend => "suspended",
        AdsState.Resume => "resuming",
        AdsState.Config => "config mode (TwinCAT system not running real-time)",
        AdsState.Reconfig => "switching to config mode",
        AdsState.Stopping => "stopping",
        AdsState.Incompatible => "incompatible",
        AdsState.Exception => "exception (PLC halted on an exception)",
        _ => "unknown",
    };

    /// <summary>
    /// Parses a requested state. For the system service (port 10000) "run"/"restart" mean restart TwinCAT in run mode
    /// (ADSSTATE_RESET) and "config" means switch to config mode (ADSSTATE_RECONFIG).
    /// For other ports (PLC, NC ...) the state is written as is.
    /// </summary>
    public static AdsState ParseCommand(string text, bool systemService)
    {
        var t = text.Trim();
        if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && Enum.IsDefined((AdsState)n))
        {
            return (AdsState)n;
        }

        var lower = t.ToLowerInvariant();
        if (systemService)
        {
            switch (lower)
            {
                case "run" or "restart" or "reset":
                    return AdsState.Reset;
                case "config" or "reconfig":
                    return AdsState.Reconfig;
            }
        }
        else
        {
            switch (lower)
            {
                case "run" or "start":
                    return AdsState.Run;
                case "stop":
                    return AdsState.Stop;
                case "reset":
                    return AdsState.Reset;
                case "config":
                    return AdsState.Config;
                case "reconfig":
                    return AdsState.Reconfig;
            }
        }

        if (Enum.TryParse<AdsState>(t, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw new TcException($"Unknown ADS state '{text}'.")
        {
            Hint = systemService
                ? "System service (port 10000): run|restart (restart TwinCAT in run mode) or config (switch to config mode)."
                : "Use run, stop, reset (PLC: reset = reset cold), config, reconfig or a numeric ADS state (5 = Run, 6 = Stop).",
        };
    }
}
