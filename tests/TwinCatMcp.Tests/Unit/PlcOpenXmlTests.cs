using System.Xml.Linq;
using TwinCatMcp.Core;
using TwinCatMcp.Plc;

namespace TwinCatMcp.Tests.Unit;

public sealed class PlcOpenXmlTests
{
    // Trimmed down from a real PlcOpenExport of TwinCAT 3.1.4026 (selection "POUs.Sub.FB_InSub").
    private const string Export = """
        <?xml version="1.0" encoding="utf-8"?>
        <project xmlns="http://www.plcopen.org/xml/tc6_0200">
          <types><dataTypes /><pous><pou name="FB_InSub" pouType="functionBlock" /></pous></types>
          <addData>
            <data name="http://www.3s-software.com/plcopenxml/projectstructure" handleUnknown="discard">
              <ProjectStructure>
                <Object Name="FB_InSub" ObjectId="fd64738b-e6e1-4ba3-b8c8-4fba7e1925ec" />
              </ProjectStructure>
            </data>
          </addData>
        </project>
        """;

    private const string FolderExport = """
        <project xmlns="http://www.plcopen.org/xml/tc6_0200">
          <addData>
            <data name="http://www.3s-software.com/plcopenxml/projectstructure" handleUnknown="discard">
              <ProjectStructure>
                <Folder Name="Sub">
                  <Object Name="FB_InSub" ObjectId="1" />
                  <Object Name="FB_Other" ObjectId="2"><Object Name="M_Run" ObjectId="3" /></Object>
                </Folder>
              </ProjectStructure>
            </data>
          </addData>
        </project>
        """;

    [Fact]
    public void Moves_top_level_objects_into_the_target_folder_chain()
    {
        var xml = PlcOpenXml.PlaceInFolder(Export, ["POUs", "Sub"]);

        var ns = XNamespace.Get("http://www.plcopen.org/xml/tc6_0200");
        var ps = XDocument.Parse(xml).Descendants(ns + "ProjectStructure").Single();
        var pous = Assert.Single(ps.Elements());
        Assert.Equal(("Folder", "POUs"), (pous.Name.LocalName, (string?)pous.Attribute("Name")));
        var sub = Assert.Single(pous.Elements());
        Assert.Equal("Sub", (string?)sub.Attribute("Name"));
        var obj = Assert.Single(sub.Elements());
        Assert.Equal(("Object", "FB_InSub"), (obj.Name.LocalName, (string?)obj.Attribute("Name")));
    }

    [Fact]
    public void Empty_folder_chain_keeps_objects_at_project_root()
    {
        var xml = PlcOpenXml.PlaceInFolder(Export, []);

        Assert.Equal(["FB_InSub"], PlcOpenXml.ListObjects(xml));
    }

    [Fact]
    public void Lists_objects_with_their_folder_paths_without_sub_objects()
    {
        Assert.Equal(["Sub/FB_InSub", "Sub/FB_Other"], PlcOpenXml.ListObjects(FolderExport));
    }

    [Fact]
    public void Missing_project_structure_is_reported()
    {
        Assert.Throws<TcException>(() => PlcOpenXml.PlaceInFolder("<project xmlns=\"http://www.plcopen.org/xml/tc6_0200\" />", ["POUs"]));
    }

    [Theory]
    [InlineData("error", 0)]
    [InlineData("none", 0)]
    [InlineData("replace", 1)]
    [InlineData("rename", 2)]
    [InlineData("skip", 3)]
    public void Conflict_options_map_to_PLCIMPORTOPTIONS(string option, int value)
    {
        // Verified live: 2 creates FB_X_1 (rename), 1 replaces, 3 skips, 0 fails with "Import conflict!".
        Assert.Equal(value, PlcOpenXml.ParseImportOption(option));
    }
}
