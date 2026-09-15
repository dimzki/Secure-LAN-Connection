using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SecureLanConnection
{
    /// <summary>
    /// Peer-to-peer LAN communicator using multicast UDP discovery and AES-256-CBC + HMAC-SHA256 encrypted TCP messaging.
    /// </summary>
    public sealed class SecureLanPeer : MonoBehaviour
    {
        public static SecureLanPeer Instance { get; private set; }

        public event Action<string, string> OnJsonReceived;
        public event Action<string> OnPeerConnected;
        public event Action<string> OnPeerDisconnected;
        public event Action OnAllExpectedPeersConnected;

        public bool IsExpectedPeerCountReached 
        {
            get 
            {
                if (_settings == null) return false;
                if (_settings.expectedPeerCount <= 1) return true;
                return _peers.Count >= (_settings.expectedPeerCount - 1);
            }
        }

        private SecureLanSettings _settings;
        private UdpClient _udpClient;
        private TcpListener _tcpListener;

        private readonly ConcurrentDictionary<string, TcpClient> _peers = new();
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new();

        private CancellationTokenSource _cts;

        private string _localIP;
        private string _instanceId; 
        private int _localTcpPort; 

        private byte[] _encKey;
        private byte[] _macKey; 
        private bool _hasFiredExpectedPeersEvent;

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            _instanceId = Guid.NewGuid().ToString("N");
            
            _settings = Resources.Load<SecureLanSettings>("SecureLanSettings");
            if (_settings == null)
            {
                Debug.LogError("[SecureLanPeer] Could not find SecureLanSettings in Resources!");
            }
        }

        private void DeriveKeys()
        {
            if (_settings == null) return;
            using var sha = SHA256.Create();
            _encKey = sha.ComputeHash(Encoding.UTF8.GetBytes("ENC:" + _settings.sharedSecret));
            _macKey = sha.ComputeHash(Encoding.UTF8.GetBytes("MAC:" + _settings.sharedSecret));
        }

        private async void Start()
        {
            if (_settings == null) return;

            DeriveKeys();

            _cts = new CancellationTokenSource();
            _localIP = GetLocalIP();

            StartTcpServer();
            StartUdpDiscovery();

            await Task.Delay(500);
            _ = BroadcastWithRetryAsync(retries: 3, intervalMs: 2000);
            
            CheckExpectedPeerCount();
        }
        
        private void CheckExpectedPeerCount()
        {
            if (!_hasFiredExpectedPeersEvent && IsExpectedPeerCountReached)
            {
                _hasFiredExpectedPeersEvent = true;
                _mainThreadQueue.Enqueue(() => OnAllExpectedPeersConnected?.Invoke());
            }
        }

        private async Task BroadcastWithRetryAsync(int retries, int intervalMs)
        {
            for (int i = 0; i <= retries; i++)
            {
                if (_cts.IsCancellationRequested) break;
                BroadcastPresence();
                if (i < retries)
                {
                    try { await Task.Delay(intervalMs, _cts.Token); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }

        private void Update()
        {
            while (_mainThreadQueue.TryDequeue(out var action))
                action?.Invoke();
        }

        private void OnDestroy()
        {
            _cts?.Cancel();
            _udpClient?.Close();
            _tcpListener?.Stop();
            foreach (var peer in _peers.Values)
                peer?.Close();
            _peers.Clear();
        }

        private void StartUdpDiscovery()
        {
            try
            {
                _udpClient = new UdpClient();
                _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, _settings.multicastPort));
                _udpClient.MulticastLoopback = true;
                _udpClient.JoinMulticastGroup(IPAddress.Parse(_settings.multicastAddress), IPAddress.Parse(_localIP));
                _ = ListenUdp();
            }
            catch (Exception e)
            {
                Debug.LogError("[SecureLanPeer] UDP setup failed: " + e.Message);
            }
        }

        private async Task ListenUdp()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var result = await _udpClient.ReceiveAsync();
                    var msg = Encoding.UTF8.GetString(result.Buffer);

                    if (msg.StartsWith("HELLO:"))
                    {
                        var parts = msg[6..].Split(':');
                        if (parts.Length == 3 &&
                            int.TryParse(parts[2], out var remotePort) &&
                            parts[0] != _instanceId)
                        {
                            _ = ConnectToPeer(parts[1], remotePort);
                        }
                    }
                }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) { break; }
                catch (Exception e) { Debug.LogWarning("[SecureLanPeer] UDP receive error: " + e.Message); }
            }
        }

        private void BroadcastPresence()
        {
            try
            {
                using var sender = new UdpClient();
                sender.MulticastLoopback = true;
                sender.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface,
                    IPAddress.Parse(_localIP).GetAddressBytes());
                    
                var bytes = Encoding.UTF8.GetBytes($"HELLO:{_instanceId}:{_localIP}:{_localTcpPort}");
                sender.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Parse(_settings.multicastAddress), _settings.multicastPort));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SecureLanPeer] Broadcast failed: " + e.Message);
            }
        }

        private void StartTcpServer()
        {
            // Bind to port 0 for automatic free port assignment
            _tcpListener = new TcpListener(IPAddress.Any, 0);
            _tcpListener.Start();
            
            _localTcpPort = ((IPEndPoint)_tcpListener.LocalEndpoint).Port;
            Debug.Log($"[SecureLanPeer] Local TCP Server started on port {_localTcpPort}");
            
            _ = AcceptLoop();
        }

        private async Task AcceptLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var client = await _tcpListener.AcceptTcpClientAsync();
                    var ep = (IPEndPoint)client.Client.RemoteEndPoint;
                    var remoteIp = ep.Address.ToString();

                    int remoteServerPort = await ReadHandshakePortAsync(client);
                    if (remoteServerPort <= 0) { client.Close(); continue; }

                    var peerKey = $"{remoteIp}:{remoteServerPort}";
                    if (_peers.ContainsKey(peerKey)) { client.Close(); continue; }

                    _peers[peerKey] = client;
                    _mainThreadQueue.Enqueue(() => OnPeerConnected?.Invoke(peerKey));
                    CheckExpectedPeerCount();
                    
                    _ = ReceiveLoop(peerKey, remoteIp, client);
                    Debug.Log($"[SecureLanPeer] Peer connected (inbound): {peerKey}");
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception e) { Debug.LogWarning("[SecureLanPeer] Accept error: " + e.Message); }
            }
        }

        private async Task ConnectToPeer(string ip, int port)
        {
            var peerKey = $"{ip}:{port}";
            if (_peers.ContainsKey(peerKey)) return;

            try
            {
                var client = new TcpClient();
                var connectTask = client.ConnectAsync(ip, port);
                if (await Task.WhenAny(connectTask, Task.Delay(5000, _cts.Token)) != connectTask)
                {
                    client.Close();
                    return;
                }
                await connectTask;

                var stream = client.GetStream();
                var portBytes = Encoding.UTF8.GetBytes($"{_localTcpPort}\n");
                await stream.WriteAsync(portBytes, 0, portBytes.Length);

                if (!_peers.TryAdd(peerKey, client)) { client.Close(); return; }
                
                _mainThreadQueue.Enqueue(() => OnPeerConnected?.Invoke(peerKey));
                CheckExpectedPeerCount();
                
                _ = ReceiveLoop(peerKey, ip, client);
                Debug.Log($"[SecureLanPeer] Connected to peer: {peerKey}");
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Debug.LogWarning($"[SecureLanPeer] Could not connect to {ip}:{port}: {e.Message}"); }
        }

        private async Task<int> ReadHandshakePortAsync(TcpClient client)
        {
            try
            {
                var stream = client.GetStream();
                stream.ReadTimeout = 3000;
                var buf = new byte[1];
                var lineBuf = new StringBuilder(8);
                while (lineBuf.Length < 8)
                {
                    if (!await ReadExact(stream, buf, 1)) return -1;
                    if (buf[0] == '\n') break;
                    lineBuf.Append((char)buf[0]);
                }
                stream.ReadTimeout = Timeout.Infinite;
                return int.TryParse(lineBuf.ToString().Trim(), out int p) ? p : -1;
            }
            catch { return -1; }
        }

        private async Task ReceiveLoop(string peerKey, string senderIp, TcpClient client)
        {
            var stream = client.GetStream();
            var lenBuf = new byte[4];
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    if (!await ReadExact(stream, lenBuf, 4)) break;
                    int msgLen = (lenBuf[0] << 24) | (lenBuf[1] << 16) | (lenBuf[2] << 8) | lenBuf[3];

                    if (msgLen <= 0 || msgLen > 1_048_576)
                    {
                        Debug.LogWarning($"[SecureLanPeer] Invalid message length from {senderIp}: {msgLen}");
                        break;
                    }

                    var msgBuf = new byte[msgLen];
                    if (!await ReadExact(stream, msgBuf, msgLen)) break;

                    var packet = Encoding.UTF8.GetString(msgBuf);
                    var json = Decrypt(packet);

                    _mainThreadQueue.Enqueue(() => OnJsonReceived?.Invoke(senderIp, json));
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Debug.LogWarning($"[SecureLanPeer] ReceiveLoop error ({senderIp}): {e.Message}"); }
            finally
            {
                if (_peers.TryRemove(peerKey, out _))
                {
                    _mainThreadQueue.Enqueue(() => OnPeerDisconnected?.Invoke(peerKey));
                    _hasFiredExpectedPeersEvent = false; 
                }
                
                client.Close();
                Debug.Log($"[SecureLanPeer] Peer disconnected: {senderIp}");

                if (!_cts.IsCancellationRequested)
                    _ = BroadcastWithRetryAsync(retries: 3, intervalMs: 2000);
            }
        }

        private async Task<bool> ReadExact(NetworkStream stream, byte[] buf, int count)
        {
            int received = 0;
            while (received < count)
            {
                int n = await stream.ReadAsync(buf, received, count - received, _cts.Token);
                if (n <= 0) return false;
                received += n;
            }
            return true;
        }

        public int GetPeerCount() => _peers.Count;

        public void BroadcastJson(string json)
        {
            var encrypted = Encrypt(json);
            var msgBytes = Encoding.UTF8.GetBytes(encrypted);
            int len = msgBytes.Length;
            var lenBytes = new byte[] { (byte)(len >> 24), (byte)(len >> 16), (byte)(len >> 8), (byte)len };

            var failed = new System.Collections.Generic.List<string>();

            foreach (var kv in _peers)
            {
                try
                {
                    var stream = kv.Value.GetStream();
                    stream.Write(lenBytes, 0, 4);
                    stream.Write(msgBytes, 0, msgBytes.Length);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[SecureLanPeer] Send error to {kv.Key}: {e.Message}");
                    failed.Add(kv.Key);
                }
            }

            foreach (var key in failed)
            {
                if (_peers.TryRemove(key, out var dead))
                {
                    dead.Close();
                    _mainThreadQueue.Enqueue(() => OnPeerDisconnected?.Invoke(key));
                    _hasFiredExpectedPeersEvent = false;
                }
            }
            if (failed.Count > 0 && !_cts.IsCancellationRequested)
                _ = BroadcastWithRetryAsync(retries: 3, intervalMs: 2000);

            Debug.Log($"[SecureLanPeer] Broadcast: \n{json}");
        }

        private string Encrypt(string plain)
        {
            var plainBytes = Encoding.UTF8.GetBytes(plain);
            byte[] iv, cipher;

            using (var aes = new AesCryptoServiceProvider())
            {
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = _encKey;
                aes.GenerateIV();
                iv = aes.IV;
                using var enc = aes.CreateEncryptor();
                cipher = enc.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
            }

            var macInput = new byte[iv.Length + cipher.Length];
            Buffer.BlockCopy(iv, 0, macInput, 0, iv.Length);
            Buffer.BlockCopy(cipher, 0, macInput, iv.Length, cipher.Length);
            byte[] mac;
            using (var hmac = new HMACSHA256(_macKey))
                mac = hmac.ComputeHash(macInput);

            return Convert.ToBase64String(iv) + "." +
                   Convert.ToBase64String(mac) + "." +
                   Convert.ToBase64String(cipher);
        }

        private string Decrypt(string packet)
        {
            try
            {
                var parts = packet.Split('.');
                if (parts.Length != 3) return "[Malformed packet]";

                var iv     = Convert.FromBase64String(parts[0]);
                var mac    = Convert.FromBase64String(parts[1]);
                var cipher = Convert.FromBase64String(parts[2]);

                var macInput = new byte[iv.Length + cipher.Length];
                Buffer.BlockCopy(iv, 0, macInput, 0, iv.Length);
                Buffer.BlockCopy(cipher, 0, macInput, iv.Length, cipher.Length);
                byte[] expectedMac;
                using (var hmac = new HMACSHA256(_macKey))
                    expectedMac = hmac.ComputeHash(macInput);

                if (!ConstantTimeEquals(mac, expectedMac))
                    return "[Invalid or tampered message]";

                using var aes = new AesCryptoServiceProvider();
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = _encKey;
                aes.IV = iv;
                using var dec = aes.CreateDecryptor();
                return Encoding.UTF8.GetString(dec.TransformFinalBlock(cipher, 0, cipher.Length));
            }
            catch
            {
                return "[Invalid or tampered message]";
            }
        }

        private static bool ConstantTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private string GetLocalIP()
        {
            try
            {
                foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                        return ip.ToString();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SecureLanPeer] Could not determine local IP: " + e.Message);
            }
            return "127.0.0.1";
        }
    }

    [Serializable]
    public struct BroadcastPayload
    {
        public string Action;
    }
}
