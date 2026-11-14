using System.Diagnostics;
using System.Runtime.InteropServices;
using TwinCatMcp.Core;

namespace TwinCatMcp.Runtime;

/// <summary>
/// Manages TC170x Usermode Runtime instances on this PC (files below C:\ProgramData\Beckhoff\TwinCAT\3.1\Runtimes and
/// TcSystemServiceUm.exe processes). Per Beckhoff docs an instance is a copy of Runtimes\UmRT_Template; it is started with
/// "TcSystemServiceUm.exe -t bin -i path -n &lt;name&gt; -c .\3.1" from the instance folder (what its Start.bat does),
/// writes its AMS Net ID into 3.1\TcRegistry.xml on first start and logs on to the local system router.
/// </summary>
public static class UmrtManager
{
    public sealed record Instance(
        string Name,
        string Path,
        string? NetId,
        bool Running,
        int[] ProcessIds,
        bool RunsAsWindowsService,
        List<LicenseFileInfo> LicenseFiles);

    public static string ProgramDataRoot =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Beckhoff\TwinCAT\3.1\Runtimes");

    public static string InstallRoot => System.IO.Path.Combine(XaeSession.TwinCatDir, "Runtimes");

    public static string TemplateDir => System.IO.Path.Combine(InstallRoot, "UmRT_Template");

    public static string Executable => System.IO.Path.Combine(InstallRoot, @"bin\TcSystemServiceUm.exe");

    public static string InstanceDir(string name)
    {
        UmrtConfig.ValidateInstanceName(name);
        return System.IO.Path.Combine(ProgramDataRoot, name);
    }

    public static List<Instance> List()
    {
        if (!Directory.Exists(ProgramDataRoot))
        {
            return [];
        }

        var processes = UmrtProcesses();
        var service = ServiceCommandLine();
        return Directory.GetDirectories(ProgramDataRoot)
            .Where(d => File.Exists(System.IO.Path.Combine(d, @"3.1\TcRegistry.xml")))
            .Select(d => Describe(d, processes, service))
            .ToList();
    }

    public static Instance Get(string name)
    {
        var dir = InstanceDir(name);
        if (!Directory.Exists(dir))
        {
            throw new TcException($"Usermode Runtime instance '{name}' does not exist ({dir}).") { Hint = "Call rt_umrt_list or rt_umrt_create." };
        }

        return Describe(dir, UmrtProcesses(), ServiceCommandLine());
    }

    public static Instance Create(string name, string? netId)
    {
        var dir = InstanceDir(name);
        if (Directory.Exists(dir))
        {
            throw new TcException($"Usermode Runtime instance '{name}' already exists ({dir}).") { Hint = "Use rt_umrt_start, or another name." };
        }

        if (!Directory.Exists(TemplateDir))
        {
            throw new TcException($"Usermode Runtime template not found: {TemplateDir}.") { Hint = "Install TC1700 (TwinCAT 3 Usermode Runtime, TwinCAT 3.1.4026 or newer) with the TwinCAT Package Manager." };
        }

        if (netId is not null)
        {
            UmrtConfig.NetIdToBin(netId); // validate before copying
        }

        CopyDirectory(TemplateDir, dir);
        if (netId is not null)
        {
            var reg = System.IO.Path.Combine(dir, @"3.1\TcRegistry.xml");
            File.WriteAllText(reg, UmrtConfig.WriteNetId(File.ReadAllText(reg), netId));
        }

        return Get(name);
    }

    /// <summary>Starts the instance (like its Start.bat) unless it already runs. Returns the process id.</summary>
    public static int Start(string name)
    {
        var inst = Get(name);
        if (inst.Running)
        {
            return inst.ProcessIds.FirstOrDefault();
        }

        if (!File.Exists(Executable))
        {
            throw new TcException($"TcSystemServiceUm.exe not found: {Executable}.") { Hint = "Install TC1700 (TwinCAT 3 Usermode Runtime, TwinCAT 3.1.4026 or newer)." };
        }

        var psi = new ProcessStartInfo(Executable)
        {
            WorkingDirectory = inst.Path,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Minimized,
        };
        foreach (var a in StartArguments(name, inst.NetId))
        {
            psi.ArgumentList.Add(a);
        }

        var p = Process.Start(psi) ?? throw new TcException("TcSystemServiceUm.exe could not be started.");
        return p.Id;
    }

    /// <summary>
    /// Command line of TcSystemServiceUm.exe. Start.bat passes "-i path" (the runtime picks a Net ID on its first start and
    /// ignores an AmsNetId preset in TcRegistry.xml); a configured Net ID is therefore passed explicitly with "-i".
    /// </summary>
    public static string[] StartArguments(string name, string? netId)
        => ["-t", "bin", "-i", string.IsNullOrWhiteSpace(netId) ? "path" : netId.Trim(), "-n", name, "-c", @".\3.1"];

    /// <summary>Stops a user-started instance: sends 'x' (exit) to its console, then kills it if it is still running.</summary>
    public static object Stop(string name, TimeSpan wait)
    {
        var inst = Get(name);
        if (inst.RunsAsWindowsService)
        {
            throw new TcException($"Instance '{name}' runs as the Windows service TcSysSrv and is not stopped by this tool.")
            {
                Hint = "Stop it via the TwinCAT system tray or 'sc stop TcSysSrv' (admin), or switch it to Config mode with system_set_config_mode.",
            };
        }

        if (!inst.Running)
        {
            return new { name, stopped = true, wasRunning = false };
        }

        var method = new List<string>();
        foreach (var pid in inst.ProcessIds)
        {
            using var p = Process.GetProcessById(pid);
            if (ConsoleInput.SendKey(pid, 'x'))
            {
                method.Add($"{pid}: console 'x'");
                if (p.WaitForExit((int)wait.TotalMilliseconds))
                {
                    continue;
                }
            }

            if (!p.HasExited)
            {
                p.Kill();
                p.WaitForExit(5000);
                method.Add($"{pid}: killed");
            }
        }

        return new { name, stopped = !Get(name).Running, wasRunning = true, method };
    }

    // ------------------------------------------------------------------ discovery

    private sealed record ProcInfo(int Pid, string? CommandLine);

    private static List<ProcInfo> UmrtProcesses()
    {
        var pids = Process.GetProcessesByName("TcSystemServiceUm").Select(p => { var id = p.Id; p.Dispose(); return id; }).ToList();
        if (pids.Count == 0)
        {
            return [];
        }

        var commandLines = CommandLines();
        return pids.Select(id => new ProcInfo(id, commandLines.GetValueOrDefault(id))).ToList();
    }

    /// <summary>Command lines of TcSystemServiceUm processes via CIM (elevated processes, e.g. the service, come back empty).</summary>
    private static Dictionary<int, string> CommandLines()
    {
        var result = new Dictionary<int, string>();
        try
        {
            var psi = new ProcessStartInfo("powershell.exe")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-Command",
                         "Get-CimInstance Win32_Process -Filter \"Name='TcSystemServiceUm.exe'\" | ForEach-Object { \"$($_.ProcessId)`t$($_.CommandLine)\" }" })
            {
                psi.ArgumentList.Add(a);
            }

            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(15000);
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var tab = line.IndexOf('\t');
                if (tab > 0 && int.TryParse(line[..tab], out var pid) && line.Length > tab + 1)
                {
                    result[pid] = line[(tab + 1)..];
                }
            }
        }
        catch
        {
            // best effort
        }

        return result;
    }

    /// <summary>ImagePath of the TwinCAT system service (on Usermode-only PCs it runs TcSystemServiceUm.exe for one instance).</summary>
    private static string? ServiceCommandLine()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\TcSysSrv");
            return key?.GetValue("ImagePath") as string;
        }
        catch
        {
            return null;
        }
    }

    private static Instance Describe(string dir, List<ProcInfo> processes, string? serviceCommandLine)
    {
        var name = System.IO.Path.GetFileName(dir);
        string? netId = null;
        try
        {
            netId = UmrtConfig.ReadNetId(File.ReadAllText(System.IO.Path.Combine(dir, @"3.1\TcRegistry.xml")));
        }
        catch
        {
            // unreadable registry - report without Net ID
        }

        var asService = serviceCommandLine is not null &&
                        serviceCommandLine.Contains("TcSystemServiceUm", StringComparison.OrdinalIgnoreCase) &&
                        serviceCommandLine.Contains(dir.TrimEnd('\\') + @"\", StringComparison.OrdinalIgnoreCase) &&
                        processes.Any(p => p.CommandLine is null || p.CommandLine.Contains("-s TcSysSrv", StringComparison.OrdinalIgnoreCase));
        var pids = processes
            .Where(p => p.CommandLine is not null && (p.CommandLine.Contains($"-n \"{name}\"", StringComparison.OrdinalIgnoreCase) ||
                                                      p.CommandLine.Contains($"-n {name} ", StringComparison.OrdinalIgnoreCase) ||
                                                      p.CommandLine.EndsWith($"-n {name}", StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Pid)
            .ToArray();

        var licenses = new List<LicenseFileInfo>();
        var licDir = System.IO.Path.Combine(dir, @"3.1\Target\License");
        if (Directory.Exists(licDir))
        {
            foreach (var f in Directory.GetFiles(licDir, "*.tclrs"))
            {
                try
                {
                    licenses.Add(LicenseFile.Parse(File.ReadAllText(f), DateTime.Now));
                }
                catch
                {
                    // ignore unreadable license files
                }
            }
        }

        return new Instance(name, dir, netId, pids.Length > 0 || asService, pids, asService, licenses);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, System.IO.Path.Combine(target, System.IO.Path.GetFileName(file)));
        }

        foreach (var sub in Directory.GetDirectories(source))
        {
            CopyDirectory(sub, System.IO.Path.Combine(target, System.IO.Path.GetFileName(sub)));
        }
    }

    /// <summary>Writes a key press into the console of another process (TcSystemServiceUm reads 'x' = exit, 'c' = config, 'r' = run).</summary>
    private static class ConsoleInput
    {
        private static readonly Lock Gate = new();

        public static bool SendKey(int pid, char key)
        {
            lock (Gate)
            {
                // Our own console (if any) is detached temporarily; stdio pipes of the MCP transport are not affected.
                FreeConsole();
                try
                {
                    if (!AttachConsole((uint)pid))
                    {
                        return false;
                    }

                    var handle = CreateFile("CONIN$", 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                    if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                    {
                        return false;
                    }

                    try
                    {
                        var records = new[] { KeyRecord(key, true), KeyRecord(key, false) };
                        return WriteConsoleInput(handle, records, (uint)records.Length, out _);
                    }
                    finally
                    {
                        CloseHandle(handle);
                    }
                }
                finally
                {
                    FreeConsole();
                }
            }
        }

        private static INPUT_RECORD KeyRecord(char c, bool down) => new()
        {
            EventType = 1,
            KeyEvent = new KEY_EVENT_RECORD
            {
                bKeyDown = down,
                wRepeatCount = 1,
                wVirtualKeyCode = (ushort)char.ToUpperInvariant(c),
                UnicodeChar = c,
            },
        };

        [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode)]
        private struct INPUT_RECORD
        {
            [FieldOffset(0)] public ushort EventType;
            [FieldOffset(4)] public KEY_EVENT_RECORD KeyEvent;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct KEY_EVENT_RECORD
        {
            [MarshalAs(UnmanagedType.Bool)] public bool bKeyDown;
            public ushort wRepeatCount;
            public ushort wVirtualKeyCode;
            public ushort wVirtualScanCode;
            public char UnicodeChar;
            public uint dwControlKeyState;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool WriteConsoleInput(IntPtr hConsoleInput, INPUT_RECORD[] lpBuffer, uint nLength, out uint lpNumberOfEventsWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);
    }
}
