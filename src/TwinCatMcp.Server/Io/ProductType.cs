using System.Globalization;
using System.Text.RegularExpressions;

namespace TwinCatMcp.Io;

/// <summary>A parsed EtherCAT product string such as "EL1008", "EL3102-0010" or "EL1008-0000-0018".</summary>
public sealed record ProductTypeName(string BaseName, int? Variant, int? Revision)
{
    /// <summary>"EL3102-0010" for variants, otherwise "EL1008" (the ESI &lt;Type&gt; text).</summary>
    public string TypeName => Variant is > 0 ? $"{BaseName}-{Variant:D4}" : BaseName;

    public bool IsFull => Variant is not null && Revision is not null;

    public override string ToString() => IsFull ? $"{BaseName}-{Variant:D4}-{Revision:D4}" : TypeName;
}

/// <summary>
/// Normalisation of EtherCAT product strings (Beckhoff "product revision" EL1008-0000-0018 = type-variant-revision),
/// identity strings for CreateChild(vInfo) and the documented E-Bus item sub type exceptions.
/// </summary>
public static partial class ProductType
{
    /// <summary>Generic item sub type of all EtherCAT boxes/terminals (TSM_BOX_TYPE_EXXXXX).</summary>
    public const int GenericEBusSubType = 9099;

    /// <summary>EtherCAT vendor id of Beckhoff Automation.</summary>
    public const uint BeckhoffVendorId = 2;

    // Source: InfoSys "Creating and handling EtherCAT devices" - Exceptions to the ItemSubType 9099.
    private static readonly Dictionary<string, int> BoxSubTypeExceptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EP6002"] = 9101, ["EL6001"] = 9101, ["EL6002"] = 9101, ["EP6001-0002"] = 9101, ["EP6002-0002"] = 9101,
        ["EL6021"] = 9103, ["EL6022"] = 9103, ["EL6021-0021"] = 9103,
        ["BK1120"] = 9081, ["ILXB11"] = 9086,
        ["EL6731"] = 9093, ["EL6751"] = 9094, ["EL6752"] = 9095,
        ["EL6731-0010"] = 9096, ["EL6751-0010"] = 9097, ["EL6752-0010"] = 9098,
        ["EL6601"] = 9100, ["EL6720"] = 9104,
        ["EL6631"] = 9106, ["EL6631-0010"] = 9107, ["EL6632"] = 9108, ["EL6652-0010"] = 9109, ["EL6652"] = 9110,
    };

    [GeneratedRegex(@"^(?<base>[A-Z][A-Z0-9]*?\d[A-Z0-9]*)(?:-(?<variant>\d{4}))?(?:-(?<rev>\d{4}))?$", RegexOptions.IgnoreCase)]
    private static partial Regex ProductRegex();

    /// <summary>Parses "EL1008", "el1008", "EL3102-0010" or "EL1008-0000-0018". Unknown formats are kept as base name.</summary>
    public static ProductTypeName Parse(string input)
    {
        var text = (input ?? "").Trim();
        var m = ProductRegex().Match(text);
        if (!m.Success)
        {
            return new ProductTypeName(text, null, null);
        }

        return new ProductTypeName(
            m.Groups["base"].Value.ToUpperInvariant(),
            m.Groups["variant"].Success ? int.Parse(m.Groups["variant"].Value, CultureInfo.InvariantCulture) : null,
            m.Groups["rev"].Success ? int.Parse(m.Groups["rev"].Value, CultureInfo.InvariantCulture) : null);
    }

    /// <summary>True for Beckhoff-style product names ("EL1008", "EL3102-0010", "EL1008-0000-0018").</summary>
    public static bool IsProductName(string text) => ProductRegex().IsMatch((text ?? "").Trim());

    /// <summary>
    /// Builds the Beckhoff product revision string from the ESI &lt;Type&gt; text and RevisionNo:
    /// low word = variant ("-0010"), high word = revision ("-0018"). E.g. EL1008 + #x00120000 = EL1008-0000-0018.
    /// </summary>
    public static string FormatRevision(string esiType, uint revisionNo)
    {
        var baseName = Parse(esiType).BaseName;
        return $"{baseName}-{revisionNo & 0xFFFF:D4}-{revisionNo >> 16:D4}";
    }

    /// <summary>Identity vInfo for CreateChild (works for any vendor): "V{vendor:X8}_P{product:X8}_R{revision:X8}".</summary>
    public static string IdentityString(uint vendorId, uint productCode, uint revisionNo)
        => $"V{vendorId:X8}_P{productCode:X8}_R{revisionNo:X8}";

    /// <summary>Parses ESI/TwinCAT numbers: "#x03f03052", "0x10" or decimal "2".</summary>
    public static uint ParseEsiNumber(string text)
    {
        var t = (text ?? "").Trim();
        if (t.StartsWith("#x", StringComparison.OrdinalIgnoreCase) || t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return uint.Parse(t[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        return unchecked((uint)long.Parse(t, NumberStyles.Integer, CultureInfo.InvariantCulture));
    }

    public static bool TryParseEsiNumber(string? text, out uint value)
    {
        try
        {
            value = ParseEsiNumber(text ?? "");
            return true;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            value = 0;
            return false;
        }
    }

    /// <summary>Item sub type for CreateChild: 9099 except for the documented special terminals (EL6001 = 9101, ...).</summary>
    public static int BoxSubType(string product)
    {
        var p = Parse(product);
        return BoxSubTypeExceptions.TryGetValue(p.TypeName, out var subType) ? subType : GenericEBusSubType;
    }

    /// <summary>Default tree item name the XAE uses for a new terminal: "Term 2 (EL1008)".</summary>
    public static string DefaultBoxName(int index, string product) => $"Term {index} ({Parse(product).TypeName})";
}
