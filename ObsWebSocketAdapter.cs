using System;
using System.Buffers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PvpChronicleRecorder;

/// <summary>
/// Minimal OBS WebSocket v5 adapter.  All operations are serialized by RecordingController, so a
/// single request/response stream is sufficient and no game-thread callback waits on network IO.
/// </summary>
internal sealed class ObsWebSocketAdapter : IAsyncDisposable
{
    private ClientWebSocket? socket;
    private readonly Uri endpoint;
    private readonly string password;

    public ObsWebSocketAdapter(string endpoint, string password)
    {
        this.endpoint = new Uri(endpoint);
        this.password = password;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (socket?.State == WebSocketState.Open) return;
        socket?.Dispose();
        socket = new ClientWebSocket();
        // OBS normally runs on the same PC.  A system proxy or TUN must not be allowed to
        // intercept loopback WebSocket traffic; doing so produces a misleading connection
        // failure even while OBS is listening correctly on 127.0.0.1:4455.
        if (ShouldBypassProxy(endpoint)) socket.Options.Proxy = null;
        await socket.ConnectAsync(endpoint, cancellationToken);
        using var hello = JsonDocument.Parse(await ReceiveTextAsync(cancellationToken));
        if (hello.RootElement.GetProperty("op").GetInt32() != 0)
            throw new InvalidOperationException("OBS did not send a WebSocket v5 hello message.");
        var data = hello.RootElement.GetProperty("d");
        string? authentication = null;
        if (data.TryGetProperty("authentication", out var auth))
        {
            if (string.IsNullOrEmpty(password))
                throw new InvalidOperationException("OBS WebSocket requires a password.");
            authentication = BuildAuthentication(password,
                auth.GetProperty("salt").GetString()!, auth.GetProperty("challenge").GetString()!);
        }
        var identify = authentication == null
            ? JsonSerializer.Serialize(new { op = 1, d = new { rpcVersion = 1, eventSubscriptions = 0 } })
            : JsonSerializer.Serialize(new { op = 1, d = new { rpcVersion = 1, authentication, eventSubscriptions = 0 } });
        await SendTextAsync(identify, cancellationToken);
        using var identified = JsonDocument.Parse(await ReceiveTextAsync(cancellationToken));
        if (identified.RootElement.GetProperty("op").GetInt32() != 2)
            throw new InvalidOperationException("OBS rejected the WebSocket identification.");
    }

    public Task<JsonElement> StartRecordingAsync(CancellationToken token) => RequestAsync("StartRecord", token);
    public Task<JsonElement> StopRecordingAsync(CancellationToken token) => RequestAsync("StopRecord", token);
    public async Task<bool> IsRecordingAsync(CancellationToken token)
    {
        var response = await RequestAsync("GetRecordStatus", token);
        return response.TryGetProperty("outputActive", out var active) && active.GetBoolean();
    }

    private async Task<JsonElement> RequestAsync(string requestType, CancellationToken token)
    {
        await ConnectAsync(token);
        var requestId = Guid.NewGuid().ToString("N");
        await SendTextAsync(JsonSerializer.Serialize(new
        {
            op = 6,
            d = new { requestType, requestId }
        }), token);
        while (true)
        {
            using var message = JsonDocument.Parse(await ReceiveTextAsync(token));
            if (message.RootElement.GetProperty("op").GetInt32() != 7) continue;
            var data = message.RootElement.GetProperty("d");
            if (data.GetProperty("requestId").GetString() != requestId) continue;
            var status = data.GetProperty("requestStatus");
            if (!status.GetProperty("result").GetBoolean())
                throw new InvalidOperationException(status.TryGetProperty("comment", out var comment)
                    ? comment.GetString() : $"OBS request {requestType} failed.");
            return data.TryGetProperty("responseData", out var response)
                ? response.Clone() : JsonDocument.Parse("{}").RootElement.Clone();
        }
    }

    internal static string BuildAuthentication(string password, string salt, string challenge)
    {
        var secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }

    internal static bool ShouldBypassProxy(Uri endpoint) => endpoint.IsLoopback;

    private async Task SendTextAsync(string value, CancellationToken token)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        await socket!.SendAsync(bytes, WebSocketMessageType.Text, true, token);
    }

    private async Task<string> ReceiveTextAsync(CancellationToken token)
    {
        var rented = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            using var stream = new System.IO.MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket!.ReceiveAsync(rented, token);
                if (result.MessageType == WebSocketMessageType.Close)
                    throw new WebSocketException("OBS closed the WebSocket connection.");
                stream.Write(rented, 0, result.Count);
                if (stream.Length > 4 * 1024 * 1024)
                    throw new InvalidOperationException("OBS response exceeded the safety limit.");
            } while (!result.EndOfMessage);
            return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
        }
        finally { ArrayPool<byte>.Shared.Return(rented); }
    }

    public async ValueTask DisposeAsync()
    {
        if (socket is { State: WebSocketState.Open })
        {
            try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "plugin disposed", CancellationToken.None); }
            catch { }
        }
        socket?.Dispose();
    }
}
