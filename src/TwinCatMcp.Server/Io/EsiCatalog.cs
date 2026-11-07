using System.Xml;

namespace TwinCatMcp.Io;

/// <summary>One &lt;Device&gt; (one revision) of an EtherCAT Slave Information (ESI) file.</summary>
public sealed record EsiDevice(
    string Type,
    string Name,
    uint VendorId,
    string VendorName,
    uint ProductCode,
    uint RevisionNo,
    string? Group,
    string File,
    bool Invisible = false)
{
    /// <summary>
    /// Ordering key for "latest revision": visible before invisible, regular revisions before special ones
    /// (Beckhoff ESI files contain e.g. EK1100 #x270b0000 = "-9995", which the XAE does not pick), then highest RevisionNo.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public (bool Visible, bool Regular, uint RevisionNo) Preference => (!Invisible, RevisionNo >> 16 < 9000, RevisionNo);

    /// <summary>Beckhoff product revision ("EL1008-0000-0018"), null for devices without Beckhoff naming.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? ProductRevision => VendorId == ProductType.BeckhoffVendorId && ProductType.IsProductName(Type)
        ? ProductType.FormatRevision(Type, RevisionNo)
        : null;

    /// <summary>The string to pass as CreateChild vInfo: product revision for Beckhoff, identity string otherwise.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string CreateInfo => ProductRevision ?? ProductType.IdentityString(VendorId, ProductCode, RevisionNo);
}

/// <summary>Search result: one device type with all its revisions (latest first).</summary>
public sealed record EsiSearchHit(
    string Type,
    string Name,
    string Vendor,
    uint VendorId,
    string ProductCode,
    string? Group,
    string Latest,
    IReadOnlyList<string> Revisions,
    string File);

/// <summary>Streaming parser for ESI files (only the fields needed for the catalog; PDOs etc. are skipped).</summary>
public static class EsiParser
{
    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Ignore,
        IgnoreComments = true,
        IgnoreWhitespace = true,
        IgnoreProcessingInstructions = true,
        XmlResolver = null,
        CloseInput = false,
    };

    /// <summary>Parses all devices of one ESI file. Broken files yield the devices read before the error.</summary>
    public static IReadOnlyList<EsiDevice> Parse(Stream stream, string fileName)
    {
        var result = new List<EsiDevice>();
        uint vendorId = 0;
        var vendorName = "";
        try
        {
            using var reader = XmlReader.Create(stream, Settings);
            var path = new string[64];
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || reader.Depth >= path.Length)
                {
                    continue;
                }

                var depth = reader.Depth;
                path[depth] = reader.LocalName;
                if (depth == 1 && reader.LocalName == "Vendor")
                {
                    (vendorId, vendorName) = ReadVendor(reader);
                }
                else if (depth == 3 && reader.LocalName == "Device" && path[2] == "Devices" && path[1] == "Descriptions")
                {
                    var device = ReadDevice(reader, vendorId, vendorName, fileName);
                    if (device is not null)
                    {
                        result.Add(device);
                    }
                }
            }
        }
        catch (XmlException)
        {
            // Partially broken ESI file: keep what was read.
        }

        return result;
    }

    private static (uint Id, string Name) ReadVendor(XmlReader reader)
    {
        uint id = 0;
        string? name = null;
        if (reader.IsEmptyElement)
        {
            return (id, "");
        }

        var depth = reader.Depth;
        reader.Read();
        while (!(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth) && !reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.Depth == depth + 1)
            {
                switch (reader.LocalName)
                {
                    case "Id":
                        ProductType.TryParseEsiNumber(reader.ReadElementContentAsString(), out id);
                        continue;
                    case "Name" when name is null:
                        name = reader.ReadElementContentAsString().Trim();
                        continue;
                    default:
                        reader.Skip();
                        continue;
                }
            }

            reader.Read();
        }

        return (id, name ?? "");
    }

    private static EsiDevice? ReadDevice(XmlReader reader, uint vendorId, string vendorName, string fileName)
    {
        if (reader.IsEmptyElement)
        {
            return null;
        }

        string? type = null;
        uint productCode = 0, revisionNo = 0;
        string? english = null, anyName = null, group = null;
        var invisible = reader.GetAttribute("Invisible") is "1" or "true";
        var depth = reader.Depth;
        reader.Read();
        while (!(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth) && !reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.Depth == depth + 1)
            {
                switch (reader.LocalName)
                {
                    case "Type":
                        ProductType.TryParseEsiNumber(reader.GetAttribute("ProductCode"), out productCode);
                        ProductType.TryParseEsiNumber(reader.GetAttribute("RevisionNo"), out revisionNo);
                        type = reader.ReadElementContentAsString().Trim();
                        continue;
                    case "Name":
                        var lcid = reader.GetAttribute("LcId");
                        var text = reader.ReadElementContentAsString().Trim();
                        anyName ??= text;
                        if (lcid == "1033")
                        {
                            english ??= text;
                        }

                        continue;
                    case "GroupType":
                        group = reader.ReadElementContentAsString().Trim();
                        continue;
                    default:
                        reader.Skip();
                        continue;
                }
            }

            reader.Read();
        }

        return string.IsNullOrEmpty(type)
            ? null
            : new EsiDevice(type, english ?? anyName ?? type, vendorId, vendorName, productCode, revisionNo, group, fileName, invisible);
    }
}

/// <summary>Pure search / resolution over a list of parsed ESI devices.</summary>
public static class EsiCatalog
{
    /// <summary>
    /// Resolves a product given as "EL1008" (latest revision), "EL3102-0010" (variant, latest revision),
    /// "EL1008-0000-0018" (exact), an identity string "V00000002_P03F03052_R00120000" or a third-party type text.
    /// </summary>
    public static EsiDevice? Resolve(IReadOnlyList<EsiDevice> devices, string product)
    {
        var text = (product ?? "").Trim();
        if (text.Length == 0)
        {
            return null;
        }

        var exact = devices.Where(d => string.Equals(d.ProductRevision, text, StringComparison.OrdinalIgnoreCase)
                                       || string.Equals(d.CreateInfo, text, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count > 0)
        {
            return exact[0];
        }

        var p = ProductType.Parse(text);
        if (p.IsFull)
        {
            return null; // a specific revision was requested but does not exist
        }

        return devices
            .Where(d => string.Equals(d.Type, p.TypeName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(d.Type, text, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => d.VendorId == ProductType.BeckhoffVendorId)
            .ThenByDescending(d => d.Preference)
            .FirstOrDefault();
    }

    /// <summary>
    /// Searches by product type, description or group. All whitespace separated words of <paramref name="query"/>
    /// must occur (case-insensitive). <paramref name="vendor"/> filters by vendor name substring or vendor id.
    /// </summary>
    public static IReadOnlyList<EsiSearchHit> Search(IReadOnlyList<EsiDevice> devices, string? query, string? vendor, int maxResults)
    {
        var q = (query ?? "").Trim();
        var words = q.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        uint? vendorId = !string.IsNullOrWhiteSpace(vendor) && ProductType.TryParseEsiNumber(vendor, out var vid) ? vid : null;

        return devices
            .Where(d => string.IsNullOrWhiteSpace(vendor)
                        || (vendorId is not null ? d.VendorId == vendorId : d.VendorName.Contains(vendor!.Trim(), StringComparison.OrdinalIgnoreCase)))
            .Where(d =>
            {
                var hay = $"{d.Type} {d.Name} {d.Group}";
                return words.All(w => hay.Contains(w, StringComparison.OrdinalIgnoreCase));
            })
            .GroupBy(d => (d.VendorId, Type: d.Type.ToUpperInvariant()))
            .Select(g =>
            {
                var ordered = g.OrderByDescending(d => d.Preference).ToList();
                var latest = ordered[0];
                return new EsiSearchHit(
                    latest.Type,
                    latest.Name,
                    latest.VendorName,
                    latest.VendorId,
                    $"#x{latest.ProductCode:x8}",
                    latest.Group,
                    latest.CreateInfo,
                    ordered.Select(d => d.CreateInfo).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    latest.File);
            })
            .OrderBy(h => Rank(h.Type, q))
            .ThenBy(h => h.Type, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, maxResults))
            .ToList();
    }

    private static int Rank(string type, string query)
    {
        if (query.Length == 0)
        {
            return 0;
        }

        if (string.Equals(type, query, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (type.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return type.Contains(query, StringComparison.OrdinalIgnoreCase) ? 2 : 3;
    }
}
