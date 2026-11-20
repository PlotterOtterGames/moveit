using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

namespace MoveIt.Motion
{
    /// <summary>
    /// The camera picture, for games that show the player (Pikturo's camera mode, Vishu!): receives the camera preview
    /// stream (docs/protocol.md, "Camera preview stream") as JPEG frames on 127.0.0.1 and shows the newest in
    /// <see cref="Texture"/>. On the box the motion service hands the frames over inside the process instead (raw
    /// pixels from libtundupose: no JPEG encode in Java and no decode on the main thread), and with OpenGL ES Unity's
    /// render thread uploads the chosen frame into the texture straight from the plugin's memory (since 2026-11-12: the
    /// main thread copies nothing; before, each 3 MB frame was copied twice there); the JPEG stream stays the fallback.
    /// The motion service only sends frames while a game holds a <see cref="Request"/>; the picture stays on this device
    /// and is never stored. The texture has no copy on the CPU: read it on the GPU (render it into a texture).
    ///
    /// A game that draws on the player (Vishu!'s clothes) can keep a few recent frames (<see cref="History"/>) and show
    /// an older one (<see cref="ShowAt"/>): the poses arrive after their picture (the camera AI takes its time), so the
    /// picture waits for the poses that go with it.
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
        /// <summary>
        /// The frame width the game would like; the height follows the camera's aspect. The PC service sends exactly
        /// this width. The box hands over the camera's own size or exactly half of it, whichever is the smallest that is
        /// still at least three quarters of this (640 of its 1024 px camera: 512 x 384; 1024: the full picture): those
        /// two cost the camera thread ~2 ms a frame, any other size 8 ms and more (frame_scale.h in the camera plugin).
        /// </summary>
        public static int RequestWidth = 640;
        /// <summary>A width asked for instead of <see cref="RequestWidth"/>, whatever the game set (PerfLab's
        /// <c>video</c> key, to compare sizes on the box); 0 = none.</summary>
        public static int WidthOverride;
        /// <summary>
        /// On the box: copy each frame through the game's memory (the way before 2026-11-12: into a kept frame, then
        /// into the texture, then uploaded) instead of having the render thread upload it straight from the camera
        /// plugin's memory. PerfLab's <c>feed=copy</c>, to compare.
        /// </summary>
        public static bool CopyFrames;

        static int AskedWidth => WidthOverride > 0 ? WidthOverride : RequestWidth;

        const int HeaderSize = 20;
        const float KeepaliveSeconds = 0.5f;
        const int MaxHistory = 8;

        static CameraFeed _instance;
        static int _users;

        /// <summary>The shown camera frame (raw camera view, not mirrored). Null until the first frame arrives.</summary>
        public Texture2D Texture { get; private set; }
        public bool HasFrame => Texture != null;
        /// <summary>True while frames keep coming (the last one arrived less than a second ago).</summary>
        public bool IsLive => HasFrame && Time.unscaledTime - _lastReceived < 1f;
        public int Width { get; private set; }
        public int Height { get; private set; }
        public float Aspect => Height > 0 ? (float)Width / Height : 16f / 9f;
        /// <summary>Capture time of the shown frame, Unix ms (same clock as the motion packets).</summary>
        public long CaptureTime { get; private set; }
        /// <summary>Capture time of the newest frame received, Unix ms.</summary>
        public long NewestTime { get; private set; }
        public float Fps { get; private set; }
        /// <summary>Time the last shown frame took on the main thread (JPEG decode, or the upload of the box's raw pixels), ms.</summary>
        public float DecodeMs { get; private set; }
        public long FramesReceived { get; private set; }

        /// <summary>How many recent frames are kept (1 = the newest only).</summary>
        public int History
        {
            get => _history;
            set => _history = Mathf.Clamp(value, 1, MaxHistory);
        }
        /// <summary>Show the newest kept frame captured at or before this time (Unix ms); 0 = always the newest.</summary>
        public long ShowAt { get; set; }

        int _history = 1;

        sealed class Frame
        {
            public long Time, Number;
            public byte[] Data;
            public int Length, Width, Height;
            public bool Raw;
            public GCHandle Pin;
        }

        // kept frames, oldest first (only _count of them are valid)
        readonly Frame[] _frames = new Frame[MaxHistory];
        int _count;
        long _shownNumber = -1, _nextNumber = 1;

        Socket _socket;
        Thread _thread;
        volatile bool _running;
        readonly object _lock = new object();
        // datagrams received since the last frame of the game (the receive thread fills, Pump empties)
        readonly byte[][] _incoming = new byte[MaxHistory][];
        readonly int[] _incomingLength = new int[MaxHistory];
        int _incomingCount;
        float _lastFrameTime = -999f, _lastReceived = -999f, _nextKeepalive;
        int _pumpedFrame = -1;
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

        /// <summary>
        /// One user less. The stream stops when nobody needs it by the feed's next frame, so a scene that asks right
        /// after the last one let go (one camera game loaded straight after another) keeps the same feed. Destroying
        /// it here handed that scene a feed that died at the end of the frame: a game without its picture.
        /// </summary>
        public static void Release() => _users = Mathf.Max(0, _users - 1);

        void Awake()
        {
            for (int i = 0; i < MaxHistory; i++) _frames[i] = new Frame();
            try
            {
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { ReceiveBufferSize = 512 * 1024 };
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

        void OnDestroy() => Shutdown();

        bool _shutDown;

        /// <summary>Stops the stream and frees the port at once (the object itself goes at the end of the frame).</summary>
        void Shutdown()
        {
            if (_shutDown) return;
            _shutDown = true;
            Ask(false);
            _running = false;
            _socket?.Close();
            _thread?.Join(200);
            _control?.Close();
            foreach (var f in _frames)
                if (f != null && f.Pin.IsAllocated) f.Pin.Free();
            if (_keptNative > 1)
            {
                try { tundu_frame_keep(1); }   // the plugin lets the kept frames' memory go
                catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException) { }
            }
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
                        if (_incomingCount == MaxHistory)
                        {
                            // the game is far behind: drop the oldest datagram
                            var first = _incoming[0];
                            int firstLength = _incomingLength[0];
                            for (int i = 1; i < MaxHistory; i++)
                            {
                                _incoming[i - 1] = _incoming[i];
                                _incomingLength[i - 1] = _incomingLength[i];
                            }
                            _incoming[MaxHistory - 1] = first;
                            _incomingLength[MaxHistory - 1] = firstLength;
                            _incomingCount--;
                        }
                        var slot = _incoming[_incomingCount] ??= new byte[65536];
                        Buffer.BlockCopy(buffer, 0, slot, 0, n);
                        _incomingLength[_incomingCount] = n;
                        _incomingCount++;
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
            if (_users == 0)
            {
                // nobody asked again since the last Release: the stream and the port go now (a Request later in this
                // frame starts a new feed), the object at the end of the frame
                Shutdown();
                Destroy(gameObject);
                return;
            }
            Pump();
        }

        /// <summary>
        /// Takes in the frames that arrived and shows the one <see cref="ShowAt"/> asks for (once per game frame; a game
        /// calls it before reading <see cref="CaptureTime"/>, so the time and the texture belong together this frame).
        /// </summary>
        public void Pump()
        {
            if (_shutDown || _pumpedFrame == Time.frameCount) return;
            _pumpedFrame = Time.frameCount;
            if (Time.unscaledTime >= _nextKeepalive)
            {
                _nextKeepalive = Time.unscaledTime + KeepaliveSeconds;
                Ask(true);
            }
            if (ShowNative()) return;
            if (!TakeRaw()) TakeDatagrams();
            ShowChosen();
        }

        /// <summary>A free frame slot at the end of the kept ones (the oldest is dropped when they're all used).</summary>
        Frame NextSlot()
        {
            if (_count >= _history)
            {
                var first = _frames[0];
                for (int i = 1; i < _count; i++) _frames[i - 1] = _frames[i];
                _count--;
                _frames[_count] = first;
            }
            return _frames[_count++];
        }

        void TakeDatagrams()
        {
            lock (_lock)
            {
                for (int i = 0; i < _incomingCount; i++)
                {
                    byte[] d = _incoming[i];
                    long time = BitConverter.ToInt64(d, 4);
                    if (_count > 0 && time <= _frames[_count - 1].Time) continue;
                    var f = NextSlot();
                    int length = _incomingLength[i] - HeaderSize;
                    if (f.Data == null || f.Data.Length < length || f.Raw)
                    {
                        if (f.Pin.IsAllocated) f.Pin.Free();
                        f.Data = new byte[65536];
                    }
                    Buffer.BlockCopy(d, HeaderSize, f.Data, 0, length);
                    f.Length = length;
                    f.Time = time;
                    f.Number = _nextNumber++;
                    f.Raw = false;
                    NewestTime = time;
                    FramesReceived++;
                    _lastReceived = Time.unscaledTime;
                }
                _incomingCount = 0;
            }
        }

        /// <summary>The frame to show: the newest one captured at or before <see cref="ShowAt"/> (or the oldest kept).</summary>
        Frame Chosen()
        {
            if (_count == 0) return null;
            if (ShowAt <= 0) return _frames[_count - 1];
            for (int i = _count - 1; i >= 0; i--)
                if (_frames[i].Time <= ShowAt) return _frames[i];
            return _frames[0];
        }

        void ShowChosen()
        {
            var f = Chosen();
            if (f == null || f.Number == _shownNumber) return;
            float start = Time.realtimeSinceStartup;
            if (_nativeTexture)   // left by the render-thread upload (no CPU copy to load into)
            {
                if (Texture != null) Destroy(Texture);
                Texture = null;
                _nativeTexture = false;
            }
            if (f.Raw)
            {
                if (Texture != null && !Texture.isReadable)  // left by a JPEG frame (LoadImage marks it non-readable)
                {
                    Destroy(Texture);
                    Texture = null;
                }
                if (Texture == null)
                    Texture = new Texture2D(f.Width, f.Height, TextureFormat.RGBA32, false) { name = "Camera Feed", wrapMode = TextureWrapMode.Clamp };
                else if (Texture.width != f.Width || Texture.height != f.Height || Texture.format != TextureFormat.RGBA32)
                    Texture.Reinitialize(f.Width, f.Height, TextureFormat.RGBA32, false);
                Texture.LoadRawTextureData(f.Pin.AddrOfPinnedObject(), f.Width * f.Height * 4);
                Texture.Apply(false);
            }
            else
            {
                if (Texture == null)
                    Texture = new Texture2D(2, 2, TextureFormat.RGB24, false) { name = "Camera Feed", wrapMode = TextureWrapMode.Clamp };
                var jpeg = new byte[f.Length];
                Buffer.BlockCopy(f.Data, 0, jpeg, 0, f.Length);
                if (!Texture.LoadImage(jpeg, true)) return;
            }
            _shownNumber = f.Number;
            DecodeMs = (Time.realtimeSinceStartup - start) * 1000f;
            Width = Texture.width;
            Height = Texture.height;
            CaptureTime = f.Time;
            float now = Time.unscaledTime;
            if (_lastFrameTime > 0f && now > _lastFrameTime) Fps = Mathf.Lerp(Fps <= 0f ? 30f : Fps, 1f / (now - _lastFrameTime), 0.1f);
            _lastFrameTime = now;
        }

        // The box: raw frames handed over inside the process (Plugins/Android/MoveItMotion.androidlib,
        // src/main/cpp/frame_share.cpp), RGBA rows bottom-up. The plugin keeps the last few (History) and Unity's render
        // thread uploads the chosen one straight from there (ShowNative); the copy path (TakeRaw) is the fallback.
        long _rawFrame;

#if UNITY_ANDROID && !UNITY_EDITOR
        static bool _rawUnavailable;
        static bool _nativeUnavailable;
        [DllImport("tundupose")] static extern long tundu_frame_info(out int width, out int height, out long time);
        [DllImport("tundupose")] static extern long tundu_frame_copy(IntPtr dst, int capacity, out int width, out int height, out long time);
        [DllImport("tundupose")] static extern void tundu_frame_keep(int count);
        [DllImport("tundupose")] static extern long tundu_frame_pick(long at, out int width, out int height, out long time);
        [DllImport("tundupose")] static extern int tundu_frame_request(int id, long number, uint texture, int width, int height);
        [DllImport("tundupose")] static extern IntPtr tundu_frame_render_func();
#else
        static bool _rawUnavailable = true;
        static bool _nativeUnavailable = true;
        static long tundu_frame_info(out int width, out int height, out long time) => throw new DllNotFoundException("tundupose");
        static long tundu_frame_copy(IntPtr dst, int capacity, out int width, out int height, out long time) => throw new DllNotFoundException("tundupose");
        static void tundu_frame_keep(int count) => throw new DllNotFoundException("tundupose");
        static long tundu_frame_pick(long at, out int width, out int height, out long time) => throw new DllNotFoundException("tundupose");
        static int tundu_frame_request(int id, long number, uint texture, int width, int height) => throw new DllNotFoundException("tundupose");
        static IntPtr tundu_frame_render_func() => throw new DllNotFoundException("tundupose");
#endif

        IntPtr _renderFunc, _textureId;
        int _keptNative, _eventId;
        bool _nativeTexture;

        /// <summary>
        /// The box, OpenGL ES: the frame <see cref="ShowAt"/> asks for, uploaded into <see cref="Texture"/> by Unity's
        /// render thread straight from the camera plugin's memory (GL.IssuePluginEvent before this frame's drawing). The
        /// main thread copies nothing. False where that isn't possible (then the copy path or the JPEG stream).
        /// </summary>
        bool ShowNative()
        {
            if (_nativeUnavailable || _rawUnavailable || CopyFrames) return false;
            try
            {
                if (_renderFunc == IntPtr.Zero)
                {
                    if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3)
                    {
                        _nativeUnavailable = true;
                        Debug.Log($"[CameraFeed] {SystemInfo.graphicsDeviceType}: frames copied through the game");
                        return false;
                    }
                    _renderFunc = tundu_frame_render_func();
                }
                long newest = tundu_frame_info(out _, out _, out long newestTime);
                if (newest == 0) return false;   // no raw frames (yet): the JPEG stream may bring them
                if (newest != _rawFrame)
                {
                    _rawFrame = newest;
                    NewestTime = newestTime;
                    FramesReceived++;
                    _lastReceived = Time.unscaledTime;
                }
                if (_keptNative != _history)
                {
                    tundu_frame_keep(_history);
                    _keptNative = _history;
                }
                long number = tundu_frame_pick(ShowAt, out int width, out int height, out long time);
                if (number == 0 || number == _shownNumber) return true;
                if (Texture == null || !_nativeTexture || Texture.width != width || Texture.height != height)
                {
                    if (Texture != null) Destroy(Texture);
                    Texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = "Camera Feed", wrapMode = TextureWrapMode.Clamp };
                    Texture.Apply(false, true);   // its memory on the GPU now; no copy kept on the CPU
                    _textureId = Texture.GetNativeTexturePtr();
                    _nativeTexture = true;
                }
                _eventId = (_eventId + 1) & 3;
                if (tundu_frame_request(_eventId, number, (uint)_textureId.ToInt64(), width, height) == 0) return true;
                GL.IssuePluginEvent(_renderFunc, _eventId);
                _shownNumber = number;
                DecodeMs = 0f;
                Width = width;
                Height = height;
                CaptureTime = time;
                float now = Time.unscaledTime;
                if (_lastFrameTime > 0f && now > _lastFrameTime) Fps = Mathf.Lerp(Fps <= 0f ? 30f : Fps, 1f / (now - _lastFrameTime), 0.1f);
                _lastFrameTime = now;
                return true;
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                _nativeUnavailable = true;
#if UNITY_ANDROID && !UNITY_EDITOR
                Debug.Log($"[CameraFeed] No render-thread upload in this build, copying frames: {e.Message}");
#endif
                return false;
            }
        }

        /// <summary>Keeps the box's newest raw frame, if there's a new one. False where there's no such library.</summary>
        bool TakeRaw()
        {
            if (_rawUnavailable) return false;
            try
            {
                long number = tundu_frame_info(out int width, out int height, out _);
                if (number == 0 || width <= 0 || height <= 0) return false;   // no raw frames (yet): the JPEG stream may bring them
                if (number == _rawFrame) return true;
                int size = width * height * 4;
                var f = NextSlot();
                if (f.Data == null || f.Data.Length < size || !f.Raw || !f.Pin.IsAllocated)
                {
                    if (f.Pin.IsAllocated) f.Pin.Free();
                    f.Data = new byte[size];
                    f.Pin = GCHandle.Alloc(f.Data, GCHandleType.Pinned);
                }
                f.Raw = true;
                number = tundu_frame_copy(f.Pin.AddrOfPinnedObject(), f.Data.Length, out width, out height, out long time);
                if (number == 0)
                {
                    _count--;   // nothing came: give the slot back
                    return true;
                }
                _rawFrame = number;
                f.Width = width;
                f.Height = height;
                f.Time = time;
                f.Number = _nextNumber++;
                NewestTime = time;
                FramesReceived++;
                _lastReceived = Time.unscaledTime;
                return true;
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                _rawUnavailable = true;
#if UNITY_ANDROID && !UNITY_EDITOR
                Debug.Log($"[CameraFeed] No raw frames in this build, using the JPEG stream: {e.Message}");
#endif
                return false;
            }
        }

        /// <summary>Tells the motion service we (still) want frames: the Android library directly, the PC service by UDP.</summary>
        static void Ask(bool on)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var service = new AndroidJavaClass("al.moveit.motion.MotionService");
                service.CallStatic("setVideo", VideoPort, AskedWidth, on);
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
                    on ? $"{{\"video\":1,\"port\":{VideoPort},\"width\":{AskedWidth}}}" : "{\"video\":0}");
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
