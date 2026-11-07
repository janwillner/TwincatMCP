using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TwinCatMcp.Ads;
using TwinCatMcp.Core;
using TwinCatMcp.Runtime;

namespace TwinCatMcp.Tools;

/// <summary>
/// TC170x Usermode Runtime instances on this PC (no XAE needed) - work package 3. A Usermode Runtime is a TwinCAT runtime that
/// runs as a normal Windows process (no real-time, works with Hyper-V/VBS) and is the target for tests on development PCs.
/// </summary>
[McpServerToolType]
public sealed class RtUmrtTools(AdsSessionManager ads)
{
    [McpServerTool(Name = "rt_umrt_list", ReadOnly = true), Description(
        "Lists the Usermode Runtime instances of this PC (folders in C:\\ProgramData\\Beckhoff\\TwinCAT\\3.1\\Runtimes) with AMS Net ID, " +
        "running state (process / Windows service), ADS system state and license files (e.g. trial license with expiry).")]
    public Task<CallToolResult> List()
        => ads.Run(() => UmrtManager.List().Select(i => new
        {
            instance = i,
            ads = i.Running && i.NetId is not null ? TargetState.Read(ads, i.NetId, 1500) : null,
        }).ToArray());

    [McpServerTool(Name = "rt_umrt_create"), Description(
        "Creates a new Usermode Runtime instance by copying the TC1700 template (UmRT_Template) to " +
        "C:\\ProgramData\\Beckhoff\\TwinCAT\\3.1\\Runtimes\\<name>. Optionally pre-sets its AMS Net ID; otherwise the runtime picks one on " +
        "first start (default 199.4.42.250.1.1, second byte incremented on collision). Start it with rt_umrt_start.")]
    public Task<CallToolResult> Create(
        [Description("Instance (folder) name, e.g. 'UmRT_McpTest'.")] string name,
        [Description("Optional AMS Net ID (must be unique in the AMS network), e.g. '199.4.42.250.1.1'.")] string? netId = null)
        => ads.Run(() => UmrtManager.Create(name, string.IsNullOrWhiteSpace(netId) ? null : netId.Trim()));

    [McpServerTool(Name = "rt_umrt_start"), Description(
        "Starts a Usermode Runtime instance (TcSystemServiceUm.exe, like its Start.bat; minimized console window) and waits until it answers " +
        "via ADS. It starts in Config mode; then use system_set_target with the returned netId and system_activate_configuration.")]
    public async Task<CallToolResult> Start(
        [Description("Instance name as listed by rt_umrt_list.")] string name,
        [Description("Seconds to wait until the runtime is reachable via ADS.")] int waitSeconds = 30)
        => await ads.RunAsync(async () =>
        {
            var pid = UmrtManager.Start(name);
            var deadline = DateTime.UtcNow.AddSeconds(Math.Max(1, waitSeconds));
            UmrtManager.Instance inst;
            TargetState.SystemState? state = null;
            do
            {
                await Task.Delay(500);
                inst = UmrtManager.Get(name);
                if (inst.NetId is not null)
                {
                    state = TargetState.Read(ads, inst.NetId, 1500);
                    if (state.Reachable)
                    {
                        break;
                    }
                }
            }
            while (DateTime.UtcNow < deadline);

            return new
            {
                name,
                processId = pid,
                netId = inst.NetId,
                reachable = state?.Reachable ?? false,
                state,
                hint = state?.Reachable == true ? null : "The runtime did not answer via ADS yet. Check its console window and rt_umrt_list.",
            };
        });

    [McpServerTool(Name = "rt_umrt_stop", Destructive = true), Description(
        "Stops a Usermode Runtime instance that was started as a process (sends 'x' to its console, kills it if it does not exit). " +
        "An instance running as the Windows service TcSysSrv is not stopped.")]
    public Task<CallToolResult> Stop(
        [Description("Instance name as listed by rt_umrt_list.")] string name,
        [Description("Seconds to wait for a graceful exit before killing the process.")] int waitSeconds = 15)
        => ads.Run(() =>
        {
            var inst = UmrtManager.Get(name);
            if (inst.NetId is not null)
            {
                ads.Disconnect(inst.NetId, TargetState.SystemServicePort);
            }

            return UmrtManager.Stop(name, TimeSpan.FromSeconds(Math.Max(1, waitSeconds)));
        });
}
