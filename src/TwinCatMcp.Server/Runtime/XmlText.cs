using System.Globalization;
using System.Security;
using System.Xml.Linq;

namespace TwinCatMcp.Runtime;

/// <summary>Small helpers for reading the XML returned by ITcSmTreeItem.ProduceXml().</summary>
internal static class XmlText
{
    public static XElement Root(string xml) => XDocument.Parse(xml).Root ?? throw new FormatException("Empty XML document.");

    public static string? Str(this XElement? e, string name) => e?.Element(name)?.Value;

    public static int? Int(this XElement? e, string name)
        => int.TryParse(e.Str(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    public static uint? UInt(this XElement? e, string name)
        => uint.TryParse(e.Str(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    public static bool? Bool(this XElement? e, string name)
        => bool.TryParse(e.Str(name), out var v) ? v : null;

    public static string Escape(string s) => SecurityElement.Escape(s) ?? "";

    public static string B(bool b) => b ? "true" : "false";

    public static string N(long v) => v.ToString(CultureInfo.InvariantCulture);
}
