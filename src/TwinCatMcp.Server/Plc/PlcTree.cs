using TCatSysManagerLib;
using TwinCatMcp.Core;

namespace TwinCatMcp.Plc;

/// <summary>A PLC project: root node (TIPC^Name, ITcPlcProject), nested project (ITcPlcIECProject) and instance.</summary>
public sealed record PlcProjectRef(string Name, ITcSmTreeItem Root, ITcSmTreeItem Nested, ITcSmTreeItem? Instance)
{
    public string RootPath => Root.PathName;

    public string ProjectPath => Nested.PathName;

    public string? InstancePath => Instance?.PathName;
}

/// <summary>COM-side helpers for PLC projects and PLC objects. STA thread only.</summary>
public static class PlcTree
{
    // ------------------------------------------------------------------ projects

    public static List<PlcProjectRef> Projects(XaeSession s)
    {
        var tipc = s.Lookup("TIPC");
        var list = new List<PlcProjectRef>();
        foreach (var child in TreeInfo.Children(tipc))
        {
            var nested = NestedOf(child);
            if (nested is null)
            {
                continue;
            }

            var instance = TreeInfo.Children(child).FirstOrDefault(c => Safe(() => c.ItemType) == PlcObjects.ProjectInstance);
            list.Add(new PlcProjectRef(child.Name, child, nested, instance));
        }

        return list;
    }

    private static ITcSmTreeItem? NestedOf(ITcSmTreeItem root)
    {
        try
        {
            if (root is ITcProjectRoot pr && pr.NestedProject is { } n)
            {
                return n;
            }
        }
        catch
        {
            // not a PLC project root (e.g. C++ or PLC2 project)
        }

        return null;
    }

    /// <summary>
    /// Resolves a PLC project by name ("Plc1"), root path ("TIPC^Plc1"), nested path ("TIPC^Plc1^Plc1 Project") or any
    /// path below it. Empty = the only PLC project of the solution.
    /// </summary>
    public static PlcProjectRef ResolveProject(XaeSession s, string? project)
    {
        var all = Projects(s);
        if (all.Count == 0)
        {
            throw new TcException("The TwinCAT project contains no PLC project.") { Hint = "Create one with plc_create_project." };
        }

        if (string.IsNullOrWhiteSpace(project))
        {
            return all.Count == 1
                ? all[0]
                : throw new TcException($"Several PLC projects exist ({string.Join(", ", all.Select(p => p.Name))}).")
                {
                    Hint = "Pass the project name or path.",
                };
        }

        var p = project.Trim();
        var match = all.FirstOrDefault(x => string.Equals(x.Name, p, StringComparison.OrdinalIgnoreCase)
                                            || string.Equals(x.RootPath, p, StringComparison.OrdinalIgnoreCase)
                                            || string.Equals(x.ProjectPath, p, StringComparison.OrdinalIgnoreCase))
                    ?? all.FirstOrDefault(x => p.StartsWith(x.RootPath + "^", StringComparison.OrdinalIgnoreCase));
        return match ?? throw new TcException($"PLC project '{project}' not found.")
        {
            Hint = "Known PLC projects: " + string.Join(", ", all.Select(x => $"{x.Name} ({x.ProjectPath})")),
        };
    }

    /// <summary>The PLC project that contains <paramref name="itemPath"/>.</summary>
    public static PlcProjectRef ProjectOf(XaeSession s, string itemPath)
        => Projects(s).FirstOrDefault(p => itemPath.StartsWith(p.RootPath + "^", StringComparison.OrdinalIgnoreCase)
                                           || string.Equals(itemPath, p.RootPath, StringComparison.OrdinalIgnoreCase))
           ?? throw new TcException($"'{itemPath}' is not inside a PLC project.");

    // ------------------------------------------------------------------ items

    /// <summary>
    /// Looks up a PLC object. Accepts full tree paths ("TIPC^Plc1^Plc1 Project^POUs^MAIN") and, when unambiguous,
    /// paths relative to a nested PLC project ("POUs^MAIN").
    /// </summary>
    public static ITcSmTreeItem ResolveItem(XaeSession s, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new TcException("path must not be empty.");
        }

        path = path.Trim().Trim('^');
        if (s.SysManager.TryLookupTreeItem(path, out var item) && item is not null)
        {
            return item;
        }

        if (!path.StartsWith("TI", StringComparison.Ordinal))
        {
            var hits = new List<ITcSmTreeItem>();
            foreach (var p in Projects(s))
            {
                if (s.SysManager.TryLookupTreeItem(p.ProjectPath + "^" + path, out var rel) && rel is not null)
                {
                    hits.Add(rel);
                }
            }

            if (hits.Count == 1)
            {
                return hits[0];
            }

            if (hits.Count > 1)
            {
                throw new TcException($"'{path}' exists in several PLC projects.") { Hint = "Use the full tree path (TIPC^<Plc>^<Plc> Project^...)." };
            }
        }

        return s.Lookup(path); // throws the standard not-found error with hint
    }

    public static int TypeOf(ITcSmTreeItem item) => Safe(() => item.ItemType);

    public static object Describe(ITcSmTreeItem item)
    {
        var type = TypeOf(item);
        return new { name = item.Name, path = item.PathName, kind = PlcObjects.KindOf(type), itemType = type };
    }

    /// <summary>All PLC objects below the nested project (library references are skipped).</summary>
    public static IEnumerable<ITcSmTreeItem> Elements(ITcSmTreeItem nested)
    {
        var stack = new Stack<ITcSmTreeItem>(TreeInfo.Children(nested).Reverse());
        while (stack.Count > 0)
        {
            var item = stack.Pop();
            var type = TypeOf(item);
            if (type == PlcObjects.LibraryManager)
            {
                continue;
            }

            yield return item;
            foreach (var c in TreeInfo.Children(item).Reverse())
            {
                stack.Push(c);
            }
        }
    }

    // ------------------------------------------------------------------ code

    public static PouText ReadText(ITcSmTreeItem item)
    {
        // Accessors of interface properties have no code. Touching DeclarationText/ImplementationText/Language on them
        // crashes TcXaeShell 4026 (RPC_S_SERVER_UNAVAILABLE), so they are never read.
        if (HasNoCode(item))
        {
            return new PouText(null, null);
        }

        string? decl = null;
        string? impl = null;
        if (item is ITcPlcDeclaration d)
        {
            decl = Safe(() => d.DeclarationText);
        }

        if (item is ITcPlcImplementation i)
        {
            impl = Safe(() => i.ImplementationText);
        }

        // Some objects expose an interface but have no such area (e.g. a property node has no implementation).
        var type = TypeOf(item);
        if (type is PlcObjects.Action or PlcObjects.Transition)
        {
            decl = null;
        }

        if (type is PlcObjects.Struct or PlcObjects.Enum or PlcObjects.Union or PlcObjects.Alias or PlcObjects.Gvl
            or PlcObjects.ParameterList or PlcObjects.Interface or PlcObjects.InterfaceMethod
            or PlcObjects.Property or PlcObjects.InterfaceProperty or PlcObjects.InterfacePropertyGet or PlcObjects.InterfacePropertySet)
        {
            impl = null;
        }

        return new PouText(decl, impl);
    }

    private static bool HasNoCode(ITcSmTreeItem item)
        => TypeOf(item) is PlcObjects.InterfacePropertyGet or PlcObjects.InterfacePropertySet;

    public static string? LanguageOf(ITcSmTreeItem item)
    {
        if (!HasNoCode(item) && item is ITcPlcImplementation i)
        {
            try
            {
                return PlcObjects.LanguageName((int)i.Language);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    public static void WriteText(ITcSmTreeItem item, string? declaration, string? implementation)
    {
        var current = ReadText(item);
        if (declaration is not null)
        {
            if (current.Declaration is null || item is not ITcPlcDeclaration)
            {
                throw new TcException($"'{item.PathName}' ({PlcObjects.KindOf(TypeOf(item))}) has no declaration area.");
            }

            if (StDeclaration.HeaderError(TypeOf(item), declaration) is { } headerError)
            {
                throw new TcException(headerError) { Hint = "Nothing was written. Re-read the object with pou_read_code and keep its header line." };
            }
        }

        if (implementation is not null && (current.Implementation is null || item is not ITcPlcImplementation))
        {
            throw new TcException($"'{item.PathName}' ({PlcObjects.KindOf(TypeOf(item))}) has no implementation area.")
            {
                Hint = "DUTs, GVLs, interfaces and property nodes have no implementation. For properties write the Get/Set children.",
            };
        }

        if (declaration is not null)
        {
            ((ITcPlcDeclaration)item).DeclarationText = declaration;
        }

        if (implementation is not null)
        {
            ((ITcPlcImplementation)item).ImplementationText = implementation;
        }
    }

    /// <summary>Keeps the line-ending style of the object (TwinCAT uses CRLF) for text supplied with LF only.</summary>
    public static string MatchNewlines(string? existing, string text)
    {
        var n = StCode.Normalize(text);
        return existing is null || StCode.UsesCrLf(existing) || !existing.Contains('\n') ? StCode.ToCrLf(n) : n;
    }

    public static T Safe<T>(Func<T> f)
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
