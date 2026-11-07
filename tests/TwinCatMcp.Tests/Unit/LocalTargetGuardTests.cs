using TwinCatMcp.Core;

namespace TwinCatMcp.Tests.Unit;

public sealed class LocalTargetGuardTests
{
    private const string Local = "169.254.123.103.1.1";

    [Theory]
    [InlineData("169.254.123.103.1.1")]
    [InlineData("local")]
    [InlineData("LOCAL")]
    [InlineData("localhost")]
    [InlineData("127.0.0.1.1.1")]
    [InlineData("")]
    [InlineData(null)]
    public void The_own_system_is_recognised_in_all_spellings(string? netId)
        => Assert.True(LocalTargetGuard.IsLocal(netId, Local));

    [Theory]
    [InlineData("199.4.42.250.1.1")] // a separate Usermode Runtime instance on the same PC
    [InlineData("5.12.34.56.1.1")]
    public void Other_systems_are_not_local(string netId)
        => Assert.False(LocalTargetGuard.IsLocal(netId, Local));

    [Fact]
    public void Writing_to_the_own_system_is_refused_without_explicit_permission()
    {
        var ex = Assert.Throws<TcException>(() => LocalTargetGuard.Ensure("local", allowLocalSystem: false, "activate the configuration", Local));

        Assert.Contains("allowLocalSystem", ex.Hint);
        Assert.Contains(Local, ex.Message);
    }

    [Fact]
    public void Explicit_permission_or_a_customer_ads_port_passes()
    {
        LocalTargetGuard.Ensure("local", allowLocalSystem: true, "activate", Local);
        LocalTargetGuard.Ensure("local", allowLocalSystem: false, "write symbols", Local, port: 25123);
        LocalTargetGuard.Ensure("199.4.42.250.1.1", allowLocalSystem: false, "activate", Local);
    }

    [Fact]
    public void Twincat_runtime_ports_on_the_own_system_are_guarded()
        => Assert.Throws<TcException>(() => LocalTargetGuard.Ensure("local", false, "write symbols", Local, port: 851));
}
