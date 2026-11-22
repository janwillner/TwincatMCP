using System.Text.Json;
using System.Text.Json.Nodes;

namespace TwinCatMcp.Guidelines;

/// <summary>
/// Reads a .twincat-mcp.json file and the chain of files it extends, and merges them (base first): objects are merged
/// key by key, every other value (arrays included) is replaced. Settings that name files are resolved relative to the
/// file that sets them.
/// </summary>
internal static class GuidelineConfigFile
{
    public const string BuiltinPrefix = "builtin:";

    private static readonly HashSet<string> TopLevelKeys = new(StringComparer.Ordinal)
    {
        "$schema", "extends", "name", "description", "guide", "staticAnalysis", "defaultSeverity", "exclude", "rules",
        "naming", "declarations", "headers", "autoFix",
    };

    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public sealed record Merged(IReadOnlyList<string> Files, JsonObject Effective, IReadOnlyDictionary<string, string> Sources);

    public static Merged Load(string file, string builtinDirectory)
    {
        var chain = new List<(string Path, JsonObject Json)>();
        Collect(Path.GetFullPath(file), builtinDirectory, chain, []);
        chain.Reverse();

        var effective = new JsonObject();
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (path, json) in chain)
        {
            json.Remove("extends");
            json.Remove("$schema");
            Merge(effective, json, "", path, sources);
        }

        return new Merged(chain.Select(c => c.Path).ToArray(), effective, sources);
    }

    /// <summary>Path of a 'builtin:&lt;name&gt;' reference: the shipped example file .twincat-mcp.&lt;name&gt;.json.</summary>
    public static string? BuiltinPath(string name, string builtinDirectory)
    {
        if (!Directory.Exists(builtinDirectory))
        {
            return null;
        }

        var wanted = $".twincat-mcp.{name}.json";
        return Directory.EnumerateFiles(builtinDirectory, ".twincat-mcp.*.json")
            .FirstOrDefault(f => string.Equals(Path.GetFileName(f), wanted, StringComparison.OrdinalIgnoreCase));
    }

    private static void Collect(string file, string builtinDirectory, List<(string, JsonObject)> chain, List<string> visiting)
    {
        if (visiting.Contains(file, StringComparer.OrdinalIgnoreCase))
        {
            throw new GuidelineConfigException($"'extends' forms a cycle: {string.Join(" -> ", visiting.Append(file))}.");
        }

        if (!File.Exists(file))
        {
            throw new GuidelineConfigException(visiting.Count == 0
                ? $"Guideline config not found: {file}"
                : $"'{visiting[^1]}' extends '{file}', which does not exist.");
        }

        var json = Parse(file);
        ResolvePaths(json, Path.GetDirectoryName(file)!);
        chain.Add((file, json));

        if (json["extends"] is not { } extends)
        {
            return;
        }

        if (extends.GetValueKind() != JsonValueKind.String || string.IsNullOrWhiteSpace(extends.GetValue<string>()))
        {
            throw new GuidelineConfigException($"{file}: 'extends' must be a file path or 'builtin:<name>'.");
        }

        var target = extends.GetValue<string>().Trim();
        string resolved;
        if (target.StartsWith(BuiltinPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var name = target[BuiltinPrefix.Length..];
            resolved = BuiltinPath(name, builtinDirectory)
                ?? throw new GuidelineConfigException(
                    $"{file}: unknown built-in guideline '{target}'. Shipped: {string.Join(", ", BuiltinNames(builtinDirectory).Select(n => BuiltinPrefix + n))}.");
        }
        else
        {
            resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, target));
        }

        visiting.Add(file);
        Collect(resolved, builtinDirectory, chain, visiting);
        visiting.RemoveAt(visiting.Count - 1);
    }

    public static IEnumerable<string> BuiltinNames(string builtinDirectory)
        => Directory.Exists(builtinDirectory)
            ? Directory.EnumerateFiles(builtinDirectory, ".twincat-mcp.*.json")
                .Select(f => Path.GetFileName(f)[".twincat-mcp.".Length..^".json".Length].ToLowerInvariant())
                .Order()
            : [];

    private static JsonObject Parse(string file)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(File.ReadAllText(file), documentOptions: ParseOptions);
        }
        catch (JsonException ex)
        {
            throw new GuidelineConfigException($"{file}: invalid JSON ({ex.Message}).");
        }

        if (node is not JsonObject obj)
        {
            throw new GuidelineConfigException($"{file}: the root must be a JSON object.");
        }

        var unknown = obj.Select(p => p.Key).Where(k => !TopLevelKeys.Contains(k)).ToArray();
        if (unknown.Length > 0)
        {
            throw new GuidelineConfigException(
                $"{file}: unknown setting(s) {string.Join(", ", unknown.Select(k => $"'{k}'"))}. Known: {string.Join(", ", TopLevelKeys.Order())}.");
        }

        return obj;
    }

    /// <summary>Makes file settings absolute, relative to the directory of the file that sets them.</summary>
    private static void ResolvePaths(JsonObject json, string directory)
    {
        static void Resolve(JsonObject parent, string key, string directory)
        {
            if (parent[key] is JsonValue v && v.GetValueKind() == JsonValueKind.String && v.GetValue<string>() is { Length: > 0 } path)
            {
                parent[key] = Path.GetFullPath(Path.Combine(directory, path));
            }
        }

        Resolve(json, "guide", directory);
        if (json["staticAnalysis"] is JsonObject sa)
        {
            Resolve(sa, "settingsFile", directory);
        }

        if (json["headers"] is JsonObject headers && headers["templates"] is JsonObject templates)
        {
            foreach (var key in templates.Select(p => p.Key).ToArray())
            {
                Resolve(templates, key, directory);
            }
        }
    }

    private static void Merge(JsonObject target, JsonObject source, string prefix, string file, Dictionary<string, string> sources)
    {
        foreach (var (key, value) in source.ToArray())
        {
            var path = prefix.Length == 0 ? key : $"{prefix}.{key}";
            if (value is JsonObject obj && target[key] is JsonObject existing)
            {
                Merge(existing, obj, path, file, sources);
                continue;
            }

            foreach (var stale in sources.Keys.Where(k => k == path || k.StartsWith(path + ".", StringComparison.Ordinal)).ToArray())
            {
                sources.Remove(stale);
            }

            target[key] = value?.DeepClone();
            Record(value, path, file, sources);
        }
    }

    private static void Record(JsonNode? value, string path, string file, Dictionary<string, string> sources)
    {
        if (value is JsonObject obj && obj.Count > 0)
        {
            foreach (var (key, child) in obj)
            {
                Record(child, $"{path}.{key}", file, sources);
            }
        }
        else
        {
            sources[path] = file;
        }
    }
}
