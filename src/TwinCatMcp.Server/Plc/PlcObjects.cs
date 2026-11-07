using TwinCatMcp.Core;

namespace TwinCatMcp.Plc;

/// <summary>
/// PLC object model of the Automation Interface: tree item types (= CreateChild sub types) of objects inside a
/// nested PLC project, kind names, languages and CreateChild vInfo layouts.
/// Source: Beckhoff InfoSys "Accessing, creating and handling PLC POUs" + live verification on TwinCAT 3.1.4026.
/// </summary>
public static class PlcObjects
{
    // TREEITEMTYPES (ITcSmTreeItem.ItemType) == nSubType for CreateChild.
    public const int ProjectDef = 56;      // TIPC^<Plc>  (ITcPlcProject, ITcProjectRoot)
    public const int ProjectInstance = 57; // TIPC^<Plc>^<Plc> Instance
    public const int App = 600;            // nested project "<Plc> Project" (ITcPlcIECProject)
    public const int Folder = 601;
    public const int Program = 602;
    public const int Function = 603;
    public const int FunctionBlock = 604;
    public const int Enum = 605;
    public const int Struct = 606;
    public const int Union = 607;
    public const int Action = 608;
    public const int Method = 609;
    public const int InterfaceMethod = 610;
    public const int Property = 611;
    public const int InterfaceProperty = 612;
    public const int PropertyGet = 613;
    public const int PropertySet = 614;
    public const int Gvl = 615;
    public const int Transition = 616;
    public const int LibraryManager = 617;
    public const int Interface = 618;
    public const int Visualization = 619;
    public const int Task = 621;            // ITcPlcTaskReference
    public const int Alias = 623;
    public const int GlobalTextList = 625;
    public const int TextList = 626;
    public const int ParameterList = 629;
    public const int UmlClassDiagram = 631;
    public const int ProgramReference = 650;
    public const int TmcDescription = 653;
    public const int InterfacePropertyGet = 654;
    public const int InterfacePropertySet = 655;

    private static readonly Dictionary<int, string> Kinds = new()
    {
        [Folder] = "FOLDER",
        [Program] = "PROGRAM",
        [Function] = "FUNCTION",
        [FunctionBlock] = "FUNCTION_BLOCK",
        [Enum] = "ENUM",
        [Struct] = "STRUCT",
        [Union] = "UNION",
        [Alias] = "ALIAS",
        [Action] = "ACTION",
        [Method] = "METHOD",
        [InterfaceMethod] = "METHOD",
        [Property] = "PROPERTY",
        [InterfaceProperty] = "PROPERTY",
        [PropertyGet] = "PROPERTY_GET",
        [PropertySet] = "PROPERTY_SET",
        [InterfacePropertyGet] = "PROPERTY_GET",
        [InterfacePropertySet] = "PROPERTY_SET",
        [Gvl] = "GVL",
        [ParameterList] = "PARAMETER_LIST",
        [Transition] = "TRANSITION",
        [Interface] = "INTERFACE",
        [TextList] = "TEXT_LIST",
        [GlobalTextList] = "GLOBAL_TEXT_LIST",
        [Visualization] = "VISUALIZATION",
        [UmlClassDiagram] = "UML_CLASS_DIAGRAM",
        [Task] = "TASK",
        [ProgramReference] = "PROGRAM_REFERENCE",
        [LibraryManager] = "REFERENCES",
        [TmcDescription] = "TMC",
        [App] = "PLC_PROJECT",
    };

    /// <summary>Filter vocabulary of plc_find_elements: kind name (or alias) -> item types.</summary>
    private static readonly Dictionary<string, int[]> KindGroups = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pou"] = [Program, Function, FunctionBlock],
        ["program"] = [Program],
        ["prg"] = [Program],
        ["function"] = [Function],
        ["fun"] = [Function],
        ["function_block"] = [FunctionBlock],
        ["fb"] = [FunctionBlock],
        ["dut"] = [Enum, Struct, Union, Alias],
        ["struct"] = [Struct],
        ["enum"] = [Enum],
        ["union"] = [Union],
        ["alias"] = [Alias],
        ["gvl"] = [Gvl, ParameterList],
        ["parameter_list"] = [ParameterList],
        ["interface"] = [Interface],
        ["itf"] = [Interface],
        ["method"] = [Method, InterfaceMethod],
        ["property"] = [Property, InterfaceProperty],
        ["action"] = [Action],
        ["transition"] = [Transition],
        ["folder"] = [Folder],
        ["text_list"] = [TextList, GlobalTextList],
        ["task"] = [Task],
        ["visualization"] = [Visualization],
    };

    /// <summary>Kinds that hold source code (declaration and/or implementation).</summary>
    public static readonly HashSet<int> CodeItemTypes =
    [
        Program, Function, FunctionBlock, Enum, Struct, Union, Alias, Action, Method, InterfaceMethod,
        Property, InterfaceProperty, PropertyGet, PropertySet, InterfacePropertyGet, InterfacePropertySet,
        Gvl, ParameterList, Transition, Interface,
    ];

    /// <summary>Top-level objects that can be exported/imported/moved via PLCopen XML.</summary>
    public static readonly HashSet<int> TopLevelTypes =
    [
        Program, Function, FunctionBlock, Enum, Struct, Union, Alias, Gvl, ParameterList, Interface, Folder, TextList,
        GlobalTextList, Visualization, UmlClassDiagram,
    ];

    public static string KindOf(int itemType) => Kinds.TryGetValue(itemType, out var k) ? k : $"TYPE_{itemType}";

    public static bool IsCode(int itemType) => CodeItemTypes.Contains(itemType);

    public static IEnumerable<string> KnownKindFilters => KindGroups.Keys;

    public static bool MatchesKind(int itemType, IReadOnlyCollection<string>? filters)
    {
        if (filters is null || filters.Count == 0)
        {
            return true;
        }

        foreach (var f in filters)
        {
            var key = f.Trim();
            if (KindGroups.TryGetValue(key, out var types) && types.Contains(itemType))
            {
                return true;
            }

            if (string.Equals(KindOf(itemType), key, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static void ValidateKindFilters(IEnumerable<string>? filters)
    {
        foreach (var f in filters ?? [])
        {
            if (!KindGroups.ContainsKey(f.Trim()) && !Kinds.Values.Contains(f.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                throw new TcException($"Unknown kind filter '{f}'.") { Hint = "Valid: " + string.Join(", ", KindGroups.Keys) };
            }
        }
    }

    // ------------------------------------------------------------------ languages (IECLANGUAGETYPES)

    public static int ParseLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return 1;
        }

        return language.Trim().ToUpperInvariant() switch
        {
            "ST" or "STRUCTURED TEXT" => 1,
            "IL" => 2,
            "SFC" => 3,
            "FBD" => 4,
            "CFC" => 5,
            "LD" => 6,
            _ => throw new TcException($"Unknown IEC language '{language}'.") { Hint = "Use ST (default), IL, SFC, FBD, CFC or LD." },
        };
    }

    public static string? LanguageName(int language) => language switch
    {
        1 => "ST",
        2 => "IL",
        3 => "SFC",
        4 => "FBD",
        5 => "CFC",
        6 => "LD",
        7 => "UML_CD",
        8 => "UML_SC",
        9 => "UML_AD",
        _ => null,
    };

    // ------------------------------------------------------------------ CreateChild sub types + vInfo

    public static int PouSubType(string type) => type.Trim().ToUpperInvariant() switch
    {
        "PROGRAM" or "PRG" => Program,
        "FUNCTION_BLOCK" or "FB" or "FUNCTIONBLOCK" => FunctionBlock,
        "FUNCTION" or "FUN" or "FC" => Function,
        _ => throw new TcException($"Unknown POU type '{type}'.") { Hint = "Use PROGRAM, FUNCTION_BLOCK or FUNCTION." },
    };

    public static int DutSubType(string type) => type.Trim().ToUpperInvariant() switch
    {
        "STRUCT" => Struct,
        "ENUM" => Enum,
        "UNION" => Union,
        "ALIAS" => Alias,
        _ => throw new TcException($"Unknown DUT type '{type}'.") { Hint = "Use STRUCT, ENUM, UNION or ALIAS." },
    };

    /// <summary>
    /// vInfo for PROGRAM (602) / FUNCTION_BLOCK (604): [language, "Extends", base, "Implements", itfs] or
    /// [language, "Extends"|"Implements", name] or just the language (scalar, as the docs require for one value).
    /// FUNCTION (603): [language, returnType].
    /// </summary>
    public static object PouVInfo(string type, int language, string? returnType, string? extends, string? implements)
    {
        var sub = PouSubType(type);
        var lang = language.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (sub == Function)
        {
            if (string.IsNullOrWhiteSpace(returnType))
            {
                throw new TcException("A FUNCTION needs a return type.") { Hint = "Pass returnType, e.g. 'BOOL' or 'LREAL'." };
            }

            if (!string.IsNullOrWhiteSpace(extends) || !string.IsNullOrWhiteSpace(implements))
            {
                throw new TcException("A FUNCTION cannot use EXTENDS or IMPLEMENTS.");
            }

            return new[] { lang, returnType.Trim() };
        }

        var hasExt = !string.IsNullOrWhiteSpace(extends);
        var hasImpl = !string.IsNullOrWhiteSpace(implements);
        if (hasExt && hasImpl)
        {
            return new[] { lang, "Extends", extends!.Trim(), "Implements", implements!.Trim() };
        }

        if (hasExt)
        {
            return new[] { lang, "Extends", extends!.Trim() };
        }

        if (hasImpl)
        {
            return new[] { lang, "Implements", implements!.Trim() };
        }

        return language;
    }

    /// <summary>
    /// Declaration passed as vInfo when creating a DUT. TwinCAT takes vInfo verbatim as declaration text,
    /// and an ALIAS cannot be created without it ("Base class not specified").
    /// </summary>
    public static string? DefaultDutDeclaration(string type, string name, string? aliasBaseType)
    {
        if (DutSubType(type) != Alias)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(aliasBaseType))
        {
            throw new TcException("An ALIAS needs a base type.") { Hint = "Pass aliasBaseType (e.g. 'LREAL' or 'STRING(80)') or a full declaration." };
        }

        return $"TYPE {name} : {aliasBaseType.Trim()};\r\nEND_TYPE\r\n";
    }

    public static string ParseAccess(string? access)
    {
        if (string.IsNullOrWhiteSpace(access))
        {
            return "PUBLIC";
        }

        var a = access.Trim().ToUpperInvariant();
        return a is "PUBLIC" or "PRIVATE" or "PROTECTED" or "INTERNAL"
            ? a
            : throw new TcException($"Unknown access modifier '{access}'.") { Hint = "Use PUBLIC, PRIVATE, PROTECTED or INTERNAL." };
    }

    // ------------------------------------------------------------------ PLCopen selection

    /// <summary>Converts a tree path inside a nested PLC project to the dotted PLCopen selection ("POUs.Sub.FB_A").</summary>
    public static string ToSelection(string projectPath, string itemPath)
    {
        var prefix = projectPath + "^";
        if (!itemPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new TcException($"'{itemPath}' is not inside PLC project '{projectPath}'.");
        }

        return itemPath[prefix.Length..].Replace('^', '.');
    }
}
