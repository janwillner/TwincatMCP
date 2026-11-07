using TwinCatMcp.Core;
using TwinCatMcp.Io;

namespace TwinCatMcp.Tests.Unit;

public sealed class IoDeviceTypesTests
{
    [Theory]
    // Sub types from InfoSys (EtherCAT master = 111, slave = 130, Profinet controller = 113) and verified ItemSubTypeNames.
    [InlineData("ethercat-master", 111)]
    [InlineData("EtherCAT Master", 111)]
    [InlineData("ethercat", 111)]
    [InlineData("ethercat-slave", 130)]
    [InlineData("eap", 112)]
    [InlineData("ethercat-simulation", 144)]
    [InlineData("profinet-controller", 113)]
    [InlineData("Profinet I/O Device (RT)", 115)]
    [InlineData("ethernetip-scanner", 133)]
    [InlineData("EtherNet/IP Scanner", 133)]
    [InlineData("ethernetip_adapter", 139)]
    [InlineData("111", 111)]
    public void Resolves_friendly_names_aliases_and_numbers(string type, int subType)
        => Assert.Equal(subType, DeviceTypes.Resolve(type));

    [Fact]
    public void Unknown_type_lists_valid_names_in_hint()
    {
        var ex = Assert.Throws<TcException>(() => DeviceTypes.Resolve("modbus-magic"));

        Assert.Contains("ethercat-master", ex.Hint);
    }

    [Fact]
    public void Default_device_name_follows_XAE_convention_and_is_unique()
    {
        Assert.Equal("Device 1 (EtherCAT)", DeviceTypes.DefaultDeviceName(111, []));
        Assert.Equal("Device 3 (EtherCAT)", DeviceTypes.DefaultDeviceName(111, ["Device 1 (EtherCAT)", "Device 2 (EtherCAT)"]));
        Assert.Equal("Device 2 (Profinet I/O Controller (RT))", DeviceTypes.DefaultDeviceName(113, ["EtherCAT Master"]));
        Assert.Equal("Device 3 (EtherCAT)", DeviceTypes.DefaultDeviceName(111, ["X", "Device 2 (EtherCAT)"]));
    }

    private static readonly NetworkAdapter[] Adapters =
    [
        new("Ethernet", "Realtek Gaming 2.5GbE Family Controller", "10-FF-E0-63-D7-15", "{2535F020-5AEA-4CD9-9922-95CA1254D8DB}", @"\DEVICE\{2535F020-5AEA-4CD9-9922-95CA1254D8DB}", "Down", "Ethernet", 0, false),
        new("EtherCAT", "TwinCAT-Intel PCI Ethernet Adapter (Gigabit)", "00-01-05-69-BC-0A", "{11111111-2222-3333-4444-555555555555}", @"\DEVICE\{11111111-2222-3333-4444-555555555555}", "Up", "Ethernet", 1000, true),
        new("vEthernet (Default Switch)", "Hyper-V Virtual Ethernet Adapter", "00-15-5D-01-D1-00", "{BEE0149A-5E02-4C80-A72A-295E0B2F4D23}", @"\DEVICE\{BEE0149A-5E02-4C80-A72A-295E0B2F4D23}", "Up", "Ethernet", 10000, false),
    ];

    [Theory]
    [InlineData("EtherCAT", "EtherCAT")]
    [InlineData("00:01:05:69:bc:0a", "EtherCAT")]
    [InlineData("00010569BC0A", "EtherCAT")]
    [InlineData("2535F020-5AEA-4CD9-9922-95CA1254D8DB", "Ethernet")]
    [InlineData(@"\DEVICE\{2535F020-5AEA-4CD9-9922-95CA1254D8DB}", "Ethernet")]
    [InlineData("Realtek", "Ethernet")]
    [InlineData("TwinCAT-Intel", "EtherCAT")]
    public void Finds_adapter_by_name_mac_guid_or_description(string query, string expectedName)
        => Assert.Equal(expectedName, NetworkAdapters.Find(Adapters, query).Name);

    [Fact]
    public void Ambiguous_adapter_query_is_rejected()
        => Assert.Throws<TcException>(() => NetworkAdapters.Find(Adapters, "Ethernet Adapter"));
}
