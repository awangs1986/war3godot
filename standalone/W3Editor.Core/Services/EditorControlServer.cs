using System.Net;
using System.Text;

namespace W3Editor.Services;

/// <summary>
/// Localhost JSON control port for background automation (MCP / scripts),
/// mirroring HiveWE's ControlServer: POST / with {"cmd": ...} returns JSON.
/// The dispatch callback is responsible for marshalling to the UI thread.
/// </summary>
public sealed class EditorControlServer : IDisposable
{
    public const int DefaultPort = 17322;

    private readonly HttpListener _listener = new();
    private readonly Func<string, Task<string>> _dispatch;
    private readonly CancellationTokenSource _cts = new();
    private readonly string? _token = Environment.GetEnvironmentVariable("W3EDITOR_IPC_TOKEN");

    public int Port { get; }

    public EditorControlServer(int port, Func<string, Task<string>> dispatch)
    {
        Port = port;
        _dispatch = dispatch;
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        _ = Task.Run(LoopAsync);
    }

    private async Task LoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync();
            }
            catch (Exception) when (_cts.IsCancellationRequested || !_listener.IsListening)
            {
                return;
            }
            catch (Exception ex)
            {
                // Transient accept failure must not kill the loop — a dead
                // accept loop looks like a healthy process with silent IPC.
                Console.Error.WriteLine($"[ipc] accept error: {ex.Message}");
                continue;
            }
            _ = Task.Run(() => HandleAsync(ctx));
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        string response;
        int status = 200;
        try
        {
            // Require a JSON content type: browsers can't send it cross-origin
            // without a CORS preflight (which we never answer), closing the
            // drive-by "simple POST from any web page" hole.
            string contentType = ctx.Request.ContentType ?? "";
            if (!contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
            {
                status = 415;
                response = "{\"ok\":false,\"error\":\"Content-Type must be application/json\"}";
            }
            else if (!string.IsNullOrEmpty(_token)
                     && ctx.Request.Headers["X-W3E-Token"] != _token)
            {
                status = 403;
                response = "{\"ok\":false,\"error\":\"bad or missing X-W3E-Token\"}";
            }
            else
            {
                using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
                string body = await reader.ReadToEndAsync();
                response = await _dispatch(body);
            }
        }
        catch (Exception ex)
        {
            response = "{\"ok\":false,\"error\":" + System.Text.Json.JsonSerializer.Serialize(ex.Message) + "}";
        }
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(response);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        }
        catch
        {
            // Client went away; nothing to do.
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
    }
}
