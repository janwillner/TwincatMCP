using System.Text.RegularExpressions;

namespace TwinCatMcp.Guidelines;

/// <summary>Case-insensitive wildcard pattern ('*' any text, '?' one character), matched against the whole text.</summary>
internal sealed class Glob(string pattern)
{
    private readonly Regex _regex = new(
        "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public string Pattern { get; } = pattern;

    public bool IsMatch(string? text) => text is not null && _regex.IsMatch(text);

    public bool HasWildcard => Pattern.Contains('*') || Pattern.Contains('?');
}
