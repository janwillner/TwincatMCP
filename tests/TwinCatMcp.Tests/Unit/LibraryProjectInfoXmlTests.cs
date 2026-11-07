using TwinCatMcp.Libraries;

namespace TwinCatMcp.Tests.Unit;

public sealed class LibraryProjectInfoXmlTests
{
    // Trimmed live ProduceXml() of "TIPC^Plc1^Plc1 Project" (4026.22) after ConsumeXml of Company/Title/Version/Author/Description.
    private const string Xml = """
        <TreeItem><ItemName>Plc1 Project</ItemName><PathName>TIPC^Plc1^Plc1 Project</PathName><ItemType>600</ItemType><IECProjectDef><ProjectInfo><Company>Acme</Company><Title>AcmeLib</Title><Version>1.2.3.4</Version><LibraryCategories /><LibraryCategoryIds /><DefaultNamespace /><Author>Bot</Author><Description>Test lib</Description><Released /><DocFormat /><AllowChecks /><LanguageModelAttribute /></ProjectInfo><CompilerSettings><ReplaceConstants>false</ReplaceConstants></CompilerSettings></IECProjectDef></TreeItem>
        """;

    [Fact]
    public void Parses_all_project_info_fields_as_dictionary()
    {
        var info = LibraryProjectInfoXml.Parse(Xml);

        Assert.Equal("Acme", info["Company"]);
        Assert.Equal("AcmeLib", info["Title"]);
        Assert.Equal("1.2.3.4", info["Version"]);
        Assert.Equal("Bot", info["Author"]);
        Assert.Null(info["DefaultNamespace"]);
        Assert.True(info.ContainsKey("Released"));
    }

    [Fact]
    public void Builds_minimal_consume_fragment_with_escaping()
    {
        var xml = LibraryProjectInfoXml.BuildConsumeXml(new Dictionary<string, string?>
        {
            ["Company"] = "A & B <GmbH>",
            ["Version"] = "1.0.0.0",
            ["Released"] = "true",
        });

        Assert.Equal(
            "<TreeItem><IECProjectDef><ProjectInfo><Company>A &amp; B &lt;GmbH&gt;</Company><Version>1.0.0.0</Version><Released>true</Released></ProjectInfo></IECProjectDef></TreeItem>",
            xml);
    }

    [Fact]
    public void Null_values_are_skipped_and_empty_strings_clear_the_field()
    {
        var xml = LibraryProjectInfoXml.BuildConsumeXml(new Dictionary<string, string?> { ["Title"] = null, ["Author"] = "" });

        Assert.Equal("<TreeItem><IECProjectDef><ProjectInfo><Author></Author></ProjectInfo></IECProjectDef></TreeItem>", xml);
    }

    [Fact]
    public void Unknown_field_names_are_rejected()
    {
        var ex = Assert.Throws<ArgumentException>(() => LibraryProjectInfoXml.BuildConsumeXml(new Dictionary<string, string?> { ["Vendor"] = "x" }));
        Assert.Contains("Company", ex.Message);
    }

    [Fact]
    public void Throws_for_xml_without_project_info()
    {
        Assert.Throws<FormatException>(() => LibraryProjectInfoXml.Parse("<TreeItem><ItemName>MAIN</ItemName></TreeItem>"));
    }
}
