namespace TwinCatMcp.Tests.Integration;

/// <summary>System / real-time / variant / PLC-offline tools that do not change a runtime (always run).</summary>
[Collection(RuntimeCollection.Name)]
[Trait("Category", "Integration")]
public sealed class RuntimeConfigToolsTests(RuntimeProjectFixture fx)
{
    [Fact]
    public async Task Real_time_settings_report_the_default_base_time_of_1_ms()
    {
        var s = await fx.Call("rt_get_settings");

        var cpu = s.GetProperty("cpus").EnumerateArray().First();
        Assert.Equal(10000u, cpu.GetProperty("baseTime").GetProperty("ticks100ns").GetUInt32());
        Assert.NotEmpty(s.GetProperty("realTimeCores").EnumerateArray());
    }

    [Fact]
    public async Task Real_time_load_limit_and_base_time_can_be_changed()
    {
        var r = await fx.Call("rt_set_settings", new { baseTime = 500, unit = "us", loadLimitPercent = 60 });

        var cpu = r.GetProperty("settings").GetProperty("cpus").EnumerateArray().First();
        Assert.Equal(5000u, cpu.GetProperty("baseTime").GetProperty("ticks100ns").GetUInt32());
        Assert.Equal(60, cpu.GetProperty("loadLimitPercent").GetInt32());

        await fx.Call("rt_set_settings", new { baseTime = 1, unit = "ms", loadLimitPercent = 80 });
    }

    [Fact]
    public async Task Target_and_versions_are_reported()
    {
        var target = await fx.Call("system_get_target");
        var versions = await fx.Call("system_get_versions");

        Assert.Matches(@"^\d+\.\d+\.\d+\.\d+\.\d+\.\d+$", target.GetProperty("netId").GetString());
        Assert.StartsWith("3.1.4026", versions.GetProperty("xaeVersion").GetString());
        Assert.Contains(versions.GetProperty("xaeVersion").GetString(), versions.GetProperty("installedVersions").EnumerateArray().Select(v => v.GetString()));
    }

    [Fact]
    public async Task Project_version_can_be_pinned_and_unpinned()
    {
        var pinned = await fx.Call("system_pin_version", new { pin = true });
        Assert.True(pinned.GetProperty("projectPinned").GetBoolean());

        var unpinned = await fx.Call("system_pin_version", new { pin = false });
        Assert.False(unpinned.GetProperty("projectPinned").GetBoolean());
    }

    [Fact]
    public async Task Target_platform_can_be_switched()
    {
        var list = await fx.Call("system_list_target_platforms");
        var original = list.GetProperty("active").GetString()!;
        Assert.Contains("TwinCAT OS (x64)", list.GetProperty("platforms").EnumerateArray().Select(p => p.GetString()));

        var set = await fx.Call("system_set_target_platform", new { platform = "TwinCAT RT (x64)" });
        Assert.Equal("TwinCAT RT (x64)", set.GetProperty("active").GetString());

        await fx.Call("system_set_target_platform", new { platform = original });
    }

    [Fact]
    public async Task Unknown_target_platform_lists_the_valid_ones()
    {
        var (json, isError) = await fx.CallRaw("system_set_target_platform", new { platform = "TwinCAT Mars (x64)" });

        Assert.True(isError);
        Assert.Contains("TwinCAT RT (x64)", json.GetProperty("hint").GetString());
    }

    [Fact]
    public async Task Variants_can_be_defined_and_selected()
    {
        await fx.Call("variant_define", new { variants = new[] { "Small", "Large" }, groups = new Dictionary<string, string[]> { ["All"] = ["Small", "Large"] } });

        var set = await fx.Call("variant_set_current", new { variant = "Large" });
        var list = await fx.Call("variant_list");

        Assert.Equal("Large", set.GetProperty("current").GetString());
        Assert.Equal(["Small", "Large"], list.GetProperty("config").GetProperty("variants").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal("All", list.GetProperty("config").GetProperty("groups")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Plc_is_offline_and_autostart_can_be_toggled()
    {
        var state = await fx.Call("plc_online_state", new { plcProject = XaeProjectFixture.PlcName });
        Assert.False(state.GetProperty("loggedIn").GetBoolean());

        var on = await fx.Call("plc_online_set_autostart", new { enabled = true, plcProject = XaeProjectFixture.PlcName });
        Assert.True(on.GetProperty("autostart").GetBoolean());
        var off = await fx.Call("plc_online_set_autostart", new { enabled = false });
        Assert.False(off.GetProperty("autostart").GetBoolean());
    }

    [Fact]
    public async Task Start_without_login_is_rejected_with_a_hint()
    {
        var (json, isError) = await fx.CallRaw("plc_online_start", new { plcProject = XaeProjectFixture.PlcName });

        Assert.True(isError);
        Assert.Contains("plc_online_login", json.GetProperty("hint").GetString());
    }

    [Fact]
    public async Task License_catalog_resolves_TC1200()
    {
        var r = await fx.Call("license_list", new { search = "TC1200" });

        var hit = r.GetProperty("catalog").EnumerateArray().Single();
        Assert.Equal("{66689887-CCBD-452C-AC9A-039D997C6E66}", hit.GetProperty("licenseId").GetString());
    }

    [Fact]
    public async Task Manual_license_can_be_added_to_and_removed_from_the_PLC_project()
    {
        var added = await fx.Call("license_add", new { license = "TF6250", plcProject = XaeProjectFixture.PlcName });
        Assert.True(added.GetProperty("present").GetBoolean(), added.ToString());
        Assert.Equal("TF6250", added.GetProperty("license").GetProperty("orderNo").GetString());

        var listed = await fx.Call("license_list");
        Assert.Contains(listed.GetProperty("plcProjectLicenses")[0].GetProperty("licenses").EnumerateArray(),
            l => l.GetProperty("orderNo").GetString() == "TF6250");

        var removed = await fx.Call("license_add", new { license = "{6952449D-F68C-49A2-ADE4-8639D85B33A4}", remove = true });
        Assert.False(removed.GetProperty("present").GetBoolean(), removed.ToString());
    }

    [Fact]
    public async Task Usermode_runtimes_of_this_pc_are_listed()
    {
        var list = await fx.Call("rt_umrt_list");

        Assert.NotEmpty(list.EnumerateArray());
        Assert.All(list.EnumerateArray(), i => Assert.False(string.IsNullOrEmpty(i.GetProperty("instance").GetProperty("path").GetString())));
    }
}
