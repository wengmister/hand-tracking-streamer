using UnityEngine;
using Oculus.Interaction.Input;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System;
using System.Collections.Generic;

public enum HandSide { Left, Right }

public class HandLandmarkLogger : MonoBehaviour
{
    [SerializeField]
    private bool _logToHUD = true;

    [SerializeField]
    private float _logFrequency = 0.01f; // Log every 0.01 seconds

    // Specify which hand this logger is for.
    [SerializeField] private HandSide handSide;

    private IHand _hand;
    private float _timer = 0f;
    
    public string remoteIP = "255.255.255.255"; // Broadcast address
    public int remotePort = 9000; // Port to send data to
    private UdpClient udpClient;
    private IPEndPoint remoteEndPoint;

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

        udpClient = new UdpClient();
        remoteEndPoint = new IPEndPoint(IPAddress.Parse(remoteIP), remotePort);
    }

    private void OnDestroy()
    {
        if (_hand != null)
        {
            _hand.WhenHandUpdated -= OnHandUpdated;
        }
        
        if (udpClient != null)
        {
            udpClient.Close();
            udpClient = null;
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
            
            // Send over UDP
            SendUdpMessage(csvMessage);
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
            
            // Log to HUD and send over UDP
            LogHUD(displayMessage);
            SendUdpMessage(csvMessage);
        }
        else
        {
            LogHUD("Failed to get local joint poses");
        }
    }

    /// <summary>
    /// Returns a human-readable name for the original joint index
    /// </summary>
    private string StreamedJointName(int jointIndex)
    {
        switch (jointIndex)
        {
            case 1: return "Wrist";
            case 2: return "ThumbMetacarpal";
            case 3: return "ThumbProximal";
            case 4: return "ThumbDistal";
            case 5: return "ThumbTip";
            case 7: return "IndexProximal";
            case 8: return "IndexIntermediate";
            case 9: return "IndexDistal";
            case 10: return "IndexTip";
            case 12: return "MiddleProximal";
            case 13: return "MiddleIntermediate";
            case 14: return "MiddleDistal";
            case 15: return "MiddleTip";
            case 17: return "RingProximal";
            case 18: return "RingIntermediate";
            case 19: return "RingDistal";
            case 20: return "RingTip";
            case 21: return "LittleProximal";
            case 22: return "LittleIntermediate";
            case 23: return "LittleDistal";
            case 24: return "PinkyTip";
            default: return $"Joint{jointIndex}";
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
    /// Sends the provided message over UDP.
    /// </summary>
    void SendUdpMessage(string message)
    { 
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            udpClient.Send(data, data.Length, remoteEndPoint);
        }
        catch (Exception ex)
        {
            LogHUD("UDP Send Error: " + ex.Message);
        }
    }



    // Helper method to log messages to the HUD on the channel for the current hand side.
    private void LogHUD(string message)
    {
        LogManager.Instance.Log(handSide.ToString(), message);
    }
}