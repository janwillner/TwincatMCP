using TwinCatMcp.Libraries;

namespace TwinCatMcp.Tests.Unit;

public sealed class LibraryIdTests
{
    [Theory]
    // Display-name format as used by TwinCAT ("<name>, <version> (<distributor>)"), see InfoSys "Managing PLC libraries and placeholders".
    [InlineData("Tc2_Math, * (Beckhoff Automation GmbH)", "Tc2_Math", "*", "Beckhoff Automation GmbH")]
    [InlineData("Tc2_MC2, 3.3.72.0 (Beckhoff Automation GmbH)", "Tc2_MC2", "3.3.72.0", "Beckhoff Automation GmbH")]
    [InlineData("CoDeSys Memory Manager, 3.4.2.0 (3S - Smart Software Solutions GmbH)", "CoDeSys Memory Manager", "3.4.2.0", "3S - Smart Software Solutions GmbH")]
    [InlineData("Tc2_MC2, 3.3.69.0", "Tc2_MC2", "3.3.69.0", null)]
    [InlineData("Tc2_MC2", "Tc2_MC2", null, null)]
    [InlineData("  Tc2_Utilities ,  *  ", "Tc2_Utilities", "*", null)]
    [InlineData("Tc2_MC2 (Beckhoff Automation GmbH)", "Tc2_MC2", null, "Beckhoff Automation GmbH")]
    public void Parse_splits_display_name(string text, string name, string? version, string? distributor)
    {
        var id = LibraryId.Parse(text);

        Assert.Equal(name, id.Name);
        Assert.Equal(version, id.Version);
        Assert.Equal(distributor, id.Distributor);
    }

    [Fact]
    public void Parse_rejects_empty_text()
    {
        Assert.Throws<ArgumentException>(() => LibraryId.Parse("  "));
    }

    [Fact]
    public void DisplayName_uses_star_when_version_is_missing()
    {
        Assert.Equal("Tc2_MC2, * (Beckhoff Automation GmbH)", new LibraryId("Tc2_MC2", null, "Beckhoff Automation GmbH").DisplayName);
    }

    [Fact]
    public void DisplayName_round_trips_explicit_version()
    {
        Assert.Equal("Tc2_MC2, 3.3.72.0 (Beckhoff Automation GmbH)", LibraryId.Parse("Tc2_MC2, 3.3.72.0 (Beckhoff Automation GmbH)").DisplayName);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("*", true)]
    [InlineData("latest", true)]
    [InlineData("3.3.72.0", false)]
    public void IsLatest_treats_star_empty_and_latest_as_newest(string? version, bool expected)
    {
        Assert.Equal(expected, LibraryVersion.IsLatest(version));
    }

    [Theory]
    [InlineData("3.3.72.0", "3.3.69.0", 1)]
    [InlineData("3.3.9.0", "3.3.10.0", -1)]
    [InlineData("3.10.1.0", "3.10.1.0", 0)]
    [InlineData("1.2", "1.2.0.0", 0)]
    public void Compare_orders_numerically(string a, string b, int sign)
    {
        Assert.Equal(sign, Math.Sign(LibraryVersion.Compare(a, b)));
    }

    [Fact]
    public void PickNewest_returns_highest_version_of_matching_name_case_insensitively()
    {
        var installed = new[]
        {
            new LibraryId("Tc2_MC2", "3.3.69.0", "Beckhoff Automation GmbH"),
            new LibraryId("tc2_mc2", "3.3.72.0", "Beckhoff Automation GmbH"),
            new LibraryId("Tc2_MC2_Camming", "3.4.0.0", "Beckhoff Automation GmbH"),
        };

        var newest = LibraryVersion.PickNewest(installed, "TC2_MC2");

        Assert.Equal("3.3.72.0", newest?.Version);
    }

    [Fact]
    public void PickNewest_returns_null_when_nothing_matches()
    {
        Assert.Null(LibraryVersion.PickNewest([new LibraryId("A", "1.0.0.0", "X")], "B"));
    }
}
