using UnityEngine;
using Oculus.Interaction.Input;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System;
using System.Diagnostics;

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

    // Public Accessors
    public IHand Hand => _hand; 
    public HandSide Side => _handSide;
    
    // Networking
    private UdpClient _udpClient;
    private TcpClient _tcpClient;
    private NetworkStream _tcpStream;
    private IPEndPoint _remoteEndPoint;
    private bool _isInitialized = false;
    private int _currentProtocol = -1; 

    // Optimization: Cache StringBuilders
    private StringBuilder _sbPacket = new StringBuilder(2048);
    private StringBuilder _sbLog = new StringBuilder(2048);

    // Debug timing metadata (per-hand stream)
    private uint _frameId = 0;
    private static readonly double _ticksToNs = 1_000_000_000.0 / Stopwatch.Frequency;
    
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

        // 4. Rate Limiting
        _timer += Time.deltaTime;
        if (_timer >= _frequency)
        {
            _timer = 0f;
            ProcessHandData();
        }
    }

    private void ProcessHandData()
    {
        if (!_hand.IsTrackedDataValid) return;

        _sbPacket.Clear();
        _sbLog.Clear();

        bool addDebugHeaderMeta = AppManager.Instance != null && AppManager.Instance.ShowDebugInfo;

        uint frameId = 0;
        ulong sendTimestampNs = 0;

        if (addDebugHeaderMeta)
        {
            _frameId++;
            frameId = _frameId;
            // Single timestamp reused for both headers in this packet for deterministic pairing
            sendTimestampNs = GetMonotonicTimestampNs();
        }

        // --- 1. PROCESS WRIST ---
        if (_hand.GetRootPose(out Pose rootPose))
        {
            // Prepare Network Packet
            if (addDebugHeaderMeta)
            {
                AppendHeaderWithMeta(_sbPacket, "wrist", frameId, sendTimestampNs);
                _sbPacket.Append(", ");
            }
            else
            {
                _sbPacket.Append(_handSide).Append(" wrist:, ");
            }

            AppendVector3(_sbPacket, rootPose.position);
            _sbPacket.Append(", ");
            AppendQuaternion(_sbPacket, rootPose.rotation);

            // Prepare HUD Log
            if (_logToHUD)
            {
                _sbLog.AppendLine($"=== [{_handSide}] Wrist ==="); 
                _sbLog.AppendLine($"Pos: {rootPose.position.ToString("F3")}");
                // _sbLog.AppendLine($"Rot: {rootPose.rotation.eulerAngles.ToString("F0")}");
            }
        }

        // --- 2. PROCESS LANDMARKS ---
        if (_hand.GetJointPosesFromWrist(out ReadOnlyHandJointPoses joints))
        {
            // Network Packet
            _sbPacket.Append("\n");
            if (addDebugHeaderMeta)
            {
                AppendHeaderWithMeta(_sbPacket, "landmarks", frameId, sendTimestampNs);
            }
            else
            {
                _sbPacket.Append(_handSide).Append(" landmarks:");
            }
            
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
                _udpClient.Client.SendBufferSize = 0;
                _remoteEndPoint = new IPEndPoint(IPAddress.Parse(ip), port);
                
                _udpClient.BeginReceive(new AsyncCallback(OnUdpReceive), null);
                LogHUD($"UDP Ready: {ip}:{port}");
            }
            else // TCP (Wired=1 OR Wireless=2)
            {
                _tcpClient = new TcpClient(AddressFamily.InterNetwork);
                _tcpClient.NoDelay = true; 
                _tcpClient.SendTimeout = 1000;
                _tcpClient.ReceiveTimeout = 1000;

                _tcpClient.Connect(ip, port);
                _tcpStream = _tcpClient.GetStream();
                
                string type = _currentProtocol == 1 ? "Wired" : "WiFi";
                LogHUD($"TCP({type}) Connected: {ip}:{port}");
            }
            _isInitialized = true;
        }
        catch (Exception ex)
        {
            LogHUD($"Conn Error: {ex.Message}");
            AppManager.Instance.StopStreaming();
        }
    }

    private void OnUdpReceive(IAsyncResult res)
    {
        try
        {
            IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            _udpClient.EndReceive(res, ref remote);
            _udpClient.BeginReceive(new AsyncCallback(OnUdpReceive), null);
        }
        catch { }
    }
    
    private void SendData(string message)
    {
        if (AppManager.Instance != null && !AppManager.Instance.isStreaming) return;

        try
        {
            if (_currentProtocol == 0 && _udpClient != null) // UDP
            {
                byte[] data = Encoding.UTF8.GetBytes(message);
                _udpClient.Send(data, data.Length, _remoteEndPoint);
            }
            else if ((_currentProtocol == 1 || _currentProtocol == 2) && _tcpStream != null && _tcpStream.CanWrite)
            {
                byte[] data = Encoding.UTF8.GetBytes(message + "\n");
                _tcpStream.Write(data, 0, data.Length);
            }
        }
        catch (Exception ex)
        {
            Disconnect();
            if (AppManager.Instance != null)
            {
                AppManager.Instance.HandleDisconnection("Host Closed Connection:" + ex.Message);
            }
        }
    }

    private void Disconnect()
    {
        try
        {
            if (_udpClient != null) { _udpClient.Close(); _udpClient = null; }
            if (_tcpStream != null) { _tcpStream.Close(); _tcpStream = null; }
            if (_tcpClient != null) { _tcpClient.Close(); _tcpClient = null; }
        }
        catch { }
        _isInitialized = false;
    }

    // --- UTILITY HELPERS ---
    private static ulong GetMonotonicTimestampNs()
    {
        return (ulong)(Stopwatch.GetTimestamp() * _ticksToNs);
    }

    private void AppendHeaderWithMeta(StringBuilder sb, string section, uint frameId, ulong sendTimestampNs)
    {
        // Matches normal message capitalization style ("Left wrist", "Right landmarks")
        // Example:
        // Left wrist | f = 123 | t = 123456789012345:
        sb.Append(_handSide)
          .Append(" ")
          .Append(section)
          .Append(" | f = ")
          .Append(frameId)
          .Append(" | t = ")
          .Append(sendTimestampNs)
          .Append(":");
    }

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
            LogManager.Instance.Log(_hudLogSource, msg);
        }
    }
}