using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace TwinCatMcp.Libraries;

public sealed record SignatureVar(string Name, string DataType, string? Comment);

/// <summary>One POU / type / GVL of a library as described by the library signature XML.</summary>
public sealed record SignatureEntry(
    string Kind,
    string Name,
    string? Comment,
    string? ReturnType,
    List<SignatureVar>? Inputs,
    List<SignatureVar>? Outputs,
    List<SignatureVar>? InOuts,
    List<SignatureVar>? Constants);

public sealed record LibrarySignatureSet(string LibraryName, string? Version, string? Distributor, List<SignatureEntry> Entries);

/// <summary>
/// Parses the XML of <c>ITcPlcLibraryManager2.ProduceLibrarySignatures</c> /
/// <c>ProduceAllLibrarySignatures</c>: &lt;Library&gt;&lt;LibraryName/&gt;&lt;Version/&gt;&lt;Distributor/&gt;
/// &lt;TypeSignatures&gt;&lt;TypeSignature type="FunctionBlock|Function|Type|Interface|VarGlobal"&gt;...
/// and renders entries as IEC 61131-3 declaration text.
/// </summary>
public static partial class LibrarySignatures
{
    public static List<LibrarySignatureSet> Parse(string xml)
    {
        var result = new List<LibrarySignatureSet>();
        if (string.IsNullOrWhiteSpace(xml))
        {
            return result;
        }

        // ProduceAllLibrarySignatures concatenates several <Library> roots -> read as fragment.
        var settings = new XmlReaderSettings { ConformanceLevel = ConformanceLevel.Fragment, IgnoreWhitespace = false };
        using var reader = XmlReader.Create(new StringReader(xml), settings);
        reader.MoveToContent();
        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Library")
            {
                var lib = (XElement)XNode.ReadFrom(reader);
                result.Add(ParseLibrary(lib));
            }
            else
            {
                reader.Read();
            }
        }

        return result;
    }

    /// <summary>
    /// Finds entries by name: exact (case-insensitive) or with '*' / '?' wildcards. Optional kind filter
    /// (FunctionBlock, Function, Type, Interface, VarGlobal; case-insensitive).
    /// </summary>
    public static List<(LibrarySignatureSet Library, SignatureEntry Entry)> Find(IEnumerable<LibrarySignatureSet> libraries, string? namePattern, string? kind = null)
    {
        Regex? rx = null;
        if (!string.IsNullOrWhiteSpace(namePattern) && (namePattern.Contains('*') || namePattern.Contains('?')))
        {
            rx = new Regex("^" + Regex.Escape(namePattern.Trim()).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase);
        }

        return libraries
            .SelectMany(l => l.Entries.Select(e => (Library: l, Entry: e)))
            .Where(h => kind is null || string.Equals(h.Entry.Kind, kind, StringComparison.OrdinalIgnoreCase))
            .Where(h => string.IsNullOrWhiteSpace(namePattern)
                        || (rx?.IsMatch(h.Entry.Name) ?? string.Equals(h.Entry.Name, namePattern.Trim(), StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    /// <summary>Renders an entry as IEC 61131-3 declaration (newline = "\n", 4-space indent).</summary>
    public static string ToIec(SignatureEntry e)
    {
        var sb = new StringBuilder();
        if (e.Comment is not null)
        {
            sb.Append("// ").Append(e.Comment).Append('\n');
        }

        switch (e.Kind)
        {
            case "FunctionBlock":
                sb.Append("FUNCTION_BLOCK ").Append(e.Name).Append('\n');
                AppendVars(sb, e);
                sb.Append("END_FUNCTION_BLOCK");
                break;
            case "Function":
                sb.Append("FUNCTION ").Append(e.Name);
                if (e.ReturnType is not null)
                {
                    sb.Append(" : ").Append(e.ReturnType);
                }

                sb.Append('\n');
                AppendVars(sb, e);
                sb.Append("END_FUNCTION");
                break;
            case "VarGlobal":
                sb.Append("// GVL ").Append(e.Name).Append('\n');
                if (e.Constants is { Count: > 0 })
                {
                    AppendBlock(sb, "VAR_GLOBAL CONSTANT", e.Constants);
                }
                else
                {
                    sb.Append("VAR_GLOBAL\nEND_VAR\n");
                }

                sb.Length--; // trailing newline
                break;
            case "Interface":
                sb.Append("INTERFACE ").Append(e.Name).Append(" (* methods/properties are not part of the library signature *)");
                break;
            default:
                sb.Append("TYPE ").Append(e.Name).Append(" (* members are not part of the library signature *)");
                break;
        }

        return sb.ToString();
    }

    private static void AppendVars(StringBuilder sb, SignatureEntry e)
    {
        AppendBlock(sb, "VAR_INPUT", e.Inputs);
        AppendBlock(sb, "VAR_OUTPUT", e.Outputs);
        AppendBlock(sb, "VAR_IN_OUT", e.InOuts);
        AppendBlock(sb, "VAR CONSTANT", e.Constants);
    }

    private static void AppendBlock(StringBuilder sb, string header, List<SignatureVar>? vars)
    {
        if (vars is not { Count: > 0 })
        {
            return;
        }

        sb.Append(header).Append('\n');
        foreach (var v in vars)
        {
            sb.Append("    ").Append(v.Name).Append(" : ").Append(v.DataType).Append(';');
            if (v.Comment is not null)
            {
                sb.Append(" // ").Append(v.Comment);
            }

            sb.Append('\n');
        }

        sb.Append("END_VAR\n");
    }

    private static LibrarySignatureSet ParseLibrary(XElement lib)
    {
        var entries = (lib.Element("TypeSignatures")?.Elements("TypeSignature") ?? [])
            .Select(ParseEntry)
            .ToList();
        return new LibrarySignatureSet(
            Text(lib.Element("LibraryName")) ?? "",
            Text(lib.Element("Version")),
            Text(lib.Element("Distributor")),
            entries);
    }

    private static SignatureEntry ParseEntry(XElement t)
    {
        var kind = t.Attribute("type")?.Value ?? "Unknown";
        var name = Text(t.Element("Name")) ?? "";
        var outputs = Vars(t.Element("Outputs"));
        string? returnType = null;
        if (kind == "Function" && outputs is not null)
        {
            var ret = outputs.FirstOrDefault(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));
            if (ret is not null)
            {
                returnType = ret.DataType;
                outputs.Remove(ret);
                if (outputs.Count == 0)
                {
                    outputs = null;
                }
            }
        }

        return new SignatureEntry(kind, name, Comment(t.Element("Comment")), returnType,
            Vars(t.Element("Inputs")), outputs, Vars(t.Element("InOuts")), Vars(t.Element("Constants")));
    }

    private static List<SignatureVar>? Vars(XElement? container)
    {
        var list = container?.Elements()
            .Select(v => new SignatureVar(Text(v.Element("Name")) ?? "", Text(v.Element("DataType")) ?? "", Comment(v.Element("Comment"))))
            .ToList();
        return list is { Count: > 0 } ? list : null;
    }

    private static string? Comment(XElement? e)
    {
        if (e is null)
        {
            return null;
        }

        var collapsed = Whitespace().Replace(e.Value, " ").Trim();
        return collapsed.Length == 0 ? null : collapsed;
    }

    private static string? Text(XElement? e) => string.IsNullOrWhiteSpace(e?.Value) ? null : e.Value.Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
