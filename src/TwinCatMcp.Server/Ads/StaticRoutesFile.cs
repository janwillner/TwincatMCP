using System.Xml;
using System.Xml.Linq;
using TwinCatMcp.Core;

namespace TwinCatMcp.Ads;

public sealed record StaticRoute(string Name, string? Address, string? NetId, string? RemoteNetId, string? Transport, string? Flags, string? Timeout);

/// <summary>Reads TwinCAT's StaticRoutes.xml (TcConfig/RemoteConnections/Route).</summary>
public static class StaticRoutesFile
{
    /// <summary>Candidate locations of StaticRoutes.xml on this PC (classic XAR and Usermode runtimes).</summary>
    public static IEnumerable<string> CandidatePaths()
    {
        var tcDir = Environment.GetEnvironmentVariable("TWINCAT3DIR");
        if (!string.IsNullOrEmpty(tcDir))
        {
            yield return Path.Combine(tcDir, "Target", "StaticRoutes.xml");
        }

        var x86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        yield return Path.Combine(x86, "Beckhoff", "TwinCAT", "3.1", "Target", "StaticRoutes.xml");
        yield return @"C:\TwinCAT\3.1\Target\StaticRoutes.xml";

        var umRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Beckhoff", "TwinCAT", "3.1", "Runtimes");
        if (Directory.Exists(umRoot))
        {
            foreach (var rt in Directory.EnumerateDirectories(umRoot))
            {
                yield return Path.Combine(rt, "3.1", "Target", "StaticRoutes.xml");
            }
        }
    }

    public static IReadOnlyList<StaticRoute> Parse(string xml)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (XmlException ex)
        {
            throw new TcException($"StaticRoutes.xml is not valid XML: {ex.Message}");
        }

        return doc.Descendants()
            .Where(e => e.Name.LocalName == "Route" && e.Parent?.Name.LocalName == "RemoteConnections")
            .Select(r =>
            {
                string? V(string n) => r.Elements().FirstOrDefault(e => e.Name.LocalName == n)?.Value.Trim();
                var netIdEl = r.Elements().FirstOrDefault(e => e.Name.LocalName == "NetId");
                return new StaticRoute(
                    V("Name") ?? string.Empty,
                    V("Address"),
                    netIdEl?.Value.Trim(),
                    netIdEl?.Attribute("RemoteNetId")?.Value,
                    V("Type"),
                    V("Flags"),
                    V("Timeout"));
            })
            .ToList();
    }
}
