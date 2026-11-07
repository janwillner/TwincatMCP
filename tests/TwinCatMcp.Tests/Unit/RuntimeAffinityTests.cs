using TwinCatMcp.Core;
using TwinCatMcp.Runtime;

namespace TwinCatMcp.Tests.Unit;

public sealed class RuntimeAffinityTests
{
    [Theory]
    [InlineData("#x0", 0ul)]
    [InlineData("#x1", 1ul)]
    [InlineData("#x0000000000000007", 7ul)]
    [InlineData("#x8000000000000000", 0x8000000000000000ul)]
    [InlineData("0x0F", 15ul)]
    [InlineData("12", 12ul)]
    public void Parses_TwinCAT_hex_masks(string text, ulong expected)
        => Assert.Equal(expected, Affinity.Parse(text));

    [Fact]
    public void Formats_masks_like_the_Beckhoff_sample()
        => Assert.Equal("#x0000000000000007", Affinity.Format(7));

    [Fact]
    public void Converts_between_core_list_and_mask()
    {
        Assert.Equal(0b1011ul, Affinity.FromCores([0, 1, 3]));
        Assert.Equal([0, 1, 3], Affinity.ToCores(0b1011));
        Assert.Empty(Affinity.ToCores(0));
    }

    [Fact]
    public void Rejects_cores_outside_0_to_63()
        => Assert.Throws<TcException>(() => Affinity.FromCores([64]));
}
