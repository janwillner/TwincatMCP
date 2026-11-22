using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace TwinCatMcp.Guidelines;

/// <summary>
/// The 'headers' section: comment templates inserted when an object is created, and what every header must contain.
/// Placeholders in templates: {name}, {kind}, {parent}, {date} (yyyy-MM-dd), {year}, {description}.
/// </summary>
internal sealed partial class HeaderRules
{
    private sealed record Assignment(HashSet<string> Kinds, Regex? Parent, Regex? Name, string Template);

    private readonly Dictionary<string, string> _templates = new(StringComparer.Ordinal);
    private readonly List<Assignment> _assignments = [];
    private readonly List<string> _templateFiles = [];

    /// <summary>The template files that were read.</summary>
    public IReadOnlyList<string> TemplateFiles => _templateFiles;

    /// <summary>Literal copyright text every header must contain (whitespace and &lt;br&gt; are ignored when comparing).</summary>
    public string? Copyright { get; private set; }

    /// <summary>Keyword that must be followed by a description, e.g. DESCRIPTION.</summary>
    public string? DescriptionKeyword { get; private set; }

    /// <summary>Text {description} is replaced with on creation; a description that still starts with it is not filled in.</summary>
    public string DescriptionPlaceholder { get; private set; } = "TODO";

    /// <summary>afterHeader (default): below the FUNCTION_BLOCK/METHOD/... line; top: first lines of the declaration.</summary>
    public bool AfterHeaderLine { get; private set; } = true;

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakRegex();

    public static HeaderRules Compile(JsonObject? headers)
    {
        var rules = new HeaderRules();
        if (headers is null)
        {
            return rules;
        }

        var known = new HashSet<string>(StringComparer.Ordinal) { "templates", "assign", "copyright", "description", "descriptionPlaceholder", "placement" };
        foreach (var key in headers.Select(p => p.Key).Where(k => !known.Contains(k)))
        {
            throw new GuidelineConfigException($"headers: unknown setting '{key}'. Known: {string.Join(", ", known)}.");
        }

        if (headers["templates"] is JsonObject templates)
        {
            foreach (var (name, value) in templates)
            {
                var files = GuidelineSet.Strings(value, $"headers.templates.{name}");
                var file = files.Length == 1 ? files[0] : throw new GuidelineConfigException($"headers.templates.{name} must be one file path.");
                rules._templates[name] = File.Exists(file)
                    ? File.ReadAllText(file).Replace("\r\n", "\n").TrimEnd('\n')
                    : throw new GuidelineConfigException($"headers.templates.{name}: file not found: {file}");
                rules._templateFiles.Add(file);
            }
        }

        if (headers["assign"] is { } assign)
        {
            if (assign is not JsonArray list)
            {
                throw new GuidelineConfigException("headers.assign must be an array of { \"kinds\": [...], \"parent\"?: regex, \"name\"?: regex, \"template\": name }.");
            }

            foreach (var entry in list.OfType<JsonObject>())
            {
                var template = GuidelineSet.Str(entry, "template") ?? throw new GuidelineConfigException("headers.assign: every entry needs 'template'.");
                if (!rules._templates.ContainsKey(template))
                {
                    throw new GuidelineConfigException($"headers.assign: template '{template}' is not defined in headers.templates.");
                }

                var kinds = GuidelineSet.Strings(entry["kinds"], "headers.assign.kinds").Select(k => k.ToUpperInvariant()).ToHashSet(StringComparer.Ordinal);
                rules._assignments.Add(new Assignment(
                    kinds,
                    GuidelineSet.Str(entry, "parent") is { } p ? CompileRegex(p, "headers.assign.parent") : null,
                    GuidelineSet.Str(entry, "name") is { } n ? CompileRegex(n, "headers.assign.name") : null,
                    template));
            }
        }

        rules.Copyright = GuidelineSet.Str(headers, "copyright");
        rules.DescriptionKeyword = GuidelineSet.Str(headers, "description");
        rules.DescriptionPlaceholder = GuidelineSet.Str(headers, "descriptionPlaceholder") ?? rules.DescriptionPlaceholder;
        rules.AfterHeaderLine = (GuidelineSet.Str(headers, "placement") ?? "afterHeader") switch
        {
            "afterHeader" => true,
            "top" => false,
            var other => throw new GuidelineConfigException($"headers.placement: unknown value '{other}'. Use 'afterHeader' or 'top'."),
        };
        return rules;
    }

    /// <summary>The template text that applies to an object (first matching assignment), null when none applies.</summary>
    public string? TemplateFor(GuidelineObject obj)
    {
        foreach (var a in _assignments)
        {
            if (a.Kinds.Contains(obj.Kind)
                && (a.Parent is null || (obj.ParentName is not null && a.Parent.IsMatch(obj.ParentName)))
                && (a.Name is null || a.Name.IsMatch(obj.Name)))
            {
                return _templates[a.Template];
            }
        }

        return null;
    }

    public string Render(string template, GuidelineObject obj, DateTime now)
        => template
            .Replace("{name}", obj.Name, StringComparison.Ordinal)
            .Replace("{kind}", obj.Kind, StringComparison.Ordinal)
            .Replace("{parent}", obj.ParentName ?? "", StringComparison.Ordinal)
            .Replace("{date}", now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{year}", now.ToString("yyyy", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{description}", DescriptionPlaceholder, StringComparison.Ordinal);

    /// <summary>
    /// True when the text already carries a header: the copyright, or else the first template line with real content
    /// (no placeholder, not only comment delimiters or border characters). A template without such a line never counts as present.
    /// </summary>
    public bool HasHeader(string text, string template)
    {
        var marker = Copyright ?? template.Split('\n')
            .Select(l => Clean(l))
            .FirstOrDefault(l => !l.Contains('{') && l.Any(char.IsLetterOrDigit));
        return marker is not null && Flatten(text).Contains(Flatten(marker), StringComparison.Ordinal);
    }

    public bool HasCopyright(string text) => Copyright is null || Flatten(text).Contains(Flatten(Copyright), StringComparison.Ordinal);

    /// <summary>The description after the keyword is present and not just the placeholder.</summary>
    public bool HasDescription(string text)
    {
        if (DescriptionKeyword is null)
        {
            return true;
        }

        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var at = lines[i].IndexOf(DescriptionKeyword, StringComparison.Ordinal);
            if (at < 0)
            {
                continue;
            }

            var rest = Clean(lines[i][(at + DescriptionKeyword.Length)..]).TrimStart(':').Trim();
            for (var j = i + 1; rest.Length == 0 && j < lines.Length; j++)
            {
                // End of the comment, or the next section keyword (PARAMETERS, INPUT VALUES ...): nothing was described.
                if (lines[j].Trim().StartsWith("*)", StringComparison.Ordinal))
                {
                    break;
                }

                var next = Clean(lines[j]);

                // Empty lines and border lines (****, ----, ====) are no description.
                if (next.Length == 0 || Regex.IsMatch(next, @"^[\*\-=_#~\s]+$"))
                {
                    continue;
                }

                // The next section keyword, e.g. 'PARAMETERS:' or 'INPUT VALUES:'.
                if (Regex.IsMatch(next, @"^[A-Z][A-Z ]*:$"))
                {
                    break;
                }

                rest = next;
            }

            return rest.Length > 0 && !rest.StartsWith(DescriptionPlaceholder, StringComparison.Ordinal);
        }

        return false;
    }

    private static Regex CompileRegex(string pattern, string where)
    {
        try
        {
            return new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException ex)
        {
            throw new GuidelineConfigException($"{where}: invalid regular expression '{pattern}' ({ex.Message}).");
        }
    }

    private static string Clean(string line) => BreakRegex().Replace(line, "").Replace("*)", "").Replace("(*", "").Trim();

    private static string Flatten(string text) => Regex.Replace(BreakRegex().Replace(text, " "), @"\s+", " ").Trim();
}
