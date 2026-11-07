using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TwinCatMcp.Core;

namespace TwinCatMcp.Io;

/// <summary>
/// Process-wide cache of the parsed ESI catalog (~1 GB of XML on a full TwinCAT install).
/// Parsed once (in parallel), then kept in memory and persisted to %LOCALAPPDATA%\TwinCatMcp\esi-catalog.json;
/// the disk cache is reused as long as the set of ESI files (path, size, timestamp) is unchanged.
/// </summary>
public static class EsiCatalogStore
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static Snapshot? _current;

    public sealed record Snapshot(IReadOnlyList<EsiDevice> Devices, IReadOnlyList<string> Directories, int FileCount, DateTime LoadedAtUtc, bool FromDiskCache, TimeSpan LoadTime);

    private sealed record DiskCache(string Signature, List<EsiDevice> Devices);

    /// <summary>
    /// ESI directories: env TWINCAT_MCP_ESI_DIRS (';'-separated) or the TwinCAT defaults
    /// (%TWINCAT3DIR%Config\Io\EtherCAT and C:\TwinCAT\3.1\Config\Io\EtherCAT).
    /// </summary>
    public static IReadOnlyList<string> DefaultDirectories()
    {
        var env = Environment.GetEnvironmentVariable("TWINCAT_MCP_ESI_DIRS");
        IEnumerable<string> candidates = !string.IsNullOrWhiteSpace(env)
            ? env.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [Path.Combine(XaeSession.TwinCatDir, @"Config\Io\EtherCAT"), @"C:\TwinCAT\3.1\Config\Io\EtherCAT"];
        return candidates
            .Select(Path.GetFullPath)
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static async Task<Snapshot> GetAsync(bool refresh = false, CancellationToken ct = default)
    {
        if (!refresh && _current is { } cached)
        {
            return cached;
        }

        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!refresh && _current is { } again)
            {
                return again;
            }

            _current = await Task.Run(() => Load(DefaultDirectories(), useDiskCache: !refresh), ct).ConfigureAwait(false);
            return _current;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static string CacheFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TwinCatMcp", "esi-catalog.json");

    public static Snapshot Load(IReadOnlyList<string> directories, bool useDiskCache)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        if (directories.Count == 0)
        {
            throw new TcException("No EtherCAT ESI directory found.")
            {
                Hint = @"Expected %TWINCAT3DIR%Config\Io\EtherCAT or C:\TwinCAT\3.1\Config\Io\EtherCAT. Set TWINCAT_MCP_ESI_DIRS to override.",
            };
        }

        var files = directories
            .SelectMany(d => Directory.EnumerateFiles(d, "*.xml", SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var signature = Signature(files);

        if (useDiskCache && TryReadDiskCache(signature) is { } fromDisk)
        {
            return new Snapshot(fromDisk, directories, files.Count, DateTime.UtcNow, true, sw.Elapsed);
        }

        var bag = new ConcurrentBag<(int Index, IReadOnlyList<EsiDevice> Devices)>();
        Parallel.ForEach(files.Select((f, i) => (f, i)), new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, item =>
        {
            try
            {
                using var stream = new FileStream(item.f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, FileOptions.SequentialScan);
                bag.Add((item.i, EsiParser.Parse(stream, Path.GetFileName(item.f))));
            }
            catch (IOException)
            {
                // unreadable file - skip
            }
            catch (UnauthorizedAccessException)
            {
                // skip
            }
        });

        var devices = bag.OrderBy(b => b.Index).SelectMany(b => b.Devices).ToList();
        TryWriteDiskCache(signature, devices);
        return new Snapshot(devices, directories, files.Count, DateTime.UtcNow, false, sw.Elapsed);
    }

    private static string Signature(IEnumerable<string> files)
    {
        var sb = new StringBuilder("v1|");
        foreach (var f in files)
        {
            var fi = new FileInfo(f);
            sb.Append(f).Append('|').Append(fi.Length).Append('|').Append(fi.LastWriteTimeUtc.Ticks).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private static List<EsiDevice>? TryReadDiskCache(string signature)
    {
        try
        {
            if (!File.Exists(CacheFile))
            {
                return null;
            }

            using var stream = File.OpenRead(CacheFile);
            var cache = JsonSerializer.Deserialize<DiskCache>(stream);
            return cache is not null && cache.Signature == signature ? cache.Devices : null;
        }
        catch
        {
            return null;
        }
    }

    private static void TryWriteDiskCache(string signature, List<EsiDevice> devices)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CacheFile)!);
            var tmp = CacheFile + ".tmp";
            using (var stream = File.Create(tmp))
            {
                JsonSerializer.Serialize(stream, new DiskCache(signature, devices));
            }

            File.Move(tmp, CacheFile, true);
        }
        catch
        {
            // cache is optional
        }
    }
}
