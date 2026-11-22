using System.Globalization;
using TwinCatMcp.Plc;

namespace TwinCatMcp.Guidelines;

/// <summary>
/// A PLC object as the guideline checks see it. Kind uses the names of PlcObjects.KindOf (FUNCTION_BLOCK, METHOD, ENUM,
/// GVL ...); ParentName/ParentKind are set for methods, properties, actions and transitions.
/// </summary>
public sealed record GuidelineObject(string Kind, string Name, string? Path = null, string? ParentName = null, string? ParentKind = null);

/// <summary>One guideline finding. Severity is 'warn' or 'error'.</summary>
public sealed record GuidelineViolation(
    string Rule,
    string Severity,
    string Message,
    string Object,
    string? Identifier = null,
    int? Line = null,
    string? Suggestion = null);

/// <summary>Text of a new object after the create fixes (header, required attributes) and what was changed.</summary>
public sealed record GuidelineCreateFix(PouText Text, IReadOnlyList<string> Applied);

/// <summary>Create fixes plus the findings of the new object (name and text).</summary>
public sealed record GuidelineCreateResult(PouText Text, IReadOnlyList<string> Applied, IReadOnlyList<GuidelineViolation> Violations);

public sealed partial class GuidelineSet
{
    private readonly NamingRules _naming;
    private readonly DeclarationRules _declarations;
    private readonly HeaderRules _headers;

    private static readonly HashSet<string> ImplementationOnlyKinds = new(StringComparer.Ordinal)
    {
        "ACTION", "TRANSITION", "PROPERTY_GET", "PROPERTY_SET",
    };

    /// <summary>Rule for the name of an object kind, null for kinds without one (folders, accessors ...).</summary>
    public static string? NameRuleOf(string kind) => kind switch
    {
        "PROGRAM" => GuidelineRules.PouProgram,
        "FUNCTION_BLOCK" => GuidelineRules.PouFunctionBlock,
        "FUNCTION" => GuidelineRules.PouFunction,
        "METHOD" => GuidelineRules.PouMethod,
        "PROPERTY" => GuidelineRules.PouProperty,
        "ACTION" => GuidelineRules.PouAction,
        "TRANSITION" => GuidelineRules.PouTransition,
        "INTERFACE" => GuidelineRules.PouInterface,
        "STRUCT" => GuidelineRules.DutStruct,
        "ENUM" => GuidelineRules.DutEnum,
        "UNION" => GuidelineRules.DutUnion,
        "ALIAS" => GuidelineRules.DutAlias,
        "GVL" => GuidelineRules.Gvl,
        "PARAMETER_LIST" => GuidelineRules.ParamList,
        _ => null,
    };

    /// <summary>Checks the name of an object (on create and rename). The declaration, if known, supplies naming/mcp-ignore attributes.</summary>
    public IReadOnlyList<GuidelineViolation> CheckName(GuidelineObject obj, string? declaration = null)
    {
        if (NameRuleOf(obj.Kind) is not { } rule || SeverityOf(rule) == GuidelineSeverity.Off)
        {
            return [];
        }

        var attributes = declaration is null ? StDeclaration.NoAttributes : StDeclaration.Parse(declaration).Attributes;
        if (Suppressed(rule, obj, null, attributes, StDeclaration.NoAttributes))
        {
            return [];
        }

        var check = _naming.Check(rule, obj.Name, null);
        return check.Ok
            ? []
            : [Violation(rule, obj, $"Name '{obj.Name}' of the {obj.Kind} does not match '{check.Expected}'.", null, null, check.Suggestion)];
    }

    /// <summary>
    /// Checks the declaration/implementation of an object. Only what is new or changed compared to 'before' is reported
    /// (before = null: a new object, everything is checked).
    /// </summary>
    public IReadOnlyList<GuidelineViolation> CheckText(GuidelineObject obj, PouText? before, PouText after)
    {
        var result = new List<GuidelineViolation>();
        var oldInfo = before?.Declaration is { } od ? StDeclaration.Parse(od) : null;
        var newInfo = after.Declaration is { } nd ? StDeclaration.Parse(nd) : null;
        var objectAttributes = newInfo?.Attributes ?? StDeclaration.NoAttributes;
        if (objectAttributes.ContainsKey("no-analysis"))
        {
            return [];
        }

        if (newInfo is not null)
        {
            CheckIdentifiers(obj, oldInfo, newInfo, result);
            CheckEnum(obj, oldInfo, newInfo, result);
            CheckRequiredAttributes(obj, oldInfo, newInfo, result);
        }

        CheckHeader(obj, before, after, objectAttributes, result);
        return result;
    }

    /// <summary>
    /// For a new object: applies the create fixes to the given text (parts that are null stay null) and checks name and text.
    /// A DESCRIPTION left as placeholder in an inserted header is only a warning - the author fills it in later.
    /// </summary>
    public GuidelineCreateResult PrepareCreate(GuidelineObject obj, PouText given, DateTime now)
    {
        var fix = ApplyCreateFixes(obj, given, now);
        var violations = CheckName(obj, fix.Text.Declaration).ToList();
        foreach (var v in CheckText(obj, null, fix.Text))
        {
            var inserted = fix.Applied.Any(a => a.StartsWith("header", StringComparison.Ordinal));
            violations.Add(v.Rule == GuidelineRules.HeaderDescription && inserted
                ? v with { Severity = GuidelineRules.Name(GuidelineSeverity.Warn), Message = $"Fill in the {_headers.DescriptionKeyword} of the inserted header." }
                : v);
        }

        return new GuidelineCreateResult(fix.Text, fix.Applied, violations);
    }

    /// <summary>
    /// After an object was created from TwinCAT's skeleton: applies the create fixes to the given parts and reminds (warn)
    /// to fill in the description of an inserted header.
    /// </summary>
    public GuidelineCreateResult CompleteCreate(GuidelineObject obj, PouText skeleton, DateTime now)
    {
        var fix = ApplyCreateFixes(obj, skeleton, now);
        var text = ImplementationOnlyKinds.Contains(obj.Kind) ? fix.Text.Implementation : fix.Text.Declaration;
        var remind = fix.Applied.Any(a => a.StartsWith("header", StringComparison.Ordinal)) && text is not null
            && SeverityOf(GuidelineRules.HeaderDescription) != GuidelineSeverity.Off && !_headers.HasDescription(text);
        return new GuidelineCreateResult(fix.Text, fix.Applied, remind
            ? [new GuidelineViolation(GuidelineRules.HeaderDescription, GuidelineRules.Name(GuidelineSeverity.Warn),
                $"Fill in the {_headers.DescriptionKeyword} of the inserted header.", obj.Path ?? obj.Name)]
            : []);
    }

    /// <summary>Inserts the header template and missing required attributes into a new object's text (null parts are skipped).</summary>
    public GuidelineCreateFix ApplyCreateFixes(GuidelineObject obj, PouText text, DateTime now)
    {
        var applied = new List<string>();
        var declaration = text.Declaration;
        var implementation = text.Implementation;

        if (AutoFixAttributes && declaration is not null && SeverityOf(GuidelineRules.RequiredAttributes) != GuidelineSeverity.Off
            && _declarations.RequiredAttributes.TryGetValue(obj.Kind, out var required))
        {
            var present = StDeclaration.Parse(declaration).Attributes;
            var missing = required.Where(a => !present.ContainsKey(a)).ToArray();
            if (missing.Length > 0)
            {
                var nl = Newline(declaration);
                declaration = string.Concat(missing.Select(a => $"{{attribute '{a}'}}{nl}")) + declaration;
                applied.AddRange(missing.Select(a => $"attribute '{a}' added"));
            }
        }

        if (AutoFixHeaders && _headers.TemplateFor(obj) is { } template)
        {
            var header = _headers.Render(template, obj, now);
            if (ImplementationOnlyKinds.Contains(obj.Kind))
            {
                if (implementation is not null && !_headers.HasHeader(implementation, template))
                {
                    var nl = Newline(implementation);
                    implementation = header.Replace("\n", nl) + nl + implementation;
                    applied.Add("header inserted into the implementation");
                }
            }
            else if (declaration is not null && !_headers.HasHeader(declaration, template))
            {
                declaration = InsertHeader(declaration, header);
                applied.Add("header inserted into the declaration");
            }
        }

        return new GuidelineCreateFix(new PouText(declaration, implementation), applied);
    }

    private string InsertHeader(string declaration, string header)
    {
        var nl = Newline(declaration);
        var lines = declaration.Replace("\r\n", "\n").Split('\n').ToList();
        var at = _headers.AfterHeaderLine && StDeclaration.HeaderLineIndex(declaration) is { } h ? h + 1 : 0;
        lines.InsertRange(at, header.Split('\n'));
        return string.Join(nl, lines);
    }

    private static string Newline(string text) => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    // ------------------------------------------------------------------ identifiers

    private void CheckIdentifiers(GuidelineObject obj, StDeclarationInfo? oldInfo, StDeclarationInfo newInfo, List<GuidelineViolation> result)
    {
        var known = oldInfo is null
            ? []
            : oldInfo.Sections.SelectMany(s => s.Variables.Select(v => Identity(CategoryOf(obj, s.Kind), v))).ToHashSet(StringComparer.Ordinal);

        foreach (var section in newInfo.Sections)
        {
            var rule = CategoryOf(obj, section.Kind);
            if (rule is null)
            {
                continue;
            }

            foreach (var v in section.Variables)
            {
                if (known.Contains(Identity(rule, v)))
                {
                    continue;
                }

                CheckMethodInputDefault(obj, section.Kind, v, newInfo.Attributes, result);
                if (SeverityOf(rule) == GuidelineSeverity.Off || Suppressed(rule, obj, v.Name, newInfo.Attributes, v.Attributes))
                {
                    continue;
                }

                var check = _naming.Check(rule, v.Name, v.Type);
                if (check.UnknownType && SeverityOf(GuidelineRules.UnknownType) != GuidelineSeverity.Off
                    && !Suppressed(GuidelineRules.UnknownType, obj, v.Name, newInfo.Attributes, v.Attributes))
                {
                    result.Add(Violation(GuidelineRules.UnknownType, obj,
                        $"Type '{v.Type}' of '{v.Name}' has no prefix in naming.types, naming.typePrefixes or naming.typeKinds.", v.Name, v.Line, null));
                }

                if (!check.Ok)
                {
                    result.Add(Violation(rule, obj,
                        $"'{v.Name}' ({section.Kind}, {v.Type}) does not match '{check.Expected}'.", v.Name, v.Line, check.Suggestion));
                }
            }
        }
    }

    private void CheckMethodInputDefault(GuidelineObject obj, string sectionKind, StVariable v, IReadOnlyDictionary<string, string> objectAttributes, List<GuidelineViolation> result)
    {
        const string rule = GuidelineRules.MethodInputDefaults;
        if (obj.Kind == "METHOD" && v.Init is not null && Upper(sectionKind).StartsWith("VAR_INPUT", StringComparison.Ordinal)
            && SeverityOf(rule) != GuidelineSeverity.Off && !Suppressed(rule, obj, v.Name, objectAttributes, v.Attributes))
        {
            result.Add(Violation(rule, obj,
                $"Method input '{v.Name}' must not have a default value (':= {v.Init}'); callers pass every input.", v.Name, v.Line, null));
        }
    }

    private static string Identity(string? rule, StVariable v)
        => $"{rule}|{v.Name}|{string.Join("", v.Type.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant()}|{v.Init}";

    /// <summary>Naming rule of a variable from the object kind and the VAR section it is declared in (null = not checked).</summary>
    public static string? CategoryOf(GuidelineObject obj, string sectionKind)
    {
        var s = Upper(sectionKind);
        var constant = s.Contains("CONSTANT", StringComparison.Ordinal);
        var first = s.Split(' ')[0];
        return first switch
        {
            "VAR_INPUT" => GuidelineRules.VarInput,
            "VAR_OUTPUT" => GuidelineRules.VarOutput,
            "VAR_IN_OUT" => GuidelineRules.VarInOut,
            "VAR_GLOBAL" => constant ? GuidelineRules.ConstGlobal : GuidelineRules.VarGlobal,
            "VAR_STAT" => GuidelineRules.VarStat,
            "VAR_TEMP" => GuidelineRules.VarTemp,
            "VAR_INST" => GuidelineRules.VarInst,
            "STRUCT" or "UNION" => GuidelineRules.VarMember,
            "VAR" when constant => GuidelineRules.ConstLocal,
            "VAR" => obj.Kind switch
            {
                "METHOD" or "PROPERTY_GET" or "PROPERTY_SET" => GuidelineRules.VarMethodLocal,
                "FUNCTION" => GuidelineRules.VarFunctionLocal,
                _ => GuidelineRules.VarLocal,
            },
            _ => null,
        };
    }

    private static string Upper(string s) => s.ToUpperInvariant();

    // ------------------------------------------------------------------ enums and attributes

    private void CheckEnum(GuidelineObject obj, StDeclarationInfo? oldInfo, StDeclarationInfo newInfo, List<GuidelineViolation> result)
    {
        if (obj.Kind != "ENUM")
        {
            return;
        }

        var known = oldInfo?.EnumValues.Select(v => $"{v.Name}|{v.Value}").ToHashSet(StringComparer.Ordinal) ?? [];
        foreach (var value in newInfo.EnumValues.Where(v => !known.Contains($"{v.Name}|{v.Value}")))
        {
            if (SeverityOf(GuidelineRules.EnumValue) != GuidelineSeverity.Off
                && !Suppressed(GuidelineRules.EnumValue, obj, value.Name, newInfo.Attributes, StDeclaration.NoAttributes))
            {
                var check = _naming.Check(GuidelineRules.EnumValue, value.Name, null);
                if (!check.Ok)
                {
                    result.Add(Violation(GuidelineRules.EnumValue, obj, $"Enum value '{value.Name}' does not match '{check.Expected}'.", value.Name, null, check.Suggestion));
                }
            }

            if (value.Value is null)
            {
                if (SeverityOf(GuidelineRules.EnumExplicitValues) != GuidelineSeverity.Off
                    && !Suppressed(GuidelineRules.EnumExplicitValues, obj, value.Name, newInfo.Attributes, StDeclaration.NoAttributes))
                {
                    result.Add(Violation(GuidelineRules.EnumExplicitValues, obj, $"Enum value '{value.Name}' needs an explicit value (':= n').", value.Name, null, null));
                }

                continue;
            }

            if (_declarations.EnumRange is { } range && SeverityOf(GuidelineRules.EnumValueRange) != GuidelineSeverity.Off
                && int.TryParse(value.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                && !InRange(range, n, obj.Name, value.Name)
                && !Suppressed(GuidelineRules.EnumValueRange, obj, value.Name, newInfo.Attributes, StDeclaration.NoAttributes))
            {
                var extension = range.ExtensionMin is { } ext ? $"; local extensions use {ext} and up and are named '{obj.Name}_<VALUE>'" : "";
                result.Add(Violation(GuidelineRules.EnumValueRange, obj,
                    $"Enum value '{value.Name}' := {n} is outside {range.Min}..{range.Max?.ToString(CultureInfo.InvariantCulture) ?? "∞"}{extension}.", value.Name, null, null));
            }
        }
    }

    private static bool InRange(EnumRange range, int n, string enumName, string valueName)
        => (n >= range.Min && (range.Max is null || n <= range.Max))
           || (range.ExtensionMin is { } ext && n >= ext && valueName.StartsWith(enumName + "_", StringComparison.OrdinalIgnoreCase));

    private void CheckRequiredAttributes(GuidelineObject obj, StDeclarationInfo? oldInfo, StDeclarationInfo newInfo, List<GuidelineViolation> result)
    {
        const string rule = GuidelineRules.RequiredAttributes;
        if (SeverityOf(rule) == GuidelineSeverity.Off || !_declarations.RequiredAttributes.TryGetValue(obj.Kind, out var required)
            || Suppressed(rule, obj, null, newInfo.Attributes, StDeclaration.NoAttributes))
        {
            return;
        }

        foreach (var attribute in required.Where(a => !newInfo.Attributes.ContainsKey(a)))
        {
            if (oldInfo is null || oldInfo.Attributes.ContainsKey(attribute))
            {
                result.Add(Violation(rule, obj, $"The {obj.Kind} needs {{attribute '{attribute}'}} before its header.", null, null, $"{{attribute '{attribute}'}}"));
            }
        }
    }

    // ------------------------------------------------------------------ header

    private void CheckHeader(GuidelineObject obj, PouText? before, PouText after, IReadOnlyDictionary<string, string> objectAttributes, List<GuidelineViolation> result)
    {
        if (_headers.TemplateFor(obj) is null)
        {
            return;
        }

        // The header lives in the declaration, only implementation-only objects carry it in the implementation.
        // A new object created without declaration gets it from TwinCAT's skeleton later (CompleteCreate).
        var useImplementation = ImplementationOnlyKinds.Contains(obj.Kind);
        var now = useImplementation ? after.Implementation : after.Declaration;
        var old = useImplementation ? before?.Implementation : before?.Declaration;
        if (now is null)
        {
            return;
        }

        if (SeverityOf(GuidelineRules.HeaderCopyright) != GuidelineSeverity.Off && !_headers.HasCopyright(now)
            && (old is null || _headers.HasCopyright(old))
            && !Suppressed(GuidelineRules.HeaderCopyright, obj, null, objectAttributes, StDeclaration.NoAttributes))
        {
            result.Add(Violation(GuidelineRules.HeaderCopyright, obj,
                $"The header must contain the copyright text: {_headers.Copyright}", null, null, null));
        }

        if (SeverityOf(GuidelineRules.HeaderDescription) != GuidelineSeverity.Off && !_headers.HasDescription(now)
            && (old is null || _headers.HasDescription(old))
            && !Suppressed(GuidelineRules.HeaderDescription, obj, null, objectAttributes, StDeclaration.NoAttributes))
        {
            result.Add(Violation(GuidelineRules.HeaderDescription, obj,
                $"The header needs a filled-in {_headers.DescriptionKeyword}.", null, null, null));
        }
    }

    // ------------------------------------------------------------------ exceptions

    private bool Suppressed(string rule, GuidelineObject obj, string? identifier, IReadOnlyDictionary<string, string> objectAttributes, IReadOnlyDictionary<string, string> attributes)
    {
        if (objectAttributes.ContainsKey("no-analysis") || attributes.ContainsKey("no-analysis"))
        {
            return true;
        }

        if (rule.StartsWith("naming.", StringComparison.Ordinal))
        {
            var naming = identifier is null ? objectAttributes : attributes;
            if (naming.TryGetValue("naming", out var mode) && mode is "off" or "omit")
            {
                return true;
            }
        }

        if (Ignores(objectAttributes, rule) || Ignores(attributes, rule))
        {
            return true;
        }

        var candidates = new[] { obj.Name, obj.Path, identifier, identifier is null ? null : $"{obj.Name}.{identifier}" };
        return GlobalExclude.Concat(_rules.TryGetValue(rule, out var r) ? r.Exclude : [])
            .Any(g => candidates.Any(g.IsMatch));
    }

    private static bool Ignores(IReadOnlyDictionary<string, string> attributes, string rule)
        => attributes.TryGetValue("mcp-ignore", out var list)
           && list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
               .Any(p => p == "*" || GuidelineRules.Match(p).Contains(rule));

    private GuidelineViolation Violation(string rule, GuidelineObject obj, string message, string? identifier, int? line, string? suggestion)
        => new(rule, GuidelineRules.Name(SeverityOf(rule)), message, obj.Path ?? obj.Name, identifier, line, suggestion);
}
