using System.ComponentModel;
using System.Reflection;
using System.Text;
using ModelContextProtocol.Server;

namespace TwinCatMcp.Core;

/// <summary>Generates a Markdown catalog of all tools / resources / prompts from their attributes (docs/tools.md).</summary>
public static class ToolCatalog
{
    private static readonly (string Prefix, string Title)[] Areas =
    [
        ("xae_", "IDE session"), ("solution_", "Solution"), ("build_", "Build"), ("tree_", "System Manager tree (generic)"),
        ("plc_online_", "PLC online (XAE)"), ("plc_", "PLC project"), ("pou_", "POUs & code"), ("lib_", "Libraries"), ("archive_", "Archives & export"),
        ("system_", "System & activation"), ("rt_", "Real-time"), ("task_", "Tasks"), ("license_", "Licenses"), ("variant_", "Project variants"),
        ("io_", "I/O devices"), ("ecat_", "EtherCAT"), ("mapping_", "Variable mapping"), ("motion_", "Motion (NC)"), ("tccom_", "TcCOM"),
        ("ads_", "ADS online"), ("route_", "ADS routes"),
    ];

    public static string Generate(Assembly assembly)
    {
        var tools = assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Select(m => (Method: m, Attr: m.GetCustomAttribute<McpServerToolAttribute>()))
            .Where(x => x.Attr?.Name is not null)
            .OrderBy(x => x.Attr!.Name, StringComparer.Ordinal)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("# Tool catalog").AppendLine();
        sb.AppendLine($"Generated from the server assembly – {tools.Count} tools. Regenerate with `TwinCatMcp.Server.exe --tool-catalog > docs/tools.md`.").AppendLine();

        var done = new HashSet<string>();
        foreach (var (prefix, title) in Areas)
        {
            var group = tools.Where(t => t.Attr!.Name!.StartsWith(prefix, StringComparison.Ordinal) && !done.Contains(t.Attr.Name!)).ToList();
            if (group.Count == 0)
            {
                continue;
            }

            sb.AppendLine($"## {title} (`{prefix}*`, {group.Count})").AppendLine();
            sb.AppendLine("| Tool | Description | Parameters |").AppendLine("|---|---|---|");
            foreach (var (method, attr) in group)
            {
                done.Add(attr!.Name!);
                var desc = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";
                var pars = method.GetParameters()
                    .Where(p => p.ParameterType != typeof(CancellationToken) && !p.ParameterType.Name.Contains("McpServer"))
                    .Select(p => p.HasDefaultValue ? $"`{p.Name}`?" : $"`{p.Name}`");
                var flags = attr.ReadOnly ? " *(read-only)*" : "";
                sb.AppendLine($"| `{attr.Name}`{flags} | {Escape(desc)} | {string.Join(", ", pars)} |");
            }

            sb.AppendLine();
        }

        var rest = tools.Where(t => !done.Contains(t.Attr!.Name!)).ToList();
        if (rest.Count > 0)
        {
            sb.AppendLine("## Other").AppendLine();
            foreach (var (method, attr) in rest)
            {
                sb.AppendLine($"- `{attr!.Name}` – {Escape(method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "")}");
            }

            sb.AppendLine();
        }

        sb.AppendLine("## Resources").AppendLine();
        foreach (var m in assembly.GetTypes().SelectMany(t => t.GetMethods()).Where(m => m.GetCustomAttribute<McpServerResourceAttribute>() is not null))
        {
            sb.AppendLine($"- `{m.GetCustomAttribute<McpServerResourceAttribute>()!.UriTemplate}` – {Escape(m.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "")}");
        }

        sb.AppendLine().AppendLine("## Prompts").AppendLine();
        foreach (var m in assembly.GetTypes().SelectMany(t => t.GetMethods()).Where(m => m.GetCustomAttribute<McpServerPromptAttribute>() is not null))
        {
            sb.AppendLine($"- `{m.GetCustomAttribute<McpServerPromptAttribute>()!.Name}` – {Escape(m.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "")}");
        }

        return sb.ToString();
    }

    private static string Escape(string s) => s.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
}
