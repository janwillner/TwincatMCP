using TwinCatMcp.Core;
using TwinCatMcp.Runtime;

namespace TwinCatMcp.Tests.Unit;

/// <summary>TC170x Usermode Runtime configuration files (3.1\TcRegistry.xml, Target\License\*.tclrs).</summary>
public sealed class RuntimeUmrtTests
{
    // TcRegistry.xml of a UmRT instance after the first start (the runtime wrote AmsNetId 199.4.42.250.1.1).
    private const string Registry =
        "<?xml version=\"1.0\"?><TcRegistry><Key Name=\"HKLM\"><Key Name=\"Software\"><Key Name=\"Beckhoff\"><Key Name=\"TwinCAT3\">" +
        "<Value Name=\"CurrentVersion\" Type=\"SZ\">3.1</Value><Key Name=\"System\"><Value Name=\"RunAsDevice\" Type=\"DW\">1</Value>" +
        "<Value Name=\"AmsNetId\" Type=\"BIN\">C7042AFA0101</Value></Key><Key Name=\"3.1\"></Key></Key></Key></Key></Key></TcRegistry>";

    // TcRegistry.xml of the copy template (no AmsNetId yet).
    private const string Template =
        "<?xml version=\"1.0\"?><TcRegistry><Key Name=\"HKLM\"><Key Name=\"Software\"><Key Name=\"Beckhoff\"><Key Name=\"TwinCAT3\">" +
        "<Value Name=\"CurrentVersion\" Type=\"SZ\">3.1</Value><Key Name=\"System\"><Value Name=\"RunAsDevice\" Type=\"DW\">1</Value></Key>" +
        "<Key Name=\"3.1\"></Key></Key></Key></Key></Key></TcRegistry>";

    [Theory]
    [InlineData("C7042AFA0101", "199.4.42.250.1.1")]
    [InlineData("A9FE7B670101", "169.254.123.103.1.1")]
    [InlineData("C0A804010101", "192.168.4.1.1.1")]
    public void Converts_BIN_registry_value_to_dotted_net_id(string bin, string netId)
    {
        Assert.Equal(netId, UmrtConfig.NetIdFromBin(bin));
        Assert.Equal(bin, UmrtConfig.NetIdToBin(netId));
    }

    [Fact]
    public void Reads_the_AmsNetId_from_TcRegistry()
    {
        Assert.Equal("199.4.42.250.1.1", UmrtConfig.ReadNetId(Registry));
        Assert.Null(UmrtConfig.ReadNetId(Template));
    }

    [Fact]
    public void Writes_the_AmsNetId_into_a_template_registry()
    {
        var updated = UmrtConfig.WriteNetId(Template, "10.1.2.3.1.1");

        Assert.Equal("10.1.2.3.1.1", UmrtConfig.ReadNetId(updated));
        Assert.Contains("<Value Name=\"AmsNetId\" Type=\"BIN\">0A0102030101</Value>", updated);
    }

    [Fact]
    public void Overwrites_an_existing_AmsNetId()
        => Assert.Equal("5.6.7.8.1.1", UmrtConfig.ReadNetId(UmrtConfig.WriteNetId(Registry, "5.6.7.8.1.1")));

    [Theory]
    [InlineData("1.2.3.4.1")]
    [InlineData("1.2.3.4.1.256")]
    [InlineData("abc")]
    public void Rejects_invalid_net_ids(string netId)
        => Assert.Throws<TcException>(() => UmrtConfig.NetIdToBin(netId));

    [Theory]
    [InlineData("UmRT_Test")]
    [InlineData("Mcp-1")]
    public void Accepts_simple_instance_names(string name)
        => UmrtConfig.ValidateInstanceName(name);

    [Theory]
    [InlineData("")]
    [InlineData("..\\evil")]
    [InlineData("a/b")]
    [InlineData("with space")]
    public void Rejects_instance_names_that_are_not_plain_folder_names(string name)
        => Assert.Throws<TcException>(() => UmrtConfig.ValidateInstanceName(name));

    // Shape of a TwinCAT trial license file (Target\License\TrialLicense.tclrs); key shortened.
    private const string TrialLicense =
        "<?xml version=\"1.0\"?><TcLicenseInfo xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><LicenseInfo>" +
        "<SystemId>{4819895A-31C2-511B-D5DD-7DF812C76D7C}</SystemId><IssueTime>2026-11-07T09:17:00</IssueTime><ExpireTime>2026-11-15T00:00:00</ExpireTime>" +
        "<LicenseKey>b9fa</LicenseKey><License><LicenseId>{4C256767-E6E6-4AF5-BD68-9F7ABAD0C200}</LicenseId><Name>TC3 ADS</Name><OrderNo>TC1000</OrderNo></License>" +
        "<License><LicenseId>{66689887-CCBD-452C-AC9A-039D997C6E66}</LicenseId><Name>TC3 PLC</Name><OrderNo>TC1200</OrderNo></License></LicenseInfo></TcLicenseInfo>";

    [Fact]
    public void License_check_requires_a_valid_file_with_a_PLC_license_when_a_PLC_is_configured()
    {
        var valid = LicenseFile.Parse(TrialLicense, new DateTime(2026, 11, 10));
        var expired = LicenseFile.Parse(TrialLicense, new DateTime(2026, 11, 20));

        Assert.Null(LicenseFile.MissingLicense([valid], needsPlc: true));
        Assert.Contains("no license file", LicenseFile.MissingLicense([], needsPlc: false));
        Assert.Contains("expired", LicenseFile.MissingLicense([expired], needsPlc: false));
        var noPlc = valid with { Licenses = [valid.Licenses[0]] };
        Assert.Contains("TC1200", LicenseFile.MissingLicense([noPlc], needsPlc: true));
        Assert.Null(LicenseFile.MissingLicense([noPlc], needsPlc: false));
    }

    [Fact]
    public void Parses_a_license_file()
    {
        var f = LicenseFile.Parse(TrialLicense, new DateTime(2026, 11, 10));

        Assert.Equal("{4819895A-31C2-511B-D5DD-7DF812C76D7C}", f.SystemId);
        Assert.Equal(new DateTime(2026, 11, 15), f.ExpireTime);
        Assert.False(f.Expired);
        Assert.Equal(["TC1000", "TC1200"], f.Licenses.Select(l => l.OrderNo));
        Assert.Equal("TC3 PLC", f.Licenses[1].Name);
        Assert.True(LicenseFile.Parse(TrialLicense, new DateTime(2026, 11, 16)).Expired);
    }
}
