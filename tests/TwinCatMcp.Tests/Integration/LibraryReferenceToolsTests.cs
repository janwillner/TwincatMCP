using System.Text.Json;

namespace TwinCatMcp.Tests.Integration;

[Collection(LibraryCollection.Name)]
[Trait("Category", "Integration")]
public sealed class LibraryReferenceToolsTests(LibraryFixture fx)
{
    private static string? S(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static List<JsonElement> Refs(JsonElement list) => list.GetProperty("references").EnumerateArray().ToList();

    [Fact]
    public async Task Standard_template_references_the_Tc2_and_Tc3_placeholders()
    {
        var list = await fx.Call("lib_list_references");

        var placeholders = Refs(list).Where(r => S(r, "kind") == "placeholder")
            .Select(r => S(r, "placeholderName")).ToList();
        Assert.Contains("Tc2_Standard", placeholders);
        Assert.Contains("Tc2_System", placeholders);
        Assert.Contains("Tc3_Module", placeholders);
        var std = Refs(list).Single(r => S(r, "placeholderName") == "Tc2_Standard");
        Assert.Equal("Beckhoff Automation GmbH", std.GetProperty("distributor").GetString());
        Assert.Matches(@"^\d+\.\d+\.\d+\.\d+$", std.GetProperty("effectiveVersion").GetString());
        Assert.Equal(fx.PlcProjectPath, list.GetProperty("plcProject").GetString());
    }

    [Fact]
    public async Task Add_Tc2_Utilities_and_Tc2_MC2_then_list_and_remove_them()
    {
        var add = await fx.Call("lib_add", new { libraries = new[] { "Tc2_Utilities, * (Beckhoff Automation GmbH)", "Tc2_MC2" } });
        Assert.All(add.GetProperty("results").EnumerateArray(), r => Assert.True(r.GetProperty("ok").GetBoolean(), r.ToString()));

        var list = await fx.Call("lib_list_references");
        var mc2 = Refs(list).Single(r => S(r, "libraryName") == "Tc2_MC2");
        Assert.Equal("library", S(mc2, "kind"));
        Assert.Equal("*", mc2.GetProperty("version").GetString());
        Assert.Equal("Beckhoff Automation GmbH", mc2.GetProperty("distributor").GetString());
        Assert.Matches(@"^3\.\d+\.\d+\.\d+$", mc2.GetProperty("effectiveVersion").GetString());
        Assert.Contains(Refs(list), r => S(r, "libraryName") == "Tc2_Utilities");

        // adding again is idempotent
        var again = await fx.Call("lib_add", new { libraries = new[] { "Tc2_MC2, * (Beckhoff Automation GmbH)" } });
        Assert.True(again.GetProperty("results")[0].GetProperty("alreadyReferenced").GetBoolean());

        await fx.Call("lib_remove", new { references = new[] { "Tc2_MC2", "Tc2_Utilities" } });

        var after = await fx.Call("lib_list_references");
        Assert.DoesNotContain(Refs(after), r => S(r, "libraryName") is "Tc2_MC2" or "Tc2_Utilities" && S(r, "kind") == "library");
    }

    [Fact]
    public async Task Unknown_library_reports_an_error_with_hint()
    {
        var (json, isError) = await fx.CallRaw("lib_add", new { libraries = new[] { "DoesNotExist_Lib, * (Nobody)" } });

        Assert.True(isError);
        Assert.Contains("DoesNotExist_Lib", json.ToString());
    }

    [Fact]
    public async Task Get_declaration_of_MC_MoveAbsolute_from_Tc2_MC2()
    {
        await fx.Call("lib_add", new { libraries = new[] { "Tc2_MC2, * (Beckhoff Automation GmbH)" } });
        try
        {
            var decl = await fx.Call("lib_get_declaration", new { objects = new[] { "MC_MoveAbsolute" }, library = "Tc2_MC2" });

            var d = decl.GetProperty("declarations")[0];
            Assert.Equal("Tc2_MC2", d.GetProperty("library").GetString());
            Assert.Equal("FunctionBlock", S(d, "kind"));
            var iec = d.GetProperty("declaration").GetString()!;
            Assert.StartsWith("FUNCTION_BLOCK MC_MoveAbsolute", iec);
            Assert.Contains("Execute : BOOL;", iec);
            Assert.Contains("Position : LREAL;", iec);
            Assert.Contains("VAR_IN_OUT", iec);
            Assert.Contains("Axis : AXIS_REF;", iec);
            Assert.Contains("ErrorID : UDINT;", iec);
        }
        finally
        {
            await fx.CallRaw("lib_remove", new { references = new[] { "Tc2_MC2" } });
        }
    }

    [Fact]
    public async Task Get_declaration_without_library_searches_all_referenced_libraries()
    {
        var decl = await fx.Call("lib_get_declaration", new { objects = new[] { "TON", "CONCAT" } });

        var byName = decl.GetProperty("declarations").EnumerateArray().ToDictionary(d => d.GetProperty("name").GetString()!);
        Assert.Equal("Tc2_Standard", byName["TON"].GetProperty("library").GetString());
        Assert.StartsWith("FUNCTION CONCAT : STRING", byName["CONCAT"].GetProperty("declaration").GetString()!.Split('\n').First(l => !l.StartsWith("//")));
    }

    [Fact]
    public async Task Get_signatures_filters_by_pattern_and_kind()
    {
        var sig = await fx.Call("lib_get_signatures", new { library = "Tc2_Standard", namePattern = "T*", kind = "FunctionBlock" });

        var names = sig.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("name").GetString()).ToList();
        Assert.Contains("TON", names);
        Assert.Contains("TOF", names);
        Assert.All(sig.GetProperty("entries").EnumerateArray(), e => Assert.Equal("FunctionBlock", S(e, "kind")));
        var ton = sig.GetProperty("entries").EnumerateArray().Single(e => e.GetProperty("name").GetString() == "TON");
        Assert.Contains(ton.GetProperty("inputs").EnumerateArray(), v => v.GetProperty("name").GetString() == "PT" && v.GetProperty("dataType").GetString() == "TIME");
    }

    [Fact]
    public async Task Placeholder_add_resolve_freeze_and_remove()
    {
        await fx.Call("lib_add_placeholder", new { placeholderName = "Mcp_Math", library = "Tc2_Math, * (Beckhoff Automation GmbH)" });
        try
        {
            var list = await fx.Call("lib_list_references");
            var ph = Refs(list).Single(r => S(r, "placeholderName") == "Mcp_Math");
            Assert.Equal("Tc2_Math, * (Beckhoff Automation GmbH)", ph.GetProperty("defaultResolution").GetString());
            var effective = ph.GetProperty("effectiveVersion").GetString()!;

            var installed = await fx.Call("lib_list_installed", new { nameContains = "Tc2_Math", latestOnly = false });
            var anyVersion = installed.GetProperty("libraries").EnumerateArray()
                .First(l => l.GetProperty("name").GetString() == "Tc2_Math").GetProperty("version").GetString();

            await fx.Call("lib_set_resolution", new { placeholderName = "Mcp_Math", library = $"Tc2_Math, {anyVersion} (Beckhoff Automation GmbH)" });
            list = await fx.Call("lib_list_references");
            ph = Refs(list).Single(r => S(r, "placeholderName") == "Mcp_Math");
            Assert.Equal($"Tc2_Math, {anyVersion} (Beckhoff Automation GmbH)", ph.GetProperty("redirection").GetString());

            var frozen = await fx.Call("lib_freeze_placeholders", new { placeholderName = "Tc2_Standard" });
            Assert.StartsWith("Tc2_Standard, ", frozen.GetProperty("redirections").GetProperty("Tc2_Standard").GetString());
            Assert.NotEqual("", effective);
        }
        finally
        {
            await fx.Call("lib_remove", new { references = new[] { "Mcp_Math" } });
        }

        var after = await fx.Call("lib_list_references");
        Assert.DoesNotContain(Refs(after), r => S(r, "placeholderName") == "Mcp_Math");
    }
}

