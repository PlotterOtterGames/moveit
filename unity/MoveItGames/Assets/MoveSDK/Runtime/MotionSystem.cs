using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace MoveIt.Motion
{
    /// <summary>
    /// Receives Motion Protocol v1 datagrams from the motion service (docs/protocol.md) on a background
    /// thread and feeds the newest one to the <see cref="BodyTracker"/> every frame.
    /// One instance lives for the whole app (DontDestroyOnLoad).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class MotionSystem : MonoBehaviour
    {
        public static MotionSystem Instance { get; private set; }

        [SerializeField, Tooltip("UDP port the motion service sends to.")]
        int port = 5005;

        [SerializeField, Tooltip("Accept packets from other devices on the network (e.g. a phone running the service).")]
        bool listenOnAllInterfaces = true;

        [SerializeField, Tooltip("Seconds without packets before the service counts as disconnected.")]
        float connectionTimeout = 1f;

        [Header("Smooth track: drawing the player (One Euro filter, body-relative)")]
        [SerializeField, Tooltip("Hz. Lower = less jitter when still, more lag.")]
        float smoothMinCutoff = 0.5f;

        [SerializeField, Tooltip("Per torso length per second. Higher = less lag during fast moves, more jitter.")]
        float smoothBeta = 1.5f;

        [Header("Fast track: gestures and hand cursors (body-relative)")]
        [SerializeField, Tooltip("Hz.")] float fastTrackMinCutoff = 1f;
        [SerializeField, Tooltip("Per torso length per second.")] float fastTrackBeta = 4f;

        /// <summary>UDP port the motion service sends to (the Android service is started with it).</summary>
        public int Port => port;

        public BodyTracker Tracker { get; private set; }
        public Body ActiveBody => Tracker?.Active;
        public bool IsConnected => Time.unscaledTime - _lastPacketTime < connectionTimeout;

        public string Source { get; private set; } = "";
        public float ServiceFps { get; private set; }
        public float InferenceMs { get; private set; }
        /// <summary>Frames per second the camera delivers (0 if the service doesn't report it).</summary>
        public float CameraFps { get; private set; }
        /// <summary>Pose model the service uses ("full", "lite", ...), empty if not reported.</summary>
        public string Model { get; private set; } = "";
        /// <summary>Capture-to-game latency (camera frame timestamp to this Update), smoothed.</summary>
        public float LatencyMs { get; private set; }
        public long PacketsReceived { get; private set; }
        public string LastError { get; private set; }

        /// <summary>Raised on the main thread for every packet handled (after the tracker updated).</summary>
        public event Action<MotionPacket> PacketReceived;

        UdpClient _udp;
        Thread _thread;
        volatile bool _running;
        readonly object _lock = new object();
        // Packets that arrived since the last frame, oldest first. All of them are fed to the tracker, so the filters
        // and speed estimates never skip a camera frame when the game runs slower than the camera.
        List<MotionPacket> _pending = new List<MotionPacket>(MaxPending);
        List<MotionPacket> _handling = new List<MotionPacket>(MaxPending);
        const int MaxPending = 6;
        float _lastPacketTime = -999f;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Tracker = new BodyTracker
            {
                SmoothMinCutoff = smoothMinCutoff, SmoothBeta = smoothBeta,
                FastMinCutoff = fastTrackMinCutoff, FastBeta = fastTrackBeta,
            };
        }

        void OnEnable()
        {
            if (Instance == this) StartListening();
        }

        void OnDisable() => StopListening();

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void StartListening()
        {
            if (_running) return;
            try
            {
                _udp = new UdpClient();
                _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udp.Client.Bind(new IPEndPoint(listenOnAllInterfaces ? IPAddress.Any : IPAddress.Loopback, port));
            }
            catch (Exception e)
            {
                LastError = $"Cannot listen on UDP port {port}: {e.Message}";
                Debug.LogError($"[MotionSystem] {LastError}");
                _udp?.Close();
                _udp = null;
                return;
            }

            _running = true;
            _thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "MotionSystem UDP" };
            _thread.Start();
            Debug.Log($"[MotionSystem] Listening for motion data on UDP port {port}");
        }

        void StopListening()
        {
            _running = false;
            _udp?.Close();
            _udp = null;
            _thread?.Join(200);
            _thread = null;
        }

        void ReceiveLoop()
        {
            var remote = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    byte[] data = _udp.Receive(ref remote);
                    var packet = JsonUtility.FromJson<MotionPacket>(Encoding.UTF8.GetString(data));
                    if (packet == null || packet.v != 1) continue;
                    lock (_lock)
                    {
                        if (_pending.Count >= MaxPending) _pending.RemoveAt(0);  // game stalled: keep the newest
                        _pending.Add(packet);
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
                catch (Exception e)
                {
                    LastError = e.Message;
                }
            }
        }

        void Update()
        {
            lock (_lock)
            {
                (_pending, _handling) = (_handling, _pending);
            }

            float now = Time.unscaledTime;
            if (_handling.Count == 0)
            {
                if (!IsConnected && Tracker.Bodies.Count > 0) Tracker.Clear();
                return;
            }

            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            foreach (var packet in _handling)
            {
                _lastPacketTime = now;
                PacketsReceived++;
                Source = packet.src;
                ServiceFps = packet.fps;
                InferenceMs = packet.inf;
                CameraFps = packet.cam;
                Model = packet.model ?? "";
                float latency = nowMs - packet.t;
                LatencyMs = LatencyMs <= 0f ? latency : Mathf.Lerp(LatencyMs, latency, 0.1f);

                Tracker.Update(packet, now);
                PacketReceived?.Invoke(packet);
            }
            _handling.Clear();
        }
    }
}
