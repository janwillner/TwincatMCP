using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TwinCatMcp.Plc;

public sealed record StVariable(string Name, string Type, string? Init = null, string? Address = null, string? Comment = null)
{
    /// <summary>
    /// {attribute '...'} pragmas that apply to this declaration (name lower case -> value, "" without value).
    /// A covering {attribute 'naming' := 'off'} region shows up as naming = off.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, string> Attributes { get; init; } = StDeclaration.NoAttributes;

    [JsonPropertyName("attributes")]
    public IReadOnlyDictionary<string, string>? AttributesOrNull => Attributes.Count == 0 ? null : Attributes;

    /// <summary>1-based line of the declaration in the declaration text (first line of a multi-line declaration).</summary>
    public int Line { get; init; }
}

public sealed record StVarSection(string Kind, List<StVariable> Variables);

/// <summary>One value of an ENUM declaration; Value is the text after ':=' or null when the value is implicit.</summary>
public sealed record StEnumValue(string Name, string? Value);

public sealed record StDeclarationInfo(
    string? Kind,
    string? Name,
    string? Access,
    string? ReturnType,
    string? Extends,
    string[] Implements,
    List<StVarSection> Sections)
{
    /// <summary>{attribute '...'} pragmas before the header line (for a GVL: before VAR_GLOBAL).</summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, string> Attributes { get; init; } = StDeclaration.NoAttributes;

    [JsonPropertyName("attributes")]
    public IReadOnlyDictionary<string, string>? AttributesOrNull => Attributes.Count == 0 ? null : Attributes;

    /// <summary>Values of an ENUM type (empty for every other kind).</summary>
    [JsonIgnore]
    public IReadOnlyList<StEnumValue> EnumValues { get; init; } = [];

    [JsonPropertyName("enumValues")]
    public IReadOnlyList<StEnumValue>? EnumValuesOrNull => EnumValues.Count == 0 ? null : EnumValues;

    /// <summary>Base type written after the closing bracket of an ENUM, e.g. INT.</summary>
    public string? EnumBaseType { get; init; }
}

/// <summary>
/// Lightweight parser for the declaration area of POUs, DUTs and GVLs (header + VAR / STRUCT sections).
/// It is tolerant: unknown constructs are skipped, it never throws on odd input.
/// </summary>
public static partial class StDeclaration
{
    public static readonly IReadOnlyDictionary<string, string> NoAttributes = new Dictionary<string, string>();

    private static readonly HashSet<string> Modifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "ABSTRACT", "FINAL", "PUBLIC", "PRIVATE", "PROTECTED", "INTERNAL",
    };

    private static readonly HashSet<string> AccessModifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "PUBLIC", "PRIVATE", "PROTECTED", "INTERNAL",
    };

    [GeneratedRegex(@"^(PROGRAM|FUNCTION_BLOCK|FUNCTION|METHOD|PROPERTY|INTERFACE|TYPE|ACTION)\b(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex HeaderRegex();

    [GeneratedRegex(@"^(VAR(_INPUT|_OUTPUT|_IN_OUT|_GLOBAL|_STAT|_TEMP|_INST|_EXTERNAL|_CONFIG)?|STRUCT|UNION)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SectionStartRegex();

    [GeneratedRegex(@"^(END_VAR|END_STRUCT|END_UNION)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SectionEndRegex();

    [GeneratedRegex(@"^(?<names>.+?)(\s+AT\s+(?<addr>\S+))?\s*:(?!=)\s*(?<rest>.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex VarRegex();

    [GeneratedRegex(@"^\s*attribute\s+'(?<name>[^']+)'(\s*:=\s*'(?<value>[^']*)')?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex AttributeRegex();

    /// <summary>First keyword a declaration of the given object type must start with (null = no fixed header, e.g. accessors).</summary>
    private static string? RequiredHeader(int itemType) => itemType switch
    {
        PlcObjects.Program => "PROGRAM",
        PlcObjects.FunctionBlock => "FUNCTION_BLOCK",
        PlcObjects.Function => "FUNCTION",
        PlcObjects.Method or PlcObjects.InterfaceMethod => "METHOD",
        PlcObjects.Property or PlcObjects.InterfaceProperty => "PROPERTY",
        PlcObjects.Interface => "INTERFACE",
        PlcObjects.Struct or PlcObjects.Enum or PlcObjects.Union or PlcObjects.Alias => "TYPE",
        PlcObjects.Gvl or PlcObjects.ParameterList => "VAR_GLOBAL",
        _ => null,
    };

    /// <summary>
    /// Null when the declaration starts with the header the object type requires (pragmas and comments may precede it),
    /// otherwise an explanation. TwinCAT itself stores a declaration without header silently and the object breaks.
    /// </summary>
    public static string? HeaderError(int itemType, string declaration)
    {
        if (RequiredHeader(itemType) is not { } required)
        {
            return null;
        }

        var firstLine = StripComments(StCode.Normalize(declaration ?? "")).Select(l => l.Code.Trim()).FirstOrDefault(l => l.Length > 0);
        var found = firstLine is null ? "nothing" : Regex.Match(firstLine, @"^[A-Za-z_][A-Za-z0-9_]*").Value is { Length: > 0 } word ? word.ToUpperInvariant() : firstLine;
        if (found == required)
        {
            return null;
        }

        var example = required switch
        {
            "VAR_GLOBAL" => "VAR_GLOBAL",
            "TYPE" => "TYPE <name> :",
            "METHOD" or "PROPERTY" or "FUNCTION" => $"{required} <name> : <type>",
            _ => $"{required} <name>",
        };
        return $"The declaration of a {PlcObjects.KindOf(itemType)} must start with '{example}' (pragmas and comments may come before it), but found '{found}'.";
    }

    /// <summary>
    /// 0-based index of the line that holds the header keyword (or the first VAR section of a GVL), null when there is none.
    /// Pragmas and comments before it do not count.
    /// </summary>
    public static int? HeaderLineIndex(string declaration)
    {
        var lines = StripComments(StCode.Normalize(declaration ?? ""));
        for (var i = 0; i < lines.Count; i++)
        {
            var code = lines[i].Code.Trim();
            if (code.Length > 0)
            {
                return HeaderRegex().IsMatch(code) || SectionStartRegex().IsMatch(code) ? i : null;
            }
        }

        return null;
    }

    public static StDeclarationInfo Parse(string declaration)
    {
        var lines = StripComments(StCode.Normalize(declaration ?? ""));

        string? kind = null, name = null, access = null, returnType = null, extends = null;
        var implements = Array.Empty<string>();
        var sections = new List<StVarSection>();
        StVarSection? current = null;
        var pending = new StringBuilder();
        string? pendingComment = null;
        var pendingLine = 0;

        // Attributes collected since the last declaration; naming 'off' stays active until naming 'on'.
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var namingOff = false;
        IReadOnlyDictionary<string, string>? objectAttributes = null;
        var typeBody = new StringBuilder();

        for (var index = 0; index < lines.Count; index++)
        {
            var (code, comment, pragmas) = lines[index];
            foreach (var pragma in pragmas)
            {
                if (AttributeRegex().Match(pragma) is not { Success: true } a)
                {
                    continue;
                }

                var key = a.Groups["name"].Value.Trim().ToLowerInvariant();
                var value = a.Groups["value"].Success ? a.Groups["value"].Value.Trim() : "";
                if (key == "naming" && value.Equals("off", StringComparison.OrdinalIgnoreCase))
                {
                    namingOff = true;
                }
                else if (key == "naming" && value.Equals("on", StringComparison.OrdinalIgnoreCase))
                {
                    namingOff = false;
                    attributes.Remove("naming");
                    continue;
                }

                attributes[key] = value;
            }

            var line = code.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (current is null)
            {
                if (kind is null && HeaderRegex().Match(line) is { Success: true } h)
                {
                    kind = h.Groups[1].Value.ToUpperInvariant();
                    ParseHeader(kind, h.Groups[2].Value, out name, out access, out returnType, out extends, out implements);
                    objectAttributes = Take(attributes, namingOff);
                    if (kind == "TYPE" && returnType is not null)
                    {
                        typeBody.Append(returnType).Append(' ');
                    }

                    continue;
                }

                if (SectionStartRegex().Match(line).Success)
                {
                    objectAttributes ??= Take(attributes, namingOff);
                    attributes.Clear();
                    current = new StVarSection(Regex.Replace(line, @"\s+", " ").TrimEnd(';'), []);
                    var first = current.Kind.Split(' ')[0].ToUpperInvariant();
                    if (first is "STRUCT" or "UNION")
                    {
                        current = current with { Kind = first };
                    }

                    sections.Add(current);
                    pending.Clear();
                    pendingComment = null;
                    continue;
                }

                if (kind == "TYPE" && !line.StartsWith("END_TYPE", StringComparison.OrdinalIgnoreCase))
                {
                    typeBody.Append(line).Append(' ');
                }

                continue;
            }

            if (SectionEndRegex().IsMatch(line))
            {
                current = null;
                continue;
            }

            if (pending.Length == 0)
            {
                pendingLine = index + 1;
            }

            pending.Append(' ').Append(line);
            if (comment is not null)
            {
                pendingComment = comment;
            }

            if (line.EndsWith(';'))
            {
                var applied = Take(attributes, namingOff);
                foreach (var v in ParseVariables(pending.ToString().Trim().TrimEnd(';'), pendingComment))
                {
                    current.Variables.Add(v with { Attributes = applied, Line = pendingLine });
                }

                pending.Clear();
                pendingComment = null;
            }
        }

        var (enumValues, enumBase) = ParseEnumBody(typeBody.ToString());
        return new StDeclarationInfo(kind, name, access, returnType, extends, implements, sections)
        {
            Attributes = objectAttributes ?? Take(attributes, namingOff),
            EnumValues = enumValues,
            EnumBaseType = enumBase,
        };
    }

    /// <summary>Attributes that apply to the next declaration; the collected one-shot attributes are consumed.</summary>
    private static IReadOnlyDictionary<string, string> Take(Dictionary<string, string> attributes, bool namingOff)
    {
        if (namingOff)
        {
            attributes["naming"] = "off";
        }

        if (attributes.Count == 0)
        {
            return NoAttributes;
        }

        var result = new Dictionary<string, string>(attributes, StringComparer.OrdinalIgnoreCase);
        attributes.Clear();
        return result;
    }

    /// <summary>Reads '( A := 0, B ) INT;' of an ENUM type. Returns no values for every other TYPE body.</summary>
    private static (IReadOnlyList<StEnumValue> Values, string? BaseType) ParseEnumBody(string body)
    {
        body = body.Trim();
        if (!body.StartsWith('('))
        {
            return ([], null);
        }

        var depth = 0;
        var close = -1;
        for (var i = 0; i < body.Length && close < 0; i++)
        {
            depth += body[i] switch { '(' => 1, ')' => -1, _ => 0 };
            if (depth == 0)
            {
                close = i;
            }
        }

        if (close < 0)
        {
            return ([], null);
        }

        var values = new List<StEnumValue>();
        foreach (var part in SplitTopLevel(body[1..close]))
        {
            var assign = part.IndexOf(":=", StringComparison.Ordinal);
            var valueName = (assign < 0 ? part : part[..assign]).Trim();
            if (valueName.Length > 0)
            {
                values.Add(new StEnumValue(valueName, assign < 0 ? null : part[(assign + 2)..].Trim()));
            }
        }

        var baseType = body[(close + 1)..].Trim().TrimEnd(';').Trim();
        return (values, baseType.Length == 0 ? null : baseType);
    }

    private static IEnumerable<string> SplitTopLevel(string text)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            depth += text[i] switch { '(' or '[' => 1, ')' or ']' => -1, _ => 0 };
            if (text[i] == ',' && depth == 0)
            {
                yield return text[start..i];
                start = i + 1;
            }
        }

        yield return text[start..];
    }

    private static void ParseHeader(string kind, string rest, out string? name, out string? access, out string? returnType, out string? extends, out string[] implements)
    {
        name = access = returnType = extends = null;
        implements = [];

        rest = rest.Trim().TrimEnd(';');
        var colon = rest.IndexOf(':');
        if (colon >= 0)
        {
            var rt = rest[(colon + 1)..].Trim();
            returnType = rt.Length == 0 ? null : rt;
            rest = rest[..colon];
        }

        var m = Regex.Match(rest, @"\bIMPLEMENTS\b(.*)$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            implements = m.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            rest = rest[..m.Index];
        }

        m = Regex.Match(rest, @"\bEXTENDS\b(.*)$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            extends = m.Groups[1].Value.Trim();
            rest = rest[..m.Index];
        }

        foreach (var word in rest.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (AccessModifiers.Contains(word))
            {
                access = word.ToUpperInvariant();
            }
            else if (!Modifiers.Contains(word) && name is null)
            {
                name = word;
            }
        }
    }

    private static IEnumerable<StVariable> ParseVariables(string statement, string? comment)
    {
        var m = VarRegex().Match(statement);
        if (!m.Success)
        {
            yield break;
        }

        var rest = m.Groups["rest"].Value.Trim();
        string? init = null;
        var assign = rest.IndexOf(":=", StringComparison.Ordinal);
        if (assign >= 0)
        {
            init = rest[(assign + 2)..].Trim();
            rest = rest[..assign].Trim();
        }

        var address = m.Groups["addr"].Success ? m.Groups["addr"].Value : null;
        foreach (var n in m.Groups["names"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return new StVariable(n, rest, init, address, comment);
        }
    }

    /// <summary>Removes (* *) / /* */ comments and {pragmas}; returns code, trailing // comment and the pragma texts per line.</summary>
    private static List<(string Code, string? Comment, List<string> Pragmas)> StripComments(string text)
    {
        var result = new List<(string, string?, List<string>)>();
        var sb = new StringBuilder();
        var pragma = new StringBuilder();
        var pragmas = new List<string>();
        string? lineComment = null;
        var depth = 0;       // (* *) nesting
        var inBlock = false; // /* */
        var inPragma = false;
        var inString = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';
            if (c == '\n')
            {
                result.Add((sb.ToString(), lineComment, pragmas));
                sb.Clear();
                pragmas = [];
                lineComment = null;
                continue;
            }

            if (depth > 0)
            {
                if (c == '*' && next == ')') { depth--; i++; }
                else if (c == '(' && next == '*') { depth++; i++; }
                continue;
            }

            if (inBlock)
            {
                if (c == '*' && next == '/') { inBlock = false; i++; }
                continue;
            }

            if (inPragma)
            {
                if (c == '}')
                {
                    inPragma = false;
                    pragmas.Add(pragma.ToString());
                    pragma.Clear();
                }
                else
                {
                    pragma.Append(c);
                }

                continue;
            }

            if (inString)
            {
                sb.Append(c);
                if (c == '\'') { inString = false; }
                continue;
            }

            if (c == '\'') { inString = true; sb.Append(c); continue; }
            if (c == '(' && next == '*') { depth++; i++; continue; }
            if (c == '/' && next == '*') { inBlock = true; i++; continue; }
            if (c == '{') { inPragma = true; continue; }
            if (c == '/' && next == '/')
            {
                var end = text.IndexOf('\n', i);
                var commentText = (end < 0 ? text[(i + 2)..] : text[(i + 2)..end]).Trim();
                lineComment = commentText.Length == 0 ? null : commentText;
                i = (end < 0 ? text.Length : end) - 1;
                continue;
            }

            sb.Append(c);
        }

        result.Add((sb.ToString(), lineComment, pragmas));
        return result;
    }
}
