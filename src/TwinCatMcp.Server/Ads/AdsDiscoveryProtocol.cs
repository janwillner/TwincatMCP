using System.Buffers.Binary;
using System.Text;
using TwinCAT.Ads;

namespace TwinCatMcp.Ads;

public sealed record DiscoveryResponse(
    uint ServiceId,
    bool IsResponse,
    string NetId,
    int Port,
    string? ComputerName,
    string? TwinCatVersion,
    string? OsVersion,
    string? Fingerprint,
    uint? Result);

/// <summary>
/// TwinCAT UDP discovery / route protocol (UDP port 48899), as implemented in Beckhoff's ADS library
/// (github.com/Beckhoff/ADS, AdsLib.cpp: AddRemoteRoute / GetRemoteAddress) and TwinCAT.Ads.Native.UdpDiscovery*.
/// Frame: cookie u32, invokeId u32, serviceId u32 (0x80000000 = response), AmsAddr (netId[6], port u16),
/// tagCount u32, tags (id u16, length u16, data).
/// </summary>
public static class AdsDiscoveryProtocol
{
    public const int Port = 48899;
    public const uint Cookie = 0x71146603;
    public const uint ServiceServerInfo = 1;
    public const uint ServiceAddRoute = 6;
    public const uint ResponseFlag = 0x80000000;

    private const ushort TagResult = 1;
    private const ushort TagPassword = 2;
    private const ushort TagTcVersion = 3;
    private const ushort TagTargetOs = 4;
    private const ushort TagComputerName = 5;
    private const ushort TagNetId = 7;
    private const ushort TagRouteName = 12;
    private const ushort TagUserName = 13;
    private const ushort TagFingerprint = 18;

    public static byte[] BuildServerInfoRequest(uint invokeId) =>
        Build(invokeId, ServiceServerInfo, new byte[6], []);

    /// <summary>
    /// Asks a remote TwinCAT system to add a route back to <paramref name="localNetId"/> / <paramref name="localAddress"/>
    /// (tags in the order Beckhoff's AdsLib sends them: route name, net id, user name, password, computer name/address).
    /// </summary>
    public static byte[] BuildAddRouteRequest(string routeName, AmsNetId localNetId, string localAddress, string userName, string password, uint invokeId)
    {
        var netId = localNetId.ToBytes();
        var tags = new List<(ushort, byte[])>
        {
            (TagRouteName, Z(string.IsNullOrEmpty(routeName) ? localAddress : routeName)),
            (TagNetId, netId),
            (TagUserName, Z(userName)),
            (TagPassword, Z(password)),
            (TagComputerName, Z(localAddress)),
        };
        return Build(invokeId, ServiceAddRoute, netId, tags);
    }

    private static byte[] Z(string s) => Encoding.ASCII.GetBytes(s + "\0");

    private static byte[] Build(uint invokeId, uint service, byte[] netId, List<(ushort Id, byte[] Data)> tags)
    {
        var size = 12 + 8 + 4 + tags.Sum(t => 4 + t.Data.Length);
        var buf = new byte[size];
        var s = buf.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(s, Cookie);
        BinaryPrimitives.WriteUInt32LittleEndian(s[4..], invokeId);
        BinaryPrimitives.WriteUInt32LittleEndian(s[8..], service);
        netId.CopyTo(s[12..]);
        BinaryPrimitives.WriteUInt32LittleEndian(s[20..], (uint)tags.Count);
        var pos = 24;
        foreach (var (id, data) in tags)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(s[pos..], id);
            BinaryPrimitives.WriteUInt16LittleEndian(s[(pos + 2)..], (ushort)data.Length);
            data.CopyTo(s[(pos + 4)..]);
            pos += 4 + data.Length;
        }

        return buf;
    }

    /// <summary>Parses a discovery frame; returns null for anything that is not a well-formed discovery frame.</summary>
    public static DiscoveryResponse? ParseResponse(ReadOnlySpan<byte> d)
    {
        if (d.Length < 24 || BinaryPrimitives.ReadUInt32LittleEndian(d) != Cookie)
        {
            return null;
        }

        var service = BinaryPrimitives.ReadUInt32LittleEndian(d[8..]);
        var netId = new AmsNetId(d.Slice(12, 6).ToArray()).ToString();
        var port = BinaryPrimitives.ReadUInt16LittleEndian(d[18..]);
        var count = BinaryPrimitives.ReadUInt32LittleEndian(d[20..]);
        string? computer = null, version = null, os = null, fingerprint = null;
        uint? result = null;
        var pos = 24;
        for (var i = 0; i < count && pos + 4 <= d.Length; i++)
        {
            var id = BinaryPrimitives.ReadUInt16LittleEndian(d[pos..]);
            var len = BinaryPrimitives.ReadUInt16LittleEndian(d[(pos + 2)..]);
            pos += 4;
            if (pos + len > d.Length)
            {
                break;
            }

            var data = d.Slice(pos, len);
            switch (id)
            {
                case TagComputerName:
                    computer = SystemServiceProtocol.ReadZ(data, 0, len);
                    break;
                case TagTcVersion when len >= 4:
                    version = $"{data[0]}.{data[1]}.{BinaryPrimitives.ReadUInt16LittleEndian(data[2..])}";
                    break;
                case TagTargetOs when len >= 16:
                    // OSVERSIONINFO: dwOSVersionInfoSize, dwMajorVersion, dwMinorVersion, dwBuildNumber, ...
                    os = $"{BinaryPrimitives.ReadUInt32LittleEndian(data[4..])}.{BinaryPrimitives.ReadUInt32LittleEndian(data[8..])}.{BinaryPrimitives.ReadUInt32LittleEndian(data[12..])}";
                    break;
                case TagFingerprint:
                    fingerprint = SystemServiceProtocol.ReadZ(data, 0, len);
                    break;
                case TagResult when len >= 4:
                    result = BinaryPrimitives.ReadUInt32LittleEndian(data);
                    break;
            }

            pos += len;
        }

        return new DiscoveryResponse(service & ~ResponseFlag, (service & ResponseFlag) != 0, netId, port, computer, version, os, fingerprint, result);
    }
}
