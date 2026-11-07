using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TCatSysManagerLib;
using TwinCatMcp.Core;
using TwinCatMcp.Plc;

namespace TwinCatMcp.Tools;

/// <summary>One edit of pou_multi_replace.</summary>
public sealed record PouEdit(
    [property: Description("Tree path of the POU/method/property accessor/DUT/GVL, e.g. 'TIPC^Plc1^Plc1 Project^POUs^MAIN'.")] string Path,
    [property: Description("Exact text to replace (copy it from pou_read_code incl. indentation). Must be unique unless replaceAll.")] string OldText,
    [property: Description("Replacement text.")] string NewText,
    [property: Description("'declaration', 'implementation' or empty/auto (the area that contains oldText).")] string? Section = null,
    [property: Description("Replace every occurrence instead of failing on ambiguity.")] bool ReplaceAll = false);

/// <summary>PLC object level tools (prefix pou_): read/write/replace/grep code and create/delete/rename/move objects.</summary>
[McpServerToolType]
public sealed class PouTools(XaeSession session)
{
    private const string PathParam =
        "Tree path of the object, e.g. 'TIPC^Plc1^Plc1 Project^POUs^FB_Motor' or 'TIPC^Plc1^Plc1 Project^POUs^FB_Motor^M_Start'. " +
        "Relative paths ('POUs^MAIN') work when only one PLC project exists.";

    private const string ProjectParam = "PLC project (name or path) used for the default parent folder. Empty = the only PLC project.";

    // ------------------------------------------------------------------ read

    [McpServerTool(Name = "pou_read_code", ReadOnly = true), Description(
        "Reads declaration + implementation + language of one or more PLC objects (POU, method, property Get/Set, action, transition, DUT, GVL, interface). " +
        "For POUs/interfaces/properties the members (methods, properties, actions, Get/Set) are listed with their paths. " +
        "Objects without an area return null for it (DUT/GVL: no implementation, action/transition: no declaration).")]
    public Task<CallToolResult> ReadCode(
        [Description("One or more object paths (batch).")] string[] paths,
        [Description("Also return the code of all members (methods, property accessors, actions, transitions) recursively.")] bool includeMemberCode = false)
        => session.RunAsync(s => new
        {
            objects = paths.Select(p => Guard(p, () =>
            {
                var item = PlcTree.ResolveItem(s, p);
                return includeMemberCode
                    ? (object)new[] { item }.Concat(TreeInfo.Descendants(item)).Where(i => PlcObjects.IsCode(PlcTree.TypeOf(i))).Select(i => CodeOf(i, false)).ToArray()
                    : CodeOf(item, true);
            })).ToArray(),
        });

    [McpServerTool(Name = "pou_get_declaration", ReadOnly = true), Description(
        "Returns the declaration of one or more objects plus a parsed view: kind, name, access, returnType, extends, implements and " +
        "VAR sections with variables (name, type, init, address, comment). Use it to learn the interface of FBs, functions, methods, DUTs and GVLs.")]
    public Task<CallToolResult> GetDeclaration([Description("One or more object paths (batch).")] string[] paths)
        => session.RunAsync(s => new
        {
            objects = paths.Select(p => Guard(p, () =>
            {
                var item = PlcTree.ResolveItem(s, p);
                var text = PlcTree.ReadText(item);
                if (text.Declaration is null)
                {
                    throw new TcException($"'{item.PathName}' has no declaration area.");
                }

                var info = StDeclaration.Parse(text.Declaration);
                return (object)new
                {
                    path = item.PathName,
                    kind = PlcObjects.KindOf(PlcTree.TypeOf(item)),
                    declaration = text.Declaration,
                    parsed = info,
                };
            })).ToArray(),
        });

    private static object CodeOf(ITcSmTreeItem item, bool withMembers)
    {
        var text = PlcTree.ReadText(item);
        return new
        {
            path = item.PathName,
            kind = PlcObjects.KindOf(PlcTree.TypeOf(item)),
            language = text.Implementation is null ? null : PlcTree.LanguageOf(item),
            declaration = text.Declaration,
            implementation = text.Implementation,
            members = withMembers ? Members(item) : null,
        };
    }

    private static object[]? Members(ITcSmTreeItem item)
    {
        var members = TreeInfo.Children(item).Where(c => PlcObjects.IsCode(PlcTree.TypeOf(c))).Select(PlcTree.Describe).ToArray();
        return members.Length == 0 ? null : members;
    }

    private static object Guard(string path, Func<object> f)
    {
        try
        {
            return f();
        }
        catch (TcException ex)
        {
            return new { path, error = ex.Message, hint = ex.Hint };
        }
        catch (Exception ex)
        {
            return new { path, error = ex.Message };
        }
    }

    // ------------------------------------------------------------------ write / replace

    [McpServerTool(Name = "pou_write_code"), Description(
        "Replaces the whole declaration and/or implementation of a PLC object. Pass only the area(s) you want to change. " +
        "The declaration must start with the matching header (e.g. 'FUNCTION_BLOCK FB_Motor', 'METHOD M_Start : BOOL', 'TYPE ST_Data :', 'VAR_GLOBAL'). " +
        "For small changes prefer pou_replace_code. Afterwards check with plc_check_all_objects or build_solution.")]
    public Task<CallToolResult> WriteCode(
        [Description(PathParam)] string path,
        [Description("New full declaration text (null = unchanged).")] string? declaration = null,
        [Description("New full implementation text, ST code (null = unchanged).")] string? implementation = null)
        => session.RunAsync(s =>
        {
            if (declaration is null && implementation is null)
            {
                throw new TcException("Pass declaration and/or implementation.");
            }

            var item = PlcTree.ResolveItem(s, path);
            var current = PlcTree.ReadText(item);
            PlcTree.WriteText(item,
                declaration is null ? null : PlcTree.MatchNewlines(current.Declaration, declaration),
                implementation is null ? null : PlcTree.MatchNewlines(current.Implementation, implementation));
            var after = PlcTree.ReadText(item);
            return new
            {
                path = item.PathName,
                declarationLines = after.Declaration is null ? (int?)null : StCode.SplitLines(after.Declaration).Length,
                implementationLines = after.Implementation is null ? (int?)null : StCode.SplitLines(after.Implementation).Length,
            };
        });

    [McpServerTool(Name = "pou_replace_code"), Description(
        "Replaces an exact text snippet in the declaration or implementation of one PLC object (like a code editor's search & replace). " +
        "Fails if oldText is not found or occurs more than once (unless replaceAll) - then add more context. Line endings are handled automatically. " +
        "Returns the changed line numbers and a small diff.")]
    public Task<CallToolResult> ReplaceCode(
        [Description(PathParam)] string path,
        [Description("Exact existing text (copy from pou_read_code, keep tabs/spaces).")] string oldText,
        [Description("Replacement text (empty string deletes oldText).")] string newText,
        [Description("'declaration', 'implementation' or empty = auto (whichever area contains oldText).")] string? section = null,
        [Description("Replace all occurrences.")] bool replaceAll = false)
        => session.RunAsync(s =>
        {
            var item = PlcTree.ResolveItem(s, path);
            var (text, applied) = StCode.ApplyEdit(PlcTree.ReadText(item), new CodeEdit(oldText, newText, section, replaceAll));
            Write(item, text, applied.Section);
            return new { path = item.PathName, applied.Section, applied.Replacements, applied.Lines, applied.Diff };
        });

    [McpServerTool(Name = "pou_multi_replace"), Description(
        "Applies several exact search & replace edits across one or more PLC objects in one call. Edits on the same object are applied in order. " +
        "All or nothing: if any edit fails (not found / ambiguous) nothing is written.")]
    public Task<CallToolResult> MultiReplace([Description("The edits.")] PouEdit[] edits)
        => session.RunAsync(s =>
        {
            if (edits is not { Length: > 0 })
            {
                throw new TcException("edits must not be empty.");
            }

            // 1) compute everything in memory
            var byItem = new List<(ITcSmTreeItem Item, PouText Original, PouText Text, List<object> Applied, HashSet<string> Sections)>();
            for (var i = 0; i < edits.Length; i++)
            {
                var e = edits[i];
                var item = PlcTree.ResolveItem(s, e.Path);
                var idx = byItem.FindIndex(x => string.Equals(x.Item.PathName, item.PathName, StringComparison.OrdinalIgnoreCase));
                if (idx < 0)
                {
                    var t = PlcTree.ReadText(item);
                    byItem.Add((item, t, t, [], []));
                    idx = byItem.Count - 1;
                }

                var entry = byItem[idx];
                try
                {
                    var (text, a) = StCode.ApplyEdit(entry.Text, new CodeEdit(e.OldText, e.NewText, e.Section, e.ReplaceAll));
                    entry.Sections.Add(a.Section);
                    entry.Applied.Add(new { edit = i + 1, a.Section, a.Replacements, a.Lines, a.Diff });
                    byItem[idx] = entry with { Text = text };
                }
                catch (TcException ex)
                {
                    throw new TcException($"Edit #{i + 1} ({item.PathName}) failed: {ex.Message} Nothing was written.") { Hint = ex.Hint };
                }
            }

            // 2) write
            foreach (var (item, _, text, _, sections) in byItem)
            {
                foreach (var sec in sections)
                {
                    Write(item, text, sec);
                }
            }

            return new { objects = byItem.Select(x => new { path = x.Item.PathName, edits = x.Applied }).ToArray() };
        });

    private static void Write(ITcSmTreeItem item, PouText text, string section)
    {
        if (section == StCode.Declaration)
        {
            PlcTree.WriteText(item, text.Declaration, null);
        }
        else
        {
            PlcTree.WriteText(item, null, text.Implementation);
        }
    }

    [McpServerTool(Name = "pou_grep", ReadOnly = true), Description(
        "Regex search inside one PLC object (and by default its methods, properties, actions, transitions). Returns path, section, line number and line.")]
    public Task<CallToolResult> Grep(
        [Description(PathParam)] string path,
        [Description(".NET regular expression applied per line.")] string pattern,
        bool ignoreCase = true,
        [Description("Also search members (methods, property accessors, actions, transitions).")] bool includeMembers = true,
        [Description("'declaration' or 'implementation'; empty = both.")] string? section = null)
        => session.RunAsync(s =>
        {
            var item = PlcTree.ResolveItem(s, path);
            var regex = PlcTools.BuildRegex(pattern, ignoreCase);
            var only = StCode.ParseSection(section);
            var items = includeMembers ? new[] { item }.Concat(TreeInfo.Descendants(item)) : [item];
            var matches = items.Where(i => PlcObjects.IsCode(PlcTree.TypeOf(i))).SelectMany(i => PlcTools.GrepItem(i, regex, only)).ToArray();
            return new { path = item.PathName, count = matches.Length, matches };
        });

    // ------------------------------------------------------------------ create

    [McpServerTool(Name = "pou_create"), Description(
        "Creates a PROGRAM, FUNCTION_BLOCK or FUNCTION. Language ST (default), IL, LD, FBD, SFC or CFC. FUNCTION needs returnType. " +
        "FUNCTION_BLOCK/PROGRAM may use extends (base FB) and implements (interfaces, comma separated). " +
        "Optional declaration/implementation are written right after creation (ST). Returns the path and the generated declaration.")]
    public Task<CallToolResult> Create(
        [Description("POU name with Beckhoff prefix, e.g. 'FB_Motor', 'F_Scale', 'PRG_Main'.")] string name,
        [Description("PROGRAM | FUNCTION_BLOCK | FUNCTION")] string type = "FUNCTION_BLOCK",
        [Description("Parent folder path. Default: '<project>^POUs'.")] string? parentPath = null,
        [Description("ST (default) | IL | LD | FBD | SFC | CFC")] string language = "ST",
        [Description("Return type, required for FUNCTION (e.g. 'BOOL').")] string? returnType = null,
        [Description("Base function block for EXTENDS (FUNCTION_BLOCK/PROGRAM).")] string? extends = null,
        [Description("Interface(s) for IMPLEMENTS, comma separated, e.g. 'I_Motor, I_Reset'.")] string? implements = null,
        [Description("Optional full declaration text to write after creation.")] string? declaration = null,
        [Description("Optional implementation text to write after creation.")] string? implementation = null,
        [Description(ProjectParam)] string? project = null)
        => session.RunAsync(s =>
        {
            var parent = Parent(s, parentPath, project, "POUs");
            var vInfo = PlcObjects.PouVInfo(type, PlcObjects.ParseLanguage(language), returnType, extends, implements);
            var item = parent.CreateChild(name, PlcObjects.PouSubType(type), "", vInfo);
            return Created(item, declaration, implementation);
        });

    [McpServerTool(Name = "pou_create_dut"), Description(
        "Creates a data type (DUT): STRUCT, ENUM, UNION or ALIAS. Pass a full declaration (e.g. 'TYPE ST_Data :\\nSTRUCT\\n\\tnValue : INT;\\nEND_STRUCT\\nEND_TYPE') " +
        "or for ALIAS just aliasBaseType. Without declaration TwinCAT's default skeleton is created.")]
    public Task<CallToolResult> CreateDut(
        [Description("Name with prefix: ST_ (struct), E_ (enum), U_ (union), T_ (alias).")] string name,
        [Description("STRUCT | ENUM | UNION | ALIAS")] string type = "STRUCT",
        [Description("Optional full declaration text.")] string? declaration = null,
        [Description("Base type for ALIAS when no declaration is given, e.g. 'LREAL' or 'STRING(80)'.")] string? aliasBaseType = null,
        [Description("Parent folder path. Default: '<project>^DUTs'.")] string? parentPath = null,
        [Description(ProjectParam)] string? project = null)
        => session.RunAsync(s =>
        {
            var sub = PlcObjects.DutSubType(type);
            var parent = Parent(s, parentPath, project, "DUTs");
            var decl = declaration is not null ? PlcTree.MatchNewlines(null, declaration) : PlcObjects.DefaultDutDeclaration(type, name, aliasBaseType);
            var item = parent.CreateChild(name, sub, "", decl is null ? null : decl);
            return Created(item, null, null);
        });

    [McpServerTool(Name = "pou_create_gvl"), Description(
        "Creates a global variable list (GVL). Optional declaration, e.g. \"{attribute 'qualified_only'}\\nVAR_GLOBAL\\n\\tbStart : BOOL;\\nEND_VAR\".")]
    public Task<CallToolResult> CreateGvl(
        [Description("Name, e.g. 'GVL_Main'.")] string name,
        [Description("Optional full declaration.")] string? declaration = null,
        [Description("Parent folder path. Default: '<project>^GVLs'.")] string? parentPath = null,
        [Description(ProjectParam)] string? project = null)
        => session.RunAsync(s =>
        {
            var parent = Parent(s, parentPath, project, "GVLs");
            var item = parent.CreateChild(name, PlcObjects.Gvl, "", declaration is null ? null : PlcTree.MatchNewlines(null, declaration));
            return Created(item, null, null);
        });

    [McpServerTool(Name = "pou_create_param_list"), Description(
        "Creates a parameter list (VAR_GLOBAL CONSTANT list usable for array bounds in libraries). Optional declaration.")]
    public Task<CallToolResult> CreateParamList(
        [Description("Name, e.g. 'Param_Machine'.")] string name,
        string? declaration = null,
        [Description("Parent folder path. Default: '<project>^GVLs'.")] string? parentPath = null,
        [Description(ProjectParam)] string? project = null)
        => session.RunAsync(s =>
        {
            var parent = Parent(s, parentPath, project, "GVLs");
            var item = parent.CreateChild(name, PlcObjects.ParameterList, "", declaration is null ? null : PlcTree.MatchNewlines(null, declaration));
            return Created(item, null, null);
        });

    [McpServerTool(Name = "pou_create_text_list"), Description("Creates an (empty) text list object. Its entries can only be edited in the IDE or via PLCopen import.")]
    public Task<CallToolResult> CreateTextList(
        string name,
        [Description("Parent folder path. Default: nested project root.")] string? parentPath = null,
        [Description(ProjectParam)] string? project = null)
        => session.RunAsync(s =>
        {
            var parent = Parent(s, parentPath, project, null);
            return PlcTree.Describe(parent.CreateChild(name, PlcObjects.TextList, "", null));
        });

    [McpServerTool(Name = "pou_create_interface"), Description(
        "Creates an INTERFACE (optionally EXTENDS other interfaces, comma separated). Add members with pou_create_method / pou_create_property.")]
    public Task<CallToolResult> CreateInterface(
        [Description("Name with I_ prefix, e.g. 'I_Motor'.")] string name,
        [Description("Interface(s) to extend, e.g. 'I_Base'.")] string? extends = null,
        [Description("Parent folder path. Default: '<project>^POUs'.")] string? parentPath = null,
        [Description(ProjectParam)] string? project = null)
        => session.RunAsync(s =>
        {
            var parent = Parent(s, parentPath, project, "POUs");
            var item = parent.CreateChild(name, PlcObjects.Interface, "", string.IsNullOrWhiteSpace(extends) ? null : extends.Trim());
            return Created(item, null, null);
        });

    [McpServerTool(Name = "pou_create_method"), Description(
        "Creates a METHOD below a FUNCTION_BLOCK, PROGRAM or INTERFACE. returnType optional, access PUBLIC (default) | PRIVATE | PROTECTED | INTERNAL " +
        "(ignored for interfaces). Optional declaration/implementation are written after creation.")]
    public Task<CallToolResult> CreateMethod(
        [Description("Path of the owning FB/PROGRAM/INTERFACE.")] string parentPath,
        [Description("Method name, e.g. 'M_Start' or 'Execute'.")] string name,
        [Description("Return type, e.g. 'BOOL'. Empty = no return value.")] string? returnType = null,
        [Description("PUBLIC | PRIVATE | PROTECTED | INTERNAL")] string? access = null,
        [Description("ST (default) | IL | LD | FBD | CFC")] string language = "ST",
        [Description("Optional full declaration text (starting with 'METHOD ...').")] string? declaration = null,
        [Description("Optional implementation (not for interfaces).")] string? implementation = null)
        => session.RunAsync(s =>
        {
            var parent = PlcTree.ResolveItem(s, parentPath);
            var ptype = PlcTree.TypeOf(parent);
            ITcSmTreeItem item;
            if (ptype == PlcObjects.Interface)
            {
                item = parent.CreateChild(name, PlcObjects.InterfaceMethod, "", string.IsNullOrWhiteSpace(returnType) ? null : returnType.Trim());
            }
            else
            {
                RequireOwner(parent, ptype, "method");
                var vInfo = new[] { PlcObjects.ParseLanguage(language).ToString(), returnType?.Trim() ?? "", PlcObjects.ParseAccess(access) };
                item = parent.CreateChild(name, PlcObjects.Method, "", vInfo);
            }

            return Created(item, declaration, implementation);
        });

    [McpServerTool(Name = "pou_create_property"), Description(
        "Creates a PROPERTY with Get and Set accessors below a FUNCTION_BLOCK, PROGRAM or INTERFACE. readOnly=true creates only Get, " +
        "writeOnly=true only Set. Optional ST code for the accessors (FB/PROGRAM only), e.g. getImplementation='P_Speed := fSpeed;'.")]
    public Task<CallToolResult> CreateProperty(
        [Description("Path of the owning FB/PROGRAM/INTERFACE.")] string parentPath,
        [Description("Property name, e.g. 'P_Speed' or 'Speed'.")] string name,
        [Description("Property type, e.g. 'LREAL'.")] string type,
        [Description("PUBLIC | PRIVATE | PROTECTED | INTERNAL (ignored for interfaces)")] string? access = null,
        [Description("Only a Get accessor.")] bool readOnly = false,
        [Description("Only a Set accessor.")] bool writeOnly = false,
        [Description("Optional implementation of Get.")] string? getImplementation = null,
        [Description("Optional implementation of Set.")] string? setImplementation = null)
        => session.RunAsync(s =>
        {
            if (readOnly && writeOnly)
            {
                throw new TcException("readOnly and writeOnly cannot both be true.");
            }

            if (string.IsNullOrWhiteSpace(type))
            {
                throw new TcException("type must not be empty.") { Hint = "e.g. 'LREAL', 'BOOL', 'ST_Data'." };
            }

            var parent = PlcTree.ResolveItem(s, parentPath);
            var ptype = PlcTree.TypeOf(parent);
            ITcSmTreeItem prop;
            if (ptype == PlcObjects.Interface)
            {
                prop = parent.CreateChild(name, PlcObjects.InterfaceProperty, "", type.Trim());

                // Verified on 4026: creating the interface Set accessor (655) AFTER the Get accessor (654) crashes the IDE.
                // Set first, then Get works.
                if (!readOnly)
                {
                    prop.CreateChild("Set", PlcObjects.InterfacePropertySet, "", null);
                }

                if (!writeOnly)
                {
                    prop.CreateChild("Get", PlcObjects.InterfacePropertyGet, "", null);
                }
            }
            else
            {
                RequireOwner(parent, ptype, "property");
                var lang = PlcObjects.ParseLanguage("ST").ToString();
                prop = parent.CreateChild(name, PlcObjects.Property, "", new[] { lang, type.Trim(), PlcObjects.ParseAccess(access) });
                if (!writeOnly)
                {
                    var get = prop.CreateChild("Get", PlcObjects.PropertyGet, "", null);
                    if (getImplementation is not null)
                    {
                        PlcTree.WriteText(get, null, PlcTree.MatchNewlines(null, getImplementation));
                    }
                }

                if (!readOnly)
                {
                    var set = prop.CreateChild("Set", PlcObjects.PropertySet, "", null);
                    if (setImplementation is not null)
                    {
                        PlcTree.WriteText(set, null, PlcTree.MatchNewlines(null, setImplementation));
                    }
                }
            }

            return new
            {
                path = prop.PathName,
                kind = "PROPERTY",
                declaration = PlcTree.ReadText(prop).Declaration,
                accessors = TreeInfo.Children(prop).Select(PlcTree.Describe).ToArray(),
            };
        });

    [McpServerTool(Name = "pou_create_action"), Description("Creates an ACTION below a FUNCTION_BLOCK or PROGRAM (implementation only, no declaration).")]
    public Task<CallToolResult> CreateAction(
        [Description("Path of the owning FB/PROGRAM.")] string parentPath,
        [Description("Action name, e.g. 'A_Reset'.")] string name,
        [Description("ST (default) | IL | LD | FBD | SFC | CFC")] string language = "ST",
        [Description("Optional implementation.")] string? implementation = null)
        => session.RunAsync(s =>
        {
            var parent = PlcTree.ResolveItem(s, parentPath);
            RequireOwner(parent, PlcTree.TypeOf(parent), "action");
            var item = parent.CreateChild(name, PlcObjects.Action, "", PlcObjects.ParseLanguage(language));
            return Created(item, null, implementation);
        });

    [McpServerTool(Name = "pou_create_transition"), Description(
        "Creates a TRANSITION object below a POU (used by SFC charts). Implementation is a boolean expression in ST, e.g. 'bStart AND NOT bError'.")]
    public Task<CallToolResult> CreateTransition(
        [Description("Path of the owning POU (usually an SFC PROGRAM/FB).")] string parentPath,
        [Description("Transition name, e.g. 'T_Start'.")] string name,
        [Description("ST (default) | IL | LD | FBD | CFC")] string language = "ST",
        [Description("Optional implementation (boolean expression).")] string? implementation = null)
        => session.RunAsync(s =>
        {
            var parent = PlcTree.ResolveItem(s, parentPath);
            RequireOwner(parent, PlcTree.TypeOf(parent), "transition");
            var item = parent.CreateChild(name, PlcObjects.Transition, "", PlcObjects.ParseLanguage(language));
            return Created(item, null, implementation);
        });

    private static void RequireOwner(ITcSmTreeItem parent, int type, string what)
    {
        if (type is PlcObjects.FunctionBlock or PlcObjects.Program)
        {
            return;
        }

        throw new TcException($"A {what} cannot be created below '{parent.PathName}' ({PlcObjects.KindOf(type)}).")
        {
            Hint = what is "method" or "property"
                ? "Use the path of a FUNCTION_BLOCK, PROGRAM or INTERFACE."
                : "Use the path of a FUNCTION_BLOCK or PROGRAM.",
        };
    }

    private static ITcSmTreeItem Parent(XaeSession s, string? parentPath, string? project, string? defaultFolder)
    {
        if (!string.IsNullOrWhiteSpace(parentPath))
        {
            return PlcTree.ResolveItem(s, parentPath);
        }

        var p = PlcTree.ResolveProject(s, project);
        if (defaultFolder is not null && s.SysManager.TryLookupTreeItem(p.ProjectPath + "^" + defaultFolder, out var folder) && folder is not null)
        {
            return folder;
        }

        return p.Nested;
    }

    private static object Created(ITcSmTreeItem item, string? declaration, string? implementation)
    {
        if (declaration is not null || implementation is not null)
        {
            var cur = PlcTree.ReadText(item);
            PlcTree.WriteText(item,
                declaration is null ? null : PlcTree.MatchNewlines(cur.Declaration, declaration),
                implementation is null ? null : PlcTree.MatchNewlines(cur.Implementation, implementation));
        }

        var text = PlcTree.ReadText(item);
        return new
        {
            path = item.PathName,
            kind = PlcObjects.KindOf(PlcTree.TypeOf(item)),
            language = text.Implementation is null ? null : PlcTree.LanguageOf(item),
            declaration = text.Declaration,
        };
    }

    // ------------------------------------------------------------------ delete / rename / move

    [McpServerTool(Name = "pou_delete", Destructive = true), Description(
        "Deletes one or more PLC objects (POUs, methods, properties, actions, DUTs, GVLs, folders incl. content). References to them are not updated.")]
    public Task<CallToolResult> Delete([Description("Paths of the objects to delete (batch).")] string[] paths)
        => session.RunAsync(s => new
        {
            results = paths.Select(p => Guard(p, () =>
            {
                var item = PlcTree.ResolveItem(s, p);
                var type = PlcTree.TypeOf(item);
                if (type is PlcObjects.App or PlcObjects.ProjectDef || !item.PathName.StartsWith("TIPC^", StringComparison.OrdinalIgnoreCase))
                {
                    throw new TcException($"'{item.PathName}' is not a PLC object.") { Hint = "Use plc_delete_project to remove a whole PLC project." };
                }

                var full = item.PathName;
                item.Parent.DeleteChild(item.Name);
                return new { deleted = full };
            })).ToArray(),
        });

    [McpServerTool(Name = "pou_rename"), Description(
        "Renames a PLC object (the declaration header is updated by TwinCAT). Usages in other code are NOT refactored - " +
        "use plc_grep + pou_multi_replace to update them.")]
    public Task<CallToolResult> Rename([Description(PathParam)] string path, [Description("New name.")] string newName)
        => session.RunAsync(s =>
        {
            var item = PlcTree.ResolveItem(s, path);
            var old = item.PathName;
            item.Name = newName;
            return new { oldPath = old, path = item.PathName, declaration = PlcTree.ReadText(item).Declaration };
        });

    [McpServerTool(Name = "pou_move"), Description(
        "Moves a top-level PLC object (POU, DUT, GVL, interface, folder) into another folder of the same PLC project. " +
        "Implemented as PLCopen export -> delete -> import into the target folder (object id, members and code are kept; " +
        "on import failure the object is restored at its original place).")]
    public Task<CallToolResult> Move(
        [Description(PathParam)] string path,
        [Description("Target folder path, e.g. 'TIPC^Plc1^Plc1 Project^POUs^Motion' (or the nested project path for the root). Missing folders are created.")] string targetFolder,
        int timeoutSeconds = 300)
        => session.RunAsync(s =>
        {
            var item = PlcTree.ResolveItem(s, path);
            var p = PlcTree.ProjectOf(s, item.PathName);
            var type = PlcTree.TypeOf(item);
            if (!PlcObjects.TopLevelTypes.Contains(type))
            {
                throw new TcException($"'{item.PathName}' ({PlcObjects.KindOf(type)}) cannot be moved on its own.")
                {
                    Hint = "Only POUs, DUTs, GVLs, interfaces and folders can be moved.",
                };
            }

            var targetChain = FolderChain(p, targetFolder);
            var sourceChain = FolderChain(p, item.Parent.PathName);
            if (sourceChain.SequenceEqual(targetChain, StringComparer.OrdinalIgnoreCase))
            {
                return new { path = item.PathName, moved = false, message = "Object is already in the target folder." };
            }

            var target = PlcTools.EnsureFolders(p, targetChain);
            if (TreeInfo.Children(target).Any(c => string.Equals(c.Name, item.Name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new TcException($"'{target.PathName}' already contains '{item.Name}'.");
            }

            var name = item.Name;
            var oldPath = item.PathName;
            var iec = (ITcPlcIECProject)p.Nested;
            var tmp = Path.Combine(Path.GetTempPath(), "twincat-mcp-move-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                var export = Path.Combine(tmp, "export.xml");
                iec.PlcOpenExport(export, PlcObjects.ToSelection(p.ProjectPath, oldPath));
                var xml = File.ReadAllText(export);
                var moved = Path.Combine(tmp, "moved.xml");
                File.WriteAllText(moved, PlcOpenXml.PlaceInFolder(xml, targetChain));

                item.Parent.DeleteChild(name);
                try
                {
                    iec.PlcOpenImport(moved, 0, "", true);
                }
                catch (Exception ex)
                {
                    var restore = Path.Combine(tmp, "restore.xml");
                    File.WriteAllText(restore, PlcOpenXml.PlaceInFolder(xml, sourceChain));
                    iec.PlcOpenImport(restore, 0, "", true);
                    throw new TcException($"Import into '{targetFolder}' failed, object restored at '{oldPath}': {ex.Message}");
                }
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch { /* ignore */ }
            }

            var newPath = target.PathName + "^" + name;
            return new { oldPath, path = newPath, moved = s.SysManager.TestItemPath(newPath) };
        }, timeoutSeconds);

    private static string[] FolderChain(PlcProjectRef p, string folderPath)
    {
        var path = folderPath.Trim().Trim('^');
        if (string.Equals(path, p.ProjectPath, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        if (path.StartsWith(p.ProjectPath + "^", StringComparison.OrdinalIgnoreCase))
        {
            path = path[(p.ProjectPath.Length + 1)..];
        }
        else if (path.StartsWith("TI", StringComparison.Ordinal))
        {
            throw new TcException($"'{folderPath}' is not inside PLC project '{p.ProjectPath}'.");
        }

        return path.Split('^', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
