using System.Text.Json.Nodes;

namespace TwinCatMcp.Guidelines;

/// <summary>Allowed range of explicit ENUM values; values from ExtensionMin on are local extensions named '&lt;ENUM&gt;_&lt;VALUE&gt;'.</summary>
internal sealed record EnumRange(int Min, int? Max, int? ExtensionMin);

/// <summary>The 'declarations' section: fixed rule kinds with parameters.</summary>
internal sealed class DeclarationRules
{
    private static readonly HashSet<string> ObjectKinds = new(StringComparer.Ordinal)
    {
        "PROGRAM", "FUNCTION_BLOCK", "FUNCTION", "METHOD", "PROPERTY", "INTERFACE", "STRUCT", "ENUM", "UNION", "ALIAS", "GVL",
        "PARAMETER_LIST",
    };

    /// <summary>Object kind (PlcObjects.KindOf, e.g. ENUM, GVL) -> attributes its declaration must carry.</summary>
    public IReadOnlyDictionary<string, string[]> RequiredAttributes { get; private init; } = new Dictionary<string, string[]>();

    public EnumRange? EnumRange { get; private init; }

    public static DeclarationRules Compile(JsonObject? declarations)
    {
        if (declarations is null)
        {
            return new DeclarationRules();
        }

        foreach (var key in declarations.Select(p => p.Key).Where(k => k is not ("requiredAttributes" or "enum")))
        {
            throw new GuidelineConfigException($"declarations: unknown setting '{key}'. Known: requiredAttributes, enum.");
        }

        var required = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (declarations["requiredAttributes"] is { } req)
        {
            if (req is not JsonObject obj)
            {
                throw new GuidelineConfigException("declarations.requiredAttributes must map an object kind to attribute names, e.g. { \"ENUM\": [\"qualified_only\", \"strict\"] }.");
            }

            foreach (var (kind, value) in obj)
            {
                if (!ObjectKinds.Contains(kind))
                {
                    throw new GuidelineConfigException($"declarations.requiredAttributes: unknown object kind '{kind}'. Known: {string.Join(", ", ObjectKinds)}.");
                }

                required[kind] = GuidelineSet.Strings(value, $"declarations.requiredAttributes.{kind}").Select(a => a.Trim().ToLowerInvariant()).ToArray();
            }
        }

        EnumRange? range = null;
        if (declarations["enum"] is { } e)
        {
            if (e is not JsonObject eo || GuidelineSet.Int(eo, "min") is not { } min)
            {
                throw new GuidelineConfigException("declarations.enum needs at least 'min' (and optionally 'max', 'extensionMin').");
            }

            range = new EnumRange(min, GuidelineSet.Int(eo, "max"), GuidelineSet.Int(eo, "extensionMin"));
        }

        return new DeclarationRules { RequiredAttributes = required, EnumRange = range };
    }
}
