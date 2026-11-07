using System.Runtime.InteropServices;
using TCatSysManagerLib;
using TwinCatMcp.Core;

namespace TwinCatMcp.Libraries;

/// <summary>
/// Resolves the nested PLC project and its library manager ("References" node) for the lib_/archive_ tools.
/// STA only (touches COM).
/// </summary>
public sealed class PlcLibraryContext
{
    private PlcLibraryContext(string plcName, ITcSmTreeItem nestedProject, ITcSmTreeItem references)
    {
        PlcName = plcName;
        NestedProject = nestedProject;
        ReferencesItem = references;
    }

    /// <summary>Name of the PLC project root below TIPC (e.g. "Plc1").</summary>
    public string PlcName { get; }

    /// <summary>The nested PLC project ("TIPC^Plc1^Plc1 Project"), implements ITcPlcIECProject*.</summary>
    public ITcSmTreeItem NestedProject { get; }

    /// <summary>The library manager node ("...^References").</summary>
    public ITcSmTreeItem ReferencesItem { get; }

    public ITcPlcLibraryManager2 Manager => (ITcPlcLibraryManager2)ReferencesItem;

    public string NestedProjectPath => NestedProject.PathName;

    public LibraryReferencesXml ReadReferences(bool includeDependencies = false)
        => LibraryReferencesXml.Parse(ReferencesItem.ProduceXml(false), includeDependencies);

    public const string PlcProjectParameterDescription =
        "PLC project to work on: PLC name ('Plc1'), its root path ('TIPC^Plc1'), the nested project path ('TIPC^Plc1^Plc1 Project') " +
        "or the References path. Empty = the only PLC project of the TwinCAT project.";

    /// <summary>
    /// Accepts null/empty (the single PLC project), a PLC name ("Plc1"), the root path ("TIPC^Plc1"),
    /// the nested project path ("TIPC^Plc1^Plc1 Project") or the References path.
    /// </summary>
    public static PlcLibraryContext Resolve(XaeSession s, string? plcProject)
    {
        ITcSmTreeItem nested;
        if (string.IsNullOrWhiteSpace(plcProject))
        {
            var all = NestedProjects(s).ToList();
            nested = all.Count switch
            {
                1 => all[0],
                0 => throw new TcException("The TwinCAT project contains no PLC project.")
                {
                    Hint = "Create one first (plc_create_project, or tree_create_child on 'TIPC' with info 'Standard PLC Template').",
                },
                _ => throw new TcException($"Several PLC projects exist: {string.Join(", ", all.Select(p => p.PathName))}.")
                {
                    Hint = "Pass plcProject, e.g. the PLC name.",
                },
            };
        }
        else
        {
            var path = plcProject.Contains('^') ? plcProject.Trim() : "TIPC^" + plcProject.Trim();
            var item = s.Lookup(path);
            if (item is ITcPlcLibraryManager)
            {
                item = item.Parent;
            }

            nested = item is ITcProjectRoot root ? root.NestedProject : item;
        }

        var references = TreeInfo.Children(nested).FirstOrDefault(c => c is ITcPlcLibraryManager)
                         ?? throw new TcException($"'{nested.PathName}' has no library manager (References) node.")
                         {
                             Hint = "Pass the PLC project (e.g. 'Plc1' or 'TIPC^Plc1^Plc1 Project').",
                         };
        var plcName = nested.Parent?.Name ?? nested.Name;
        return new PlcLibraryContext(plcName, nested, references);
    }

    /// <summary>Like <see cref="Resolve"/> but returns null instead of throwing when there is no usable PLC project.</summary>
    public static PlcLibraryContext? TryResolve(XaeSession s, string? plcProject)
    {
        try
        {
            return s.IsConnected && s.HasProject ? Resolve(s, plcProject) : null;
        }
        catch (TcException)
        {
            return null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    public static IEnumerable<ITcSmTreeItem> NestedProjects(XaeSession s)
    {
        foreach (var plc in TreeInfo.Children(s.Lookup("TIPC")))
        {
            ITcSmTreeItem? nested = null;
            try
            {
                nested = (plc as ITcProjectRoot)?.NestedProject;
            }
            catch (COMException)
            {
                // not a PLC project root
            }

            if (nested is not null)
            {
                yield return nested;
            }
        }
    }
}
