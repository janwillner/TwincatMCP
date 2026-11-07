using System.Text.RegularExpressions;
using EnvDTE;
using EnvDTE80;

namespace TwinCatMcp.Plc;

public sealed record PlcDiagnostic(string Level, string? Code, string Message, string? Object, string? File, int? FileLine);

public sealed record PlcBuildResult(int ErrorCount, int WarningCount, List<PlcDiagnostic> Errors, List<PlcDiagnostic> Warnings)
{
    /// <summary>Raw output text the result was parsed from.</summary>
    public string? Output { get; init; }
}

/// <summary>
/// Parses the TwinCAT PLC compiler messages from the Visual Studio "Build" output pane.
/// In a hidden TcXaeShell (SuppressUI, automation) the DTE Error List stays empty, but the Build pane is filled,
/// e.g. "C:\...\POUs\FB_A.TcPOU(8) : error: C0007: Expression expected instead of ';'" and
/// "Compile complete -- 1 errors, 0 warnings".
/// </summary>
public static partial class PlcBuildOutput
{
    [GeneratedRegex(@"^(?<file>.+?)\((?<line>\d+)\)\s*:\s*(?<level>error|warning|info|message)\s*:?\s*(?<code>[A-Z]+\d+)\s*:\s*(?<msg>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex FileDiag();

    [GeneratedRegex(@"^(?<level>error|warning)\s*:?\s*(?<code>[A-Z]+\d+)?\s*:\s*(?<msg>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex PlainDiag();

    [GeneratedRegex(@"^Compile complete -- (?<e>\d+) errors?, (?<w>\d+) warnings?", RegexOptions.IgnoreCase)]
    private static partial Regex CompileSummary();

    public static PlcBuildResult Parse(string text)
    {
        var errors = new List<PlcDiagnostic>();
        var warnings = new List<PlcDiagnostic>();
        int? sumE = null, sumW = null;
        foreach (var raw in StCode.SplitLines(text ?? ""))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (CompileSummary().Match(line) is { Success: true } s)
            {
                sumE = (sumE ?? 0) + int.Parse(s.Groups["e"].Value);
                sumW = (sumW ?? 0) + int.Parse(s.Groups["w"].Value);
                continue;
            }

            PlcDiagnostic? d = null;
            if (FileDiag().Match(line) is { Success: true } f)
            {
                var file = f.Groups["file"].Value.Trim();
                d = new PlcDiagnostic(Level(f.Groups["level"].Value), f.Groups["code"].Value, f.Groups["msg"].Value.Trim(),
                    Path.GetFileNameWithoutExtension(file), file, int.Parse(f.Groups["line"].Value));
            }
            else if (PlainDiag().Match(line) is { Success: true } p)
            {
                d = new PlcDiagnostic(Level(p.Groups["level"].Value), p.Groups["code"].Success && p.Groups["code"].Length > 0 ? p.Groups["code"].Value : null,
                    p.Groups["msg"].Value.Trim(), null, null, null);
            }

            if (d is not null)
            {
                (d.Level == "error" ? errors : d.Level == "warning" ? warnings : null)?.Add(d);
            }
        }

        return new PlcBuildResult(sumE ?? errors.Count, sumW ?? warnings.Count, errors, warnings);
    }

    private static string Level(string s) => s.ToLowerInvariant() switch
    {
        "error" => "error",
        "warning" => "warning",
        _ => "message",
    };

    /// <summary>Text appended to a pane since <paramref name="before"/> was read (whole text if the pane was cleared).</summary>
    public static string Delta(string before, string after)
        => after.StartsWith(before, StringComparison.Ordinal) ? after[before.Length..] : after;

    // ------------------------------------------------------------------ COM (STA only)

    /// <summary>Full text of the "Build" output pane ("" if it does not exist yet).</summary>
    public static string ReadBuildPane(DTE2 dte)
    {
        try
        {
            foreach (OutputWindowPane pane in dte.ToolWindows.OutputWindow.OutputWindowPanes)
            {
                if (string.Equals(pane.Name, "Build", StringComparison.OrdinalIgnoreCase))
                {
                    var doc = pane.TextDocument;
                    return doc.StartPoint.CreateEditPoint().GetText(doc.EndPoint) ?? "";
                }
            }
        }
        catch
        {
            // pane not available
        }

        return "";
    }

    /// <summary>Runs <paramref name="action"/> and returns the compiler diagnostics it wrote to the Build pane.</summary>
    public static PlcBuildResult Capture(DTE2 dte, Action action)
    {
        var before = ReadBuildPane(dte);
        action();
        var delta = Delta(before, ReadBuildPane(dte));
        return Parse(delta) with { Output = delta };
    }
}
