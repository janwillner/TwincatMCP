using System.Text.Json;
using System.Text.Json.Nodes;
using TwinCAT.Ads;
using TwinCAT.Ads.SumCommand;
using TwinCAT.TypeSystem;
using TwinCatMcp.Core;

namespace TwinCatMcp.Ads;

public sealed record SymbolValue(string Symbol, string Type, JsonNode? Value, string? Error = null);

/// <summary>Symbol level ADS operations (sum read/write of raw images + JSON conversion, type descriptions).</summary>
public static class AdsSymbolAccess
{
    /// <summary>Largest symbol that is converted to JSON in one go.</summary>
    public const int MaxValueBytes = 1024 * 1024;

    /// <summary>Reads the raw byte images of the symbols with one ADS sum command (falls back to single reads).</summary>
    public static IReadOnlyList<(byte[]? Data, AdsErrorCode Error)> ReadRaw(AdsClient client, IReadOnlyList<ISymbol> symbols)
    {
        foreach (var s in symbols)
        {
            if (s.ByteSize > MaxValueBytes)
            {
                throw new TcException($"Symbol '{s.InstancePath}' is too large ({s.ByteSize} bytes) to read as JSON.")
                {
                    Hint = "Read individual members / elements (e.g. MAIN.aBuffer[0]) or use ads_read_raw.",
                };
            }
        }

        var result = new SumSymbolRead(client, symbols.ToList()).ReadRaw();
        if (result.ValueResults is null || result.ValueResults.Length != symbols.Count)
        {
            throw AdsSessionManager.ToTcException(result.ErrorCode, "Sum read");
        }

        return result.ValueResults
            .Select(v => v.ErrorCode == AdsErrorCode.NoError && v.Value is ReadOnlyMemory<byte> mem
                ? (mem.ToArray(), AdsErrorCode.NoError)
                : ((byte[]?)null, v.ErrorCode == AdsErrorCode.NoError ? AdsErrorCode.DeviceInvalidData : v.ErrorCode))
            .ToList();
    }

    public static IReadOnlyList<SymbolValue> ReadValues(AdsClient client, IReadOnlyList<ISymbol> symbols)
    {
        var raw = ReadRaw(client, symbols);
        var list = new List<SymbolValue>(symbols.Count);
        for (var i = 0; i < symbols.Count; i++)
        {
            var s = symbols[i];
            var (data, err) = raw[i];
            list.Add(data is null
                ? new SymbolValue(s.InstancePath, s.TypeName, null, $"ADS error {err} ({(int)err})")
                : new SymbolValue(s.InstancePath, s.TypeName, Decode(s, data)));
        }

        return list;
    }

    public static JsonNode? Decode(ISymbol symbol, ReadOnlySpan<byte> data) =>
        symbol.DataType is null
            ? new JsonObject { ["raw"] = Convert.ToHexString(data) }
            : AdsValueCodec.Decode(symbol.DataType, data);

    /// <summary>Encodes JSON values (merging into the current images) and writes them with one sum command.</summary>
    public static IReadOnlyList<AdsErrorCode> WriteValues(AdsClient client, IReadOnlyList<ISymbol> symbols, IReadOnlyList<JsonElement> values)
    {
        var current = ReadRaw(client, symbols);
        var images = new List<ReadOnlyMemory<byte>>(symbols.Count);
        for (var i = 0; i < symbols.Count; i++)
        {
            var s = symbols[i];
            if (s.DataType is null)
            {
                throw new TcException($"Data type of '{s.InstancePath}' ({s.TypeName}) is unknown - cannot convert the value.") { Hint = "Use ads_write_raw." };
            }

            if (s.IsReadOnly)
            {
                throw new TcException($"Symbol '{s.InstancePath}' is read-only.");
            }

            images.Add(AdsValueCodec.Encode(s.DataType, values[i], current[i].Data ?? []));
        }

        var result = new SumSymbolWrite(client, symbols.ToList()).WriteRaw(images);
        if (result.SubErrors is { Length: > 0 } sub && sub.Length == symbols.Count)
        {
            return sub;
        }

        return Enumerable.Repeat(result.ErrorCode, symbols.Count).ToList();
    }

    // ------------------------------------------------------------ descriptions

    public static object DescribeSymbol(ISymbol s, bool withType)
    {
        var addr = s as IProcessImageAddress;
        return new
        {
            name = s.InstancePath,
            type = s.TypeName,
            category = s.Category.ToString(),
            size = s.ByteSize,
            indexGroup = addr is null ? null : $"0x{addr.IndexGroup:X}",
            indexOffset = addr is null ? null : $"0x{addr.IndexOffset:X}",
            comment = string.IsNullOrEmpty(s.Comment) ? null : s.Comment,
            attributes = Attributes(s.Attributes),
            readOnly = s.IsReadOnly ? true : (bool?)null,
            persistent = s.IsPersistent ? true : (bool?)null,
            reference = s.IsReference ? true : (bool?)null,
            dataType = withType && s.DataType is not null ? DescribeType(s.DataType) : null,
        };
    }

    public static object? Attributes(ITypeAttributeCollection? attributes) =>
        attributes is null || attributes.Count == 0
            ? null
            : attributes.Select(a => new { name = a.Name, value = string.IsNullOrEmpty(a.Value) ? null : a.Value }).ToList();

    public static object DescribeType(IDataType t)
    {
        object? members = null, enumValues = null, dimensions = null, methods = null;
        string? elementType = null, baseType = null;
        int? length = null;

        switch (t)
        {
            case IEnumType e:
                enumValues = e.EnumValues.Select(v => new { name = v.Name, value = Convert.ToInt64(v.Value) }).ToList();
                baseType = e.BaseType?.Name;
                break;
            case IArrayType a:
                dimensions = a.Dimensions.Select(d => new { lower = d.LowerBound, upper = d.LowerBound + d.ElementCount - 1 }).ToList();
                elementType = a.ElementType?.Name ?? a.ElementTypeName;
                break;
            case IInterfaceType i:
                IEnumerable<IMember> ms;
                try
                {
                    ms = i.AllMembers is { Count: > 0 } all ? all : i.Members;
                }
                catch (Exception)
                {
                    ms = i.Members;
                }

                members = ms?.Select(m => new
                {
                    name = m.InstanceName,
                    type = m.TypeName,
                    offset = m.ByteOffset,
                    size = m.ByteSize,
                    comment = string.IsNullOrEmpty(m.Comment) ? null : m.Comment,
                }).ToList();
                baseType = i.BaseTypeName;
                if (t is IRpcCallableType { RpcMethods.Count: > 0 } rpc)
                {
                    methods = rpc.RpcMethods.Select(DescribeMethod).ToList();
                }

                break;
            case IStringType s:
                length = s.Length;
                break;
            case IAliasType al:
                baseType = al.BaseType?.Name ?? al.BaseTypeName;
                break;
            case IPointerType p:
                baseType = p.ReferencedType?.Name ?? p.ReferenceTypeName;
                break;
            case IReferenceType r:
                baseType = r.ReferencedType?.Name ?? r.ReferencedTypeName;
                break;
        }

        return new
        {
            name = t.Name,
            category = t.Category.ToString(),
            size = t.ByteSize,
            baseType = string.IsNullOrEmpty(baseType) ? null : baseType,
            length,
            elementType,
            dimensions,
            members,
            enumValues,
            rpcMethods = methods,
            comment = string.IsNullOrEmpty(t.Comment) ? null : t.Comment,
            attributes = Attributes(t.Attributes),
        };
    }

    public static object DescribeMethod(IRpcMethod m) => new
    {
        name = m.Name,
        returnType = RpcMethodExtension.IsVoid(m) ? null : m.ReturnType,
        parameters = m.Parameters.Select(p => new
        {
            name = p.Name,
            type = p.TypeName,
            direction = RpcMethodParameterExtension.IsInput(p) && RpcMethodParameterExtension.IsOutput(p) ? "inout" : RpcMethodParameterExtension.IsOutput(p) ? "out" : "in",
        }).ToList(),
        comment = string.IsNullOrEmpty(m.Comment) ? null : m.Comment,
    };

    /// <summary>Numeric view of a decoded value (for min/max/comparison) or null.</summary>
    public static double? AsNumber(JsonNode? node)
    {
        switch (node)
        {
            case JsonValue v when v.GetValueKind() == JsonValueKind.Number:
                return double.Parse(v.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture);
            case JsonValue v when v.GetValueKind() is JsonValueKind.True or JsonValueKind.False:
                return v.GetValueKind() == JsonValueKind.True ? 1 : 0;
            case JsonObject o when o["value"] is JsonValue ev && o.ContainsKey("name"):
                return AsNumber(ev);
            default:
                return null;
        }
    }
}
