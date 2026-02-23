using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed class VideoSignalingClient : IDisposable
{
    public sealed class Envelope
    {
        public string Type;
        public string SessionId;
        public string PayloadJson;
    }

    public event Action<Envelope> OnEnvelope;
    public event Action<string> OnError;
    public event Action OnConnected;
    public event Action OnDisconnected;

    private ClientWebSocket _socket;
    private CancellationTokenSource _cts;
    private Task _receiveLoopTask;

    public bool IsConnected => _socket != null && _socket.State == WebSocketState.Open;

    public async Task<bool> ConnectAsync(string host, int port, int timeoutMs = 4000)
    {
        try
        {
            await DisconnectAsync();
            _cts = new CancellationTokenSource();
            _socket = new ClientWebSocket();
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            connectCts.CancelAfter(timeoutMs);
            await _socket.ConnectAsync(new Uri($"ws://{host}:{port}"), connectCts.Token);
            _receiveLoopTask = ReceiveLoopAsync(_cts.Token);
            OnConnected?.Invoke();
            return true;
        }
        catch (OperationCanceledException)
        {
            OnError?.Invoke($"Signaling connect timed out: ws://{host}:{port}");
            return false;
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Signaling connect failed: ws://{host}:{port} {ex.Message}");
            return false;
        }
    }

    public async Task SendAsync(string type, string sessionId, string payloadJson)
    {
        if (!IsConnected) return;
        string raw = BuildEnvelopeJson(type, sessionId, payloadJson);
        byte[] bytes = Encoding.UTF8.GetBytes(raw);
        await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
    }

    public async Task DisconnectAsync()
    {
        if (_cts != null)
        {
            try { _cts.Cancel(); } catch { }
            _cts.Dispose();
            _cts = null;
        }

        if (_socket != null)
        {
            try
            {
                if (_socket.State == WebSocketState.Open || _socket.State == WebSocketState.CloseReceived)
                {
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "client_close", CancellationToken.None);
                }
            }
            catch { }
            _socket.Dispose();
            _socket = null;
        }

        if (_receiveLoopTask != null)
        {
            try { await _receiveLoopTask; } catch { }
            _receiveLoopTask = null;
        }

        OnDisconnected?.Invoke();
    }

    public void Dispose()
    {
        _ = DisconnectAsync();
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        byte[] buffer = new byte[16 * 1024];
        var sb = new StringBuilder();

        while (!token.IsCancellationRequested && _socket != null)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Signaling receive failed: {ex.Message}");
                break;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (!result.EndOfMessage) continue;

            string raw = sb.ToString();
            sb.Clear();
            TryDispatchEnvelope(raw);
        }
    }

    private void TryDispatchEnvelope(string raw)
    {
        try
        {
            JObject root = JObject.Parse(raw);
            string type = root.Value<string>("type") ?? string.Empty;
            string sessionId = root.Value<string>("session_id") ?? string.Empty;
            string payload = root["payload"] != null ? root["payload"].ToString() : "{}";
            OnEnvelope?.Invoke(new Envelope
            {
                Type = type,
                SessionId = sessionId,
                PayloadJson = payload,
            });
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Invalid signaling payload: {ex.Message}");
        }
    }

    private static string BuildEnvelopeJson(string type, string sessionId, string payloadJson)
    {
        var payload = ParsePayloadOrEmpty(payloadJson);
        var envelope = new JObject
        {
            ["type"] = type ?? string.Empty,
            ["session_id"] = sessionId ?? string.Empty,
            ["payload"] = payload,
        };
        return envelope.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static JToken ParsePayloadOrEmpty(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return new JObject();
        }
        try
        {
            return JToken.Parse(payloadJson);
        }
        catch
        {
            return new JObject();
        }
    }
}
