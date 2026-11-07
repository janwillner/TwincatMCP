using TwinCAT.Ads;
using TwinCAT.Ads.Native;
using TwinCatMcp.Ads;

namespace TwinCatMcp.Tests.Unit;

/// <summary>
/// UDP discovery / route protocol on port 48899 (cookie 0x71146603, service ids ServerInfo=1, AddRoute=6,
/// response flag 0x80000000, tags TcVersion=3, TargetOs=4, ComputerName=5, NetId=7, RouteName=12, UserName=13,
/// Password=2, Fingerprint=18) as implemented by Beckhoff's MIT licensed ADS library (AdsLib.cpp) and the
/// TwinCAT.Ads.Native.UdpDiscovery* types. The response literal was captured from the local TwinCAT 3.1.4026 system.
/// </summary>
public sealed class AdsDiscoveryProtocolTests
{
    // header + AmsAddr(169.254.123.103.1.1:10000) + 4 tags: ComputerName "JanPC", TargetOs (OSVERSIONINFO, 148 bytes:
    // size 0x94, major 10, minor 0, build 26200, platform 2, 128 bytes CSD version), TcVersion 3.1.4026, Fingerprint.
    private static readonly string LiveServerInfoResponse =
        "036614710000000001000080" + "A9FE7B670101" + "1027" + "04000000" +
        "0500" + "0600" + "4A616E504300" +
        "0400" + "9400" + "94000000" + "0A000000" + "00000000" + "58660000" + "02000000" + new string('0', 256) +
        "0300" + "0400" + "0301BA0F" +
        "1200" + "4100" + "6137356435643936393765613930316334626234656631396434346334306138326261313233616636323432353364396336616666636539323138646638353900";

    [Fact]
    public void Builds_server_info_request()
    {
        var bytes = AdsDiscoveryProtocol.BuildServerInfoRequest(invokeId: 0);
        Assert.Equal("03661471" + "00000000" + "01000000" + "000000000000" + "0000" + "00000000", Convert.ToHexString(bytes));
    }

    [Fact]
    public void Server_info_request_is_understood_by_beckhoffs_marshaler()
    {
        var bytes = AdsDiscoveryProtocol.BuildServerInfoRequest(invokeId: 7);
        UdpDiscoveryMarshaler.Unmarshal(bytes, out var info);
        Assert.Equal(UdpDiscoveryServiceID.ServerInfo, info.ServiceId);
        Assert.Equal(7, info.InvokeId);
        Assert.False(info.Response);
    }

    [Fact]
    public void Parses_live_server_info_response()
    {
        var r = AdsDiscoveryProtocol.ParseResponse(Convert.FromHexString(LiveServerInfoResponse));

        Assert.NotNull(r);
        Assert.True(r!.IsResponse);
        Assert.Equal(1u, r.ServiceId);
        Assert.Equal("169.254.123.103.1.1", r.NetId);
        Assert.Equal(10000, r.Port);
        Assert.Equal("JanPC", r.ComputerName);
        Assert.Equal("3.1.4026", r.TwinCatVersion);
        Assert.Equal("10.0.26200", r.OsVersion);
        Assert.StartsWith("a75d5d96", r.Fingerprint);
    }

    [Fact]
    public void Garbage_is_not_a_response()
    {
        Assert.Null(AdsDiscoveryProtocol.ParseResponse(new byte[] { 1, 2, 3 }));
        Assert.Null(AdsDiscoveryProtocol.ParseResponse(new byte[24]));
    }

    [Fact]
    public void Builds_add_route_request_like_beckhoff_adslib()
    {
        var bytes = AdsDiscoveryProtocol.BuildAddRouteRequest(
            routeName: "R", localNetId: AmsNetId.Parse("192.168.0.1.1.1"), localAddress: "192.168.0.1",
            userName: "Administrator", password: "1", invokeId: 0);

        var expected =
            "03661471" + "00000000" + "06000000" + // cookie, invoke id, service AddRoute
            "C0A800010101" + "0000" + // AmsAddr { destNetId, port 0 }
            "05000000" + // tag count
            "0C00" + "0200" + "5200" + // ROUTENAME "R"
            "0700" + "0600" + "C0A800010101" + // NETID
            "0D00" + "0E00" + "41646D696E6973747261746F7200" + // USERNAME
            "0200" + "0200" + "3100" + // PASSWORD
            "0500" + "0C00" + "3139322E3136382E302E3100"; // COMPUTERNAME = address
        Assert.Equal(expected, Convert.ToHexString(bytes));

        UdpDiscoveryMarshaler.Unmarshal(bytes, out var info);
        Assert.Equal(UdpDiscoveryServiceID.AddRoute, info.ServiceId);
        Assert.Equal(5, info.Tags.Count);
    }

    [Fact]
    public void Parses_add_route_result_tag()
    {
        // response: cookie, invoke, 0x80000006, AmsAddr, 1 tag: Result(1) len 4 value 0x704 (DeviceNotFound)
        var hex = "03661471" + "00000000" + "06000080" + "0A0000020101" + "1027" + "01000000" + "0100" + "0400" + "04070000";
        var r = AdsDiscoveryProtocol.ParseResponse(Convert.FromHexString(hex));
        Assert.Equal(0x704u, r!.Result);
        Assert.Equal(6u, r.ServiceId);
    }
}
