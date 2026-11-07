using System.Text;
using TCatSysManagerLib;
using TwinCatMcp.Core;

namespace TwinCatMcp.Motion;

/// <summary>
/// Reads / writes the TwinCAT 4026 axis "Simulation" flag. It is not exposed by ProduceXml/ConsumeXml, so the axis is
/// exported as .xti (ITcSmTreeItem.ExportChild), patched (SimulationMode="true") and re-imported at the same position
/// with bReconnect=true so that I/O and PLC links are restored. STA thread only.
/// </summary>
public static class AxisSimulation
{
    public static bool Get(ITcSmTreeItem axis)
    {
        var (dir, file) = Export(axis);
        try
        {
            return AxisXti.GetSimulationMode(File.ReadAllText(file));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    public static (ITcSmTreeItem Axis, bool Changed) Set(ITcSmTreeItem axis, bool enabled)
    {
        var parent = axis.Parent;
        var name = axis.Name;
        var (dir, file) = Export(axis);
        try
        {
            var bytes = File.ReadAllBytes(file);
            var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            var text = new UTF8Encoding(false).GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));
            if (AxisXti.GetSimulationMode(text) == enabled)
            {
                return (axis, false);
            }

            File.WriteAllText(file, AxisXti.SetSimulationMode(text, enabled), new UTF8Encoding(hasBom));

            var siblings = TreeInfo.Children(parent).Select(c => c.Name).ToList();
            var index = siblings.FindIndex(n => n.Equals(name, StringComparison.Ordinal));
            var before = index >= 0 && index + 1 < siblings.Count ? siblings[index + 1] : "";

            parent.DeleteChild(name);
            var imported = parent.ImportChild(file, before, true, name);
            return (imported, true);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    private static (string Dir, string File) Export(ITcSmTreeItem axis)
    {
        var dir = Path.Combine(Path.GetTempPath(), "twincat-mcp-xti", Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(dir);
        var safe = string.Concat(axis.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var file = Path.Combine(dir, safe + ".xti");
        axis.Parent.ExportChild(axis.Name, file);
        if (!File.Exists(file))
        {
            throw new TcException($"Exporting axis '{axis.PathName}' to .xti failed.");
        }

        return (dir, file);
    }

    private static void TryDelete(string dir)
    {
        try
        {
            Directory.Delete(dir, true);
        }
        catch
        {
            // best effort
        }
    }
}
