using System.Diagnostics;
using System.Text.Json;
using W3Editor.PluginContracts;
using W3Editor.Services;

namespace W3Editor.Mcp;

/// <summary>
/// Plugin host acceptance: build a minimal plugin DLL into a temp plugins dir,
/// load via <see cref="W3Editor.Plugins.PluginLoader"/> and assert one plugin with
/// Id <c>com.d1.test-plugin</c>. Usage: --plugin-host-selftest [outDir]
/// </summary>
internal static class PluginHostSelfTest
{
    public static int Run(string[] args)
    {
        string outDir = args.Length >= 2
            ? Path.GetFullPath(args[1])
            : Path.Combine(EditorPaths.OutDir, "plugin_host_selftest");
        Directory.CreateDirectory(outDir);
        string pluginsDir = Path.Combine(outDir, "plugins");
        Directory.CreateDirectory(pluginsDir);

        string? buildError = BuildTestPlugin(outDir, pluginsDir);
        if (buildError != null)
        {
            Console.Error.WriteLine(buildError);
            return 1;
        }

        var loaded = W3Editor.Plugins.PluginLoader.LoadAll(pluginsDir);

        bool pass = loaded.Count == 1 && loaded[0].Id == "com.d1.test-plugin";
        var report = new
        {
            pluginsDir,
            loaded = loaded.Count,
            ids = loaded.Select(p => p.Id).ToArray(),
            expectedId = "com.d1.test-plugin",
            pass,
        };
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        string jsonPath = Path.Combine(outDir, "plugin_host_selftest.json");
        File.WriteAllText(jsonPath, json);
        Console.WriteLine(json);
        return pass ? 0 : 1;
    }

    private static string? BuildTestPlugin(string outDir, string pluginsDir)
    {
        string? contractsCsproj = FindPluginContractsCsproj();
        if (contractsCsproj == null)
            return "PluginHostSelfTest: W3Editor.PluginContracts.csproj not found";

        string projectDir = Path.Combine(outDir, "test_plugin_src");
        Directory.CreateDirectory(projectDir);
        string csprojPath = Path.Combine(projectDir, "TestPlugin.csproj");
        string pluginPath = Path.Combine(projectDir, "TestPlugin.cs");

        string refPath = contractsCsproj.Replace('\\', '/');
        File.WriteAllText(csprojPath, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <AssemblyName>com.d1.test-plugin</AssemblyName>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{refPath}" />
              </ItemGroup>
            </Project>
            """);

        File.WriteAllText(pluginPath, """
            using W3Editor.PluginContracts;

            public sealed class TestPlugin : IW3EditorPlugin
            {
                public string Id => "com.d1.test-plugin";
                public string Name => "Test Plugin";
                public string Version => "1.0.0";
                public void Register(IEditorPluginContext ctx) { }
            }
            """);

        var psi = new ProcessStartInfo("dotnet", $"build \"{csprojPath}\" -c Release -o \"{pluginsDir}\" --nologo -v q")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(psi)!;
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            return $"PluginHostSelfTest: dotnet build failed ({process.ExitCode})\n{stdout}\n{stderr}";
        return null;
    }

    private static string? FindPluginContractsCsproj()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            foreach (string candidate in new[]
                     {
                         Path.Combine(dir, "W3Editor.PluginContracts", "W3Editor.PluginContracts.csproj"),
                         Path.Combine(dir, "standalone", "W3Editor.PluginContracts", "W3Editor.PluginContracts.csproj"),
                     })
            {
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        string fromAssembly = typeof(IW3EditorPlugin).Assembly.Location;
        if (fromAssembly.Length > 0)
        {
            dir = Path.GetDirectoryName(fromAssembly);
            while (dir != null)
            {
                string candidate = Path.Combine(dir, "W3Editor.PluginContracts.csproj");
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
                dir = Directory.GetParent(dir)?.FullName;
            }
        }

        return null;
    }
}
