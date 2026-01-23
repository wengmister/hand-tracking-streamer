using UnityEngine;
using TMPro; 
using UnityEngine.UI;

public class AppManager : MonoBehaviour
{
    public static AppManager Instance { get; private set; } 

    [Header("UI References")]
    public TMP_Dropdown protocolDropdown; // 0 = UDP, 1 = TCP
    public TMP_InputField ipInputField;
    public TMP_InputField portInputField;
    public TMP_Dropdown handDropdown;     // 0 = Both, 1 = Left, 2 = Right
    public GameObject menuPanel;          // To hide the menu after starting

    [Header("Interaction Settings")]
    [Tooltip("Drag your Left and Right Ray Building Blocks here.")]
    public GameObject[] rayInteractors;

    [Header("Visual Settings")]
    [Tooltip("Drag '[BuildingBlock] Synthetic Left Hand' here")]
    public GameObject syntheticHandLeft;
    [Tooltip("Drag '[BuildingBlock] Synthetic Right Hand' here")]
    public GameObject syntheticHandRight;

    [Header("Logging Settings")]
    [Tooltip("Which Log Source should we push the 'Starting' message to?")]
    public string targetLogSource = "Left"; 

    [Header("Status")]
    public bool isStreaming = false;

    // --- Public Properties ---
    public string ServerIP { get; private set; }
    public int ServerPort { get; private set; }
    public int SelectedProtocol { get; private set; } // 0 = UDP, 1 = TCP
    public int SelectedHandMode { get; private set; } // 0 = Both, 1 = Left, 2 = Right
    // -------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(this);
        else Instance = this;
    }

    private void Start()
    {
        // NEW: Listen for dropdown changes to auto-fill IP/Port
        if (protocolDropdown != null)
        {
            protocolDropdown.onValueChanged.AddListener(OnProtocolChanged);
            
            // Trigger it once at startup so the fields aren't empty
            OnProtocolChanged(protocolDropdown.value);
        }
    }

    private void OnDestroy()
    {
        // Clean up listener to prevent memory leaks
        if (protocolDropdown != null)
        {
            protocolDropdown.onValueChanged.RemoveListener(OnProtocolChanged);
        }
    }

    // --- NEW: Auto-fill Logic ---
    private void OnProtocolChanged(int index)
    {
        // Index 0 = UDP, Index 1 = TCP
        if (index == 0) 
        {
            // UDP Defaults
            if (ipInputField != null) ipInputField.text = "255.255.255.255";
            if (portInputField != null) portInputField.text = "9000";
        }
        else if (index == 1)
        {
            // TCP Defaults
            if (ipInputField != null) ipInputField.text = "127.0.0.1";
            if (portInputField != null) portInputField.text = "8000";
        }
    }
    // ----------------------------

    public void OnStartStreaming()
    {
        // 1. Read the IP Address
        ServerIP = ipInputField.text;
        if (string.IsNullOrEmpty(ServerIP))
        {
            SendLog("Error: IP Address is empty!");
            return; 
        }

        // 2. Read the Port Number
        int portNumber;
        if (!int.TryParse(portInputField.text, out portNumber))
        {
            SendLog("Error: Port number is invalid!");
            return;
        }
        ServerPort = portNumber;

        // 3. Read Configs
        SelectedProtocol = protocolDropdown.value;
        string protocolName = protocolDropdown.options[SelectedProtocol].text;

        int handSelection = handDropdown.value;
        string handName = handDropdown.options[handSelection].text;
        SelectedHandMode = handSelection;

        // 4. Push Success Message
        string statusMsg = $"Starting Stream! \nIP: {ServerIP} \nPort: {ServerPort} \nProtocol: {protocolName} \nHands: {handName}";
        SendLog(statusMsg);

        // 5. Hide the Menu
        if(menuPanel != null)
        {
            menuPanel.SetActive(false);
        }

        // 6. DISABLE RAYS 
        ToggleRays(false);

        // 7. ENABLE SYNTHETIC HANDS
        UpdateHandVisuals(SelectedHandMode);

        // 8. Flip the switch
        isStreaming = true;
    }

    public void StopStreaming()
    {
        isStreaming = false;
        
        // Show menu again
        if(menuPanel != null) menuPanel.SetActive(true);
        
        // Re-enable Rays
        ToggleRays(true);
        
        SendLog("Streaming Stopped.");
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
        {
            LogManager.Instance.Log(targetLogSource, message);
        }
        else
        {
            Debug.Log($"[{targetLogSource}] {message}");
        }
    }
}