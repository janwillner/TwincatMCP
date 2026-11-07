using TwinCatMcp.Runtime;

namespace TwinCatMcp.Tests.Unit;

public sealed class RuntimeVariantXmlTests
{
    // Sample from Beckhoff "Access to TwinCAT variant management" (ITcSysManager14.ProjectVariantConfig).
    private const string BeckhoffSample =
        "<?xml version=\"1.0\"?><ProjectVariants><Group><Name>Group1</Name><Member>Variant1</Member><Member>Variant2</Member></Group>" +
        "<Group><Name>Group2</Name><Member>Variant2</Member><Member>Variant3</Member></Group><Variant><Name>Variant1</Name></Variant>" +
        "<Variant><Name>Variant2</Name></Variant><Variant><Name>Variant3</Name></Variant></ProjectVariants>";

    [Fact]
    public void Parses_variants_and_groups()
    {
        var cfg = VariantXml.Parse(BeckhoffSample);

        Assert.Equal(["Variant1", "Variant2", "Variant3"], cfg.Variants);
        Assert.Equal(2, cfg.Groups.Count);
        Assert.Equal("Group1", cfg.Groups[0].Name);
        Assert.Equal(["Variant1", "Variant2"], cfg.Groups[0].Members);
    }

    [Fact]
    public void Empty_or_missing_config_has_no_variants()
    {
        Assert.Empty(VariantXml.Parse(null).Variants);
        Assert.Empty(VariantXml.Parse("").Variants);
    }

    [Fact]
    public void Builds_the_Beckhoff_sample_structure()
    {
        var xml = VariantXml.Build(new VariantXml.VariantConfig(
            ["Variant1", "Variant2", "Variant3"],
            [new VariantXml.VariantGroup("Group1", ["Variant1", "Variant2"]), new VariantXml.VariantGroup("Group2", ["Variant2", "Variant3"])]));

        Assert.Equal(BeckhoffSample, xml);
    }
}
