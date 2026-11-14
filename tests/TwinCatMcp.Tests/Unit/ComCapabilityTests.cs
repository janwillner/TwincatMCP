using TwinCatMcp.Core;

namespace TwinCatMcp.Tests.Unit;

/// <summary>Newer Automation Interface versions (e.g. ITcSysManager18 on 4026) may be missing on older XAEs such as 4024.</summary>
public sealed class ComCapabilityTests
{
    public interface IOld
    {
    }

    public interface INew : IOld
    {
    }

    private sealed class OldOnly : IOld
    {
    }

    private sealed class Both : INew
    {
    }

    [Fact]
    public void Returns_the_object_as_the_newer_interface_when_it_is_implemented()
    {
        var obj = new Both();

        Assert.Same(obj, ComCapability.Require<INew>(obj, "Project variants"));
    }

    [Fact]
    public void Missing_interface_raises_a_user_facing_error_naming_feature_interface_and_version()
    {
        var ex = Assert.Throws<TcException>(() => ComCapability.Require<INew>(new OldOnly(), "Project variants"));

        Assert.Contains("Project variants", ex.Message);
        Assert.Contains("INew", ex.Message);
        Assert.Contains("4026", ex.Hint);
    }

    [Fact]
    public void Required_version_in_the_hint_can_be_overridden()
    {
        var ex = Assert.Throws<TcException>(() => ComCapability.Require<INew>(new OldOnly(), "SARIF export", "TwinCAT 3.1.4026.20"));

        Assert.Contains("3.1.4026.20", ex.Hint);
    }
}
