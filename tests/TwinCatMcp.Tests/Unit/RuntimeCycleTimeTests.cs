using TwinCatMcp.Core;
using TwinCatMcp.Runtime;

namespace TwinCatMcp.Tests.Unit;

/// <summary>
/// TwinCAT task cycle times and real-time base times are stored in 100 ns units
/// (ITcTaskModule.CycleTime docs; default PlcTask 10 ms = TaskDef/CycleTime 100000; BaseTime 10000 = 1 ms).
/// </summary>
public sealed class RuntimeCycleTimeTests
{
    [Theory]
    [InlineData(10.0, "ms", 100000u)]
    [InlineData(1.0, "ms", 10000u)]
    [InlineData(500.0, "us", 5000u)]
    [InlineData(50.0, "µs", 500u)]
    [InlineData(100.0, "ns", 1u)]
    [InlineData(0.25, "ms", 2500u)]
    public void Converts_to_100ns_ticks(double value, string unit, uint expectedTicks)
        => Assert.Equal(expectedTicks, CycleTime.ToTicks(value, unit));

    [Fact]
    public void Ticks_are_reported_in_ms_and_us()
    {
        var d = CycleTime.Describe(100000);

        Assert.Equal(100000u, d.Ticks100ns);
        Assert.Equal(10.0, d.Milliseconds);
        Assert.Equal(10000.0, d.Microseconds);
    }

    [Theory]
    [InlineData(0.0, "ms")]
    [InlineData(-1.0, "ms")]
    [InlineData(150.0, "ns")]
    [InlineData(1.0, "s")]
    public void Rejects_invalid_values_and_units(double value, string unit)
        => Assert.Throws<TcException>(() => CycleTime.ToTicks(value, unit));
}
