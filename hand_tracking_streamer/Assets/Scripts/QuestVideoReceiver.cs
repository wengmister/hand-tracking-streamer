using System;
using System.Collections;
using System.Reflection;
using System.Threading.Tasks;
using Unity.WebRTC;
using UnityEngine;

public class QuestVideoReceiver : MonoBehaviour
{
    public event Action<string> OnLocalOfferReady;
    public event Action<string, string, int?> OnLocalIceCandidate;
    public event Action<Texture> OnRemoteTexture;
    public event Action<string> OnPeerStateChanged;
    public event Action<string> OnError;

    private RTCPeerConnection _peer;
    private VideoStreamTrack _remoteTrack;
    private Coroutine _updateCoroutine;
    private bool _updateRunning;

    public void InitializePeer()
    {
        if (!_updateRunning)
        {
            _updateCoroutine = StartCoroutine(WebRTC.Update());
            _updateRunning = true;
        }

        ClosePeer();
        _peer = new RTCPeerConnection();
        TryAddVideoRecvTransceiver();
        _peer.OnConnectionStateChange = state => OnPeerStateChanged?.Invoke(state.ToString());
        _peer.OnIceConnectionChange = state => OnPeerStateChanged?.Invoke($"ICE {state}");
        _peer.OnIceCandidate = candidate =>
        {
            if (candidate == null) return;
            OnLocalIceCandidate?.Invoke(candidate.Candidate, candidate.SdpMid, candidate.SdpMLineIndex);
        };
        _peer.OnTrack = e =>
        {
            if (e.Track is VideoStreamTrack vt)
            {
                _remoteTrack = vt;
                _remoteTrack.OnVideoReceived += texture => OnRemoteTexture?.Invoke(texture);
            }
        };
    }

    public Task<bool> CreateAndSendOfferAsync()
    {
        var tcs = new TaskCompletionSource<bool>();
        StartCoroutine(CreateOfferRoutine(tcs));
        return tcs.Task;
    }

    public Task<bool> SetRemoteAnswerAsync(string sdp)
    {
        var tcs = new TaskCompletionSource<bool>();
        StartCoroutine(SetRemoteAnswerRoutine(sdp, tcs));
        return tcs.Task;
    }

    public void AddRemoteIceCandidate(string candidate, string sdpMid, int? sdpMLineIndex)
    {
        if (_peer == null || string.IsNullOrWhiteSpace(candidate)) return;
        var init = new RTCIceCandidateInit
        {
            candidate = candidate,
            sdpMid = sdpMid,
            sdpMLineIndex = sdpMLineIndex ?? 0,
        };
        _peer.AddIceCandidate(new RTCIceCandidate(init));
    }

    public void ClosePeer()
    {
        if (_remoteTrack != null)
        {
            _remoteTrack.Dispose();
            _remoteTrack = null;
        }
        if (_peer != null)
        {
            _peer.Close();
            _peer.Dispose();
            _peer = null;
        }
    }

    private IEnumerator CreateOfferRoutine(TaskCompletionSource<bool> tcs)
    {
        if (_peer == null)
        {
            tcs.SetResult(false);
            yield break;
        }

        var op = _peer.CreateOffer();
        yield return op;
        if (op.IsError)
        {
            OnError?.Invoke($"CreateOffer failed: {op.Error.message}");
            tcs.SetResult(false);
            yield break;
        }

        var desc = op.Desc;
        var setLocal = _peer.SetLocalDescription(ref desc);
        yield return setLocal;
        if (setLocal.IsError)
        {
            OnError?.Invoke($"SetLocalDescription failed: {setLocal.Error.message}");
            tcs.SetResult(false);
            yield break;
        }

        OnLocalOfferReady?.Invoke(desc.sdp);
        tcs.SetResult(true);
    }

    private IEnumerator SetRemoteAnswerRoutine(string sdp, TaskCompletionSource<bool> tcs)
    {
        if (_peer == null)
        {
            tcs.SetResult(false);
            yield break;
        }

        var desc = new RTCSessionDescription
        {
            type = RTCSdpType.Answer,
            sdp = sdp,
        };
        var op = _peer.SetRemoteDescription(ref desc);
        yield return op;
        if (op.IsError)
        {
            OnError?.Invoke($"SetRemoteDescription failed: {op.Error.message}");
            tcs.SetResult(false);
            yield break;
        }

        tcs.SetResult(true);
    }

    private void OnDestroy()
    {
        ClosePeer();
        if (_updateRunning)
        {
            if (_updateCoroutine != null)
            {
                StopCoroutine(_updateCoroutine);
                _updateCoroutine = null;
            }
            _updateRunning = false;
        }
    }

    private void TryAddVideoRecvTransceiver()
    {
        if (_peer == null) return;
        try
        {
            MethodInfo[] methods = typeof(RTCPeerConnection).GetMethods();
            foreach (MethodInfo method in methods)
            {
                if (method.Name != "AddTransceiver") continue;
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length < 1) continue;
                if (parameters[0].ParameterType != typeof(TrackKind)) continue;

                object[] args = new object[parameters.Length];
                args[0] = TrackKind.Video;

                if (parameters.Length >= 2)
                {
                    Type initType = parameters[1].ParameterType;
                    object initValue = Activator.CreateInstance(initType);
                    PropertyInfo directionProp = initType.GetProperty("direction");
                    if (directionProp != null)
                    {
                        object recvOnly = Enum.Parse(directionProp.PropertyType, "RecvOnly");
                        directionProp.SetValue(initValue, recvOnly);
                    }
                    args[1] = initValue;
                }

                method.Invoke(_peer, args);
                OnPeerStateChanged?.Invoke("Added video recv transceiver");
                return;
            }

            OnError?.Invoke("No compatible AddTransceiver(TrackKind, ...) overload found.");
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"AddTransceiver failed: {ex.Message}");
        }
    }
}
