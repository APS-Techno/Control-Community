using APS.Mcp;
using APS.Wire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// stdout is the MCP channel — ALL logging must go to stderr.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

var cfg = McpBridgeConfig.Load(McpBridgeConfig.ResolvePath(args));
builder.Services.AddSingleton(cfg);
builder.Services.AddSingleton<WireClient>();
builder.Services.AddSingleton<IWireCaller>(sp => sp.GetRequiredService<WireClient>());

builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<ControlTools>();

await builder.Build().RunAsync();
