using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TwinCAT.TypeSystem;
using TwinCatMcp.Core;

namespace TwinCatMcp.Ads;

/// <summary>
/// Converts the raw ADS byte image of a symbol into JSON and back, driven by the symbol's
/// <see cref="IDataType"/> (primitives, TIME/DATE types, STRING/WSTRING, enums, aliases,
/// structs/function blocks/unions, arrays incl. multi-dimensional and arrays of structs).
/// Pure logic - no ADS communication.
/// </summary>
public static class AdsValueCodec
{
    // ---------------------------------------------------------------- decode

    public static JsonNode? Decode(IDataType type, ReadOnlySpan<byte> data)
    {
        if (data.Length < type.ByteSize)
        {
            throw new TcException($"Value of type {type.Name} needs {type.ByteSize} bytes but only {data.Length} were read.");
        }

        switch (type.Category)
        {
            case DataTypeCategory.Enum when type is IEnumType e:
                return DecodeEnum(e, data);
            case DataTypeCategory.Alias when type is IAliasType a && a.BaseType is not null:
                return Decode(a.BaseType, data);
            case DataTypeCategory.SubRange when type is ISubRangeType s && s.BaseType is not null:
                return Decode(s.BaseType, data);
            case DataTypeCategory.String when type is IStringType st:
                return DecodeString(st, data[..type.ByteSize]);
            case DataTypeCategory.Array when type is IArrayType at:
                return DecodeArray(at, data);
            case DataTypeCategory.Struct or DataTypeCategory.FunctionBlock or DataTypeCategory.Program or DataTypeCategory.Union
                when type is IInterfaceType it:
                return DecodeStruct(it, data);
            case DataTypeCategory.Union when type is IUnionType u:
                return DecodeUnion(u, data);
            case DataTypeCategory.Pointer or DataTypeCategory.Reference or DataTypeCategory.Interface:
                return JsonValue.Create(FormatAddress(data[..type.ByteSize]));
        }

        var kind = AdsPrimitives.KindOf(type);
        return kind is null
            ? new JsonObject { ["raw"] = Convert.ToHexString(data[..type.ByteSize]) }
            : DecodePrimitive(kind.Value, data);
    }

    private static JsonNode? DecodePrimitive(PrimitiveKind kind, ReadOnlySpan<byte> d) => kind switch
    {
        PrimitiveKind.Bool => JsonValue.Create(d[0] != 0),
        PrimitiveKind.SInt => JsonValue.Create((sbyte)d[0]),
        PrimitiveKind.USInt => JsonValue.Create(d[0]),
        PrimitiveKind.Int => JsonValue.Create(BinaryPrimitives.ReadInt16LittleEndian(d)),
        PrimitiveKind.UInt => JsonValue.Create(BinaryPrimitives.ReadUInt16LittleEndian(d)),
        PrimitiveKind.DInt => JsonValue.Create(BinaryPrimitives.ReadInt32LittleEndian(d)),
        PrimitiveKind.UDInt => JsonValue.Create(BinaryPrimitives.ReadUInt32LittleEndian(d)),
        PrimitiveKind.LInt => JsonValue.Create(BinaryPrimitives.ReadInt64LittleEndian(d)),
        PrimitiveKind.ULInt => JsonValue.Create(BinaryPrimitives.ReadUInt64LittleEndian(d)),
        PrimitiveKind.Real => JsonValue.Create(BinaryPrimitives.ReadSingleLittleEndian(d)),
        PrimitiveKind.LReal => JsonValue.Create(BinaryPrimitives.ReadDoubleLittleEndian(d)),
        PrimitiveKind.Time => JsonValue.Create(IecTime.FormatTime(BinaryPrimitives.ReadUInt32LittleEndian(d))),
        PrimitiveKind.LTime => JsonValue.Create(IecTime.FormatLTime(BinaryPrimitives.ReadUInt64LittleEndian(d))),
        PrimitiveKind.Date => JsonValue.Create(IecTime.FormatDate(BinaryPrimitives.ReadUInt32LittleEndian(d))),
        PrimitiveKind.LDate => JsonValue.Create(IecTime.FormatLDate(BinaryPrimitives.ReadUInt64LittleEndian(d))),
        PrimitiveKind.Dt => JsonValue.Create(IecTime.FormatDt(BinaryPrimitives.ReadUInt32LittleEndian(d))),
        PrimitiveKind.LDt => JsonValue.Create(IecTime.FormatLDt(BinaryPrimitives.ReadUInt64LittleEndian(d))),
        PrimitiveKind.Tod => JsonValue.Create(IecTime.FormatTod(BinaryPrimitives.ReadUInt32LittleEndian(d))),
        PrimitiveKind.LTod => JsonValue.Create(IecTime.FormatLTod(BinaryPrimitives.ReadUInt64LittleEndian(d))),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static JsonNode DecodeEnum(IEnumType e, ReadOnlySpan<byte> data)
    {
        var baseKind = e.BaseType is null ? KindBySize(e.ByteSize, signed: true) : AdsPrimitives.KindOf(e.BaseType) ?? KindBySize(e.ByteSize, true);
        var raw = ReadInteger(baseKind, data);
        string? name = null;
        foreach (var v in e.EnumValues)
        {
            if (Convert.ToInt64(v.Value, CultureInfo.InvariantCulture) == raw)
            {
                name = v.Name;
                break;
            }
        }

        return new JsonObject { ["name"] = name, ["value"] = raw };
    }

    private static JsonNode DecodeString(IStringType st, ReadOnlySpan<byte> data)
    {
        var isWide = IsWide(st);
        int len;
        if (isWide)
        {
            len = data.Length & ~1;
            for (var i = 0; i + 1 < data.Length; i += 2)
            {
                if (data[i] == 0 && data[i + 1] == 0)
                {
                    len = i;
                    break;
                }
            }
        }
        else
        {
            len = data.IndexOf((byte)0);
            if (len < 0)
            {
                len = data.Length;
            }
        }

        return JsonValue.Create(StringEncoding(st).GetString(data[..len]))!;
    }

    private static JsonNode DecodeArray(IArrayType at, ReadOnlySpan<byte> data)
    {
        var lengths = DimensionLengths(at);
        var elemSize = ElementSize(at, lengths);
        var index = 0;
        return DecodeDim(at.ElementType!, data, lengths, 0, elemSize, ref index);
    }

    private static JsonArray DecodeDim(IDataType elem, ReadOnlySpan<byte> data, int[] lengths, int dim, int elemSize, ref int index)
    {
        var arr = new JsonArray();
        for (var i = 0; i < lengths[dim]; i++)
        {
            if (dim == lengths.Length - 1)
            {
                arr.Add(Decode(elem, data.Slice(index * elemSize, elemSize)));
                index++;
            }
            else
            {
                arr.Add(DecodeDim(elem, data, lengths, dim + 1, elemSize, ref index));
            }
        }

        return arr;
    }

    private static JsonNode DecodeStruct(IInterfaceType it, ReadOnlySpan<byte> data)
    {
        var obj = new JsonObject();
        foreach (var m in Members(it))
        {
            if (m.DataType is null || m.IsStatic)
            {
                continue;
            }

            if (m.IsBitType && m.BitSize == 1 && IsBoolLike(m.DataType))
            {
                var bit = m.BitOffset;
                obj[m.InstanceName] = (data[bit / 8] & (1 << (bit % 8))) != 0;
                continue;
            }

            if (m.ByteOffset + m.DataType.ByteSize > data.Length)
            {
                continue;
            }

            obj[m.InstanceName] = Decode(m.DataType, data.Slice(m.ByteOffset, m.DataType.ByteSize));
        }

        return obj;
    }

    /// <summary>UNION: every field overlays offset 0 - each field is shown with its interpretation of the bytes.</summary>
    private static JsonNode DecodeUnion(IUnionType u, ReadOnlySpan<byte> data)
    {
        var obj = new JsonObject();
        foreach (var f in u.Fields)
        {
            if (f.DataType is not null && f.DataType.ByteSize <= data.Length)
            {
                obj[f.InstanceName] = Decode(f.DataType, data[..f.DataType.ByteSize]);
            }
        }

        return obj;
    }

    // ---------------------------------------------------------------- encode

    /// <summary>
    /// Encodes <paramref name="value"/> into the byte image of <paramref name="type"/>.
    /// <paramref name="current"/> (optional, the current byte image) is used as the base, so partial
    /// struct objects / shorter arrays only change the given members / leading elements.
    /// </summary>
    public static byte[] Encode(IDataType type, JsonElement value, ReadOnlySpan<byte> current = default)
    {
        var buffer = new byte[type.ByteSize];
        if (current.Length >= buffer.Length)
        {
            current[..buffer.Length].CopyTo(buffer);
        }

        EncodeInto(type, value, buffer, type.Name);
        return buffer;
    }

    private static void EncodeInto(IDataType type, JsonElement v, Span<byte> dest, string path)
    {
        switch (type.Category)
        {
            case DataTypeCategory.Enum when type is IEnumType e:
                EncodeEnum(e, v, dest, path);
                return;
            case DataTypeCategory.Alias when type is IAliasType a && a.BaseType is not null:
                EncodeInto(a.BaseType, v, dest, path);
                return;
            case DataTypeCategory.SubRange when type is ISubRangeType s && s.BaseType is not null:
                EncodeInto(s.BaseType, v, dest, path);
                return;
            case DataTypeCategory.String when type is IStringType st:
                EncodeString(st, v, dest, path);
                return;
            case DataTypeCategory.Array when type is IArrayType at:
                EncodeArray(at, v, dest, path);
                return;
            case DataTypeCategory.Struct or DataTypeCategory.FunctionBlock or DataTypeCategory.Program or DataTypeCategory.Union
                when type is IInterfaceType it:
                EncodeStruct(it, v, dest, path);
                return;
            case DataTypeCategory.Union when type is IUnionType u:
                EncodeUnion(u, v, dest, path);
                return;
            case DataTypeCategory.Pointer or DataTypeCategory.Reference or DataTypeCategory.Interface:
                WriteInteger(KindBySize(type.ByteSize, signed: false), ParseUnsigned(v, type, path), dest);
                return;
        }

        var kind = AdsPrimitives.KindOf(type);
        if (kind is null)
        {
            if (v.ValueKind == JsonValueKind.Object && v.TryGetProperty("raw", out var raw) && raw.ValueKind == JsonValueKind.String)
            {
                var bytes = Convert.FromHexString(raw.GetString()!);
                bytes.AsSpan(0, Math.Min(bytes.Length, dest.Length)).CopyTo(dest);
                return;
            }

            throw new TcException($"Writing values of type '{type.Name}' ({type.Category}) is not supported for '{path}'.")
            {
                Hint = "Write its members individually, or pass {\"raw\":\"<hex bytes>\"}.",
            };
        }

        EncodePrimitive(kind.Value, type, v, dest, path);
    }

    private static void EncodePrimitive(PrimitiveKind kind, IDataType type, JsonElement v, Span<byte> dest, string path)
    {
        switch (kind)
        {
            case PrimitiveKind.Bool:
                dest[0] = ParseBool(v, path) ? (byte)1 : (byte)0;
                return;
            case PrimitiveKind.Real:
                BinaryPrimitives.WriteSingleLittleEndian(dest, (float)ParseDouble(v, type, path));
                return;
            case PrimitiveKind.LReal:
                BinaryPrimitives.WriteDoubleLittleEndian(dest, ParseDouble(v, type, path));
                return;
            case PrimitiveKind.Time or PrimitiveKind.LTime or PrimitiveKind.Date or PrimitiveKind.LDate
                or PrimitiveKind.Dt or PrimitiveKind.LDt or PrimitiveKind.Tod or PrimitiveKind.LTod:
                EncodeTime(kind, type, v, dest, path);
                return;
            case PrimitiveKind.ULInt:
                BinaryPrimitives.WriteUInt64LittleEndian(dest, ParseUnsigned(v, type, path));
                return;
            default:
                var (min, max) = Range(kind);
                var n = ParseSigned(v, type, path);
                if (n < min || n > max)
                {
                    throw new TcException($"Value {n} is out of range for {type.Name} at '{path}'.")
                    {
                        Hint = $"{type.Name} accepts {min} .. {max}.",
                    };
                }

                WriteInteger(kind, n, dest);
                return;
        }
    }

    private static void EncodeTime(PrimitiveKind kind, IDataType type, JsonElement v, Span<byte> dest, string path)
    {
        ulong raw;
        if (v.ValueKind == JsonValueKind.Number)
        {
            raw = v.GetUInt64();
        }
        else if (v.ValueKind == JsonValueKind.String)
        {
            var s = v.GetString()!;
            var parsed = kind switch
            {
                PrimitiveKind.Time => IecTime.TryParseDuration(s, out var ns) ? (ulong?)(ns / 1_000_000) : null,
                PrimitiveKind.LTime => IecTime.TryParseDuration(s, out var ns) ? ns : null,
                PrimitiveKind.Tod => IecTime.TryParseTimeOfDay(s, out var ns) ? ns / 1_000_000 : null,
                PrimitiveKind.LTod => IecTime.TryParseTimeOfDay(s, out var ns) ? ns : null,
                PrimitiveKind.Date => IecTime.TryParseDateTime(s, out var ns) ? ns / 1_000_000_000 : null,
                PrimitiveKind.LDate => IecTime.TryParseDateTime(s, out var ns) ? ns : null,
                PrimitiveKind.Dt => IecTime.TryParseDateTime(s, out var ns) ? ns / 1_000_000_000 : null,
                PrimitiveKind.LDt => IecTime.TryParseDateTime(s, out var ns) ? ns : null,
                _ => null,
            };

            if (parsed is null && ulong.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var plain))
            {
                parsed = plain;
            }

            raw = parsed ?? throw new TcException($"'{s}' is not a valid {type.Name} literal for '{path}'.")
            {
                Hint = "Examples: T#1m30s500ms, LTIME#1s, TOD#12:30:00.500, D#2024-01-15, DT#2024-01-15-12:30:00 or a raw number " +
                       "(TIME/TOD in ms, LTIME in ns, DATE/DT in seconds since 1970).",
            };
        }
        else
        {
            throw TypeError(type, path, v);
        }

        if (type.ByteSize == 4)
        {
            if (raw > uint.MaxValue)
            {
                throw new TcException($"Value {raw} is out of range for {type.Name} at '{path}'.");
            }

            BinaryPrimitives.WriteUInt32LittleEndian(dest, (uint)raw);
        }
        else
        {
            BinaryPrimitives.WriteUInt64LittleEndian(dest, raw);
        }
    }

    private static void EncodeEnum(IEnumType e, JsonElement v, Span<byte> dest, string path)
    {
        var baseKind = e.BaseType is null ? KindBySize(e.ByteSize, true) : AdsPrimitives.KindOf(e.BaseType) ?? KindBySize(e.ByteSize, true);
        long raw;
        if (v.ValueKind == JsonValueKind.Object)
        {
            if (v.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
            {
                v = n;
            }
            else if (v.TryGetProperty("value", out var val))
            {
                v = val;
            }
        }

        if (v.ValueKind == JsonValueKind.Number)
        {
            raw = v.GetInt64();
        }
        else if (v.ValueKind == JsonValueKind.String)
        {
            var s = v.GetString()!.Trim();
            var dot = s.LastIndexOf('.');
            var name = dot >= 0 ? s[(dot + 1)..] : s;
            var match = e.EnumValues.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                raw = Convert.ToInt64(match.Value, CultureInfo.InvariantCulture);
            }
            else if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var num))
            {
                raw = num;
            }
            else
            {
                throw new TcException($"'{s}' is not a value of enum {e.Name} ('{path}').")
                {
                    Hint = "Valid names: " + string.Join(", ", e.EnumValues.Select(x => x.Name)),
                };
            }
        }
        else
        {
            throw TypeError(e, path, v);
        }

        WriteInteger(baseKind, raw, dest);
    }

    private static void EncodeString(IStringType st, JsonElement v, Span<byte> dest, string path)
    {
        if (v.ValueKind != JsonValueKind.String)
        {
            throw TypeError(st, path, v);
        }

        var s = v.GetString()!;
        var bytes = StringEncoding(st).GetBytes(s);
        var terminator = IsWide(st) ? 2 : 1;
        if (bytes.Length + terminator > dest.Length)
        {
            throw new TcException($"String is too long for {st.Name} at '{path}' ({s.Length} characters).")
            {
                Hint = $"{st.Name} holds at most {(dest.Length - terminator) / terminator} characters.",
            };
        }

        dest.Clear();
        bytes.CopyTo(dest);
    }

    private static void EncodeArray(IArrayType at, JsonElement v, Span<byte> dest, string path)
    {
        if (v.ValueKind != JsonValueKind.Array)
        {
            throw TypeError(at, path, v);
        }

        var lengths = DimensionLengths(at);
        var elemSize = ElementSize(at, lengths);
        var lower = at.Dimensions.Select(d => d.LowerBound).ToArray();
        EncodeDim(at.ElementType!, v, dest, lengths, lower, 0, 0, elemSize, path);
    }

    private static void EncodeDim(IDataType elem, JsonElement v, Span<byte> dest, int[] lengths, int[] lower, int dim, int baseIndex, int elemSize, string path)
    {
        if (v.ValueKind != JsonValueKind.Array)
        {
            throw new TcException($"Expected a nested JSON array for dimension {dim + 1} at '{path}'.");
        }

        var count = v.GetArrayLength();
        if (count > lengths[dim])
        {
            throw new TcException($"Too many elements at '{path}': {count} given, dimension {dim + 1} has {lengths[dim]}.");
        }

        var stride = 1;
        for (var d = dim + 1; d < lengths.Length; d++)
        {
            stride *= lengths[d];
        }

        var i = 0;
        foreach (var item in v.EnumerateArray())
        {
            var index = baseIndex + (i * stride);
            var itemPath = $"{path}[{lower[dim] + i}]";
            if (dim == lengths.Length - 1)
            {
                EncodeInto(elem, item, dest.Slice(index * elemSize, elemSize), itemPath);
            }
            else
            {
                EncodeDim(elem, item, dest, lengths, lower, dim + 1, index, elemSize, itemPath);
            }

            i++;
        }
    }

    private static void EncodeStruct(IInterfaceType it, JsonElement v, Span<byte> dest, string path)
    {
        if (v.ValueKind != JsonValueKind.Object)
        {
            throw TypeError(it, path, v);
        }

        var members = Members(it).Where(m => m.DataType is not null && !m.IsStatic).ToList();
        foreach (var prop in v.EnumerateObject())
        {
            var m = members.FirstOrDefault(x => string.Equals(x.InstanceName, prop.Name, StringComparison.OrdinalIgnoreCase))
                    ?? throw new TcException($"'{prop.Name}' is not a member of {it.Name} ('{path}').")
                    {
                        Hint = "Members: " + string.Join(", ", members.Select(x => x.InstanceName)),
                    };

            var memberPath = $"{path}.{m.InstanceName}";
            if (m.IsBitType && m.BitSize == 1 && IsBoolLike(m.DataType!))
            {
                var bit = m.BitOffset;
                if (ParseBool(prop.Value, memberPath))
                {
                    dest[bit / 8] |= (byte)(1 << (bit % 8));
                }
                else
                {
                    dest[bit / 8] &= (byte)~(1 << (bit % 8));
                }

                continue;
            }

            EncodeInto(m.DataType!, prop.Value, dest.Slice(m.ByteOffset, m.DataType!.ByteSize), memberPath);
        }
    }

    private static void EncodeUnion(IUnionType u, JsonElement v, Span<byte> dest, string path)
    {
        if (v.ValueKind != JsonValueKind.Object || v.EnumerateObject().Count() != 1)
        {
            throw new TcException($"A UNION ({u.Name}) at '{path}' takes an object with exactly one field.")
            {
                Hint = "Fields: " + string.Join(", ", u.Fields.Select(f => f.InstanceName)),
            };
        }

        var prop = v.EnumerateObject().First();
        var field = u.Fields.FirstOrDefault(f => string.Equals(f.InstanceName, prop.Name, StringComparison.OrdinalIgnoreCase))
                    ?? throw new TcException($"'{prop.Name}' is not a field of {u.Name} ('{path}').")
                    {
                        Hint = "Fields: " + string.Join(", ", u.Fields.Select(f => f.InstanceName)),
                    };
        EncodeInto(field.DataType!, prop.Value, dest[..field.DataType!.ByteSize], $"{path}.{field.InstanceName}");
    }

    // ---------------------------------------------------------------- helpers

    private static IEnumerable<IMember> Members(IInterfaceType it)
    {
        try
        {
            if (it.AllMembers is { Count: > 0 } all)
            {
                return all;
            }
        }
        catch (Exception)
        {
            // AllMembers may throw for unresolved base types - fall back to declared members.
        }

        return it.Members ?? Enumerable.Empty<IMember>();
    }

    private static int[] DimensionLengths(IArrayType at)
    {
        var lengths = at.Dimensions.Select(d => d.ElementCount).ToArray();
        return lengths.Length == 0 ? [0] : lengths;
    }

    private static int ElementSize(IArrayType at, int[] lengths)
    {
        var total = lengths.Aggregate(1, (a, b) => a * b);
        return at.ElementType?.ByteSize is > 0 and var s ? s : (total == 0 ? 0 : at.ByteSize / total);
    }

    private static bool IsBoolLike(IDataType t) => AdsPrimitives.KindOf(t) == PrimitiveKind.Bool;

    private static bool IsWide(IStringType st) =>
        st.Name.StartsWith("WSTRING", StringComparison.OrdinalIgnoreCase) || st.Encoding is UnicodeEncoding;

    private static Encoding StringEncoding(IStringType st) =>
        IsWide(st) ? Encoding.Unicode : st.Encoding ?? Encoding.Latin1;

    private static string FormatAddress(ReadOnlySpan<byte> d) => d.Length switch
    {
        8 => $"0x{BinaryPrimitives.ReadUInt64LittleEndian(d):X16}",
        4 => $"0x{BinaryPrimitives.ReadUInt32LittleEndian(d):X8}",
        _ => "0x" + Convert.ToHexString(d),
    };

    private static PrimitiveKind KindBySize(int size, bool signed) => size switch
    {
        1 => signed ? PrimitiveKind.SInt : PrimitiveKind.USInt,
        2 => signed ? PrimitiveKind.Int : PrimitiveKind.UInt,
        4 => signed ? PrimitiveKind.DInt : PrimitiveKind.UDInt,
        _ => signed ? PrimitiveKind.LInt : PrimitiveKind.ULInt,
    };

    private static long ReadInteger(PrimitiveKind k, ReadOnlySpan<byte> d) => k switch
    {
        PrimitiveKind.SInt => (sbyte)d[0],
        PrimitiveKind.USInt or PrimitiveKind.Bool => d[0],
        PrimitiveKind.Int => BinaryPrimitives.ReadInt16LittleEndian(d),
        PrimitiveKind.UInt => BinaryPrimitives.ReadUInt16LittleEndian(d),
        PrimitiveKind.DInt => BinaryPrimitives.ReadInt32LittleEndian(d),
        PrimitiveKind.UDInt => BinaryPrimitives.ReadUInt32LittleEndian(d),
        PrimitiveKind.ULInt => (long)BinaryPrimitives.ReadUInt64LittleEndian(d),
        _ => BinaryPrimitives.ReadInt64LittleEndian(d),
    };

    private static void WriteInteger(PrimitiveKind k, long n, Span<byte> d)
    {
        switch (k)
        {
            case PrimitiveKind.SInt or PrimitiveKind.USInt or PrimitiveKind.Bool:
                d[0] = unchecked((byte)n);
                break;
            case PrimitiveKind.Int or PrimitiveKind.UInt:
                BinaryPrimitives.WriteUInt16LittleEndian(d, unchecked((ushort)n));
                break;
            case PrimitiveKind.DInt or PrimitiveKind.UDInt:
                BinaryPrimitives.WriteUInt32LittleEndian(d, unchecked((uint)n));
                break;
            default:
                BinaryPrimitives.WriteUInt64LittleEndian(d, unchecked((ulong)n));
                break;
        }
    }

    private static void WriteInteger(PrimitiveKind k, ulong n, Span<byte> d) => WriteInteger(k, unchecked((long)n), d);

    private static (long Min, long Max) Range(PrimitiveKind k) => k switch
    {
        PrimitiveKind.SInt => (sbyte.MinValue, sbyte.MaxValue),
        PrimitiveKind.USInt => (0, byte.MaxValue),
        PrimitiveKind.Int => (short.MinValue, short.MaxValue),
        PrimitiveKind.UInt => (0, ushort.MaxValue),
        PrimitiveKind.DInt => (int.MinValue, int.MaxValue),
        PrimitiveKind.UDInt => (0, uint.MaxValue),
        _ => (long.MinValue, long.MaxValue),
    };

    private static bool ParseBool(JsonElement v, string path)
    {
        switch (v.ValueKind)
        {
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Number when v.TryGetInt64(out var n) && (n == 0 || n == 1):
                return n == 1;
            case JsonValueKind.String:
                var s = v.GetString()!.Trim();
                if (s.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || s == "1")
                {
                    return true;
                }

                if (s.Equals("FALSE", StringComparison.OrdinalIgnoreCase) || s == "0")
                {
                    return false;
                }

                break;
        }

        throw new TcException($"Expected BOOL (true/false) at '{path}' but got {v.GetRawText()}.");
    }

    private static double ParseDouble(JsonElement v, IDataType type, string path)
    {
        if (v.ValueKind == JsonValueKind.Number)
        {
            return v.GetDouble();
        }

        if (v.ValueKind == JsonValueKind.String &&
            double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            return d;
        }

        throw TypeError(type, path, v);
    }

    private static long ParseSigned(JsonElement v, IDataType type, string path)
    {
        if (v.ValueKind == JsonValueKind.Number)
        {
            if (v.TryGetInt64(out var n))
            {
                return n;
            }

            if (v.TryGetUInt64(out var u))
            {
                return unchecked((long)u);
            }

            throw new TcException($"{type.Name} expects an integer at '{path}' but got {v.GetRawText()}.");
        }

        if (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
        {
            return v.ValueKind == JsonValueKind.True ? 1 : 0;
        }

        if (v.ValueKind == JsonValueKind.String && IecNumber.TryParse(v.GetString()!, out var parsed))
        {
            return unchecked((long)parsed);
        }

        throw TypeError(type, path, v);
    }

    private static ulong ParseUnsigned(JsonElement v, IDataType type, string path)
    {
        if (v.ValueKind == JsonValueKind.Number && v.TryGetUInt64(out var u))
        {
            return u;
        }

        if (v.ValueKind == JsonValueKind.String && IecNumber.TryParse(v.GetString()!, out var parsed))
        {
            return parsed;
        }

        return unchecked((ulong)ParseSigned(v, type, path));
    }

    private static TcException TypeError(IDataType type, string path, JsonElement v) =>
        new($"Cannot convert {v.GetRawText()} to {type.Name} at '{path}'.")
        {
            Hint = type.Category switch
            {
                DataTypeCategory.Struct or DataTypeCategory.FunctionBlock => "Pass a JSON object with member names, e.g. {\"nValue\": 5}.",
                DataTypeCategory.Array => "Pass a JSON array (nested arrays for multi-dimensional arrays).",
                DataTypeCategory.String => "Pass a JSON string.",
                DataTypeCategory.Enum => "Pass the enum name as string (e.g. \"Running\") or its integer value.",
                _ => "Pass a JSON number (or numeric string like \"16#FF\"), true/false for BOOL.",
            },
        };
}

/// <summary>Parses IEC / C style integer literals: 42, -7, 16#FF, 2#1010, 8#17, 0x1F, 1_000.</summary>
public static class IecNumber
{
    public static bool TryParse(string text, out ulong value)
    {
        value = 0;
        var s = text.Trim().Replace("_", "");
        if (s.Length == 0)
        {
            return false;
        }

        var hash = s.IndexOf('#');
        if (hash > 0)
        {
            var prefix = s[..hash];
            var digits = s[(hash + 1)..];
            var radix = prefix switch { "16" => 16, "8" => 8, "2" => 2, "10" => 10, _ => 0 };
            if (radix == 0)
            {
                return false;
            }

            try
            {
                value = Convert.ToUInt64(digits, radix);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return ulong.TryParse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var signed))
        {
            value = unchecked((ulong)signed);
            return true;
        }

        return ulong.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
