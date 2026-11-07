using TwinCatMcp.Io;

namespace TwinCatMcp.Tests.Unit;

public sealed class IoProductTypeTests
{
    [Theory]
    [InlineData("EL1008", "EL1008", null, null)]
    [InlineData("el1008 ", "EL1008", null, null)]
    [InlineData("EL1008-0000-0018", "EL1008", 0, 18)]
    [InlineData("EK1100-0000-0017", "EK1100", 0, 17)]
    [InlineData("EL3102-0010", "EL3102", 10, null)]
    [InlineData("EL1004-0010-0017", "EL1004", 10, 17)]
    public void Parses_short_and_full_Beckhoff_product_strings(string input, string baseName, int? variant, int? revision)
    {
        var p = ProductType.Parse(input);

        Assert.Equal(baseName, p.BaseName);
        Assert.Equal(variant, p.Variant);
        Assert.Equal(revision, p.Revision);
    }

    [Theory]
    // Values from the ESI files and the TwinCAT XML description (<ProductRevision>EL1008-0000-0018</ProductRevision>, RevisionNo 1179648 = #x00120000).
    [InlineData("EL1008", 0x00120000u, "EL1008-0000-0018")]
    [InlineData("EK1100", 0x00110000u, "EK1100-0000-0017")]
    [InlineData("EL1004-0010", 0x0011000Au, "EL1004-0010-0017")]
    [InlineData("EL7041", 1703936u, "EL7041-0000-0026")]
    public void Formats_product_revision_from_ESI_type_and_revision_number(string esiType, uint revisionNo, string expected)
        => Assert.Equal(expected, ProductType.FormatRevision(esiType, revisionNo));

    [Fact]
    public void Identity_string_uses_hex_vendor_product_and_revision()
        // Format documented for ITcSmTreeItem::CreateChild vInfo, e.g. "V00000002_P044c2c52_R00000000".
        => Assert.Equal("V00000002_P03F03052_R00120000", ProductType.IdentityString(2, 0x03f03052, 0x00120000));

    [Theory]
    [InlineData("#x03f03052", 0x03f03052u)]
    [InlineData("#X03F03052", 0x03f03052u)]
    [InlineData("2", 2u)]
    [InlineData(" 66072658 ", 66072658u)]
    [InlineData("0x10", 16u)]
    public void Parses_ESI_numbers(string text, uint expected)
        => Assert.Equal(expected, ProductType.ParseEsiNumber(text));

    [Theory]
    [InlineData("EL6001", 9101)]
    [InlineData("EL6002-0000-0018", 9101)]
    [InlineData("EL6021", 9103)]
    [InlineData("EL6731-0010", 9096)]
    [InlineData("EL6731", 9093)]
    [InlineData("BK1120", 9081)]
    [InlineData("EL1008", 9099)]
    [InlineData("EK1100", 9099)]
    public void Box_sub_type_honours_documented_exceptions(string product, int expected)
        => Assert.Equal(expected, ProductType.BoxSubType(product));

    [Theory]
    [InlineData(2, "EL1008-0000-0018", "Term 2 (EL1008)")]
    [InlineData(1, "EK1100", "Term 1 (EK1100)")]
    [InlineData(5, "EL3102-0010", "Term 5 (EL3102-0010)")]
    public void Default_terminal_name_follows_XAE_convention(int index, string product, string expected)
        => Assert.Equal(expected, ProductType.DefaultBoxName(index, product));
}
