using System.Xml.Linq;
using TwinCatMcp.Io;

namespace TwinCatMcp.Tests.Unit;

public sealed class IoXmlTests
{
    // Real ProduceXml() output of an EL1008 channel variable (TwinCAT 3.1.4026).
    private const string ChannelInputXml =
        "<?xml version=\"1.0\"?><TreeItem><ItemName>Input</ItemName><PathName>TIID^EtherCAT Master^Term 1 (EK1100)^Term 2 (EL1008)^Channel 1^Input</PathName>" +
        "<ItemType>7</ItemType><ItemId>14</ItemId><ChildCount>0</ChildCount><Disabled>false</Disabled><TreeImageId>12</TreeImageId><VarDef>" +
        "<VarType GUID=\"{18071995-0000-0000-0000-000000000010}\">BIT</VarType><VarBitSize>1</VarBitSize><VarBitAddr>640</VarBitAddr><VarInOut>0</VarInOut>" +
        "<VarFlags>#x0</VarFlags><OnlineValue>0</OnlineValue><AdsInfo><AmsAddress><AmsPort>11</AmsPort></AmsAddress><IndexGroup Hex=\"#x03040010\">50593808</IndexGroup>" +
        "<IndexOffset Hex=\"#xc0000280\">-1073741184</IndexOffset><Length Hex=\"#x00000001\">1</Length></AdsInfo></VarDef></TreeItem>";

    private const string PlcInputXml =
        "<?xml version=\"1.0\"?><TreeItem><ItemName>GVL_IO.bIn1</ItemName><PathName>TIPC^Plc1^Plc1 Instance^PlcTask Inputs^GVL_IO.bIn1</PathName><ItemType>7</ItemType>" +
        "<ItemId>122</ItemId><ChildCount>0</ChildCount><Disabled>false</Disabled><TreeImageId>12</TreeImageId><VarDef><VarType GUID=\"{18071995-0000-0000-0000-000000000030}\">BOOL</VarType>" +
        "<VarBitSize>8</VarBitSize><VarBitAddr>3097240</VarBitAddr><VarInOut>0</VarInOut><VarFlags>#x8000</VarFlags><OnlineValue>0</OnlineValue></VarDef></TreeItem>";

    // Real ProduceXml() of an EL7041 slave (trimmed to the parts the parser reads).
    private const string SlaveXml =
        "<?xml version=\"1.0\"?><TreeItem><ItemName>Term 5 (EL7041)</ItemName><PathName>TIID^EtherCAT Master^Term 1 (EK1100)^Term 5 (EL7041)</PathName><ItemType>5</ItemType>" +
        "<ItemSubType>9099</ItemSubType><ItemSubTypeName>EL7041 1Ch. Stepper motor output stage (50V, 5A)</ItemSubTypeName>" +
        "<BoxDef><FieldbusAddress>1005</FieldbusAddress><AmsAddress><AmsPort>0</AmsPort><AmsPortTimeout>5</AmsPortTimeout></AmsAddress></BoxDef>" +
        "<EtherCAT><Slave><Info><Name><![CDATA[Term 5 (EL7041)]]></Name><PhysAddr>1005</PhysAddr><AutoIncAddr>65532</AutoIncAddr><Physics>KK</Physics><VendorId>2</VendorId>" +
        "<ProductCode>461451346</ProductCode><RevisionNo>1703936</RevisionNo><SerialNo>0</SerialNo><ProductRevision>EL7041-0000-0026</ProductRevision></Info>" +
        "<Mailbox DataLinkLayer=\"true\"><Protocol>CoE</Protocol><Protocol>FoE</Protocol><CoE><InitCmds>" +
        "<InitCmd Fixed=\"true\" CompleteAccess=\"true\"><Transition>PS</Transition><Comment><![CDATA[download pdo 0x1C12 index]]></Comment><Timeout>0</Timeout><Ccs>1</Ccs><Index>7186</Index><SubIndex>0</SubIndex><Data>0300001602160416</Data></InitCmd>" +
        "<InitCmd><Transition>IP</Transition><Timeout>0</Timeout><Ccs>1</Ccs><Index>61569</Index><SubIndex>1</SubIndex><Data>00001a00</Data></InitCmd>" +
        "<InitCmd AdaptAutomatically=\"true\"><Transition>IP</Transition><Timeout>0</Timeout><Ccs>1</Ccs><Index>4339</Index><SubIndex>5</SubIndex><Data>0100</Data></InitCmd>" +
        "</InitCmds><CanOpenType>39</CanOpenType></CoE></Mailbox><PreviousPort Selected=\"true\"><Port>B</Port><PhysAddr>1004</PhysAddr></PreviousPort></Slave></EtherCAT></TreeItem>";

    // Real ProduceXml() of a new (unbound) EtherCAT master, device part.
    private const string MasterXml =
        "<?xml version=\"1.0\"?><TreeItem><ItemName>EtherCAT Master</ItemName><PathName>TIID^EtherCAT Master</PathName><ItemType>2</ItemType><ItemSubType>111</ItemSubType>" +
        "<ItemSubTypeName>EtherCAT Master</ItemSubTypeName><DeviceDef><AmsPort>28673</AmsPort><AmsNetId>169.254.123.103.2.1</AmsNetId><AddressInfo><Pnp><DeviceDesc></DeviceDesc>" +
        "<DeviceName></DeviceName><DeviceData>000000000000</DeviceData></Pnp></AddressInfo><ScanBoxes>false</ScanBoxes></DeviceDef></TreeItem>";

    [Fact]
    public void Parses_io_channel_variable()
    {
        var v = IoXml.ParseVarDef(ChannelInputXml)!;

        Assert.Equal("Input", v.Name);
        Assert.Equal("TIID^EtherCAT Master^Term 1 (EK1100)^Term 2 (EL1008)^Channel 1^Input", v.Path);
        Assert.Equal("BIT", v.Type);
        Assert.Equal(1, v.BitSize);
        Assert.Equal(640, v.BitAddress);
        Assert.Equal("input", v.Direction);
        Assert.Equal(11, v.AdsPort);
        Assert.Equal(0x03040010u, v.AdsIndexGroup);
        Assert.Equal(0xc0000280u, v.AdsIndexOffset);
    }

    [Fact]
    public void Parses_plc_process_image_variable()
    {
        var v = IoXml.ParseVarDef(PlcInputXml)!;

        Assert.Equal("BOOL", v.Type);
        Assert.Equal(8, v.BitSize);
        Assert.Equal("input", v.Direction);
        Assert.Null(v.AdsPort);
    }

    [Fact]
    public void Non_variable_xml_yields_null()
        => Assert.Null(IoXml.ParseVarDef(MasterXml));

    [Fact]
    public void Parses_slave_identity_and_addresses()
    {
        var s = IoXml.ParseSlaveInfo(SlaveXml)!;

        Assert.Equal(1005, s.PhysAddr);
        Assert.Equal(65532, s.AutoIncAddr);
        Assert.Equal(2u, s.VendorId);
        Assert.Equal(461451346u, s.ProductCode);
        Assert.Equal(1703936u, s.RevisionNo);
        Assert.Equal(0u, s.SerialNo);
        Assert.Equal("EL7041-0000-0026", s.ProductRevision);
        Assert.Equal("B", s.PreviousPort);
        Assert.Equal(1004, s.PreviousPhysAddr);
        Assert.Equal(["CoE", "FoE"], s.MailboxProtocols);
    }

    [Fact]
    public void Slave_info_is_null_for_non_EtherCAT_items()
        => Assert.Null(IoXml.ParseSlaveInfo(MasterXml));

    [Fact]
    public void Parses_CoE_startup_list()
    {
        var cmds = IoXml.ParseCoeInitCmds(SlaveXml);

        Assert.Equal(3, cmds.Count);
        var first = cmds[0];
        Assert.Equal("PS", first.Transition);
        Assert.Equal(0x1C12, first.Index);
        Assert.Equal(0, first.SubIndex);
        Assert.Equal("0300001602160416", first.Data);
        Assert.Equal("download pdo 0x1C12 index", first.Comment);
        Assert.True(first.Fixed);
        Assert.True(first.CompleteAccess);
        Assert.Equal(0xF081, cmds[1].Index);
        Assert.False(cmds[1].Fixed);
    }

    [Fact]
    public void CoE_startup_xml_round_trips_through_the_parser()
    {
        var cmds = IoXml.ParseCoeInitCmds(SlaveXml).ToList();
        cmds.Add(new CoeInitCmd("PS", 0x8010, 0x01, "e803", "Max current", false, false, 0, 1));

        var xml = IoXml.BuildCoeInitCmdsXml(cmds);
        var back = IoXml.ParseCoeInitCmds(xml);

        Assert.StartsWith("<TreeItem><EtherCAT><Slave><Mailbox><CoE><InitCmds>", xml);
        Assert.Equal(4, back.Count);
        Assert.Equal(cmds[3], back[3]);
        Assert.Equal(cmds[0], back[0]);
    }

    [Fact]
    public void Built_CoE_command_uses_decimal_index_and_hex_data()
    {
        var xml = IoXml.BuildCoeInitCmdsXml([new CoeInitCmd("PS", 0x8010, 1, "e803", "Max current", false, false, 0, 1)]);
        var cmd = XElement.Parse(xml).Descendants("InitCmd").Single();

        Assert.Equal("PS", (string?)cmd.Element("Transition"));
        Assert.Equal("32784", (string?)cmd.Element("Index"));
        Assert.Equal("1", (string?)cmd.Element("SubIndex"));
        Assert.Equal("e803", (string?)cmd.Element("Data"));
        Assert.Equal("1", (string?)cmd.Element("Ccs"));
        Assert.Null(cmd.Attribute("Fixed"));
    }

    [Fact]
    public void Upsert_appends_new_startup_entry_and_keeps_existing_ones_verbatim()
    {
        var xml = IoXml.UpsertCoeInitCmd(SlaveXml, new CoeInitCmd("PS", 0x8010, 1, "e803", "Max current", false, false, 0, 1));
        var cmds = IoXml.ParseCoeInitCmds(xml);

        Assert.Equal(4, cmds.Count);
        Assert.Equal(0x8010, cmds[3].Index);
        Assert.Contains("AdaptAutomatically=\"true\"", xml);
        Assert.Contains("CompleteAccess=\"true\"", xml);
    }

    [Fact]
    public void Upsert_replaces_entry_with_same_index_subindex_and_transition()
    {
        var once = IoXml.UpsertCoeInitCmd(SlaveXml, new CoeInitCmd("IP", 0xF081, 1, "11223344", null, false, false, 0, 1));
        var cmds = IoXml.ParseCoeInitCmds(once);

        Assert.Equal(3, cmds.Count);
        Assert.Equal("11223344", cmds.Single(c => c.Index == 0xF081).Data);
    }

    [Fact]
    public void Upsert_refuses_to_change_fixed_entries_generated_by_TwinCAT()
        => Assert.ThrowsAny<Exception>(() => IoXml.UpsertCoeInitCmd(SlaveXml, new CoeInitCmd("PS", 0x1C12, 0, "00", null, false, false, 0, 1)));

    [Fact]
    public void Remove_deletes_matching_user_entries_only()
    {
        var (xml, removed) = IoXml.RemoveCoeInitCmd(SlaveXml, 0xF081, 1, null);

        Assert.Equal(1, removed);
        Assert.Equal(2, IoXml.ParseCoeInitCmds(xml).Count);
    }

    [Theory]
    [InlineData("1000", "UINT", "e803")]
    [InlineData("1000", "UDINT", "e8030000")]
    [InlineData("-1", "INT", "ffff")]
    [InlineData("5", "USINT", "05")]
    [InlineData("TRUE", "BOOL", "01")]
    [InlineData("0x1A00", "WORD", "001a")]
    [InlineData("1.5", "REAL", "0000c03f")]
    [InlineData("hex:0102A0", "", "0102a0")]
    [InlineData("0102A0", "HEX", "0102a0")]
    public void Encodes_CoE_values_little_endian(string value, string dataType, string expectedHex)
        => Assert.Equal(expectedHex, CoeValue.Encode(value, dataType));

    [Theory]
    [InlineData("e803", "UINT", "1000")]
    [InlineData("ffff", "INT", "-1")]
    [InlineData("0000c03f", "REAL", "1.5")]
    [InlineData("0102", "", "0x0201")]
    public void Decodes_CoE_values(string hex, string dataType, string expected)
        => Assert.Equal(expected, CoeValue.Decode(hex, dataType));

    [Fact]
    public void Rejects_value_that_does_not_fit_the_type()
        => Assert.ThrowsAny<Exception>(() => CoeValue.Encode("70000", "UINT"));

    [Fact]
    public void Parses_adapter_and_net_id_of_device()
    {
        var a = IoXml.ParseDeviceAddress(MasterXml)!;

        Assert.Equal("169.254.123.103.2.1", a.AmsNetId);
        Assert.Equal("", a.DeviceName);
        Assert.Equal("000000000000", a.Mac);
        Assert.False(a.IsBound);
    }

    [Fact]
    public void Builds_adapter_xml_in_DeviceDef_AddressInfo_Pnp_form()
    {
        var xml = IoXml.BuildAdapterXml("TwinCAT-Intel PCI Ethernet Adapter (Gigabit)", @"\DEVICE\{2535F020-5AEA-4CD9-9922-95CA1254D8DB}", "10-FF-E0-63-D7-15");
        var pnp = XElement.Parse(xml).Element("DeviceDef")!.Element("AddressInfo")!.Element("Pnp")!;

        Assert.Equal("TwinCAT-Intel PCI Ethernet Adapter (Gigabit)", (string?)pnp.Element("DeviceDesc"));
        Assert.Equal(@"\DEVICE\{2535F020-5AEA-4CD9-9922-95CA1254D8DB}", (string?)pnp.Element("DeviceName"));
        Assert.Equal("10ffe063d715", (string?)pnp.Element("DeviceData"));
    }

    [Theory]
    [InlineData("10-FF-E0-63-D7-15", "10ffe063d715")]
    [InlineData("10:ff:e0:63:d7:15", "10ffe063d715")]
    [InlineData("10FFE063D715", "10ffe063d715")]
    public void Normalizes_mac_addresses(string mac, string expected)
        => Assert.Equal(expected, IoXml.NormalizeMac(mac));

    [Fact]
    public void Parses_found_devices_of_a_device_scan()
    {
        // Structure documented in InfoSys "Performing a device scan": TreeItem/DeviceGrpDef/FoundDevices/Device.
        const string xml = "<TreeItem><ItemName>Devices</ItemName><DeviceGrpDef><FoundDevices>" +
                           "<Device><ItemName>Device 1 (EtherCAT)</ItemName><ItemSubType>111</ItemSubType><ItemSubTypeName>EtherCAT Master</ItemSubTypeName>" +
                           "<AddressInfo><Pnp><DeviceDesc>TwinCAT-Intel PCI Ethernet Adapter (Gigabit)</DeviceDesc><DeviceName>\\DEVICE\\{ABC}</DeviceName><DeviceData>00010569bc0a</DeviceData></Pnp></AddressInfo></Device>" +
                           "</FoundDevices></DeviceGrpDef></TreeItem>";

        var found = Assert.Single(IoXml.ParseFoundDevices(xml));

        Assert.Equal(111, found.SubType);
        Assert.Equal("EtherCAT Master", found.SubTypeName);
        Assert.Equal("Device 1 (EtherCAT)", found.Name);
        Assert.Equal("TwinCAT-Intel PCI Ethernet Adapter (Gigabit)", found.Adapter);
        Assert.StartsWith("<AddressInfo>", found.AddressInfoXml);
    }

    [Fact]
    public void Empty_scan_result_yields_no_devices()
        => Assert.Empty(IoXml.ParseFoundDevices("<?xml version=\"1.0\"?><TreeItem><ItemName languageIndependentName=\"TIID\">Devices</ItemName><DeviceGrpDef><FoundDevices/></DeviceGrpDef></TreeItem>"));

    [Fact]
    public void Address_xml_wraps_found_address_info_in_DeviceDef()
        => Assert.Equal("<TreeItem><DeviceDef><AddressInfo><Pnp/></AddressInfo></DeviceDef></TreeItem>", IoXml.WrapDeviceDef("<AddressInfo><Pnp/></AddressInfo>"));
}
