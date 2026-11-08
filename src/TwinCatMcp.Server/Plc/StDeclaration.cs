using System.Text;
using System.Text.RegularExpressions;

namespace TwinCatMcp.Plc;

public sealed record StVariable(string Name, string Type, string? Init = null, string? Address = null, string? Comment = null);

public sealed record StVarSection(string Kind, List<StVariable> Variables);

public sealed record StDeclarationInfo(
    string? Kind,
    string? Name,
    string? Access,
    string? ReturnType,
    string? Extends,
    string[] Implements,
    List<StVarSection> Sections);

/// <summary>
/// Lightweight parser for the declaration area of POUs, DUTs and GVLs (header + VAR / STRUCT sections).
/// It is tolerant: unknown constructs are skipped, it never throws on odd input.
/// </summary>
public static partial class StDeclaration
{
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

    public static StDeclarationInfo Parse(string declaration)
    {
        var lines = StripComments(StCode.Normalize(declaration ?? ""));

        string? kind = null, name = null, access = null, returnType = null, extends = null;
        var implements = Array.Empty<string>();
        var sections = new List<StVarSection>();
        StVarSection? current = null;
        var pending = new StringBuilder();
        string? pendingComment = null;

        foreach (var (code, comment) in lines)
        {
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
                    continue;
                }

                if (SectionStartRegex().Match(line).Success)
                {
                    current = new StVarSection(Regex.Replace(line, @"\s+", " ").TrimEnd(';'), []);
                    var first = current.Kind.Split(' ')[0].ToUpperInvariant();
                    if (first is "STRUCT" or "UNION")
                    {
                        current = current with { Kind = first };
                    }

                    sections.Add(current);
                    pending.Clear();
                    pendingComment = null;
                }

                continue;
            }

            if (SectionEndRegex().IsMatch(line))
            {
                current = null;
                continue;
            }

            pending.Append(' ').Append(line);
            if (comment is not null)
            {
                pendingComment = comment;
            }

            if (line.EndsWith(';'))
            {
                foreach (var v in ParseVariables(pending.ToString().Trim().TrimEnd(';'), pendingComment))
                {
                    current.Variables.Add(v);
                }

                pending.Clear();
                pendingComment = null;
            }
        }

        return new StDeclarationInfo(kind, name, access, returnType, extends, implements, sections);
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

    /// <summary>Removes (* *) / /* */ comments and {pragmas}; returns code + trailing // comment per line.</summary>
    private static List<(string Code, string? Comment)> StripComments(string text)
    {
        var result = new List<(string, string?)>();
        var sb = new StringBuilder();
        string? lineComment = null;
        var depth = 0;      // (* *) nesting
        var inPragma = false;
        var inString = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';
            if (c == '\n')
            {
                result.Add((sb.ToString(), lineComment));
                sb.Clear();
                lineComment = null;
                continue;
            }

            if (depth > 0)
            {
                if (c == '*' && next == ')') { depth--; i++; }
                else if (c == '(' && next == '*') { depth++; i++; }
                continue;
            }

            if (inPragma)
            {
                if (c == '}') { inPragma = false; }
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

        result.Add((sb.ToString(), lineComment));
        return result;
    }
}
