using UnityEngine;
using TMPro; 
using UnityEngine.UI;
using System;
using System.Net.Sockets;

public class AppManager : MonoBehaviour
{
    public static AppManager Instance { get; private set; } 

    [Header("UI References")]
    public TMP_Dropdown protocolDropdown; 
    public TMP_InputField ipInputField;
    public TMP_InputField portInputField;
    public TMP_Dropdown handDropdown;     
    public GameObject menuPanel;          
    
    [Header("Network Status References")]
    public TextMeshProUGUI networkStatusText; 
    public Button btnStart;                

    [Header("Build Info")]
    public TextMeshProUGUI versionText;   

    [Header("Visual Settings")]
    public Toggle visualizationToggle; 
    public bool ShowLandmarks => visualizationToggle != null && visualizationToggle.isOn;

    [Header("Interaction Settings")]
    public GameObject[] rayInteractors;

    [Header("Hand Visuals")]
    public GameObject syntheticHandLeft;
    public GameObject syntheticHandRight;

    [Header("Logging Settings")]
    public string targetLogSource = "Left"; 

    [Header("Status")]
    public bool isStreaming = false;
    private string _connectionErrorMessage = ""; 
    private Color _statusColor = Color.green; // Persistent color cache

    public string ServerIP { get; private set; }
    public int ServerPort { get; private set; }
    public int SelectedProtocol { get; private set; } 
    public int SelectedHandMode { get; private set; } 

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(this);
        else Instance = this;
    }

    private void Start()
    {
        // Automatically pulls from Project Settings > Player > Version
        string version = Application.version; 
        
        if (versionText != null) 
        {
            versionText.text = $"v{version}";
        }

        if (protocolDropdown != null)
        {
            protocolDropdown.onValueChanged.AddListener(OnProtocolChanged);
            OnProtocolChanged(protocolDropdown.value);
        }

        ipInputField.onValueChanged.AddListener(delegate { ClearError(); });
        portInputField.onValueChanged.AddListener(delegate { ClearError(); });
    }

    private void Update()
    {
        // If streaming, check if the user clicks the Left Menu Button
        // This is the flat button with three lines on the Left Quest Controller
        if (isStreaming && OVRInput.GetDown(OVRInput.Button.Start, OVRInput.Controller.LTouch))
        {
            StopStreaming(); 
        }

        if (!isStreaming)
        {
            ValidateNetwork();
        }
    }
    
    private void OnDestroy()
    {
        if (protocolDropdown != null)
        {
            protocolDropdown.onValueChanged.RemoveListener(OnProtocolChanged);
        }
    }

    private void OnProtocolChanged(int index)
    {
        ClearError();
        if (index == 0) // UDP
        {
            if (ipInputField != null) ipInputField.text = "255.255.255.255";
            if (portInputField != null) portInputField.text = "9000";
            UpdateStatusUI("UDP Ready", Color.green, true);
        }
        else if (index == 1) // TCP
        {
            if (ipInputField != null) ipInputField.text = "127.0.0.1";
            if (portInputField != null) portInputField.text = "8000";
            StartCoroutine(QuickTCPCheck());
        }
    }

    public void ClearError()
    {
        _connectionErrorMessage = "";
        _statusColor = Color.green;
    }

    private void ValidateNetwork()
    {
        if (Application.internetReachability == NetworkReachability.NotReachable)
        {
            UpdateStatusUI("Error: No Active Network Connection", Color.red, false);
            return;
        }

        if (!string.IsNullOrEmpty(_connectionErrorMessage))
        {
            // Uses the persistent color (Red or Yellow) set by the connection logic
            UpdateStatusUI(_connectionErrorMessage, _statusColor, true);
            return;
        }

        UpdateStatusUI("System Ready", Color.green, true);
    }

    private void UpdateStatusUI(string message, Color color, bool canStart)
    {
        _statusColor = color; // Cache the color for the Update loop
        if (networkStatusText != null)
        {
            networkStatusText.text = message;
            networkStatusText.color = color;
        }

        if (btnStart != null)
        {
            btnStart.interactable = canStart;
            var breather = btnStart.GetComponent<UIButtonBreather>();
            if (breather != null) breather.enabled = canStart;
        }
    }

    public void OnStartStreaming()
    {
        ClearError();
        ServerIP = ipInputField.text;
        
        int parsedPort;
        if (!int.TryParse(portInputField.text, out parsedPort))
        {
            UpdateStatusUI("Error: Port number is invalid!", Color.red, false);
            return;
        }
        ServerPort = parsedPort;

        SelectedProtocol = protocolDropdown.value;
        SelectedHandMode = handDropdown.value;

        if (SelectedProtocol == 1) // TCP
        {
            try 
            {
                using (TcpClient testClient = new TcpClient())
                {
                    IAsyncResult result = testClient.BeginConnect(ServerIP, ServerPort, null, null);
                    bool success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(1));
                    
                    if (!success)
                        throw new Exception("Timed Out");

                    testClient.EndConnect(result);
                }
            }
            catch (Exception)
            {
                _connectionErrorMessage = "Error: TCP Refused. Check ADB Reverse / Server.";
                // Setting Color.red here is now persistent
                UpdateStatusUI(_connectionErrorMessage, Color.red, true);
                return; 
            }
        }

        UpdateStatusUI("Streaming Active", Color.green, true);
        
        // 1. Show correct Hand and Protocol names in the HUD log
        string protocolName = protocolDropdown.options[SelectedProtocol].text;
        string handName = handDropdown.options[SelectedHandMode].text;
        string statusMsg = $"Stream started! \nIP: {ServerIP} \nPort: {ServerPort} \nProtocol: {protocolName} \nHands: {handName}";
        SendLog(statusMsg);
        
        if(menuPanel != null) menuPanel.SetActive(false);

        ToggleRays(false);
        UpdateHandVisuals(SelectedHandMode);
        isStreaming = true;
    }

    public void StopStreaming()
    {
        isStreaming = false;
        ClearError();

        // Re-enable UI and Rays for interaction
        if (menuPanel != null) 
        {
            menuPanel.SetActive(true);
            MenuRecenter recenterScript = FindFirstObjectByType<MenuRecenter>();
            if (recenterScript != null) recenterScript.Recenter();
        }
        ToggleRays(true);

        SendLog("Streaming stopped by user.");
    }

    private void UpdateHandVisuals(int mode)
    {
        bool showLeft = (mode == 0 || mode == 1);
        bool showRight = (mode == 0 || mode == 2);

        if (syntheticHandLeft != null) syntheticHandLeft.SetActive(showLeft);
        if (syntheticHandRight != null) syntheticHandRight.SetActive(showRight);
    }

    private void ToggleRays(bool state)
    {
        if (rayInteractors == null) return;
        foreach (var ray in rayInteractors)
        {
            if (ray != null) ray.SetActive(state);
        }
    }

    private void SendLog(string message)
    {
        if (LogManager.Instance != null)
            LogManager.Instance.Log(targetLogSource, message);
        else
            Debug.Log($"[{targetLogSource}] {message}");
    }

    private System.Collections.IEnumerator QuickTCPCheck()
    {
        UpdateStatusUI("Checking ADB Tunnel...", Color.yellow, false);
        
        string targetIP = ipInputField.text;
        int targetPort;
        if (!int.TryParse(portInputField.text, out targetPort)) yield break;

        bool success = false;
        
        var task = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    var result = client.BeginConnect(targetIP, targetPort, null, null);
                    if (result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(1)))
                    {
                        client.EndConnect(result);
                        return true;
                    }
                }
            }
            catch { }
            return false;
        });

        while (!task.IsCompleted) yield return null;
        success = task.Result;

        if (!success)
        {
            _connectionErrorMessage = "Warning: TCP Refused. Is 'adb reverse' running?";
            // Persistent Yellow for the passive background check
            UpdateStatusUI(_connectionErrorMessage, Color.yellow, true);
        }
    }
}