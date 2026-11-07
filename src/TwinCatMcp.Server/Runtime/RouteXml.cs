namespace TwinCatMcp.Runtime;

public sealed record RouteEntry(string Name, string NetId, string Kind);

public sealed record RouteList(string? Target, List<RouteEntry> Routes)
{
    public string? Resolve(string name)
        => Routes.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))?.NetId;

    public string? NameOf(string netId)
        => Routes.FirstOrDefault(r => string.Equals(r.NetId, netId, StringComparison.OrdinalIgnoreCase))?.Name;
}

/// <summary>Parses the routes node (TIRR, RoutePrj): the ADS routes the XAE knows ('Choose Target System' list).</summary>
public static class RouteXml
{
    public static RouteList Parse(string xml)
    {
        var prj = XmlText.Root(xml).Element("RoutePrj");
        var routes = new List<RouteEntry>();
        foreach (var (element, kind) in new[] { ("ActualRoutes", "actual"), ("StaticRoutes", "static"), ("ProjectRoutes", "project") })
        {
            foreach (var r in prj?.Element(element)?.Elements("Route") ?? [])
            {
                if (r.Str("Name") is { Length: > 0 } name && r.Str("NetId") is { Length: > 0 } netId)
                {
                    routes.Add(new RouteEntry(name, netId, kind));
                }
            }
        }

        return new RouteList(prj.Str("Target"), routes);
    }
}
