using TwinCAT.TypeSystem;

namespace TwinCatMcp.Ads;

public enum PrimitiveKind
{
    Bool,
    SInt,
    USInt,
    Int,
    UInt,
    DInt,
    UDInt,
    LInt,
    ULInt,
    Real,
    LReal,
    Time,
    LTime,
    Date,
    LDate,
    Dt,
    LDt,
    Tod,
    LTod,
}

/// <summary>Maps IEC 61131-3 / TwinCAT primitive type names to their binary representation.</summary>
public static class AdsPrimitives
{
    private static readonly Dictionary<string, PrimitiveKind> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BOOL"] = PrimitiveKind.Bool,
        ["BIT"] = PrimitiveKind.Bool,
        ["SINT"] = PrimitiveKind.SInt,
        ["INT8"] = PrimitiveKind.SInt,
        ["USINT"] = PrimitiveKind.USInt,
        ["BYTE"] = PrimitiveKind.USInt,
        ["UINT8"] = PrimitiveKind.USInt,
        ["INT"] = PrimitiveKind.Int,
        ["INT16"] = PrimitiveKind.Int,
        ["UINT"] = PrimitiveKind.UInt,
        ["WORD"] = PrimitiveKind.UInt,
        ["UINT16"] = PrimitiveKind.UInt,
        ["DINT"] = PrimitiveKind.DInt,
        ["INT32"] = PrimitiveKind.DInt,
        ["UDINT"] = PrimitiveKind.UDInt,
        ["DWORD"] = PrimitiveKind.UDInt,
        ["UINT32"] = PrimitiveKind.UDInt,
        ["LINT"] = PrimitiveKind.LInt,
        ["INT64"] = PrimitiveKind.LInt,
        ["ULINT"] = PrimitiveKind.ULInt,
        ["LWORD"] = PrimitiveKind.ULInt,
        ["UINT64"] = PrimitiveKind.ULInt,
        ["REAL"] = PrimitiveKind.Real,
        ["FLOAT"] = PrimitiveKind.Real,
        ["LREAL"] = PrimitiveKind.LReal,
        ["DOUBLE"] = PrimitiveKind.LReal,
        ["TIME"] = PrimitiveKind.Time,
        ["LTIME"] = PrimitiveKind.LTime,
        ["DATE"] = PrimitiveKind.Date,
        ["LDATE"] = PrimitiveKind.LDate,
        ["DT"] = PrimitiveKind.Dt,
        ["DATE_AND_TIME"] = PrimitiveKind.Dt,
        ["LDT"] = PrimitiveKind.LDt,
        ["LDATE_AND_TIME"] = PrimitiveKind.LDt,
        ["TOD"] = PrimitiveKind.Tod,
        ["TIME_OF_DAY"] = PrimitiveKind.Tod,
        ["LTOD"] = PrimitiveKind.LTod,
        ["LTIME_OF_DAY"] = PrimitiveKind.LTod,
    };

    /// <summary>Returns the primitive kind of a type, or null when it is not a (known) primitive.</summary>
    public static PrimitiveKind? KindOf(IDataType type)
    {
        if (ByName.TryGetValue(type.Name.Trim(), out var kind))
        {
            return kind;
        }

        if (type.Category == DataTypeCategory.Alias && type is IAliasType { BaseType: not null } a)
        {
            return KindOf(a.BaseType);
        }

        if (type is IPrimitiveType p && type.Category == DataTypeCategory.Primitive)
        {
            var flags = p.PrimitiveFlags;
            if (flags.HasFlag(PrimitiveTypeFlags.Bool))
            {
                return PrimitiveKind.Bool;
            }

            if (flags.HasFlag(PrimitiveTypeFlags.Float))
            {
                return type.ByteSize == 4 ? PrimitiveKind.Real : PrimitiveKind.LReal;
            }

            var unsigned = flags.HasFlag(PrimitiveTypeFlags.Unsigned);
            return type.ByteSize switch
            {
                1 => unsigned ? PrimitiveKind.USInt : PrimitiveKind.SInt,
                2 => unsigned ? PrimitiveKind.UInt : PrimitiveKind.Int,
                4 => unsigned ? PrimitiveKind.UDInt : PrimitiveKind.DInt,
                8 => unsigned ? PrimitiveKind.ULInt : PrimitiveKind.LInt,
                _ => null,
            };
        }

        return null;
    }

    public static bool IsNumeric(PrimitiveKind k) => k is not (PrimitiveKind.Bool or PrimitiveKind.Time or PrimitiveKind.LTime or
        PrimitiveKind.Date or PrimitiveKind.LDate or PrimitiveKind.Dt or PrimitiveKind.LDt or PrimitiveKind.Tod or PrimitiveKind.LTod);
}
