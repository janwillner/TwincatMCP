using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TCatSysManagerLib;
using TwinCatMcp.Core;

namespace TwinCatMcp.Tools;

/// <summary>Generic System Manager tree access (works for every node type: tasks, I/O, PLC, NC, ...).</summary>
[McpServerToolType]
public sealed class TreeTools(XaeSession session)
{
    [McpServerTool(Name = "tree_get_item", ReadOnly = true), Description(
        "Describes a tree item (name, full path, item type, sub type, child count). Optionally includes children up to 'depth' levels.")]
    public Task<CallToolResult> GetItem(
        [Description("Tree path, e.g. 'TIRT' or 'TIPC^MyPlc^MyPlc Project^POUs'")] string treePath,
        [Description("Levels of children to include (0 = none).")] int depth = 0)
        => session.RunAsync(s => TreeInfo.Describe(s.Lookup(treePath), Math.Clamp(depth, 0, 10)));

    [McpServerTool(Name = "tree_get_children", ReadOnly = true), Description(
        "Lists the direct children of one or more tree items. Use the roots TIRC, TIRT, TIRS, TIPC, TINC, TIID, TIRR to explore a project.")]
    public Task<CallToolResult> GetChildren(
        [Description("One or more tree paths.")] string[] treePaths)
        => session.RunAsync(s => treePaths.ToDictionary(p => p, p =>
        {
            try
            {
                return (object)TreeInfo.Children(s.Lookup(p)).Select(c => TreeInfo.Describe(c)).ToArray();
            }
            catch (Exception ex) when (!XaeSession.IsSessionFatal(ex))
            {
                return new { error = ex.Message };
            }
        }));

    [McpServerTool(Name = "tree_exists", ReadOnly = true), Description("Checks whether one or more tree paths exist.")]
    public Task<CallToolResult> Exists(string[] treePaths)
        => session.RunAsync(s => treePaths.ToDictionary(p => p, p => s.SysManager.TestItemPath(p)));

    [McpServerTool(Name = "tree_produce_xml", ReadOnly = true), Description(
        "Returns the XML description of a tree item (ITcSmTreeItem.ProduceXml). This exposes all parameters of tasks, devices, " +
        "terminals, axes, PLC projects etc. Modify the XML and send it back with tree_consume_xml.")]
    public Task<CallToolResult> ProduceXml(string treePath, [Description("Include all children recursively (can be large).")] bool recursive = false)
        => session.RunAsync(s => new { treePath, xml = s.Lookup(treePath).ProduceXml(recursive) });

    [McpServerTool(Name = "tree_consume_xml"), Description(
        "Applies XML to a tree item (ITcSmTreeItem.ConsumeXml). Only the elements contained in the XML are changed, " +
        "so a minimal '<TreeItem>...</TreeItem>' fragment with just the changed parameters is enough.")]
    public Task<CallToolResult> ConsumeXml(string treePath, string xml)
        => session.RunAsync(s =>
        {
            var item = s.Lookup(treePath);
            item.ConsumeXml(xml);
            string? xmlError = null;
            try { xmlError = ((ITcSmTreeItem3)item).GetLastXmlError(); } catch { /* not available */ }
            return new { treePath, applied = true, lastXmlError = string.IsNullOrWhiteSpace(xmlError) ? null : xmlError };
        });

    [McpServerTool(Name = "tree_create_child"), Description(
        "Creates a child item below a tree item (ITcSmTreeItem.CreateChild). The meaning of subType and info depends on the parent: " +
        "e.g. TIRT -> subType 0 = task; TIID -> subType 111 = EtherCAT master; EtherCAT master -> subType 9099 + info 'EL1008' = terminal; " +
        "TINC -> subType 1 = NC task; PLC folder -> see plc_/pou_ tools which wrap this.")]
    public Task<CallToolResult> CreateChild(
        string parentPath,
        string name,
        [Description("Item sub type (see TwinCAT Automation Interface docs).")] int subType = 0,
        [Description("Insert before this sibling (optional).")] string? before = null,
        [Description("Additional creation info (vInfo), e.g. product type 'EL1008-0000-0018' or a template name.")] string? info = null)
        => session.RunAsync(s =>
        {
            var parent = s.Lookup(parentPath);
            var child = parent.CreateChild(name, subType, before ?? "", info is null ? Type.Missing : info);
            return TreeInfo.Describe(child);
        }, TimeSpan.FromMinutes(2));

    [McpServerTool(Name = "tree_delete_child", Destructive = true), Description("Deletes a child item by name below the given parent.")]
    public Task<CallToolResult> DeleteChild(string parentPath, string childName)
        => session.RunAsync(s => { s.Lookup(parentPath).DeleteChild(childName); return new { deleted = parentPath + "^" + childName }; });

    [McpServerTool(Name = "tree_delete_item", Destructive = true), Description("Deletes the tree item at the given path.")]
    public Task<CallToolResult> DeleteItem(string treePath)
        => session.RunAsync(s =>
        {
            var item = s.Lookup(treePath);
            var name = item.Name;
            item.Parent.DeleteChild(name);
            return new { deleted = treePath };
        });

    [McpServerTool(Name = "tree_rename_item"), Description("Renames a tree item.")]
    public Task<CallToolResult> Rename(string treePath, string newName)
        => session.RunAsync(s =>
        {
            var item = s.Lookup(treePath);
            item.Name = newName;
            return TreeInfo.Describe(item);
        });

    [McpServerTool(Name = "tree_set_comment"), Description("Sets the comment of a tree item.")]
    public Task<CallToolResult> SetComment(string treePath, string comment)
        => session.RunAsync(s => { s.Lookup(treePath).Comment = comment; return new { treePath, comment }; });

    [McpServerTool(Name = "tree_set_disabled"), Description("Disables or enables a tree item (e.g. an I/O device, box, task or axis).")]
    public Task<CallToolResult> SetDisabled(string treePath, bool disabled)
        => session.RunAsync(s =>
        {
            ((ITcSmTreeItem2)s.Lookup(treePath)).Disabled = disabled ? DISABLED_STATE.SMDS_DISABLED : DISABLED_STATE.SMDS_NOT_DISABLED;
            return new { treePath, disabled };
        });

    [McpServerTool(Name = "tree_export_child"), Description("Exports a child item to a file (.xti for I/O/tasks/NC, .tcpou etc.).")]
    public Task<CallToolResult> ExportChild(string parentPath, string childName, string filePath)
        => session.RunAsync(s => { s.Lookup(parentPath).ExportChild(childName, Path.GetFullPath(filePath)); return new { exported = Path.GetFullPath(filePath) }; });

    [McpServerTool(Name = "tree_import_child"), Description("Imports a child item from a file (.xti, ...) below the given parent.")]
    public Task<CallToolResult> ImportChild(string parentPath, string filePath, [Description("Optional new name")] string? name = null, string? before = null, bool reconnect = true)
        => session.RunAsync(s => TreeInfo.Describe(s.Lookup(parentPath).ImportChild(Path.GetFullPath(filePath), before ?? "", reconnect, name ?? "")));

    [McpServerTool(Name = "tree_find", ReadOnly = true), Description(
        "Searches the tree below a root for items whose name contains the pattern (case-insensitive). Returns their paths.")]
    public Task<CallToolResult> Find(
        [Description("Root path, e.g. TIID or TIPC")] string rootPath,
        [Description("Substring of the item name. Empty = all items.")] string? namePattern = null,
        int maxDepth = 8,
        int maxResults = 500)
        => session.RunAsync(s =>
        {
            var root = s.Lookup(rootPath);
            return TreeInfo.Descendants(root, maxDepth)
                .Where(i => string.IsNullOrEmpty(namePattern) || (i.Name?.Contains(namePattern, StringComparison.OrdinalIgnoreCase) ?? false))
                .Take(maxResults)
                .Select(i => TreeInfo.Describe(i))
                .ToArray();
        });
}
