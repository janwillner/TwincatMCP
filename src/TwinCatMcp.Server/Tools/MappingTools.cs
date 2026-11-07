using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TCatSysManagerLib;
using TwinCatMcp.Core;
using TwinCatMcp.Io;

namespace TwinCatMcp.Tools;

/// <summary>A pair of variables to link (PLC/NC/TcCOM variable on one side, I/O variable on the other).</summary>
public sealed class LinkRequest
{
    [Description("Full tree path of the first variable, e.g. 'TIPC^Plc1^Plc1 Instance^PlcTask Inputs^GVL_IO.bIn1'.")]
    public string A { get; set; } = "";

    [Description("Full tree path of the second variable, e.g. 'TIID^EtherCAT Master^Term 1 (EK1100)^Term 2 (EL1008)^Channel 1^Input'. " +
                 "For mapping_unlink it may be omitted to remove all links of A.")]
    public string? B { get; set; }

    [Description("Optional bit offset inside variable A (only for partial links of differently sized variables).")]
    public int? OffsetA { get; set; }

    [Description("Optional bit offset inside variable B.")]
    public int? OffsetB { get; set; }

    [Description("Optional link size in bits (only together with offsets).")]
    public int? BitSize { get; set; }
}

[McpServerToolType]
public sealed class MappingTools(XaeSession session)
{
    [McpServerTool(Name = "mapping_link"), Description(
        "Links variables (ITcSysManager.LinkVariables), e.g. a PLC input 'TIPC^Plc1^Plc1 Instance^PlcTask Inputs^GVL_IO.bIn1' to an " +
        "I/O channel 'TIID^EtherCAT Master^Term 1 (EK1100)^Term 2 (EL1008)^Channel 1^Input'. Batch: pass several pairs. " +
        "The PLC must have been built (build_solution) so its process image (PlcTask Inputs/Outputs) exists; PLC variables need " +
        "'AT %I*' (inputs) or 'AT %Q*' (outputs). Use io_list_variables to find valid paths. Each pair reports success/error individually.")]
    public Task<CallToolResult> Link(
        [Description("Pairs to link: [{a, b}] with full tree paths. Direction does not matter (input<->input, output<->output).")] LinkRequest[] links)
        => session.RunAsync(s => links.Select(l => (object)Try(l, () =>
        {
            Require(l.B, "b");
            if (l.OffsetA is not null || l.OffsetB is not null || l.BitSize is not null)
            {
                s.SysManager.LinkVariables(l.A, l.B!, l.OffsetA ?? 0, l.OffsetB ?? 0, l.BitSize ?? 0);
            }
            else
            {
                s.SysManager.LinkVariables(l.A, l.B!);
            }
        })).ToArray(), TimeSpan.FromMinutes(5));

    [McpServerTool(Name = "mapping_unlink", Destructive = true), Description(
        "Removes links (ITcSysManager.UnlinkVariables). Pass {a, b} to remove one link or only {a} to remove all links of variable a.")]
    public Task<CallToolResult> Unlink(
        [Description("Links to remove: [{a, b}] or [{a}].")] LinkRequest[] links)
        => session.RunAsync(s => links.Select(l => (object)Try(l, () =>
        {
            if (string.IsNullOrWhiteSpace(l.B))
            {
                s.SysManager.UnlinkVariables(l.A);
            }
            else
            {
                s.SysManager.UnlinkVariables(l.A, l.B);
            }
        })).ToArray(), TimeSpan.FromMinutes(5));

    [McpServerTool(Name = "mapping_list_links", ReadOnly = true), Description(
        "Lists all variable links of the project as a compact list of {a, b} full tree paths (parsed from ProduceMappingInfo). " +
        "Optionally filtered by a path substring (e.g. 'Term 2 (EL1008)' or 'Plc1 Instance').")]
    public Task<CallToolResult> ListLinks(
        [Description("Optional case-insensitive substring that either side of the link must contain.")] string? filter = null)
        => session.RunAsync(s =>
        {
            var links = MappingInfo.Parse(s.SysManager.ProduceMappingInfo())
                .Where(l => string.IsNullOrWhiteSpace(filter)
                            || l.A.Contains(filter, StringComparison.OrdinalIgnoreCase)
                            || l.B.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return new { count = links.Count, links };
        });

    [McpServerTool(Name = "mapping_produce", ReadOnly = true), Description(
        "Returns the raw mapping XML of the whole project (ITcSysManager.ProduceMappingInfo). Use mapping_list_links for a compact view; " +
        "the raw XML can be saved and restored later with mapping_consume.")]
    public Task<CallToolResult> Produce()
        => session.RunAsync(s => new { xml = s.SysManager.ProduceMappingInfo() });

    [McpServerTool(Name = "mapping_consume", Destructive = true), Description(
        "Applies mapping XML (ITcSysManager.ConsumeMappingInfo). WARNING: this REPLACES ALL existing links of the project with the given set. " +
        "Typical use: restore XML previously returned by mapping_produce. Format: <VarLinks><OwnerA Name=\"TIPC^Plc1^Plc1 Instance\">" +
        "<OwnerB Name=\"TIID^EtherCAT Master^Term 1 (EK1100)^Term 3 (EL2008)\"><Link VarA=\"PlcTask Outputs^GVL_IO.bOut1\" VarB=\"Channel 1^Output\"/>" +
        "</OwnerB></OwnerA></VarLinks>.")]
    public Task<CallToolResult> Consume(
        [Description("Complete mapping XML (all links that should exist afterwards).")] string xml)
        => session.RunAsync(s =>
        {
            s.SysManager.ConsumeMappingInfo(xml);
            return new { applied = true, linkCount = MappingInfo.Parse(s.SysManager.ProduceMappingInfo()).Count };
        });

    [McpServerTool(Name = "mapping_clear", Destructive = true), Description(
        "Deletes ALL variable links of the project (ITcSysManager.ClearMappingInfo). Consider mapping_produce first to keep a backup.")]
    public Task<CallToolResult> Clear()
        => session.RunAsync(s =>
        {
            var before = MappingInfo.Parse(s.SysManager.ProduceMappingInfo()).Count;
            s.SysManager.ClearMappingInfo();
            return new { cleared = true, removedLinks = before };
        });

    [McpServerTool(Name = "mapping_check", ReadOnly = true), Description(
        "Checks all links: verifies that both ends of every link still exist in the tree and reports bit size mismatches " +
        "(e.g. after deleting/renaming terminals or PLC variables). Optionally also runs the XAE commands " +
        "'Check variable links' / 'Generate mappings' (results go to the XAE output window).")]
    public Task<CallToolResult> Check(
        [Description("Also execute the XAE commands CheckVariableLinks and GenerateMappings (ITcSmCommands).")] bool runXaeCommands = false)
        => session.RunAsync(s =>
        {
            var links = MappingInfo.Parse(s.SysManager.ProduceMappingInfo());
            var problems = new List<object>();
            foreach (var l in links)
            {
                var aExists = s.SysManager.TestItemPath(l.A);
                var bExists = s.SysManager.TestItemPath(l.B);
                if (!aExists || !bExists)
                {
                    problems.Add(new { l.A, l.B, problem = !aExists && !bExists ? "both ends missing" : !aExists ? "a missing" : "b missing" });
                    continue;
                }

                var sa = IoTreeHelpers.TryGetVarInfo(s.Lookup(l.A));
                var sb = IoTreeHelpers.TryGetVarInfo(s.Lookup(l.B));
                if (sa is not null && sb is not null && l.BitSize is null && !IoXml.AreLinkCompatible(sa, sb))
                {
                    problems.Add(new { l.A, l.B, problem = $"bit size mismatch ({sa.Type}:{sa.BitSize} vs {sb.Type}:{sb.BitSize})" });
                }
            }

            object? xaeCommands = null;
            if (runXaeCommands)
            {
                xaeCommands = IoTreeHelpers.RunSmCommands(s, ("CheckVariableLinks", c => c.CheckVariableLinks()), ("GenerateMappings", c => c.GenerateMappings()));
            }

            return new { linkCount = links.Count, ok = problems.Count == 0, problems, xaeCommands };
        }, TimeSpan.FromMinutes(5));

    [McpServerTool(Name = "mapping_auto_link"), Description(
        "Links several PLC variables to I/O channels in one call using a name map. Paths are relative: plcVariable is searched in the " +
        "process image of the PLC instance (e.g. 'GVL_IO.bIn1' under 'PlcTask Inputs'/'PlcTask Outputs'), ioVariable is relative to " +
        "ioParent (e.g. 'Term 2 (EL1008)^Channel 1^Input' under 'TIID^EtherCAT Master^Term 1 (EK1100)'). Example map: " +
        "{\"GVL_IO.bIn1\": \"Term 2 (EL1008)^Channel 1^Input\", \"GVL_IO.bOut1\": \"Term 3 (EL2008)^Channel 1^Output\"}.")]
    public Task<CallToolResult> AutoLink(
        [Description("PLC instance path, e.g. 'TIPC^Plc1^Plc1 Instance'.")] string plcInstance,
        [Description("Common I/O parent path, e.g. 'TIID^EtherCAT Master^Term 1 (EK1100)' or 'TIID^EtherCAT Master'.")] string ioParent,
        [Description("Map of PLC variable (e.g. 'GVL_IO.bIn1') to I/O variable path relative to ioParent (e.g. 'Term 2 (EL1008)^Channel 1^Input').")]
        Dictionary<string, string> map)
        => session.RunAsync(s =>
        {
            var instance = s.Lookup(plcInstance);
            var plcVars = IoTreeHelpers.EnumerateVariables(instance, includeSubBoxes: false, max: 100_000)
                .ToDictionary(v => v.Name, v => v.Path, StringComparer.OrdinalIgnoreCase);
            return map.Select(kv =>
            {
                var a = kv.Key.Contains('^') ? kv.Key : plcVars.GetValueOrDefault(kv.Key);
                var b = ioParent.TrimEnd('^') + "^" + kv.Value.TrimStart('^');
                if (a is null)
                {
                    return (object)new { a = kv.Key, b, ok = false, error = $"PLC variable '{kv.Key}' not found in the process image of '{plcInstance}'. Build the PLC and declare it with AT %I*/%Q*." };
                }

                return Try(new LinkRequest { A = a, B = b }, () => s.SysManager.LinkVariables(a, b));
            }).ToArray();
        }, TimeSpan.FromMinutes(5));

    private static object Try(LinkRequest l, Action action)
    {
        try
        {
            action();
            return new { a = l.A, b = l.B, ok = true };
        }
        catch (Exception ex)
        {
            return new { a = l.A, b = l.B, ok = false, error = ex.Message };
        }
    }

    private static void Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new TcException($"'{name}' must not be empty.");
        }
    }
}
