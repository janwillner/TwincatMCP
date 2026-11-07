using TwinCatMcp.Libraries;

namespace TwinCatMcp.Tests.Unit;

public sealed class LibraryReferencesXmlTests
{
    // Trimmed from a live ProduceXml() of "TIPC^Plc1^Plc1 Project^References" (TwinCAT 3.1.4026.22).
    private const string Xml = """
        <TreeItem><ItemName>References</ItemName><PathName>TIPC^Plc1^Plc1 Project^References</PathName><ItemType>617</ItemType><ItemSubTypeName>TREEITEMTYPE_PLCLIBMAN</ItemSubTypeName><ChildCount>5</ChildCount>
        <PlcLibDef>
          <Repositories>
            <Repository><Name>System</Name><RootFolder>C:\ProgramData\Beckhoff\TwinCAT\PlcEngineering\Managed Libraries</RootFolder></Repository>
            <Repository><Name>TypeSystem</Name><RootFolder>C:\Users\jan_w\AppData\Local\Temp\TypeSystem</RootFolder></Repository>
          </Repositories>
          <DevicePlaceholders>
            <Placeholder><PlaceholderName>Tc2_Standard</PlaceholderName><DefaultResolution><LibraryName>Tc2_Standard</LibraryName><Version>*</Version><Distributor>Beckhoff Automation GmbH</Distributor></DefaultResolution></Placeholder>
            <Placeholder><PlaceholderName>Tc2_Utilities</PlaceholderName><DefaultResolution><LibraryName>Tc2_Utilities</LibraryName><Version>*</Version><Distributor>Beckhoff Automation GmbH</Distributor></DefaultResolution></Placeholder>
          </DevicePlaceholders>
          <PlaceholderRedirections><Redirection><Placeholder>Placeholder_NC</Placeholder><Library>Tc2_NC, 3.3.4.0 (Beckhoff Automation GmbH)</Library></Redirection><Redirection><Placeholder>Tc2_Standard</Placeholder><Library>Tc2_Standard, 3.4.5.0 (Beckhoff Automation GmbH)</Library></Redirection></PlaceholderRedirections>
          <References>
            <PlaceholderReference id="4262190327"><LibItemName>#Tc2_Standard</LibItemName><PlaceholderName>Tc2_Standard</PlaceholderName><Namespace>Tc2_Standard</Namespace><RelativePath>c:\programdata\beckhoff\twincat\plcengineering\managed libraries\beckhoff automation gmbh\tc2_standard\3.4.5.0\tc2_standard.compiled-library-ge33</RelativePath><DefaultResolution>Tc2_Standard, * (Beckhoff Automation GmbH)</DefaultResolution><EffectiveResolution><LibraryName>Tc2_Standard</LibraryName><Version>3.4.5.0</Version><Distributor>Beckhoff Automation GmbH</Distributor></EffectiveResolution></PlaceholderReference>
            <PlaceholderReference id="1391281416" systemLibrary="true"><LibItemName>#Tc3_Module</LibItemName><PlaceholderName>Tc3_Module</PlaceholderName><Namespace>Tc3_Module</Namespace><RelativePath>c:\x\tc3_module.compiled-library-ge33</RelativePath><DefaultResolution>Tc3_Module, * (Beckhoff Automation GmbH)</DefaultResolution><EffectiveResolution><LibraryName>Tc3_Module</LibraryName><Version>3.4.5.0</Version><Distributor>Beckhoff Automation GmbH</Distributor><Dependencies><Dependency isPlaceholder="true"><PlaceholderName>Tc2_Standard</PlaceholderName><EffectiveResolution><LibraryName>Tc2_Standard</LibraryName><Version>3.4.5.0</Version><Distributor>Beckhoff Automation GmbH</Distributor></EffectiveResolution></Dependency></Dependencies></EffectiveResolution></PlaceholderReference>
            <Library id="1329561425"><LibItemName>Tc2_MC2, * (Beckhoff Automation GmbH)</LibItemName><LibraryName>Tc2_MC2</LibraryName><Version>*</Version><Distributor>Beckhoff Automation GmbH</Distributor><Namespace>Tc2_MC2</Namespace><RelativePath>c:\x\tc2_mc2.compiled-library-ge33</RelativePath><EffectiveVersion>3.3.72.0</EffectiveVersion><Dependencies><Dependency isPlaceholder="true"><PlaceholderName>Tc2_Math</PlaceholderName><EffectiveResolution><LibraryName>Tc2_Math</LibraryName><Version>3.5.1.0</Version><Distributor>Beckhoff Automation GmbH</Distributor></EffectiveResolution></Dependency><Dependency isLibrary="true"><LibraryName>Tc3_GlobalTypes_MC</LibraryName><Version>1.0.0.0</Version><Distributor>Beckhoff Automation GmbH</Distributor></Dependency></Dependencies></Library>
            <Library id="2"><LibItemName>Tc2_MC2, 3.3.69.0 (Beckhoff Automation GmbH)</LibItemName><LibraryName>Tc2_MC2</LibraryName><Version>3.3.69.0</Version><Distributor>Beckhoff Automation GmbH</Distributor><Namespace>Tc2_MC2</Namespace><RelativePath /><EffectiveVersion>3.3.69.0</EffectiveVersion></Library>
            <PlaceholderReference id="1127238093"><LibItemName>#Placeholder_NC</LibItemName><PlaceholderName>Placeholder_NC</PlaceholderName><Namespace>Tc2_NC</Namespace><RelativePath /><DefaultResolution>Tc2_NC, * (Beckhoff Automation GmbH)</DefaultResolution><EffectiveResolution /></PlaceholderReference>
          </References>
          <Commands><ScanLibraries><Active>false</Active></ScanLibraries></Commands>
        </PlcLibDef></TreeItem>
        """;

    [Fact]
    public void Parses_repositories_in_order()
    {
        var doc = LibraryReferencesXml.Parse(Xml);

        Assert.Equal(["System", "TypeSystem"], doc.Repositories.Select(r => r.Name));
        Assert.Equal(@"C:\ProgramData\Beckhoff\TwinCAT\PlcEngineering\Managed Libraries", doc.Repositories[0].Folder);
        Assert.Equal(0, doc.Repositories[0].Index);
        Assert.Equal(1, doc.Repositories[1].Index);
    }

    [Fact]
    public void Parses_libraries_with_requested_and_effective_version()
    {
        var doc = LibraryReferencesXml.Parse(Xml);

        var mc2 = doc.References.Single(r => r.ItemName == "Tc2_MC2, * (Beckhoff Automation GmbH)");
        Assert.Equal("library", mc2.Kind);
        Assert.Equal("Tc2_MC2", mc2.LibraryName);
        Assert.Equal("*", mc2.Version);
        Assert.Equal("3.3.72.0", mc2.EffectiveVersion);
        Assert.Equal("Beckhoff Automation GmbH", mc2.Distributor);
        Assert.Equal("Tc2_MC2", mc2.Namespace);
        Assert.Null(mc2.PlaceholderName);
        Assert.False(mc2.Unresolved);
    }

    [Fact]
    public void Parses_placeholders_with_default_and_effective_resolution()
    {
        var doc = LibraryReferencesXml.Parse(Xml);

        var std = doc.References.Single(r => r.PlaceholderName == "Tc2_Standard");
        Assert.Equal("placeholder", std.Kind);
        Assert.Equal("#Tc2_Standard", std.ItemName);
        Assert.Equal("Tc2_Standard, * (Beckhoff Automation GmbH)", std.DefaultResolution);
        Assert.Equal("Tc2_Standard", std.LibraryName);
        Assert.Equal("3.4.5.0", std.EffectiveVersion);
        Assert.Equal("Beckhoff Automation GmbH", std.Distributor);
        Assert.Equal("Tc2_Standard, 3.4.5.0 (Beckhoff Automation GmbH)", std.Redirection);
        Assert.False(std.SystemLibrary);
    }

    [Fact]
    public void Flags_system_libraries_and_unresolved_placeholders()
    {
        var doc = LibraryReferencesXml.Parse(Xml);

        Assert.True(doc.References.Single(r => r.PlaceholderName == "Tc3_Module").SystemLibrary);
        var nc = doc.References.Single(r => r.PlaceholderName == "Placeholder_NC");
        Assert.True(nc.Unresolved);
        Assert.Null(nc.EffectiveVersion);
        Assert.Equal("Tc2_NC, 3.3.4.0 (Beckhoff Automation GmbH)", nc.Redirection);
    }

    [Fact]
    public void Dependencies_are_only_included_on_request()
    {
        Assert.Null(LibraryReferencesXml.Parse(Xml).References.Single(r => r.ItemName.StartsWith("Tc2_MC2, *")).Dependencies);

        var deps = LibraryReferencesXml.Parse(Xml, includeDependencies: true).References.Single(r => r.ItemName.StartsWith("Tc2_MC2, *")).Dependencies!;

        Assert.Equal(["Tc2_Math, 3.5.1.0 (Beckhoff Automation GmbH)", "Tc3_GlobalTypes_MC, 1.0.0.0 (Beckhoff Automation GmbH)"], deps);
    }

    [Fact]
    public void Parses_device_placeholders()
    {
        var doc = LibraryReferencesXml.Parse(Xml);

        Assert.Contains(doc.DevicePlaceholders, p => p.Name == "Tc2_Utilities" && p.DefaultResolution == "Tc2_Utilities, * (Beckhoff Automation GmbH)");
    }

    // ---- choosing the RemoveReference() arguments (verified live: RemoveReference("Tc2_MC2","*",...) fails, the
    // effective version or the bare name works; placeholders are removed by placeholder name).

    [Fact]
    public void Remove_target_for_placeholder_is_the_placeholder_name()
    {
        var doc = LibraryReferencesXml.Parse(Xml);

        var t = doc.FindForRemoval("Placeholder_NC");

        Assert.Equal(new LibraryId("Placeholder_NC", null, null), t);
    }

    [Fact]
    public void Remove_target_for_ambiguous_library_name_requires_a_version()
    {
        var doc = LibraryReferencesXml.Parse(Xml);

        var ex = Assert.Throws<AmbiguousLibraryException>(() => doc.FindForRemoval("Tc2_MC2"));
        Assert.Contains("Tc2_MC2, 3.3.69.0 (Beckhoff Automation GmbH)", ex.Message);
    }

    [Theory]
    [InlineData("Tc2_MC2, *")]
    [InlineData("Tc2_MC2, * (Beckhoff Automation GmbH)")]
    [InlineData("Tc2_MC2, 3.3.72.0")]
    public void Remove_target_for_star_reference_uses_effective_version(string request)
    {
        var doc = LibraryReferencesXml.Parse(Xml);

        Assert.Equal(new LibraryId("Tc2_MC2", "3.3.72.0", "Beckhoff Automation GmbH"), doc.FindForRemoval(request));
    }

    [Fact]
    public void Remove_target_for_fixed_version_reference()
    {
        var doc = LibraryReferencesXml.Parse(Xml);

        Assert.Equal(new LibraryId("Tc2_MC2", "3.3.69.0", "Beckhoff Automation GmbH"), doc.FindForRemoval("Tc2_MC2, 3.3.69.0"));
    }

    [Fact]
    public void Remove_target_is_null_when_not_referenced()
    {
        Assert.Null(LibraryReferencesXml.Parse(Xml).FindForRemoval("Tc2_Utilities"));
    }
}
