using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TCatSysManagerLib;
using TwinCatMcp.Core;
using TwinCatMcp.Motion;

namespace TwinCatMcp.Tools;

/// <summary>TcCOM module instances below 'TIRC^TcCOM Objects': classes, create, parameters, task context, delete.</summary>
[McpServerToolType]
public sealed class TcComTools(XaeSession session)
{
    public const string TcComRoot = "TIRC^TcCOM Objects";

    [McpServerTool(Name = "tccom_list_classes", ReadOnly = true), Description(
        "Lists TcCOM module classes installed on this PC (read from the TMC files in TwinCAT Config\\Modules, CustomConfig\\Modules " +
        "and the module repository C:\\ProgramData\\Beckhoff\\TwinCAT\\3.1\\Repository). Returns name, classId, class factory, group, " +
        "parameter names. Use name or classId with tccom_create_object. Example filter: 'SoftDrive', 'Kinematic', 'Xts'.")]
    public Task<CallToolResult> ListClasses(
        [Description("Case-insensitive substring of name, group, class factory, vendor or classId. Empty = all.")] string? filter = null,
        [Description("Maximum number of classes returned.")] int maxResults = 100,
        [Description("Include parameter names of each class.")] bool includeParameters = false)
    {
        try
        {
            var classes = TmcCatalog.Scan(TmcCatalog.DefaultFolders())
                .Where(c => string.IsNullOrWhiteSpace(filter) ||
                            new[] { c.Name, c.Group, c.ClassFactory, c.Vendor, c.ClassId }.Any(v => v?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true))
                .ToList();
            return Task.FromResult(ToolResult.Ok(new
            {
                total = classes.Count,
                classes = classes.Take(Math.Max(1, maxResults)).Select(c => new
                {
                    name = c.Name,
                    classId = c.ClassId,
                    classFactory = c.ClassFactory,
                    group = c.Group,
                    vendor = c.Vendor,
                    parameters = includeParameters ? c.Parameters : null,
                    tmc = c.SourceFile,
                }).ToArray(),
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error(ex));
        }
    }

    [McpServerTool(Name = "tccom_list_objects", ReadOnly = true), Description(
        "Lists TcCOM object instances (ITcModuleManager): tree path, class name, classId, object id (OTCID) and parent object id. " +
        "By default only objects below 'TIRC^TcCOM Objects'; includeAll=true also lists tasks, NC, PLC and I/O modules.")]
    public Task<CallToolResult> ListObjects([Description("Also list system modules (tasks, NC, PLC instances, ...).")] bool includeAll = false)
        => session.RunAsync(s =>
        {
            var mm = (ITcModuleManager3)s.SysManager.GetModuleManager();
            var list = new List<object>();
            foreach (ITcModuleInstance2 m in mm)
            {
                var path = NcTree.Safe(() => ((ITcSmTreeItem)m).PathName);
                if (!includeAll && (path is null || !path.StartsWith(TcComRoot + "^", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                list.Add(new
                {
                    name = NcTree.Safe(() => m.ModuleInstanceName),
                    path,
                    className = NcTree.Safe(() => m.ModuleTypeName),
                    classId = NcTree.Safe(() => m.ClassID.ToString("B").ToUpperInvariant()),
                    objectId = MotionUnits.FormatHex(NcTree.Safe(() => m.oid)),
                    parentObjectId = NcTree.Safe(() => m.ParentOID) is var p and > 0 ? MotionUnits.FormatHex(p) : null,
                });
            }

            return new { count = list.Count, objects = list };
        });

    [McpServerTool(Name = "tccom_create_object"), Description(
        "Creates a TcCOM object instance from an installed module class (by classId GUID or class name, see tccom_list_classes), " +
        "e.g. classIdOrName='{272A98C0-4C87-4243-BED6-3BB69E29F02C}' or 'TcJobPool'. The tree shows it as '<name> (<class>)'. " +
        "Optionally assigns the cyclic task context right away (taskPath e.g. 'TIRT^Task 1').")]
    public Task<CallToolResult> CreateObject(
        [Description("Instance name, e.g. 'SoftDrive1'.")] string name,
        [Description("Module class GUID (with or without braces) or class name.")] string classIdOrName,
        [Description("Parent node; default 'TIRC^TcCOM Objects'.")] string parentPath = TcComRoot,
        [Description("Task to use as context 0 (optional), e.g. 'TIRT^Task 1'.")] string? taskPath = null)
        => session.RunAsync(s =>
        {
            var parent = s.Lookup(parentPath);
            var info = TcComXml.NormalizeGuid(classIdOrName) ?? classIdOrName.Trim();
            var item = parent.CreateChild(name, 0, "", info);
            if (!string.IsNullOrWhiteSpace(taskPath))
            {
                SetContext(s, item, taskPath, null, null);
            }

            return Summarize(TcComXml.Read(item.ProduceXml(false)));
        }, TimeSpan.FromMinutes(2));

    [McpServerTool(Name = "tccom_get_parameters", ReadOnly = true), Description(
        "Returns class, object id, parameters (name, type, current value), contexts (task assignment) and data areas of a TcCOM object. " +
        "object: full tree path or the instance name below 'TIRC^TcCOM Objects' (e.g. 'SoftDrive1').")]
    public Task<CallToolResult> GetParameters([Description("TcCOM object path or name.")] string obj)
        => session.RunAsync(s => Summarize(TcComXml.Read(ResolveObject(s, obj).ProduceXml(false))));

    [McpServerTool(Name = "tccom_set_parameters"), Description(
        "Sets TcCOM object parameter values (startup values, via ConsumeXml ParameterValues). Values are given as text in TwinCAT " +
        "notation, e.g. {\"PoolId\": \"3\", \"MqttTopic\": \"line1\", \"MqttClient\": \"#x01010020\"}. Returns the values read back.")]
    public Task<CallToolResult> SetParameters(
        [Description("TcCOM object path or name.")] string obj,
        [Description("Parameter name -> value text.")] Dictionary<string, string> values)
        => session.RunAsync(s =>
        {
            var item = ResolveObject(s, obj);
            var module = TcComXml.Read(item.ProduceXml(false));
            item.ConsumeXml(TcComXml.BuildParameterUpdate(module, values));
            var after = TcComXml.Read(item.ProduceXml(false));
            return new
            {
                path = item.PathName,
                values = after.Parameters.Where(p => values.Keys.Any(k => k.Equals(p.Name, StringComparison.OrdinalIgnoreCase)))
                    .ToDictionary(p => p.Name, p => p.Value),
                lastXmlError = NcTree.LastXmlError(item),
            };
        });

    [McpServerTool(Name = "tccom_set_context"), Description(
        "Assigns a task to a context of a TcCOM object (ITcModuleInstance2.SetModuleContext), i.e. which task calls the module cyclically. " +
        "Pass taskPath (e.g. 'TIRT^Task 1') or taskObjectId (e.g. '#x02010010'). contextId: the module's context id as listed by " +
        "tccom_get_parameters (usually 1); default = first context.")]
    public Task<CallToolResult> SetContextTool(
        [Description("TcCOM object path or name.")] string obj,
        [Description("Task tree path, e.g. 'TIRT^Task 1'.")] string? taskPath = null,
        [Description("Task object id instead of taskPath, e.g. '#x02010010'.")] string? taskObjectId = null,
        [Description("Context id (see tccom_get_parameters, usually 1). Optional = first context.")] int? contextId = null)
        => session.RunAsync(s =>
        {
            var item = ResolveObject(s, obj);
            var (id, oid) = SetContext(s, item, taskPath, taskObjectId, contextId);
            var contexts = Contexts(TcComXml.Read(item.ProduceXml(false)));
            return new { path = item.PathName, contextId = id, taskObjectId = MotionUnits.FormatHex(oid), contexts };
        });

    [McpServerTool(Name = "tccom_delete_object", Destructive = true), Description("Deletes TcCOM object instances (path or instance name).")]
    public Task<CallToolResult> DeleteObject([Description("TcCOM object paths or names.")] string[] objects)
        => session.RunAsync(s =>
        {
            var deleted = new List<string>();
            foreach (var o in objects)
            {
                var item = ResolveObject(s, o);
                var path = item.PathName;
                item.Parent.DeleteChild(item.Name);
                deleted.Add(path);
            }

            return new { deleted };
        });

    // ------------------------------------------------------------------ helpers (STA)

    private static ITcSmTreeItem ResolveObject(XaeSession s, string obj)
    {
        if (string.IsNullOrWhiteSpace(obj))
        {
            throw new TcException("obj must not be empty.");
        }

        if (obj.Contains('^') && s.SysManager.TryLookupTreeItem(obj, out var direct) && direct is not null)
        {
            return direct;
        }

        var parentPath = obj.Contains('^') ? obj[..obj.LastIndexOf('^')] : TcComRoot;
        var shortName = obj.Contains('^') ? obj[(obj.LastIndexOf('^') + 1)..] : obj;
        var parent = s.Lookup(parentPath);
        var name = TcComXml.ResolveChildName(TreeInfo.Children(parent).Select(c => c.Name), shortName)
                   ?? throw new TcException($"TcCOM object '{obj}' not found.") { Hint = "Use tccom_list_objects. Tree names look like '<instance> (<class>)'." };
        return s.Lookup(parentPath + "^" + name);
    }

    private static (uint ContextId, uint TaskOid) SetContext(XaeSession s, ITcSmTreeItem item, string? taskPath, string? taskObjectId, int? contextId)
    {
        uint oid;
        if (!string.IsNullOrWhiteSpace(taskObjectId))
        {
            oid = MotionUnits.ParseTcUInt(taskObjectId);
        }
        else if (!string.IsNullOrWhiteSpace(taskPath))
        {
            oid = TcComXml.ReadObjectId(s.Lookup(taskPath).ProduceXml(false));
        }
        else
        {
            throw new TcException("Pass taskPath or taskObjectId.") { Hint = "Tasks are below TIRT, e.g. 'TIRT^Task 1' (see tree_get_children TIRT)." };
        }

        var mi = item as ITcModuleInstance2 ?? throw new TcException($"'{item.PathName}' is not a TcCOM module instance.");
        // SetModuleContext expects the context *id* of the module (TcModuleInstance/Module/Contexts/Context/Id), not an index.
        var id = contextId ?? TcComXml.Read(item.ProduceXml(false)).Contexts.FirstOrDefault()?.Id
                 ?? throw new TcException($"'{item.PathName}' has no context to assign a task to.");
        mi.SetModuleContext((uint)id, oid);
        return ((uint)id, oid);
    }

    private static object Summarize(TcComModuleInfo m) => new
    {
        name = m.Name,
        path = m.Path,
        className = m.ClassName,
        classId = m.ClassId,
        classFactory = m.ClassFactory,
        objectId = m.ObjectId is { } o ? MotionUnits.FormatHex(o) : null,
        parameters = m.Parameters.Select(p => new { name = p.Name, type = p.Type, value = p.Value }).ToArray(),
        contexts = Contexts(m),
        dataAreas = m.DataAreas.Select(d => new { name = d.Name, type = d.AreaType, symbols = d.Symbols }).ToArray(),
    };

    private static object[] Contexts(TcComModuleInfo m) => m.Contexts.Select(c => (object)new
    {
        id = c.Id,
        name = c.Name,
        taskObjectId = c.TaskObjectId is { } t ? MotionUnits.FormatHex(t) : null,
    }).ToArray();
}
