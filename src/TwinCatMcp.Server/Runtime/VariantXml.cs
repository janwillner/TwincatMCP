using System.Text;

namespace TwinCatMcp.Runtime;

/// <summary>Project variant configuration (ITcSysManager14.ProjectVariantConfig XML).</summary>
public static class VariantXml
{
    public sealed record VariantGroup(string Name, List<string> Members);

    public sealed record VariantConfig(List<string> Variants, List<VariantGroup> Groups);

    public static VariantConfig Parse(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return new VariantConfig([], []);
        }

        var root = XmlText.Root(xml);
        return new VariantConfig(
            root.Elements("Variant").Select(v => v.Str("Name") ?? "").Where(n => n.Length > 0).ToList(),
            root.Elements("Group").Select(g => new VariantGroup(g.Str("Name") ?? "", g.Elements("Member").Select(m => m.Value).ToList())).ToList());
    }

    public static string Build(VariantConfig cfg)
    {
        var sb = new StringBuilder("<?xml version=\"1.0\"?><ProjectVariants>");
        foreach (var g in cfg.Groups)
        {
            sb.Append("<Group><Name>").Append(XmlText.Escape(g.Name)).Append("</Name>");
            foreach (var m in g.Members)
            {
                sb.Append("<Member>").Append(XmlText.Escape(m)).Append("</Member>");
            }

            sb.Append("</Group>");
        }

        foreach (var v in cfg.Variants)
        {
            sb.Append("<Variant><Name>").Append(XmlText.Escape(v)).Append("</Name></Variant>");
        }

        return sb.Append("</ProjectVariants>").ToString();
    }
}
