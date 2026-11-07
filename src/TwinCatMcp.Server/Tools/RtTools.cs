using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TwinCatMcp.Core;
using TwinCatMcp.Runtime;

namespace TwinCatMcp.Tools;

/// <summary>Real-time settings of the project (TIRS: cores, base time, CPU limit) - work package 3.</summary>
[McpServerToolType]
public sealed class RtTools(XaeSession session)
{
    [McpServerTool(Name = "rt_get_settings", ReadOnly = true), Description(
        "Returns the real-time settings of the project (TIRS): real-time cores (affinity), per-core base time (ms/us and 100 ns ticks), " +
        "CPU load limit %, latency warning, router memory and the CPU info last read from the target.")]
    public Task<CallToolResult> GetSettings()
        => session.RunAsync(s => RealTimeXml.Parse(s.Lookup("TIRS").ProduceXml(false)));

    [McpServerTool(Name = "rt_set_settings"), Description(
        "Changes real-time settings (TIRS RTimeSetDef). Only given values change. Example: cores=[0], baseTime=500, unit='us', loadLimitPercent=60. " +
        "baseTime/loadLimitPercent/latencyWarningUs apply to every core in 'cores' (or to all current real-time cores if 'cores' is empty). " +
        "Task cycle times must be multiples of the base time. Activate the configuration afterwards.")]
    public Task<CallToolResult> SetSettings(
        [Description("Real-time core indices (e.g. [0] or [1,2]). Empty = keep the current cores.")] int[]? cores = null,
        [Description("Base time per core in 'unit' (e.g. 1 ms, 500 us, 250 us). Empty = unchanged.")] double? baseTime = null,
        [Description("'ms', 'us' or 'ns'.")] string unit = "ms",
        [Description("CPU load limit in percent (1..100). Empty = unchanged.")] int? loadLimitPercent = null,
        [Description("Latency warning in microseconds (0 = off). Empty = unchanged.")] int? latencyWarningUs = null,
        [Description("Number of CPUs TwinCAT may use (MaxCPUs). Empty = derived from the highest core index if needed.")] int? maxCpus = null,
        [Description("Router memory in KB (RouterMemory, e.g. 32768). Empty = unchanged.")] int? routerMemoryKb = null)
        => session.RunAsync(s =>
        {
            var item = s.Lookup("TIRS");
            var before = RealTimeXml.Parse(item.ProduceXml(false));
            if (loadLimitPercent is < 1 or > 100)
            {
                throw new TcException("loadLimitPercent must be between 1 and 100.");
            }

            uint? baseTicks = baseTime is { } b ? CycleTime.ToTicks(b, unit) : null;
            var targetCores = cores is { Length: > 0 } ? cores.Distinct().Order().ToArray() : before.RealTimeCores;
            ulong? affinity = cores is { Length: > 0 } ? Affinity.FromCores(targetCores) : null;
            var needed = targetCores.Length == 0 ? 0 : targetCores.Max() + 1;
            int? max = maxCpus ?? (needed > before.MaxCpus ? needed : null);
            var perCore = baseTicks is null && loadLimitPercent is null && latencyWarningUs is null && affinity is null
                ? []
                : targetCores.Select(c => new RealTimeXml.CpuSetting(c, loadLimitPercent, baseTicks, latencyWarningUs)).ToList();

            var xml = RealTimeXml.Build(max, affinity, perCore, routerMemoryKb);
            var error = XaeRuntime.Consume(item, xml);
            var after = RealTimeXml.Parse(item.ProduceXml(false));
            return new { settings = after, appliedXml = xml, xmlError = error };
        });
}
