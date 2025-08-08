using UnityEngine;
using Oculus.Interaction.Input;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System;
using System.Collections.Generic;

public enum HandSideTCP { Left, Right }

public class HandLandmarkLoggerTCP : MonoBehaviour
{
    [SerializeField]
    private bool _logToHUD = true;

    [SerializeField]
    private float _logFrequency = 0.01f; // Log every 0.01 seconds

    // Specify which hand this logger is for.
    [SerializeField] private HandSideTCP handSide;

    private IHand _hand;
    private float _timer = 0f;
    
    public string remoteIP = "127.0.0.1"; // Localhost via ADB port forwarding
    public int remotePort = 8000; // Port to send data to
    
    [SerializeField]
    private bool useAdbPortForwarding = true; // Using ADB USB connection
    
    private TcpClient tcpClient;
    private NetworkStream networkStream;
    private bool isConnected = false;

    private void Start()
    {
        _hand = GetComponent<IHand>();
        if (_hand == null)
        {
            LogHUD("HandLandmarkLogger requires a component that implements IHand on the same GameObject");
            enabled = false;
            return;
        }

        // Subscribe to hand updates
        _hand.WhenHandUpdated += OnHandUpdated;

        // Initialize TCP connection
        ConnectToServer();
    }

    private void ConnectToServer()
    {
        try
        {
            if (useAdbPortForwarding)
            {
                LogHUD($"Attempting ADB TCP connection to localhost:{remotePort}...");
                LogHUD("Make sure ADB port forwarding is set up!");
                
                tcpClient = new TcpClient();
                
                // Set timeouts for USB/ADB connection
                tcpClient.ReceiveTimeout = 3000;
                tcpClient.SendTimeout = 3000;
                
                tcpClient.Connect("127.0.0.1", remotePort);
                networkStream = tcpClient.GetStream();
                isConnected = true;
                LogHUD($"✓ Connected via ADB to localhost:{remotePort}");
            }
            else
            {
                LogHUD($"Attempting direct TCP connection to {remoteIP}:{remotePort}...");
                LogHUD("Make sure both devices are on same network!");
                
                tcpClient = new TcpClient();
                
                // Set timeouts for WiFi connection
                tcpClient.ReceiveTimeout = 5000;
                tcpClient.SendTimeout = 5000;
                
                tcpClient.Connect(remoteIP, remotePort);
                networkStream = tcpClient.GetStream();
                isConnected = true;
                LogHUD($"✓ Connected directly to {remoteIP}:{remotePort}");
            }
        }
        catch (Exception ex)
        {
            if (useAdbPortForwarding)
            {
                LogHUD($"✗ ADB connection failed: {ex.Message}");
                LogHUD("Check: 1) USB connected 2) ADB forwarding 3) Server running");
            }
            else
            {
                LogHUD($"✗ Direct connection failed: {ex.Message}");
                LogHUD("Check: 1) Network connection 2) Firewall 3) Server running");
            }
            isConnected = false;
        }
    }

    private void OnDestroy()
    {
        if (_hand != null)
        {
            _hand.WhenHandUpdated -= OnHandUpdated;
        }
        
        DisconnectFromServer();
    }

    private void DisconnectFromServer()
    {
        try
        {
            if (networkStream != null)
            {
                networkStream.Close();
                networkStream = null;
            }
            
            if (tcpClient != null)
            {
                tcpClient.Close();
                tcpClient = null;
            }
            
            isConnected = false;
            LogHUD("Disconnected from TCP server");
        }
        catch (Exception ex)
        {
            LogHUD($"Error disconnecting: {ex.Message}");
        }
    }

    private void OnHandUpdated()
    {
        // This will be called whenever the hand data is updated
        if (_logToHUD)
        {
            _timer += Time.deltaTime;
            if (_timer >= _logFrequency)
            {
                _timer = 0f;
                LogHandLandmarks();
                LogWristData(); // Add logging of wrist data
            }
        }
    }

    /// <summary>
    /// Logs the wrist position and orientation to the HUD and sends over UDP
    /// </summary>
    private void LogWristData()
    {
        if (!_hand.IsTrackedDataValid)
        {
            LogHUD("Wrist tracking not valid");
            return;
        }

        // Using the GetRootPose method since GetJointPosesFromRoot is not available
        if (_hand.GetRootPose(out Pose rootPose))
        {
            // Root pose should represent the wrist/palm center in world space
            Vector3 wristPosition = rootPose.position;
            Quaternion wristRotation = rootPose.rotation;
            
            // Convert quaternion to euler angles for HUD display only
            Vector3 wristEulerAngles = wristRotation.eulerAngles;
            
            // Build display message for HUD
            string displayMessage = $"{handSide} Wrist Data:\n";
            displayMessage += $"Position: ({wristPosition.x:F3}, {wristPosition.y:F3}, {wristPosition.z:F3})\n";
            displayMessage += $"Rotation (Euler): ({wristEulerAngles.x:F1}°, {wristEulerAngles.y:F1}°, {wristEulerAngles.z:F1}°)\n";
            displayMessage += $"Rotation (Quat): ({wristRotation.x:F3}, {wristRotation.y:F3}, {wristRotation.z:F3}, {wristRotation.w:F3})\n";
            
            // Log to HUD
            LogHUD(displayMessage);
            
            // Get left hand fist state
            string leftFistState = LeftHandFistDetector.LeftHandState;
            
            // Build CSV message for UDP with quaternion values and left fist state
            string csvMessage = $"{handSide} wrist:";
            csvMessage += $", {wristPosition.x:F3}, {wristPosition.y:F3}, {wristPosition.z:F3}";
            csvMessage += $", {wristRotation.x:F3}, {wristRotation.y:F3}, {wristRotation.z:F3}, {wristRotation.w:F3}";
            csvMessage += $", leftFist: {leftFistState.ToLower()}";
            
            // Send over TCP
            SendTcpMessage(csvMessage);
        }
        else
        {
            LogHUD("Failed to get wrist pose");
        }
    }

    /// <summary>
    /// Logs 21 essential hand landmarks (excluding metacarpals) with their position data to the HUD and sends over UDP
    /// </summary>
    private void LogHandLandmarks()
    {
        if (!_hand.IsTrackedDataValid)
        {
            LogHUD("Hand tracking not valid");
            return;
        }

        // Log basic hand info first
        LogHUD($"{handSide} Hand - IsTracked: {_hand.IsTrackedDataValid}, IsConnected: {_hand.IsConnected}");

        // Get local poses from wrist
        if (_hand.GetJointPosesFromWrist(out ReadOnlyHandJointPoses localJointPoses))
        {
            LogHUD($"Local joints available: {localJointPoses.Count}");
            
            // Build display message for HUD showing ALL landmark positions for debugging
            string displayMessage = $"{handSide} Hand Landmarks (Local Coordinates):\n";
            
            // Build CSV message for UDP with selected landmark positions
            string csvMessage = $"{handSide} landmarks:";
            
            // Define the 6 joint indices for display (wrist and finger tips only)
            int[] displayJoints = {
                1,  // Wrist
                5,  // ThumbTip
                10, // IndexTip
                15, // MiddleTip
                20, // RingTip
                24  // LittleTip
            };
            
            // Define all 21 joint indices for UDP streaming (renumbered 0-20)
            int[] streamedJoints = {
                1,  // 0: Wrist
                2,  // 1: ThumbMetacarpal
                3,  // 2: ThumbProximal
                4,  // 3: ThumbDistal
                5,  // 4: ThumbTip
                7,  // 5: IndexProximal
                8,  // 6: IndexIntermediate
                9,  // 7: IndexDistal
                10, // 8: IndexTip
                12, // 9: MiddleProximal
                13, // 10: MiddleIntermediate
                14, // 11: MiddleDistal
                15, // 12: MiddleTip
                17, // 13: RingProximal
                18, // 14: RingIntermediate
                19, // 15: RingDistal
                20, // 16: RingTip
                21, // 17: LittleProximal
                22, // 18: LittleIntermediate
                23, // 19: LittleDistal
                24  // 20: LittleTip
            };
            
            // Log ALL available local joints first for debugging
            for (int j = 0; j < Math.Min(localJointPoses.Count, 30); j++)
            {
                Vector3 localPos = localJointPoses[j].position;
                LogHUD($"LocalJoint[{j}]: Pos({localPos.x:F4},{localPos.y:F4},{localPos.z:F4})");
            }
            
            LogHUD($"=== Selected {displayJoints.Length} Key Landmarks (Wrist, Fingertips) ===");
            
            // Process display joints for logging
            for (int i = 0; i < displayJoints.Length; i++)
            {
                int originalJointIndex = displayJoints[i];
                int renumberedIndex = i; // Renumber: 0=Wrist, 1=ThumbTip, 2=IndexTip, 3=MiddleTip, 4=RingTip, 5=PinkyTip
                
                if (originalJointIndex < localJointPoses.Count)
                {
                    Vector3 localPosition = localJointPoses[originalJointIndex].position;
                    
                    string jointName = GetRenumberedJointName(renumberedIndex);
                    displayMessage += $"{jointName}[{renumberedIndex}]: Pos({localPosition.x:F4},{localPosition.y:F4},{localPosition.z:F4})\n";
                }
                else
                {
                    displayMessage += $"Joint {renumberedIndex}: (ORIGINAL INDEX {originalJointIndex} OUT OF RANGE - Max: {localJointPoses.Count-1})\n";
                }
            }
            
            // Process all 21 joints for UDP streaming
            for (int i = 0; i < streamedJoints.Length; i++)
            {
                int originalJointIndex = streamedJoints[i];
                
                if (originalJointIndex < localJointPoses.Count)
                {
                    Vector3 localPosition = localJointPoses[originalJointIndex].position;
                    csvMessage += $", {localPosition.x:F4}, {localPosition.y:F4}, {localPosition.z:F4}";
                }
                else
                {
                    // Handle case where joint is not available
                    csvMessage += ", 0.0000, 0.0000, 0.0000";
                }
            }
            
            // Log to HUD and send over TCP
            LogHUD(displayMessage);
            SendTcpMessage(csvMessage);
        }
        else
        {
            LogHUD("Failed to get local joint poses");
        }
    }

    /// <summary>
    /// Returns a human-readable name for the renumbered index (0-5)
    /// </summary>
    private string GetRenumberedJointName(int renumberedIndex)
    {
        switch (renumberedIndex)
        {
            case 0: return "Wrist";
            case 1: return "ThumbTip";
            case 2: return "IndexTip";
            case 3: return "MiddleTip";
            case 4: return "RingTip";
            case 5: return "PinkyTip";
            default: return $"Landmark{renumberedIndex}";
        }
    }

    /// <summary>
    /// Sends the provided message over TCP.
    /// </summary>
    void SendTcpMessage(string message)
    { 
        try
        {
            if (!isConnected || networkStream == null)
            {
                // Try to reconnect
                ConnectToServer();
                if (!isConnected)
                {
                    LogHUD("TCP not connected, message not sent");
                    return;
                }
            }

            // Add newline delimiter for easier parsing on receiver side
            string messageWithDelimiter = message + "\n";
            byte[] data = Encoding.UTF8.GetBytes(messageWithDelimiter);
            networkStream.Write(data, 0, data.Length);
            networkStream.Flush();
        }
        catch (Exception ex)
        {
            LogHUD($"TCP Send Error: {ex.Message}");
            isConnected = false;
            
            // Try to reconnect on next send
            DisconnectFromServer();
        }
    }



    // Helper method to log messages to the HUD on the channel for the current hand side.
    private void LogHUD(string message)
    {
        LogManager.Instance.Log(handSide.ToString(), message);
    }
}