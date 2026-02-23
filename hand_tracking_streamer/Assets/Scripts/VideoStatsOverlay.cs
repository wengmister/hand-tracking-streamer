using TMPro;
using UnityEngine;

public class VideoStatsOverlay : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI overlayText;
    [SerializeField] private bool visibleByDefault = false;

    private bool _visible;
    private string _signalingState = "idle";
    private string _peerState = "idle";
    private float _fps;
    private float _bitrateKbps;
    private int _frameDrops;
    private float _rttMs = -1f;
    private string _lastError = string.Empty;
    private string _preset = "720p30";

    private void Start()
    {
        _visible = visibleByDefault;
        Refresh();
    }

    public void SetVisible(bool visible)
    {
        _visible = visible;
        Refresh();
    }

    public void SetPreset(string preset)
    {
        _preset = string.IsNullOrWhiteSpace(preset) ? "720p30" : preset;
        Refresh();
    }

    public void SetSignalingState(string state)
    {
        _signalingState = state;
        Refresh();
    }

    public void SetPeerState(string state)
    {
        _peerState = state;
        Refresh();
    }

    public void SetStats(float fps, float bitrateKbps, int frameDrops, float rttMs)
    {
        _fps = fps;
        _bitrateKbps = bitrateKbps;
        _frameDrops = frameDrops;
        _rttMs = rttMs;
        Refresh();
    }

    public void SetError(string error)
    {
        _lastError = error;
        Refresh();
    }

    private void Refresh()
    {
        if (overlayText == null) return;
        overlayText.gameObject.SetActive(_visible);
        if (!_visible) return;
        overlayText.text =
            $"Video Preset: {_preset}\n" +
            $"Signaling: {_signalingState}\n" +
            $"Peer: {_peerState}\n" +
            $"FPS: {_fps:F1}\n" +
            $"Bitrate: {_bitrateKbps:F0} kbps\n" +
            $"Drops: {_frameDrops}\n" +
            $"RTT: {(_rttMs < 0 ? "n/a" : _rttMs.ToString("F1"))} ms\n" +
            $"Error: {(_lastError == string.Empty ? "-" : _lastError)}";
    }
}
