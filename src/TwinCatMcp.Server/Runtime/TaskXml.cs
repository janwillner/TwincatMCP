using System.Text;

namespace TwinCatMcp.Runtime;

/// <summary>Real-time task description parsed from TIRT^&lt;task&gt; ProduceXml().</summary>
public sealed record TaskInfo(
    string Name,
    string Path,
    int Priority,
    CycleTimeInfo CycleTime,
    bool AutoStart,
    bool IoAtBegin,
    int AmsPort,
    bool WithImage,
    string CpuAffinity,
    int[] Cores,
    bool Disabled,
    int ExceedWarning);

/// <summary>Builds/parses the TaskDef XML of real-time tasks (TIRT children) and the PlcTaskDef of PLC task references.</summary>
public static class TaskXml
{
    /// <summary>CreateChild sub types on TIRT: 0 = task with process image, 1 = task without image.</summary>
    public const int SubTypeWithImage = 0;
    public const int SubTypeWithoutImage = 1;

    public static TaskInfo Parse(string xml)
    {
        var root = XmlText.Root(xml);
        var def = root.Element("TaskDef");
        var mask = Affinity.Parse(def.Str("CpuAffinity"));
        return new TaskInfo(
            Name: root.Str("ItemName") ?? "",
            Path: root.Str("PathName") ?? "",
            Priority: def.Int("Priority") ?? 0,
            CycleTime: CycleTime.Describe(def.UInt("CycleTime") ?? 0),
            AutoStart: def.Bool("AutoStart") ?? false,
            IoAtBegin: def.Bool("IoAtBegin") ?? false,
            AmsPort: def.Int("AmsPort") ?? 0,
            // Verified on 4026.22: tasks without image report <ItemSubType>1</ItemSubType>, tasks with image omit the element.
            WithImage: (root.Int("ItemSubType") ?? SubTypeWithImage) == SubTypeWithImage,
            CpuAffinity: def.Str("CpuAffinity") ?? "#x0",
            Cores: Affinity.ToCores(mask),
            Disabled: root.Bool("Disabled") ?? false,
            ExceedWarning: def.Int("ExceedWarning") ?? 0);
    }

    /// <summary>Minimal ConsumeXml fragment; only non-null values are written.</summary>
    public static string BuildTaskDef(int? priority, uint? cycleTimeTicks, bool? autoStart, ulong? cpuAffinity, bool? ioAtBegin = null)
    {
        var sb = new StringBuilder("<TreeItem><TaskDef>");
        if (priority is { } p)
        {
            sb.Append("<Priority>").Append(XmlText.N(p)).Append("</Priority>");
        }

        if (cycleTimeTicks is { } c)
        {
            sb.Append("<CycleTime>").Append(XmlText.N(c)).Append("</CycleTime>");
        }

        if (autoStart is { } a)
        {
            sb.Append("<AutoStart>").Append(XmlText.B(a)).Append("</AutoStart>");
        }

        if (ioAtBegin is { } io)
        {
            sb.Append("<IoAtBegin>").Append(XmlText.B(io)).Append("</IoAtBegin>");
        }

        if (cpuAffinity is { } m)
        {
            sb.Append("<CpuAffinity>").Append(Affinity.Format(m)).Append("</CpuAffinity>");
        }

        return sb.Append("</TaskDef></TreeItem>").ToString();
    }

    public static string BuildLinkedTask(string taskPath)
        => $"<TreeItem><PlcTaskDef><LinkedTask>{XmlText.Escape(taskPath)}</LinkedTask></PlcTaskDef></TreeItem>";

    public static string? ParseLinkedTask(string xml)
        => XmlText.Root(xml).Element("PlcTaskDef").Str("LinkedTask") is { Length: > 0 } s ? s : null;
}
