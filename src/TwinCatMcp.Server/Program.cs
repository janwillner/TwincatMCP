using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TwinCatMcp.Core;

if (args.Contains("--tool-catalog"))
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    Console.Write(ToolCatalog.Generate(typeof(XaeSession).Assembly));
    return;
}

var builder = Host.CreateApplicationBuilder(args);

// stdout is reserved for the MCP protocol: all logs go to stderr.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(
    Enum.TryParse<LogLevel>(Environment.GetEnvironmentVariable("TWINCAT_MCP_LOGLEVEL"), true, out var lvl) ? lvl : LogLevel.Information);

builder.Services.AddSingleton<StaComThread>();
builder.Services.AddSingleton<XaeSession>();
builder.Services.AddSingleton<TwinCatMcp.Ads.AdsSessionManager>();

builder.Services
    .AddMcpServer(o =>
    {
        o.ServerInfo = new() { Name = "twincat-mcp", Version = typeof(XaeSession).Assembly.GetName().Version?.ToString() ?? "0.1.0" };
        o.ServerInstructions = ServerInstructions.Text;
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithResourcesFromAssembly()
    .WithPromptsFromAssembly();

await builder.Build().RunAsync();
