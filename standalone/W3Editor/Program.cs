using Avalonia;

namespace W3Editor;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--terrain-render-smoke"))
        {
            Environment.ExitCode = Rendering.TerrainRenderingSmoke.Run();
            return;
        }

        if (args.Any(a => a == "--plugin-preview-smoke"))
        {
            string result = Rendering.PluginPreviewPass.SmokeTest();
            if (result == "pass")
            {
                Console.WriteLine("{\"pass\":true}");
                Environment.Exit(0);
            }
            else
            {
                Console.WriteLine($"{{\"pass\":false,\"error\":\"{result}\"}}");
                Environment.Exit(1);
            }
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
