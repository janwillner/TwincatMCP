namespace TwinCatMcp.Guidelines;

/// <summary>Severity of a guideline rule: off = not checked, warn = reported, error = the write is rejected.</summary>
public enum GuidelineSeverity
{
    Off,
    Warn,
    Error,
}

/// <summary>The catalog of rule keys a configuration can set (rules section, mcp-ignore pragma, findings).</summary>
public static class GuidelineRules
{
    // Names of PLC objects (key suffix = naming.templates key).
    public const string PouProgram = "naming.pou.program";
    public const string PouFunctionBlock = "naming.pou.functionBlock";
    public const string PouFunction = "naming.pou.function";
    public const string PouMethod = "naming.pou.method";
    public const string PouProperty = "naming.pou.property";
    public const string PouAction = "naming.pou.action";
    public const string PouTransition = "naming.pou.transition";
    public const string PouInterface = "naming.pou.interface";
    public const string DutStruct = "naming.dut.struct";
    public const string DutEnum = "naming.dut.enum";
    public const string DutUnion = "naming.dut.union";
    public const string DutAlias = "naming.dut.alias";
    public const string Gvl = "naming.gvl";
    public const string ParamList = "naming.paramList";

    // Declared identifiers.
    public const string VarInput = "naming.var.input";
    public const string VarOutput = "naming.var.output";
    public const string VarInOut = "naming.var.inOut";
    public const string VarLocal = "naming.var.local";
    public const string VarMethodLocal = "naming.var.methodLocal";
    public const string VarFunctionLocal = "naming.var.functionLocal";
    public const string VarTemp = "naming.var.temp";
    public const string VarStat = "naming.var.stat";
    public const string VarInst = "naming.var.inst";
    public const string VarGlobal = "naming.var.global";
    public const string VarMember = "naming.var.member";
    public const string ConstLocal = "naming.const.local";
    public const string ConstGlobal = "naming.const.global";
    public const string EnumValue = "naming.enum.value";
    public const string UnknownType = "naming.unknownType";

    // Declarations.
    public const string RequiredAttributes = "decl.requiredAttributes";
    public const string EnumExplicitValues = "decl.enum.explicitValues";
    public const string EnumValueRange = "decl.enum.valueRange";
    public const string MethodInputDefaults = "decl.method.inputDefaults";

    // Header templates.
    public const string HeaderCopyright = "header.copyright";
    public const string HeaderDescription = "header.description";

    public static readonly IReadOnlyList<string> All =
    [
        PouProgram, PouFunctionBlock, PouFunction, PouMethod, PouProperty, PouAction, PouTransition, PouInterface,
        DutStruct, DutEnum, DutUnion, DutAlias, Gvl, ParamList,
        VarInput, VarOutput, VarInOut, VarLocal, VarMethodLocal, VarFunctionLocal, VarTemp, VarStat, VarInst, VarGlobal,
        VarMember, ConstLocal, ConstGlobal, EnumValue, UnknownType,
        RequiredAttributes, EnumExplicitValues, EnumValueRange, MethodInputDefaults,
        HeaderCopyright, HeaderDescription,
    ];

    /// <summary>
    /// Keys matched by a rules entry: an exact key or a prefix pattern such as 'naming.var.*' or '*'.
    /// naming.unknownType is only matched by its exact key - a pattern must not turn the strict type check on.
    /// </summary>
    public static IEnumerable<string> Match(string pattern)
    {
        if (pattern == "*")
        {
            return All.Where(k => k != UnknownType);
        }

        if (pattern.EndsWith(".*", StringComparison.Ordinal))
        {
            var prefix = pattern[..^1];
            return All.Where(k => k != UnknownType && k.StartsWith(prefix, StringComparison.Ordinal));
        }

        return All.Where(k => k == pattern);
    }

    public static GuidelineSeverity ParseSeverity(string text, string where)
        => text.Trim().ToLowerInvariant() switch
        {
            "off" => GuidelineSeverity.Off,
            "warn" or "warning" => GuidelineSeverity.Warn,
            "error" => GuidelineSeverity.Error,
            _ => throw new GuidelineConfigException($"{where}: unknown severity '{text}'. Use 'off', 'warn' or 'error'."),
        };

    public static string Name(GuidelineSeverity severity) => severity.ToString().ToLowerInvariant();
}
