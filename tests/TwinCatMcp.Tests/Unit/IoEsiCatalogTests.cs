using System.Text;
using TwinCatMcp.Io;

namespace TwinCatMcp.Tests.Unit;

public sealed class IoEsiCatalogTests
{
    // Trimmed excerpt in the shape of "Beckhoff EL1xxx.xml" / "Beckhoff EK11xx.xml" (ETG.2000 EtherCATInfo).
    private const string BeckhoffEsi = """
        <?xml version="1.0" encoding="ISO-8859-1"?>
        <EtherCATInfo Version="1.2">
          <Vendor FileVersion="6">
            <Id>2</Id>
            <Name>Beckhoff Automation GmbH &amp; Co. KG</Name>
          </Vendor>
          <Descriptions>
            <Groups>
              <Group SortOrder="390"><Type>DigIn</Type><Name LcId="1033">Digital Input Terminals (EL1xxx)</Name></Group>
            </Groups>
            <Devices>
              <Device Physics="YY">
                <Type ProductCode="#x03f03052" RevisionNo="#x00100000">EL1008</Type>
                <Name LcId="1033"><![CDATA[EL1008 8Ch. Dig. Input 24V, 3ms]]></Name>
                <Name LcId="1031"><![CDATA[EL1008 8K. Dig. Eingang 24V, 3ms]]></Name>
                <GroupType>DigIn</GroupType>
                <Fmmu>Inputs</Fmmu>
                <Sm StartAddress="#x1000" ControlByte="#x00" Enable="1">Inputs</Sm>
                <TxPdo Fixed="1" Sm="0"><Index>#x1a00</Index><Name>Channel 1</Name>
                  <Entry><Index>#x6000</Index><SubIndex>1</SubIndex><BitLen>1</BitLen><Name>Input</Name><DataType>BOOL</DataType></Entry>
                </TxPdo>
              </Device>
              <Device Physics="YY">
                <Type ProductCode="#x03f03052" RevisionNo="#x00120000">EL1008</Type>
                <Name LcId="1031"><![CDATA[EL1008 8K. Dig. Eingang 24V, 3ms]]></Name>
                <Name LcId="1033"><![CDATA[EL1008 8Ch. Dig. Input 24V, 3ms]]></Name>
                <GroupType>DigIn</GroupType>
              </Device>
              <Device Physics="YY">
                <Type ProductCode="#x03EC3052" RevisionNo="#x0011000A" CheckRevisionNo="LW_EQ">EL1004-0010</Type>
                <Name LcId="1033"><![CDATA[EL1004-0010 4Ch. Dig. Input 24V, 10µs, Sensor Supply]]></Name>
                <GroupType>DigIn</GroupType>
              </Device>
              <Device Physics="YY">
                <Type ProductCode="#x044c2c52" RevisionNo="#x00110000">EK1100</Type>
                <Name LcId="1033"><![CDATA[EK1100 EtherCAT Coupler (2A E-Bus)]]></Name>
                <GroupType>SystemBk</GroupType>
              </Device>
            </Devices>
          </Descriptions>
        </EtherCATInfo>
        """;

    private const string ThirdPartyEsi = """
        <?xml version="1.0" encoding="utf-8"?>
        <EtherCATInfo>
          <Vendor><Id>#x00000539</Id><Name>ACME Drives</Name></Vendor>
          <Descriptions>
            <Devices>
              <Device>
                <Type ProductCode="#x00001234" RevisionNo="#x00000002">ACME Servo X1</Type>
                <Name><![CDATA[ACME Servo drive X1 (CiA402)]]></Name>
                <GroupType>Drives</GroupType>
              </Device>
            </Devices>
          </Descriptions>
        </EtherCATInfo>
        """;

    private static List<EsiDevice> Load(string xml, string file = "Beckhoff EL1xxx.xml")
    {
        using var stream = new MemoryStream(Encoding.Latin1.GetBytes(xml));
        return EsiParser.Parse(stream, file).ToList();
    }

    private static List<EsiDevice> Catalog() => [.. Load(BeckhoffEsi), .. Load(ThirdPartyEsi, "ACME.xml")];

    [Fact]
    public void Parser_reads_type_identity_english_name_group_and_vendor()
    {
        var devices = Load(BeckhoffEsi);

        Assert.Equal(4, devices.Count);
        var el1008 = devices[1];
        Assert.Equal("EL1008", el1008.Type);
        Assert.Equal(0x03f03052u, el1008.ProductCode);
        Assert.Equal(0x00120000u, el1008.RevisionNo);
        Assert.Equal(2u, el1008.VendorId);
        Assert.Equal("Beckhoff Automation GmbH & Co. KG", el1008.VendorName);
        Assert.Equal("EL1008 8Ch. Dig. Input 24V, 3ms", el1008.Name);
        Assert.Equal("DigIn", el1008.Group);
        Assert.Equal("Beckhoff EL1xxx.xml", el1008.File);
        Assert.Equal("EL1008-0000-0018", el1008.ProductRevision);
    }

    [Fact]
    public void Parser_ignores_names_of_nested_pdo_entries()
        => Assert.Equal("EL1008 8Ch. Dig. Input 24V, 3ms", Load(BeckhoffEsi)[0].Name);

    [Fact]
    public void Third_party_devices_get_an_identity_string_instead_of_a_product_revision()
    {
        var acme = Load(ThirdPartyEsi, "ACME.xml").Single();

        Assert.Equal(0x539u, acme.VendorId);
        Assert.Null(acme.ProductRevision);
        Assert.Equal("V00000539_P00001234_R00000002", acme.CreateInfo);
    }

    [Fact]
    public void Resolve_short_name_picks_the_latest_revision()
    {
        var d = EsiCatalog.Resolve(Catalog(), "el1008");

        Assert.NotNull(d);
        Assert.Equal("EL1008-0000-0018", d!.ProductRevision);
    }

    [Fact]
    public void Latest_revision_ignores_special_9995_revisions_and_invisible_devices()
    {
        // The TwinCAT 4026 ESI files contain e.g. EK1100 with RevisionNo #x270b0000 ("EK1100-0000-9995"); the XAE picks EK1100-0000-0018.
        const string xml = """
            <EtherCATInfo><Vendor><Id>2</Id><Name>Beckhoff</Name></Vendor><Descriptions><Devices>
              <Device Physics="YY"><Type ProductCode="#x044c2c52" RevisionNo="#x00110000">EK1100</Type><Name LcId="1033">EK1100 EtherCAT Coupler</Name></Device>
              <Device Physics="YY"><Type ProductCode="#x044c2c52" RevisionNo="#x00120000">EK1100</Type><Name LcId="1033">EK1100 EtherCAT Coupler</Name></Device>
              <Device Physics="YY" Invisible="1"><Type ProductCode="#x044c2c52" RevisionNo="#x00130000">EK1100</Type><Name LcId="1033">EK1100 hidden</Name></Device>
              <Device Physics="YKY"><Type ProductCode="#x044c2c52" RevisionNo="#x270b0000">EK1100</Type><Name LcId="1033">EK1100 Ethernet Coupler Terminal</Name></Device>
            </Devices></Descriptions></EtherCATInfo>
            """;
        var devices = Load(xml);

        Assert.True(devices[2].Invisible);
        Assert.Equal("EK1100-0000-0018", EsiCatalog.Resolve(devices, "EK1100")!.ProductRevision);
        var hit = Assert.Single(EsiCatalog.Search(devices, "EK1100", null, 5));
        Assert.Equal("EK1100-0000-0018", hit.Latest);
        Assert.Equal("EK1100 EtherCAT Coupler", hit.Name);
        Assert.Equal("EK1100-0000-9995", EsiCatalog.Resolve(devices, "EK1100-0000-9995")!.ProductRevision);
    }

    [Fact]
    public void Resolve_full_product_revision_picks_that_revision()
        => Assert.Equal(0x00100000u, EsiCatalog.Resolve(Catalog(), "EL1008-0000-0016")!.RevisionNo);

    [Fact]
    public void Resolve_variant_without_revision()
        => Assert.Equal("EL1004-0010-0017", EsiCatalog.Resolve(Catalog(), "EL1004-0010")!.ProductRevision);

    [Fact]
    public void Resolve_third_party_by_type_text()
        => Assert.Equal("V00000539_P00001234_R00000002", EsiCatalog.Resolve(Catalog(), "ACME Servo X1")!.CreateInfo);

    [Fact]
    public void Resolve_unknown_returns_null()
        => Assert.Null(EsiCatalog.Resolve(Catalog(), "EL9999"));

    [Fact]
    public void Search_groups_revisions_per_type_with_latest_first()
    {
        var hits = EsiCatalog.Search(Catalog(), "EL1008", null, 10);

        var hit = Assert.Single(hits);
        Assert.Equal("EL1008", hit.Type);
        Assert.Equal("EL1008-0000-0018", hit.Latest);
        Assert.Equal(["EL1008-0000-0018", "EL1008-0000-0016"], hit.Revisions);
    }

    [Fact]
    public void Search_matches_all_words_in_description()
    {
        var hits = EsiCatalog.Search(Catalog(), "dig input 10µs", null, 10);

        Assert.Equal("EL1004-0010", Assert.Single(hits).Type);
    }

    [Fact]
    public void Search_ranks_exact_type_before_prefix_and_description_matches()
    {
        var hits = EsiCatalog.Search(Catalog(), "EL100", null, 10);

        Assert.Equal(["EL1004-0010", "EL1008"], hits.Select(h => h.Type).ToArray());
    }

    [Fact]
    public void Search_filters_by_vendor_name_or_id()
    {
        Assert.Equal("ACME Servo X1", Assert.Single(EsiCatalog.Search(Catalog(), "", "acme", 10)).Type);
        Assert.Equal("ACME Servo X1", Assert.Single(EsiCatalog.Search(Catalog(), "servo", "0x539", 10)).Type);
        Assert.Empty(EsiCatalog.Search(Catalog(), "servo", "beckhoff", 10));
    }

    [Fact]
    public void Search_filters_by_group()
        => Assert.Equal("EK1100", Assert.Single(EsiCatalog.Search(Catalog(), "SystemBk", null, 10)).Type);

    [Fact]
    public void Parser_skips_broken_files_without_throwing()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<EtherCATInfo><Vendor><Id>2</Id></Vendor><Descriptions><Devices><Device><Type"));

        var devices = EsiParser.Parse(stream, "broken.xml").ToList();

        Assert.Empty(devices);
    }
}
