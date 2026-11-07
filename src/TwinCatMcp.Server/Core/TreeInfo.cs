using TCatSysManagerLib;

namespace TwinCatMcp.Core;

/// <summary>Serializable description of a System Manager tree item.</summary>
public sealed record TreeItemInfo(
    string Name,
    string Path,
    int ItemType,
    string? ItemTypeName,
    int SubType,
    string? SubTypeName,
    int ChildCount,
    string? Disabled,
    string? Comment,
    List<TreeItemInfo>? Children = null);

public static class TreeInfo
{
    public static TreeItemInfo Describe(ITcSmTreeItem item, int depth = 0)
    {
        List<TreeItemInfo>? children = null;
        var count = Safe(() => item.ChildCount);
        if (depth > 0 && count > 0)
        {
            children = new List<TreeItemInfo>(count);
            foreach (var child in Children(item))
            {
                children.Add(Describe(child, depth - 1));
            }
        }

        var type = Safe(() => item.ItemType);
        return new TreeItemInfo(
            Name: Safe(() => item.Name) ?? "",
            Path: Safe(() => item.PathName) ?? "",
            ItemType: type,
            ItemTypeName: Enum.IsDefined(typeof(TREEITEMTYPES), type) ? ((TREEITEMTYPES)type).ToString() : null,
            SubType: Safe(() => item.ItemSubType),
            SubTypeName: NullIfEmpty(Safe(() => item.ItemSubTypeName)),
            ChildCount: count,
            Disabled: Safe(() => ((ITcSmTreeItem2)item).Disabled) is var d && d != DISABLED_STATE.SMDS_NOT_DISABLED ? d.ToString() : null,
            Comment: NullIfEmpty(Safe(() => item.Comment)),
            Children: children);
    }

    /// <summary>Enumerates the direct children of a tree item (1-based COM index).</summary>
    public static IEnumerable<ITcSmTreeItem> Children(ITcSmTreeItem item)
    {
        var count = Safe(() => item.ChildCount);
        for (var i = 1; i <= count; i++)
        {
            ITcSmTreeItem? child = null;
            try
            {
                child = item.get_Child(i);
            }
            catch
            {
                // skip inaccessible child
            }

            if (child is not null)
            {
                yield return child;
            }
        }
    }

    /// <summary>Depth-first walk over all descendants.</summary>
    public static IEnumerable<ITcSmTreeItem> Descendants(ITcSmTreeItem root, int maxDepth = int.MaxValue)
    {
        var stack = new Stack<(ITcSmTreeItem Item, int Depth)>();
        foreach (var c in Children(root).Reverse())
        {
            stack.Push((c, 1));
        }

        while (stack.Count > 0)
        {
            var (item, depth) = stack.Pop();
            yield return item;
            if (depth < maxDepth)
            {
                foreach (var c in Children(item).Reverse())
                {
                    stack.Push((c, depth + 1));
                }
            }
        }
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    private static T Safe<T>(Func<T> f)
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
