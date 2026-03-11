using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

public class VideoStreamManager : MonoBehaviour
{
    public enum SessionState
    {
        Idle,
        SignalingConnecting,
        OfferSent,
        Connected,
        Playing,
        Stopping,
    }

    [SerializeField] private QuestVideoReceiver videoReceiver;
    [SerializeField] private VideoPanelRenderer panelRenderer;
    [SerializeField] private VideoStatsOverlay statsOverlay;
    [SerializeField] private string logSource = "Left";

    private VideoSignalingClient _signalingClient;
    private SessionState _state = SessionState.Idle;
    private string _sessionId;
    private bool _isStopping;
    private readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();

    public SessionState CurrentState => _state;

    public void SetVideoUiVisible(bool visible)
    {
        panelRenderer?.SetVisible(visible);
        if (!visible)
        {
            statsOverlay?.SetVisible(false);
        }
    }

    public async Task<bool> StartVideoSession(
        string signalingHost,
        int signalingPort,
        string preset,
        bool showDebugStats
    )
    {
        if (_state != SessionState.Idle) return false;
        if (videoReceiver == null)
        {
            FailFatal("Video receiver missing.");
            return false;
        }

        _sessionId = Guid.NewGuid().ToString("N");
        _signalingClient = new VideoSignalingClient();
        _signalingClient.OnEnvelope += envelope => EnqueueMainThread(() => HandleEnvelope(envelope));
        _signalingClient.OnError += error => EnqueueMainThread(() => FailFatal(error));
        _signalingClient.OnConnected += () => EnqueueMainThread(() =>
        {
            _state = SessionState.Connected;
            statsOverlay?.SetSignalingState("connected");
        });
        _signalingClient.OnDisconnected += () => EnqueueMainThread(() =>
        {
            statsOverlay?.SetSignalingState("disconnected");
        });

        _state = SessionState.SignalingConnecting;
        SetVideoUiVisible(true);
        panelRenderer?.SetStatus("Connecting Video...");
        statsOverlay?.SetVisible(showDebugStats);
        statsOverlay?.SetPreset(preset);
        statsOverlay?.SetSignalingState("connecting");
        LogInfo($"session={_sessionId} signaling connect {signalingHost}:{signalingPort} preset={preset}");

        bool connected = await _signalingClient.ConnectAsync(signalingHost, signalingPort, 4000);
        if (!connected)
        {
            FailFatal("Signaling connection failed.");
            return false;
        }
        panelRenderer?.SetStatus("Signaling Connected");
        LogInfo("signaling connected");

        videoReceiver.InitializePeer();
        videoReceiver.OnLocalOfferReady += OnLocalOfferReady;
        videoReceiver.OnLocalIceCandidate += OnLocalIceCandidate;
        videoReceiver.OnRemoteTexture += OnRemoteTexture;
        videoReceiver.OnPeerStateChanged += OnPeerStateChanged;
        videoReceiver.OnError += FailFatal;

        await _signalingClient.SendAsync("hello", _sessionId, $"{{\"app_version\":\"{Application.version}\",\"video_preset\":\"{preset}\",\"client_name\":\"quest\"}}");
        await _signalingClient.SendAsync("start_video", _sessionId, "{}");
        panelRenderer?.SetStatus("Starting Video...");
        LogInfo("sent hello + start_video");

        _state = SessionState.OfferSent;
        bool offerOk = await videoReceiver.CreateAndSendOfferAsync();
        if (!offerOk)
        {
            FailFatal("Offer creation failed.");
            return false;
        }
        LogInfo("local offer created");

        return true;
    }

    public async Task StopVideoSession(string reason)
    {
        if (_isStopping) return;
        _isStopping = true;
        _state = SessionState.Stopping;
        statsOverlay?.SetSignalingState("stopping");
        LogInfo($"stopping session reason={reason}");

        try
        {
            if (_signalingClient != null && _signalingClient.IsConnected && !string.IsNullOrWhiteSpace(_sessionId))
            {
                await _signalingClient.SendAsync(
                    "stop_video",
                    _sessionId,
                    new JObject { ["reason"] = reason ?? "stop" }.ToString(Newtonsoft.Json.Formatting.None)
                );
            }
        }
        catch { }

        if (videoReceiver != null)
        {
            videoReceiver.OnLocalOfferReady -= OnLocalOfferReady;
            videoReceiver.OnLocalIceCandidate -= OnLocalIceCandidate;
            videoReceiver.OnRemoteTexture -= OnRemoteTexture;
            videoReceiver.OnPeerStateChanged -= OnPeerStateChanged;
            videoReceiver.OnError -= FailFatal;
            videoReceiver.ClosePeer();
        }

        if (_signalingClient != null)
        {
            _signalingClient.Dispose();
            _signalingClient = null;
        }

        panelRenderer?.Clear();
        panelRenderer?.SetStatus("Video Off");
        statsOverlay?.SetSignalingState("idle");
        statsOverlay?.SetPeerState("idle");

        _sessionId = string.Empty;
        _state = SessionState.Idle;
        _isStopping = false;
        LogInfo("session stopped");
    }

    private async void OnLocalOfferReady(string sdp)
    {
        if (_signalingClient == null) return;
        if (string.IsNullOrWhiteSpace(sdp))
        {
            FailFatal("Local SDP offer is empty.");
            return;
        }

        bool hasVideoMLine = sdp.Contains("m=video");
        LogInfo($"local offer sdp_len={sdp.Length} has_video_mline={hasVideoMLine}");
        if (!hasVideoMLine)
        {
            FailFatal("Local SDP offer missing m=video. Receiver transceiver not configured.");
            return;
        }

        await _signalingClient.SendAsync(
            "offer",
            _sessionId,
            new JObject { ["sdp"] = sdp ?? string.Empty }.ToString(Newtonsoft.Json.Formatting.None)
        );
        LogInfo("offer sent to host");
    }

    private async void OnLocalIceCandidate(string candidate, string sdpMid, int? sdpMLineIndex)
    {
        if (_signalingClient == null) return;
        var payload = new JObject
        {
            ["candidate"] = candidate ?? string.Empty,
            ["sdpMid"] = sdpMid,
            ["sdpMLineIndex"] = sdpMLineIndex.HasValue ? sdpMLineIndex.Value : JValue.CreateNull(),
        };
        await _signalingClient.SendAsync(
            "ice_candidate",
            _sessionId,
            payload.ToString(Newtonsoft.Json.Formatting.None)
        );
        LogDebug("local ICE sent");
    }

    private void OnRemoteTexture(Texture texture)
    {
        panelRenderer?.SetTexture(texture);
        panelRenderer?.SetStatus(string.Empty);
        _state = SessionState.Playing;
        LogInfo("remote texture received (playing)");
    }

    private void OnPeerStateChanged(string state)
    {
        statsOverlay?.SetPeerState(state);
        LogDebug($"peer state: {state}");
    }

    private async void HandleEnvelope(VideoSignalingClient.Envelope envelope)
    {
        if (envelope.SessionId != _sessionId) return;

        switch (envelope.Type)
        {
            case "hello_ack":
                statsOverlay?.SetSignalingState("ready");
                LogInfo("hello_ack received");
                break;
            case "answer":
                await HandleAnswer(envelope.PayloadJson);
                break;
            case "ice_candidate":
                HandleRemoteIce(envelope.PayloadJson);
                break;
            case "video_state":
                HandleVideoState(envelope.PayloadJson);
                break;
            case "stats":
                HandleStats(envelope.PayloadJson);
                break;
            case "error":
                HandleServerError(envelope.PayloadJson);
                break;
            case "pong":
                LogDebug("pong received");
                break;
            default:
                LogDebug($"unknown envelope type={envelope.Type}");
                break;
        }
    }

    private void Update()
    {
        while (_mainThreadActions.TryDequeue(out Action action))
        {
            try { action?.Invoke(); } catch { }
        }
    }

    private async Task HandleAnswer(string payloadJson)
    {
        try
        {
            JObject root = JObject.Parse(payloadJson);
            string sdp = root.Value<string>("sdp");
            if (string.IsNullOrWhiteSpace(sdp))
            {
                FailFatal("Empty SDP answer.");
                return;
            }

            bool ok = await videoReceiver.SetRemoteAnswerAsync(sdp);
            if (!ok) FailFatal("Failed to set remote answer.");
            LogInfo("answer applied");
        }
        catch (Exception ex)
        {
            FailFatal($"Invalid answer payload: {ex.Message}");
        }
    }

    private void HandleRemoteIce(string payloadJson)
    {
        try
        {
            JObject root = JObject.Parse(payloadJson);
            string candidate = root.Value<string>("candidate") ?? string.Empty;
            string sdpMid = root["sdpMid"]?.Type == JTokenType.Null ? null : root.Value<string>("sdpMid");
            int? sdpMLineIndex = root["sdpMLineIndex"]?.Type == JTokenType.Integer
                ? root.Value<int?>("sdpMLineIndex")
                : null;
            videoReceiver.AddRemoteIceCandidate(candidate, sdpMid, sdpMLineIndex);
            LogDebug("remote ICE applied");
        }
        catch (Exception ex)
        {
            FailFatal($"Invalid remote ICE payload: {ex.Message}");
        }
    }

    private void HandleVideoState(string payloadJson)
    {
        try
        {
            JObject root = JObject.Parse(payloadJson);
            string state = root.Value<string>("state") ?? "unknown";
            string reason = root.Value<string>("reason") ?? string.Empty;
            statsOverlay?.SetSignalingState(state);
            LogInfo($"video_state={state} reason={reason}");
            if (state == "error")
            {
                FailFatal($"Video state error: {reason}");
            }
        }
        catch (Exception ex)
        {
            FailFatal($"Invalid video_state payload: {ex.Message}");
        }
    }

    private void HandleStats(string payloadJson)
    {
        try
        {
            JObject root = JObject.Parse(payloadJson);
            float fps = root.Value<float?>("fps") ?? 0f;
            float bitrate = root.Value<float?>("bitrate_kbps") ?? 0f;
            int drops = root.Value<int?>("frame_drops") ?? 0;
            float rtt = root["rtt_ms"]?.Type == JTokenType.Null ? -1f : (root.Value<float?>("rtt_ms") ?? -1f);
            statsOverlay?.SetStats(fps, bitrate, drops, rtt);
            LogDebug($"stats fps={fps:F1} bitrate={bitrate:F0} drops={drops} rtt={rtt:F1}");
        }
        catch { }
    }

    private void HandleServerError(string payloadJson)
    {
        try
        {
            JObject root = JObject.Parse(payloadJson);
            string message = root.Value<string>("message") ?? "Unknown video error";
            FailFatal(message);
        }
        catch (Exception ex)
        {
            FailFatal($"Video error payload parse failed: {ex.Message}");
        }
    }

    private async void FailFatal(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) reason = "Unknown video failure";
        statsOverlay?.SetError(reason);
        panelRenderer?.SetStatus($"Video Error: {reason}");
        if (LogManager.Instance != null)
        {
            LogManager.Instance.Log(logSource, $"[Video] {reason}");
        }

        if (_state != SessionState.Idle)
        {
            await StopVideoSession("fatal_error");
        }

        if (AppManager.Instance != null && AppManager.Instance.isStreaming)
        {
            AppManager.Instance.HandleDisconnection($"Video failure: {reason}");
        }
    }

    private void EnqueueMainThread(Action action)
    {
        if (action != null) _mainThreadActions.Enqueue(action);
    }

    private void LogDebug(string msg)
    {
        if (LogManager.Instance == null) return;
        bool shouldLog = AppManager.Instance != null && AppManager.Instance.ShowDebugInfo;
        if (!shouldLog) return;
        LogManager.Instance.Log(logSource, $"[VideoDebug] {msg}");
    }

    private void LogInfo(string msg)
    {
        if (LogManager.Instance == null) return;
        LogManager.Instance.Log(logSource, $"[Video] {msg}");
    }
}
