using UnityEngine;
using Oculus.Interaction.Input;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System;

public class HandLandmarkStreamer : MonoBehaviour
{
    public enum HandSide { Left, Right }

    [Header("Configuration")]
    [Tooltip("Which hand data to track and stream")]
    [SerializeField] private HandSide _handSide;
    
    [Header("Logging")]
    [SerializeField] private bool _logToHUD = true;
    
    [Tooltip("The Log Source name to push text to (e.g., 'Right' to see everything on the right hand panel)")]
    [SerializeField] private string _hudLogSource = "Right"; // Default to Right as requested
    
    [SerializeField] private float _frequency = 0.01f; // 100Hz

    private IHand _hand;
    private float _timer;
    private float _nextSendTime;

    // Public Accessors
    public IHand Hand => _hand; 
    public HandSide Side => _handSide;
    
    // Networking
    private UdpClient _udpClient;
    private IAsyncResult _udpReceiveResult;
    private TcpClient _tcpClient;
    private NetworkStream _tcpStream;
    private IPEndPoint _remoteEndPoint;
    private bool _isInitialized = false;
    private int _currentProtocol = -1; 
    private AndroidJavaObject _wifiLock;

    // Optimization: Cache StringBuilders
    private StringBuilder _sbPacket = new StringBuilder(2048);
    private StringBuilder _sbLog = new StringBuilder(2048);
    
    // Indices for the 21 standard landmarks (Sending)
    private readonly int[] _streamedJoints = {
        1, 2, 3, 4, 5,       // Wrist, Thumb
        7, 8, 9, 10,         // Index
        12, 13, 14, 15,      // Middle
        17, 18, 19, 20,      // Ring
        22, 23, 24, 25       // Pinky
    };

    // Indices for the HUD Display (Wrist + Tips only)
    private readonly int[] _displayJoints = { 
        5,  // Thumb Tip
        10, // Index Tip
        15, // Middle Tip
        20, // Ring Tip
        25  // Pinky Tip
    };

    private void Start()
    {
        _hand = GetComponent<IHand>();
        if (_hand == null)
        {
            LogHUD("Error: No IHand component found!");
            enabled = false;
            return;
        }
        _hand.WhenHandUpdated += OnHandUpdated;
    }

    private void OnDestroy()
    {
        if (_hand != null) _hand.WhenHandUpdated -= OnHandUpdated;
        Disconnect();
        ReleaseWifiLock();
    }

    private void OnHandUpdated()
    {
        // 1. Check AppManager State
        if (AppManager.Instance == null || !AppManager.Instance.isStreaming)
        {
            if (_isInitialized) Disconnect();
            return;
        }

        // 2. Check Hand Mode (Logic unchanged: still checks if this hand should be streaming)
        int mode = AppManager.Instance.SelectedHandMode;
        if (mode == 1 && _handSide == HandSide.Right) return;
        if (mode == 2 && _handSide == HandSide.Left) return;

        // 3. Init Network
        if (!_isInitialized) InitializeNetwork();

        // 4. Rate Limiting (stable pacing)
        if (_nextSendTime <= 0f)
        {
            _nextSendTime = Time.unscaledTime;
        }
        if (Time.unscaledTime >= _nextSendTime)
        {
            _nextSendTime = Time.unscaledTime + _frequency;
            ProcessHandData();
        }
    }

    private void ProcessHandData()
    {
        if (!_hand.IsTrackedDataValid) return;

        _sbPacket.Clear();
        _sbLog.Clear();

        // --- 1. PROCESS WRIST ---
        if (_hand.GetRootPose(out Pose rootPose))
        {
            // Prepare Network Packet
            _sbPacket.Append(_handSide).Append(" wrist:, ");
            AppendVector3(_sbPacket, rootPose.position);
            _sbPacket.Append(", ");
            AppendQuaternion(_sbPacket, rootPose.rotation);

            // Prepare HUD Log
            if (_logToHUD)
            {
                // Added _handSide label to log so you know which hand is which on the shared screen
                _sbLog.AppendLine($"=== [{_handSide}] Wrist ==="); 
                _sbLog.AppendLine($"Pos: {rootPose.position.ToString("F3")}");
                // Optional: Comment out rotation if it clutters the shared screen too much
                // _sbLog.AppendLine($"Rot: {rootPose.rotation.eulerAngles.ToString("F0")}");
            }
        }

        // --- 2. PROCESS LANDMARKS ---
        if (_hand.GetJointPosesFromWrist(out ReadOnlyHandJointPoses joints))
        {
            // Network Packet
            _sbPacket.Append("\n").Append(_handSide).Append(" landmarks:");
            
            foreach (int index in _streamedJoints)
            {
                if (index < joints.Count)
                {
                    _sbPacket.Append(", ");
                    AppendVector3(_sbPacket, joints[index].position);
                }
                else
                {
                    _sbPacket.Append(", 0, 0, 0");
                }
            }

            // HUD Log
            if (_logToHUD)
            {
                _sbLog.AppendLine($"=== [{_handSide}] Landmarks ===");
                for (int i = 0; i < _displayJoints.Length; i++)
                {
                    int jointIndex = _displayJoints[i];
                    if (jointIndex < joints.Count)
                    {
                        string name = GetRenumberedJointName(i);
                        Vector3 pos = joints[jointIndex].position;
                        _sbLog.AppendLine($"{name}: {pos.ToString("F3")}");
                    }
                }
                
                // Final Push to HUD using the CUSTOM SOURCE
                LogHUD(_sbLog.ToString());
            }
        }

        // --- 3. SEND NETWORK DATA ---
        SendData(_sbPacket.ToString());
    }

    // --- NETWORK HELPERS ---
    private void InitializeNetwork()
    {
        string ip = AppManager.Instance.ServerIP;
        int port = AppManager.Instance.ServerPort;
        _currentProtocol = AppManager.Instance.SelectedProtocol;

        try
        {
            if (_currentProtocol == 0) // UDP
            {
                _udpClient = new UdpClient();
                _remoteEndPoint = new IPEndPoint(IPAddress.Parse(ip), port);
                // Only connect for unicast targets; broadcast connect can throw.
                if (!_remoteEndPoint.Address.Equals(IPAddress.Broadcast))
                {
                    _udpClient.Connect(_remoteEndPoint);
                    StartUdpReceive();
                }
                // Log success to the configured HUD source
                LogHUD($"UDP Ready: {ip}:{port}");
            }
            else // TCP
            {
                _tcpClient = new TcpClient();
                _tcpClient.Connect(ip, port);
                _tcpStream = _tcpClient.GetStream();
                LogHUD($"TCP Connected: {ip}:{port}");
            }
            _isInitialized = true;
            AcquireWifiLock();
        }
        catch (Exception ex)
        {
            LogHUD($"Conn Error: {ex.Message}");
            AppManager.Instance.StopStreaming();
        }
    }

    private void SendData(string message)
    {
        try
        {
            if (_currentProtocol == 0) // UDP
            {
                byte[] data = Encoding.UTF8.GetBytes(message);
                _udpClient.Send(data, data.Length, _remoteEndPoint);
            }
            else if (_currentProtocol == 1 && _tcpStream != null && _tcpStream.CanWrite) // TCP
            {
                byte[] data = Encoding.UTF8.GetBytes(message + "\n");
                _tcpStream.Write(data, 0, data.Length);
            }
        }
        catch (Exception)
        {
            Disconnect();
        }
    }

    private void Disconnect()
    {
        try
        {
            if (_udpClient != null)
            {
                _udpClient.Close();
                _udpClient = null;
                _udpReceiveResult = null;
            }
            if (_tcpStream != null) { _tcpStream.Close(); _tcpStream = null; }
            if (_tcpClient != null) { _tcpClient.Close(); _tcpClient = null; }
        }
        catch { }
        _isInitialized = false;
        _nextSendTime = 0f;
        ReleaseWifiLock();
    }

    // --- UTILITY HELPERS ---
    private void AppendVector3(StringBuilder sb, Vector3 vec)
    {
        sb.Append(vec.x.ToString("F4")).Append(", ")
          .Append(vec.y.ToString("F4")).Append(", ")
          .Append(vec.z.ToString("F4"));
    }

    private void AppendQuaternion(StringBuilder sb, Quaternion q)
    {
        sb.Append(q.x.ToString("F3")).Append(", ")
          .Append(q.y.ToString("F3")).Append(", ")
          .Append(q.z.ToString("F3")).Append(", ")
          .Append(q.w.ToString("F3"));
    }

    private string GetRenumberedJointName(int index)
    {
        switch (index)
        {
            case 0: return "Thumb";
            case 1: return "Index";
            case 2: return "Mid";
            case 3: return "Ring";
            case 4: return "Pinky";
            default: return "J";
        }
    }

    private void LogHUD(string msg)
    {
        if (_logToHUD && LogManager.Instance != null)
        {
            // Use the specific HUD Source name instead of the HandSide
            LogManager.Instance.Log(_hudLogSource, msg);
        }
    }

    private void AcquireWifiLock()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using var wifiManager = activity.Call<AndroidJavaObject>("getSystemService", "wifi");
            if (wifiManager == null) return;
            // WIFI_MODE_FULL_HIGH_PERF = 3
            _wifiLock = wifiManager.Call<AndroidJavaObject>("createWifiLock", 3, "HTS_WifiLock");
            _wifiLock.Call("acquire");
        }
        catch (Exception)
        {
            // Best-effort: ignore lock errors.
        }
#endif
    }

    private void ReleaseWifiLock()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            if (_wifiLock != null)
            {
                _wifiLock.Call("release");
                _wifiLock = null;
            }
        }
        catch (Exception)
        {
            // Best-effort: ignore lock errors.
        }
#endif
    }

    private void StartUdpReceive()
    {
        if (_udpClient == null) return;
        try
        {
            _udpReceiveResult = _udpClient.BeginReceive(OnUdpReceive, null);
        }
        catch (Exception)
        {
            // Best-effort: ignore receive errors.
        }
    }

    private void OnUdpReceive(IAsyncResult ar)
    {
        if (_udpClient == null) return;
        try
        {
            IPEndPoint any = new IPEndPoint(IPAddress.Any, 0);
            _udpClient.EndReceive(ar, ref any);
        }
        catch (Exception)
        {
            // Ignore receive errors.
        }
        finally
        {
            if (_udpClient != null)
            {
                try
                {
                    _udpReceiveResult = _udpClient.BeginReceive(OnUdpReceive, null);
                }
                catch (Exception)
                {
                    // Best-effort: ignore restart errors.
                }
            }
        }
    }
}
