using System.Globalization;
using TwinCatMcp.Core;

namespace TwinCatMcp.Runtime;

/// <summary>A duration in TwinCAT 100 ns units (task cycle time, real-time base time).</summary>
public sealed record CycleTimeInfo(uint Ticks100ns, double Milliseconds, double Microseconds);

/// <summary>
/// Conversion between human units and TwinCAT's 100 ns ticks. Task cycle times (TaskDef/CycleTime,
/// ITcTaskModule.CycleTime) and real-time base times (RTimeSetDef/CPUs/CPU/BaseTime) use 100 ns units:
/// 10 ms = 100000, 1 ms = 10000, 500 µs = 5000.
/// </summary>
public static class CycleTime
{
    public const double NanosecondsPerTick = 100.0;

    public static uint ToTicks(double value, string unit)
    {
        var nsPerUnit = (unit ?? "").Trim().ToLowerInvariant() switch
        {
            "ms" => 1_000_000.0,
            "us" or "µs" or "μs" => 1_000.0,
            "ns" => 1.0,
            "ticks" or "100ns" => NanosecondsPerTick,
            _ => throw new TcException($"Unknown time unit '{unit}'.") { Hint = "Use 'ms', 'us' or 'ns' (or 'ticks' for raw 100 ns units)." },
        };

        if (double.IsNaN(value) || value <= 0)
        {
            throw new TcException($"Time value must be > 0 (got {value.ToString(CultureInfo.InvariantCulture)} {unit}).");
        }

        var ticks = value * nsPerUnit / NanosecondsPerTick;
        var rounded = Math.Round(ticks);
        if (Math.Abs(ticks - rounded) > 1e-6 || rounded < 1 || rounded > uint.MaxValue)
        {
            throw new TcException($"{value.ToString(CultureInfo.InvariantCulture)} {unit} is not a positive multiple of 100 ns.")
            {
                Hint = "TwinCAT stores cycle/base times in 100 ns units, e.g. 1 ms, 500 us, 62.5 us.",
            };
        }

        return (uint)rounded;
    }

    public static CycleTimeInfo Describe(uint ticks)
        => new(ticks, ticks * NanosecondsPerTick / 1_000_000.0, ticks * NanosecondsPerTick / 1_000.0);
}
