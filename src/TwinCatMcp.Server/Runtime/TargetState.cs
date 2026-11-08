using TwinCAT.Ads;
using TwinCatMcp.Ads;

namespace TwinCatMcp.Runtime;

/// <summary>ADS access to the TwinCAT system service of a target (port 10000).</summary>
public static class TargetState
{
    public const int SystemServicePort = 10000;

    public sealed record SystemState(string NetId, bool Reachable, string? AdsState, int? DeviceState, string? Error);

    /// <summary>Reads the TwinCAT system state (Run / Config / Stop ...) via ADS port 10000.</summary>
    public static SystemState Read(AdsSessionManager ads, string netId, int timeoutMs = 2000)
    {
        try
        {
            var st = ads.Get(netId, SystemServicePort, timeoutMs).ReadState();
            return new SystemState(netId, true, st.AdsState.ToString(), st.DeviceState, null);
        }
        catch (Exception ex)
        {
            ads.Disconnect(netId, SystemServicePort);
            return new SystemState(netId, false, null, null, ex.Message);
        }
    }

    /// <summary>Polls the system state until it equals <paramref name="wanted"/> (e.g. "Run", "Config") or the timeout expires.</summary>
    public static async Task<(SystemState State, bool Reached, double Seconds)> WaitForAsync(AdsSessionManager ads, string netId, string wanted, TimeSpan timeout)
    {
        var (state, reached, seconds, _) = await WaitForAsync<object>(ads, netId, wanted, timeout, null).ConfigureAwait(false);
        return (state, reached, seconds);
    }

    /// <summary>Like <see cref="WaitForAsync(AdsSessionManager,string,string,TimeSpan)"/>; stops early when <paramref name="abort"/> returns a value.</summary>
    public static async Task<(SystemState State, bool Reached, double Seconds, T? Abort)> WaitForAsync<T>(
        AdsSessionManager ads, string netId, string wanted, TimeSpan timeout, Func<T?>? abort) where T : class
    {
        var start = DateTime.UtcNow;
        var state = Read(ads, netId);
        T? aborted = null;
        while (!string.Equals(state.AdsState, wanted, StringComparison.OrdinalIgnoreCase) && DateTime.UtcNow - start < timeout)
        {
            await Task.Delay(500).ConfigureAwait(false);
            aborted = abort?.Invoke();
            if (aborted is not null)
            {
                break;
            }

            state = Read(ads, netId);
        }

        return (state, string.Equals(state.AdsState, wanted, StringComparison.OrdinalIgnoreCase), Math.Round((DateTime.UtcNow - start).TotalSeconds, 1), aborted);
    }

    /// <summary>Snapshot of the system state taken right before a restart is requested (see <see cref="WaitForRestartAsync{T}"/>).</summary>
    public static RestartTracker BeginRestart(AdsSessionManager ads, string netId) => new(Read(ads, netId).AdsState);

    /// <summary>Waits until the restart observed by <paramref name="tracker"/> has completed in Run mode.</summary>
    public static async Task<(SystemState State, bool Reached, double Seconds, T? Abort)> WaitForRestartAsync<T>(
        AdsSessionManager ads, string netId, RestartTracker tracker, TimeSpan timeout, Func<T?>? abort) where T : class
    {
        var start = DateTime.UtcNow;
        while (true)
        {
            var state = Read(ads, netId, 1000);
            if (tracker.Observe(state.Reachable ? state.AdsState : null))
            {
                return (state, true, Math.Round((DateTime.UtcNow - start).TotalSeconds, 1), null);
            }

            if (DateTime.UtcNow - start >= timeout)
            {
                return (state, false, Math.Round((DateTime.UtcNow - start).TotalSeconds, 1), null);
            }

            await Task.Delay(200).ConfigureAwait(false); // the Stop phase of a Usermode Runtime restart lasts only ~1 s
            if (abort?.Invoke() is { } aborted)
            {
                return (Read(ads, netId), false, Math.Round((DateTime.UtcNow - start).TotalSeconds, 1), aborted);
            }
        }
    }

    /// <summary>Sets the system state via ADS WriteControl on port 10000 (AdsState.Reconfig = config mode, AdsState.Reset = restart in run mode).</summary>
    public static void WriteControl(AdsSessionManager ads, string netId, AdsState state)
        => ads.Get(netId, SystemServicePort, 5000).WriteControl(new StateInfo(state, 0));

    public sealed record TargetInfo(
        string NetId,
        SystemState State,
        string? DeviceName,
        string? TwinCatVersion,
        string? Platform,
        bool? UsermodeRuntime,
        string? LocalUsermodeRuntime,
        string? Error,
        string? Note);

    /// <summary>Device info of the system service + platform/runtime type (TC170x "Runtime type (ADS)": port 200, IG 0x700, IO 2/4).</summary>
    public static TargetInfo ReadInfo(AdsSessionManager ads, string netId)
    {
        var state = Read(ads, netId);
        if (!state.Reachable)
        {
            return new TargetInfo(netId, state, null, null, null, null, LocalInstance(ads, netId), state.Error, null);
        }

        string? name = null, version = null, platform = null, error = null, note = null;
        bool? umrt = null;
        try
        {
            var di = ads.Get(netId, SystemServicePort).ReadDeviceInfo();
            name = di.Name;
            version = $"{di.Version.Version}.{di.Version.Revision}.{di.Version.Build}";
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        // Same source as ads_get_system_info: system service target info (index group 700 decimal).
        try
        {
            var sys = new SystemServiceClient(ads.Get(netId, SystemServicePort));
            platform = sys.TryReadString(SystemServiceProtocol.TargetInfo, SystemServiceProtocol.TargetInfoPlatform);
            umrt = sys.TryRead(SystemServiceProtocol.TargetInfo, SystemServiceProtocol.TargetInfoIsUmRuntime, 4) is { Length: > 0 } um ? um[0] != 0 : null;
            if (string.IsNullOrEmpty(platform))
            {
                note = "Platform not provided by this target's system service (older TwinCAT version?).";
            }
        }
        catch (Exception ex)
        {
            note = $"Platform/runtime type not readable: {ex.Message}";
        }

        var local = LocalInstance(ads, netId);
        if (platform is null && local is not null)
        {
            umrt ??= true;
            note += " The target is the local Usermode Runtime '" + local + "': use target platform 'TwinCAT OS (x64)'.";
        }

        return new TargetInfo(netId, state, name, version, string.IsNullOrEmpty(platform) ? null : platform, umrt, local, error, note?.Trim());
    }

    private static string? LocalInstance(AdsSessionManager ads, string netId)
    {
        try
        {
            // "local" / "" stand for this PC's router Net ID.
            var resolved = ads.Get(netId, SystemServicePort).Address?.NetId.ToString() ?? netId;
            return UmrtManager.List().FirstOrDefault(i => string.Equals(i.NetId, resolved, StringComparison.OrdinalIgnoreCase))?.Name;
        }
        catch
        {
            return null;
        }
    }
}
