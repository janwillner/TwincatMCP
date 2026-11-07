using System.Globalization;
using System.Xml.Linq;
using TwinCatMcp.Core;

namespace TwinCatMcp.Motion;

public sealed record NcTaskInfo(string Name, string Path, int? CycleTimeUs, int? Priority, int? AmsPort, uint? ObjectId);

/// <summary>XML of the NC SAF task (TINC^&lt;name&gt; SAF): &lt;TaskDef&gt; with CycleTime in 100 ns ticks.</summary>
public static class NcTaskXml
{
    /// <summary>TwinCAT always appends " SAF" to the name passed to CreateChild on TINC.</summary>
    public static string SafName(string requested)
    {
        var n = requested.Trim();
        return n.EndsWith(" SAF", StringComparison.OrdinalIgnoreCase) ? n : n + " SAF";
    }

    public static NcTaskInfo Read(string produceXml)
    {
        var root = XDocument.Parse(produceXml).Root ?? throw new TcException("Empty task XML.");
        var def = root.Element("TaskDef");
        var ticks = Int(def?.Element("CycleTime"));
        return new NcTaskInfo(
            Name: root.Element("ItemName")?.Value ?? "",
            Path: root.Element("PathName")?.Value ?? "",
            CycleTimeUs: ticks is null ? null : MotionUnits.TicksToMicroseconds(ticks.Value),
            Priority: Int(def?.Element("Priority")),
            AmsPort: Int(def?.Element("AmsPort")),
            ObjectId: root.Element("ObjectId") is { } o ? MotionUnits.ParseTcUInt(o.Value) : null);
    }

    /// <summary>Returns a TaskDef fragment for ConsumeXml or null when nothing is to be changed.</summary>
    public static string? BuildUpdate(int? cycleTimeUs, int? priority)
    {
        if (cycleTimeUs is null && priority is null)
        {
            return null;
        }

        var def = new XElement("TaskDef");
        if (cycleTimeUs is { } us)
        {
            if (us <= 0)
            {
                throw new TcException("cycleTimeUs must be > 0.");
            }

            def.Add(new XElement("CycleTime", MotionUnits.MicrosecondsToTicks(us).ToString(CultureInfo.InvariantCulture)));
        }

        if (priority is { } p)
        {
            def.Add(new XElement("Priority", p.ToString(CultureInfo.InvariantCulture)));
        }

        return new XElement("TreeItem", def).ToString(SaveOptions.DisableFormatting);
    }

    private static int? Int(XElement? e)
        => e is not null && int.TryParse(e.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
}
