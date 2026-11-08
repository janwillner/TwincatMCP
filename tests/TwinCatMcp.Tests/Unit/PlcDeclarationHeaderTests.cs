using TwinCatMcp.Plc;

namespace TwinCatMcp.Tests.Unit;

/// <summary>pou_write_code must not store a declaration whose header does not match the object (TwinCAT accepts it silently).</summary>
public sealed class PlcDeclarationHeaderTests
{
    [Theory]
    [InlineData(PlcObjects.FunctionBlock, "FUNCTION_BLOCK FB_X\nVAR\nEND_VAR\n")]
    [InlineData(PlcObjects.Program, "PROGRAM MAIN\nVAR\nEND_VAR\n")]
    [InlineData(PlcObjects.Function, "FUNCTION F_Add : INT\nVAR_INPUT\n\tnA : INT;\nEND_VAR\n")]
    [InlineData(PlcObjects.Method, "{attribute 'TcRpcEnable'}\nMETHOD Start : BOOL\n")]
    [InlineData(PlcObjects.InterfaceMethod, "METHOD Start : BOOL\n")]
    [InlineData(PlcObjects.Property, "PROPERTY PUBLIC State : INT")]
    [InlineData(PlcObjects.Interface, "INTERFACE I_Machine EXTENDS I_Base\n")]
    [InlineData(PlcObjects.Enum, "{attribute 'qualified_only'}\n{attribute 'strict'}\nTYPE E_State :\n(\n\teIdle\n);\nEND_TYPE\n")]
    [InlineData(PlcObjects.Struct, "// recipe data\n(* multi\nline *)\nTYPE ST_Recipe :\nSTRUCT\nEND_STRUCT\nEND_TYPE\n")]
    [InlineData(PlcObjects.Gvl, "{attribute 'qualified_only'}\nVAR_GLOBAL\n\tbIn AT %I* : BOOL;\nEND_VAR\n")]
    [InlineData(PlcObjects.ParameterList, "VAR_GLOBAL CONSTANT\n\tnMax : INT := 10;\nEND_VAR\n")]
    [InlineData(PlcObjects.PropertyGet, "VAR\nEND_VAR\n")]
    [InlineData(PlcObjects.Action, "")]
    public void Matching_header_is_accepted(int itemType, string declaration)
    {
        Assert.Null(StDeclaration.HeaderError(itemType, declaration));
    }

    [Theory]
    [InlineData(PlcObjects.Program, "VAR\n\tnBroken : INT;\nEND_VAR\n", "PROGRAM", "VAR")]
    [InlineData(PlcObjects.Function, "FUNCTION_BLOCK F_Add\n", "FUNCTION", "FUNCTION_BLOCK")]
    [InlineData(PlcObjects.FunctionBlock, "{attribute 'x'}\n", "FUNCTION_BLOCK", "nothing")]
    [InlineData(PlcObjects.Gvl, "VAR\n\tbIn : BOOL;\nEND_VAR\n", "VAR_GLOBAL", "VAR")]
    [InlineData(PlcObjects.Struct, "STRUCT\n\tnA : INT;\nEND_STRUCT\n", "TYPE", "STRUCT")]
    public void Missing_or_wrong_header_is_explained(int itemType, string declaration, string expected, string found)
    {
        var error = StDeclaration.HeaderError(itemType, declaration);

        Assert.NotNull(error);
        Assert.Contains($"'{expected}", error);
        Assert.Contains($"'{found}'", error);
    }
}
