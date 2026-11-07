using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace TwinCatMcp.Core;

/// <summary>Compares the arguments of a tool call with the tool's JSON input schema.</summary>
public static class ArgumentCheck
{
    public sealed record Result(
        IReadOnlyList<string> Missing,
        IReadOnlyList<string> Unknown,
        IReadOnlyList<string> Expected,
        IReadOnlyDictionary<string, string> Suggestions);

    public static Result Analyze(JsonElement inputSchema, IEnumerable<string> givenArguments)
    {
        var given = givenArguments.ToList();
        var properties = new List<(string Name, string Type)>();
        if (inputSchema.ValueKind == JsonValueKind.Object && inputSchema.TryGetProperty("properties", out var props))
        {
            foreach (var p in props.EnumerateObject())
            {
                properties.Add((p.Name, TypeOf(p.Value)));
            }
        }

        var required = new HashSet<string>(StringComparer.Ordinal);
        if (inputSchema.ValueKind == JsonValueKind.Object && inputSchema.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in req.EnumerateArray())
            {
                if (r.GetString() is { } name)
                {
                    required.Add(name);
                }
            }
        }

        var known = properties.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var unknown = given.Where(g => !known.Contains(g)).ToList();
        var missing = required.Where(r => !given.Contains(r, StringComparer.Ordinal)).OrderBy(r => r, StringComparer.Ordinal).ToList();
        var suggestions = new Dictionary<string, string>();
        foreach (var u in unknown)
        {
            var names = properties.Select(p => p.Name).ToList();
            var match = names.FirstOrDefault(n => string.Equals(n, u, StringComparison.OrdinalIgnoreCase))
                        ?? names.FirstOrDefault(n => n.StartsWith(u, StringComparison.OrdinalIgnoreCase) || u.StartsWith(n, StringComparison.OrdinalIgnoreCase))
                        ?? names.Select(n => (Name: n, Distance: Distance(n.ToLowerInvariant(), u.ToLowerInvariant())))
                               .Where(x => x.Distance <= Math.Max(1, Math.Min(u.Length, x.Name.Length) / 3))
                               .OrderBy(x => x.Distance)
                               .Select(x => x.Name)
                               .FirstOrDefault();
            if (match is not null)
            {
                suggestions[u] = match;
            }
        }

        var expected = properties.Select(p => $"{p.Name}: {p.Type}{(required.Contains(p.Name) ? " (required)" : "")}").ToList();
        return new Result(missing, unknown, expected, suggestions);
    }

    /// <summary>Levenshtein distance.</summary>
    private static int Distance(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            prev[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            }

            (prev, cur) = (cur, prev);
        }

        return prev[b.Length];
    }

    private static string TypeOf(JsonElement schema)
    {
        if (!schema.TryGetProperty("type", out var t))
        {
            return "any";
        }

        return t.ValueKind switch
        {
            JsonValueKind.String => t.GetString() ?? "any",
            JsonValueKind.Array => string.Join("|", t.EnumerateArray().Select(x => x.GetString()).Where(x => x is not null && x != "null")),
            _ => "any",
        };
    }

    /// <summary>
    /// Call-tool filter: when the SDK reports a generic invocation failure (typically missing/misspelled/wrongly typed
    /// arguments), replace it with a precise, actionable error that names the expected parameters.
    /// </summary>
    public static IMcpRequestFilterBuilder AddArgumentDiagnostics(this IMcpRequestFilterBuilder filters)
        => filters.AddCallToolFilter(next => async (context, ct) =>
        {
            CallToolResult result;
            try
            {
                result = await next(context, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not ModelContextProtocol.McpException)
            {
                return Describe(context, ex.Message);
            }

            var text = result.IsError == true
                ? string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text))
                : null;
            return text is not null && text.StartsWith("An error occurred invoking", StringComparison.Ordinal)
                ? Describe(context, null)
                : result;
        });

    private static CallToolResult Describe(RequestContext<CallToolRequestParams> context, string? exceptionMessage)
    {
        var toolName = context.Params?.Name ?? "?";
        var schema = (context.MatchedPrimitive as McpServerTool)?.ProtocolTool.InputSchema
                     ?? context.Server.ServerOptions.ToolCollection?.FirstOrDefault(t => t.ProtocolTool.Name == toolName)?.ProtocolTool.InputSchema
                     ?? default;
        var given = context.Params?.Arguments?.Keys ?? (IEnumerable<string>)[];
        var a = Analyze(schema, given);

        var parts = new List<string>();
        if (a.Missing.Count > 0)
        {
            parts.Add("missing required: " + string.Join(", ", a.Missing));
        }

        if (a.Unknown.Count > 0)
        {
            parts.Add("unknown: " + string.Join(", ", a.Unknown.Select(u => a.Suggestions.TryGetValue(u, out var s) ? $"{u} (did you mean '{s}'?)" : u)));
        }

        var message = parts.Count > 0
            ? $"Invalid arguments for '{toolName}': {string.Join("; ", parts)}."
            : $"'{toolName}' could not process its arguments (a value probably has the wrong type or format)."
              + (exceptionMessage is null ? "" : " " + exceptionMessage);
        return ToolResult.Error(new TcException(message)
        {
            Hint = "Expected parameters: " + string.Join(", ", a.Expected),
        });
    }
}
