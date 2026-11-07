using System.Text.RegularExpressions;
using TwinCatMcp.Core;

namespace TwinCatMcp.Motion;

/// <summary>
/// Patches the exported .xti of an NC axis. The TwinCAT 4026 axis "Simulation" setting (Settings tab) is not part of
/// the ProduceXml/ConsumeXml format; it is persisted as attribute SimulationMode="true" on the &lt;Axis&gt; element.
/// Text-based so the rest of the file stays byte-identical.
/// </summary>
public static partial class AxisXti
{
    [GeneratedRegex(@"<Axis\s[^>]*>")]
    private static partial Regex AxisStartTag();

    [GeneratedRegex(@"\sSimulationMode=""[^""]*""")]
    private static partial Regex SimulationAttribute();

    public static bool GetSimulationMode(string xti)
    {
        var tag = AxisStartTag().Match(xti);
        if (!tag.Success)
        {
            return false;
        }

        var attr = Regex.Match(tag.Value, @"\sSimulationMode=""([^""]*)""");
        return attr.Success && (attr.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase) || attr.Groups[1].Value == "1");
    }

    public static string SetSimulationMode(string xti, bool enabled)
    {
        var tag = AxisStartTag().Match(xti);
        if (!tag.Success)
        {
            throw new TcException("The exported file contains no <Axis> element.");
        }

        var cleaned = SimulationAttribute().Replace(tag.Value, "");
        if (enabled)
        {
            var insertAt = cleaned.EndsWith("/>", StringComparison.Ordinal) ? cleaned.Length - 2 : cleaned.Length - 1;
            cleaned = cleaned.Insert(insertAt, " SimulationMode=\"true\"");
        }

        return string.Concat(xti.AsSpan(0, tag.Index), cleaned, xti.AsSpan(tag.Index + tag.Length));
    }
}
