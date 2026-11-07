using TwinCatMcp.Runtime;

namespace TwinCatMcp.Tests.Unit;

public sealed class RuntimeRouteXmlTests
{
    // ProduceXml() of TIRR on this PC (TwinCAT 3.1.4026.22) with a running Usermode Runtime instance (image data removed).
    private const string ProducedTirr =
        "<?xml version=\"1.0\"?><TreeItem><ItemName>Routes</ItemName><PathName>TIRR</PathName><ItemType>31</ItemType><RoutePrj>" +
        "<Target>169.254.123.103.1.1</Target><UseRelativeNetIds>false</UseRelativeNetIds><ActualRoutes><Route><Name>UmRT_McpTest</Name>" +
        "<NetId>199.4.42.250.1.1</NetId></Route></ActualRoutes><StaticRoutes/><ProjectRoutes/><TargetList><BroadcastSearch>false</BroadcastSearch>" +
        "<Search></Search></TargetList></RoutePrj></TreeItem>";

    [Fact]
    public void Parses_target_and_actual_routes()
    {
        var r = RouteXml.Parse(ProducedTirr);

        Assert.Equal("169.254.123.103.1.1", r.Target);
        var route = Assert.Single(r.Routes);
        Assert.Equal("UmRT_McpTest", route.Name);
        Assert.Equal("199.4.42.250.1.1", route.NetId);
        Assert.Equal("actual", route.Kind);
    }

    [Fact]
    public void Resolves_route_names_case_insensitively()
    {
        var r = RouteXml.Parse(ProducedTirr);

        Assert.Equal("199.4.42.250.1.1", r.Resolve("umrt_mcptest"));
        Assert.Null(r.Resolve("Unknown"));
        Assert.Equal("UmRT_McpTest", r.NameOf("199.4.42.250.1.1"));
    }
}
