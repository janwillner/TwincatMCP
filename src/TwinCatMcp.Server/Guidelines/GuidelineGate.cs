using TCatSysManagerLib;
using TwinCatMcp.Core;
using TwinCatMcp.Plc;

namespace TwinCatMcp.Guidelines;

/// <summary>
/// Connects the PLC write tools to the guidelines of the open solution (STA only). Without .twincat-mcp.json every
/// method is a no-op; with a broken config every method throws, so nothing is written.
/// </summary>
internal static class GuidelineGate
{
    private static readonly HashSet<string> MemberKinds = new(StringComparer.Ordinal)
    {
        "METHOD", "PROPERTY", "ACTION", "TRANSITION", "PROPERTY_GET", "PROPERTY_SET",
    };

    /// <summary>The guideline view of an existing tree item (name = a new name for renames).</summary>
    public static GuidelineObject ObjectOf(ITcSmTreeItem item, string? name = null)
    {
        var kind = PlcObjects.KindOf(PlcTree.TypeOf(item));
        var parent = MemberKinds.Contains(kind) ? PlcTree.Safe(() => item.Parent) : null;
        var path = name is null ? item.PathName : item.PathName[..(item.PathName.LastIndexOf('^') + 1)] + name;
        return new GuidelineObject(kind, name ?? item.Name, path, parent?.Name, parent is null ? null : PlcObjects.KindOf(PlcTree.TypeOf(parent)));
    }

    /// <summary>The guideline view of an object about to be created below parent.</summary>
    public static GuidelineObject NewObject(ITcSmTreeItem parent, int itemType, string name)
    {
        var kind = PlcObjects.KindOf(itemType);
        var member = MemberKinds.Contains(kind);
        return new GuidelineObject(kind, name, parent.PathName + "^" + name, member ? parent.Name : null,
            member ? PlcObjects.KindOf(PlcTree.TypeOf(parent)) : null);
    }

    /// <summary>Findings of a write to an existing object (only what is new or changed).</summary>
    public static IReadOnlyList<GuidelineViolation> Findings(XaeSession s, ITcSmTreeItem item, PouText before, PouText after)
        => s.Guidelines is { } g ? g.CheckText(ObjectOf(item), before, after) : [];

    /// <summary>Rejects the write on errors, reports warnings.</summary>
    public static void CheckWrite(XaeSession s, ITcSmTreeItem item, PouText before, PouText after)
        => s.Enforce(Findings(s, item, before, after));

    /// <summary>
    /// Before creating an object: applies the create fixes to the text the caller passed (null parts stay null), checks
    /// name and text, and rejects the create on errors. Returns the text to write.
    /// </summary>
    public static PouText PrepareCreate(XaeSession s, GuidelineObject obj, PouText given)
    {
        if (s.Guidelines is not { } g)
        {
            return given;
        }

        var result = g.PrepareCreate(obj, given, DateTime.Now);
        s.Enforce(result.Violations);
        s.RecordFixes(result.Applied);
        return result.Text;
    }

    /// <summary>After creating an object: applies the create fixes to the skeleton TwinCAT generated for the parts the caller did not pass.</summary>
    public static void FixSkeleton(XaeSession s, ITcSmTreeItem item, bool declarationGiven, bool implementationGiven)
    {
        if (s.Guidelines is not { } g || (declarationGiven && implementationGiven))
        {
            return;
        }

        var text = PlcTree.ReadText(item);
        var part = new PouText(declarationGiven ? null : text.Declaration, implementationGiven ? null : text.Implementation);
        var fix = g.CompleteCreate(ObjectOf(item), part, DateTime.Now);
        if (fix.Applied.Count == 0)
        {
            return;
        }

        PlcTree.WriteText(item,
            fix.Text.Declaration is { } d && d != text.Declaration ? PlcTree.MatchNewlines(text.Declaration, d) : null,
            fix.Text.Implementation is { } i && i != text.Implementation ? PlcTree.MatchNewlines(text.Implementation, i) : null);
        s.RecordFixes(fix.Applied);
        s.Enforce(fix.Violations);
    }

    /// <summary>Checks a new name before a rename; rejects it on errors.</summary>
    public static void CheckRename(XaeSession s, ITcSmTreeItem item, string newName)
    {
        if (s.Guidelines is not { } g || GuidelineSet.NameRuleOf(PlcObjects.KindOf(PlcTree.TypeOf(item))) is null)
        {
            return;
        }

        s.Enforce(g.CheckName(ObjectOf(item, newName), PlcTree.ReadText(item).Declaration));
    }
}
