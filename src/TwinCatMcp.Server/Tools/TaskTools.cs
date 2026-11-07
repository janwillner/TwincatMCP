using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TCatSysManagerLib;
using TwinCatMcp.Core;
using TwinCatMcp.Runtime;

namespace TwinCatMcp.Tools;

/// <summary>Real-time tasks below TIRT (create, cycle time, priority, cores, autostart) and PLC task links (work package 3).</summary>
[McpServerToolType]
public sealed class TaskTools(XaeSession session)
{
    private const int PlcTaskItemType = 621; // TREEITEMTYPE_PLCTASK: task reference inside a nested PLC project

    [McpServerTool(Name = "task_list", ReadOnly = true), Description(
        "Lists all real-time tasks (TIRT) with cycle time (ms/us and raw 100 ns ticks), priority, AMS port, autostart, CPU cores, and which PLC task " +
        "references are linked to each task.")]
    public Task<CallToolResult> List()
        => session.RunAsync(s =>
        {
            var links = PlcTaskReferences(s);
            return TreeInfo.Children(s.Lookup("TIRT"))
                .Select(t => TaskXml.Parse(t.ProduceXml(false)))
                .Select(t => new
                {
                    task = t,
                    linkedPlcTasks = links.Where(l => string.Equals(l.LinkedTask, t.Path, StringComparison.OrdinalIgnoreCase)).Select(l => l.Path).ToArray(),
                })
                .ToArray();
        });

    [McpServerTool(Name = "task_create"), Description(
        "Creates a real-time task below TIRT and configures it in one step. Example: name='FastTask', cycleTime=1, unit='ms', priority=10. " +
        "The cycle time must be a multiple of the real-time base time (rt_get_settings, default 1 ms). " +
        "Link a PLC to it with task_link_plc.")]
    public Task<CallToolResult> Create(
        [Description("Task name, e.g. 'FastTask'.")] string name,
        [Description("Cycle time value in 'unit' (e.g. 1 with unit 'ms', 500 with unit 'us'). Default 10 ms.")] double cycleTime = 10,
        [Description("Unit of cycleTime: 'ms', 'us' or 'ns' (TwinCAT stores 100 ns ticks).")] string unit = "ms",
        [Description("Task priority (lower = higher priority, 1..). Empty = TwinCAT picks a free priority.")] int? priority = null,
        [Description("true = task with process image (Image/Inputs/Outputs for I/O variables; sub type 0), false = without image (sub type 1, usual for PLC tasks).")] bool withImage = false,
        [Description("Start the task automatically with TwinCAT (default true in TwinCAT).")] bool? autoStart = null,
        [Description("Optional CPU core indices the task may run on (real-time cores, e.g. [1]). Empty = default core.")] int[]? cores = null)
        => session.RunAsync(s =>
        {
            var ticks = CycleTime.ToTicks(cycleTime, unit);
            var tirt = s.Lookup("TIRT");
            if (s.SysManager.TestItemPath("TIRT^" + name))
            {
                throw new TcException($"Task '{name}' already exists.") { Hint = "Use task_configure to change it or choose another name." };
            }

            var item = tirt.CreateChild(name, withImage ? TaskXml.SubTypeWithImage : TaskXml.SubTypeWithoutImage, "", Type.Missing);
            var error = XaeRuntime.Consume(item, TaskXml.BuildTaskDef(priority, ticks, autoStart, cores is { Length: > 0 } ? Affinity.FromCores(cores) : null));
            return new { task = TaskXml.Parse(item.ProduceXml(false)), xmlError = error };
        });

    [McpServerTool(Name = "task_configure"), Description(
        "Changes cycle time, priority, autostart, CPU cores or 'I/O at task begin' of an existing real-time task (TaskDef XML). Only given values change. " +
        "Returns the task as TwinCAT stored it plus warnings when TwinCAT adjusted a value (e.g. cycle time not a multiple of the base time).")]
    public Task<CallToolResult> Configure(
        [Description("Task name ('PlcTask') or path ('TIRT^PlcTask').")] string task,
        [Description("New cycle time in 'unit'. Empty = unchanged.")] double? cycleTime = null,
        [Description("'ms', 'us' or 'ns'.")] string unit = "ms",
        [Description("New priority. Empty = unchanged.")] int? priority = null,
        [Description("Autostart with TwinCAT. Empty = unchanged.")] bool? autoStart = null,
        [Description("CPU core indices, e.g. [0] or [1,2]. Empty = unchanged.")] int[]? cores = null,
        [Description("Update the process image at the beginning of the task cycle. Empty = unchanged.")] bool? ioAtBegin = null)
        => session.RunAsync(s =>
        {
            var item = s.Lookup(XaeRuntime.TaskPath(task));
            uint? ticks = cycleTime is { } c ? CycleTime.ToTicks(c, unit) : null;
            var error = XaeRuntime.Consume(item, TaskXml.BuildTaskDef(priority, ticks, autoStart, cores is { Length: > 0 } ? Affinity.FromCores(cores) : null, ioAtBegin));
            var after = TaskXml.Parse(item.ProduceXml(false));
            var warnings = new List<string>();
            if (ticks is { } t && after.CycleTime.Ticks100ns != t)
            {
                warnings.Add($"TwinCAT stored cycle time {after.CycleTime.Ticks100ns} ticks instead of {t} (must be a multiple of the base time, see rt_get_settings).");
            }

            if (priority is { } p && after.Priority != p)
            {
                warnings.Add($"TwinCAT stored priority {after.Priority} instead of {p} (priority already used by another task?).");
            }

            return new { task = after, xmlError = error, warnings };
        });

    [McpServerTool(Name = "task_delete", Destructive = true), Description("Deletes a real-time task below TIRT. Unlink PLC task references first (task_link_plc).")]
    public Task<CallToolResult> Delete([Description("Task name or 'TIRT^name'.")] string task)
        => session.RunAsync(s =>
        {
            var path = XaeRuntime.TaskPath(task);
            var item = s.Lookup(path);
            item.Parent.DeleteChild(item.Name);
            return new { deleted = path };
        });

    [McpServerTool(Name = "task_link_plc"), Description(
        "Links a PLC task reference (the task object inside the PLC project, e.g. 'TIPC^Plc1^Plc1 Project^PlcTask') to a real-time task " +
        "(ITcPlcTaskReference.LinkedTask). Example: plcTask='PlcTask', plcProject='Plc1', task='FastTask'.")]
    public Task<CallToolResult> LinkPlc(
        [Description("Real-time task name or path ('FastTask' / 'TIRT^FastTask').")] string task,
        [Description("PLC task reference name inside the nested PLC project (default 'PlcTask' of the Standard PLC template) or its full path.")] string plcTask = "PlcTask",
        [Description("PLC project name (e.g. 'Plc1'). Empty = first PLC project. Ignored when plcTask is a full path.")] string? plcProject = null)
        => session.RunAsync(s =>
        {
            var taskPath = XaeRuntime.TaskPath(task);
            s.Lookup(taskPath);
            var refPath = plcTask.Contains('^') ? plcTask : XaeRuntime.NestedProjectPath(s, plcProject ?? "") + "^" + plcTask;
            var item = s.Lookup(refPath);
            string? error = null;
            if (item is ITcPlcTaskReference reference)
            {
                reference.LinkedTask = taskPath;
            }
            else
            {
                error = XaeRuntime.Consume(item, TaskXml.BuildLinkedTask(taskPath));
            }

            return new { plcTask = refPath, linkedTask = TaskXml.ParseLinkedTask(item.ProduceXml(false)), xmlError = error };
        });

    private sealed record PlcTaskRef(string Path, string? LinkedTask);

    private static List<PlcTaskRef> PlcTaskReferences(XaeSession s)
    {
        var result = new List<PlcTaskRef>();
        foreach (var nested in XaeRuntime.NestedProjects(s))
        {
            foreach (var item in TreeInfo.Children(nested).Where(c => c.ItemType == PlcTaskItemType))
            {
                result.Add(new PlcTaskRef(item.PathName, XaeRuntime.Try(() => TaskXml.ParseLinkedTask(item.ProduceXml(false)))));
            }
        }

        return result;
    }
}
