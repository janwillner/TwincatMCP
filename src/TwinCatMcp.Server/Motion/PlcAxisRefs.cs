using TCatSysManagerLib;
using TwinCatMcp.Core;

namespace TwinCatMcp.Motion;

public sealed record PlcAxisRef(string Symbol, string NcToPlcPath, string PlcToNcPath);

/// <summary>
/// Finds AXIS_REF variables in PLC project instances. After a build, an AXIS_REF 'GVL.axX' appears in the instance as
/// input item 'GVL.axX.NcToPlc' (PlcTask Inputs) and output item 'GVL.axX.PlcToNc' (PlcTask Outputs). STA thread only.
/// </summary>
public static class PlcAxisRefs
{
    public const int ItemTypePlcInstance = 57;
    private const string NcToPlc = ".NcToPlc";
    private const string PlcToNc = ".PlcToNc";

    public static IEnumerable<ITcSmTreeItem> Instances(XaeSession s, string? plc)
    {
        foreach (var project in TreeInfo.Children(s.Lookup("TIPC")))
        {
            foreach (var inst in TreeInfo.Children(project).Where(c => NcTree.Safe(() => c.ItemType) == ItemTypePlcInstance))
            {
                if (string.IsNullOrWhiteSpace(plc) ||
                    project.Name.Equals(plc, StringComparison.OrdinalIgnoreCase) ||
                    inst.PathName.Equals(plc, StringComparison.OrdinalIgnoreCase) ||
                    project.PathName.Equals(plc, StringComparison.OrdinalIgnoreCase))
                {
                    yield return inst;
                }
            }
        }
    }

    public static List<PlcAxisRef> Find(XaeSession s, string? plc)
    {
        var result = new List<PlcAxisRef>();
        foreach (var inst in Instances(s, plc))
        {
            var vars = Variables(inst).Select(v => (Name: NcTree.Safe(() => v.Name) ?? "", Path: NcTree.Safe(() => v.PathName) ?? "")).ToList();
            foreach (var nc in vars.Where(v => v.Name.EndsWith(NcToPlc, StringComparison.OrdinalIgnoreCase)))
            {
                var symbol = nc.Name[..^NcToPlc.Length];
                var plcToNc = vars.FirstOrDefault(v => v.Name.Equals(symbol + PlcToNc, StringComparison.OrdinalIgnoreCase));
                if (plcToNc.Path is { Length: > 0 })
                {
                    result.Add(new PlcAxisRef(symbol, nc.Path, plcToNc.Path));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Process image variables of an item. They are not tree children (ChildCount = 0 for a PLC instance) but are
    /// reachable via ITcSmTreeItem.Var[nInOut, n] (nInOut 0 = inputs, 1 = outputs), plus the children of tree children.
    /// </summary>
    public static IEnumerable<ITcSmTreeItem> Variables(ITcSmTreeItem item)
    {
        foreach (var io in new[] { 0, 1 })
        {
            var count = NcTree.Safe(() => item.get_VarCount(io));
            for (var i = 1; i <= count; i++)
            {
                var v = NcTree.Safe(() => item.get_Var(io, i));
                if (v is not null)
                {
                    yield return v;
                }
            }
        }

        foreach (var d in TreeInfo.Descendants(item, 3))
        {
            yield return d;
        }
    }

    public static PlcAxisRef Resolve(XaeSession s, string? plc, string symbol)
    {
        var wanted = symbol.Trim();
        if (wanted.EndsWith(NcToPlc, StringComparison.OrdinalIgnoreCase))
        {
            wanted = wanted[..^NcToPlc.Length];
        }

        var all = Find(s, plc);
        var matches = all.Where(r => r.Symbol.Equals(wanted, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new TcException($"PLC AXIS_REF '{symbol}' not found in the PLC instance I/O.")
            {
                Hint = all.Count == 0
                    ? "No AXIS_REF found in the PLC instance I/O. Declare e.g. 'axX : AXIS_REF;' (library Tc2_MC2), make sure it is used " +
                      "by code that is called (a GVL nobody references is not linked into the application), then build the PLC project (build_solution)."
                    : "Available: " + string.Join(", ", all.Select(r => r.Symbol)),
            },
            _ => throw new TcException($"'{symbol}' exists in several PLC projects.") { Hint = "Pass plc, e.g. one of: " + string.Join(", ", matches.Select(m => m.NcToPlcPath)) },
        };
    }
}
