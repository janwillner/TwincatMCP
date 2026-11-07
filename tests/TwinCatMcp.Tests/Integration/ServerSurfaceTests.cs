using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace TwinCatMcp.Tests.Integration;

[CollectionDefinition(Name)]
public sealed class PlainServerCollection : ICollectionFixture<McpServerFixture>
{
    public const string Name = "PlainServer";
}

/// <summary>Quality gate for the MCP surface itself – no IDE required.</summary>
[Collection(PlainServerCollection.Name)]
public sealed partial class ServerSurfaceTests(McpServerFixture fx)
{
    private static readonly string[] AllowedPrefixes =
    [
        "xae_", "solution_", "tree_", "build_", "plc_", "pou_", "lib_", "archive_", "system_", "task_", "rt_", "license_",
        "variant_", "io_", "ecat_", "mapping_", "motion_", "tccom_", "ads_", "route_",
    ];

    [Fact]
    public async Task Every_tool_has_a_prefixed_snake_case_name_and_a_description()
    {
        var tools = await fx.Client.ListToolsAsync();

        Assert.NotEmpty(tools);
        foreach (var tool in tools)
        {
            Assert.Matches(SnakeCase(), tool.Name);
            Assert.True(AllowedPrefixes.Any(p => tool.Name.StartsWith(p, StringComparison.Ordinal)), $"{tool.Name} has no known area prefix");
            Assert.False(string.IsNullOrWhiteSpace(tool.Description), $"{tool.Name} has no description");
        }

        Assert.Equal(tools.Count, tools.Select(t => t.Name).Distinct().Count());
    }

    [Fact]
    public async Task Every_tool_parameter_has_a_description_or_a_self_explaining_name()
    {
        // Parameters may omit a description only when the name is self-explaining.
        var selfExplaining = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "treePath", "treePaths", "parentPath", "name", "newName", "comment", "disabled", "visible", "silent", "save",
            "saveAll", "xml", "recursive", "filePath", "path", "childName", "before", "reconnect", "command", "args",
            "includeWarnings", "includeMessages", "timeoutSeconds", "configuration", "platform", "maxDepth", "maxResults",
            "netId", "port", "rootPath", "namePattern", "pane", "lastLines", "rebuild", "project", "depth", "overwrite",
        };

        var missing = new List<string>();
        foreach (var tool in await fx.Client.ListToolsAsync())
        {
            if (!tool.JsonSchema.TryGetProperty("properties", out var props))
            {
                continue;
            }

            foreach (var p in props.EnumerateObject())
            {
                var hasDescription = p.Value.TryGetProperty("description", out var d) && !string.IsNullOrWhiteSpace(d.GetString());
                if (!hasDescription && !selfExplaining.Contains(p.Name))
                {
                    missing.Add($"{tool.Name}.{p.Name}");
                }
            }
        }

        Assert.True(missing.Count == 0, "Parameters without description: " + string.Join(", ", missing));
    }

    [Fact]
    public async Task Workflow_guide_resource_is_readable()
    {
        var result = await fx.Client.ReadResourceAsync("twincat://guide/workflow");

        var text = result.Contents.OfType<TextResourceContents>().Single().Text;
        Assert.Contains("solution_create", text);
        Assert.Contains("system_activate_configuration", text);
    }

    [Fact]
    public async Task Session_status_resource_reports_disconnected_without_ide()
    {
        var result = await fx.Client.ReadResourceAsync("twincat://session/status");

        var json = JsonDocument.Parse(result.Contents.OfType<TextResourceContents>().Single().Text).RootElement;
        Assert.False(json.GetProperty("connected").GetBoolean());
    }

    [Fact]
    public async Task Create_project_prompt_embeds_the_arguments()
    {
        var prompt = await fx.Client.GetPromptAsync("create_twincat_project", new Dictionary<string, object?>
        {
            ["description"] = "Conveyor with two sensors",
            ["directory"] = @"C:\Temp",
            ["name"] = "Conveyor",
        });

        var text = string.Concat(prompt.Messages.Select(m => m.Content).OfType<TextContentBlock>().Select(c => c.Text));
        Assert.Contains("Conveyor with two sensors", text);
        Assert.Contains(@"C:\Temp", text);
    }

    [Fact]
    public async Task Calling_an_xae_tool_without_session_explains_how_to_start()
    {
        var (json, isError) = await fx.CallRaw("tree_get_item", new { treePath = "TIRT" });

        Assert.True(isError);
        Assert.Contains("xae_start", json.GetProperty("hint").GetString());
    }

    [GeneratedRegex("^[a-z][a-z0-9]*(_[a-z0-9]+)+$")]
    private static partial Regex SnakeCase();
}
