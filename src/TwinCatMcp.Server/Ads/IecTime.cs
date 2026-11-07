using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TwinCatMcp.Ads;

/// <summary>
/// IEC 61131-3 time literals. Binary representation in TwinCAT:
/// TIME = UDINT ms, LTIME = ULINT ns, TOD = UDINT ms since midnight, LTOD = ULINT ns since midnight,
/// DATE / DT = UDINT seconds since 1970-01-01, LDATE / LDT = ULINT ns since 1970-01-01.
/// </summary>
public static partial class IecTime
{
    private const ulong NsPerMs = 1_000_000;
    private const ulong NsPerS = 1_000_000_000;

    public static string FormatTime(uint ms) => "T#" + Components(ms * NsPerMs, withSubMs: false);

    public static string FormatLTime(ulong ns) => "LTIME#" + Components(ns, withSubMs: true);

    public static string FormatDate(uint seconds) => "D#" + ToDate(seconds * NsPerS).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string FormatLDate(ulong ns) => "LDATE#" + ToDate(ns).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string FormatDt(uint seconds) => "DT#" + ToDate(seconds * NsPerS).ToString("yyyy-MM-dd-HH:mm:ss", CultureInfo.InvariantCulture);

    public static string FormatLDt(ulong ns) =>
        "LDT#" + ToDate(ns).ToString("yyyy-MM-dd-HH:mm:ss", CultureInfo.InvariantCulture) + Fraction(ns % NsPerS, 9);

    public static string FormatTod(uint ms) =>
        "TOD#" + TimeOfDay(ms * NsPerMs) + (ms % 1000 == 0 ? string.Empty : $".{ms % 1000:000}");

    public static string FormatLTod(ulong ns) => "LTOD#" + TimeOfDay(ns) + Fraction(ns % NsPerS, 9);

    private static DateTime ToDate(ulong ns) => DateTime.UnixEpoch.AddTicks((long)(ns / 100));

    private static string TimeOfDay(ulong ns)
    {
        var s = ns / NsPerS;
        return $"{s / 3600:00}:{s / 60 % 60:00}:{s % 60:00}";
    }

    private static string Fraction(ulong part, int digits) =>
        part == 0 ? string.Empty : "." + part.ToString(new string('0', digits), CultureInfo.InvariantCulture).TrimEnd('0');

    private static string Components(ulong ns, bool withSubMs)
    {
        var sb = new StringBuilder();
        void Add(ulong v, string unit)
        {
            if (v != 0)
            {
                sb.Append(v.ToString(CultureInfo.InvariantCulture)).Append(unit);
            }
        }

        var totalMs = ns / NsPerMs;
        Add(totalMs / 86_400_000, "d");
        Add(totalMs / 3_600_000 % 24, "h");
        Add(totalMs / 60_000 % 60, "m");
        Add(totalMs / 1000 % 60, "s");
        Add(totalMs % 1000, "ms");
        if (withSubMs)
        {
            Add(ns / 1000 % 1000, "us");
            Add(ns % 1000, "ns");
        }

        return sb.Length == 0 ? (withSubMs ? "0ns" : "0ms") : sb.ToString();
    }

    [GeneratedRegex(@"(\d+(?:\.\d+)?)(ms|us|ns|d|h|m|s)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DurationPart();

    /// <summary>Parses T#.. / TIME#.. / LTIME#.. literals (e.g. T#1h2m3s4ms, LTIME#1s500us) into nanoseconds.</summary>
    public static bool TryParseDuration(string text, out ulong ns)
    {
        ns = 0;
        var s = StripPrefix(text, "LTIME#", "TIME#", "LT#", "T#").Replace("_", "").Trim();
        if (s.Length == 0)
        {
            return false;
        }

        var pos = 0;
        decimal total = 0;
        foreach (Match m in DurationPart().Matches(s))
        {
            if (m.Index != pos)
            {
                return false;
            }

            pos = m.Index + m.Length;
            var value = decimal.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            total += value * m.Groups[2].Value.ToLowerInvariant() switch
            {
                "d" => 86_400_000_000_000m,
                "h" => 3_600_000_000_000m,
                "m" => 60_000_000_000m,
                "s" => 1_000_000_000m,
                "ms" => 1_000_000m,
                "us" => 1_000m,
                _ => 1m,
            };
        }

        if (pos != s.Length)
        {
            return false;
        }

        ns = (ulong)decimal.Round(total);
        return true;
    }

    /// <summary>Parses TOD#HH:mm:ss[.fff] / LTOD#.. into nanoseconds since midnight.</summary>
    public static bool TryParseTimeOfDay(string text, out ulong ns)
    {
        ns = 0;
        var s = StripPrefix(text, "LTIME_OF_DAY#", "TIME_OF_DAY#", "LTOD#", "TOD#").Trim();
        var m = Regex.Match(s, @"^(\d{1,2}):(\d{2})(?::(\d{2})(?:\.(\d{1,9}))?)?$");
        if (!m.Success)
        {
            return false;
        }

        ulong h = ulong.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        ulong min = ulong.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        ulong sec = m.Groups[3].Success ? ulong.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
        if (h > 23 || min > 59 || sec > 59)
        {
            return false;
        }

        ns = (((h * 3600) + (min * 60) + sec) * NsPerS) + FractionNs(m.Groups[4].Value);
        return true;
    }

    /// <summary>
    /// Parses D#yyyy-MM-dd, DT#yyyy-MM-dd-HH:mm:ss[.f], LDT#.., or ISO 8601 (yyyy-MM-ddTHH:mm:ss[Z]) into
    /// nanoseconds since 1970-01-01 (the PLC's DT has no time zone; the value is taken as is).
    /// </summary>
    public static bool TryParseDateTime(string text, out ulong ns)
    {
        ns = 0;
        var s = StripPrefix(text, "LDATE_AND_TIME#", "DATE_AND_TIME#", "LDATE#", "DATE#", "LDT#", "DT#", "D#").Trim();
        var m = Regex.Match(s, @"^(\d{4})-(\d{2})-(\d{2})(?:[-T ](\d{1,2}):(\d{2})(?::(\d{2})(?:\.(\d{1,9}))?)?)?Z?$");
        if (!m.Success)
        {
            return false;
        }

        try
        {
            var date = new DateTime(
                int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
                m.Groups[4].Success ? int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) : 0,
                m.Groups[5].Success ? int.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture) : 0,
                m.Groups[6].Success ? int.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture) : 0,
                DateTimeKind.Utc);
            if (date < DateTime.UnixEpoch)
            {
                return false;
            }

            ns = ((ulong)(date - DateTime.UnixEpoch).Ticks * 100) + FractionNs(m.Groups[7].Value);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static ulong FractionNs(string digits) =>
        string.IsNullOrEmpty(digits) ? 0 : ulong.Parse(digits.PadRight(9, '0')[..9], CultureInfo.InvariantCulture);

    private static string StripPrefix(string text, params string[] prefixes)
    {
        var t = text.Trim();
        foreach (var p in prefixes)
        {
            if (t.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            {
                return t[p.Length..];
            }
        }

        return t;
    }
}
