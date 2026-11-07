using System.Text.Json;

namespace TwinCatMcp.Tests.Integration;

/// <summary>Linking PLC process image variables to EtherCAT channels (offline configuration).</summary>
[Collection(IoCollection.Name)]
[Trait("Category", "Integration")]
public sealed class IoMappingTests(IoProjectFixture fx)
{
    private const string Out = @"C:\Users\jan_w\AppData\Local\Temp\claude\9abca6ffa7b051ec\552da8f4-52b4-45ed-810a-16427e972480\scratchpad\wp4";

    private async Task<string> CreateStation(string master)
    {
        await fx.Call("io_add_device", new { type = "ethercat-master", name = master });
        var result = await fx.Call("ecat_add_terminals", new { parent = "TIID^" + master, products = new[] { "EK1100", "EL1008", "EL2008" } });
        Assert.True(result.GetProperty("complete").GetBoolean(), result.ToString());
        return $"TIID^{master}^Term 1 (EK1100)";
    }

    private async Task<List<(string A, string B)>> Links(string? filter = null)
    {
        var json = await fx.Call("mapping_list_links", new { filter });
        return json.GetProperty("links").EnumerateArray().Select(l => (l.GetProperty("a").GetString()!, l.GetProperty("b").GetString()!)).ToList();
    }

    private static bool Has(List<(string A, string B)> links, string x, string y)
        => links.Any(l => (l.A == x && l.B == y) || (l.A == y && l.B == x));

    [Fact]
    public async Task PLC_variables_are_linked_listed_and_unlinked()
    {
        var coupler = await CreateStation("M_Link");
        var plcIn = IoProjectFixture.PlcInputs + "^GVL_IO.bIn1";
        var plcOut = IoProjectFixture.PlcOutputs + "^GVL_IO.bOut1";
        var ioIn = coupler + "^Term 2 (EL1008)^Channel 1^Input";
        var ioOut = coupler + "^Term 3 (EL2008)^Channel 1^Output";

        var result = await fx.Call("mapping_link", new { links = new[] { new { a = plcIn, b = ioIn }, new { a = plcOut, b = ioOut } } });

        Assert.All(result.EnumerateArray(), r => Assert.True(r.GetProperty("ok").GetBoolean(), r.ToString()));
        var links = await Links("M_Link");
        Assert.True(Has(links, plcIn, ioIn), string.Join("\n", links));
        Assert.True(Has(links, plcOut, ioOut));

        Directory.CreateDirectory(Out);
        await File.WriteAllTextAsync(Path.Combine(Out, "mapping_produce.xml"), (await fx.Call("mapping_produce")).GetProperty("xml").GetString());

        var vars = await fx.Call("io_list_variables", new { treePath = coupler + "^Term 2 (EL1008)" });
        var ch1 = vars.GetProperty("variables").EnumerateArray().Single(v => v.GetProperty("path").GetString() == ioIn);
        Assert.Equal(plcIn, ch1.GetProperty("linkedTo")[0].GetString());

        await fx.Call("mapping_unlink", new { links = new[] { new { a = plcIn, b = ioIn } } });
        await fx.Call("mapping_unlink", new { links = new[] { new { a = plcOut } } });
        var after = await Links("M_Link");
        Assert.Empty(after);
    }

    [Fact]
    public async Task Plc_instance_variables_are_listed()
    {
        var vars = await fx.Call("io_list_variables", new { treePath = IoProjectFixture.PlcInstance, includeLinks = false });

        var names = vars.GetProperty("variables").EnumerateArray().Select(v => v.GetProperty("name").GetString()).ToList();
        Assert.Contains("GVL_IO.bIn1", names);
        Assert.Contains("GVL_IO.bOut4", names);
        var analog = vars.GetProperty("variables").EnumerateArray().Single(v => v.GetProperty("name").GetString() == "GVL_IO.nAnalog");
        Assert.Equal("INT", analog.GetProperty("type").GetString());
        Assert.Equal(16, analog.GetProperty("bitSize").GetInt32());
    }

    [Fact]
    public async Task Auto_link_maps_by_name()
    {
        var coupler = await CreateStation("M_Auto");

        var result = await fx.Call("mapping_auto_link", new
        {
            plcInstance = IoProjectFixture.PlcInstance,
            ioParent = coupler,
            map = new Dictionary<string, string>
            {
                ["GVL_IO.bIn2"] = "Term 2 (EL1008)^Channel 2^Input",
                ["GVL_IO.bOut2"] = "Term 3 (EL2008)^Channel 2^Output",
                ["GVL_IO.doesNotExist"] = "Term 2 (EL1008)^Channel 3^Input",
            },
        });

        var items = result.EnumerateArray().ToList();
        Assert.Equal(2, items.Count(i => i.GetProperty("ok").GetBoolean()));
        Assert.Contains(items, i => !i.GetProperty("ok").GetBoolean() && i.GetProperty("error").GetString()!.Contains("doesNotExist"));
        var links = await Links("M_Auto");
        Assert.True(Has(links, IoProjectFixture.PlcInputs + "^GVL_IO.bIn2", coupler + "^Term 2 (EL1008)^Channel 2^Input"));

        await fx.Call("mapping_unlink", new { links = new[] { new { a = IoProjectFixture.PlcInputs + "^GVL_IO.bIn2" }, new { a = IoProjectFixture.PlcOutputs + "^GVL_IO.bOut2" } } });
    }

    [Fact]
    public async Task Invalid_link_is_reported_per_pair()
    {
        var result = await fx.Call("mapping_link", new { links = new[] { new { a = IoProjectFixture.PlcInputs + "^GVL_IO.bIn3", b = "TIID^Nope^Channel 1^Input" } } });

        var r = result.EnumerateArray().Single();
        Assert.False(r.GetProperty("ok").GetBoolean());
        Assert.False(string.IsNullOrEmpty(r.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task Mapping_xml_round_trips_through_produce_clear_consume_and_check()
    {
        var coupler = await CreateStation("M_Xml");
        var plcIn = IoProjectFixture.PlcInputs + "^GVL_IO.bIn4";
        var ioIn = coupler + "^Term 2 (EL1008)^Channel 4^Input";
        await fx.Call("mapping_link", new { links = new[] { new { a = plcIn, b = ioIn } } });
        var xml = (await fx.Call("mapping_produce")).GetProperty("xml").GetString();

        var check = await fx.Call("mapping_check");
        Assert.True(check.GetProperty("ok").GetBoolean(), check.ToString());

        var cleared = await fx.Call("mapping_clear");
        Assert.True(cleared.GetProperty("removedLinks").GetInt32() >= 1);
        Assert.Empty(await Links());

        var consumed = await fx.Call("mapping_consume", new { xml });
        Assert.True(consumed.GetProperty("linkCount").GetInt32() >= 1);
        Assert.True(Has(await Links("M_Xml"), plcIn, ioIn));

        await fx.Call("mapping_unlink", new { links = new[] { new { a = plcIn } } });
    }

    [Fact]
    public async Task Check_reports_links_whose_terminal_was_deleted()
    {
        var coupler = await CreateStation("M_Dangling");
        var plcOut = IoProjectFixture.PlcOutputs + "^GVL_IO.bOut3";
        await fx.Call("mapping_link", new { links = new[] { new { a = plcOut, b = coupler + "^Term 3 (EL2008)^Channel 3^Output" } } });

        await fx.Call("tree_delete_item", new { treePath = coupler + "^Term 3 (EL2008)" });
        var check = await fx.Call("mapping_check");

        // Deleting a terminal removes its links in TwinCAT, so the project must stay consistent.
        Assert.True(check.GetProperty("ok").GetBoolean(), check.ToString());
        Assert.DoesNotContain(await Links("M_Dangling"), l => l.A.Contains("Term 3") || l.B.Contains("Term 3"));
    }
}
