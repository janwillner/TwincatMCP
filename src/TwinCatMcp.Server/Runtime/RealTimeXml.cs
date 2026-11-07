using System.Globalization;
using System.Text;

namespace TwinCatMcp.Runtime;

public sealed record RtCpu(int Id, int LoadLimitPercent, CycleTimeInfo BaseTime, int LatencyWarningUs);

public sealed record RtTargetCpuInfo(int NumProcessors, int RealTimeCpus, string? AvailableCpus, string? NonWindowsCpus, bool? ConfigValidOnTarget);

/// <summary>Real-time settings (TIRS, RTimeSetDef).</summary>
public sealed record RtSettings(
    int MaxCpus,
    string Affinity,
    int[] RealTimeCores,
    int RouterMemoryKb,
    int MaxStackSizeKb,
    List<RtCpu> Cpus,
    RtTargetCpuInfo? TargetCpuInfo);

/// <summary>Builds/parses the RTimeSetDef XML of the real-time settings node (TIRS).</summary>
public static class RealTimeXml
{
    /// <summary>One CPU entry of RTimeSetDef/CPUs; null members are not written.</summary>
    public sealed record CpuSetting(int Id, int? LoadLimitPercent, uint? BaseTimeTicks, int? LatencyWarningUs);

    public static RtSettings Parse(string xml)
    {
        var def = XmlText.Root(xml).Element("RTimeSetDef");
        var affinity = def.Str("Affinity") ?? "#x0";
        var cpus = def?.Element("CPUs")?.Elements("CPU").Select(c => new RtCpu(
            Id: int.TryParse(c.Attribute("id")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0,
            LoadLimitPercent: c.Int("LoadLimit") ?? 0,
            BaseTime: CycleTime.Describe(c.UInt("BaseTime") ?? 0),
            LatencyWarningUs: c.Int("LatencyWarning") ?? 0)).ToList() ?? [];
        var info = def?.Element("TargetCPUInfo");
        return new RtSettings(
            MaxCpus: def.Int("MaxCPUs") ?? 0,
            Affinity: affinity,
            RealTimeCores: Runtime.Affinity.ToCores(Runtime.Affinity.Parse(affinity)),
            RouterMemoryKb: def.Int("RouterMemory") ?? 0,
            MaxStackSizeKb: def.Int("MaxStackSize") ?? 0,
            Cpus: cpus,
            TargetCpuInfo: info is null
                ? null
                : new RtTargetCpuInfo(
                    NumProcessors: info.Int("NumProcessors") ?? 0,
                    RealTimeCpus: info.Int("RealTimeCPUs") ?? 0,
                    AvailableCpus: info.Str("AvailabeCPUs"),
                    NonWindowsCpus: info.Str("NonWindowsCPUs"),
                    ConfigValidOnTarget: info.Int("CpuConfigValidOnTarget") is { } v ? v != 0 : null));
    }

    public static string Build(int? maxCpus, ulong? affinity, IReadOnlyList<CpuSetting> cpus, int? routerMemoryKb)
    {
        var sb = new StringBuilder("<TreeItem><RTimeSetDef>");
        if (routerMemoryKb is { } rm)
        {
            sb.Append("<RouterMemory>").Append(XmlText.N(rm)).Append("</RouterMemory>");
        }

        if (maxCpus is { } m)
        {
            sb.Append("<MaxCPUs>").Append(XmlText.N(m)).Append("</MaxCPUs>");
        }

        if (affinity is { } a)
        {
            sb.Append("<Affinity>").Append(Runtime.Affinity.Format(a)).Append("</Affinity>");
        }

        if (cpus.Count > 0)
        {
            sb.Append("<CPUs>");
            foreach (var c in cpus)
            {
                sb.Append("<CPU id=\"").Append(XmlText.N(c.Id)).Append("\">");
                if (c.LoadLimitPercent is { } l)
                {
                    sb.Append("<LoadLimit>").Append(XmlText.N(l)).Append("</LoadLimit>");
                }

                if (c.BaseTimeTicks is { } b)
                {
                    sb.Append("<BaseTime>").Append(XmlText.N(b)).Append("</BaseTime>");
                }

                if (c.LatencyWarningUs is { } w)
                {
                    sb.Append("<LatencyWarning>").Append(XmlText.N(w)).Append("</LatencyWarning>");
                }

                sb.Append("</CPU>");
            }

            sb.Append("</CPUs>");
        }

        return sb.Append("</RTimeSetDef></TreeItem>").ToString();
    }
}
