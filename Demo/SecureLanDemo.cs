using UnityEngine;
using UnityEngine.UI;
using SecureLanConnection;
using System.Text;

public class SecureLanDemo : MonoBehaviour
{
    public Text connectionStatusText;
    public Text logText;
    public ScrollRect scrollRect;

    private StringBuilder _logBuilder = new StringBuilder();

    private void Start()
    {
        if (logText != null)
        {
            if (logText.font == null) 
            {
                logText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
            logText.color = Color.black;
            logText.alignment = TextAnchor.UpperLeft;
        }

        LogMessage("SecureLanDemo initialized. Waiting for connections...");

        if (SecureLanPeer.Instance != null)
        {
            SecureLanPeer.Instance.OnPeerConnected += HandlePeerConnected;
            SecureLanPeer.Instance.OnPeerDisconnected += HandlePeerDisconnected;
            SecureLanPeer.Instance.OnJsonReceived += HandleJsonReceived;
            SecureLanPeer.Instance.OnAllExpectedPeersConnected += HandleAllPeersConnected;
            
            UpdateConnectionStatus();
        }
        else
        {
            LogMessage("SecureLanPeer not found.");
        }
    }

    private void OnDestroy()
    {
        if (SecureLanPeer.Instance != null)
        {
            SecureLanPeer.Instance.OnPeerConnected -= HandlePeerConnected;
            SecureLanPeer.Instance.OnPeerDisconnected -= HandlePeerDisconnected;
            SecureLanPeer.Instance.OnJsonReceived -= HandleJsonReceived;
            SecureLanPeer.Instance.OnAllExpectedPeersConnected -= HandleAllPeersConnected;
        }
    }

    private void HandlePeerConnected(string peerKey)
    {
        UpdateConnectionStatus();
        LogMessage($"Peer Connected: {peerKey}");
    }

    private void HandlePeerDisconnected(string peerKey)
    {
        UpdateConnectionStatus();
        LogMessage($"Peer Disconnected: {peerKey}");
    }

    private void HandleJsonReceived(string senderIp, string json)
    {
        LogMessage($"Received from {senderIp}: {json}");
    }

    private void HandleAllPeersConnected()
    {
        UpdateConnectionStatus();
        LogMessage("All expected peers connected.");
    }

    private void UpdateConnectionStatus()
    {
        if (connectionStatusText != null && SecureLanPeer.Instance != null)
        {
            if (SecureLanPeer.Instance.IsExpectedPeerCountReached)
            {
                connectionStatusText.text = "CONNECTED";
            }
            else
            {
                connectionStatusText.text = $"WAITING... ({SecureLanPeer.Instance.GetPeerCount()} connected)";
            }
        }
    }

    private void LogMessage(string message)
    {
        _logBuilder.AppendLine($"[{System.DateTime.Now:HH:mm:ss}] {message}");
        if (logText != null)
        {
            logText.text = _logBuilder.ToString();
        }
    }
}
