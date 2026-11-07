using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TCatSysManagerLib;
using TwinCatMcp.Core;
using TwinCatMcp.Motion;

namespace TwinCatMcp.Tools;

/// <summary>
/// NC motion configuration (TINC): NC task, axes, axis parameters, simulation, link to I/O drives and PLC AXIS_REF.
/// Everything here is offline configuration; nothing is activated.
/// </summary>
[McpServerToolType]
public sealed class MotionTools(XaeSession session)
{
    // ------------------------------------------------------------------ NC task

    [McpServerTool(Name = "motion_create_nc_task"), Description(
        "Creates an NC task below MOTION (TINC). TwinCAT appends ' SAF' to the name (e.g. 'NC-Task 1' -> 'TINC^NC-Task 1 SAF') and " +
        "creates the 'Axes' folder; the SVB task is created implicitly (cycle 10 ms). Default SAF cycle time is 2000 us, priority 4. " +
        "A project can contain only one NC task. Next step: motion_add_axis.")]
    public Task<CallToolResult> CreateNcTask(
        [Description("Task name without ' SAF', e.g. 'NC-Task 1'.")] string name = "NC-Task 1",
        [Description("SAF cycle time in microseconds, e.g. 2000 (= 2 ms). Optional.")] int? cycleTimeUs = null,
        [Description("SAF task priority (lower = higher priority), e.g. 4. Optional.")] int? priority = null)
        => session.RunAsync(s =>
        {
            var tinc = s.Lookup("TINC");
            if (NcTree.NcTasks(s).FirstOrDefault() is { } existing)
            {
                // TwinCAT allows only one NC (PTP) task per project: CreateChild fails with "Item already exists!".
                throw new TcException($"The project already has an NC task: '{existing.PathName}'. Only one NC task is allowed.")
                {
                    Hint = "Add axes to it with motion_add_axis; change cycle time/priority with motion_set_nc_task.",
                };
            }

            var requested = name.EndsWith(" SAF", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
            var task = tinc.CreateChild(requested, 1, "", Type.Missing);
            if (NcTaskXml.BuildUpdate(cycleTimeUs, priority) is { } xml)
            {
                task.ConsumeXml(xml);
            }

            return new { task = TaskView(task) };
        });

    [McpServerTool(Name = "motion_set_nc_task"), Description(
        "Changes cycle time and/or priority of an NC SAF task. Example: ncTask='NC-Task 1 SAF', cycleTimeUs=1000.")]
    public Task<CallToolResult> SetNcTask(
        [Description("NC task name or path; optional when the project has exactly one NC task.")] string? ncTask = null,
        [Description("SAF cycle time in microseconds.")] int? cycleTimeUs = null,
        [Description("SAF task priority.")] int? priority = null)
        => session.RunAsync(s =>
        {
            var task = NcTree.ResolveNcTask(s, ncTask);
            var xml = NcTaskXml.BuildUpdate(cycleTimeUs, priority) ?? throw new TcException("Nothing to change: pass cycleTimeUs and/or priority.");
            task.ConsumeXml(xml);
            return new { task = TaskView(task), lastXmlError = NcTree.LastXmlError(task) };
        });

    // ------------------------------------------------------------------ axes

    [McpServerTool(Name = "motion_list_axes", ReadOnly = true), Description(
        "Lists all NC tasks with their axes: axis type, encoder/drive/controller sub types, I/O link and object id. " +
        "Use it to discover axis paths for the other motion_ tools.")]
    public Task<CallToolResult> ListAxes(
        [Description("Restrict to one NC task (name or path). Optional.")] string? ncTask = null)
        => session.RunAsync(s =>
        {
            var tasks = string.IsNullOrWhiteSpace(ncTask) ? NcTree.NcTasks(s).ToList() : [NcTree.ResolveNcTask(s, ncTask)];
            return new
            {
                ncTasks = tasks.Select(t => new
                {
                    task = TaskView(t),
                    axes = NcTree.Axes(t).Select(NcTree.DescribeAxis).ToArray(),
                }).ToArray(),
            };
        });

    [McpServerTool(Name = "motion_add_axis"), Description(
        "Adds one or more NC axes to an NC task. A new axis has a simulation encoder and is not linked to I/O, i.e. it can be " +
        "moved without hardware. axisType: continuous (default, servo/stepper via drive), discrete (two speed), stepper " +
        "(low cost stepper, digital I/O), encoder (encoder only), hydraulic, timeGenerator - or the numeric sub type. " +
        "Example: names=['X','Y'], axisType='continuous'.")]
    public Task<CallToolResult> AddAxis(
        [Description("Axis names, e.g. ['Axis 1'] or ['X','Y','Z'].")] string[] names,
        [Description("NC task name or path; optional when the project has exactly one NC task.")] string? ncTask = null,
        [Description("continuous | discrete | stepper | encoder | hydraulic | timeGenerator | numeric sub type.")] string axisType = "continuous")
        => session.RunAsync(s =>
        {
            var task = NcTree.ResolveNcTask(s, ncTask);
            var axes = NcTree.AxesFolder(s, task);
            var subType = NcTree.ParseAxisType(axisType);
            var created = new List<object>();
            foreach (var n in names)
            {
                var axis = axes.CreateChild(n, subType, "", Type.Missing);
                created.Add(NcTree.DescribeAxis(axis));
            }

            return new { created };
        }, TimeSpan.FromMinutes(2));

    [McpServerTool(Name = "motion_remove_axis", Destructive = true), Description(
        "Deletes NC axes (and their variable links). Accepts full paths or unique axis names.")]
    public Task<CallToolResult> RemoveAxis([Description("Axis paths or names.")] string[] axes)
        => session.RunAsync(s =>
        {
            var removed = new List<string>();
            foreach (var a in axes)
            {
                var axis = NcTree.ResolveAxis(s, a);
                var path = axis.PathName;
                axis.Parent.DeleteChild(axis.Name);
                removed.Add(path);
            }

            return new { removed };
        });

    // ------------------------------------------------------------------ parameters

    [McpServerTool(Name = "motion_get_axis_parameters", ReadOnly = true), Description(
        "Returns the most important axis parameters in compact, LLM-readable form (units: position unit 'unit', s): " +
        "maxVelocity, acceleration, deceleration, jerk, manual/homing velocities, scalingFactor [unit/INC], encoderMode, " +
        "moduloFactor, software limits, position lag monitoring, referenceVelocity, positionControlKp, target position monitoring. " +
        "Use motion_set_axis_parameters with the same names to change them.")]
    public Task<CallToolResult> GetAxisParameters(
        [Description("Axis paths or names.")] string[] axes,
        [Description("Also read the 4026 'Simulation' flag (needs an .xti export, slower).")] bool includeSimulationMode = false)
        => session.RunAsync(s => axes.Select(a =>
        {
            var axis = NcTree.ResolveAxis(s, a);
            var xml = axis.ProduceXml(false);
            return new
            {
                axis = NcTree.DescribeAxis(axis),
                simulationMode = includeSimulationMode ? AxisSimulation.Get(axis) : (bool?)null,
                parameters = AxisXml.ReadParameters(xml),
            };
        }).ToArray());

    [McpServerTool(Name = "motion_set_axis_parameters"), Description(
        "Changes axis parameters (partial update via ConsumeXml - only the given ones are touched). Names as returned by " +
        "motion_get_axis_parameters, e.g. {\"maxVelocity\": 500, \"acceleration\": 2000, \"deceleration\": 2000, \"jerk\": 20000, " +
        "\"scalingFactor\": 0.0001, \"softLimitMaxEnabled\": true, \"softLimitMax\": 800, \"positionLagMax\": 2}. " +
        "Instead of scalingFactor you may pass feedPerRevolution + incrementsPerRevolution (scalingFactor = feed / increments). " +
        "Returns the values read back after the update; 'notApplied' lists values TwinCAT did not accept.")]
    public Task<CallToolResult> SetAxisParameters(
        [Description("Axis path or name.")] string axis,
        [Description("Parameter name -> value. See motion_get_axis_parameters for names.")] Dictionary<string, JsonElement>? parameters = null,
        [Description("Feed per motor/encoder revolution in position units, e.g. 10 (mm). Use with incrementsPerRevolution.")] double? feedPerRevolution = null,
        [Description("Encoder increments per revolution, e.g. 65536 or 1048576.")] double? incrementsPerRevolution = null)
        => session.RunAsync(s =>
        {
            var changes = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in parameters ?? [])
            {
                changes[k] = v;
            }

            if (feedPerRevolution is not null || incrementsPerRevolution is not null)
            {
                if (feedPerRevolution is null || incrementsPerRevolution is null)
                {
                    throw new TcException("Pass both feedPerRevolution and incrementsPerRevolution.");
                }

                changes["scalingFactor"] = MotionUnits.ScalingFactor(feedPerRevolution.Value, incrementsPerRevolution.Value);
            }

            var xml = AxisXml.BuildParameterUpdate(changes);
            var item = NcTree.ResolveAxis(s, axis);
            item.ConsumeXml(xml);
            var after = AxisXml.ReadParameters(item.ProduceXml(false));
            var applied = new Dictionary<string, object?>();
            var notApplied = new Dictionary<string, object?>();
            foreach (var (k, v) in changes)
            {
                var def = AxisXml.Find(k);
                var expected = AxisXml.FormatValue(def, v);
                var actual = after[def.Name];
                var actualText = actual switch
                {
                    null => null,
                    bool b => b ? "true" : "false",
                    double d => MotionUnits.FormatNumber(d),
                    _ => actual.ToString(),
                };
                if (string.Equals(actualText, expected, StringComparison.Ordinal))
                {
                    applied[def.Name] = actual;
                }
                else
                {
                    notApplied[def.Name] = new { requested = expected, actual };
                }
            }

            return new
            {
                axis = item.PathName,
                applied,
                notApplied = notApplied.Count > 0 ? notApplied : null,
                lastXmlError = NcTree.LastXmlError(item),
            };
        });

    [McpServerTool(Name = "motion_get_axis_xml", ReadOnly = true), Description(
        "Returns the raw NcAxisDef XML of an axis (ProduceXml) for parameters not covered by motion_get_axis_parameters. " +
        "section: full (default) | AxisParam | Encoder | Drive | Controller. Change raw values with tree_consume_xml.")]
    public Task<CallToolResult> GetAxisXml(
        [Description("Axis path or name.")] string axis,
        [Description("full | AxisParam | Encoder | Drive | Controller")] string section = "full")
        => session.RunAsync(s =>
        {
            var item = NcTree.ResolveAxis(s, axis);
            return new { axis = item.PathName, section, xml = AxisXml.ExtractSection(item.ProduceXml(false), section) };
        });

    // ------------------------------------------------------------------ simulation

    [McpServerTool(Name = "motion_set_axis_simulation"), Description(
        "Switches the TwinCAT 4026 axis 'Simulation' mode (Settings tab) on or off. In simulation mode the I/O link is kept but " +
        "ignored and a simulated drive + encoder are used, so a configured axis can run without hardware. Implemented by exporting " +
        "the axis (.xti), setting SimulationMode and re-importing it in place with links reconnected. " +
        "Note: an axis that was never linked to I/O already runs simulated (simulation encoder, no drive).")]
    public Task<CallToolResult> SetAxisSimulation(
        [Description("Axis paths or names.")] string[] axes,
        [Description("true = simulation on, false = off.")] bool enabled = true)
        => session.RunAsync(s => new
        {
            axes = axes.Select(a =>
            {
                var axis = NcTree.ResolveAxis(s, a);
                var (item, changed) = AxisSimulation.Set(axis, enabled);
                var mapping = s.SysManager.ProduceMappingInfo();
                return new
                {
                    axis = item.PathName,
                    simulationMode = AxisSimulation.Get(item),
                    changed,
                    ioLinkCount = NcTree.CountLinks(mapping, item.PathName, "TIID"),
                    plcLinkCount = NcTree.CountLinks(mapping, item.PathName, "TIPC"),
                };
            }).ToArray(),
        }, TimeSpan.FromMinutes(3));

    // ------------------------------------------------------------------ links

    [McpServerTool(Name = "motion_link_axis_to_drive"), Description(
        "Links an NC axis to an I/O drive box/terminal (like 'Link To I/O...' in XAE), e.g. an EL7041 stepper terminal " +
        "'TIID^Device 1 (EtherCAT)^Term 1 (EK1100)^Term 2 (EL7041)' or an AX5000 servo drive. TwinCAT then selects matching " +
        "encoder/drive sub types (e.g. EL7041 -> encoder MDP 511, drive MDP 703; AX5000 -> SoE/Sercos drive) and creates all " +
        "process data links. Works offline. channel: drive channel for multi-axis devices (0 = first, AX5203 has 0 and 1). " +
        "Pass ioPath='' to remove the I/O link.")]
    public Task<CallToolResult> LinkAxisToDrive(
        [Description("Axis path or name.")] string axis,
        [Description("Tree path of the drive box/terminal below TIID, or '' to unlink.")] string ioPath,
        [Description("Channel of multi-channel drives (0-based).")] int channel = 0,
        [Description("Optional axis I/O type, e.g. 'Sercos' for SoE drives (AX5000). Default: detected by TwinCAT; if that fails 'Sercos' is tried.")] string? axisIoType = null)
        => session.RunAsync(s =>
        {
            var item = NcTree.ResolveAxis(s, axis);
            var unlink = string.IsNullOrWhiteSpace(ioPath);
            if (!unlink)
            {
                s.Lookup(ioPath); // nice error if the I/O path is wrong
            }

            item.ConsumeXml(AxisXml.BuildIoLink(unlink ? "" : ioPath, channel, unlink ? null : axisIoType));
            var summary = AxisXml.ReadSummary(item.ProduceXml(false));
            if (!unlink && summary.IoLink is null && string.IsNullOrWhiteSpace(axisIoType))
            {
                // SoE drives (AX5000) are not detected automatically; they need AxisIoType 'Sercos'.
                item.ConsumeXml(AxisXml.BuildIoLink(ioPath, channel, "Sercos"));
                summary = AxisXml.ReadSummary(item.ProduceXml(false));
            }

            var mapping = s.SysManager.ProduceMappingInfo();
            var links = NcTree.CountLinks(mapping, item.PathName, "TIID");
            if (!unlink && summary.IoLink is null)
            {
                throw new TcException($"TwinCAT did not link '{item.PathName}' to '{ioPath}'.")
                {
                    Hint = "The target must be a drive box/terminal that supports NC axes (e.g. EL70x1, EL72xx, AX5xxx, AX8xxx) and channel must exist. " +
                           (NcTree.LastXmlError(item) ?? ""),
                };
            }

            return new { axis = NcTree.DescribeAxis(item), ioLinkCount = links };
        });

    [McpServerTool(Name = "motion_find_plc_axis_refs", ReadOnly = true), Description(
        "Lists PLC AXIS_REF variables that can be linked to NC axes (PLC instance I/O items '<symbol>.NcToPlc' / '.PlcToNc'). " +
        "The PLC project must have been built (build_solution) after declaring the AXIS_REF (library Tc2_MC2).")]
    public Task<CallToolResult> FindPlcAxisRefs(
        [Description("PLC project name or instance path (e.g. 'Plc1' or 'TIPC^Plc1^Plc1 Instance'). Optional = all PLC projects.")] string? plc = null)
        => session.RunAsync(s => new { axisRefs = PlcAxisRefs.Find(s, plc).Select(r => new { symbol = r.Symbol, ncToPlc = r.NcToPlcPath, plcToNc = r.PlcToNcPath }).ToArray() });

    [McpServerTool(Name = "motion_link_axis_to_plc"), Description(
        "Links an NC axis to a PLC AXIS_REF variable (like 'Link To PLC...' in XAE): axis 'Outputs^ToPlc' <-> '<symbol>.NcToPlc' " +
        "and axis 'Inputs^FromPlc' <-> '<symbol>.PlcToNc'. plcAxisRef is the symbol as shown in the PLC instance, e.g. " +
        "'GVL_Motion.axX' or 'MAIN.fbAxis.Axis' (see motion_find_plc_axis_refs). Build the PLC project first.")]
    public Task<CallToolResult> LinkAxisToPlc(
        [Description("Axis path or name.")] string axis,
        [Description("AXIS_REF symbol, e.g. 'GVL_Motion.axX'.")] string plcAxisRef,
        [Description("PLC project name or instance path; optional when only one PLC project matches.")] string? plc = null)
        => session.RunAsync(s =>
        {
            var item = NcTree.ResolveAxis(s, axis);
            var r = PlcAxisRefs.Resolve(s, plc, plcAxisRef);
            var toPlc = item.PathName + "^Outputs^ToPlc";
            var fromPlc = item.PathName + "^Inputs^FromPlc";
            s.SysManager.LinkVariables(toPlc, r.NcToPlcPath);
            s.SysManager.LinkVariables(fromPlc, r.PlcToNcPath);
            return new
            {
                axis = item.PathName,
                plcAxisRef = r.Symbol,
                links = new[] { new[] { toPlc, r.NcToPlcPath }, new[] { fromPlc, r.PlcToNcPath } },
                linkCount = NcTree.CountLinks(s.SysManager.ProduceMappingInfo(), item.PathName, "TIPC"),
            };
        });

    [McpServerTool(Name = "motion_unlink_axis_from_plc", Destructive = true), Description("Removes the links between an NC axis and its PLC AXIS_REF.")]
    public Task<CallToolResult> UnlinkAxisFromPlc([Description("Axis path or name.")] string axis)
        => session.RunAsync(s =>
        {
            var item = NcTree.ResolveAxis(s, axis);
            s.SysManager.UnlinkVariables(item.PathName + "^Outputs^ToPlc");
            s.SysManager.UnlinkVariables(item.PathName + "^Inputs^FromPlc");
            return new { axis = item.PathName, linkCount = NcTree.CountLinks(s.SysManager.ProduceMappingInfo(), item.PathName, "TIPC") };
        });

    private static object TaskView(TCatSysManagerLib.ITcSmTreeItem task)
    {
        var t = NcTaskXml.Read(task.ProduceXml(false));
        return new
        {
            name = t.Name,
            path = t.Path,
            cycleTimeUs = t.CycleTimeUs,
            priority = t.Priority,
            amsPort = t.AmsPort,
            objectId = t.ObjectId is { } o ? MotionUnits.FormatHex(o) : null,
            axesPath = t.Path + "^Axes",
        };
    }
}
