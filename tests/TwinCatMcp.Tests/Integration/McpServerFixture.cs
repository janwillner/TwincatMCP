using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace TwinCatMcp.Tests.Integration;

/// <summary>
/// Starts the real MCP server (stdio) once per test collection and talks to it through the
/// official MCP client - exactly what Claude / any MCP host sees.
/// </summary>
public class McpServerFixture : IAsyncLifetime
{
    public McpClient Client { get; private set; } = null!;

    public static string ServerExe
    {
        get
        {
            // Test output: tests/TwinCatMcp.Tests/bin/<cfg>/net10.0-windows -> server output next to it via project reference.
            var dir = AppContext.BaseDirectory;
            var exe = Path.Combine(dir, "TwinCatMcp.Server.exe");
            if (!File.Exists(exe))
            {
                throw new FileNotFoundException("Server executable not found next to tests", exe);
            }

            return exe;
        }
    }

    public virtual async Task InitializeAsync()
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "twincat-mcp-test",
            Command = ServerExe,
            Arguments = [],
            ShutdownTimeout = TimeSpan.FromSeconds(60),
        });
        Client = await McpClient.CreateAsync(transport);
    }

    public virtual async Task DisposeAsync()
    {
        if (Client is not null)
        {
            await Client.DisposeAsync();
        }
    }

    /// <summary>Calls a tool and returns the parsed JSON body. Throws if the tool reported an error.</summary>
    public async Task<JsonElement> Call(string tool, object? args = null, TimeSpan? timeout = null)
    {
        var (json, isError) = await CallRaw(tool, args, timeout);
        if (isError)
        {
            throw new ToolFailedException(tool, json.ToString());
        }

        return json;
    }

    /// <summary>Calls a tool and returns body + error flag (for negative tests).</summary>
    public async Task<(JsonElement Json, bool IsError)> CallRaw(string tool, object? args = null, TimeSpan? timeout = null)
    {
        var dict = args is null
            ? new Dictionary<string, object?>()
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(args))!;
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(10));
        var result = await Client.CallToolAsync(tool, dict, cancellationToken: cts.Token);
        var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(t => t.Text));
        JsonElement json;
        try
        {
            json = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text).RootElement.Clone();
        }
        catch (JsonException)
        {
            json = JsonDocument.Parse(JsonSerializer.Serialize(new { raw = text })).RootElement.Clone();
        }

        return (json, result.IsError == true);
    }
}

public sealed class ToolFailedException(string tool, string body) : Exception($"Tool '{tool}' failed: {body}");

/// <summary>
/// Fixture with a live TcXaeShell and a fresh solution "McpIt" containing one PLC project "Plc1".
/// Shared by all XAE integration tests of a collection (starting the IDE takes ~30-60 s).
/// </summary>
public class XaeProjectFixture : McpServerFixture
{
    public string WorkDir { get; } = Path.Combine(Path.GetTempPath(), "twincat-mcp-it", Guid.NewGuid().ToString("N")[..8]);

    public const string SolutionName = "McpIt";
    public const string PlcName = "Plc1";

    /// <summary>Tree path of the nested PLC project ("TIPC^Plc1^Plc1 Project").</summary>
    public string PlcProjectPath => $"TIPC^{PlcName}^{PlcName} Project";

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Directory.CreateDirectory(WorkDir);
        await Call("xae_start", new { visible = false });
        await Call("solution_create", new { directory = WorkDir, name = SolutionName });
    }

    public override async Task DisposeAsync()
    {
        try
        {
            await CallRaw("xae_close", new { saveAll = false, quitIde = true }, TimeSpan.FromMinutes(2));
        }
        catch
        {
            // ignore
        }

        await base.DisposeAsync();
        try
        {
            Directory.Delete(WorkDir, true);
        }
        catch
        {
            // files may still be locked briefly
        }
    }
}

[CollectionDefinition(Name)]
public sealed class XaeCollection : ICollectionFixture<XaeProjectFixture>
{
    public const string Name = "XAE";
}
