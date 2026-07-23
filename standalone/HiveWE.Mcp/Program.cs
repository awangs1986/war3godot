using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using HiveWE.Mcp;
using HiveWE.Mcp.Tools;

// CLI: HiveWE.Mcp.exe --selftest [map-folder]
if (args.Length >= 1 && args[0] == "--selftest")
{
    try
    {
        var session = new HiveWeSession();
        Console.Error.WriteLine(session.StatusJson());
        string? map = args.Length >= 2 ? args[1] : @"D:\maps\LostTemple";
        Console.Error.WriteLine(session.Launch(map));
        await session.WaitForIpcAsync(TimeSpan.FromSeconds(45), CancellationToken.None);
        Console.Error.WriteLine("ipc " + await session.IpcStatusAsync(CancellationToken.None));
        Console.Error.WriteLine("brush " + await session.IpcBrushStampAsync(
            "raise", 64, 64, size: 8, strength: 0.5, tileId: null, CancellationToken.None));
        string png = await session.ScreenshotAsync(null, waitMs: 20000, CancellationToken.None);
        Console.Error.WriteLine("screenshot " + png);
        Console.Error.WriteLine(session.Close(force: true));
        Console.WriteLine(png);
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("ERROR: " + ex);
        return 1;
    }
}

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddSingleton<HiveWeSession>();
builder.Services.AddSingleton<HiveWeTools>();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
return 0;
