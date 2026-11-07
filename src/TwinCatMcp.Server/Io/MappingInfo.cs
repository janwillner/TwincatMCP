using System.Xml.Linq;

namespace TwinCatMcp.Io;

/// <summary>One variable link with full tree paths on both sides.</summary>
public sealed record VarLink(string A, string B, string OwnerA, string VarA, string OwnerB, string VarB, int? OffsetA = null, int? OffsetB = null, int? BitSize = null);

/// <summary>Parses / builds the mapping XML of ITcSysManager.ProduceMappingInfo / ConsumeMappingInfo.</summary>
public static class MappingInfo
{
    /// <summary>
    /// Flattens &lt;VarLinks&gt;&lt;OwnerA Name=".."&gt;&lt;OwnerB Name=".."&gt;&lt;Link VarA=".." VarB=".."/&gt; into full paths
    /// (OwnerA^VarA, OwnerB^VarB).
    /// </summary>
    public static IReadOnlyList<VarLink> Parse(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return [];
        }

        var doc = XDocument.Parse(xml);
        var result = new List<VarLink>();
        foreach (var link in doc.Descendants("Link"))
        {
            var ownerB = link.Ancestors("OwnerB").FirstOrDefault();
            var ownerA = link.Ancestors("OwnerA").FirstOrDefault();
            if (ownerA is null || ownerB is null)
            {
                continue;
            }

            var oa = (string?)ownerA.Attribute("Name") ?? "";
            var ob = (string?)ownerB.Attribute("Name") ?? "";
            var va = (string?)link.Attribute("VarA") ?? "";
            var vb = (string?)link.Attribute("VarB") ?? "";
            result.Add(new VarLink(
                Join(oa, va), Join(ob, vb), oa, va, ob, vb,
                Int(link.Attribute("OffsA")), Int(link.Attribute("OffsB")), Int(link.Attribute("Size"))));
        }

        return result;
    }

    /// <summary>Builds VarLinks XML (as accepted by ConsumeMappingInfo) from links grouped by owners.</summary>
    public static string Build(IEnumerable<VarLink> links)
    {
        var root = new XElement("VarLinks");
        foreach (var byA in links.GroupBy(l => l.OwnerA))
        {
            var a = new XElement("OwnerA", new XAttribute("Name", byA.Key));
            foreach (var byB in byA.GroupBy(l => l.OwnerB))
            {
                var b = new XElement("OwnerB", new XAttribute("Name", byB.Key));
                foreach (var l in byB)
                {
                    var e = new XElement("Link", new XAttribute("VarA", l.VarA), new XAttribute("VarB", l.VarB));
                    if (l.OffsetA is not null) e.Add(new XAttribute("OffsA", l.OffsetA));
                    if (l.OffsetB is not null) e.Add(new XAttribute("OffsB", l.OffsetB));
                    if (l.BitSize is not null) e.Add(new XAttribute("Size", l.BitSize));
                    b.Add(e);
                }

                a.Add(b);
            }

            root.Add(a);
        }

        return root.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>All links touching the given variable path (either side).</summary>
    public static IEnumerable<string> LinkedTo(IEnumerable<VarLink> links, string path)
    {
        foreach (var l in links)
        {
            if (string.Equals(l.A, path, StringComparison.OrdinalIgnoreCase))
            {
                yield return l.B;
            }
            else if (string.Equals(l.B, path, StringComparison.OrdinalIgnoreCase))
            {
                yield return l.A;
            }
        }
    }

    private static string Join(string owner, string variable)
        => string.IsNullOrEmpty(owner) ? variable : string.IsNullOrEmpty(variable) ? owner : owner + "^" + variable;

    private static int? Int(XAttribute? a) => a is not null && int.TryParse(a.Value, out var v) ? v : null;
}
