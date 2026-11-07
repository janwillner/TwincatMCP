using System.Xml.Linq;
using TCatSysManagerLib;
using TwinCatMcp.Core;

namespace TwinCatMcp.Motion;

/// <summary>COM helpers for the NC configuration tree (TINC). STA thread only.</summary>
public static class NcTree
{
    // Item types observed on TwinCAT 3.1.4026 (ITcSmTreeItem.ItemType).
    public const int ItemTypeNcTask = 1;
    public const int ItemTypeAxes = 21;
    public const int ItemTypeAxis = 22;
    public const int ItemTypeEncoder = 23;
    public const int ItemTypeDrive = 24;
    public const int ItemTypeController = 25;

    /// <summary>Axis sub types accepted by CreateChild below "Axes" (verified live on 4026; 4 and 8+ are rejected).</summary>
    public static readonly IReadOnlyDictionary<string, int> AxisSubTypes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["continuous"] = 1,
        ["discrete"] = 2,
        ["stepper"] = 3,
        ["encoder"] = 5,
        ["hydraulic"] = 6,
        ["timeGenerator"] = 7,
    };

    public static int ParseAxisType(string? axisType)
    {
        if (string.IsNullOrWhiteSpace(axisType))
        {
            return 1;
        }

        if (int.TryParse(axisType, out var n))
        {
            return n;
        }

        return AxisSubTypes.TryGetValue(axisType.Trim(), out var st)
            ? st
            : throw new TcException($"Unknown axis type '{axisType}'.")
            {
                Hint = "Use one of: " + string.Join(", ", AxisSubTypes.Select(kv => $"{kv.Key} ({kv.Value})")),
            };
    }

    public static IEnumerable<ITcSmTreeItem> NcTasks(XaeSession s)
        => TreeInfo.Children(s.Lookup("TINC")).Where(c => Safe(() => c.ItemType) == ItemTypeNcTask);

    public static ITcSmTreeItem ResolveNcTask(XaeSession s, string? ncTask)
    {
        if (!string.IsNullOrWhiteSpace(ncTask))
        {
            if (ncTask.Contains('^'))
            {
                return s.Lookup(ncTask);
            }

            var safName = NcTaskXml.SafName(ncTask);
            return NcTasks(s).FirstOrDefault(t => t.Name.Equals(ncTask, StringComparison.OrdinalIgnoreCase) || t.Name.Equals(safName, StringComparison.OrdinalIgnoreCase))
                   ?? throw new TcException($"NC task '{ncTask}' not found.") { Hint = "Use motion_list_axes or create one with motion_create_nc_task." };
        }

        var tasks = NcTasks(s).ToList();
        return tasks.Count switch
        {
            0 => throw new TcException("The project has no NC task.") { Hint = "Create one with motion_create_nc_task." },
            1 => tasks[0],
            _ => throw new TcException("The project has several NC tasks; pass ncTask.") { Hint = string.Join(", ", tasks.Select(t => t.PathName)) },
        };
    }

    public static ITcSmTreeItem AxesFolder(XaeSession s, ITcSmTreeItem ncTask) => s.Lookup(ncTask.PathName + "^Axes");

    public static IEnumerable<ITcSmTreeItem> Axes(ITcSmTreeItem root)
        => TreeInfo.Descendants(root, 4).Where(i => Safe(() => i.ItemType) == ItemTypeAxis);

    /// <summary>Accepts a full tree path or a bare axis name (searched in all NC tasks).</summary>
    public static ITcSmTreeItem ResolveAxis(XaeSession s, string axis)
    {
        if (string.IsNullOrWhiteSpace(axis))
        {
            throw new TcException("axis must not be empty.");
        }

        ITcSmTreeItem item;
        if (axis.Contains('^'))
        {
            item = s.Lookup(axis);
        }
        else
        {
            var matches = NcTasks(s).SelectMany(t => Axes(t)).Where(a => a.Name.Equals(axis, StringComparison.OrdinalIgnoreCase)).ToList();
            item = matches.Count switch
            {
                0 => throw new TcException($"Axis '{axis}' not found.") { Hint = "Use motion_list_axes to see all axes and their paths." },
                1 => matches[0],
                _ => throw new TcException($"Axis name '{axis}' is ambiguous.") { Hint = "Pass the full path: " + string.Join(", ", matches.Select(m => m.PathName)) },
            };
        }

        if (Safe(() => item.ItemType) != ItemTypeAxis)
        {
            throw new TcException($"'{item.PathName}' is not an NC axis (item type {Safe(() => item.ItemType)}).")
            {
                Hint = "Pass an axis path like 'TINC^NC-Task 1 SAF^Axes^Axis 1'.",
            };
        }

        return item;
    }

    /// <summary>Compact axis description incl. the names of encoder/drive/controller sub types.</summary>
    public static object DescribeAxis(ITcSmTreeItem axis)
    {
        var summary = AxisXml.ReadSummary(axis.ProduceXml(false));
        string? encoder = null, drive = null, controller = null;
        foreach (var c in TreeInfo.Children(axis))
        {
            switch (Safe(() => c.ItemType))
            {
                case ItemTypeEncoder: encoder ??= Safe(() => c.ItemSubTypeName); break;
                case ItemTypeDrive: drive ??= Safe(() => c.ItemSubTypeName); break;
                case ItemTypeController: controller ??= Safe(() => c.ItemSubTypeName); break;
            }
        }

        return new
        {
            name = summary.Name,
            path = summary.Path,
            axisType = summary.SubTypeName,
            subType = summary.SubType,
            disabled = summary.Disabled ? true : (bool?)null,
            encoder = encoder is null ? null : $"{summary.EncoderType}: {encoder}",
            drive = drive is null ? null : $"{summary.DriveType}: {drive}",
            controller = controller is null ? null : $"{summary.ControllerType}: {controller}",
            ioLink = summary.IoLink,
            ioChannel = summary.IoChannel,
            axisIoType = summary.AxisIoType,
            objectId = summary.ObjectId is { } o ? MotionUnits.FormatHex(o) : null,
        };
    }

    /// <summary>Counts the variable links between an owner (e.g. axis) and anything below another path prefix.</summary>
    public static int CountLinks(string mappingXml, string ownerPath, string? otherPrefix = null)
    {
        var doc = XDocument.Parse(mappingXml);
        var n = 0;
        foreach (var a in doc.Root?.Elements("OwnerA") ?? [])
        {
            foreach (var b in a.Elements("OwnerB"))
            {
                var an = a.Attribute("Name")?.Value ?? "";
                var bn = b.Attribute("Name")?.Value ?? "";
                var involves = an.Equals(ownerPath, StringComparison.OrdinalIgnoreCase) || bn.Equals(ownerPath, StringComparison.OrdinalIgnoreCase);
                var other = an.Equals(ownerPath, StringComparison.OrdinalIgnoreCase) ? bn : an;
                if (involves && (otherPrefix is null || other.StartsWith(otherPrefix, StringComparison.OrdinalIgnoreCase)))
                {
                    n += b.Elements("Link").Count();
                }
            }
        }

        return n;
    }

    public static string? LastXmlError(ITcSmTreeItem item)
    {
        try
        {
            var e = ((ITcSmTreeItem3)item).GetLastXmlError();
            return string.IsNullOrWhiteSpace(e) ? null : e;
        }
        catch
        {
            return null;
        }
    }

    public static T Safe<T>(Func<T> f)
    {
        try
        {
            return f();
        }
        catch
        {
            return default!;
        }
    }
}
