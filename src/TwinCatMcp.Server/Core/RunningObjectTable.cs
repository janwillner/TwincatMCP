using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace TwinCatMcp.Core;

/// <summary>Enumerates DTE instances registered in the COM Running Object Table.</summary>
public static class RunningObjectTable
{
    public sealed record DteEntry(string DisplayName, string ProgId, int ProcessId, object Dte);

    [DllImport("ole32.dll")]
    private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable prot);

    [DllImport("ole32.dll")]
    private static extern int CreateBindCtx(int reserved, out IBindCtx ppbc);

    /// <summary>
    /// Returns all DTE objects whose ROT display name looks like "!TcXaeShell.DTE.17.0:1234"
    /// or "!VisualStudio.DTE.17.0:1234".
    /// </summary>
    public static List<DteEntry> GetDteInstances()
    {
        var result = new List<DteEntry>();
        if (GetRunningObjectTable(0, out var rot) != 0 || rot is null)
        {
            return result;
        }

        rot.EnumRunning(out var enumMoniker);
        CreateBindCtx(0, out var ctx);
        var monikers = new IMoniker[1];
        while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
        {
            try
            {
                monikers[0].GetDisplayName(ctx, null, out var name);
                if (name is null || !name.StartsWith('!') || !name.Contains(".DTE.", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var colon = name.LastIndexOf(':');
                var progId = colon > 0 ? name[1..colon] : name[1..];
                var pid = colon > 0 && int.TryParse(name[(colon + 1)..], out var p) ? p : 0;
                rot.GetObject(monikers[0], out var obj);
                if (obj is not null)
                {
                    result.Add(new DteEntry(name, progId, pid, obj));
                }
            }
            catch (COMException)
            {
                // Stale ROT entry - ignore.
            }
        }

        return result;
    }
}
