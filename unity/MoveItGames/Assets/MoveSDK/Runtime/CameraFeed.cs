using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace MoveIt.Motion
{
    /// <summary>
    /// The camera picture, for games that show the player (Pikturo's camera mode): receives the camera preview stream
    /// (docs/protocol.md, "Camera preview stream") as JPEG frames on 127.0.0.1 and keeps the newest in
    /// <see cref="Texture"/>. The motion service only sends frames while a game holds a <see cref="Request"/>; the
    /// picture stays on this device and is never stored.
    ///
    /// The image is the raw camera view (not mirrored), the same space as the joints in the motion packets:
    /// texture u = 1 - viewX / aspect, v = viewY for a <see cref="Body"/> joint in view space.
    /// </summary>
    public sealed class CameraFeed : MonoBehaviour
    {
        /// <summary>Where frames arrive (localhost only).</summary>
        public static int VideoPort = 5006;
        /// <summary>Where the PC service (motion-service-py/video.py) listens for requests.</summary>
        public static int ControlPort = 5007;
        /// <summary>Requested frame width; the height follows the camera's aspect.</summary>
        public static int RequestWidth = 640;

        const int HeaderSize = 20;
        const float KeepaliveSeconds = 0.5f;

        static CameraFeed _instance;
        static int _users;

        /// <summary>Newest camera frame (raw camera view, not mirrored). Null until the first frame arrives.</summary>
        public Texture2D Texture { get; private set; }
        public bool HasFrame => Texture != null;
        /// <summary>True while frames keep coming (the last one is less than a second old).</summary>
        public bool IsLive => HasFrame && Time.unscaledTime - _lastFrameTime < 1f;
        public int Width { get; private set; }
        public int Height { get; private set; }
        public float Aspect => Height > 0 ? (float)Width / Height : 16f / 9f;
        /// <summary>Capture time of the shown frame, Unix ms (same clock as the motion packets).</summary>
        public long CaptureTime { get; private set; }
        public float Fps { get; private set; }
        /// <summary>Time the last JPEG took to decode on the main thread, ms.</summary>
        public float DecodeMs { get; private set; }
        public long FramesReceived { get; private set; }

        Socket _socket;
        Thread _thread;
        volatile bool _running;
        readonly object _lock = new object();
        byte[] _latest;
        int _latestLength;
        float _lastFrameTime = -999f, _nextKeepalive;
        Socket _control;

        /// <summary>Starts the stream (if needed) and returns the feed. Pair every call with <see cref="Release"/>.</summary>
        public static CameraFeed Request()
        {
            _users++;
            if (_instance == null)
            {
                var go = new GameObject("Camera Feed") { hideFlags = HideFlags.DontSave };
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<CameraFeed>();
            }
            return _instance;
        }

        /// <summary>One user less; the stream stops when nobody needs it.</summary>
        public static void Release()
        {
            _users = Mathf.Max(0, _users - 1);
            if (_users == 0 && _instance != null) Destroy(_instance.gameObject);
        }

        void Awake()
        {
            try
            {
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { ReceiveBufferSize = 256 * 1024 };
                _socket.Bind(new IPEndPoint(IPAddress.Loopback, VideoPort));
            }
            catch (Exception e)
            {
                Debug.LogError($"[CameraFeed] Cannot listen on UDP port {VideoPort}: {e.Message}");
                _socket?.Close();
                _socket = null;
                return;
            }
            _running = true;
            _thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "CameraFeed UDP" };
            _thread.Start();
            Debug.Log($"[CameraFeed] Listening for camera frames on UDP port {VideoPort}");
        }

        void OnDestroy()
        {
            Ask(false);
            _running = false;
            _socket?.Close();
            _thread?.Join(200);
            _control?.Close();
            if (Texture != null) Destroy(Texture);
            if (_instance == this) _instance = null;
        }

        void ReceiveLoop()
        {
            var buffer = new byte[65536];
            while (_running)
            {
                try
                {
                    int n = _socket.Receive(buffer);
                    if (n <= HeaderSize || buffer[0] != 'T' || buffer[1] != 'V' || buffer[2] != 'F' || buffer[3] != '1') continue;
                    lock (_lock)
                    {
                        if (_latest == null || _latest.Length < n) _latest = new byte[65536];
                        Buffer.BlockCopy(buffer, 0, _latest, 0, n);
                        _latestLength = n;
                    }
                }
                catch (SocketException)
                {
                    if (!_running) break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }

        void Update()
        {
            if (Time.unscaledTime >= _nextKeepalive)
            {
                _nextKeepalive = Time.unscaledTime + KeepaliveSeconds;
                Ask(true);
            }

            byte[] jpeg = null;
            long time = 0;
            lock (_lock)
            {
                if (_latestLength > 0)
                {
                    time = BitConverter.ToInt64(_latest, 4);
                    jpeg = new byte[_latestLength - HeaderSize];
                    Buffer.BlockCopy(_latest, HeaderSize, jpeg, 0, jpeg.Length);
                    _latestLength = 0;
                }
            }
            if (jpeg == null) return;

            if (Texture == null)
                Texture = new Texture2D(2, 2, TextureFormat.RGB24, false) { name = "Camera Feed", wrapMode = TextureWrapMode.Clamp };
            float start = Time.realtimeSinceStartup;
            if (!Texture.LoadImage(jpeg, true)) return;
            DecodeMs = (Time.realtimeSinceStartup - start) * 1000f;
            Width = Texture.width;
            Height = Texture.height;
            CaptureTime = time;
            float now = Time.unscaledTime;
            if (FramesReceived > 0 && now > _lastFrameTime) Fps = Mathf.Lerp(Fps <= 0f ? 30f : Fps, 1f / (now - _lastFrameTime), 0.1f);
            _lastFrameTime = now;
            FramesReceived++;
        }

        /// <summary>Tells the motion service we (still) want frames: the Android library directly, the PC service by UDP.</summary>
        static void Ask(bool on)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var service = new AndroidJavaClass("al.moveit.motion.MotionService");
                service.CallStatic("setVideo", VideoPort, RequestWidth, on);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CameraFeed] setVideo failed: {e.Message}");
            }
#else
            if (_instance == null) return;
            try
            {
                _instance._control ??= new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                byte[] msg = System.Text.Encoding.UTF8.GetBytes(
                    on ? $"{{\"video\":1,\"port\":{VideoPort},\"width\":{RequestWidth}}}" : "{\"video\":0}");
                _instance._control.SendTo(msg, new IPEndPoint(IPAddress.Loopback, ControlPort));
            }
            catch (SocketException)
            {
                // no service running yet: the next keepalive tries again
            }
#endif
        }
    }
}
