using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TCatSysManagerLib;
using TwinCAT.Ads;
using TwinCatMcp.Ads;
using TwinCatMcp.Core;
using TwinCatMcp.Runtime;

namespace TwinCatMcp.Tools;

/// <summary>
/// Target system, activation, TwinCAT state (config/run), XAE version and target platform (work package 3).
/// These tools change the state of a TwinCAT runtime - only use them on a target you are allowed to restart.
/// </summary>
[McpServerToolType]
public sealed class SystemTools(XaeSession session, AdsSessionManager ads)
{
    private const string AllowLocalDescription =
        "Required (true) when the target is this PC's own TwinCAT system (<Local>), which may run another application. " +
        "Normally set a dedicated target with system_set_target instead.";

    [McpServerTool(Name = "system_get_target", ReadOnly = true), Description(
        "Returns the target system of the selected TwinCAT project: AMS Net ID (ITcSysManager.GetTargetNetId) and its route name if known. " +
        "Use before activating to make sure you deploy to the right runtime.")]
    public Task<CallToolResult> GetTarget()
        => session.RunAsync(s =>
        {
            var netId = XaeRuntime.TargetNetId(s);
            return new { netId, routeName = Routes(s)?.NameOf(netId) };
        });

    [McpServerTool(Name = "system_set_target", Idempotent = true), Description(
        "Sets the target system for activation/login (ITcSysManager.SetTargetNetId). Accepts an AMS Net ID (e.g. '199.4.42.250.1.1' for a local " +
        "Usermode Runtime, '5.12.34.56.1.1' for a remote PLC), a route name as listed by system_list_targets, or 'local'. " +
        "For remote targets an ADS route must exist (route_ tools). Does not change the runtime itself.")]
    public Task<CallToolResult> SetTarget(
        [Description("AMS Net ID, route name from system_list_targets, or 'local'.")] string target)
        => session.RunAsync(s =>
        {
            if (string.IsNullOrWhiteSpace(target))
            {
                throw new TcException("target must not be empty.") { Hint = "Pass an AMS Net ID, a route name or 'local'." };
            }

            var t = target.Trim();
            string netId;
            if (t.Equals("local", StringComparison.OrdinalIgnoreCase) || t.Equals("<local>", StringComparison.OrdinalIgnoreCase))
            {
                netId = LocalNetId();
            }
            else if (AmsNetId.TryParse(t, out var id))
            {
                netId = id.ToString();
            }
            else
            {
                var routes = Routes(s);
                netId = routes?.Resolve(t) ?? throw new TcException($"Unknown target '{t}'.")
                {
                    Hint = "Known routes: " + string.Join(", ", routes?.Routes.Select(r => $"{r.Name} ({r.NetId})") ?? []) + ". Or pass an AMS Net ID.",
                };
            }

            s.SysManager.SetTargetNetId(netId);
            var now = XaeRuntime.TargetNetId(s);
            return new { netId = now, routeName = Routes(s)?.NameOf(now) };
        });

    [McpServerTool(Name = "system_list_targets", ReadOnly = true), Description(
        "Lists the target systems the XAE knows: the current target and the ADS routes of the local router (TIRR: actual, static and project routes, " +
        "incl. local Usermode Runtime instances). Pass a route name or Net ID to system_set_target.")]
    public Task<CallToolResult> ListTargets()
        => session.RunAsync(s => new
        {
            currentNetId = XaeRuntime.TargetNetId(s),
            localNetId = XaeRuntime.Try(LocalNetId),
            routes = Routes(s)?.Routes ?? [],
        });

    [McpServerTool(Name = "system_activate_configuration", Destructive = true), Description(
        "Activates the TwinCAT configuration on the target (ITcSysManager.ActivateConfiguration: writes the boot configuration incl. PLC boot projects) " +
        "and, with restart=true (default), restarts TwinCAT in Run mode (StartRestartTwinCAT) without any dialog, then waits until the target " +
        "reports ADS state Run. Build the solution first and make sure the target platform matches (system_get_info). " +
        "Requires valid licenses on the target (license_list).")]
    public Task<CallToolResult> ActivateConfiguration(
        [Description("Restart TwinCAT in Run mode after activation (the XAE question 'Restart TwinCAT in Run mode?' answered with yes).")] bool restart = true,
        [Description("Seconds to wait for the target to reach Run mode.")] int waitSeconds = 60,
        [Description("Skip the license pre-check for local Usermode Runtimes (license file with TC1200 when a PLC is configured).")] bool skipLicenseCheck = false,
        [Description(AllowLocalDescription)] bool allowLocalSystem = false,
        int timeoutSeconds = 300)
        => XaeRuntime.RunThenAsync(session, s =>
        {
            var netId = XaeRuntime.TargetNetId(s);
            LocalTargetGuard.Ensure(netId, allowLocalSystem, "activate the configuration");
            if (restart && !skipLicenseCheck)
            {
                CheckLocalLicenses(s, netId);
            }

            XaeRuntime.WhenXaeIsReady(() => s.SysManager.ActivateConfiguration());

            // Snapshot after the activation (it already bumps the restart counter) and before the asynchronous restart.
            var tracker = TargetState.BeginRestart(ads, netId);
            if (restart)
            {
                XaeRuntime.WhenXaeIsReady(() => s.SysManager.StartRestartTwinCAT());
            }

            return (netId, tracker);
        }, async t =>
        {
            var (netId, tracker) = t;
            if (!restart)
            {
                return new { activated = true, restarted = false, netId, state = TargetState.Read(ads, netId) };
            }

            var r = await WaitForRun(netId, tracker, waitSeconds);
            return new
            {
                activated = true,
                restarted = true,
                netId,
                runMode = r.Reached,
                waitedSeconds = r.Seconds,
                state = r.State,
                xaeDialog = r.Dialog,
                hint = r.Hint,
            };
        }, timeoutSeconds);

    [McpServerTool(Name = "system_restart", Destructive = true), Description(
        "Starts or restarts TwinCAT on the target in Run mode (ITcSysManager.StartRestartTwinCAT) with the configuration last activated on it, " +
        "then waits for ADS state Run. Does NOT activate the current project - use system_activate_configuration for that.")]
    public Task<CallToolResult> Restart(
        [Description("Seconds to wait for the target to reach Run mode after the restart.")] int waitSeconds = 60,
        [Description(AllowLocalDescription)] bool allowLocalSystem = false,
        int timeoutSeconds = 180)
        => XaeRuntime.RunThenAsync(session, s =>
        {
            var netId = XaeRuntime.TargetNetId(s);
            LocalTargetGuard.Ensure(netId, allowLocalSystem, "restart TwinCAT");
            var tracker = TargetState.BeginRestart(ads, netId);
            XaeRuntime.WhenXaeIsReady(() => s.SysManager.StartRestartTwinCAT());
            return (netId, tracker);
        }, async t =>
        {
            var (netId, tracker) = t;
            var r = await WaitForRun(netId, tracker, waitSeconds);
            return new { netId, reached = r.Reached, waitedSeconds = r.Seconds, state = r.State, xaeDialog = r.Dialog, hint = r.Hint };
        }, timeoutSeconds);

    [McpServerTool(Name = "system_set_config_mode", Destructive = true), Description(
        "Switches the target TwinCAT system to Config mode (stops real-time, PLC and I/O; needed for I/O scans or to free the target) via ADS " +
        "(system service port 10000, state Reconfig = XAE 'Restart TwinCAT (Config Mode)') and waits for ADS state Config. " +
        "Pass netId to address any target without an XAE session.")]
    public Task<CallToolResult> SetConfigMode(
        [Description("Optional AMS Net ID. Empty = target of the current XAE project.")] string? netId = null,
        [Description("Seconds to wait until the target reports Config mode.")] int waitSeconds = 30,
        [Description(AllowLocalDescription)] bool allowLocalSystem = false)
        => WithTarget(netId, async target =>
        {
            LocalTargetGuard.Ensure(target, allowLocalSystem, "switch TwinCAT to Config mode");
            TargetState.WriteControl(ads, target, AdsState.Reconfig);
            return await WaitResult(target, "Config", waitSeconds);
        });

    [McpServerTool(Name = "system_set_run_mode", Destructive = true), Description(
        "Restarts the target TwinCAT system in Run mode with its currently activated configuration (same as system_restart, but also works " +
        "without an XAE session via ADS: system service port 10000, state Reset). Does NOT activate the current project.")]
    public Task<CallToolResult> SetRunMode(
        [Description("Optional AMS Net ID. Empty = target of the current XAE project.")] string? netId = null,
        [Description("Seconds to wait until the target reports Run mode.")] int waitSeconds = 60,
        [Description(AllowLocalDescription)] bool allowLocalSystem = false)
        => WithTarget(netId, async target =>
        {
            LocalTargetGuard.Ensure(target, allowLocalSystem, "restart TwinCAT in Run mode");
            var tracker = TargetState.BeginRestart(ads, target);
            TargetState.WriteControl(ads, target, AdsState.Reset);
            var (state, reached, seconds, _) = await TargetState.WaitForRestartAsync<object>(ads, target, tracker, TimeSpan.FromSeconds(Math.Max(1, waitSeconds)), null);
            return new { netId = target, reached, wanted = "Run", waitedSeconds = seconds, state };
        });

    [McpServerTool(Name = "system_get_state", ReadOnly = true), Description(
        "Returns the state of the target TwinCAT system: ADS state of the system service (Run, Config, Stop, Invalid ...) and, with an XAE session, " +
        "ITcSysManager.IsTwinCATStarted. Pass netId to query any target via ADS only (no XAE session needed).")]
    public async Task<CallToolResult> GetState(
        [Description("Optional AMS Net ID. Empty = target of the current XAE project.")] string? netId = null)
    {
        if (!string.IsNullOrWhiteSpace(netId) && !(session.IsConnected && session.HasProject))
        {
            return await ads.Run(() => new { netId, ads = TargetState.Read(ads, netId) });
        }

        return await XaeRuntime.RunThenAsync(session, s =>
        {
            var current = XaeRuntime.TargetNetId(s);
            var target = string.IsNullOrWhiteSpace(netId) ? current : netId.Trim();
            bool? started = string.Equals(target, current, StringComparison.OrdinalIgnoreCase)
                ? XaeRuntime.Try<bool?>(() => s.SysManager.IsTwinCATStarted())
                : null;
            return (target, started);
        }, r => Task.FromResult<object?>(new { netId = r.target, ads = TargetState.Read(ads, r.target), twinCatStarted = r.started }));
    }

    [McpServerTool(Name = "system_get_info", ReadOnly = true), Description(
        "Reads information about a TwinCAT target via ADS (no XAE needed): system state, device name, TwinCAT version, platform " +
        "(e.g. 'TwinCAT RT (x64)' or 'TwinCAT OS (x64)') and whether it is a Usermode Runtime. Use the platform with system_set_target_platform.")]
    public Task<CallToolResult> GetInfo(
        [Description("AMS Net ID. Empty = target of the current XAE project (or the local system without XAE).")] string? netId = null)
        => WithTarget(netId, target => Task.FromResult<object?>(TargetState.ReadInfo(ads, target)), fallbackToLocal: true);

    [McpServerTool(Name = "system_get_versions", ReadOnly = true), Description(
        "Returns the TwinCAT XAE version in use and all installed Remote Manager versions (ITcRemoteManager), and whether the project " +
        "is pinned to a TwinCAT version (TcVersionFixed / PinnedTcVersion).")]
    public Task<CallToolResult> GetVersions()
        => session.RunAsync(s =>
        {
            var rm = XaeRuntime.Try(() => XaeRuntime.RemoteManager(s));
            return new
            {
                xaeVersion = rm is null ? null : XaeRuntime.Try(() => rm.Version),
                installedVersions = rm is null ? null : XaeRuntime.Try(() => rm.Versions),
                projectPinned = s.HasProject ? XaeRuntime.Try<bool?>(() => s.SysManager.TcVersionFixed) : null,
                pinnedVersion = s.HasProject ? PinnedVersion(s) : null,
            };
        });

    [McpServerTool(Name = "system_pin_version", Idempotent = true), Description(
        "Pins (or unpins) the TwinCAT version of the project ('Pin Version' on the project's General tab). When pinned, the project always " +
        "opens with the XAE version it was saved with. Save the solution afterwards.")]
    public Task<CallToolResult> PinVersion(
        [Description("true = pin to the current TwinCAT version, false = unpin.")] bool pin = true)
        => session.RunAsync(s =>
        {
            s.SysManager.TcVersionFixed = pin;
            return new
            {
                projectPinned = s.SysManager.TcVersionFixed,
                pinnedVersion = PinnedVersion(s),
            };
        });

    [McpServerTool(Name = "system_list_target_platforms", ReadOnly = true), Description(
        "Lists the build target platforms of the project (e.g. 'TwinCAT RT (x64)', 'TwinCAT OS (x64)', 'TwinCAT RT (x86)') and the active one. " +
        "A Usermode Runtime / TwinCAT/BSD target needs 'TwinCAT OS (x64)'.")]
    public Task<CallToolResult> ListTargetPlatforms()
        => session.RunAsync(s =>
        {
            var cm = (ITcConfigManager)s.SysManager.ConfigurationManager;
            return new { active = cm.ActiveTargetPlatform, platforms = cm.TargetPlatforms };
        });

    [McpServerTool(Name = "system_set_target_platform", Idempotent = true), Description(
        "Sets the active target platform for compilation (ITcConfigManager.ActiveTargetPlatform), e.g. 'TwinCAT OS (x64)' for a Usermode Runtime " +
        "or TwinCAT/BSD, 'TwinCAT RT (x64)' for Windows real-time. Rebuild afterwards.")]
    public Task<CallToolResult> SetTargetPlatform(
        [Description("Platform name as listed by system_list_target_platforms.")] string platform)
        => session.RunAsync(s =>
        {
            var cm = (ITcConfigManager)s.SysManager.ConfigurationManager;
            var match = cm.TargetPlatforms.FirstOrDefault(p => string.Equals(p, platform?.Trim(), StringComparison.OrdinalIgnoreCase))
                        ?? throw new TcException($"Unknown target platform '{platform}'.") { Hint = "Available: " + string.Join(", ", cm.TargetPlatforms) };
            cm.ActiveTargetPlatform = match;
            return new { active = cm.ActiveTargetPlatform };
        });

    // ------------------------------------------------------------------ helpers

    private sealed record RunWait(TargetState.SystemState State, bool Reached, double Seconds, XaeWindows.Dialog? Dialog, string? Hint);

    /// <summary>Waits for Run mode; stops early (and reports it) when the XAE shows a modal message box, e.g. "license not found".</summary>
    private async Task<RunWait> WaitForRun(string netId, RestartTracker tracker, int waitSeconds)
    {
        var pid = session.ProcessId;
        var (state, reached, seconds, dialog) = await TargetState.WaitForRestartAsync(ads, netId, tracker, TimeSpan.FromSeconds(Math.Max(1, waitSeconds)),
            () => XaeWindows.ModalDialogs(pid).FirstOrDefault());
        string? hint = null;
        if (dialog is not null)
        {
            hint = "The XAE shows a modal message box and rejects all calls until it is closed by a human (click OK in the window '" + dialog.Title + "'). " +
                   (dialog.Text.Contains("license", StringComparison.OrdinalIgnoreCase)
                       ? "The target has no valid license: generate a 7-day trial (license_activate_trial, docs/runtime-setup.md), then activate again."
                       : "Read the message, fix the cause and activate again.");
        }
        else if (!reached)
        {
            hint = "Target did not reach Run mode. Check licenses (license_list), the target platform (system_get_info) and system_get_state.";
        }

        return new RunWait(state, reached, seconds, dialog, hint);
    }

    /// <summary>Fails fast when the target is a local Usermode Runtime without a usable license file (avoids the blocking XAE error box).</summary>
    private static void CheckLocalLicenses(XaeSession s, string netId)
    {
        var local = XaeRuntime.Try(() => UmrtManager.List().FirstOrDefault(i => string.Equals(i.NetId, netId, StringComparison.OrdinalIgnoreCase)));
        if (local is null)
        {
            return;
        }

        var needsPlc = XaeRuntime.NestedProjects(s).Any();
        if (LicenseFile.MissingLicense(local.LicenseFiles, needsPlc) is { } missing)
        {
            throw new TcException($"The target is the local Usermode Runtime '{local.Name}' ({netId}) and it has {missing}. " +
                                  "Restarting it in Run mode would fail with 'license not found' and block the XAE with an error box.")
            {
                Hint = "Generate a 7-day trial license for this target (license_activate_trial; steps in docs/runtime-setup.md), " +
                       "or call with restart=false (activation only) or skipLicenseCheck=true.",
            };
        }
    }

    private async Task<object?> WaitResult(string netId, string wanted, int waitSeconds)
    {
        var (state, reached, seconds) = await TargetState.WaitForAsync(ads, netId, wanted, TimeSpan.FromSeconds(Math.Max(1, waitSeconds)));
        return new { netId, reached, wanted, waitedSeconds = seconds, state };
    }

    /// <summary>Resolves the target (explicit Net ID, else the XAE project's target, else optionally the local system) and runs an ADS action.</summary>
    private async Task<CallToolResult> WithTarget(string? netId, Func<string, Task<object?>> action, bool fallbackToLocal = false)
    {
        var target = string.IsNullOrWhiteSpace(netId) ? null : netId.Trim();
        if (target is null && session.IsConnected && session.HasProject)
        {
            try
            {
                target = await session.Sta.InvokeAsync(() => XaeRuntime.TargetNetId(session), TimeSpan.FromSeconds(60));
            }
            catch (Exception ex)
            {
                return ToolResult.Error(ex);
            }
        }

        if (target is null && fallbackToLocal)
        {
            target = LocalNetId();
        }

        if (target is null)
        {
            return ToolResult.Error("No target given and no XAE project selected.", "Pass netId, or open a project (solution_open) and set its target (system_set_target).");
        }

        return await ads.RunAsync(() => action(target));
    }

    /// <summary>Pinned TwinCAT version (ITcSysManager14); null when not pinned or not available on this XAE.</summary>
    private static string? PinnedVersion(XaeSession s)
        => XaeRuntime.Try(() => s.SysManagerAs<ITcSysManager14>("Reading the pinned TwinCAT version").PinnedTcVersion) is { Length: > 0 } v ? v : null;

    private static RouteList? Routes(XaeSession s) => XaeRuntime.Try(() => RouteXml.Parse(s.Lookup("TIRR").ProduceXml(false)));

    /// <summary>AMS Net ID of the local router.</summary>
    private static string LocalNetId()
    {
        using var c = new AdsClient();
        c.Connect(AmsNetId.Local, TargetState.SystemServicePort);
        return c.Address!.NetId.ToString();
    }
}
