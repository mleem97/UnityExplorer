using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityExplorer.Config;

namespace UnityExplorer.MCP
{
    /// <summary>
    /// WebSocket client that connects to the MCP FastMCP server.
    /// Uses raw TCP + manual WebSocket framing for net35 compatibility.
    /// </summary>
    internal static class MCPBridge
    {
        private static TcpClient tcpClient;
        private static NetworkStream stream;
        private static Thread receiveThread;
        private static volatile bool connected;
        private static volatile bool shouldRun;
        private static float reconnectTimer;
        private static float heartbeatTimer;
        private static readonly object sendLock = new();

        private const float HEARTBEAT_INTERVAL = 5f;

        internal static bool Connected => connected;

        private static volatile bool connecting;

        internal static void Init()
        {
            CommandDispatcher.Init();
            shouldRun = true;
            if (ConfigManager.MCP_Auto_Connect.Value)
                TryConnectAsync();
        }

        internal static void Update()
        {
            if (!shouldRun) return;

            if (!connected)
            {
                if (!connecting)
                {
                    reconnectTimer -= Time.unscaledDeltaTime;
                    if (reconnectTimer <= 0f)
                    {
                        reconnectTimer = ConfigManager.MCP_Reconnect_Delay.Value;
                        TryConnectAsync();
                    }
                }
                return;
            }

            // Heartbeat
            heartbeatTimer -= Time.unscaledDeltaTime;
            if (heartbeatTimer <= 0f)
            {
                heartbeatTimer = HEARTBEAT_INTERVAL;
                SendRaw("{\"type\":\"heartbeat\"}");
            }
        }

        internal static void Shutdown()
        {
            shouldRun = false;
            Disconnect();
        }

        private static void TryConnectAsync()
        {
            if (connecting) return;
            connecting = true;
            new Thread(() =>
            {
                try { TryConnect(); }
                finally { connecting = false; }
            }) { IsBackground = true }.Start();
        }

        private static void TryConnect()
        {
            try
            {
                string url = ConfigManager.MCP_Server_URL.Value;
                // Parse ws://host:port
                string hostPort = url.Replace("ws://", "").TrimEnd('/');
                string[] parts = hostPort.Split(':');
                string host = parts[0];
                int port = parts.Length > 1 ? int.Parse(parts[1]) : 80;

                tcpClient = new TcpClient();
                tcpClient.Connect(host, port);
                stream = tcpClient.GetStream();

                // WebSocket handshake
                string key = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
                string handshake =
                    $"GET / HTTP/1.1\r\n" +
                    $"Host: {host}:{port}\r\n" +
                    $"Upgrade: websocket\r\n" +
                    $"Connection: Upgrade\r\n" +
                    $"Sec-WebSocket-Key: {key}\r\n" +
                    $"Sec-WebSocket-Version: 13\r\n\r\n";

                byte[] handshakeBytes = Encoding.ASCII.GetBytes(handshake);
                stream.Write(handshakeBytes, 0, handshakeBytes.Length);

                // Read response (we just need to consume the headers)
                byte[] buffer = new byte[4096];
                int read = stream.Read(buffer, 0, buffer.Length);
                string response = Encoding.ASCII.GetString(buffer, 0, read);
                if (!response.Contains("101"))
                {
                    ExplorerCore.LogWarning("[MCP] WebSocket handshake failed.");
                    Disconnect();
                    return;
                }

                connected = true;
                heartbeatTimer = HEARTBEAT_INTERVAL;

                // Send identity
                string identity = new JsonHelper.JsonBuilder()
                    .StartObject()
                    .Key("type").Value("identity")
                    .Key("protocol_version").Value(1)
                    .Key("game_name").Value(Application.productName)
                    .Key("unity_version").Value(Application.unityVersion)
                    .Key("explorer_version").Value(ExplorerCore.VERSION)
                    .Key("runtime").Value(
#if CPP
                        "IL2CPP"
#else
                        "Mono"
#endif
                    )
                    .EndObject()
                    .ToString();
                SendFrame(identity);

                // Start receive thread
                receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
                receiveThread.Start();

                ExplorerCore.Log("[MCP] Connected to MCP server.");
            }
            catch (Exception ex)
            {
                ExplorerCore.LogWarning($"[MCP] Connection failed: {ex.Message}");
                Disconnect();
            }
        }

        private static void ReceiveLoop()
        {
            // Capture local reference to avoid null race with Disconnect()
            NetworkStream localStream = stream;
            try
            {
                while (connected && shouldRun && localStream != null)
                {
                    string message = ReadFrame(localStream);
                    if (message == null) break;
                    CommandDispatcher.EnqueueCommand(message);
                }
            }
            catch (Exception ex)
            {
                if (connected)
                    ExplorerCore.LogWarning($"[MCP] Receive error: {ex.Message}");
            }
            finally
            {
                connected = false;
            }
        }

        internal static void SendResponse(CommandResponse response)
        {
            if (!connected) return;
            try
            {
                SendFrame(response.ToJson());
            }
            catch (Exception ex)
            {
                ExplorerCore.LogWarning($"[MCP] Send error: {ex.Message}");
                Disconnect();
            }
        }

        private static void SendRaw(string text)
        {
            if (!connected) return;
            try { SendFrame(text); }
            catch { Disconnect(); }
        }

        // ── WebSocket Frame Helpers ─────────────────────────────────────

        private static void SendFrame(string text)
        {
            byte[] payload = Encoding.UTF8.GetBytes(text);
            byte[] mask = Guid.NewGuid().ToByteArray();

            // Build frame: text opcode (0x81), masked
            int headerLen = 2;
            if (payload.Length >= 126 && payload.Length <= 65535)
                headerLen += 2;
            else if (payload.Length > 65535)
                headerLen += 8;

            byte[] frame = new byte[headerLen + 4 + payload.Length]; // +4 for mask
            frame[0] = 0x81; // FIN + text opcode

            int offset;
            if (payload.Length < 126)
            {
                frame[1] = (byte)(0x80 | payload.Length);
                offset = 2;
            }
            else if (payload.Length <= 65535)
            {
                frame[1] = 0x80 | 126;
                frame[2] = (byte)(payload.Length >> 8);
                frame[3] = (byte)(payload.Length & 0xFF);
                offset = 4;
            }
            else
            {
                frame[1] = 0x80 | 127;
                long len = payload.Length;
                for (int i = 7; i >= 0; i--)
                {
                    frame[2 + i] = (byte)(len & 0xFF);
                    len >>= 8;
                }
                offset = 10;
            }

            // Mask key
            Array.Copy(mask, 0, frame, offset, 4);
            offset += 4;

            // Masked payload
            for (int i = 0; i < payload.Length; i++)
                frame[offset + i] = (byte)(payload[i] ^ mask[i % 4]);

            lock (sendLock)
            {
                if (stream != null)
                    stream.Write(frame, 0, frame.Length);
            }
        }

        private static string ReadFrame(NetworkStream s)
        {
            // Read first 2 bytes
            byte[] header = ReadExact(s, 2);
            if (header == null) return null;

            int opcode = header[0] & 0x0F;
            bool masked = (header[1] & 0x80) != 0;
            long payloadLen = header[1] & 0x7F;

            if (opcode == 0x08) return null; // Close frame

            if (payloadLen == 126)
            {
                byte[] ext = ReadExact(s, 2);
                if (ext == null) return null;
                payloadLen = (ext[0] << 8) | ext[1];
            }
            else if (payloadLen == 127)
            {
                byte[] ext = ReadExact(s, 8);
                if (ext == null) return null;
                payloadLen = 0;
                for (int i = 0; i < 8; i++)
                    payloadLen = (payloadLen << 8) | ext[i];
            }

            byte[] maskKey = null;
            if (masked)
            {
                maskKey = ReadExact(s, 4);
                if (maskKey == null) return null;
            }

            byte[] payload = ReadExact(s, (int)payloadLen);
            if (payload == null) return null;

            if (masked)
                for (int i = 0; i < payload.Length; i++)
                    payload[i] ^= maskKey[i % 4];

            // Handle ping with pong (control frames have payload <= 125 bytes per RFC 6455)
            if (opcode == 0x09 && payload.Length <= 125)
            {
                byte[] pong = new byte[2 + payload.Length + 4];
                pong[0] = 0x8A; // FIN + pong
                pong[1] = (byte)(0x80 | payload.Length);
                byte[] pMask = Guid.NewGuid().ToByteArray();
                Array.Copy(pMask, 0, pong, 2, 4);
                for (int i = 0; i < payload.Length; i++)
                    pong[6 + i] = (byte)(payload[i] ^ pMask[i % 4]);
                lock (stream)
                    stream.Write(pong, 0, pong.Length);
                return ReadFrame(s); // read next real frame
            }

            // Only process text frames (0x01), skip pong (0x0A) and others
            if (opcode != 0x01)
                return ReadFrame(s);

            return Encoding.UTF8.GetString(payload);
        }

        private static byte[] ReadExact(NetworkStream s, int count)
        {
            if (count == 0) return new byte[0];
            byte[] buffer = new byte[count];
            int total = 0;
            while (total < count)
            {
                int read = s.Read(buffer, total, count - total);
                if (read == 0) return null; // connection closed
                total += read;
            }
            return buffer;
        }

        private static void Disconnect()
        {
            connected = false;
            try { stream?.Close(); } catch { }
            try { tcpClient?.Close(); } catch { }
            stream = null;
            tcpClient = null;
        }
    }
}
