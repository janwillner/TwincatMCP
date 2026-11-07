using System.Text;
using TwinCAT.Ads;
using TwinCatMcp.Ads;
using TwinCatMcp.Core;

namespace TwinCatMcp.Tests.Unit;

/// <summary>
/// System service (AMS port 10000) payload layouts. Literals come from TwinCAT 3.1.4026's SDK header
/// SystemServices.h (AdsSysServState, TAdsSysAddRemote, TFOPEN_INDEXOFFS) and were confirmed against the
/// local system service (route enumeration of a temporary dummy route, SYSSERV_STATE).
/// </summary>
public sealed class AdsSystemServiceProtocolTests
{
    [Fact]
    public void Parses_sysserv_state()
    {
        // adsState=5 devState=1 restartIndex=22 version 3.1 build 4026 platform=x64 osType=3 flags=0x0620
        var s = SystemServiceProtocol.ParseSysServState(Convert.FromHexString("0500010016000301BA0F010320060000"));

        Assert.Equal(AdsState.Run, s.AdsState);
        Assert.Equal(1, s.DeviceState);
        Assert.Equal(22, s.RestartIndex);
        Assert.Equal("3.1.4026", s.Version);
        Assert.Equal("x64", s.Platform);
        Assert.Equal(6, s.RestartCounter);
        Assert.False(s.ConfigModeOnly);
    }

    private const string RouteEntryHex =
        "0AFFFF010101" + "0100" + "01000000" + "60EA0000" + "00000000" + "000000000000000000000000" +
        "0D000000" + "0E000000" + "00000000" +
        "31302E3235352E3235352E3100" + "4D43505F50524F42455F544D5000";

    [Fact]
    public void Parses_enumerated_route_entry()
    {
        var r = SystemServiceProtocol.ParseRouteEntry(Convert.FromHexString(RouteEntryHex));

        Assert.Equal("MCP_PROBE_TMP", r.Name);
        Assert.Equal("10.255.255.1.1.1", r.NetId);
        Assert.Equal("10.255.255.1", r.Address);
        Assert.Equal("TCP_IP", r.Transport);
        Assert.True(r.Temporary);
        Assert.Equal(60000u, r.TimeoutMs);
    }

    [Fact]
    public void Builds_add_route_request()
    {
        var bytes = SystemServiceProtocol.BuildAddRoute("MCP_PROBE_TMP", AmsNetId.Parse("10.255.255.1.1.1"), "10.255.255.1", temporary: true);

        var expected = "0AFFFF010101" + "0100" + "01000000" + "00000000" + "00000000" + "000000000000000000000000" +
                       "0D000000" + "0E000000" + "00000000" +
                       "31302E3235352E3235352E3100" + "4D43505F50524F42455F544D5000";
        Assert.Equal(expected, Convert.ToHexString(bytes));
    }

    [Fact]
    public void Builds_delete_route_request_as_zero_terminated_name()
    {
        Assert.Equal("4D43505F50524F42455F544D5000", Convert.ToHexString(SystemServiceProtocol.BuildDeleteRoute("MCP_PROBE_TMP")));
    }

    // TFOPEN_INDEXOFFS: bit0 read, bit1 write, bit2 append, bit3 plus, bit4 binary, bit6 ensureDirectory,
    // bits 16..31 openPath (SYSTEMSERVICE_OPENGENERIC=1, OPENBOOTPATH=4, OPENTARGETPATH=5)
    [Theory]
    [InlineData("generic", FileOpenMode.Read, 0x00010011u)]
    [InlineData("boot", FileOpenMode.Read, 0x00040011u)]
    [InlineData("target", FileOpenMode.Read, 0x00050011u)]
    [InlineData("generic", FileOpenMode.Write, 0x0001005Au)]
    [InlineData("generic", FileOpenMode.Append, 0x0001005Cu)]
    public void Builds_fopen_index_offset(string root, FileOpenMode mode, uint expected)
    {
        Assert.Equal(expected, SystemServiceProtocol.FileOpenFlags(root, mode));
    }

    [Fact]
    public void Unknown_path_root_lists_valid_roots()
    {
        var ex = Assert.Throws<TcException>(() => SystemServiceProtocol.FileOpenFlags("moon", FileOpenMode.Read));
        Assert.Contains("boot", ex.Hint);
    }

    [Fact]
    public void Parses_file_find_entry()
    {
        // handle(4) + WIN32_FIND_DATAA (attributes, 3x FILETIME, sizeHigh, sizeLow, 2 reserved, name[260], alt[14]) = 324 bytes
        var buf = new byte[324];
        BitConverter.GetBytes(0xF601u).CopyTo(buf, 0);
        BitConverter.GetBytes(0x10u).CopyTo(buf, 4);
        BitConverter.GetBytes(new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc()).CopyTo(buf, 24);
        BitConverter.GetBytes(1u).CopyTo(buf, 32);
        BitConverter.GetBytes(5u).CopyTo(buf, 36);
        Encoding.ASCII.GetBytes("Boot").CopyTo(buf, 48);

        var e = SystemServiceProtocol.ParseFileFind(buf);

        Assert.Equal(0xF601u, e.Handle);
        Assert.Equal("Boot", e.Name);
        Assert.True(e.IsDirectory);
        Assert.Equal((1L << 32) + 5, e.Size);
        Assert.Equal(new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc), e.ModifiedUtc);
    }

    [Theory]
    [InlineData(0, "x86")]
    [InlineData(1, "x64")]
    [InlineData(4, "ARM64")]
    public void Names_platforms(byte value, string expected)
    {
        Assert.Equal(expected, SystemServiceProtocol.PlatformName(value));
    }
}
