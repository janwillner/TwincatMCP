using System.Globalization;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using TwinCatMcp.Core;

namespace TwinCatMcp.Motion;

public enum AxisParamKind
{
    Number,
    Integer,
    Bool,
    Text,
}

/// <summary>One LLM-friendly axis parameter and where it lives below &lt;TreeItem&gt;&lt;NcAxisDef&gt;.</summary>
public sealed record AxisParameterDef(string Name, string XPath, AxisParamKind Kind, string Description);

/// <summary>Summary of an NC axis tree item (from its ProduceXml).</summary>
public sealed record AxisSummary(
    string Name,
    string Path,
    int SubType,
    string? SubTypeName,
    uint? ObjectId,
    bool Disabled,
    int? AxisType,
    string? AxisIoType,
    string? IoLink,
    int? IoChannel,
    int? EncoderType,
    int? DriveType,
    int? ControllerType);

/// <summary>
/// Maps friendly axis parameter names to the NC axis XML of the TwinCAT Automation Interface
/// (ITcSmTreeItem.ProduceXml / ConsumeXml of TINC^...^Axes^Axis n, element NcAxisDef) and back.
/// The element names and the order below were captured from TwinCAT 3.1.4026.22.
/// </summary>
public static class AxisXml
{
    /// <summary>Catalog in the element order of ProduceXml (the order matters for the generated fragments).</summary>
    public static readonly IReadOnlyList<AxisParameterDef> Parameters =
    [
        new("unit", "AxisParam/Unit", AxisParamKind.Text, "Position unit shown in XAE, e.g. 'mm' or 'deg' (does not scale anything)."),
        new("acceleration", "AxisParam/Dynamic/Acceleration", AxisParamKind.Number, "Default acceleration [unit/s^2]."),
        new("deceleration", "AxisParam/Dynamic/Deceleration", AxisParamKind.Number, "Default deceleration [unit/s^2]."),
        new("jerk", "AxisParam/Dynamic/Jerk", AxisParamKind.Number, "Default jerk [unit/s^3]."),
        new("homingVelocityTowardsCam", "AxisParam/Velo/RefSearch", AxisParamKind.Number, "Homing velocity towards the PLC cam [unit/s]."),
        new("homingVelocityOffCam", "AxisParam/Velo/RefSync", AxisParamKind.Number, "Homing velocity off the PLC cam [unit/s]."),
        new("manualVelocitySlow", "AxisParam/Velo/SlowManual", AxisParamKind.Number, "Manual (jog) velocity slow [unit/s]."),
        new("manualVelocityFast", "AxisParam/Velo/FastManual", AxisParamKind.Number, "Manual (jog) velocity fast [unit/s]."),
        new("rapidTraverseVelocity", "AxisParam/Velo/Fast", AxisParamKind.Number, "Rapid traverse velocity (G0) [unit/s]."),
        new("maxVelocity", "AxisParam/Velo/Maximum", AxisParamKind.Number, "Maximum permitted velocity [unit/s]."),
        new("positionRangeMonitoring", "AxisParam/PositionAreaControl/Enable", AxisParamKind.Bool, "Position range monitoring on/off."),
        new("positionRangeWindow", "AxisParam/PositionAreaControl/Range", AxisParamKind.Number, "Position range window [unit]."),
        new("targetPositionMonitoring", "AxisParam/TargetPosControl/Enable", AxisParamKind.Bool, "Target position (in-target) monitoring on/off."),
        new("targetPositionWindow", "AxisParam/TargetPosControl/Range", AxisParamKind.Number, "Target position window [unit]."),
        new("targetPositionTime", "AxisParam/TargetPosControl/Time", AxisParamKind.Number, "Target position monitoring time [s]."),
        new("scalingFactor", "Encoder/EncParam/ScaleFactorNumerator", AxisParamKind.Number, "Encoder scaling factor numerator [unit/INC]."),
        new("scalingFactorDenominator", "Encoder/EncParam/ScaleFactorDenominator", AxisParamKind.Number, "Encoder scaling factor denominator (default 1)."),
        new("encoderOffset", "Encoder/EncParam/Offset", AxisParamKind.Number, "Position bias / encoder offset [unit]."),
        new("moduloFactor", "Encoder/EncParam/ModuloFactor", AxisParamKind.Number, "Modulo factor [unit], e.g. 360 for a rotary axis."),
        new("encoderInverse", "Encoder/EncParam/Inverse", AxisParamKind.Bool, "Invert encoder counting direction."),
        new("encoderMode", "Encoder/EncParam/EncMode", AxisParamKind.Integer, "Encoder evaluation mode as stored in the XML (raw integer)."),
        new("softLimitMinEnabled", "Encoder/EncParam/SoftEndMinControl/Enable", AxisParamKind.Bool, "Minimum software end position monitoring on/off."),
        new("softLimitMin", "Encoder/EncParam/SoftEndMinControl/Range", AxisParamKind.Number, "Minimum software end position [unit]."),
        new("softLimitMaxEnabled", "Encoder/EncParam/SoftEndMaxControl/Enable", AxisParamKind.Bool, "Maximum software end position monitoring on/off."),
        new("softLimitMax", "Encoder/EncParam/SoftEndMaxControl/Range", AxisParamKind.Number, "Maximum software end position [unit]."),
        new("driveInverse", "Drive/DrvParam/Inverse", AxisParamKind.Bool, "Invert motor polarity / output direction."),
        new("referenceVelocity", "Drive/DrvParam/Analog/VeloReferenz", AxisParamKind.Number, "Reference velocity of the drive [unit/s] (velocity at 100 % output)."),
        new("outputAtReferenceVelocity", "Drive/DrvParam/Analog/OutputReferenz", AxisParamKind.Number, "Output ratio at reference velocity (0..1, default 1)."),
        new("positionLagMonitoring", "Controller/CtrParam/PosDiffControl/Enable", AxisParamKind.Bool, "Position lag (following error) monitoring on/off."),
        new("positionLagMax", "Controller/CtrParam/PosDiffControl/Range", AxisParamKind.Number, "Maximum position lag [unit]."),
        new("positionLagFilterTime", "Controller/CtrParam/PosDiffControl/Time", AxisParamKind.Number, "Position lag filter time [s]."),
        new("positionControlKp", "Controller/CtrParam/PID/PosKp", AxisParamKind.Number, "Position controller proportional gain Kv [(unit/s)/unit]."),
    ];

    private static readonly Dictionary<string, AxisParameterDef> ByName =
        Parameters.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

    public static string ValidNames => string.Join(", ", Parameters.Select(p => p.Name));

    public static AxisParameterDef Find(string name)
        => ByName.TryGetValue(name.Trim(), out var def)
            ? def
            : throw new TcException($"Unknown axis parameter '{name}'.") { Hint = "Valid parameters: " + ValidNames };

    // ------------------------------------------------------------------ reading

    public static Dictionary<string, object?> ReadParameters(string produceXml)
    {
        var def = NcAxisDef(produceXml);
        var result = new Dictionary<string, object?>();
        foreach (var p in Parameters)
        {
            var el = Element(def, p.XPath);
            result[p.Name] = el is null ? null : Convert(p, el.Value);
        }

        return result;
    }

    public static AxisSummary ReadSummary(string produceXml)
    {
        var root = XDocument.Parse(produceXml).Root ?? throw new TcException("Empty axis XML.");
        var def = root.Element("NcAxisDef");
        var io = def?.Element("IoItem");
        var ioPath = io?.Element("PathName")?.Value;
        return new AxisSummary(
            Name: root.Element("ItemName")?.Value ?? "",
            Path: root.Element("PathName")?.Value ?? "",
            SubType: IntOrNull(root.Element("ItemSubType")) ?? 0,
            SubTypeName: NullIfEmpty(root.Element("ItemSubTypeName")?.Value),
            ObjectId: root.Element("ObjectId") is { } oid ? MotionUnits.ParseTcUInt(oid.Value) : null,
            Disabled: string.Equals(root.Element("Disabled")?.Value, "true", StringComparison.OrdinalIgnoreCase),
            AxisType: IntOrNull(def?.Element("AxisType")),
            AxisIoType: NullIfEmpty(def?.Element("AxisIoType")?.Value),
            IoLink: NullIfEmpty(ioPath),
            IoChannel: string.IsNullOrEmpty(ioPath) ? null : IntOrNull(io?.Element("Channel")),
            EncoderType: IntOrNull(def?.Element("Encoder")?.Element("EncType")),
            DriveType: IntOrNull(def?.Element("Drive")?.Element("DrvType")),
            ControllerType: IntOrNull(def?.Element("Controller")?.Element("CtrType")));
    }

    /// <summary>Returns one section of NcAxisDef (AxisParam, Encoder, Drive, Controller) or the whole document.</summary>
    public static string ExtractSection(string produceXml, string? section)
    {
        if (string.IsNullOrWhiteSpace(section) || section.Equals("full", StringComparison.OrdinalIgnoreCase))
        {
            return produceXml;
        }

        var def = NcAxisDef(produceXml);
        var el = def.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(section, StringComparison.OrdinalIgnoreCase))
                 ?? throw new TcException($"Section '{section}' not found in the axis XML.")
                 {
                     Hint = "Use one of: full, " + string.Join(", ", def.Elements().Select(e => e.Name.LocalName).Distinct()),
                 };
        return el.ToString(SaveOptions.DisableFormatting);
    }

    // ------------------------------------------------------------------ writing

    /// <summary>Builds a minimal ConsumeXml fragment that only contains the given parameters.</summary>
    public static string BuildParameterUpdate(IReadOnlyDictionary<string, object?> changes)
    {
        if (changes.Count == 0)
        {
            throw new TcException("No parameters given.") { Hint = "Valid parameters: " + ValidNames };
        }

        var values = new Dictionary<AxisParameterDef, string>();
        foreach (var (name, value) in changes)
        {
            var def = Find(name);
            values[def] = FormatValue(def, value);
        }

        var ncAxisDef = new XElement("NcAxisDef");
        foreach (var def in Parameters.Where(values.ContainsKey))
        {
            var node = ncAxisDef;
            foreach (var part in def.XPath.Split('/'))
            {
                var child = node.Element(part);
                if (child is null)
                {
                    child = new XElement(part);
                    node.Add(child);
                }

                node = child;
            }

            node.Value = values[def];
        }

        return new XElement("TreeItem", ncAxisDef).ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>ConsumeXml fragment that links the axis to an I/O box/terminal (same as "Link To I/O..." in XAE).</summary>
    public static string BuildIoLink(string ioPath, int channel)
        => "<TreeItem><NcAxisDef><IoItem><PathName>" + SecurityElement.Escape(ioPath) + "</PathName><Channel>" +
           channel.ToString(CultureInfo.InvariantCulture) + "</Channel></IoItem></NcAxisDef></TreeItem>";

    public static string FormatValue(AxisParameterDef def, object? value)
    {
        var raw = value switch
        {
            null => throw new TcException($"Parameter '{def.Name}' needs a value."),
            JsonElement { ValueKind: JsonValueKind.String } je => je.GetString()!,
            JsonElement { ValueKind: JsonValueKind.True } => "true",
            JsonElement { ValueKind: JsonValueKind.False } => "false",
            JsonElement { ValueKind: JsonValueKind.Number } je => je.GetRawText(),
            JsonElement je => throw new TcException($"Parameter '{def.Name}': unsupported JSON value {je.GetRawText()}."),
            bool b => b ? "true" : "false",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "",
        };

        switch (def.Kind)
        {
            case AxisParamKind.Text:
                return raw;
            case AxisParamKind.Bool:
                return raw.Trim().ToLowerInvariant() switch
                {
                    "true" or "1" or "on" or "yes" => "true",
                    "false" or "0" or "off" or "no" => "false",
                    _ => throw new TcException($"Parameter '{def.Name}' expects true/false, got '{raw}'."),
                };
            case AxisParamKind.Integer:
                return int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
                    ? i.ToString(CultureInfo.InvariantCulture)
                    : throw new TcException($"Parameter '{def.Name}' expects an integer, got '{raw}'.");
            default:
                return double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                    ? MotionUnits.FormatNumber(d)
                    : throw new TcException($"Parameter '{def.Name}' expects a number, got '{raw}'.");
        }
    }

    // ------------------------------------------------------------------ helpers

    private static XElement NcAxisDef(string produceXml)
        => XDocument.Parse(produceXml).Root?.Element("NcAxisDef")
           ?? throw new TcException("The XML does not contain <NcAxisDef>; the item is not an NC axis.")
           {
               Hint = "Pass the path of an axis, e.g. 'TINC^NC-Task 1 SAF^Axes^Axis 1' (see motion_list_axes).",
           };

    private static XElement? Element(XElement root, string xpath)
    {
        XElement? node = root;
        foreach (var part in xpath.Split('/'))
        {
            node = node?.Element(part);
        }

        return node;
    }

    private static object? Convert(AxisParameterDef def, string text) => def.Kind switch
    {
        AxisParamKind.Text => text,
        AxisParamKind.Bool => string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) || text == "1",
        AxisParamKind.Integer => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : text,
        _ => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : text,
    };

    private static int? IntOrNull(XElement? e)
        => e is not null && int.TryParse(e.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
