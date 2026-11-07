using System.ComponentModel;
using System.Text;
using System.Xml.Linq;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TwinCAT.Ads;
using TwinCAT.Ads.TypeSystem;
using TwinCatMcp.Ads;
using TwinCatMcp.Core;

namespace TwinCatMcp.Tools;

/// <summary>ADS connections, device state, raw index group/offset access, target/system info and file access.</summary>
[McpServerToolType]
public sealed class AdsTools(AdsSessionManager ads)
{
    private const string NetIdDesc = "AMS Net ID of the target, e.g. 5.12.34.56.1.1, or 'local' (default) for this PC.";
    private const string PortDesc = "AMS port: 851 = PLC runtime 1 (default), 852 = PLC 2, 10000 = system service, 500 = NC, 300 = I/O.";
    private const string SysNetIdDesc = "AMS Net ID of the target system ('local' = this PC). Uses its system service (port 10000).";

    private static object Target(AdsClient c) => new { netId = c.Address?.NetId.ToString(), port = c.Address?.Port };

    private static object StateOf(StateInfo s) => new
    {
        adsState = AdsStateText.Name(s.AdsState),
        adsStateValue = (int)s.AdsState,
        deviceState = s.DeviceState,
        description = AdsStateText.Describe(s.AdsState),
    };

    private static string VersionOf(AdsVersion v) => $"{v.Version}.{v.Revision}.{v.Build}";

    [McpServerTool(Name = "ads_connect"), Description(
        "Opens (or reuses) an ADS connection to netId:port and checks it by reading device info and ADS state. " +
        "Connections are also opened implicitly by all ads_ tools; use this to verify a target is reachable. " +
        "Example: {netId:'local', port:851} for PLC 1 on this PC, {netId:'5.12.34.56.1.1', port:10000} for a remote system service.")]
    public Task<CallToolResult> Connect(
        [Description(NetIdDesc)] string? netId = null,
        [Description(PortDesc)] int port = AdsSessionManager.DefaultPlcPort,
        [Description("ADS timeout in ms for this connection.")] int timeoutMs = 5000)
        => ads.Run(() =>
        {
            var c = ads.Get(netId, port, timeoutMs);
            var err = c.TryReadState(out var state);
            if (err != AdsErrorCode.NoError)
            {
                ads.Disconnect(netId, port);
                throw AdsSessionManager.ToTcException(err, $"Connecting to {netId ?? "local"}:{port}");
            }

            var info = c.ReadDeviceInfo();
            return new
            {
                target = Target(c),
                deviceInfo = new { name = info.Name, version = VersionOf(info.Version) },
                state = StateOf(state),
            };
        });

    [McpServerTool(Name = "ads_disconnect", Idempotent = true), Description("Closes the cached ADS connection (and symbol table) to netId:port.")]
    public Task<CallToolResult> Disconnect(
        [Description(NetIdDesc)] string? netId = null,
        [Description(PortDesc)] int port = AdsSessionManager.DefaultPlcPort)
        => ads.Run(() => new { disconnected = ads.Disconnect(netId, port) });

    [McpServerTool(Name = "ads_list_connections", ReadOnly = true, Idempotent = true), Description("Lists the ADS connections cached by this server.")]
    public Task<CallToolResult> ListConnections()
        => ads.Run(() => new
        {
            connections = ads.Clients.Select(kv => new
            {
                key = kv.Key,
                netId = kv.Value.Address?.NetId.ToString(),
                port = kv.Value.Address?.Port,
                connected = kv.Value.IsConnected,
            }).ToList(),
        });

    [McpServerTool(Name = "ads_read_state", ReadOnly = true, Idempotent = true), Description(
        "Reads the ADS state (Run, Stop, Config, Error, ...) and device state of an ADS device. " +
        "Port 851 = PLC 1 (Run/Stop), port 10000 = TwinCAT system (Run = run mode, Config = config mode).")]
    public Task<CallToolResult> ReadState(
        [Description(NetIdDesc)] string? netId = null,
        [Description(PortDesc)] int port = AdsSessionManager.DefaultPlcPort)
        => ads.Run(() =>
        {
            var c = ads.Get(netId, port);
            var err = c.TryReadState(out var s);
            if (err != AdsErrorCode.NoError)
            {
                throw AdsSessionManager.ToTcException(err, $"Reading the state of {netId ?? "local"}:{port}");
            }

            return StateOf(s);
        });

    [McpServerTool(Name = "ads_read_device_info", ReadOnly = true, Idempotent = true), Description(
        "Reads name and version of an ADS device (e.g. 'TwinCAT System' 3.1.4026 on port 10000, 'Plc30 App' on 851).")]
    public Task<CallToolResult> ReadDeviceInfo(
        [Description(NetIdDesc)] string? netId = null,
        [Description(PortDesc)] int port = AdsSessionManager.DefaultPlcPort)
        => ads.Run(() =>
        {
            var c = ads.Get(netId, port);
            var info = c.ReadDeviceInfo();
            return new { target = Target(c), name = info.Name, version = VersionOf(info.Version) };
        });

    [McpServerTool(Name = "ads_write_control", Destructive = true), Description(
        "Sets the ADS state of a device (ADS WriteControl). PLC port (851..): 'run' starts, 'stop' stops, 'reset' resets the PLC application. " +
        "WARNING: on port 10000 (system service) this changes the WHOLE TwinCAT system of that PC: 'run'/'restart' restarts TwinCAT in run mode, " +
        "'config' switches to config mode - all PLCs, NC and I/O stop. This requires confirmSystemService=true. " +
        "Numeric ADS states are accepted too (5 = Run, 6 = Stop, 2 = Reset, 16 = Reconfig).")]
    public Task<CallToolResult> WriteControl(
        [Description("Requested state: run | stop | reset | config | restart (system service) | numeric ADS state.")] string state,
        [Description(NetIdDesc)] string? netId = null,
        [Description(PortDesc)] int port = AdsSessionManager.DefaultPlcPort,
        [Description("Device state to send along (normally 0).")] int deviceState = 0,
        [Description("Must be true to change the state of the system service (port 10000) - restarts/stops the whole TwinCAT runtime.")] bool confirmSystemService = false,
        [Description(AdsLocal.Description)] bool allowLocalSystem = false)
        => ads.Run(() =>
        {
            var isSystem = port == AdsSessionManager.SystemServicePort;
            if (isSystem && !confirmSystemService)
            {
                throw new TcException("Refusing to change the TwinCAT system state (port 10000) without confirmation.")
                {
                    Hint = "This restarts or stops the whole TwinCAT runtime of the target PC. Pass confirmSystemService=true if that is intended.",
                };
            }

            LocalTargetGuard.Ensure(netId, allowLocalSystem, "change the ADS state", port);
            var requested = AdsStateText.ParseCommand(state, isSystem);
            var c = ads.Get(netId, port);
            c.TryReadState(out var before);
            var err = c.TryWriteControl(new StateInfo(requested, (ushort)deviceState));
            if (err != AdsErrorCode.NoError && !(isSystem && err == AdsErrorCode.ClientSyncTimeOut))
            {
                throw AdsSessionManager.ToTcException(err, $"WriteControl({requested})");
            }

            Thread.Sleep(isSystem ? 1000 : 100);
            var afterErr = c.TryReadState(out var after);
            return new
            {
                requested = AdsStateText.Name(requested),
                before = StateOf(before),
                after = afterErr == AdsErrorCode.NoError ? StateOf(after) : null,
                note = isSystem ? "TwinCAT restarts asynchronously - poll ads_read_state on port 10000 until Run/Config is reached." : null,
            };
        });

    // ------------------------------------------------------------------ raw

    [McpServerTool(Name = "ads_read_raw", ReadOnly = true), Description(
        "Reads raw bytes by index group / index offset (ADS Read, or ADS ReadWrite when writeData is given). " +
        "Numbers accept decimal, 0x.. or 16#.. (e.g. indexGroup '0x4020' = PLC %M area, '0xF020' = %I, '0xF030' = %Q). Returns hex and base64.")]
    public Task<CallToolResult> ReadRaw(
        [Description("Index group, e.g. '0x4020' or '16#F020' or '16416'.")] string indexGroup,
        [Description("Index offset, e.g. '0' or '0x10'.")] string indexOffset,
        [Description("Number of bytes to read (max 1 MB).")] int length,
        [Description("Optional request data for an ADS ReadWrite (hex or base64, see format).")] string? writeData = null,
        [Description("Encoding of writeData: 'hex' (default) or 'base64'.")] string format = "hex",
        [Description(NetIdDesc)] string? netId = null,
        [Description(PortDesc)] int port = AdsSessionManager.DefaultPlcPort)
        => ads.Run(() =>
        {
            var ig = ParseNumber(indexGroup, nameof(indexGroup));
            var io = ParseNumber(indexOffset, nameof(indexOffset));
            if (length < 0 || length > 1024 * 1024)
            {
                throw new TcException("length must be 0 .. 1048576.");
            }

            var c = ads.Get(netId, port);
            var r = writeData is null
                ? c.ReadAsResult(ig, io, length)
                : c.ReadWriteAsResult(ig, io, length, Bytes(writeData, format));
            if (r.ErrorCode != AdsErrorCode.NoError)
            {
                throw AdsSessionManager.ToTcException(r.ErrorCode, $"Read 0x{ig:X}/0x{io:X}");
            }

            var data = r.Data.ToArray();
            return new { indexGroup = $"0x{ig:X}", indexOffset = $"0x{io:X}", length = data.Length, hex = Convert.ToHexString(data), base64 = Convert.ToBase64String(data) };
        });

    [McpServerTool(Name = "ads_write_raw", Destructive = true), Description(
        "Writes raw bytes by index group / index offset (ADS Write). Data as hex ('0A000000') or base64. Little endian, exactly as the target expects. " +
        "Prefer ads_write_symbols for PLC variables.")]
    public Task<CallToolResult> WriteRaw(
        [Description("Index group, e.g. '0x4020'.")] string indexGroup,
        [Description("Index offset.")] string indexOffset,
        [Description("Bytes to write (hex or base64).")] string data,
        [Description("'hex' (default) or 'base64'.")] string format = "hex",
        [Description(NetIdDesc)] string? netId = null,
        [Description(PortDesc)] int port = AdsSessionManager.DefaultPlcPort,
        [Description(AdsLocal.Description)] bool allowLocalSystem = false)
        => ads.Run(() =>
        {
            LocalTargetGuard.Ensure(netId, allowLocalSystem, "write raw ADS data", port);
            var ig = ParseNumber(indexGroup, nameof(indexGroup));
            var io = ParseNumber(indexOffset, nameof(indexOffset));
            var bytes = Bytes(data, format);
            var c = ads.Get(netId, port);
            var r = c.WriteAsResult(ig, io, bytes);
            if (r.ErrorCode != AdsErrorCode.NoError)
            {
                throw AdsSessionManager.ToTcException(r.ErrorCode, $"Write 0x{ig:X}/0x{io:X}");
            }

            return new { indexGroup = $"0x{ig:X}", indexOffset = $"0x{io:X}", written = bytes.Length };
        });

    private static uint ParseNumber(string text, string name) =>
        IecNumber.TryParse(text, out var v) && v <= uint.MaxValue
            ? (uint)v
            : throw new TcException($"Invalid {name} '{text}'.") { Hint = "Use decimal (16416), hex (0x4020) or IEC (16#4020)." };

    private static byte[] Bytes(string data, string format)
    {
        try
        {
            return format.Equals("base64", StringComparison.OrdinalIgnoreCase)
                ? Convert.FromBase64String(data)
                : Convert.FromHexString(data.Replace(" ", "").Replace("-", "").Replace("0x", "", StringComparison.OrdinalIgnoreCase));
        }
        catch (FormatException)
        {
            throw new TcException($"Data is not valid {format}.") { Hint = "hex: '0A0B0C' (spaces allowed), base64: 'CgsM'." };
        }
    }

    // ------------------------------------------------------------------ system

    [McpServerTool(Name = "ads_get_system_info", ReadOnly = true, Idempotent = true), Description(
        "TwinCAT system information of a target via its system service (port 10000): TwinCAT version/build, ADS/system state (run/config), " +
        "platform (x64/ARM...), OS, target type, host name, Net ID, whether it is a Usermode Runtime, name of the active configuration project.")]
    public Task<CallToolResult> GetSystemInfo([Description(SysNetIdDesc)] string? netId = null)
        => ads.Run(() =>
        {
            var c = ads.Get(netId, AdsSessionManager.SystemServicePort);
            var sys = new SystemServiceClient(c);
            var info = c.ReadDeviceInfo();
            var stateRaw = sys.TryRead(SystemServiceProtocol.SysServStateGroup, 0, 16);
            var state = stateRaw is { Length: >= 16 } ? SystemServiceProtocol.ParseSysServState(stateRaw) : null;
            c.TryReadState(out var adsState);
            var xml = sys.TryReadString(SystemServiceProtocol.TargetInfo, SystemServiceProtocol.TargetInfoQueryXml, 16384);
            XElement? desc = null;
            try
            {
                desc = xml is null ? null : XElement.Parse(xml);
            }
            catch (System.Xml.XmlException)
            {
                // ignore malformed info
            }

            string? X(string name) => desc?.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value;
            var isUm = sys.TryRead(SystemServiceProtocol.TargetInfo, SystemServiceProtocol.TargetInfoIsUmRuntime, 4);
            return new
            {
                netId = c.Address?.NetId.ToString(),
                deviceName = info.Name,
                twinCatVersion = state?.Version ?? VersionOf(info.Version),
                systemState = StateOf(adsState),
                mode = adsState.AdsState switch { AdsState.Run => "run", AdsState.Config => "config", _ => AdsStateText.Name(adsState.AdsState).ToLowerInvariant() },
                platform = sys.TryReadString(SystemServiceProtocol.TargetInfo, SystemServiceProtocol.TargetInfoPlatform),
                cpuArchitecture = state?.Platform,
                osType = state?.OsType,
                osName = X("OsName"),
                osVersion = X("OsVersion"),
                targetType = X("TargetType"),
                usermodeRuntime = isUm is { Length: > 0 } ? isUm[0] != 0 : (bool?)null,
                hostName = sys.TryReadString(SystemServiceProtocol.IpHostName, 0),
                activeProject = sys.TryReadString(SystemServiceProtocol.TargetInfo, SystemServiceProtocol.TargetInfoProjectName),
                restartIndex = state?.RestartIndex,
                hardware = desc is null ? null : new { model = X("Model"), serialNo = X("SerialNo"), cpuVersion = X("CPUVersion") },
            };
        });

    [McpServerTool(Name = "ads_list_plc_runtimes", ReadOnly = true), Description(
        "Probes the PLC runtime ports of a target (TwinCAT 3: 851..860, TwinCAT 2: 801/811/821/831 or custom) and lists the ones that answer, " +
        "with ADS state (Run/Stop), device name and the PLC project/application name (TwinCAT_SystemInfoVarList._AppInfo).")]
    public Task<CallToolResult> ListPlcRuntimes(
        [Description(NetIdDesc)] string? netId = null,
        [Description("Ports to probe; default 851..860 and 801, 811, 821, 831.")] int[]? ports = null,
        [Description("Timeout per port in ms.")] int timeoutMs = 1000)
        => ads.Run(() =>
        {
            var id = AdsSessionManager.ParseNetId(netId);
            var probe = ports is { Length: > 0 } ? ports : Enumerable.Range(851, 10).Concat([801, 811, 821, 831]).ToArray();
            var found = new List<object>();
            var errors = new Dictionary<string, string>();
            foreach (var p in probe)
            {
                using var c = new AdsClient();
                c.Timeout = Math.Max(100, timeoutMs);
                c.Connect(id, p);
                var err = c.TryReadState(out var s);
                if (err != AdsErrorCode.NoError)
                {
                    if (err is AdsErrorCode.TargetMachineNotFound or AdsErrorCode.ClientSyncTimeOut)
                    {
                        throw AdsSessionManager.ToTcException(err, $"Probing {id}:{p}");
                    }

                    errors[p.ToString()] = err.ToString();
                    continue;
                }

                string? name = null;
                try
                {
                    name = c.ReadDeviceInfo().Name;
                }
                catch (AdsErrorException)
                {
                    // optional
                }

                found.Add(new
                {
                    port = p,
                    state = StateOf(s),
                    deviceName = name,
                    projectName = ReadPlcString(c, "TwinCAT_SystemInfoVarList._AppInfo.ProjectName"),
                    appName = ReadPlcString(c, "TwinCAT_SystemInfoVarList._AppInfo.AppName"),
                });
            }

            return new { netId = id.ToString(), runtimes = found, notAnswering = errors };
        });

    private static string? ReadPlcString(AdsClient c, string path)
    {
        if (c.TryReadSymbol(path, out var sym) != AdsErrorCode.NoError || sym is not IAdsSymbol { } s)
        {
            return null;
        }

        var r = c.ReadAsResult(s.IndexGroup, s.IndexOffset, s.ByteSize);
        return r.ErrorCode == AdsErrorCode.NoError ? SystemServiceProtocol.ReadZ(r.Data.Span, 0, r.Data.Length) : null;
    }

    // ------------------------------------------------------------------ files

    private const string RootDesc = "Path root: 'generic' (absolute path on the target, default), 'boot' (TwinCAT boot folder), 'target' (TwinCAT target folder), " +
                                    "'config', 'install', 'repository', 'license', 'data'.";

    [McpServerTool(Name = "ads_read_file", ReadOnly = true), Description(
        "Reads a file on the target through the TwinCAT system service (works remotely over an ADS route, no file share needed). " +
        "Text is returned as UTF-8 (encoding 'text') or the bytes as base64. Example: {path:'CurrentConfig.xml', root:'boot'}.")]
    public Task<CallToolResult> ReadFile(
        [Description("File path (absolute for root 'generic', otherwise relative to the root).")] string path,
        [Description(RootDesc)] string root = "generic",
        [Description("'text' (UTF-8, default) or 'base64'.")] string encoding = "text",
        [Description("Read at most this many bytes (default 1 MB, max 16 MB).")] int maxBytes = 1024 * 1024,
        [Description(SysNetIdDesc)] string? netId = null)
        => ads.Run(() =>
        {
            var sys = new SystemServiceClient(ads.Get(netId, AdsSessionManager.SystemServicePort));
            var (data, truncated) = sys.ReadFile(path, root, Math.Clamp(maxBytes, 1, 16 * 1024 * 1024));
            var b64 = encoding.Equals("base64", StringComparison.OrdinalIgnoreCase);
            return new
            {
                path,
                root,
                size = data.Length,
                truncated,
                content = b64 ? null : Encoding.UTF8.GetString(data),
                base64 = b64 ? Convert.ToBase64String(data) : null,
            };
        });

    [McpServerTool(Name = "ads_write_file", Destructive = true), Description(
        "Writes (creates/overwrites or appends) a file on the target through the TwinCAT system service; missing directories are created. " +
        "Content as UTF-8 text or base64. Example: {path:'C:\\\\Temp\\\\recipe.csv', content:'a;b\\n1;2'}.")]
    public Task<CallToolResult> WriteFile(
        [Description("File path (absolute for root 'generic').")] string path,
        [Description("File content.")] string content,
        [Description("'text' (UTF-8, default) or 'base64'.")] string encoding = "text",
        [Description("Append instead of overwrite.")] bool append = false,
        [Description(RootDesc)] string root = "generic",
        [Description(SysNetIdDesc)] string? netId = null,
        [Description(AdsLocal.Description)] bool allowLocalSystem = false)
        => ads.Run(() =>
        {
            LocalTargetGuard.Ensure(netId, allowLocalSystem, "write files");
            byte[] bytes;
            try
            {
                bytes = encoding.Equals("base64", StringComparison.OrdinalIgnoreCase) ? Convert.FromBase64String(content) : Encoding.UTF8.GetBytes(content);
            }
            catch (FormatException)
            {
                throw new TcException("content is not valid base64.");
            }

            var sys = new SystemServiceClient(ads.Get(netId, AdsSessionManager.SystemServicePort));
            sys.WriteFile(path, root, bytes, append);
            return new { path, root, written = bytes.Length, append };
        });

    [McpServerTool(Name = "ads_delete_file", Destructive = true), Description(
        "Deletes a file - or, with isDirectory=true, an EMPTY directory - on the target through the TwinCAT system service.")]
    public Task<CallToolResult> DeleteFile(
        [Description("File or directory path (absolute for root 'generic').")] string path,
        [Description(RootDesc)] string root = "generic",
        [Description("Remove an empty directory instead of a file.")] bool isDirectory = false,
        [Description(SysNetIdDesc)] string? netId = null,
        [Description(AdsLocal.Description)] bool allowLocalSystem = false)
        => ads.Run(() =>
        {
            LocalTargetGuard.Ensure(netId, allowLocalSystem, "delete files");
            var sys = new SystemServiceClient(ads.Get(netId, AdsSessionManager.SystemServicePort));
            if (isDirectory)
            {
                sys.RemoveDirectory(path, root);
            }
            else
            {
                sys.DeleteFile(path, root);
            }

            return new { deleted = path, root, isDirectory };
        });

    [McpServerTool(Name = "ads_list_dir", ReadOnly = true), Description(
        "Lists a directory on the target through the TwinCAT system service: names, directory flag, size, modification time. " +
        "Example: {path:'', root:'boot'} lists the TwinCAT boot folder; {path:'C:\\\\TwinCAT'} an absolute folder.")]
    public Task<CallToolResult> ListDir(
        [Description("Directory path; empty = the root folder itself (for roots other than 'generic').")] string path = "",
        [Description(RootDesc)] string root = "generic",
        [Description("Max entries returned.")] int maxEntries = 500,
        [Description(SysNetIdDesc)] string? netId = null)
        => ads.Run(() =>
        {
            var sys = new SystemServiceClient(ads.Get(netId, AdsSessionManager.SystemServicePort));
            var (entries, truncated) = sys.ListDirectory(path, root, Math.Clamp(maxEntries, 1, 10_000));
            return new
            {
                path,
                root,
                count = entries.Count,
                truncated,
                entries = entries.Select(e => new { name = e.Name, isDirectory = e.IsDirectory, size = e.IsDirectory ? (long?)null : e.Size, modified = e.ModifiedUtc }).ToList(),
            };
        });
}
