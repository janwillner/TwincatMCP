using System.Buffers.Binary;
using System.Globalization;
using TwinCatMcp.Core;

namespace TwinCatMcp.Io;

/// <summary>Conversion between typed values and the little-endian hex data of CoE SDO / startup entries.</summary>
public static class CoeValue
{
    /// <summary>Supported IEC / CoE type names and their byte size.</summary>
    public static readonly IReadOnlyDictionary<string, int> TypeSizes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["BOOL"] = 1, ["BIT"] = 1, ["USINT"] = 1, ["SINT"] = 1, ["BYTE"] = 1, ["UINT8"] = 1, ["INT8"] = 1,
        ["UINT"] = 2, ["INT"] = 2, ["WORD"] = 2, ["UINT16"] = 2, ["INT16"] = 2,
        ["UDINT"] = 4, ["DINT"] = 4, ["DWORD"] = 4, ["REAL"] = 4, ["UINT32"] = 4, ["INT32"] = 4, ["REAL32"] = 4,
        ["ULINT"] = 8, ["LINT"] = 8, ["LWORD"] = 8, ["LREAL"] = 8, ["UINT64"] = 8, ["INT64"] = 8, ["REAL64"] = 8,
    };

    /// <summary>
    /// Encodes <paramref name="value"/> as little-endian hex for <paramref name="dataType"/> (e.g. UINT 1000 -> "e803").
    /// Raw bytes: dataType "HEX" or value prefix "hex:". Numbers may be decimal or 0x-hex.
    /// </summary>
    public static string Encode(string value, string? dataType)
    {
        var v = (value ?? "").Trim();
        var type = (dataType ?? "").Trim().ToUpperInvariant();
        if (v.StartsWith("hex:", StringComparison.OrdinalIgnoreCase) || type is "HEX" or "RAW" or "OCTET_STRING")
        {
            var raw = new string(v.Replace("hex:", "", StringComparison.OrdinalIgnoreCase).Where(Uri.IsHexDigit).ToArray()).ToLowerInvariant();
            if (raw.Length == 0 || raw.Length % 2 != 0)
            {
                throw new TcException($"Invalid hex data '{value}'.") { Hint = "Use an even number of hex digits, e.g. hex:e803 (little-endian)." };
            }

            return raw;
        }

        if (!TypeSizes.TryGetValue(type, out var size))
        {
            throw new TcException($"Unknown CoE data type '{dataType}'.")
            {
                Hint = "Use BOOL, USINT, SINT, UINT, INT, UDINT, DINT, ULINT, LINT, BYTE, WORD, DWORD, LWORD, REAL, LREAL or HEX (raw little-endian bytes).",
            };
        }

        var bytes = new byte[size];
        switch (type)
        {
            case "REAL" or "REAL32":
                BinaryPrimitives.WriteSingleLittleEndian(bytes, float.Parse(v, CultureInfo.InvariantCulture));
                break;
            case "LREAL" or "REAL64":
                BinaryPrimitives.WriteDoubleLittleEndian(bytes, double.Parse(v, CultureInfo.InvariantCulture));
                break;
            case "BOOL" or "BIT":
                bytes[0] = v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1" ? (byte)1
                    : v.Equals("false", StringComparison.OrdinalIgnoreCase) || v == "0" ? (byte)0
                    : throw new TcException($"Invalid BOOL value '{value}'.");
                break;
            default:
                var n = ParseInteger(v);
                var signed = type is "SINT" or "INT" or "DINT" or "LINT" or "INT8" or "INT16" or "INT32" or "INT64";
                CheckRange(n, size, signed, value, type);
                var buffer = new byte[8];
                BinaryPrimitives.WriteInt64LittleEndian(buffer, n);
                Array.Copy(buffer, bytes, size);
                break;
        }

        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Decodes little-endian hex for a type. Unknown/empty type returns "0x" + big-endian hex for readability.</summary>
    public static string Decode(string hex, string? dataType)
    {
        var bytes = Convert.FromHexString(new string((hex ?? "").Where(Uri.IsHexDigit).ToArray()));
        var type = (dataType ?? "").Trim().ToUpperInvariant();
        if (!TypeSizes.TryGetValue(type, out var size) || bytes.Length < size)
        {
            return "0x" + Convert.ToHexString(bytes.Reverse().ToArray());
        }

        var span = bytes.AsSpan(0, size);
        return type switch
        {
            "BOOL" or "BIT" => (span[0] != 0).ToString().ToUpperInvariant(),
            "USINT" or "BYTE" or "UINT8" => span[0].ToString(CultureInfo.InvariantCulture),
            "SINT" or "INT8" => ((sbyte)span[0]).ToString(CultureInfo.InvariantCulture),
            "UINT" or "WORD" or "UINT16" => BinaryPrimitives.ReadUInt16LittleEndian(span).ToString(CultureInfo.InvariantCulture),
            "INT" or "INT16" => BinaryPrimitives.ReadInt16LittleEndian(span).ToString(CultureInfo.InvariantCulture),
            "UDINT" or "DWORD" or "UINT32" => BinaryPrimitives.ReadUInt32LittleEndian(span).ToString(CultureInfo.InvariantCulture),
            "DINT" or "INT32" => BinaryPrimitives.ReadInt32LittleEndian(span).ToString(CultureInfo.InvariantCulture),
            "ULINT" or "LWORD" or "UINT64" => BinaryPrimitives.ReadUInt64LittleEndian(span).ToString(CultureInfo.InvariantCulture),
            "LINT" or "INT64" => BinaryPrimitives.ReadInt64LittleEndian(span).ToString(CultureInfo.InvariantCulture),
            "REAL" or "REAL32" => BinaryPrimitives.ReadSingleLittleEndian(span).ToString(CultureInfo.InvariantCulture),
            "LREAL" or "REAL64" => BinaryPrimitives.ReadDoubleLittleEndian(span).ToString(CultureInfo.InvariantCulture),
            _ => "0x" + Convert.ToHexString(bytes.Reverse().ToArray()),
        };
    }

    private static long ParseInteger(string v)
    {
        if (v.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || v.StartsWith("#x", StringComparison.OrdinalIgnoreCase))
        {
            return unchecked((long)ulong.Parse(v[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }

        if (v.StartsWith("16#", StringComparison.Ordinal))
        {
            return unchecked((long)ulong.Parse(v[3..].Replace("_", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }

        if (long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
        {
            return l;
        }

        return unchecked((long)ulong.Parse(v, NumberStyles.Integer, CultureInfo.InvariantCulture));
    }

    private static void CheckRange(long n, int size, bool signed, string original, string type)
    {
        if (size == 8)
        {
            return;
        }

        var bits = size * 8;
        var ok = signed
            ? n >= -(1L << (bits - 1)) && n < (1L << (bits - 1))
            : n >= 0 && n < (1L << bits);
        if (!ok)
        {
            throw new TcException($"Value '{original}' does not fit into {type} ({size} byte{(size > 1 ? "s" : "")}).");
        }
    }
}
