using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TwinCatMcp.Core;
using TwinCatMcp.Runtime;

namespace TwinCatMcp.Tools;

/// <summary>TwinCAT project variants (ITcSysManager14 ProjectVariantConfig / CurrentProjectVariant) - work package 3.</summary>
[McpServerToolType]
public sealed class VariantTools(XaeSession session)
{
    [McpServerTool(Name = "variant_list", ReadOnly = true), Description(
        "Lists the project variants and variant groups of the TwinCAT project and the current variant.")]
    public Task<CallToolResult> List()
        => session.RunAsync(s => new
        {
            current = Current(s),
            config = VariantXml.Parse(s.SysManager.ProjectVariantConfig),
        });

    [McpServerTool(Name = "variant_get_current", ReadOnly = true), Description("Returns the currently selected project variant (empty = no variant management).")]
    public Task<CallToolResult> GetCurrent()
        => session.RunAsync(s => new { current = Current(s) });

    [McpServerTool(Name = "variant_set_current", Idempotent = true), Description(
        "Selects the current project variant by name ('Variant1') or a variant group in brackets ('[Group1]'). Rebuild/activate afterwards.")]
    public Task<CallToolResult> SetCurrent([Description("Variant name or '[GroupName]'.")] string variant)
        => session.RunAsync(s =>
        {
            var cfg = VariantXml.Parse(s.SysManager.ProjectVariantConfig);
            var name = variant?.Trim() ?? "";
            var known = name.StartsWith('[') && name.EndsWith(']')
                ? cfg.Groups.Any(g => string.Equals("[" + g.Name + "]", name, StringComparison.OrdinalIgnoreCase))
                : cfg.Variants.Any(v => string.Equals(v, name, StringComparison.OrdinalIgnoreCase));
            if (!known)
            {
                throw new TcException($"Unknown variant '{variant}'.") { Hint = "Known variants: " + string.Join(", ", cfg.Variants) + (cfg.Groups.Count > 0 ? "; groups: " + string.Join(", ", cfg.Groups.Select(g => "[" + g.Name + "]")) : "") };
            }

            s.SysManager.CurrentProjectVariant = name;
            return new { current = Current(s) };
        });

    [McpServerTool(Name = "variant_define"), Description(
        "Defines the project variants (replaces the whole variant configuration). Example: variants=['Small','Large'], groups={'All':['Small','Large']}. " +
        "Afterwards select one with variant_set_current and use tree_ tools / PlcCompilerDefines per variant.")]
    public Task<CallToolResult> Define(
        [Description("Variant names.")] string[] variants,
        [Description("Optional groups: group name -> member variant names.")] Dictionary<string, string[]>? groups = null)
        => session.RunAsync(s =>
        {
            if (variants is null || variants.Length == 0)
            {
                throw new TcException("At least one variant is required.");
            }

            var cfg = new VariantXml.VariantConfig(
                variants.Select(v => v.Trim()).ToList(),
                (groups ?? []).Select(g => new VariantXml.VariantGroup(g.Key, g.Value.ToList())).ToList());
            s.SysManager.ProjectVariantConfig = VariantXml.Build(cfg);
            return new { config = VariantXml.Parse(s.SysManager.ProjectVariantConfig), current = Current(s) };
        });

    private static string? Current(XaeSession s)
        => XaeRuntime.Try(() => s.SysManager.CurrentProjectVariant) is { Length: > 0 } v ? v : null;
}
