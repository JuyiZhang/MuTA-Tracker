using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Debugger: MonoBehaviour
{
    private List<string> debugMessageHistory = new List<string>();

    [SerializeField]
    private TMPro.TextMeshProUGUI debugText;

    private string debugMessage;

    private void Start()
    {
        debugText.text = "Debugging started";
        Application.logMessageReceived += AddLogMessage;
    }

    public void AddLogMessage(string message, string stackTrace, LogType type)
    {
        AddDebugMessage(message);
    }

    public void AddDebugMessage(string debugMsg)
    {
        if (debugMessageHistory.Count >= 10)
        {
            debugMessageHistory.Clear();
        }
        debugMessageHistory.Add(debugMsg);
        debugText.text = string.Join(System.Environment.NewLine, debugMessageHistory.ToArray());
    }
}
