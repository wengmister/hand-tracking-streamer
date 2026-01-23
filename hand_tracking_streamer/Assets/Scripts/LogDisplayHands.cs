using UnityEngine;
using UnityEngine.UI;
using System.Text;
using System.Collections.Generic;

public class LogDisplayHands : MonoBehaviour
{
    [SerializeField] private Text logText;

    [SerializeField] private string logSource; 

    // We don't need to serialize this anymore since we overwrite it in code,
    // but keeping it as a private variable to store the current limit.
    private int _currentMaxMessages = 1;

    // Optimization: Cache StringBuilder to avoid memory garbage
    private StringBuilder _sb = new StringBuilder(1000);

    private void Update()
    {
        UpdateDisplayLimit();
        DisplayLog();
    }

    private void UpdateDisplayLimit()
    {
        // Safety check
        if (AppManager.Instance == null) return;

        // Mode 0 = Both Hands
        // Mode 1 = Left Hand
        // Mode 2 = Right Hand
        int mode = AppManager.Instance.SelectedHandMode;

        if (mode == 0) 
        {
            // Both hands selected -> We need space for 2 messages (one per hand)
            _currentMaxMessages = 2;
        }
        else 
        {
            // Single hand selected -> We only need the latest single message
            _currentMaxMessages = 1;
        }
    }

    private void DisplayLog()
    {
        if (LogManager.Instance == null) return;

        // 1. Get the raw list of messages for this source
        var messages = LogManager.Instance.GetLogMessages(logSource);
        
        if (messages == null || messages.Count == 0)
        {
            logText.text = ""; 
            return;
        }

        // 2. Clear the builder
        _sb.Clear();

        // 3. Calculate start index (Show only the last N messages based on dynamic limit)
        int startIdx = Mathf.Max(0, messages.Count - _currentMaxMessages);

        // 4. Build the string
        for (int i = startIdx; i < messages.Count; i++)
        {
            _sb.AppendLine(messages[i]);
        }

        // 5. Update UI
        logText.text = _sb.ToString();
    }
}