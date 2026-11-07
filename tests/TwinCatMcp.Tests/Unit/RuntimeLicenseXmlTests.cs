using TwinCatMcp.Runtime;

namespace TwinCatMcp.Tests.Unit;

public sealed class RuntimeLicenseXmlTests
{
    [Fact]
    public void Builds_the_AddLicenses_command_accepted_by_TwinCAT_4026()
        // Verified live: <License><Guid>{...}</Guid></License> is accepted; <License Id=...>, <Id>, <LicenseId>+<Name> are rejected.
        => Assert.Equal(
            "<TreeItem><IECProjectDef><LicenseSettings><Commands><AddLicenses><License><Guid>{6952449D-F68C-49A2-ADE4-8639D85B33A4}</Guid></License>" +
            "</AddLicenses></Commands></LicenseSettings></IECProjectDef></TreeItem>",
            LicenseXml.PlcLicenseCommand("{6952449D-F68C-49A2-ADE4-8639D85B33A4}", remove: false));

    [Fact]
    public void Builds_the_RemoveLicenses_command()
        => Assert.Contains("<RemoveLicenses><License><Guid>{A}</Guid></License></RemoveLicenses>", LicenseXml.PlcLicenseCommand("{A}", remove: true));

    [Fact]
    public void Parses_the_licenses_of_a_PLC_project()
    {
        // ProduceXml() of the nested PLC project after AddLicenses (TwinCAT stores the GUID lower case without braces).
        const string xml = "<TreeItem><IECProjectDef><LicenseSettings><Licenses><License><Guid>6952449d-f68c-49a2-ade4-8639d85b33a4</Guid><Comment /></License>" +
                           "</Licenses><Commands><AddLicenses /><RemoveLicenses /></Commands></LicenseSettings></IECProjectDef></TreeItem>";

        Assert.Equal(["{6952449D-F68C-49A2-ADE4-8639D85B33A4}"], LicenseXml.ParsePlcLicenses(xml));
    }

    [Fact]
    public void A_PLC_project_without_manual_licenses_has_none()
        => Assert.Empty(LicenseXml.ParsePlcLicenses("<TreeItem><IECProjectDef><LicenseSettings><Licenses /></LicenseSettings></IECProjectDef></TreeItem>"));
}
