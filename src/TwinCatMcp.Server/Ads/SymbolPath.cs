using System.Text;
using System.Text.RegularExpressions;
using TwinCatMcp.Core;

namespace TwinCatMcp.Ads;

/// <summary>Helpers for PLC instance paths like <c>MAIN.stData.aValues[3]</c>.</summary>
public static class SymbolPath
{
    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new TcException("Symbol path is empty.") { Hint = "Pass a full instance path, e.g. MAIN.nCounter or GVL.stData.nValue." };
        }

        var sb = new StringBuilder(path.Length);
        var inBracket = false;
        foreach (var c in path.Trim())
        {
            if (c == '[')
            {
                inBracket = true;
            }
            else if (c == ']')
            {
                inBracket = false;
            }
            else if (inBracket && char.IsWhiteSpace(c))
            {
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>True when <paramref name="path"/> is <paramref name="prefix"/> itself or one of its members / elements.</summary>
    public static bool IsSelfOrDescendant(string prefix, string path)
    {
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return path.Length == prefix.Length || path[prefix.Length] is '.' or '[';
    }

    /// <summary>Returns up to <paramref name="max"/> known names that resemble <paramref name="path"/>.</summary>
    public static IReadOnlyList<string> Suggest(string path, IEnumerable<string> names, int max = 5)
    {
        var last = path.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? path;
        var bracket = last.IndexOf('[');
        if (bracket > 0)
        {
            last = last[..bracket];
        }

        var stem = last.Length > 4 ? last[..^2] : last;
        return names
            .Select(n => (Name: n, Score: Score(n, path, last, stem)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .Select(x => x.Name)
            .ToList();
    }

    private static int Score(string name, string path, string last, string stem)
    {
        var score = 0;
        if (name.Contains(last, StringComparison.OrdinalIgnoreCase))
        {
            score += 3;
        }
        else if (stem.Length >= 3 && name.Contains(stem, StringComparison.OrdinalIgnoreCase))
        {
            score += 2;
        }

        if (score > 0)
        {
            var ns = path.Split('.')[0];
            if (name.StartsWith(ns + ".", StringComparison.OrdinalIgnoreCase))
            {
                score += 1;
            }
        }

        return score;
    }
}

/// <summary>
/// Symbol name filter: null/empty = all, "re:..." or "/.../" = regular expression, pattern with * or ? = glob,
/// otherwise a case-insensitive prefix.
/// </summary>
public sealed class SymbolFilter
{
    private readonly Func<string, bool> _match;

    private SymbolFilter(Func<string, bool> match) => _match = match;

    public static SymbolFilter Create(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return new SymbolFilter(_ => true);
        }

        var p = pattern.Trim();
        string? regex = null;
        if (p.StartsWith("re:", StringComparison.OrdinalIgnoreCase))
        {
            regex = p[3..];
        }
        else if (p.Length >= 2 && p[0] == '/' && p[^1] == '/')
        {
            regex = p[1..^1];
        }

        if (regex is not null)
        {
            try
            {
                var r = new Regex(regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                return new SymbolFilter(r.IsMatch);
            }
            catch (ArgumentException ex)
            {
                throw new TcException($"Invalid regular expression '{regex}': {ex.Message}")
                {
                    Hint = "Use .NET regex syntax, e.g. re:^MAIN\\.n or /Counter$/ - or a glob like *.bStart*.",
                };
            }
        }

        if (p.Contains('*') || p.Contains('?'))
        {
            var r = new Regex("^" + Regex.Escape(p).Replace("\\*", ".*").Replace("\\?", ".") + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return new SymbolFilter(r.IsMatch);
        }

        return new SymbolFilter(n => n.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsMatch(string name) => _match(name);
}
