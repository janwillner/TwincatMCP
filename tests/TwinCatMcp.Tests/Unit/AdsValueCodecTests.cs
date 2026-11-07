using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TwinCAT.Ads.TypeSystem;
using TwinCAT.TypeSystem;
using TwinCatMcp.Ads;
using TwinCatMcp.Core;

namespace TwinCatMcp.Tests.Unit;

/// <summary>
/// JSON &lt;-&gt; ADS byte image conversion. Expected byte images are little endian literals as
/// defined by the ADS / IEC 61131-3 data type layout (TIME = UDINT ms, DT/DATE = UDINT seconds since
/// 1970-01-01, TOD = UDINT ms since midnight, LTIME = ULINT ns, STRING = n+1 bytes, zero terminated).
/// </summary>
public sealed class AdsValueCodecTests
{
    private static readonly PrimitiveType Bool = new("BOOL", typeof(bool));
    private static readonly PrimitiveType Int = new("INT", typeof(short));
    private static readonly PrimitiveType UDInt = new("UDINT", typeof(uint));
    private static readonly PrimitiveType Real = new("REAL", typeof(float));
    private static readonly PrimitiveType LReal = new("LREAL", typeof(double));
    private static readonly PrimitiveType Time = new("TIME", typeof(uint));
    private static readonly PrimitiveType LTime = new("LTIME", typeof(ulong));
    private static readonly PrimitiveType Date = new("DATE", typeof(uint));
    private static readonly PrimitiveType Dt = new("DT", typeof(uint));
    private static readonly PrimitiveType Tod = new("TOD", typeof(uint));

    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static byte[] Hex(string hex) => Convert.FromHexString(hex.Replace(" ", ""));

    private static string Str(JsonNode? n) => n?.ToJsonString() ?? "null";

    [Theory]
    [InlineData("INT", "F9FF", "-7")]
    [InlineData("UDINT", "78563412", "305419896")]
    [InlineData("BOOL", "01", "true")]
    [InlineData("BOOL", "00", "false")]
    [InlineData("REAL", "0000C03F", "1.5")]
    [InlineData("LREAL", "0000000000000C40", "3.5")]
    public void Decodes_primitives(string type, string hex, string json)
    {
        IDataType t = type switch { "INT" => Int, "UDINT" => UDInt, "BOOL" => Bool, "REAL" => Real, _ => LReal };
        Assert.Equal(json, Str(AdsValueCodec.Decode(t, Hex(hex))));
    }

    [Theory]
    [InlineData("INT", "-7", "F9FF")]
    [InlineData("INT", "\"16#FF\"", "FF00")]
    [InlineData("UDINT", "305419896", "78563412")]
    [InlineData("BOOL", "true", "01")]
    [InlineData("BOOL", "\"FALSE\"", "00")]
    [InlineData("REAL", "1.5", "0000C03F")]
    [InlineData("LREAL", "3.5", "0000000000000C40")]
    public void Encodes_primitives(string type, string json, string hex)
    {
        IDataType t = type switch { "INT" => Int, "UDINT" => UDInt, "BOOL" => Bool, "REAL" => Real, _ => LReal };
        Assert.Equal(hex, Convert.ToHexString(AdsValueCodec.Encode(t, J(json))));
    }

    [Fact]
    public void Out_of_range_integer_is_rejected_with_a_hint()
    {
        var ex = Assert.Throws<TcException>(() => AdsValueCodec.Encode(Int, J("40000")));
        Assert.Contains("INT", ex.Message);
        Assert.NotNull(ex.Hint);
    }

    [Fact]
    public void Time_types_are_human_readable_iec_literals()
    {
        Assert.Equal("\"T#1m30s500ms\"", Str(AdsValueCodec.Decode(Time, Hex("84610100"))));
        Assert.Equal("\"LTIME#1s500ms\"", Str(AdsValueCodec.Decode(LTime, Hex("002F685900000000"))));
        Assert.Equal("\"D#2024-01-15\"", Str(AdsValueCodec.Decode(Date, Hex("8075A465"))));
        Assert.Equal("\"DT#2024-01-15-12:30:00\"", Str(AdsValueCodec.Decode(Dt, Hex("4825A565"))));
        Assert.Equal("\"TOD#12:30:00.500\"", Str(AdsValueCodec.Decode(Tod, Hex("34A7AE02"))));
    }

    [Fact]
    public void Time_types_accept_iec_literals_and_raw_numbers()
    {
        Assert.Equal("84610100", Convert.ToHexString(AdsValueCodec.Encode(Time, J("\"T#1m30s500ms\""))));
        Assert.Equal("84610100", Convert.ToHexString(AdsValueCodec.Encode(Time, J("90500"))));
        Assert.Equal("002F685900000000", Convert.ToHexString(AdsValueCodec.Encode(LTime, J("\"LTIME#1s500ms\""))));
        Assert.Equal("8075A465", Convert.ToHexString(AdsValueCodec.Encode(Date, J("\"D#2024-01-15\""))));
        Assert.Equal("4825A565", Convert.ToHexString(AdsValueCodec.Encode(Dt, J("\"DT#2024-01-15-12:30:00\""))));
        Assert.Equal("34A7AE02", Convert.ToHexString(AdsValueCodec.Encode(Tod, J("\"TOD#12:30:00.500\""))));
    }

    [Fact]
    public void Zero_time_is_T0ms()
    {
        Assert.Equal("\"T#0ms\"", Str(AdsValueCodec.Decode(Time, Hex("00000000"))));
    }

    [Fact]
    public void String_is_zero_terminated_and_padded()
    {
        var t = new StringType(10, Encoding.Latin1);
        var bytes = AdsValueCodec.Encode(t, J("\"Hello\""));
        Assert.Equal(11, bytes.Length);
        Assert.Equal("48656C6C6F000000000000", Convert.ToHexString(bytes));
        Assert.Equal("\"Hello\"", Str(AdsValueCodec.Decode(t, bytes)));
    }

    [Fact]
    public void Too_long_string_is_rejected()
    {
        var t = new StringType(3, Encoding.Latin1);
        Assert.Throws<TcException>(() => AdsValueCodec.Encode(t, J("\"Hello\"")));
    }

    [Fact]
    public void WString_uses_utf16()
    {
        var t = new WStringType(4);
        var bytes = AdsValueCodec.Encode(t, J("\"Hi\""));
        Assert.Equal("48006900000000000000", Convert.ToHexString(bytes));
        Assert.Equal("\"Hi\"", Str(AdsValueCodec.Decode(t, bytes)));
    }

    private static StructType Struct() => new StructType("ST_Test", null)
        .AddAligned(new Member("a", Bool))
        .AddAligned(new Member("b", Int))
        .AddAligned(new Member("c", LReal));

    [Fact]
    public void Struct_decodes_to_json_object()
    {
        var t = Struct();
        var data = new byte[t.ByteSize];
        var bOff = t.Members["b"].ByteOffset;
        var cOff = t.Members["c"].ByteOffset;
        data[0] = 1;
        BitConverter.GetBytes((short)42).CopyTo(data, bOff);
        BitConverter.GetBytes(2.5).CopyTo(data, cOff);

        Assert.Equal("{\"a\":true,\"b\":42,\"c\":2.5}", Str(AdsValueCodec.Decode(t, data)));
    }

    [Fact]
    public void Partial_struct_write_keeps_other_members()
    {
        var t = Struct();
        var current = new byte[t.ByteSize];
        current[0] = 1;
        BitConverter.GetBytes(2.5).CopyTo(current, t.Members["c"].ByteOffset);

        var bytes = AdsValueCodec.Encode(t, J("{\"B\": 7}"), current);

        Assert.Equal("{\"a\":true,\"b\":7,\"c\":2.5}", Str(AdsValueCodec.Decode(t, bytes)));
    }

    [Fact]
    public void Unknown_struct_member_lists_valid_members()
    {
        var ex = Assert.Throws<TcException>(() => AdsValueCodec.Encode(Struct(), J("{\"x\": 1}")));
        Assert.Contains("x", ex.Message);
        Assert.Contains("a, b, c", ex.Hint);
    }

    [Fact]
    public void Array_decodes_to_json_array_and_encodes_back()
    {
        var t = new ArrayType(Int, new DimensionCollection().AddDimension(new Dimension(1, 3)));
        var data = Hex("0100 0200 FDFF");
        Assert.Equal("[1,2,-3]", Str(AdsValueCodec.Decode(t, data)));
        Assert.Equal("01000200FDFF", Convert.ToHexString(AdsValueCodec.Encode(t, J("[1,2,-3]"))));
    }

    [Fact]
    public void Multi_dimensional_array_is_nested_row_major()
    {
        var t = new ArrayType(Int, new DimensionCollection().AddDimension(new Dimension(0, 2)).AddDimension(new Dimension(0, 3)));
        var data = Hex("0100 0200 0300 0400 0500 0600");
        Assert.Equal("[[1,2,3],[4,5,6]]", Str(AdsValueCodec.Decode(t, data)));
        Assert.Equal("010002000300040005000600", Convert.ToHexString(AdsValueCodec.Encode(t, J("[[1,2,3],[4,5,6]]"))));
    }

    [Fact]
    public void Shorter_array_write_keeps_remaining_elements()
    {
        var t = new ArrayType(Int, new DimensionCollection().AddDimension(new Dimension(1, 3)));
        var bytes = AdsValueCodec.Encode(t, J("[9]"), Hex("0100 0200 0300"));
        Assert.Equal("090002000300", Convert.ToHexString(bytes));
    }

    [Fact]
    public void Array_of_struct_decodes_each_element()
    {
        var st = Struct();
        var t = new ArrayType(st, new DimensionCollection().AddDimension(new Dimension(0, 2)));
        var data = new byte[t.ByteSize];
        data[st.ByteSize] = 1; // element[1].a
        var json = Str(AdsValueCodec.Decode(t, data));
        Assert.Equal("[{\"a\":false,\"b\":0,\"c\":0},{\"a\":true,\"b\":0,\"c\":0}]", json);
    }

    private static EnumType<short> Color() =>
        new("E_Color", Int, new EnumValueCollection<short>().AddValue("Red", 1).AddValue("Green", 2));

    [Fact]
    public void Enum_decodes_to_name_and_value()
    {
        Assert.Equal("{\"name\":\"Green\",\"value\":2}", Str(AdsValueCodec.Decode(Color(), Hex("0200"))));
        Assert.Equal("{\"name\":null,\"value\":7}", Str(AdsValueCodec.Decode(Color(), Hex("0700"))));
    }

    [Theory]
    [InlineData("\"Green\"")]
    [InlineData("\"E_Color.Green\"")]
    [InlineData("2")]
    [InlineData("{\"name\":\"green\"}")]
    [InlineData("{\"value\":2}")]
    public void Enum_accepts_name_or_value(string json)
    {
        Assert.Equal("0200", Convert.ToHexString(AdsValueCodec.Encode(Color(), J(json))));
    }

    [Fact]
    public void Unknown_enum_name_lists_valid_names()
    {
        var ex = Assert.Throws<TcException>(() => AdsValueCodec.Encode(Color(), J("\"Blue\"")));
        Assert.Contains("Red, Green", ex.Hint);
    }

    [Fact]
    public void Alias_resolves_to_base_type()
    {
        var t = new AliasType("T_Counter", Int);
        Assert.Equal("5", Str(AdsValueCodec.Decode(t, Hex("0500"))));
        Assert.Equal("0500", Convert.ToHexString(AdsValueCodec.Encode(t, J("5"))));
    }

    [Fact]
    public void Wrong_json_kind_gives_a_type_error()
    {
        var ex = Assert.Throws<TcException>(() => AdsValueCodec.Encode(Int, J("\"abc\"")));
        Assert.Contains("INT", ex.Message);
    }
}
