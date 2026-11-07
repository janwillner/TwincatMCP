using System.Text.Json;

namespace TwinCatMcp.Tests.Integration;

/// <summary>Offline EtherCAT configuration (no hardware): devices, terminals, process image, slave info, CoE, topology.</summary>
[Collection(IoCollection.Name)]
[Trait("Category", "Integration")]
public sealed class IoEtherCatTests(IoProjectFixture fx)
{
    /// <summary>Creates a master with EK1100 + EL1008 + EL2008 (+ extra terminals) and returns the coupler path.</summary>
    private async Task<string> CreateStation(string master, params string[] extra)
    {
        await fx.Call("io_add_device", new { type = "ethercat-master", name = master });
        var result = await fx.Call("ecat_add_terminals", new { parent = "TIID^" + master, products = new[] { "EK1100", "EL1008", "EL2008" }.Concat(extra).ToArray() });
        Assert.True(result.GetProperty("complete").GetBoolean(), result.ToString());
        return $"TIID^{master}^Term 1 (EK1100)";
    }

    [Fact]
    public async Task Added_EtherCAT_master_is_listed_with_its_type()
    {
        var created = await fx.Call("io_add_device", new { type = "ethercat-master", name = "M_List" });
        var devices = await fx.Call("io_list_devices");

        Assert.Equal(111, created.GetProperty("device").GetProperty("subType").GetInt32());
        var dev = devices.EnumerateArray().Single(d => d.GetProperty("name").GetString() == "M_List");
        Assert.Equal("ethercat-master", dev.GetProperty("type").GetString());
        Assert.Equal(0, dev.GetProperty("boxCount").GetInt32());
    }

    [Fact]
    public async Task Terminals_are_created_below_the_coupler_with_resolved_product_revisions()
    {
        await fx.Call("io_add_device", new { type = "ethercat", name = "M_Terms" });

        var result = await fx.Call("ecat_add_terminals", new { parent = "TIID^M_Terms", products = new[] { "EK1100", "EL1008", "EL2008-0000-0017" } });

        var created = result.GetProperty("created").EnumerateArray().ToList();
        Assert.Equal(3, created.Count);
        Assert.Equal("TIID^M_Terms^Term 1 (EK1100)", created[0].GetProperty("path").GetString());
        Assert.Equal("TIID^M_Terms^Term 1 (EK1100)^Term 2 (EL1008)", created[1].GetProperty("path").GetString());
        Assert.Equal("EK1100-0000-0018", created[0].GetProperty("productRevision").GetString());
        Assert.Equal("EL1008-0000-0018", created[1].GetProperty("productRevision").GetString());
        Assert.Equal("EL2008-0000-0017", created[2].GetProperty("productRevision").GetString());
    }

    [Fact]
    public async Task Unknown_product_reports_an_error_and_the_items_created_before()
    {
        await fx.Call("io_add_device", new { type = "ethercat-master", name = "M_Bad" });

        var result = await fx.Call("ecat_add_terminals", new { parent = "TIID^M_Bad", products = new[] { "EK1100", "XY9999 does not exist" } });

        Assert.False(result.GetProperty("complete").GetBoolean());
        Assert.Single(result.GetProperty("created").EnumerateArray());
        Assert.Contains("not found", result.GetProperty("failed").GetProperty("error").GetString());
    }

    [Fact]
    public async Task Single_box_can_be_added_below_a_coupler()
    {
        var coupler = await CreateStation("M_Box");

        var box = await fx.Call("ecat_add_box", new { parent = coupler, product = "EL3102" });

        Assert.Equal(coupler + "^Term 4 (EL3102)", box.GetProperty("path").GetString());
        Assert.StartsWith("EL3102-0000-", box.GetProperty("productRevision").GetString());
    }

    [Fact]
    public async Task Topology_nests_terminals_below_the_coupler()
    {
        await CreateStation("M_Topo");

        var topo = await fx.Call("ecat_get_topology", new { master = "M_Topo", details = true });

        var master = topo.EnumerateArray().Single();
        var coupler = master.GetProperty("boxes").EnumerateArray().Single();
        Assert.Equal("Term 1 (EK1100)", coupler.GetProperty("box").GetProperty("name").GetString());
        var terminals = coupler.GetProperty("children").EnumerateArray().Select(c => c.GetProperty("box").GetProperty("name").GetString()).ToList();
        Assert.Equal(["Term 2 (EL1008)", "Term 3 (EL2008)"], terminals);
        Assert.Equal(1002, coupler.GetProperty("children")[0].GetProperty("details").GetProperty("ethercatAddress").GetInt32());
    }

    [Fact]
    public async Task Terminal_variables_are_listed_with_type_and_direction()
    {
        var coupler = await CreateStation("M_Vars");

        var inputs = await fx.Call("io_list_variables", new { treePath = coupler + "^Term 2 (EL1008)" });
        var outputs = await fx.Call("io_list_variables", new { treePath = coupler + "^Term 3 (EL2008)", direction = "output" });

        var vars = inputs.GetProperty("variables").EnumerateArray().ToList();
        Assert.Contains(vars, v => v.GetProperty("path").GetString() == coupler + "^Term 2 (EL1008)^Channel 1^Input");
        var ch1 = vars.First(v => v.GetProperty("path").GetString()!.EndsWith("Channel 1^Input"));
        Assert.Equal("BIT", ch1.GetProperty("type").GetString());
        Assert.Equal(1, ch1.GetProperty("bitSize").GetInt32());
        Assert.Equal("input", ch1.GetProperty("direction").GetString());
        Assert.Equal(8, vars.Count(v => v.GetProperty("name").GetString() == "Input"));
        Assert.Equal(8, outputs.GetProperty("variables").EnumerateArray().Count(v => v.GetProperty("name").GetString() == "Output"));
    }

    [Fact]
    public async Task Coupler_variables_include_terminals_only_on_request()
    {
        var coupler = await CreateStation("M_SubBoxes");

        var own = await fx.Call("io_list_variables", new { treePath = coupler, includeLinks = false });
        var all = await fx.Call("io_list_variables", new { treePath = coupler, includeSubBoxes = true, includeLinks = false });
        var device = await fx.Call("io_list_variables", new { treePath = "TIID^M_SubBoxes", includeLinks = false });

        static List<string> Paths(JsonElement j) => j.GetProperty("variables").EnumerateArray().Select(v => v.GetProperty("path").GetString()!).ToList();
        Assert.DoesNotContain(Paths(own), p => p.Contains("Term 2 (EL1008)"));
        Assert.Contains(Paths(own), p => p.StartsWith(coupler + "^"));
        Assert.Contains(Paths(all), p => p == coupler + "^Term 2 (EL1008)^Channel 1^Input");
        Assert.DoesNotContain(Paths(device), p => p.Contains("Term 1 (EK1100)"));
        Assert.Contains(Paths(device), p => p.StartsWith("TIID^M_SubBoxes^Inputs^"));
    }

    [Fact]
    public async Task Variable_info_contains_ads_address()
    {
        var coupler = await CreateStation("M_VarInfo");
        var path = coupler + "^Term 2 (EL1008)^Channel 1^Input";

        var info = await fx.Call("io_get_variable_info", new { treePaths = new[] { path } });

        var v = info.GetProperty(path);
        Assert.Equal("BIT", v.GetProperty("type").GetString());
        Assert.StartsWith("0x", v.GetProperty("ads").GetProperty("indexGroup").GetString());
    }

    [Fact]
    public async Task Slave_info_reports_EtherCAT_identity()
    {
        var coupler = await CreateStation("M_Ident");

        var info = await fx.Call("ecat_get_slave_info", new { treePaths = new[] { coupler + "^Term 2 (EL1008)" } });

        var el1008 = info.GetProperty(coupler + "^Term 2 (EL1008)");
        Assert.Equal(2u, el1008.GetProperty("vendorId").GetUInt32());
        // ProductCode of the EL1008 in the ESI: #x03f03052
        Assert.Equal("0x03F03052", el1008.GetProperty("productCode").GetString());
        Assert.Equal("EL1008-0000-0018", el1008.GetProperty("productRevision").GetString());
        Assert.Equal(1002, el1008.GetProperty("ethercatAddress").GetInt32());
    }

    [Fact]
    public async Task CoE_startup_parameter_is_added_read_back_and_removed()
    {
        var coupler = await CreateStation("M_Coe", "EL7041");
        var el7041 = coupler + "^Term 4 (EL7041)";

        var set = await fx.Call("ecat_set_startup_parameter", new { treePath = el7041, index = "0x8010", subIndex = "1", value = "1500", dataType = "UINT", comment = "max current" });
        var coe = await fx.Call("ecat_get_coe", new { treePath = el7041 });

        Assert.Equal("dc05", set.GetProperty("entry").GetProperty("data").GetString());
        var entry = coe.GetProperty("startupList").EnumerateArray().Single(e => e.GetProperty("object").GetString() == "0x8010:01");
        Assert.Equal("PS", entry.GetProperty("transition").GetString());
        Assert.Equal("dc05", entry.GetProperty("data").GetString());
        Assert.False(entry.GetProperty("fixed").GetBoolean());

        await fx.Call("ecat_set_startup_parameter", new { treePath = el7041, index = "0x8010", subIndex = "1", value = "1000", dataType = "UINT" });
        var replaced = await fx.Call("ecat_get_coe", new { treePath = el7041 });
        Assert.Equal("e803", replaced.GetProperty("startupList").EnumerateArray().Single(e => e.GetProperty("object").GetString() == "0x8010:01").GetProperty("data").GetString());

        var removed = await fx.Call("ecat_set_startup_parameter", new { treePath = el7041, index = "0x8010", subIndex = "1", remove = true });
        Assert.Equal(1, removed.GetProperty("removed").GetInt32());
        var after = await fx.Call("ecat_get_coe", new { treePath = el7041 });
        Assert.DoesNotContain(after.GetProperty("startupList").EnumerateArray(), e => e.GetProperty("object").GetString() == "0x8010:01");
        Assert.Contains(after.GetProperty("startupList").EnumerateArray(), e => e.GetProperty("fixed").GetBoolean());
    }

    [Fact]
    public async Task Online_CoE_read_without_hardware_fails_per_object_not_globally()
    {
        var coupler = await CreateStation("M_CoeOnline", "EL7041");

        var (json, isError) = await fx.CallRaw("ecat_get_coe", new { treePath = coupler + "^Term 4 (EL7041)", onlineObjects = new[] { "0x1018:02" }, onlineDataType = "UDINT" }, TimeSpan.FromMinutes(2));

        Assert.False(isError, json.ToString());
        Assert.True(json.GetProperty("online")[0].TryGetProperty("error", out _));
    }

    [Fact]
    public async Task Box_can_be_exported_to_xti_and_imported_again()
    {
        var coupler = await CreateStation("M_Xti");
        var file = Path.Combine(fx.WorkDir, "el1008.xti");

        await fx.Call("tree_export_child", new { parentPath = coupler, childName = "Term 2 (EL1008)", filePath = file });
        var imported = await fx.Call("tree_import_child", new { parentPath = coupler, filePath = file, name = "Term 9 (EL1008)" });

        Assert.True(File.Exists(file));
        Assert.Equal(coupler + "^Term 9 (EL1008)", imported.GetProperty("path").GetString());
        var info = await fx.Call("ecat_get_slave_info", new { treePaths = new[] { coupler + "^Term 9 (EL1008)" } });
        Assert.Equal("EL1008-0000-0018", info.GetProperty(coupler + "^Term 9 (EL1008)").GetProperty("productRevision").GetString());
    }

    [Fact]
    public async Task Device_scan_without_hardware_returns_gracefully()
    {
        var started = DateTime.UtcNow;

        var (json, isError) = await fx.CallRaw("io_scan_devices", new { timeoutSeconds = 120 }, TimeSpan.FromMinutes(3));

        Assert.False(isError, json.ToString());
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromMinutes(2));
        if (json.GetProperty("scanned").GetBoolean())
        {
            Assert.Equal(JsonValueKind.Array, json.GetProperty("found").ValueKind);
        }
        else
        {
            Assert.False(string.IsNullOrEmpty(json.GetProperty("hint").GetString()));
        }
    }

    [Fact]
    public async Task Box_scan_on_unbound_master_returns_gracefully()
    {
        await fx.Call("io_add_device", new { type = "ethercat-master", name = "M_Scan" });

        var (json, isError) = await fx.CallRaw("io_scan_boxes", new { device = "M_Scan", timeoutSeconds = 120 }, TimeSpan.FromMinutes(3));

        Assert.False(isError, json.ToString());
        if (!json.GetProperty("scanned").GetBoolean())
        {
            Assert.False(string.IsNullOrEmpty(json.GetProperty("hint").GetString()));
        }
        else
        {
            Assert.Equal(0, json.GetProperty("boxCount").GetInt32());
        }
    }

    [Fact]
    public async Task Device_can_be_bound_to_a_local_adapter_and_unbound()
    {
        var adapters = await fx.Call("io_list_network_adapters", new { includeVirtual = true });
        var adapter = adapters.EnumerateArray().First();
        await fx.Call("io_add_device", new { type = "ethercat-master", name = "M_Adapter" });

        var bound = await fx.Call("io_set_device_adapter", new { device = "M_Adapter", adapter = adapter.GetProperty("mac").GetString() });
        var listed = (await fx.Call("io_list_devices")).EnumerateArray().Single(d => d.GetProperty("name").GetString() == "M_Adapter");

        Assert.Equal(adapter.GetProperty("deviceName").GetString(), bound.GetProperty("adapter").GetProperty("applied").GetProperty("deviceName").GetString());
        Assert.Equal(adapter.GetProperty("deviceName").GetString(), listed.GetProperty("adapter").GetProperty("deviceName").GetString());

        await fx.Call("io_set_device_adapter", new { device = "M_Adapter", clear = true });
        var cleared = (await fx.Call("io_list_devices")).EnumerateArray().Single(d => d.GetProperty("name").GetString() == "M_Adapter");
        Assert.False(cleared.TryGetProperty("adapter", out var a) && a.ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public async Task Device_without_name_gets_the_XAE_default_name()
    {
        var created = await fx.Call("io_add_device", new { type = "ethercat-master" });

        var name = created.GetProperty("device").GetProperty("name").GetString()!;
        Assert.Matches(@"^Device \d+ \(EtherCAT\)$", name);
        await fx.Call("io_remove_device", new { device = name });
    }

    [Fact]
    public async Task Device_can_be_removed()
    {
        await fx.Call("io_add_device", new { type = "ethercat-master", name = "M_Remove" });

        await fx.Call("io_remove_device", new { device = "M_Remove" });

        var exists = await fx.Call("tree_exists", new { treePaths = new[] { "TIID^M_Remove" } });
        Assert.False(exists.GetProperty("TIID^M_Remove").GetBoolean());
    }

    [Fact]
    public async Task Other_device_types_can_be_added_by_friendly_name()
    {
        var profinet = await fx.Call("io_add_device", new { type = "profinet-controller", name = "PN_Test" });
        var eip = await fx.Call("io_add_device", new { type = "ethernetip-scanner", name = "EIP_Test" });

        Assert.Equal(113, profinet.GetProperty("device").GetProperty("subType").GetInt32());
        Assert.Equal(133, eip.GetProperty("device").GetProperty("subType").GetInt32());
        await fx.Call("io_remove_device", new { device = "PN_Test" });
        await fx.Call("io_remove_device", new { device = "EIP_Test" });
    }
}
