# Secure LAN Connection

A plug-and-play peer-to-peer LAN communicator for Unity using multicast UDP discovery and AES-256-CBC encrypted TCP messaging.

## Features
- **Plug and Play:** Automatic TCP port binding means zero port conflicts.
- **Easy Setup:** Editor setup window pops up automatically to configure the Room Secret and Peer count.
- **Simultaneous Start:** Built-in events to wait for a specific number of peers before starting.
- **Robust:** Auto-reconnection and connection state tracking.

## Installation

### Install via Unity Package Manager (UPM) - Recommended

You can install this package directly via the Unity Package Manager using the Git URL.

1. Open your Unity project.
2. Go to **Window > Package Manager**.
3. Click the **+** button in the top-left corner.
4. Select **Add package from git URL...**
5. Enter the following URL and click **Add**:

```text
https://github.com/dimzki/Secure-LAN-Connection.git
```

*Note: You can also append a specific version tag or branch to the URL (e.g., `https://github.com/dimzki/Secure-LAN-Connection.git#v1.0.0`).*

### Install via manifest.json

Alternatively, you can open your project's `Packages/manifest.json` file and add the following line to your `"dependencies"` block:

```json
"com.dimzki.securelanconnection": "https://github.com/dimzki/Secure-LAN-Connection.git"
```

## Quick Start
1. After installation, an Editor window will pop up. Configure your **Shared Secret**.
2. Add the `SecureLanPeer` component to a GameObject in your scene.
3. Subscribe to the events:
   - `OnPeerConnected`
   - `OnPeerDisconnected`
   - `OnAllExpectedPeersConnected`
   - `OnJsonReceived`
4. Use `SecureLanPeer.Instance.BroadcastJson(jsonString)` to send data to all peers in the room!
