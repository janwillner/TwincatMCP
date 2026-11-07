using TCatSysManagerLib;
using TwinCatMcp.Core;

namespace TwinCatMcp.Io;

/// <summary>COM helpers for the I/O tree (call on the STA thread only).</summary>
public static class IoTreeHelpers
{
    public const int ItemTypeDevice = (int)TREEITEMTYPES.TREEITEMTYPE_DEVICE;
    public const int ItemTypeBox = (int)TREEITEMTYPES.TREEITEMTYPE_BOX;
    public const int ItemTypeTerm = (int)TREEITEMTYPES.TREEITEMTYPE_TERM;
    public const int ItemTypeVar = (int)TREEITEMTYPES.TREEITEMTYPE_VAR;
    public const int ItemTypeVarGroup = (int)TREEITEMTYPES.TREEITEMTYPE_VARGRP;

    /// <summary>EtherCAT master sub type (TSM_DEV_TYPE_ETHERCATPROT).</summary>
    public const int EtherCatMasterSubType = 111;

    public static bool IsBox(ITcSmTreeItem item) => Safe(() => item.ItemType) is ItemTypeBox or ItemTypeTerm;

    /// <summary>
    /// Real children only. An EtherCAT master enumerates ALL boxes of its segment as children (flat), although they are
    /// nested below couplers in the tree; this keeps only items whose path is directly below <paramref name="item"/>.
    /// </summary>
    public static IEnumerable<ITcSmTreeItem> DirectChildren(ITcSmTreeItem item)
    {
        var prefix = Safe(() => item.PathName) + "^";
        foreach (var child in TreeInfo.Children(item))
        {
            var path = Safe(() => child.PathName) ?? "";
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && path.IndexOf('^', prefix.Length) < 0)
            {
                yield return child;
            }
        }
    }

    /// <summary>Variables / variable groups of an item (ITcSmTreeItem.Var[nInOut, n], 0 = inputs, 1 = outputs).</summary>
    public static IEnumerable<ITcSmTreeItem> Vars(ITcSmTreeItem item)
    {
        for (var io = 0; io <= 1; io++)
        {
            var count = Safe(() => item.get_VarCount(io));
            var list = new List<ITcSmTreeItem>();
            for (var n = 1; n <= count; n++)
            {
                var v = SafeRef(() => item.get_Var(io, n));
                if (v is null && n == 1)
                {
                    // 0-based fallback
                    for (var m = 0; m < count; m++)
                    {
                        if (SafeRef(() => item.get_Var(io, m)) is { } v0)
                        {
                            list.Add(v0);
                        }
                    }

                    break;
                }

                if (v is not null)
                {
                    list.Add(v);
                }
            }

            foreach (var v in list)
            {
                yield return v;
            }
        }
    }

    /// <summary>
    /// All variables (TREEITEMTYPE_VAR) below an item: box, terminal, channel group, PLC instance or a single variable.
    /// Sub boxes (terminals below a coupler) are only included when <paramref name="includeSubBoxes"/> is set.
    /// </summary>
    public static IEnumerable<VarInfo> EnumerateVariables(ITcSmTreeItem root, bool includeSubBoxes, int max)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<ITcSmTreeItem>();
        queue.Enqueue(root);
        var produced = 0;
        while (queue.Count > 0 && produced < max)
        {
            var item = queue.Dequeue();
            var path = Safe(() => item.PathName) ?? "";
            if (!seen.Add(path))
            {
                continue;
            }

            if (Safe(() => item.ItemType) == ItemTypeVar)
            {
                if (TryGetVarInfo(item) is { } info)
                {
                    produced++;
                    yield return info;
                }

                continue;
            }

            foreach (var next in Vars(item).Concat(DirectChildren(item)))
            {
                if (!includeSubBoxes && IsBox(next))
                {
                    continue;
                }

                queue.Enqueue(next);
            }
        }
    }

    public static VarInfo? TryGetVarInfo(ITcSmTreeItem item)
    {
        try
        {
            return IoXml.ParseVarDef(item.ProduceXml(false));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Executes XAE "System Manager commands" (ITcSmCommands). The object is obtained from the System Manager
    /// (QueryInterface) or from DTE.GetObject("TcSmCommands"). Returns per-command status.
    /// </summary>
    public static object RunSmCommands(XaeSession s, params (string Name, Action<ITcSmCommands> Run)[] commands)
    {
        ITcSmCommands? cmds = s.SysManager as ITcSmCommands;
        var source = cmds is null ? null : "ITcSysManager";
        if (cmds is null)
        {
            foreach (var name in new[] { "TcSmCommands", "TcSysManagerCommands" })
            {
                try
                {
                    cmds = s.Dte.GetObject(name) as ITcSmCommands;
                    if (cmds is not null)
                    {
                        source = $"DTE.GetObject(\"{name}\")";
                        break;
                    }
                }
                catch
                {
                    // try next
                }
            }
        }

        if (cmds is null)
        {
            return new { available = false, message = "ITcSmCommands is not available from this XAE instance." };
        }

        var results = new Dictionary<string, string>();
        foreach (var (name, run) in commands)
        {
            try
            {
                run(cmds);
                results[name] = "ok";
            }
            catch (Exception ex)
            {
                results[name] = "failed: " + ex.Message;
            }
        }

        return new { available = true, source, results };
    }

    /// <summary>Walks up to the I/O device (TREEITEMTYPE_DEVICE) that owns a box/variable.</summary>
    public static ITcSmTreeItem? OwningDevice(ITcSmTreeItem item)
    {
        var current = item;
        for (var i = 0; i < 64 && current is not null; i++)
        {
            if (Safe(() => current.ItemType) == ItemTypeDevice)
            {
                return current;
            }

            current = SafeRef(() => current.Parent);
        }

        return null;
    }

    public static T? Safe<T>(Func<T> f)
    {
        try
        {
            return f();
        }
        catch
        {
            return default;
        }
    }

    public static T? SafeRef<T>(Func<T> f)
        where T : class
    {
        try
        {
            return f();
        }
        catch
        {
            return null;
        }
    }
}
