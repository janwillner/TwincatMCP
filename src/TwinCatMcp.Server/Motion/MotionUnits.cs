using System.Globalization;
using TwinCatMcp.Core;

namespace TwinCatMcp.Motion;

/// <summary>Unit and number conversions used by the NC / TcCOM XML of the System Manager.</summary>
public static class MotionUnits
{
    /// <summary>
    /// NC encoder scaling factor (position unit per encoder increment) from the mechanical feed per motor/encoder
    /// revolution and the encoder increments per revolution, e.g. 10 mm / 65536 INC = 0.000152587890625 mm/INC.
    /// </summary>
    public static double ScalingFactor(double feedPerRevolution, double incrementsPerRevolution)
    {
        if (incrementsPerRevolution <= 0)
        {
            throw new TcException("incrementsPerRevolution must be > 0.");
        }

        return feedPerRevolution / incrementsPerRevolution;
    }

    /// <summary>Task cycle times in TaskDef XML are given in 100 ns ticks.</summary>
    public static int MicrosecondsToTicks(int microseconds) => checked(microseconds * 10);

    public static int TicksToMicroseconds(int ticks) => ticks / 10;

    /// <summary>Parses TwinCAT numbers like "#x05010010", "0x0201" or "33619984".</summary>
    public static uint ParseTcUInt(string text)
    {
        var t = text.Trim();
        if (t.StartsWith("#x", StringComparison.OrdinalIgnoreCase) || t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return uint.Parse(t[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        return uint.Parse(t, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    public static string FormatHex(uint value) => "#x" + value.ToString("x8", CultureInfo.InvariantCulture);

    /// <summary>Formats a number for the System Manager XML (invariant culture, never exponent notation).</summary>
    public static string FormatNumber(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new TcException($"Invalid number: {value}");
        }

        var r = value.ToString("R", CultureInfo.InvariantCulture);
        if (!r.Contains('E', StringComparison.OrdinalIgnoreCase))
        {
            return r;
        }

        return ((decimal)value).ToString(CultureInfo.InvariantCulture);
    }
}
