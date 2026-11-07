using System.Text;
using System.Text.RegularExpressions;
using TwinCatMcp.Core;

namespace TwinCatMcp.Plc;

/// <summary>Declaration and implementation text of a PLC object (null = the object has no such area).</summary>
public sealed record PouText(string? Declaration, string? Implementation);

/// <summary>One exact-match edit. Section: "declaration", "implementation" or null/"auto".</summary>
public sealed record CodeEdit(string OldText, string NewText, string? Section = null, bool ReplaceAll = false);

/// <summary>Result of an applied edit.</summary>
public sealed record AppliedEdit(string Section, int Replacements, int[] Lines, string Diff);

public sealed record GrepHit(int Line, string Text);

/// <summary>Pure text helpers for IEC 61131-3 / ST source held in POU declaration and implementation areas.</summary>
public static class StCode
{
    public sealed record ReplaceResult(string Text, int Count, int[] Lines, string Diff);

    public const string Declaration = "declaration";
    public const string Implementation = "implementation";

    private const int MaxDiffLines = 60;

    /// <summary>Normalizes a user supplied section name. Returns null for auto.</summary>
    public static string? ParseSection(string? section)
    {
        if (string.IsNullOrWhiteSpace(section) || section.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var s = section.Trim().ToLowerInvariant();
        if (s is "declaration" or "decl" or "header")
        {
            return Declaration;
        }

        if (s is "implementation" or "impl" or "body" or "code")
        {
            return Implementation;
        }

        throw new TcException($"Unknown section '{section}'.") { Hint = "Use 'declaration', 'implementation' or 'auto'." };
    }

    /// <summary>Applies one edit to the declaration or implementation of a POU (section auto-detected if not given).</summary>
    public static (PouText Text, AppliedEdit Applied) ApplyEdit(PouText text, CodeEdit edit)
    {
        var section = ParseSection(edit.Section);
        if (section is null)
        {
            var oldN = Normalize(edit.OldText ?? "");
            var inDecl = text.Declaration is not null && oldN.Length > 0 && Normalize(text.Declaration).Contains(oldN, StringComparison.Ordinal);
            var inImpl = text.Implementation is not null && oldN.Length > 0 && Normalize(text.Implementation).Contains(oldN, StringComparison.Ordinal);
            if (inDecl && inImpl)
            {
                throw new TcException("oldText occurs in both declaration and implementation.")
                {
                    Hint = "Pass section='declaration' or section='implementation'.",
                };
            }

            if (!inDecl && !inImpl)
            {
                if (oldN.Length == 0)
                {
                    Replace("", "", "", false); // throws the 'empty' error
                }

                var hints = new[] { (Declaration, text.Declaration), (Implementation, text.Implementation) }
                    .Where(a => a.Item2 is not null)
                    .Select(a => $"{a.Item1}: {NotFoundHint(Normalize(a.Item2!), oldN)}");
                throw new TcException("oldText not found in declaration or implementation.") { Hint = string.Join(" | ", hints) };
            }

            section = inDecl ? Declaration : Implementation;
        }

        var current = section == Declaration ? text.Declaration : text.Implementation;
        if (current is null)
        {
            throw new TcException($"This object has no {section} area.")
            {
                Hint = "Actions and transitions only have an implementation; DUTs, GVLs and interfaces only a declaration.",
            };
        }

        var r = Replace(current, edit.OldText ?? "", edit.NewText, edit.ReplaceAll);
        var updated = section == Declaration ? text with { Declaration = r.Text } : text with { Implementation = r.Text };
        return (updated, new AppliedEdit(section, r.Count, r.Lines, r.Diff));
    }

    /// <summary>Applies edits sequentially (each on the result of the previous). All or nothing: throws on the first failing edit.</summary>
    public static (PouText Text, List<AppliedEdit> Applied) ApplyEdits(PouText text, IEnumerable<CodeEdit> edits)
    {
        var applied = new List<AppliedEdit>();
        var i = 0;
        foreach (var edit in edits)
        {
            try
            {
                (text, var a) = ApplyEdit(text, edit);
                applied.Add(a);
            }
            catch (TcException ex)
            {
                throw new TcException($"Edit #{i + 1} failed: {ex.Message}") { Hint = ex.Hint };
            }

            i++;
        }

        return (text, applied);
    }

    /// <summary>Returns all lines matching <paramref name="regex"/> with their 1-based line numbers.</summary>
    public static List<GrepHit> Grep(string text, Regex regex)
    {
        var hits = new List<GrepHit>();
        var lines = SplitLines(text);
        for (var i = 0; i < lines.Length; i++)
        {
            if (regex.IsMatch(lines[i]))
            {
                hits.Add(new GrepHit(i + 1, lines[i]));
            }
        }

        return hits;
    }

    /// <summary>
    /// Replaces <paramref name="oldText"/> by <paramref name="newText"/> (exact, ordinal match).
    /// Line endings are normalized for matching; the original line ending style of <paramref name="text"/> is kept.
    /// Throws a <see cref="TcException"/> when the old text is missing or (without <paramref name="replaceAll"/>) ambiguous.
    /// </summary>
    public static ReplaceResult Replace(string text, string oldText, string newText, bool replaceAll)
    {
        if (string.IsNullOrEmpty(oldText))
        {
            throw new TcException("oldText must not be empty.") { Hint = "Use pou_write_code to set a whole area." };
        }

        var crlf = UsesCrLf(text);
        var src = Normalize(text);
        var oldN = Normalize(oldText);
        var newN = Normalize(newText ?? "");

        var positions = FindAll(src, oldN);
        if (positions.Count == 0)
        {
            throw new TcException("oldText not found.") { Hint = NotFoundHint(src, oldN) };
        }

        if (positions.Count > 1 && !replaceAll)
        {
            var lines = string.Join(", ", positions.Select(p => LineOf(src, p)).Distinct());
            throw new TcException($"oldText is ambiguous: found {positions.Count} times (lines {lines}).")
            {
                Hint = "Include more surrounding context in oldText to make it unique, or pass replaceAll=true.",
            };
        }

        var sb = new StringBuilder(src.Length + (newN.Length - oldN.Length) * positions.Count);
        var diff = new StringBuilder();
        var newLines = new List<int>();
        var last = 0;
        var diffLines = 0;
        foreach (var pos in positions)
        {
            sb.Append(src, last, pos - last);
            var newPos = sb.Length;
            sb.Append(newN);
            last = pos + oldN.Length;

            var newText2 = sb.ToString();
            var lineNo = LineOf(newText2, newPos);
            if (newLines.Count == 0 || newLines[^1] != lineNo)
            {
                newLines.Add(lineNo);
            }

            if (diffLines < MaxDiffLines)
            {
                diffLines += AppendDiff(diff, src, pos, oldN.Length, newText2, newPos, newN.Length, src, last, lineNo);
            }
        }

        sb.Append(src, last, src.Length - last);
        var result = sb.ToString();
        if (diffLines >= MaxDiffLines)
        {
            diff.AppendLine("... (diff truncated)");
        }

        return new ReplaceResult(crlf ? ToCrLf(result) : result, positions.Count, newLines.ToArray(), diff.ToString().TrimEnd('\n'));
    }

    // ------------------------------------------------------------------ line helpers

    public static bool UsesCrLf(string text) => text.Contains("\r\n", StringComparison.Ordinal);

    public static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    public static string ToCrLf(string normalized) => normalized.Replace("\n", "\r\n", StringComparison.Ordinal);

    /// <summary>1-based line number of a character index.</summary>
    public static int LineOf(string text, int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    public static string[] SplitLines(string text) => Normalize(text).Split('\n');

    private static List<int> FindAll(string src, string needle)
    {
        var list = new List<int>();
        var i = 0;
        while ((i = src.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            list.Add(i);
            i += needle.Length;
        }

        return list;
    }

    private static int LineStart(string text, int index)
    {
        var i = text.LastIndexOf('\n', Math.Max(0, Math.Min(index, text.Length) - 1));
        return index == 0 ? 0 : i + 1;
    }

    private static int LineEnd(string text, int index)
    {
        var i = text.IndexOf('\n', Math.Min(index, text.Length));
        return i < 0 ? text.Length : i;
    }

    private static int AppendDiff(StringBuilder diff, string src, int pos, int oldLen, string newSoFar, int newPos, int newLen, string _, int afterOld, int lineNo)
    {
        // Old: full lines touched by the match in the original text.
        var oStart = LineStart(src, pos);
        var oEnd = LineEnd(src, pos + oldLen);
        var oldBlock = src[oStart..oEnd];

        // New: full lines = prefix of the first line + replacement + rest of the last original line.
        var prefix = src[oStart..pos];
        var suffix = src[afterOld..oEnd];
        var newBlock = prefix + newSoFar.Substring(newPos, newLen) + suffix;

        diff.Append("@@ line ").Append(lineNo).Append('\n');
        var count = 1;
        foreach (var l in oldBlock.Split('\n'))
        {
            diff.Append('-').Append(l).Append('\n');
            count++;
        }

        foreach (var l in newBlock.Split('\n'))
        {
            diff.Append('+').Append(l).Append('\n');
            count++;
        }

        return count;
    }

    private static string NotFoundHint(string src, string needle)
    {
        // Whitespace-tolerant search: collapse all whitespace and compare.
        var squeezedNeedle = Regex.Replace(needle, @"\s+", "");
        if (squeezedNeedle.Length > 0)
        {
            var map = new List<int>(src.Length);
            var squeezed = new StringBuilder(src.Length);
            for (var i = 0; i < src.Length; i++)
            {
                if (!char.IsWhiteSpace(src[i]))
                {
                    squeezed.Append(src[i]);
                    map.Add(i);
                }
            }

            var idx = squeezed.ToString().IndexOf(squeezedNeedle, StringComparison.Ordinal);
            if (idx >= 0)
            {
                return $"A match ignoring whitespace/indentation exists at line {LineOf(src, map[idx])}. " +
                       "Re-read the code (pou_read_code) and copy oldText exactly, including tabs and spaces.";
            }
        }

        var firstLine = needle.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        if (firstLine is not null)
        {
            var lines = src.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(firstLine, StringComparison.Ordinal))
                {
                    return $"The first line of oldText occurs at line {i + 1}, but the following lines differ. Re-read the code with pou_read_code.";
                }
            }
        }

        return "Re-read the current code with pou_read_code (it may have changed) and copy oldText exactly. " +
               "Check that you target the right section (declaration vs implementation).";
    }
}
