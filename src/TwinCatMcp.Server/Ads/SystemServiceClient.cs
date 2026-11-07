using System.Buffers.Binary;
using System.Text;
using TwinCAT.Ads;
using TwinCatMcp.Core;

namespace TwinCatMcp.Ads;

/// <summary>ADS requests against the TwinCAT system service (port 10000): files, routes, target info.</summary>
public sealed class SystemServiceClient(AdsClient client)
{
    private const int ReadChunk = 64 * 1024;
    private static readonly Encoding PathEncoding = Encoding.Latin1;

    public AdsClient Client => client;

    private ReadOnlyMemory<byte> ReadWrite(uint ig, uint io, int readLength, ReadOnlyMemory<byte> write, string what)
    {
        var r = client.ReadWriteAsResult(ig, io, readLength, write);
        if (r.ErrorCode != AdsErrorCode.NoError)
        {
            throw AdsSessionManager.ToTcException(r.ErrorCode, what);
        }

        return r.Data;
    }

    public byte[]? TryRead(uint ig, uint io, int length)
    {
        var r = client.ReadAsResult(ig, io, length);
        return r.ErrorCode == AdsErrorCode.NoError ? r.Data.ToArray() : null;
    }

    public string? TryReadString(uint ig, uint io, int length = 1024) =>
        TryRead(ig, io, length) is { } d ? SystemServiceProtocol.ReadZ(d, 0, d.Length) : null;

    // ------------------------------------------------------------------ files

    public uint OpenFile(string path, string root, FileOpenMode mode)
    {
        var flags = SystemServiceProtocol.FileOpenFlags(root, mode);
        var r = client.ReadWriteAsResult(SystemServiceProtocol.FOpen, flags, 4, PathEncoding.GetBytes(path));
        if (r.ErrorCode == AdsErrorCode.DeviceNotFound)
        {
            throw new TcException($"Opening '{path}' (root '{root}') failed: file or directory not found or not accessible on the target.")
            {
                Hint = "Paths are resolved on the TARGET by the TwinCAT system service, which may run under a service account " +
                       "(e.g. Usermode Runtime) without access to user profile folders. Use e.g. C:\\ProgramData\\..., or root 'boot'; check with ads_list_dir.",
            };
        }

        if (r.ErrorCode != AdsErrorCode.NoError)
        {
            throw AdsSessionManager.ToTcException(r.ErrorCode, $"Opening '{path}'");
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(r.Data.Span);
    }

    public void CloseFile(uint handle)
    {
        client.ReadWriteAsResult(SystemServiceProtocol.FClose, handle, 0, ReadOnlyMemory<byte>.Empty);
    }

    public (byte[] Data, bool Truncated) ReadFile(string path, string root, int maxBytes)
    {
        var handle = OpenFile(path, root, FileOpenMode.Read);
        try
        {
            using var ms = new MemoryStream();
            while (ms.Length < maxBytes)
            {
                var want = (int)Math.Min(ReadChunk, maxBytes - ms.Length);
                var chunk = ReadWrite(SystemServiceProtocol.FRead, handle, want, ReadOnlyMemory<byte>.Empty, $"Reading '{path}'");
                if (chunk.Length == 0)
                {
                    return (ms.ToArray(), false);
                }

                ms.Write(chunk.Span);
            }

            // probe whether more data exists
            var more = ReadWrite(SystemServiceProtocol.FRead, handle, 1, ReadOnlyMemory<byte>.Empty, $"Reading '{path}'");
            return (ms.ToArray(), more.Length > 0);
        }
        finally
        {
            CloseFile(handle);
        }
    }

    public void WriteFile(string path, string root, ReadOnlyMemory<byte> content, bool append)
    {
        var handle = OpenFile(path, root, append ? FileOpenMode.Append : FileOpenMode.Write);
        try
        {
            for (var pos = 0; pos < content.Length; pos += ReadChunk)
            {
                var part = content.Slice(pos, Math.Min(ReadChunk, content.Length - pos));
                ReadWrite(SystemServiceProtocol.FWrite, handle, 0, part, $"Writing '{path}'");
            }
        }
        finally
        {
            CloseFile(handle);
        }
    }

    public void DeleteFile(string path, string root)
    {
        // AdsTool: FOPEN::READ | FOPEN::ENABLE_DIR (bit 7) | openPath << 16
        var flags = 0x1u | 0x80u | (SystemServiceProtocol.OpenPathOf(root) << 16);
        ReadWrite(SystemServiceProtocol.FDelete, flags, 0, PathEncoding.GetBytes(path), $"Deleting '{path}'");
    }

    /// <summary>Removes an empty directory (SYSTEMSERVICE_RMDIR, index offset = openPath &lt;&lt; 16).</summary>
    public void RemoveDirectory(string path, string root)
    {
        ReadWrite(SystemServiceProtocol.RmDir, SystemServiceProtocol.OpenPathOf(root) << 16, 0, PathEncoding.GetBytes(path), $"Removing directory '{path}'");
    }

    /// <summary>Lists a directory with SYSTEMSERVICE_FFILEFIND (first call: openPath + "dir\*", then handle until 'not found').</summary>
    public (List<FileFindEntry> Entries, bool Truncated) ListDirectory(string path, string root, int maxEntries)
    {
        var sep = path.Contains('/') && !path.Contains('\\') ? '/' : '\\';
        var pattern = string.IsNullOrEmpty(path) ? "*" : path.TrimEnd('\\', '/') + sep + "*";
        var entries = new List<FileFindEntry>();
        var truncated = false;
        var r = client.ReadWriteAsResult(SystemServiceProtocol.FFileFind, SystemServiceProtocol.OpenPathOf(root), SystemServiceProtocol.FileFindDataSize, PathEncoding.GetBytes(pattern));
        if (r.ErrorCode == AdsErrorCode.DeviceNotFound)
        {
            throw new TcException($"Directory '{path}' not found on the target (root '{root}').")
            {
                Hint = "Use an absolute path with root 'generic' (e.g. C:\\TwinCAT\\3.1\\Boot) or a path relative to root 'boot' / 'target'.",
            };
        }

        var guard = 0;
        while (r.ErrorCode == AdsErrorCode.NoError && guard++ < 100_000)
        {
            var e = SystemServiceProtocol.ParseFileFind(r.Data.Span);
            if (e.Name is not ("." or ".."))
            {
                if (entries.Count < maxEntries)
                {
                    entries.Add(e);
                }
                else
                {
                    truncated = true;
                }
            }

            r = client.ReadWriteAsResult(SystemServiceProtocol.FFileFind, e.Handle, SystemServiceProtocol.FileFindDataSize, ReadOnlyMemory<byte>.Empty);
        }

        if (r.ErrorCode is not (AdsErrorCode.NoError or AdsErrorCode.DeviceNotFound or AdsErrorCode.DeviceNoMoreHandles))
        {
            throw AdsSessionManager.ToTcException(r.ErrorCode, $"Listing '{path}'");
        }

        return (entries, truncated);
    }

    // ------------------------------------------------------------------ routes

    public List<AdsRouteEntry> EnumRoutes()
    {
        var list = new List<AdsRouteEntry>();
        for (uint i = 0; i < 4096; i++)
        {
            var r = client.ReadAsResult(SystemServiceProtocol.EnumRemote, i, 4096);
            if (r.ErrorCode is AdsErrorCode.DeviceNoMoreHandles or AdsErrorCode.DeviceNotFound)
            {
                break;
            }

            if (r.ErrorCode != AdsErrorCode.NoError)
            {
                throw AdsSessionManager.ToTcException(r.ErrorCode, "Enumerating routes");
            }

            list.Add(SystemServiceProtocol.ParseRouteEntry(r.Data.Span));
        }

        return list;
    }

    public void AddRoute(string name, AmsNetId netId, string address, bool temporary)
    {
        var r = client.WriteAsResult(SystemServiceProtocol.AddRemote, 0, SystemServiceProtocol.BuildAddRoute(name, netId, address, temporary));
        if (r.ErrorCode != AdsErrorCode.NoError)
        {
            throw AdsSessionManager.ToTcException(r.ErrorCode, $"Adding route '{name}'");
        }
    }

    public void RemoveRoute(string name)
    {
        var r = client.WriteAsResult(SystemServiceProtocol.DelRemote, 0, SystemServiceProtocol.BuildDeleteRoute(name));
        if (r.ErrorCode != AdsErrorCode.NoError)
        {
            throw AdsSessionManager.ToTcException(r.ErrorCode, $"Removing route '{name}'");
        }
    }
}
