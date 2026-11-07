using System.Buffers.Binary;
using System.Text;
using TwinCAT.Ads;
using TwinCatMcp.Core;

namespace TwinCatMcp.Ads;

public enum FileOpenMode
{
    Read,
    Write,
    Append,
}

public sealed record SysServState(
    AdsState AdsState,
    int DeviceState,
    int RestartIndex,
    string Version,
    string Platform,
    string OsType,
    int Flags,
    int RestartCounter,
    bool ConfigModeOnly,
    bool RouterModeOnly);

public sealed record AdsRouteEntry(
    string Name,
    string NetId,
    string Address,
    string Transport,
    uint Flags,
    uint TimeoutMs,
    bool Temporary,
    bool Disabled);

public sealed record FileFindEntry(uint Handle, string Name, uint Attributes, bool IsDirectory, long Size, DateTime? CreatedUtc, DateTime? ModifiedUtc);

/// <summary>
/// Payload layouts of the TwinCAT system service (AMS port 10000) as declared in the TwinCAT SDK header
/// SystemServices.h (TwinCAT 3.1.4026). Pure build/parse logic.
/// </summary>
public static class SystemServiceProtocol
{
    // Index groups (enum nSystemServiceIndexGroups)
    public const uint FOpen = 120;
    public const uint FClose = 121;
    public const uint FRead = 122;
    public const uint FWrite = 123;
    public const uint FDelete = 131;
    public const uint FFileFind = 133;
    public const uint RmDir = 139;
    public const uint SysServStateGroup = 240;
    public const uint TargetInfo = 700;
    public const uint IpHostName = 702;
    public const uint AddRemote = 801;
    public const uint DelRemote = 802;
    public const uint EnumRemote = 803;

    // nTargetInfoOffsets
    public const uint TargetInfoQueryXml = 1;
    public const uint TargetInfoTargetType = 2;
    public const uint TargetInfoPlatform = 4;
    public const uint TargetInfoProjectName = 7;
    public const uint TargetInfoIsUmRuntime = 10;

    /// <summary>sizeof(handle) + sizeof(WIN32_FIND_DATAA) incl. padding as answered by the system service.</summary>
    public const int FileFindDataSize = 324;

    private const uint AddRemoteFlagTemporary = 0x1;

    // nSystemServiceOpenFile
    private static readonly Dictionary<string, uint> OpenPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["generic"] = 1,
        ["bootprj"] = 2,
        ["bootdata"] = 3,
        ["boot"] = 4,
        ["target"] = 5,
        ["config"] = 6,
        ["install"] = 7,
        ["repository"] = 8,
        ["license"] = 9,
        ["data"] = 10,
    };

    public static IReadOnlyCollection<string> PathRoots => OpenPaths.Keys;

    public static uint OpenPathOf(string root) =>
        OpenPaths.TryGetValue(root.Trim(), out var p)
            ? p
            : throw new TcException($"Unknown path root '{root}'.")
            {
                Hint = "Valid roots: " + string.Join(", ", OpenPaths.Keys) +
                       ". 'generic' = absolute path on the target, 'boot' = TwinCAT boot folder, 'target' = TwinCAT target folder.",
            };

    /// <summary>Index offset for SYSTEMSERVICE_FOPEN (TFOPEN_INDEXOFFS).</summary>
    public static uint FileOpenFlags(string root, FileOpenMode mode)
    {
        const uint read = 1 << 0, write = 1 << 1, append = 1 << 2, plus = 1 << 3, binary = 1 << 4, ensureDir = 1 << 6;
        var flags = mode switch
        {
            FileOpenMode.Read => read | binary,
            FileOpenMode.Write => write | plus | binary | ensureDir,
            _ => append | plus | binary | ensureDir,
        };
        return flags | (OpenPathOf(root) << 16);
    }

    public static SysServState ParseSysServState(ReadOnlySpan<byte> d)
    {
        if (d.Length < 16)
        {
            throw new TcException($"SYSSERV_STATE answer too short ({d.Length} bytes).");
        }

        var flags = BinaryPrimitives.ReadUInt16LittleEndian(d[12..]);
        return new SysServState(
            (AdsState)BinaryPrimitives.ReadUInt16LittleEndian(d),
            BinaryPrimitives.ReadUInt16LittleEndian(d[2..]),
            BinaryPrimitives.ReadUInt16LittleEndian(d[4..]),
            $"{d[6]}.{d[7]}.{BinaryPrimitives.ReadUInt16LittleEndian(d[8..])}",
            PlatformName(d[10]),
            OsTypeName(d[11]),
            flags,
            (flags >> 8) & 0xF,
            (flags & 0x1) != 0,
            (flags & 0x2) != 0);
    }

    public static string PlatformName(byte platform) => platform switch
    {
        0 => "x86",
        1 => "x64",
        2 => "ARM",
        3 => "ARM-Thumb2",
        4 => "ARM64",
        5 => "x64-E",
        _ => $"unknown({platform})",
    };

    public static string OsTypeName(byte osType) => osType switch
    {
        0 => "Windows",
        1 => "Windows CE",
        2 => "UM",
        3 => "TC",
        _ => $"unknown({osType})",
    };

    public static string TransportName(ushort t) => t switch
    {
        0 => "None",
        1 => "TCP_IP",
        _ => $"Type{t}",
    };

    /// <summary>TAdsSysAddRemote (pack 1): netId[6], nTransType u16, dwFlags, nTimeout, nMaxFragment, reserved[3], cbAddress, cbName, cbAddNetIds, address\0, name\0.</summary>
    public static byte[] BuildAddRoute(string name, AmsNetId netId, string address, bool temporary)
    {
        var addr = Encoding.ASCII.GetBytes(address + "\0");
        var nm = Encoding.ASCII.GetBytes(name + "\0");
        var buf = new byte[44 + addr.Length + nm.Length];
        netId.ToBytes().CopyTo(buf, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(6), 1); // TCP/IP
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(8), temporary ? AddRemoteFlagTemporary : 0);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(32), (uint)addr.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(36), (uint)nm.Length);
        addr.CopyTo(buf, 44);
        nm.CopyTo(buf, 44 + addr.Length);
        return buf;
    }

    public static byte[] BuildDeleteRoute(string name) => Encoding.ASCII.GetBytes(name + "\0");

    /// <summary>Parses one SYSTEMSERVICE_ENUMREMOTE answer (same layout as TAdsSysAddRemote).</summary>
    public static AdsRouteEntry ParseRouteEntry(ReadOnlySpan<byte> d)
    {
        if (d.Length < 44)
        {
            throw new TcException($"Route entry too short ({d.Length} bytes).");
        }

        var netId = new AmsNetId(d[..6].ToArray()).ToString();
        var trans = BinaryPrimitives.ReadUInt16LittleEndian(d[6..]);
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(d[8..]);
        var timeout = BinaryPrimitives.ReadUInt32LittleEndian(d[12..]);
        var cbAddress = (int)BinaryPrimitives.ReadUInt32LittleEndian(d[32..]);
        var cbName = (int)BinaryPrimitives.ReadUInt32LittleEndian(d[36..]);
        var address = ReadZ(d, 44, cbAddress);
        var name = ReadZ(d, 44 + cbAddress, cbName);
        return new AdsRouteEntry(name, netId, address, TransportName(trans), flags, timeout, (flags & AddRemoteFlagTemporary) != 0, false);
    }

    public static FileFindEntry ParseFileFind(ReadOnlySpan<byte> d)
    {
        if (d.Length < 48 + 260)
        {
            throw new TcException($"File find answer too short ({d.Length} bytes).");
        }

        var attributes = BinaryPrimitives.ReadUInt32LittleEndian(d[4..]);
        var size = ((long)BinaryPrimitives.ReadUInt32LittleEndian(d[32..]) << 32) | BinaryPrimitives.ReadUInt32LittleEndian(d[36..]);
        return new FileFindEntry(
            BinaryPrimitives.ReadUInt32LittleEndian(d),
            ReadZ(d, 48, 260),
            attributes,
            (attributes & 0x10) != 0,
            size,
            FileTime(BinaryPrimitives.ReadInt64LittleEndian(d[8..])),
            FileTime(BinaryPrimitives.ReadInt64LittleEndian(d[24..])));
    }

    private static DateTime? FileTime(long ft) => ft <= 0 ? null : DateTime.FromFileTimeUtc(ft);

    public static string ReadZ(ReadOnlySpan<byte> d, int offset, int maxLength)
    {
        if (offset >= d.Length)
        {
            return string.Empty;
        }

        var span = d.Slice(offset, Math.Min(maxLength, d.Length - offset));
        var end = span.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end < 0 ? span : span[..end]);
    }
}
