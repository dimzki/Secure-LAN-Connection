using UnityEngine;

namespace SecureLanConnection
{
    [CreateAssetMenu(fileName = "SecureLanSettings", menuName = "Secure LAN Connection/Settings")]
    public class SecureLanSettings : ScriptableObject
    {
        [Header("Room Settings")]
        [Tooltip("The 'Room Password'. Only PCs with this exact secret can connect and communicate with each other.")]
        public string sharedSecret = "CHANGE_THIS_SECRET";

        [Tooltip("The total number of peers (PCs) expected in the room. Set to 0 for a normal, open-ended start where peers can join freely. If greater than 0, the OnAllExpectedPeersConnected event will only fire when this exact number of peers is connected.")]
        public int expectedPeerCount = 0;

        [Header("Advanced Network Settings")]
        [Tooltip("The UDP port used to broadcast presence and find other peers on the LAN. This must be the same for all peers in the room.")]
        public int multicastPort = 7777;

        [Tooltip("The UDP multicast IP address. Keep this as 239.255.255.250 unless you have specific network routing needs.")]
        public string multicastAddress = "239.255.255.250";
    }
}
