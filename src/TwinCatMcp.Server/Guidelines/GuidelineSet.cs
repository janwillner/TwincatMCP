using System.Text.Json;
using System.Text.Json.Nodes;

namespace TwinCatMcp.Guidelines;

/// <summary>
/// The effective coding guidelines of one solution: the merged .twincat-mcp.json chain, compiled into checks.
/// Pure logic (no COM) - the tools pass in names and declaration/implementation texts.
/// </summary>
public sealed partial class GuidelineSet
{
    private readonly Dictionary<string, RuleSetting> _rules;

    private GuidelineSet(GuidelineConfigFile.Merged merged)
    {
        Files = merged.Files;
        Effective = merged.Effective;
        Sources = merged.Sources;
        Name = Str(Effective, "name");
        Description = Str(Effective, "description");
        DefaultSeverity = Str(Effective, "defaultSeverity") is { } d
            ? GuidelineRules.ParseSeverity(d, "defaultSeverity")
            : GuidelineSeverity.Warn;
        GlobalExclude = Globs(Effective["exclude"], "exclude");
        StaticAnalysisSettingsFile = Str(Effective["staticAnalysis"] as JsonObject, "settingsFile");

        if (Str(Effective, "guide") is { } guide)
        {
            GuidePath = guide;
            GuideMarkdown = File.Exists(guide)
                ? File.ReadAllText(guide)
                : throw new GuidelineConfigException($"guide file not found: {guide} (set in {Sources.GetValueOrDefault("guide")}).");
        }

        _naming = NamingRules.Compile(Effective["naming"] as JsonObject);
        _declarations = DeclarationRules.Compile(Effective["declarations"] as JsonObject);
        _headers = HeaderRules.Compile(Effective["headers"] as JsonObject);
        AutoFixHeaders = Bool(Effective["autoFix"] as JsonObject, "headers") ?? true;
        AutoFixAttributes = Bool(Effective["autoFix"] as JsonObject, "requiredAttributes") ?? true;
        _rules = CompileRules(Effective["rules"]);
    }

    /// <summary>Loads a .twincat-mcp.json file. 'builtin:&lt;name&gt;' references resolve to the shipped example files in builtinDirectory.</summary>
    public static GuidelineSet Load(string configFile, string builtinDirectory)
        => new(GuidelineConfigFile.Load(configFile, builtinDirectory));

    /// <summary>The files of the chain, base first, the solution's own file last.</summary>
    public IReadOnlyList<string> Files { get; }

    /// <summary>Every file the guidelines were built from: the config chain, the guide and the header templates.</summary>
    public IReadOnlyList<string> DependencyFiles
        => [.. Files, .. GuidePath is null ? Array.Empty<string>() : [GuidePath], .. _headers.TemplateFiles];

    /// <summary>Merged configuration (extends removed, file settings absolute).</summary>
    public JsonObject Effective { get; }

    /// <summary>Dotted setting path -> file that set the value.</summary>
    public IReadOnlyDictionary<string, string> Sources { get; }

    public string? Name { get; }

    public string? Description { get; }

    public string? GuidePath { get; }

    /// <summary>The prose style guide (Markdown) named by 'guide', null without one.</summary>
    public string? GuideMarkdown { get; }

    public string? StaticAnalysisSettingsFile { get; }

    public GuidelineSeverity DefaultSeverity { get; }

    public bool AutoFixHeaders { get; }

    public bool AutoFixAttributes { get; }

    private IReadOnlyList<Glob> GlobalExclude { get; }

    /// <summary>Active rules (severity not off) with their severity.</summary>
    public IReadOnlyDictionary<string, string> ActiveRules
        => _rules.Where(r => r.Value.Severity != GuidelineSeverity.Off)
            .OrderBy(r => r.Key, StringComparer.Ordinal)
            .ToDictionary(r => r.Key, r => GuidelineRules.Name(r.Value.Severity));

    public GuidelineSeverity SeverityOf(string rule) => _rules.TryGetValue(rule, out var r) ? r.Severity : GuidelineSeverity.Off;

    private sealed record RuleSetting(GuidelineSeverity Severity, IReadOnlyList<Glob> Exclude);

    private Dictionary<string, RuleSetting> CompileRules(JsonNode? node)
    {
        var explicitly = new Dictionary<string, RuleSetting>(StringComparer.Ordinal);
        if (node is not null and not JsonObject)
        {
            throw new GuidelineConfigException("'rules' must be an object: { \"<rule key>\": \"off|warn|error\" | { \"severity\": ..., \"exclude\": [...] } }.");
        }

        foreach (var (pattern, value) in (JsonObject?)node ?? [])
        {
            var keys = GuidelineRules.Match(pattern).ToArray();
            if (keys.Length == 0)
            {
                throw new GuidelineConfigException(
                    $"rules: unknown rule '{pattern}'. Known rules: {string.Join(", ", GuidelineRules.All)} (patterns like 'naming.var.*' are allowed).");
            }

            RuleSetting setting = value switch
            {
                JsonValue v when v.GetValueKind() == JsonValueKind.String
                    => new(GuidelineRules.ParseSeverity(v.GetValue<string>(), $"rules.{pattern}"), []),
                JsonObject o => new(
                    Str(o, "severity") is { } sev ? GuidelineRules.ParseSeverity(sev, $"rules.{pattern}.severity") : DefaultSeverity,
                    Globs(o["exclude"], $"rules.{pattern}.exclude")),
                _ => throw new GuidelineConfigException($"rules.{pattern}: use \"off\", \"warn\", \"error\" or {{ \"severity\": ..., \"exclude\": [...] }}."),
            };

            // A pattern sets every rule it matches; a later exact key wins over an earlier pattern.
            foreach (var key in keys)
            {
                explicitly[key] = setting;
            }
        }

        var rules = new Dictionary<string, RuleSetting>(StringComparer.Ordinal);
        foreach (var key in GuidelineRules.All)
        {
            // A naming category without template has nothing to check, whatever its severity says.
            if (key.StartsWith("naming.", StringComparison.Ordinal) && key != GuidelineRules.UnknownType && !_naming.HasTemplate(key))
            {
                continue;
            }

            if (explicitly.TryGetValue(key, out var s))
            {
                rules[key] = s;
            }
            else if (IsConfigured(key))
            {
                rules[key] = new RuleSetting(DefaultSeverity, []);
            }
        }

        return rules;
    }

    /// <summary>A rule without an entry in 'rules' is active (with defaultSeverity) when its settings are configured.</summary>
    private bool IsConfigured(string rule) => rule switch
    {
        GuidelineRules.UnknownType or GuidelineRules.EnumExplicitValues or GuidelineRules.MethodInputDefaults => false,
        GuidelineRules.RequiredAttributes => _declarations.RequiredAttributes.Count > 0,
        GuidelineRules.EnumValueRange => _declarations.EnumRange is not null,
        GuidelineRules.HeaderCopyright => _headers.Copyright is not null,
        GuidelineRules.HeaderDescription => _headers.DescriptionKeyword is not null,
        _ when rule.StartsWith("naming.", StringComparison.Ordinal) => _naming.HasTemplate(rule),
        _ => false,
    };

    internal static string? Str(JsonObject? obj, string key)
        => obj?[key] is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;

    internal static bool? Bool(JsonObject? obj, string key)
        => obj?[key] is JsonValue v && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False ? v.GetValue<bool>() : null;

    internal static int? Int(JsonObject? obj, string key) => obj?[key] switch
    {
        null => null,
        JsonValue v when v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<int>(out var n) => n,
        _ => throw new GuidelineConfigException($"'{key}' must be a whole number."),
    };

    /// <summary>A string or an array of strings (alternatives).</summary>
    internal static string[] Strings(JsonNode? node, string where) => node switch
    {
        null => [],
        JsonValue v when v.GetValueKind() == JsonValueKind.String => [v.GetValue<string>()],
        JsonArray a => a.Select(n => n is JsonValue x && x.GetValueKind() == JsonValueKind.String
            ? x.GetValue<string>()
            : throw new GuidelineConfigException($"{where}: every entry must be a string.")).ToArray(),
        _ => throw new GuidelineConfigException($"{where}: expected a string or an array of strings."),
    };

    private static IReadOnlyList<Glob> Globs(JsonNode? node, string where) => Strings(node, where).Select(g => new Glob(g)).ToArray();
}
