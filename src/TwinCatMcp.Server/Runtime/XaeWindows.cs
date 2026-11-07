using System.Runtime.InteropServices;
using System.Text;

namespace TwinCatMcp.Runtime;

/// <summary>
/// Read-only detection of modal message boxes of the XAE process (e.g. "Target system reports a fatal error" with
/// "license not found" after a restart). Such a box blocks every COM call (RPC_E_CALL_REJECTED) until a human closes it,
/// even in SilentMode; tools report its text instead of waiting for a timeout. Nothing is clicked.
/// </summary>
public static class XaeWindows
{
    public sealed record Dialog(string Title, string Text);

    public static List<Dialog> ModalDialogs(int? processId)
    {
        var result = new List<Dialog>();
        if (processId is not { } pid || pid <= 0)
        {
            return result;
        }

        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out var owner);
            if (owner != pid || !IsWindowVisible(h) || ClassName(h) != "#32770")
            {
                return true;
            }

            var texts = new List<string>();
            EnumChildWindows(h, (c, _) =>
            {
                if (ClassName(c) == "Static" && WindowText(c) is { Length: > 0 } t)
                {
                    texts.Add(t.Trim());
                }

                return true;
            }, IntPtr.Zero);
            result.Add(new Dialog(WindowText(h), string.Join(" ", texts)));
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static string WindowText(IntPtr h)
    {
        var sb = new StringBuilder(2048);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string ClassName(IntPtr h)
    {
        var sb = new StringBuilder(256);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString();
    }

    private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int max);
}
