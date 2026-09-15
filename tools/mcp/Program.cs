using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using OpenCommonwealth.Services.Hkx;
using BehaviourStudio.Mcp;

var roots = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    if (args[i] != "--root")
    {
        Console.Error.WriteLine($"unknown argument: {args[i]}");
        return 2;
    }
    if (++i >= args.Length)
    {
        Console.Error.WriteLine("--root requires a directory");
        return 2;
    }
    roots.Add(args[i]);
}

if (!McpPathPolicy.TryCreate(roots, out var paths, out string startupError))
{
    Console.Error.WriteLine(startupError);
    return 2;
}

var builder = Host.CreateApplicationBuilder();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSingleton(paths!);
builder.Services.AddSingleton<AssistantInspection>();
builder.Services.AddSingleton<AssistantDiskMutation>();
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<McpTools>();

await builder.Build().RunAsync();
return 0;
