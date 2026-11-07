using TwinCatMcp.Core;
using TwinCatMcp.Motion;

namespace TwinCatMcp.Tests.Unit;

public sealed class MotionUnitsTests
{
    [Theory]
    [InlineData(10.0, 65536.0, 0.000152587890625)] // 10 mm per revolution, 16 bit encoder
    [InlineData(360.0, 3600.0, 0.1)]               // degrees, 3600 inc/rev
    [InlineData(5.0, 1000.0, 0.005)]
    public void Scaling_factor_is_feed_per_revolution_divided_by_increments(double feed, double increments, double expected)
    {
        Assert.Equal(expected, MotionUnits.ScalingFactor(feed, increments), 15);
    }

    [Fact]
    public void Scaling_factor_rejects_non_positive_increments()
    {
        Assert.Throws<TcException>(() => MotionUnits.ScalingFactor(10, 0));
    }

    [Theory]
    [InlineData(2000, 20000)] // NC SAF default: 2 ms = 20000 x 100 ns
    [InlineData(1000, 10000)]
    [InlineData(250, 2500)]
    public void Task_cycle_time_in_microseconds_is_converted_to_100ns_ticks(int us, int ticks)
    {
        Assert.Equal(ticks, MotionUnits.MicrosecondsToTicks(us));
        Assert.Equal(us, MotionUnits.TicksToMicroseconds(ticks));
    }

    [Theory]
    [InlineData("#x05010010", 0x05010010u)]
    [InlineData("#x00ffffff", 0x00ffffffu)]
    [InlineData("33619984", 33619984u)]
    [InlineData("0x02010040", 0x02010040u)]
    public void TwinCAT_hex_notation_is_parsed(string text, uint expected)
    {
        Assert.Equal(expected, MotionUnits.ParseTcUInt(text));
    }

    [Theory]
    [InlineData(0.0001, "0.0001")]
    [InlineData(3000.0, "3000")]
    [InlineData(0.000152587890625, "0.000152587890625")]
    [InlineData(-12.5, "-12.5")]
    [InlineData(1e-9, "0.000000001")]
    public void Numbers_are_formatted_without_exponent(double value, string expected)
    {
        Assert.Equal(expected, MotionUnits.FormatNumber(value));
    }
}
