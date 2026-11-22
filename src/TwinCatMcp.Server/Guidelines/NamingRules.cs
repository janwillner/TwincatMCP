using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace TwinCatMcp.Guidelines;

/// <summary>Result of checking one name against its naming template.</summary>
internal sealed record NameCheck(bool Ok, bool UnknownType, string Expected, string? Suggestion);

/// <summary>
/// The 'naming' section: token templates per identifier category and the tables that give the {type} token its prefix.
/// Templates: literal text plus {type}, {Name} (PascalCase part), {name} (any identifier part), {NAME} (UPPER_SNAKE part)
/// and {*} (free text the guideline leaves to the user). A template starting with '@' is a .NET regex in which the same
/// tokens can be used. A list of templates means alternatives.
/// </summary>
internal sealed partial class NamingRules
{
    private const string NamePart = "[A-Z][A-Za-z0-9_]*";
    private const string AnyNamePart = "[A-Za-z][A-Za-z0-9_]*";
    private const string UpperPart = "[A-Z][A-Z0-9_]*";
    private const string FreePart = "[A-Za-z0-9_]*?";
    private const string UnknownTypePart = "(?:[a-z][a-z0-9]*_?)?";

    private static readonly string[] Wrappers = ["ARRAY", "POINTER", "REFERENCE"];

    private static readonly HashSet<string> Kinds = new(StringComparer.Ordinal)
    {
        "functionBlock", "struct", "enum", "union", "alias", "interface",
    };

    private readonly Dictionary<string, string[]> _templates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string[]> _typePrefixes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string[]> _kindPrefixes = new(StringComparer.Ordinal);
    private readonly List<(Regex Match, string Kind)> _typeKinds = [];
    private readonly List<(Glob Glob, string[] Prefixes)> _types = [];
    private readonly Dictionary<string, Regex> _regexCache = new(StringComparer.Ordinal);
    private bool _composite = true;

    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"^ARRAY\s*\[.*?\]\s*OF\s+(?<inner>.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ArrayRegex();

    [GeneratedRegex(@"^(?<wrapper>POINTER|REFERENCE)\s+TO\s+(?<inner>.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex PointerRegex();

    public static NamingRules Compile(JsonObject? naming)
    {
        var rules = new NamingRules();
        if (naming is null)
        {
            return rules;
        }

        var known = new HashSet<string>(StringComparer.Ordinal) { "compositePrefixes", "typePrefixes", "kindPrefixes", "typeKinds", "types", "templates" };
        foreach (var key in naming.Select(p => p.Key).Where(k => !known.Contains(k)))
        {
            throw new GuidelineConfigException($"naming: unknown setting '{key}'. Known: {string.Join(", ", known)}.");
        }

        rules._composite = GuidelineSet.Bool(naming, "compositePrefixes") ?? true;
        foreach (var (type, value) in Obj(naming, "typePrefixes"))
        {
            rules._typePrefixes[type] = GuidelineSet.Strings(value, $"naming.typePrefixes.{type}");
        }

        foreach (var (kind, value) in Obj(naming, "kindPrefixes"))
        {
            if (!Kinds.Contains(kind))
            {
                throw new GuidelineConfigException($"naming.kindPrefixes: unknown kind '{kind}'. Known: {string.Join(", ", Kinds)}.");
            }

            rules._kindPrefixes[kind] = GuidelineSet.Strings(value, $"naming.kindPrefixes.{kind}");
        }

        if (naming["typeKinds"] is { } typeKinds)
        {
            if (typeKinds is not JsonArray list)
            {
                throw new GuidelineConfigException("naming.typeKinds must be an array of { \"match\": \"<regex on the type name>\", \"kind\": \"<kind>\" }.");
            }

            foreach (var entry in list)
            {
                var match = GuidelineSet.Str(entry as JsonObject, "match");
                var kind = GuidelineSet.Str(entry as JsonObject, "kind");
                if (match is null || kind is null || !Kinds.Contains(kind))
                {
                    throw new GuidelineConfigException(
                        $"naming.typeKinds: every entry needs 'match' (regex) and 'kind' ({string.Join(", ", Kinds)}).");
                }

                rules._typeKinds.Add((CompileRegex(match, "naming.typeKinds"), kind));
            }
        }

        foreach (var (type, value) in Obj(naming, "types"))
        {
            rules._types.Add((new Glob(type), GuidelineSet.Strings(value, $"naming.types.{type}")));
        }

        // Exact type names win over patterns; among patterns the file order decides (stable sort).
        var ordered = rules._types.OrderBy(t => t.Glob.HasWildcard).ToList();
        rules._types.Clear();
        rules._types.AddRange(ordered);

        foreach (var (key, value) in Obj(naming, "templates"))
        {
            var rule = "naming." + key;
            if (!GuidelineRules.All.Contains(rule) || rule == GuidelineRules.UnknownType)
            {
                throw new GuidelineConfigException(
                    $"naming.templates: unknown category '{key}'. Known: {string.Join(", ", GuidelineRules.All.Where(r => r.StartsWith("naming.", StringComparison.Ordinal) && r != GuidelineRules.UnknownType).Select(r => r["naming.".Length..]))}.");
            }

            var templates = GuidelineSet.Strings(value, $"naming.templates.{key}");
            foreach (var t in templates)
            {
                // Validate tokens and regex syntax once with a known and an unknown type.
                rules.Build(t, [""]);
                rules.Build(t, null);
            }

            rules._templates[rule] = templates;
        }

        foreach (var (_, kind) in rules._typeKinds)
        {
            if (!rules._kindPrefixes.ContainsKey(kind))
            {
                throw new GuidelineConfigException($"naming.typeKinds uses kind '{kind}', but naming.kindPrefixes has no prefix for it.");
            }
        }

        return rules;
    }

    public bool HasTemplate(string rule) => _templates.ContainsKey(rule);

    /// <summary>Checks a name against the templates of a rule. type = declared data type (null for object names).</summary>
    public NameCheck Check(string rule, string name, string? type)
    {
        var templates = _templates[rule];
        var prefixes = type is null ? [""] : PrefixesOf(type);
        var unknown = type is not null && prefixes is null;
        var ok = templates.Any(t => Build(t, prefixes).IsMatch(name));
        var expected = string.Join(" | ", templates.Select(t => Describe(t, prefixes)));
        return new NameCheck(ok, unknown, expected, ok ? null : Suggest(templates, name, prefixes));
    }

    /// <summary>The allowed {type} prefixes of a declared type; null when the type is unknown to the configuration.</summary>
    public string[]? PrefixesOf(string type)
    {
        var wrappers = new List<string>();
        var t = type.Trim();
        while (true)
        {
            if (ArrayRegex().Match(t) is { Success: true } a)
            {
                wrappers.Add("ARRAY");
                t = a.Groups["inner"].Value.Trim();
            }
            else if (PointerRegex().Match(t) is { Success: true } p)
            {
                wrappers.Add(p.Groups["wrapper"].Value.ToUpperInvariant());
                t = p.Groups["inner"].Value.Trim();
            }
            else
            {
                break;
            }
        }

        var wrapperPrefixes = wrappers.Select(w => _typePrefixes.TryGetValue(w, out var x) ? x : [""]).ToList();
        if (!_composite && wrapperPrefixes.Count > 0)
        {
            return wrapperPrefixes[0];
        }

        var basePrefixes = BasePrefixes(t);
        if (basePrefixes is null)
        {
            return null;
        }

        IEnumerable<string> combined = [""];
        foreach (var alternatives in wrapperPrefixes.Append(basePrefixes))
        {
            combined = combined.SelectMany(c => alternatives.Select(a => c + a)).ToArray();
        }

        return combined.Distinct(StringComparer.Ordinal).ToArray();
    }

    private string[]? BasePrefixes(string type)
    {
        // STRING(80), WSTRING[255], INT (0..10) -> STRING, WSTRING, INT
        var name = Regex.Replace(type, @"\s*[\(\[].*$", "", RegexOptions.Singleline).Trim();
        var shortName = name.Contains('.') ? name[(name.LastIndexOf('.') + 1)..] : name;

        foreach (var (glob, prefixes) in _types)
        {
            if (glob.IsMatch(name) || glob.IsMatch(shortName))
            {
                return prefixes;
            }
        }

        if (_typePrefixes.TryGetValue(name, out var p) || _typePrefixes.TryGetValue(shortName, out p))
        {
            return p;
        }

        foreach (var (match, kind) in _typeKinds)
        {
            if (match.IsMatch(shortName))
            {
                return _kindPrefixes[kind];
            }
        }

        return null;
    }

    private Regex Build(string template, string[]? typePrefixes)
    {
        var key = template + "\u0001" + (typePrefixes is null ? "\u0002" : string.Join("\u0001", typePrefixes));
        if (_regexCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var isRegex = template.StartsWith('@');
        var body = isRegex ? template[1..] : template;
        var sb = new StringBuilder();
        var last = 0;
        foreach (Match m in TokenRegex().Matches(body))
        {
            var literal = body[last..m.Index];
            sb.Append(isRegex ? literal : Regex.Escape(literal));
            sb.Append(Token(m.Groups[1].Value, typePrefixes, template));
            last = m.Index + m.Length;
        }

        var rest = body[last..];
        sb.Append(isRegex ? rest : Regex.Escape(rest));
        var pattern = sb.ToString();
        if (!pattern.StartsWith('^'))
        {
            pattern = "^(?:" + pattern + ")";
        }

        if (!pattern.EndsWith('$'))
        {
            pattern += "$";
        }

        var regex = CompileRegex(pattern, $"naming template '{template}'");
        _regexCache[key] = regex;
        return regex;
    }

    private static string Token(string token, string[]? typePrefixes, string template) => token switch
    {
        "type" => typePrefixes is null
            ? UnknownTypePart
            : "(?:" + string.Join("|", typePrefixes.OrderByDescending(p => p.Length).Select(Regex.Escape)) + ")",
        "Name" => NamePart,
        "name" => AnyNamePart,
        "NAME" => UpperPart,
        "*" => FreePart,
        _ => throw new GuidelineConfigException(
            $"naming template '{template}': unknown token '{{{token}}}'. Use {{type}}, {{Name}}, {{name}}, {{NAME}} or {{*}}."),
    };

    private static string Describe(string template, string[]? typePrefixes)
    {
        var type = typePrefixes is null ? "<type prefix>"
            : typePrefixes.Length == 1 ? typePrefixes[0]
            : "(" + string.Join("|", typePrefixes.Select(p => p.Length == 0 ? "<none>" : p)) + ")";
        return template.Replace("{type}", type, StringComparison.Ordinal);
    }

    /// <summary>A corrected name when it follows unambiguously from the template; otherwise null.</summary>
    private string? Suggest(string[] templates, string name, string[]? typePrefixes)
    {
        if (typePrefixes is null)
        {
            return null;
        }

        foreach (var template in templates.Where(t => !t.StartsWith('@') && !t.Contains("{*}", StringComparison.Ordinal)))
        {
            var tokens = TokenRegex().Matches(template).Select(m => m.Groups[1].Value).ToArray();
            if (tokens.Count(t => t is "Name" or "name" or "NAME") != 1)
            {
                continue;
            }

            var core = Core(name, template);
            if (core.Length == 0)
            {
                continue;
            }

            var candidate = TokenRegex().Replace(template, m => m.Groups[1].Value switch
            {
                "type" => typePrefixes[0],
                "NAME" => UpperSnake(core),
                _ => char.ToUpperInvariant(core[0]) + core[1..],
            });

            if (Build(template, typePrefixes).IsMatch(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>The descriptive part of a name: known scope/type prefixes and a Hungarian lower-case prefix removed.</summary>
    private string Core(string name, string template)
    {
        var literals = _templates.Values.SelectMany(t => t)
            .Where(t => !t.StartsWith('@'))
            .SelectMany(t => TokenRegex().Split(t).Where((_, i) => i % 2 == 0))
            .Where(l => l.Length > 0)
            .Concat(_typePrefixes.Values.SelectMany(v => v))
            .Concat(_kindPrefixes.Values.SelectMany(v => v))
            .Concat(_types.SelectMany(t => t.Prefixes))
            .Where(p => p.Length > 0 && (p.EndsWith('_') || p.StartsWith('_')))
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(p => p.Length)
            .ToArray();

        var core = name;
        for (var changed = true; changed && core.Length > 0;)
        {
            changed = false;
            foreach (var l in literals)
            {
                if (core.Length > l.Length && core.StartsWith(l, StringComparison.OrdinalIgnoreCase))
                {
                    core = core[l.Length..];
                    changed = true;
                    break;
                }
            }
        }

        // Hungarian notation of another convention: bEnable -> Enable, arValues -> Values.
        core = Regex.Replace(core, "^[a-z]{1,5}(?=[A-Z])", "");

        // Literal text the template puts in front of the name part, e.g. 'Temp' in '{type}Temp{Name}'.
        var beforeName = TokenRegex().Split(template).Where((_, i) => i % 2 == 0).Skip(1).FirstOrDefault(l => l.Length > 0 && !l.EndsWith('_'));
        if (beforeName is not null && core.StartsWith(beforeName, StringComparison.Ordinal) && core.Length > beforeName.Length)
        {
            core = core[beforeName.Length..];
        }

        return core.TrimStart('_');
    }

    private static string UpperSnake(string text)
        => text.ToUpperInvariant() == text ? text : Regex.Replace(text, "(?<=[a-z0-9])(?=[A-Z])", "_").ToUpperInvariant();

    private static IEnumerable<KeyValuePair<string, JsonNode?>> Obj(JsonObject parent, string key)
        => parent[key] switch
        {
            null => [],
            JsonObject o => o,
            _ => throw new GuidelineConfigException($"naming.{key} must be an object."),
        };

    private static Regex CompileRegex(string pattern, string where)
    {
        try
        {
            return new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException ex)
        {
            throw new GuidelineConfigException($"{where}: invalid regular expression ({ex.Message}).");
        }
    }

    internal static bool IsString(JsonNode? node) => node is JsonValue v && v.GetValueKind() == JsonValueKind.String;
}
