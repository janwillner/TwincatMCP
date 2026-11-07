using System.Globalization;
using TwinCatMcp.Core;

namespace TwinCatMcp.Runtime;

/// <summary>CPU core masks as used in TaskDef/CpuAffinity and RTimeSetDef/Affinity ("#x0000000000000007").</summary>
public static class Affinity
{
    public static ulong Parse(string? text)
    {
        var s = (text ?? "").Trim();
        if (s.Length == 0)
        {
            return 0;
        }

        if (s.StartsWith("#x", StringComparison.OrdinalIgnoreCase) || s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return ulong.Parse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        return ulong.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    public static string Format(ulong mask) => "#x" + mask.ToString("X16", CultureInfo.InvariantCulture);

    public static ulong FromCores(IEnumerable<int> cores)
    {
        ulong mask = 0;
        foreach (var c in cores)
        {
            if (c is < 0 or > 63)
            {
                throw new TcException($"CPU core index {c} is out of range (0..63).");
            }

            mask |= 1UL << c;
        }

        return mask;
    }

    public static int[] ToCores(ulong mask)
        => Enumerable.Range(0, 64).Where(i => (mask & (1UL << i)) != 0).ToArray();
}
