using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace W3Editor.Mcp;

/// <summary>
/// Client for the resident W3Editor control port (MainWindow.Ipc.cs, default
/// 17322). Lets MCP take background screenshots without spawning a fresh
/// editor process per shot.
/// </summary>
public static class EditorIpcClient
{
    public const int DefaultPort = 17322;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };

    public static int Port =>
        int.TryParse(Environment.GetEnvironmentVariable("W3EDITOR_IPC_PORT"), out int p) ? p : DefaultPort;

    public static string BaseUrl => $"http://127.0.0.1:{Port}/";

    public static async Task<JsonNode?> TryPostAsync(object payload, CancellationToken ct, TimeSpan? timeout = null)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(120));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            };
            if (Environment.GetEnvironmentVariable("W3EDITOR_IPC_TOKEN") is { Length: > 0 } token)
                request.Headers.Add("X-W3E-Token", token);
            using var response = await Http.SendAsync(request, cts.Token);
            string body = await response.Content.ReadAsStringAsync(cts.Token);
            return JsonNode.Parse(body);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Editor not running / port closed — caller falls back. Log so a
            // real failure (bad JSON, auth) is still diagnosable on stderr.
            Console.Error.WriteLine($"[w3e-ipc] {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    public static async Task<bool> IsAliveAsync(CancellationToken ct)
    {
        JsonNode? pong = await TryPostAsync(new { cmd = "ping" }, ct, TimeSpan.FromSeconds(2));
        return pong?["ok"]?.GetValue<bool>() == true;
    }

    /// <summary>Starts a resident W3Editor (with control port) on the given map folder.</summary>
    public static async Task<int> LaunchResidentAsync(string mapPath, CancellationToken ct)
    {
        string exe = EditorSession.FindW3EditorExe()
            ?? throw new FileNotFoundException("W3Editor.exe not found. Build standalone/W3Editor first.");
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };
        psi.ArgumentList.Add("--open");
        psi.ArgumentList.Add(mapPath);
        var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start W3Editor.exe");

        // Wait for the control port to come up (map load included).
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (proc.HasExited)
                throw new InvalidOperationException($"W3Editor exited early (code {proc.ExitCode}).");
            if (await IsAliveAsync(ct))
                return proc.Id;
            await Task.Delay(500, ct);
        }
        throw new TimeoutException("W3Editor control port did not come up within 60 s.");
    }
}
