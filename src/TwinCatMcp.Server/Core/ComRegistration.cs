namespace TwinCatMcp.Core;

/// <summary>Resolves the executable of a COM LocalServer32 registration.</summary>
public static class ComRegistration
{
    /// <summary>Executable registered as LocalServer32 for the CLSID (null if not resolvable).</summary>
    public static string? LocalServerExe(Guid clsid)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey($@"CLSID\{{{clsid}}}\LocalServer32");
            return ExecutableFromCommand(key?.GetValue(null) as string);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Executable path of a LocalServer32 command line. Handles quoted paths and unquoted paths that contain spaces
    /// (TwinCAT 4026 registers 'C:\Program Files\Beckhoff\TcXaeShell\...\TcXaeShell.exe' without quotes), with or
    /// without trailing arguments. Returns null when no existing file can be identified.
    /// </summary>
    public static string? ExecutableFromCommand(string? command, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        var raw = command?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        if (raw.StartsWith('"'))
        {
            var end = raw.IndexOf('"', 1);
            var quoted = end > 0 ? raw[1..end] : raw[1..];
            return exists(quoted) ? quoted : null;
        }

        if (exists(raw))
        {
            return raw;
        }

        // Unquoted with arguments: the longest prefix ending before a space that names an existing file.
        for (var i = raw.LastIndexOf(' '); i > 0; i = raw.LastIndexOf(' ', i - 1))
        {
            var candidate = raw[..i].TrimEnd();
            if (exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
