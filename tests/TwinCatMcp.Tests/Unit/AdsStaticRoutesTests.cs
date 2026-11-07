using TwinCatMcp.Ads;
using TwinCatMcp.Core;

namespace TwinCatMcp.Tests.Unit;

public sealed class AdsStaticRoutesTests
{
    // Literal from Beckhoff InfoSys "AmsNAT > Configuration" (tc3_grundlagen/4275317003.html).
    private const string AmsNatSample = """
        <?xml version="1.0" encoding="UTF-8"?><TcConfig xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="http://www.beckhoff.com/schemas/2015/12/TcConfig">   <RemoteConnections>      <Route>         <Name>CX-111111</Name>         <Address>10.0.10.8</Address>         <NetId RemoteNetId="5.19.8.122.1.1">10.0.10.8.1.1</NetId>         <Type>TCP_IP</Type>      </Route>      <Route>         <Name>CX-222222</Name>         <Address>10.0.20.8</Address>         <NetId RemoteNetId="5.19.8.122.1.1">10.0.20.8.1.1</NetId>         <Type>TCP_IP</Type>      </Route>   </RemoteConnections></TcConfig>
        """;

    [Fact]
    public void Parses_routes_from_static_routes_xml()
    {
        var routes = StaticRoutesFile.Parse(AmsNatSample);

        Assert.Equal(2, routes.Count);
        Assert.Equal("CX-111111", routes[0].Name);
        Assert.Equal("10.0.10.8", routes[0].Address);
        Assert.Equal("10.0.10.8.1.1", routes[0].NetId);
        Assert.Equal("5.19.8.122.1.1", routes[0].RemoteNetId);
        Assert.Equal("TCP_IP", routes[0].Transport);
    }

    [Fact]
    public void Empty_remote_connections_give_no_routes()
    {
        const string xml = """
            <?xml version="1.0"?>
            <TcConfig xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="http://www.beckhoff.com/schemas/2009/05/TcConfig">
            	<RemoteConnections>
            	</RemoteConnections>
            </TcConfig>
            """;
        Assert.Empty(StaticRoutesFile.Parse(xml));
    }

    [Fact]
    public void Invalid_xml_gives_a_tool_error()
    {
        Assert.Throws<TcException>(() => StaticRoutesFile.Parse("<TcConfig>"));
    }
}
