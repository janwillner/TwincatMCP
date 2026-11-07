using System.Text;
using TwinCAT.Ads;
using TwinCatMcp.Ads;

namespace TwinCatMcp.Runtime;

/// <summary>ADS access to the TwinCAT system service of a target (port 10000) and the real-time target info (port 200).</summary>
public static class TargetState
{
    public const int SystemServicePort = 10000;
    public const int RealTimePort = 200;
    private const uint SystemServiceTargetInfo = 0x700;
    private const uint TargetInfoTargetType = 2;
    private const uint TargetInfoPlatform = 4;

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

    /// <summary>Sets the system state via ADS WriteControl on port 10000 (AdsState.Reconfig = config mode, AdsState.Reset = restart in run mode).</summary>
    public static void WriteControl(AdsSessionManager ads, string netId, AdsState state)
        => ads.Get(netId, SystemServicePort, 5000).WriteControl(new StateInfo(state, 0));

    public sealed record TargetInfo(
        string NetId,
        SystemState State,
        string? DeviceName,
        string? TwinCatVersion,
        string? Platform,
        bool? IsUsermodeRuntime,
        string? LocalUsermodeRuntime,
        string? Error,
        string? Note);

    /// <summary>Device info of the system service + platform/runtime type (TC170x "Runtime type (ADS)": port 200, IG 0x700, IO 2/4).</summary>
    public static TargetInfo ReadInfo(AdsSessionManager ads, string netId)
    {
        var state = Read(ads, netId);
        if (!state.Reachable)
        {
            return new TargetInfo(netId, state, null, null, null, null, LocalInstance(netId), state.Error, null);
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

        foreach (var port in new[] { SystemServicePort, RealTimePort })
        {
            try
            {
                using var rt = new AdsClient { Timeout = 2000 };
                rt.Connect(AdsSessionManager.ParseNetId(netId), port);
                var platformResult = rt.ReadAsResult(SystemServiceTargetInfo, TargetInfoPlatform, 256);
                platformResult.ThrowOnError();
                platform = Encoding.ASCII.GetString(platformResult.Data.Span).TrimEnd('\0');
                var typeResult = rt.ReadAsResult(SystemServiceTargetInfo, TargetInfoTargetType, 4);
                typeResult.ThrowOnError();
                umrt = BitConverter.ToUInt32(typeResult.Data.Span) == 1;
                error = null;
                break;
            }
            catch (Exception ex)
            {
                note = $"Platform/runtime type not provided by this runtime (ADS port {port}, IG 0x700: {ex.Message}).";
            }
        }

        var local = LocalInstance(netId);
        if (platform is null && local is not null)
        {
            umrt ??= true;
            note += " The target is the local Usermode Runtime '" + local + "': use target platform 'TwinCAT OS (x64)'.";
        }

        return new TargetInfo(netId, state, name, version, string.IsNullOrEmpty(platform) ? null : platform, umrt, local, error, note?.Trim());
    }

    private static string? LocalInstance(string netId)
    {
        try
        {
            return UmrtManager.List().FirstOrDefault(i => string.Equals(i.NetId, netId, StringComparison.OrdinalIgnoreCase))?.Name;
        }
        catch
        {
            return null;
        }
    }
}
