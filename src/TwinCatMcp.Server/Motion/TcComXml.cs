using System.Globalization;
using System.Xml.Linq;
using TwinCatMcp.Core;

namespace TwinCatMcp.Motion;

public sealed record TcComParameter(string Name, string? Type, string? Value, bool IsString, int? ContextId);

public sealed record TcComContext(int Id, string? Name, uint? TaskObjectId, int? Priority, int? CycleTimeTicks);

public sealed record TcComDataArea(string Name, string? AreaType, int? ContextId, List<string> Symbols);

public sealed record TcComModuleInfo(
    string Name,
    string Path,
    string? ClassName,
    string? ClassId,
    string? ClassFactory,
    uint? ObjectId,
    List<TcComParameter> Parameters,
    List<TcComContext> Contexts,
    List<TcComDataArea> DataAreas);

/// <summary>XML of TcCOM module instances (&lt;TreeItem&gt;&lt;TcModuleInstance&gt;&lt;Module&gt;...).</summary>
public static class TcComXml
{
    public static TcComModuleInfo Read(string produceXml)
    {
        var root = XDocument.Parse(produceXml).Root ?? throw new TcException("Empty TcCOM XML.");
        var module = root.Element("TcModuleInstance")?.Element("Module")
                     ?? throw new TcException("The item is not a TcCOM module instance (no <TcModuleInstance><Module>).")
                     {
                         Hint = "Use tccom_list_objects to find TcCOM object paths below 'TIRC^TcCOM Objects'.",
                     };

        var values = module.Element("ParameterValues")?.Elements("Value")
                         .Where(v => v.Element("Name") is not null)
                         .GroupBy(v => v.Element("Name")!.Value)
                         .ToDictionary(g => g.Key, g => g.First())
                     ?? new Dictionary<string, XElement>();

        var parameters = module.Element("Parameters")?.Elements("Parameter").Select(p =>
        {
            var name = p.Element("Name")?.Value ?? "";
            var type = p.Element("BaseType")?.Value;
            values.TryGetValue(name, out var v);
            var stringValue = v?.Element("String");
            var plainValue = v?.Element("Value");
            var isString = stringValue is not null || (type?.StartsWith("STRING", StringComparison.OrdinalIgnoreCase) ?? false);
            return new TcComParameter(name, type, stringValue?.Value ?? plainValue?.Value, isString, Int(p.Element("ContextId")));
        }).ToList() ?? [];

        var contexts = module.Element("Contexts")?.Elements("Context").Select(c =>
        {
            var otcid = c.Descendants("OTCID").FirstOrDefault() ?? c.Element("ManualConfig")?.Element("OTCID");
            return new TcComContext(
                Id: Int(c.Element("Id")) ?? 0,
                Name: c.Element("Name")?.Value,
                TaskObjectId: otcid is null || string.IsNullOrWhiteSpace(otcid.Value) ? null : MotionUnits.ParseTcUInt(otcid.Value),
                Priority: Int(c.Element("Priority")),
                CycleTimeTicks: Int(c.Element("CycleTime")));
        }).ToList() ?? [];

        var areas = module.Element("DataAreas")?.Elements("DataArea").Select(a => new TcComDataArea(
            Name: a.Element("Name")?.Value ?? "",
            AreaType: a.Element("AreaNo")?.Attribute("AreaType")?.Value,
            ContextId: Int(a.Element("ContextId")),
            Symbols: a.Elements("Symbol").Select(s => s.Element("Name")?.Value ?? "").ToList())).ToList() ?? [];

        var clsid = module.Element("CLSID");
        return new TcComModuleInfo(
            Name: root.Element("ItemName")?.Value ?? "",
            Path: root.Element("PathName")?.Value ?? "",
            ClassName: module.Element("Name")?.Value,
            ClassId: NormalizeGuid(module.Attribute("GUID")?.Value ?? clsid?.Value),
            ClassFactory: clsid?.Attribute("ClassFactory")?.Value,
            ObjectId: module.Element("OTCID") is { } o ? MotionUnits.ParseTcUInt(o.Value)
                : root.Element("ObjectId") is { } oid ? MotionUnits.ParseTcUInt(oid.Value) : null,
            Parameters: parameters,
            Contexts: contexts,
            DataAreas: areas);
    }

    /// <summary>Builds a ConsumeXml fragment that only sets the given parameter values.</summary>
    public static string BuildParameterUpdate(TcComModuleInfo module, IReadOnlyDictionary<string, string> values)
    {
        if (values.Count == 0)
        {
            throw new TcException("No parameter values given.");
        }

        var byName = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        foreach (var name in byName.Keys.Where(n => !module.Parameters.Any(x => x.Name.Equals(n, StringComparison.OrdinalIgnoreCase))))
        {
            throw new TcException($"Unknown parameter '{name}' of TcCOM object '{module.Name}'.")
            {
                Hint = "Valid parameters: " + string.Join(", ", module.Parameters.Select(x => x.Name)),
            };
        }

        // Emit in the module's declaration order (same order as ProduceXml).
        var pv = new XElement("ParameterValues");
        foreach (var p in module.Parameters.Where(p => byName.ContainsKey(p.Name)))
        {
            pv.Add(new XElement("Value", new XElement("Name", p.Name), new XElement(p.IsString ? "String" : "Value", byName[p.Name])));
        }

        return new XElement("TreeItem", new XElement("TcModuleInstance", new XElement("Module", pv))).ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>Reads &lt;ObjectId&gt; of any tree item XML (tasks, modules, axes).</summary>
    public static uint ReadObjectId(string produceXml)
    {
        var text = XDocument.Parse(produceXml).Root?.Element("ObjectId")?.Value;
        return string.IsNullOrWhiteSpace(text)
            ? throw new TcException("The item has no ObjectId.") { Hint = "Pass a task path such as 'TIRT^Task 1'." }
            : MotionUnits.ParseTcUInt(text);
    }

    /// <summary>
    /// TcCOM objects appear in the tree as "&lt;instance&gt; (&lt;class&gt;)". Accepts the full tree name or the instance name.
    /// </summary>
    public static string? ResolveChildName(IEnumerable<string> childNames, string requested)
    {
        var names = childNames.ToList();
        return names.FirstOrDefault(n => n.Equals(requested, StringComparison.OrdinalIgnoreCase))
               ?? names.FirstOrDefault(n => n.StartsWith(requested + " (", StringComparison.OrdinalIgnoreCase) && n.EndsWith(')'));
    }

    public static string? NormalizeGuid(string? text)
        => Guid.TryParse(text?.Trim(), out var g) ? g.ToString("B").ToUpperInvariant() : null;

    private static int? Int(XElement? e)
        => e is not null && int.TryParse(e.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
}
