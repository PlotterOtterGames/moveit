using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MoveIt.Motion;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using Joint = MoveIt.Motion.Joint;

namespace MoveIt.Games.Paint
{
    /// <summary>
    /// "Pikturo!": paint on the TV with your hands. <b>One hand paints, the other is the switch</b>: raise the free
    /// hand to your chest and the brush paints, lower it and the brush stops (<see cref="PaintPen"/>). The painting hand
    /// is the right one by default (the "Dora" button swaps it, remembered). Toolbars are pressed by holding either
    /// hand on a button: brushes on the left, colours on the right, actions on top.
    ///
    /// The brush cursor is drawn between real camera samples, about one camera frame in the past
    /// (<see cref="HandInterpolator"/>): it never slides past the point where the hand stopped, so lines and corners
    /// come out where they were meant. The switch state travels with each sample, so a stroke starts and ends exactly
    /// where the brush was when the switch went up and down.
    ///
    /// Mechanics from the classic painting games: colour palette, brush sizes, marker / rainbow / spray / neon /
    /// glitter brushes, stamps (Mario Paint, Kid Pix), eraser, paint bucket, undo, clear, left-right symmetry,
    /// colouring pages (line art the bucket fills, PNGs in Resources/Paint/Pages), and saving a photo.
    ///
    /// <b>Camera mode</b> ("Pikturo mbi veten!"): the paper becomes transparent and the camera picture shows behind it,
    /// so children can paint a dress, a crown or wings on themselves. The picture is framed on the player (like the
    /// hands' reach zone, from the knees to above the head), so the hands line up with their image and the toolbars
    /// sit within reach. While nobody paints, the framing follows the player, and the painting moves with it.
    /// </summary>
    public sealed class PaintGame : MonoBehaviour
    {
        public enum Mode { Paper, Camera }

        enum ButtonKind { Tool, Color, Undo, Size, Symmetry, Page, Hand, Clear, Mode, Photo }

        sealed class Button
        {
            public ButtonKind Kind;
            public int Index;
            public Rect Rect;
            /// <summary>Hold longer: the action changes a lot (clear, camera, photo).</summary>
            public bool Big;
            /// <summary>Keeps pressing while held (cycle buttons, undo).</summary>
            public bool Repeat;
        }

        sealed class Hand
        {
            public readonly PaintPen Pen = new PaintPen();
            public BrushStroke Stroke;
            public Joint Shoulder, Elbow, Wrist;
            public Color Tint;
            public float Speed;
            public Button Hover;
            public float Dwell;
            public bool Pressed;
            public bool FillDone;
            public int Owner;
            /// <summary>
            /// The brush cursor: between real camera samples, ~one camera frame behind, never past a stop. In
            /// tools/motion/cursor_sim.py: overshoot at stops 8 px (was 55–68), 3.5 px/frame stutter, ~125 ms lag.
            /// </summary>
            public readonly HandInterpolator Track = new HandInterpolator(4f, 1f, 1f);
            /// <summary>Camera mode: the free hand's cursor (for buttons), from the raw samples like BodyHands' cursors.</summary>
            public readonly HandInterpolator CameraCursor = new HandInterpolator(1f, 4f, 2f) { Margin = 0.5f, MaxExtrapolation = 0.08f };
            /// <summary>Seen by the camera, through single unsure frames (a dip used to end the stroke and start a new one).</summary>
            public readonly HandPresence Presence = new HandPresence();
            public double CameraSampleTime;
            public Vector2 CameraLast, CameraVelocity;
            /// <summary>Where this hand's cursor is drawn this frame, and whether it is shown.</summary>
            public Vector2 Cursor;
            public bool CursorVisible;
            /// <summary>Recent (time, speed) while painting, oldest first: finds the arm-drop at the end of a stroke.</summary>
            public readonly List<Vector2> History = new List<Vector2>();
        }

        [Header("Scene")]
        [SerializeField] Camera cam;
        [SerializeField, Tooltip("Tundu/Fx: paper colour or the camera picture.")] Material backgroundMaterial;
        [SerializeField, Tooltip("Tundu/Fx, premultiplied (One, OneMinusSrcAlpha): the painting.")] Material canvasMaterial;
        [SerializeField, Tooltip("Tundu/Fx, alpha blended: colouring page lines.")] Material lineArtMaterial;
        [SerializeField, Tooltip("Tundu/Brush: stamps into the painting.")] Material brushMaterial;

        [Header("Canvas")]
        [SerializeField] int canvasWidth = 1280;
        [SerializeField] int canvasHeight = 720;
        [SerializeField] Color paperColor = new Color(1f, 0.973f, 0.933f);

        [Header("Hands")]
        [SerializeField, Tooltip("Seconds a hand must stay on a button to press it.")]
        float dwellSeconds = 0.6f;
        [SerializeField, Tooltip("Hold time for buttons that change a lot (clear, camera, photo).")]
        float bigDwellSeconds = 1f;
        [SerializeField, Tooltip("Cycle buttons (size, stamps, pages, undo) press again after this much longer.")]
        float repeatSeconds = 1f;
        [SerializeField, Tooltip("An arm within this many degrees of hanging straight down is resting (brush up).")]
        float restAngle = 35f;
        [SerializeField, Tooltip("When the brush lifts, the fast movement just before (screen heights per second) was the arm " +
                                 "dropping or leaving, not painting: its paint is wiped. Paint stays wet this long to allow it.")]
        float tailSpeed = 0.9f;
        [SerializeField] float wetSeconds = 0.5f;

        [Header("One-hand painting: the other hand is the switch")]
        [SerializeField, Tooltip("The switch turns on when the free hand's wrist is this high above the hips " +
                                 "(torso lengths: 0 = hips, 1 = shoulders): lifted to the chest.")]
        float switchOnHeight = 0.6f;
        [SerializeField, Tooltip("...and off below this height (lower than 'on', so it doesn't flicker).")]
        float switchOffHeight = 0.35f;

        [Header("Camera mode framing (torso lengths)")]
        [SerializeField, Tooltip("The picture shows from this far below the hips (1 = about the knees)...")]
        float cameraBelowHips = 1f;
        [SerializeField, Tooltip("...to this far above the shoulders (hands raised high).")]
        float cameraAboveShoulders = 1.3f;
        [SerializeField, Tooltip("Hands reach this far either side of the shoulders (same as BodyHands' zone).")]
        float reachHalfWidth = 1.6f;

        [Header("Photos")]
        [SerializeField, Tooltip("Newest photos kept on the box (older ones are deleted).")]
        int keepPhotos = 60;

        public static readonly Color[] Palette =
        {
            Hex("#FF5A4E"), Hex("#FF9F1C"), Hex("#FFC93C"), Hex("#6FD66A"), Hex("#31C6F4"), Hex("#2F6BFF"),
            Hex("#7B61FF"), Hex("#FF7EC8"), Hex("#8B5A2B"), Hex("#1E1E24"), Hex("#FFFFFF"),
        };
        static readonly float[] Sizes = { 14f, 28f, 52f };
        static readonly string[] SizeNames = { "S", "M", "L" };
        static readonly BrushShape[] StampShapes = { BrushShape.Star, BrushShape.Heart, BrushShape.Flower, BrushShape.Footprint, BrushShape.Bubble };
        const int ToolCount = 8;

        public Mode CurrentMode { get; private set; } = Mode.Paper;
        public int ColorIndex { get; private set; }
        public PaintTool Tool { get; private set; } = PaintTool.Marker;
        public int SizeIndex { get; private set; } = 1;
        public bool Mirror { get; private set; }
        public BrushShape StampShape { get; private set; } = BrushShape.Star;
        /// <summary>Colouring page shown, -1 = blank paper.</summary>
        public int PageIndex { get; private set; } = -1;
        public int PageCount => _pages?.Length ?? 0;
        public PaintCanvas Canvas => CurrentMode == Mode.Camera ? _cameraCanvas : _paperCanvas;
        public PaintPen LeftPen => _left.Pen;
        public PaintPen RightPen => _right.Pen;
        /// <summary>Which hand paints (the other one is the switch). Remembered on the box.</summary>
        public bool RightHanded { get; private set; } = true;
        /// <summary>The free hand is raised: the brush paints.</summary>
        public bool SwitchOn { get; private set; }
        /// <summary>Strokes started since the game opened (the "raise your other hand" hint goes away after a few).</summary>
        public int StrokeCount { get; private set; }
        /// <summary>Camera mode: the part of the camera picture on screen, in view space (Body coordinates).</summary>
        public Rect CameraCrop => _crop;
        public CameraFeed Feed => _feed;
        public string LastPhoto { get; private set; }

        PaintCanvas _paperCanvas, _cameraCanvas;
        Material _bg, _canvasMat, _lineMat, _brush;
        Mesh _quad;
        Transform _bgQuad, _canvasQuad, _lineQuad;
        Texture2D[] _pages;
        readonly Dictionary<int, bool[]> _walls = new Dictionary<int, bool[]>();
        BodyHands _hands;
        CameraFeed _feed;
        readonly Hand _left = new Hand { Owner = 0, Stroke = new BrushStroke(11, 0), Shoulder = Joint.LeftShoulder, Elbow = Joint.LeftElbow, Wrist = Joint.LeftWrist };
        readonly Hand _right = new Hand { Owner = 1, Stroke = new BrushStroke(17, 1), Shoulder = Joint.RightShoulder, Elbow = Joint.RightElbow, Wrist = Joint.RightWrist };
        readonly List<Button> _buttons = new List<Button>();
        Rect _leftBar, _rightBar, _topBar;
        float _guiWidth;
        Mode _laidOutFor;
        Rect _crop;
        bool _cropValid;
        float _cropUnit;
        string _toast;
        float _toastUntil, _flash, _introUntil, _stampSoundAt;
        Color _flashColor = Color.white;
        AudioSource _brushLoop;
        float _brushLevel;
        MotionDebugOverlay _mirror;
        bool _mirrorWasVisible;
        GUIStyle _round, _label;
        int _photoCount;
        double _lastBodySample;
        const string RightHandedKey = "paint.rightHanded";

        Hand BrushHand => RightHanded ? _right : _left;
        Hand FreeHand => RightHanded ? _left : _right;

        string PhotoDir => Path.Combine(Application.persistentDataPath, "Photos");
        BrushSettings Settings => new BrushSettings
        {
            Tool = Tool, Color = Palette[ColorIndex], Size = Sizes[SizeIndex], StampShape = StampShape, Mirror = Mirror,
        };

        // ---------------------------------------------------------------- lifecycle

        void Start()
        {
            _hands = BodyHands.Instance != null ? BodyHands.Instance : FindAnyObjectByType<BodyHands>();
            // The skeleton mirror lives on the app-wide motion rig: hide it here (it would cover the colours, and camera
            // mode shows the player anyway), and give it back to the next scene.
            _mirror = MotionSystem.Instance != null ? MotionSystem.Instance.GetComponent<MotionDebugOverlay>() : null;
            if (_mirror != null)
            {
                _mirrorWasVisible = _mirror.Visible;
                _mirror.Visible = false;
            }

            _bg = new Material(backgroundMaterial);
            _canvasMat = new Material(canvasMaterial);
            _lineMat = new Material(lineArtMaterial);
            _brush = new Material(brushMaterial);
            _quad = MakeQuadMesh();
            _bgQuad = CreateQuad("Background", _bg, 0);
            _canvasQuad = CreateQuad("Painting", _canvasMat, 1);
            _lineQuad = CreateQuad("Colouring page", _lineMat, 2);

            _paperCanvas = NewCanvas();
            _pages = Resources.LoadAll<Texture2D>("Paint/Pages").OrderBy(t => t.name).ToArray();
            _left.Tint = Brand.Current.PlayerColor(1);
            _right.Tint = Brand.Current.PlayerColor(0);
            _photoCount = Directory.Exists(PhotoDir) ? Directory.GetFiles(PhotoDir, "*.png").Length : 0;
            RightHanded = PlayerPrefs.GetInt(RightHandedKey, 1) == 1;

            _brushLoop = gameObject.AddComponent<AudioSource>();
            _brushLoop.clip = Resources.Load<AudioClip>("Sfx/paint_brush");
            _brushLoop.loop = true;
            _brushLoop.volume = 0f;
            if (_brushLoop.clip != null) _brushLoop.Play();

            ApplyMode();
            _introUntil = Time.unscaledTime + 7f;
            GameAudio.PlayMusic("paint");
            Debug.Log($"[Paint] Ready: {_pages.Length} colouring pages, canvas {canvasWidth}x{canvasHeight}");
        }

        void OnDestroy()
        {
            if (_feed != null) CameraFeed.Release();
            _feed = null;
            if (_mirror != null) _mirror.Visible = _mirrorWasVisible;
            _paperCanvas?.Dispose();
            _cameraCanvas?.Dispose();
            foreach (var m in new[] { _bg, _canvasMat, _lineMat, _brush })
                if (m != null) Destroy(m);
            if (_quad != null) Destroy(_quad);
        }

        void Update()
        {
            if (GameNavigation.HandleCommonKeys()) return;
            float dt = Time.deltaTime;
            _guiWidth = Screen.width / (Screen.height / GameHud.Height);
            Layout();
            FitQuads();
            HandleKeys();

            var body = MotionSystem.Instance != null ? MotionSystem.Instance.ActiveBody : null;
            if (CurrentMode == Mode.Camera) UpdateCameraPicture(body, dt);

            bool mouse = _hands != null && _hands.UsingMouse;
            Canvas.Clock = Time.unscaledTime;
            if (mouse) UpdateMouse(dt);
            else
            {
                if (body == null) SwitchOn = false;
                else if (body.SampleTime != _lastBodySample)
                {
                    _lastBodySample = body.SampleTime;
                    OnBodySample(body);
                }
                UpdateBrush(body, dt);
                UpdateFreeHand(body, dt);
            }
            Canvas.Dry();
            Canvas.Flush();
            Canvas.Present();
            _canvasMat.SetTexture("_BaseMap", Canvas.Shown);
            UpdateSound(dt);
            _flash = Mathf.Max(0f, _flash - dt * 2.5f);
        }

        // ---------------------------------------------------------------- hands

        /// <summary>
        /// A new camera sample: is the switch (the free hand) up, and where is the brush hand? Both go into the brush's
        /// track together, so the stroke follows the switch at exactly the right moments.
        /// </summary>
        void OnBodySample(Body body)
        {
            double now = Time.unscaledTimeAsDouble;
            var free = FreeHand;
            bool freeSeen = free.Presence.Sample(body.Visibility[(int)free.Wrist], now);
            bool freeOnToolbar = free.CursorVisible && !OverPicture(free.Cursor);   // it's pressing a button, not switching
            if (freeOnToolbar || !free.Presence.Tracked(now) || body.FastTorsoLength <= 0.02f) SwitchOn = false;
            else if (freeSeen)
                SwitchOn = SwitchState(SwitchOn, (body.Fast[(int)free.Wrist].y - body.FastHipCenter.y) / body.FastTorsoLength,
                                       switchOnHeight, switchOffHeight);
            // (else a frame the camera AI is unsure about: the switch stays as it was)
            if (freeSeen && CurrentMode == Mode.Camera && CameraPosition(body, free, body.Raw, out var cursor))
                free.CameraCursor.Sample(body.SampleTime, cursor, false, now);

            var brush = BrushHand;
            if (brush.Presence.Sample(body.Visibility[(int)brush.Wrist], now) && BrushSample(body, brush, out var position, out double time))
                brush.Track.Sample(time, position, SwitchOn, now);
        }

        /// <summary>The switch with hysteresis: on above <paramref name="onHeight"/>, off again only below <paramref name="offHeight"/>.</summary>
        public static bool SwitchState(bool wasOn, float height, float onHeight, float offHeight) =>
            wasOn ? height > offHeight : height > onHeight;

        /// <summary>The brush hand's position this sample (low-lag track) and its capture time: the hand zone (paper) or
        /// the framed camera picture.</summary>
        bool BrushSample(Body body, Hand hand, out Vector2 position, out double time)
        {
            time = body.SampleTime;
            if (CurrentMode == Mode.Camera)
                return CameraSample(body, hand, out position);
            var h = _hands == null ? default : hand == _left ? _hands.Left : _hands.Right;
            position = h.SamplePosition;
            time = h.SampleTime;
            hand.Speed = h.Speed;
            return h.Tracked;
        }

        void UpdateBrush(Body body, float dt)
        {
            var hand = BrushHand;
            double now = Time.unscaledTimeAsDouble;
            bool tracked = body != null && hand.Presence.Tracked(now) && hand.Track.HasSample;
            if (!tracked) hand.Track.Reset();
            bool switchAt = false;
            var pos = tracked ? hand.Track.Evaluate(now, dt, out switchAt) : default;
            bool onScreen = tracked && OnScreen(pos);
            bool resting = tracked && IsResting(body, hand);
            int change = hand.Pen.UpdateSwitch(tracked, pos, onScreen, onScreen && OverPicture(pos), resting, switchAt);
            // Switch down = a deliberate stop: the stroke ends as it is. Losing the hand, resting it or leaving the picture
            // with the switch still up: that last fast movement wasn't painting, wipe it.
            Apply(hand, change, pos, dt, trimTail: switchAt);
            hand.Cursor = pos;
            hand.CursorVisible = hand.Pen.Visible;
            Dwell(hand, onScreen && !resting ? ButtonAt(pos) : null, dt);
        }

        /// <summary>The free hand: the switch (see <see cref="OnBodySample"/>), and a cursor that can press buttons.</summary>
        void UpdateFreeHand(Body body, float dt)
        {
            var hand = FreeHand;
            Vector2 pos;
            bool tracked;
            if (CurrentMode == Mode.Camera)
            {
                double now = Time.unscaledTimeAsDouble;
                tracked = body != null && hand.Presence.Tracked(now) && hand.CameraCursor.HasSample && _crop.height > 0f;
                if (!tracked) hand.CameraCursor.Reset();
                pos = tracked ? hand.CameraCursor.Evaluate(now, dt, out _) : default;
            }
            else
            {
                var h = _hands == null ? default : hand == _left ? _hands.Left : _hands.Right;
                tracked = h.Tracked && body != null;
                pos = h.Position;
            }
            bool resting = tracked && IsResting(body, hand);
            hand.Cursor = pos;
            hand.CursorVisible = tracked && !resting && OnScreen(pos);
            Dwell(hand, hand.CursorVisible ? ButtonAt(pos) : null, dt);
        }

        /// <summary>Development without a camera: the mouse is the brush, its button the switch.</summary>
        void UpdateMouse(float dt)
        {
            var hand = BrushHand;
            FreeHand.CursorVisible = false;
            Dwell(FreeHand, null, dt);
            var pos = _hands.Right.Position;
            bool onScreen = OnScreen(pos);
            var mouse = Mouse.current;
            bool held = mouse != null && mouse.leftButton.isPressed;
            int change = hand.Pen.UpdateMouse(pos, onScreen, onScreen && OverPicture(pos), held);
            Apply(hand, change, pos, dt, trimTail: false);
            hand.Cursor = pos;
            hand.CursorVisible = onScreen;
            var button = onScreen ? ButtonAt(pos) : null;
            if (button != null && mouse != null && mouse.leftButton.wasPressedThisFrame) Press(button);
            hand.Hover = button;
            hand.Dwell = 0f;
        }

        /// <summary>Starts, continues or ends this hand's stroke from the pen's state.</summary>
        void Apply(Hand hand, int change, Vector2 screen, float dt, bool trimTail = true)
        {
            var canvasPos = new Vector2(screen.x * canvasWidth, screen.y * canvasHeight);
            if (change > 0)
            {
                _introUntil = Mathf.Min(_introUntil, Time.unscaledTime + 0.5f);
                bool otherPainting = (hand == _left ? _right : _left).Pen.State == PenState.Painting;
                if (!otherPainting) Canvas.PushUndo();   // both hands painting together = one undo step
                if (Tool == PaintTool.Fill)
                {
                    int filled = Canvas.Fill(canvasPos, Palette[ColorIndex], CurrentMode == Mode.Paper ? Walls(PageIndex) : null);
                    hand.FillDone = true;
                    GameAudio.Sfx(filled > 0 ? "paint_fill" : "ui_back", filled > 0 ? 1f : 0.4f);
                    Debug.Log($"[Paint] Fill at {canvasPos} with colour {ColorIndex}: {filled} pixels");
                }
                else
                {
                    hand.FillDone = false;
                    hand.History.Clear();
                    hand.Stroke.Begin(Canvas, Settings, canvasPos);
                    StrokeCount++;
                    GameAudio.Sfx("paint_down", 0.5f);
                    Debug.Log($"[Paint] Stroke starts at {canvasPos} ({Tool}, colour {ColorIndex}, {(hand == _left ? "left" : "right")} hand)");
                }
            }
            else if (change < 0) EndStroke(hand, change, trimTail);

            if (hand.Pen.State == PenState.Painting && hand.Stroke.Active)
            {
                hand.History.Add(new Vector2(Time.unscaledTime, hand.Speed));
                if (hand.History.Count > 90) hand.History.RemoveAt(0);
                int stamps = hand.Stroke.StampsPlaced;
                hand.Stroke.MoveTo(Canvas, Settings, canvasPos, dt);
                if (hand.Stroke.StampsPlaced > stamps && Time.unscaledTime > _stampSoundAt)
                {
                    _stampSoundAt = Time.unscaledTime + 0.12f;
                    GameAudio.Sfx("paint_stamp", 0.7f, 1f + 0.08f * (hand.Stroke.StampsPlaced % 5), 0.03f);
                }
            }
        }

        void EndStroke(Hand hand, int change, bool trimTail)
        {
            if (change >= 0 || !hand.Stroke.Active) return;
            if (!trimTail)
            {
                hand.Stroke.End(Canvas, Settings);
                Debug.Log($"[Paint] Stroke ends: {hand.Stroke.Distance:0} px");
                return;
            }
            hand.Stroke.Abort();
            // The brush lifted because the arm dropped (or the hand left the picture): that last movement was fast.
            // Wipe the still-wet paint from where the hand got fast, so the stroke has no tail.
            var h = hand.History;
            float now = Time.unscaledTime;
            int k = h.Count - 1;
            while (k >= 0 && h[k].y < tailSpeed && now - h[k].x < 0.15f) k--;   // it may have slowed down at the end
            int wiped = 0;
            if (k >= 0 && h[k].y >= tailSpeed)
            {
                while (k > 0 && h[k - 1].y >= tailSpeed) k--;
                wiped = Canvas.Wipe(hand.Owner, h[k].x - 0.05f);
            }
            Debug.Log($"[Paint] Stroke ends: {hand.Stroke.Distance:0} px, tail of {wiped} stamps wiped");
        }

        /// <summary>Ends every stroke as it is and dries the paint (before a toolbar action changes the picture).</summary>
        void LiftAll()
        {
            foreach (var hand in new[] { _left, _right })
            {
                EndStroke(hand, hand.Pen.Lift(), trimTail: false);
                hand.Stroke.End(Canvas, Settings);
            }
            Canvas.DryAll();
            Canvas.Flush();
            _canvasMat.SetTexture("_BaseMap", Canvas.Shown);
        }

        PaintCanvas NewCanvas() => new PaintCanvas(canvasWidth, canvasHeight, _brush) { WetSeconds = wetSeconds };

        /// <summary>Holding a hand on a button presses it (a fill bar shows the progress).</summary>
        void Dwell(Hand hand, Button button, float dt)
        {
            if (button != hand.Hover)
            {
                hand.Hover = button;
                hand.Dwell = 0f;
                hand.Pressed = false;
                if (button != null) GameAudio.Sfx("ui_move", 0.35f);
            }
            if (button == null) return;
            hand.Dwell += dt;
            float needed = button.Big ? bigDwellSeconds : dwellSeconds;
            if (!hand.Pressed && hand.Dwell >= needed)
            {
                hand.Pressed = true;
                Press(button);
            }
            else if (hand.Pressed && button.Repeat && hand.Dwell >= needed + repeatSeconds)
            {
                hand.Dwell = needed;
                Press(button);
            }
        }

        float DwellProgress(Hand hand) =>
            hand.Hover == null || hand.Pressed ? 0f : Mathf.Clamp01(hand.Dwell / (hand.Hover.Big ? bigDwellSeconds : dwellSeconds));

        /// <summary>
        /// Arm hanging straight down at the side = resting, the brush lifts. An arm reaching forward and down (painting
        /// low, toward the TV) also points down in the picture, but it looks short there. (World depth would tell
        /// "in front of the body" too, but MediaPipe's wrist depth is too noisy for a hanging arm.)
        /// </summary>
        bool IsResting(Body body, Hand hand)
        {
            if (body == null || !body.IsVisible(hand.Elbow, 0.3f)) return false;
            Vector2 shoulder = body.Fast[(int)hand.Shoulder], elbow = body.Fast[(int)hand.Elbow], wrist = body.Fast[(int)hand.Wrist];
            if (Vector2.Angle(elbow - shoulder, Vector2.down) >= restAngle || Vector2.Angle(wrist - elbow, Vector2.down) >= restAngle)
                return false;
            return Vector2.Distance(shoulder, wrist) >= 0.85f * body.FastTorsoLength;   // shorter = foreshortened, reaching forward
        }

        /// <summary>
        /// Camera mode: the hand's place in the framed camera picture this sample, and its speed from the last two
        /// samples (screen heights per second).
        /// </summary>
        bool CameraSample(Body body, Hand hand, out Vector2 position)
        {
            if (!CameraPosition(body, hand, body.Fast, out position)) return false;
            float dt = (float)(body.SampleTime - hand.CameraSampleTime);
            bool first = hand.CameraSampleTime <= 0 || dt < 0 || dt >= 0.25f;
            if (first) hand.CameraVelocity = Vector2.zero;
            else if (dt >= Body.MinSpeedInterval)   // (a burst of samples 1 ms apart would turn noise into speed)
                hand.CameraVelocity = Vector2.Lerp((position - hand.CameraLast) / dt, hand.CameraVelocity, 0.4f);
            if (first || dt >= Body.MinSpeedInterval)
            {
                hand.CameraSampleTime = body.SampleTime;
                hand.CameraLast = position;
            }
            var v = hand.CameraVelocity;
            hand.Speed = new Vector2(v.x * _crop.width / _crop.height, v.y).magnitude;   // screen heights/s
            return true;
        }

        /// <summary>Camera mode: where the hand is in the framed camera picture, on a track of the body (Fast, Raw).</summary>
        bool CameraPosition(Body body, Hand hand, Vector3[] track, out Vector2 position)
        {
            position = default;
            if (_crop.height <= 0f) return false;
            Vector2 tip = body.HandTip(track, hand.Elbow, hand.Wrist, 0.3f);   // toward the fingertips, like BodyHands
            position = new Vector2((tip.x - _crop.xMin) / _crop.width, (tip.y - _crop.yMin) / _crop.height);
            return true;
        }

        static bool OnScreen(Vector2 p) => p.x >= 0f && p.x <= 1f && p.y >= 0f && p.y <= 1f;

        Vector2 ToGui(Vector2 screen) => new Vector2(screen.x * _guiWidth, (1f - screen.y) * GameHud.Height);

        bool OverPicture(Vector2 screen)
        {
            var g = ToGui(screen);
            return !_leftBar.Contains(g) && !_rightBar.Contains(g) && !_topBar.Contains(g);
        }

        Button ButtonAt(Vector2 screen)
        {
            var g = ToGui(screen);
            foreach (var b in _buttons)
                if (b.Rect.Contains(g)) return b;
            return null;
        }

        // ---------------------------------------------------------------- actions

        void Press(Button b)
        {
            switch (b.Kind)
            {
                case ButtonKind.Tool: SelectTool((PaintTool)b.Index); break;
                case ButtonKind.Color: SelectColor(b.Index); break;
                case ButtonKind.Undo: Undo(); break;
                case ButtonKind.Size:
                    SizeIndex = (SizeIndex + 1) % Sizes.Length;
                    GameAudio.Sfx("paint_pick", 1f, 0.9f + 0.15f * SizeIndex);
                    break;
                case ButtonKind.Symmetry:
                    Mirror = !Mirror;
                    GameAudio.Sfx("paint_pick", 1f, Mirror ? 1.2f : 0.9f);
                    break;
                case ButtonKind.Page: NextPage(); break;
                case ButtonKind.Hand: SwapHands(); break;
                case ButtonKind.Clear: Clear(); break;
                case ButtonKind.Mode: SetMode(CurrentMode == Mode.Paper ? Mode.Camera : Mode.Paper); break;
                case ButtonKind.Photo: SavePhoto(); break;
            }
        }

        /// <summary>Left-handed players: the left hand paints and the right hand is the switch (and back).</summary>
        public void SwapHands()
        {
            LiftAll();
            RightHanded = !RightHanded;
            PlayerPrefs.SetInt(RightHandedKey, RightHanded ? 1 : 0);
            PlayerPrefs.Save();
            SwitchOn = false;
            ResetHandTracks();
            GameAudio.Sfx("paint_pick", 1f, RightHanded ? 1.1f : 0.9f);
            ShowToast(PaintStrings.HandSwapped(RightHanded));
            Debug.Log($"[Paint] {(RightHanded ? "Right" : "Left")} hand paints");
        }

        public void SelectTool(PaintTool tool)
        {
            if (tool == PaintTool.Stamp && Tool == PaintTool.Stamp)   // pressing Stamps again: the next stamp
                StampShape = StampShapes[(Array.IndexOf(StampShapes, StampShape) + 1) % StampShapes.Length];
            Tool = tool;
            GameAudio.Sfx("paint_pick");
            Debug.Log($"[Paint] Tool {Tool}{(Tool == PaintTool.Stamp ? " " + StampShape : "")}");
        }

        public void SelectColor(int index)
        {
            ColorIndex = Mathf.Clamp(index, 0, Palette.Length - 1);
            if (Tool == PaintTool.Eraser) Tool = PaintTool.Marker;   // picking a colour means "paint with it"
            GameAudio.Sfx("paint_pick", 1f, 0.85f + 0.05f * ColorIndex);
            Debug.Log($"[Paint] Colour {ColorIndex}");
        }

        public void Undo()
        {
            LiftAll();
            if (Canvas.Undo()) GameAudio.Sfx("paint_undo");
            else ShowToast(PaintStrings.NothingToUndo);
        }

        public void Clear()
        {
            LiftAll();
            Canvas.PushUndo();
            Canvas.Clear();
            GameAudio.Sfx("paint_clear");
            _flash = 0.6f;
            _flashColor = CurrentMode == Mode.Paper ? paperColor : Color.white;
            ShowToast(PaintStrings.Cleared);
        }

        void NextPage()
        {
            if (CurrentMode != Mode.Paper || _pages.Length == 0) return;
            LiftAll();
            PageIndex = PageIndex + 1 >= _pages.Length ? -1 : PageIndex + 1;
            Canvas.PushUndo();
            Canvas.Clear();
            ApplyMode();
            GameAudio.Sfx("paint_clear", 0.6f, 1.2f);
            Debug.Log($"[Paint] Page {(PageIndex < 0 ? "blank" : _pages[PageIndex].name)}");
        }

        public void SetMode(Mode mode)
        {
            if (mode == CurrentMode) return;
            LiftAll();
            CurrentMode = mode;
            if (mode == Mode.Camera)
            {
                _cameraCanvas ??= NewCanvas();
                if (_feed == null) _feed = CameraFeed.Request();
                _cropValid = false;
                _introUntil = Time.unscaledTime + 6f;
            }
            else if (_feed != null)
            {
                CameraFeed.Release();
                _feed = null;
            }
            ResetHandTracks();
            ApplyMode();
            GameAudio.Sfx("ui_select", vary: 0f);
            Debug.Log($"[Paint] Mode {mode}");
        }

        /// <summary>The cursors start fresh: their samples were for the other hand or in the other picture.</summary>
        void ResetHandTracks()
        {
            foreach (var hand in new[] { _left, _right })
            {
                hand.Track.Reset();
                hand.CameraCursor.Reset();
            }
        }

        void ApplyMode()
        {
            _canvasMat.SetTexture("_BaseMap", Canvas.Shown);
            bool page = CurrentMode == Mode.Paper && PageIndex >= 0 && PageIndex < _pages.Length;
            _lineQuad.gameObject.SetActive(page);
            if (page) _lineMat.SetTexture("_BaseMap", _pages[PageIndex]);
            if (CurrentMode == Mode.Paper)
            {
                _bg.SetTexture("_BaseMap", Texture2D.whiteTexture);
                _bg.SetTextureScale("_BaseMap", Vector2.one);
                _bg.SetTextureOffset("_BaseMap", Vector2.zero);
                _bg.SetColor("_BaseColor", paperColor);
            }
        }

        bool[] Walls(int page)
        {
            if (page < 0 || page >= _pages.Length) return null;
            if (_walls.TryGetValue(page, out var walls)) return walls;
            try
            {
                var tex = _pages[page];
                var src = tex.GetPixels32();
                int sw = tex.width, sh = tex.height;
                walls = new bool[canvasWidth * canvasHeight];
                for (int y = 0; y < canvasHeight; y++)
                {
                    int sy = Mathf.Min(sh - 1, (int)((y + 0.5f) * sh / canvasHeight));
                    for (int x = 0; x < canvasWidth; x++)
                    {
                        int sx = Mathf.Min(sw - 1, (int)((x + 0.5f) * sw / canvasWidth));
                        walls[y * canvasWidth + x] = src[sy * sw + sx].a > 100;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Paint] Page {page} isn't readable, the bucket ignores its lines: {e.Message}");
                walls = null;
            }
            _walls[page] = walls;
            return walls;
        }

        /// <summary>Saves the picture (paper or camera picture + painting + page lines) as a PNG on the box only.</summary>
        public void SavePhoto()
        {
            LiftAll();
            const int w = 1920, h = 1080;
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var cmd = new CommandBuffer { name = "Paint photo" };
            cmd.SetRenderTarget(rt);
            cmd.ClearRenderTarget(false, true, Color.black);
            cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(-0.5f, 0.5f, -0.5f, 0.5f, -1f, 1f));
            cmd.DrawMesh(_quad, Matrix4x4.identity, _bg, 0, 0);
            cmd.DrawMesh(_quad, Matrix4x4.identity, _canvasMat, 0, 0);
            if (_lineQuad.gameObject.activeSelf) cmd.DrawMesh(_quad, Matrix4x4.identity, _lineMat, 0, 0);
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Release();

            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            var active = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(rt);
            byte[] png = tex.EncodeToPNG();
            Destroy(tex);

            try
            {
                Directory.CreateDirectory(PhotoDir);
                LastPhoto = Path.Combine(PhotoDir, $"tundu-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
                File.WriteAllBytes(LastPhoto, png);
                var all = Directory.GetFiles(PhotoDir, "*.png").OrderBy(f => f).ToList();
                for (int i = 0; i < all.Count - keepPhotos; i++) File.Delete(all[i]);
                _photoCount = Mathf.Min(all.Count, keepPhotos);
                ShowToast(PaintStrings.PhotoSaved(_photoCount));
                Debug.Log($"[Paint] Photo saved: {LastPhoto}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Paint] Photo not saved: {e.Message}");
            }
            GameAudio.Sfx("paint_photo", vary: 0f);
            _flash = 1f;
            _flashColor = Color.white;
        }

        void ShowToast(string text)
        {
            _toast = text;
            _toastUntil = Time.unscaledTime + 2.5f;
        }

        void HandleKeys()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.zKey.wasPressedThisFrame) Undo();
                if (kb.cKey.wasPressedThisFrame) Clear();
                if (kb.mKey.wasPressedThisFrame) Press(new Button { Kind = ButtonKind.Symmetry });
                if (kb.pKey.wasPressedThisFrame) NextPage();
                if (kb.kKey.wasPressedThisFrame) SetMode(CurrentMode == Mode.Paper ? Mode.Camera : Mode.Paper);
                if (kb.fKey.wasPressedThisFrame) SavePhoto();
                if (kb.sKey.wasPressedThisFrame) Press(new Button { Kind = ButtonKind.Size });
                if (kb.hKey.wasPressedThisFrame) SwapHands();
                for (int i = 0; i < ToolCount; i++)
                    if (kb[Key.Digit1 + i].wasPressedThisFrame) SelectTool((PaintTool)i);
            }
            // TV remote: ← → colour, ↑ ↓ brush, OK = photo
            if (RemoteInput.Left) SelectColor((ColorIndex + Palette.Length - 1) % Palette.Length);
            if (RemoteInput.Right) SelectColor((ColorIndex + 1) % Palette.Length);
            if (RemoteInput.Up) SelectTool((PaintTool)(((int)Tool + ToolCount - 1) % ToolCount));
            if (RemoteInput.Down) SelectTool((PaintTool)(((int)Tool + 1) % ToolCount));
            if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) SavePhoto();
        }

        // ---------------------------------------------------------------- camera picture

        void UpdateCameraPicture(Body body, float dt)
        {
            bool live = _feed != null && _feed.HasFrame;
            float imageAspect = body != null ? body.Aspect : live ? _feed.Aspect : 16f / 9f;
            float screenAspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            Rect target;
            if (body != null && body.ShouldersVisible && body.HipsVisible)
            {
                float u = body.TorsoLength;
                _cropUnit = _cropUnit <= 0f ? u : Mathf.Lerp(_cropUnit, u, Mathf.Clamp01(dt * 2f));
                float bottom = body.HipCenter.y - cameraBelowHips * _cropUnit;
                float top = body.ShoulderCenter.y + cameraAboveShoulders * _cropUnit;
                target = Fit(body.ShoulderCenter.x, (top + bottom) * 0.5f, top - bottom, imageAspect, screenAspect);
            }
            else target = Fit(imageAspect * 0.5f, 0.5f, 1f, imageAspect, screenAspect);

            bool painting = _left.Pen.State == PenState.Painting || _right.Pen.State == PenState.Painting;
            if (!_cropValid)
            {
                _crop = target;
                _cropValid = true;
            }
            else if (!painting)   // hold the framing still while painting, so strokes stay where they were put
            {
                float k = 1f - Mathf.Exp(-dt / 0.6f);
                bool clearMove = Vector2.Distance(target.center, _crop.center) > 0.04f * _crop.height ||
                                 Mathf.Abs(target.height / _crop.height - 1f) > 0.05f;
                if (clearMove)
                    _crop = new Rect(Vector2.Lerp(_crop.position, target.position, k), Vector2.Lerp(_crop.size, target.size, k));
            }

            if (live)
            {
                // raw camera image -> mirrored view: u = 1 - x / aspect, v = y
                _bg.SetTexture("_BaseMap", _feed.Texture);
                _bg.SetTextureScale("_BaseMap", new Vector2(-_crop.width / imageAspect, _crop.height));
                _bg.SetTextureOffset("_BaseMap", new Vector2(1f - _crop.xMin / imageAspect, _crop.yMin));
                _bg.SetColor("_BaseColor", Color.white);
            }
            else
            {
                _bg.SetTexture("_BaseMap", Texture2D.whiteTexture);
                _bg.SetColor("_BaseColor", Brand.Current.BackgroundColor);
            }
        }

        /// <summary>A screen-shaped window on the camera image around (cx, cy), <paramref name="height"/> tall if it fits.</summary>
        static Rect Fit(float cx, float cy, float height, float imageAspect, float screenAspect)
        {
            float h = Mathf.Min(height, 1f), w = h * screenAspect;
            if (w > imageAspect)
            {
                w = imageAspect;
                h = w / screenAspect;
            }
            float x = Mathf.Clamp(cx - w * 0.5f, 0f, imageAspect - w);
            float y = Mathf.Clamp(cy - h * 0.5f, 0f, 1f - h);
            return new Rect(x, y, w, h);
        }

        // ---------------------------------------------------------------- scene objects

        static Mesh MakeQuadMesh()
        {
            var mesh = new Mesh { name = "Paint quad" };
            mesh.SetVertices(new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f) });
            mesh.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            mesh.SetColors(new[] { Color.white, Color.white, Color.white, Color.white });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            return mesh;
        }

        Transform CreateQuad(string name, Material material, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = _quad;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.sortingOrder = order;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        void FitQuads()
        {
            float h = cam.orthographicSize * 2f, w = h * cam.aspect;
            var center = new Vector3(cam.transform.position.x, cam.transform.position.y, 0f);
            foreach (var q in new[] { _bgQuad, _canvasQuad, _lineQuad })
            {
                q.position = center;
                q.localScale = new Vector3(w, h, 1f);
            }
        }

        // ---------------------------------------------------------------- layout

        void Layout()
        {
            if (_buttons.Count > 0 && Mathf.Approximately(_lastLayoutWidth, _guiWidth) && _laidOutFor == CurrentMode) return;
            _lastLayoutWidth = _guiWidth;
            _laidOutFor = CurrentMode;
            _buttons.Clear();
            float w = _guiWidth;
            // Paper: the hands' zone is the whole screen. Camera: the picture is framed on the player, so their reach is
            // a band around the middle (reachHalfWidth torso lengths out of the framed height).
            float reach = reachHalfWidth / (cameraBelowHips + 1f + cameraAboveShoulders) * GameHud.Height;
            float left = CurrentMode == Mode.Camera ? Mathf.Max(0f, w * 0.5f - reach) : 0f;
            float right = CurrentMode == Mode.Camera ? Mathf.Min(w, w * 0.5f + reach) : w;

            _leftBar = new Rect(left + 10f, 140f, 132f, 926f);
            for (int i = 0; i < ToolCount; i++)
                _buttons.Add(new Button { Kind = ButtonKind.Tool, Index = i, Rect = new Rect(left + 24f, 152f + i * 113f, 104f, 104f), Repeat = i == (int)PaintTool.Stamp });

            _rightBar = new Rect(right - 142f, 140f, 132f, 926f);
            for (int i = 0; i < Palette.Length; i++)
                _buttons.Add(new Button { Kind = ButtonKind.Color, Index = i, Rect = new Rect(right - 128f, 150f + i * 82.5f, 104f, 80f) });

            var top = new List<(ButtonKind kind, bool big, bool repeat)>
            {
                (ButtonKind.Undo, false, true), (ButtonKind.Size, false, true), (ButtonKind.Symmetry, false, false),
            };
            if (CurrentMode == Mode.Paper && _pages != null && _pages.Length > 0) top.Add((ButtonKind.Page, false, true));
            top.Add((ButtonKind.Hand, false, false));
            top.Add((ButtonKind.Clear, true, false));
            top.Add((ButtonKind.Mode, true, false));
            top.Add((ButtonKind.Photo, true, false));
            float bw = CurrentMode == Mode.Camera ? 92f : 118f, gap = CurrentMode == Mode.Camera ? 8f : 10f;
            float total = top.Count * bw + (top.Count - 1) * gap;
            float x0 = w * 0.5f - total * 0.5f;
            for (int i = 0; i < top.Count; i++)
                _buttons.Add(new Button { Kind = top[i].kind, Big = top[i].big, Repeat = top[i].repeat, Rect = new Rect(x0 + i * (bw + gap), 14f, bw, 112f) });
            _topBar = new Rect(x0 - 10f, 4f, total + 20f, 130f);
        }

        float _lastLayoutWidth;

        // ---------------------------------------------------------------- sound

        void UpdateSound(float dt)
        {
            float target = 0f;
            var brush = BrushHand;
            if (brush.Pen.State == PenState.Painting && brush.Stroke.Active && Tool != PaintTool.Stamp)
                target = Mathf.Clamp(brush.Speed / 1.2f, 0.15f, 1f);
            if (_hands != null && _hands.UsingMouse && brush.Pen.State == PenState.Painting) target = 0.6f;
            _brushLevel = Mathf.MoveTowards(_brushLevel, target, dt * 4f);
            _brushLoop.volume = _brushLevel * GameAudio.SfxVolume * 0.45f;
            _brushLoop.pitch = 0.85f + 0.35f * _brushLevel;
        }

        // ---------------------------------------------------------------- drawing

        void OnGUI()
        {
            float w = GameHud.Begin();
            EnsureStyles();
            var brand = Brand.Current;

            if (CurrentMode == Mode.Camera && (_feed == null || !_feed.IsLive))
                GameHud.Text(new Rect(0, 470, w, 80), PaintStrings.WaitingCamera, GameHud.Medium);

            var panel = brand.SurfaceColor;
            panel.a = CurrentMode == Mode.Camera ? 0.6f : 0.92f;
            Panel(_leftBar, panel);
            Panel(_rightBar, panel);
            Panel(_topBar, panel);
            foreach (var b in _buttons) DrawButton(b, brand);

            DrawSwitch(FreeHand);
            DrawCursor(BrushHand);

            if (Time.unscaledTime < _introUntil) DrawIntro(w);
            if (Time.unscaledTime < _toastUntil)
            {
                var r = new Rect(w * 0.5f - 520f, 940f, 1040f, 84f);
                Panel(r, new Color(0f, 0f, 0f, 0.6f));
                GameHud.Text(r, _toast, GameHud.Small);
            }
            else DrawStatus(w);
            if (_flash > 0f) GameHud.Box(new Rect(0, 0, w, GameHud.Height), _flashColor.WithAlpha(Mathf.Clamp01(_flash) * 0.85f));
        }

        void DrawButton(Button b, Brand brand)
        {
            bool selected = b.Kind switch
            {
                ButtonKind.Tool => (int)Tool == b.Index,
                ButtonKind.Color => ColorIndex == b.Index,
                ButtonKind.Symmetry => Mirror,
                ButtonKind.Page => PageIndex >= 0,
                _ => false,
            };
            var r = b.Rect;
            float progress = Math.Max(Progress(_left, b), Progress(_right, b));

            if (b.Kind == ButtonKind.Color)
            {
                float d = selected ? 74f : 58f;
                var c = new Rect(r.center.x - d / 2f, r.center.y - d / 2f, d, d);
                if (selected) Icon(new Rect(c.x - 7f, c.y - 7f, d + 14f, d + 14f), PaintArt.Disc, Color.white);
                Icon(c, PaintArt.Disc, Palette[b.Index]);
                if (progress > 0f) Icon(new Rect(r.center.x - 14f, r.center.y - 14f, 28f, 28f), PaintArt.Disc, Color.white.WithAlpha(progress));
                return;
            }

            Panel(r, selected ? brand.PrimaryColor : new Color(1f, 1f, 1f, 0.1f));
            if (progress > 0f) GameHud.Box(new Rect(r.x + 6f, r.yMax - 6f - (r.height - 12f) * progress, r.width - 12f, (r.height - 12f) * progress), new Color(1f, 1f, 1f, 0.35f));
            var color = Palette[ColorIndex];
            var ink = selected ? brand.BackgroundColor : Color.white;
            var iconRect = b.Kind == ButtonKind.Tool ? new Rect(r.x + 14f, r.y + 14f, r.width - 28f, r.height - 28f) : new Rect(r.center.x - 32f, r.y + 8f, 64f, 64f);
            switch (b.Kind)
            {
                case ButtonKind.Tool:
                    switch ((PaintTool)b.Index)
                    {
                        case PaintTool.Marker: Icon(Shrink(iconRect, 0.62f), PaintArt.Disc, color); break;
                        case PaintTool.Rainbow: Icon(iconRect, PaintArt.IconTexture(PaintArt.Icon.Rainbow), Color.white); break;
                        case PaintTool.Spray: Icon(iconRect, PaintArt.IconTexture(PaintArt.Icon.Spray), color); break;
                        case PaintTool.Neon: Icon(iconRect, PaintArt.IconTexture(PaintArt.Icon.Neon), color); break;
                        case PaintTool.Glitter: Icon(iconRect, PaintArt.IconTexture(PaintArt.Icon.Glitter), Color.Lerp(color, Color.white, 0.5f)); break;
                        case PaintTool.Stamp:
                            GUI.color = color;
                            GUI.DrawTextureWithTexCoords(Shrink(iconRect, 0.85f), PaintArt.BrushAtlas, PaintArt.ShapeUv(StampShape));
                            GUI.color = Color.white;
                            break;
                        case PaintTool.Eraser: Icon(iconRect, PaintArt.IconTexture(PaintArt.Icon.Eraser), Color.white); break;
                        case PaintTool.Fill:
                            Icon(iconRect, PaintArt.IconTexture(PaintArt.Icon.Fill), Color.white);
                            Icon(new Rect(iconRect.x + 6f, iconRect.yMax - 30f, 26f, 26f), PaintArt.Disc, color);
                            break;
                    }
                    return;
                case ButtonKind.Size:
                {
                    float d = 18f + 16f * SizeIndex;
                    Icon(new Rect(r.center.x - d / 2f, r.y + 40f - d / 2f, d, d), PaintArt.Disc, ink);
                    Label(r, SizeNames[SizeIndex], ink);
                    return;
                }
                case ButtonKind.Undo: Icon(iconRect, PaintArt.IconTexture(PaintArt.Icon.Undo), ink); Label(r, PaintStrings.Undo, ink); return;
                case ButtonKind.Symmetry: Icon(iconRect, PaintArt.IconTexture(PaintArt.Icon.Symmetry), Color.white); Label(r, PaintStrings.Symmetry, ink); return;
                case ButtonKind.Hand:
                    // the painting hand; the icon is a right hand, mirrored for the left
                    GUI.color = ink;
                    GUI.DrawTextureWithTexCoords(iconRect, PaintArt.IconTexture(PaintArt.Icon.Hand),
                        RightHanded ? new Rect(0f, 0f, 1f, 1f) : new Rect(1f, 0f, -1f, 1f));
                    GUI.color = Color.white;
                    Label(r, PaintStrings.HandButton(RightHanded), ink);
                    return;
                case ButtonKind.Page:
                    Icon(iconRect, PaintArt.IconTexture(PaintArt.Icon.Page), Color.white);
                    Label(r, PageIndex < 0 ? PaintStrings.Page : $"{PaintStrings.Page} {PageIndex + 1}/{_pages.Length}", ink);
                    return;
                case ButtonKind.Clear: Icon(iconRect, PaintArt.IconTexture(PaintArt.Icon.Clear), ink); Label(r, PaintStrings.Clear, ink); return;
                case ButtonKind.Mode:
                    bool toCamera = CurrentMode == Mode.Paper;
                    Icon(iconRect, PaintArt.IconTexture(toCamera ? PaintArt.Icon.Camera : PaintArt.Icon.Paper), ink);
                    Label(r, toCamera ? PaintStrings.CameraMode : PaintStrings.PaperMode, ink);
                    return;
                case ButtonKind.Photo: Icon(iconRect, PaintArt.IconTexture(PaintArt.Icon.Photo), Color.white); Label(r, PaintStrings.Photo, ink); return;
            }
        }

        float Progress(Hand hand, Button b) => hand.Hover == b ? DwellProgress(hand) : 0f;

        /// <summary>The free hand: a "switch" — a hollow ring when down, a full paint blob when raised (painting).</summary>
        void DrawSwitch(Hand hand)
        {
            if (!hand.CursorVisible) return;
            var g = ToGui(hand.Cursor);
            if (hand.Hover != null || !OverPicture(hand.Cursor))
            {
                Icon(Around(g, 54f), PaintArt.Ring, hand.Tint);
                float p = DwellProgress(hand);
                if (p > 0f) Icon(Around(g, 44f * p), PaintArt.Disc, hand.Tint.WithAlpha(0.8f));
                return;
            }
            if (SwitchOn)
            {
                float pulse = 1f + 0.08f * Mathf.Sin(Time.unscaledTime * 10f);
                Icon(Around(g, 70f * pulse), PaintArt.Disc, Palette[ColorIndex]);
                Icon(Around(g, 78f * pulse), PaintArt.Ring, Color.white);
            }
            else
            {
                Icon(Around(g, 64f), PaintArt.Ring, hand.Tint.WithAlpha(0.7f));
                Icon(Around(g, 14f), PaintArt.Disc, hand.Tint.WithAlpha(0.7f));
            }
        }

        void DrawCursor(Hand hand)
        {
            var pen = hand.Pen;
            if (!hand.CursorVisible || !pen.Visible) return;
            var g = ToGui(hand.Cursor);
            float scale = GameHud.Height / canvasHeight;
            float brush = Mathf.Max(18f, Sizes[SizeIndex] * scale * (Tool == PaintTool.Eraser ? 2f : 1f));
            var color = Tool == PaintTool.Eraser ? Color.white : Palette[ColorIndex];
            if (hand.Hover != null || !OverPicture(pen.Position))
            {
                Icon(Around(g, 54f), PaintArt.Ring, hand.Tint);
                float p = DwellProgress(hand);
                if (p > 0f) Icon(Around(g, 44f * p), PaintArt.Disc, hand.Tint.WithAlpha(0.8f));
                return;
            }
            if (pen.State == PenState.Painting)
            {
                Icon(Around(g, brush), PaintArt.Disc, color.WithAlpha(0.85f));
                Icon(Around(g, brush + 14f), PaintArt.Ring, hand.Tint);
            }
            else
            {
                // brush up: a hollow brush the size of the stroke, with the colour in the middle
                Icon(Around(g, brush + 14f), PaintArt.Ring, hand.Tint);
                Icon(Around(g, Mathf.Min(brush, 22f)), PaintArt.Disc, color.WithAlpha(0.9f));
            }
        }

        void DrawIntro(float w)
        {
            var r = new Rect(w * 0.5f - 640f, 300f, 1280f, 380f);
            Panel(r, new Color(0.08f, 0.1f, 0.22f, 0.88f));
            bool camera = CurrentMode == Mode.Camera;
            GameHud.Text(new Rect(r.x, r.y + 20f, r.width, 110f), camera ? PaintStrings.CameraTitle : PaintStrings.Title,
                GameHud.Big, TextAnchor.MiddleCenter, Brand.Current.PrimaryColor);
            GameHud.Text(new Rect(r.x + 40f, r.y + 140f, r.width - 80f, 70f), camera ? PaintStrings.CameraHowTo : PaintStrings.PaintWith(RightHanded), GameHud.Small);
            GameHud.Text(new Rect(r.x + 40f, r.y + 210f, r.width - 80f, 70f), PaintStrings.SwitchHowTo(RightHanded), GameHud.Small);
            GameHud.Text(new Rect(r.x + 40f, r.y + 280f, r.width - 80f, 70f), camera ? PaintStrings.PaintWith(RightHanded) : PaintStrings.HowTo3, GameHud.Small);
        }

        void DrawStatus(float w)
        {
            var motion = MotionSystem.Instance;
            var brush = BrushHand;
            string status = motion == null || !motion.IsConnected ? $"{PaintStrings.MouseHelp}   ·   {PaintStrings.KeyboardHelp}"
                : motion.ActiveBody == null ? PaintStrings.StepIn
                : StrokeCount < 3 && !SwitchOn && brush.CursorVisible && OverPicture(brush.Cursor) ? PaintStrings.RaiseToPaint(RightHanded)
                : null;
            if (status == null) return;
            var r = new Rect(w * 0.5f - 760f, 990f, 1520f, 64f);
            Panel(r, new Color(0f, 0f, 0f, 0.45f));
            GameHud.Text(r, status, _label);
        }

        static Rect Around(Vector2 c, float size) => new Rect(c.x - size / 2f, c.y - size / 2f, size, size);

        static Rect Shrink(Rect r, float k) =>
            new Rect(r.center.x - r.width * k / 2f, r.center.y - r.height * k / 2f, r.width * k, r.height * k);

        static void Icon(Rect r, Texture tex, Color tint)
        {
            var old = GUI.color;
            GUI.color = tint;
            GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit, true);
            GUI.color = old;
        }

        void Label(Rect button, string text, Color color)
        {
            _label.normal.textColor = color;
            _label.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(button.x - 10f, button.yMax - 40f, button.width + 20f, 36f), text, _label);
        }

        void Panel(Rect r, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            GUI.Box(r, GUIContent.none, _round);
            GUI.color = old;
        }

        void EnsureStyles()
        {
            if (_round != null) return;
            _round = new GUIStyle { normal = { background = PaintArt.Rounded }, border = new RectOffset(20, 20, 20, 20) };
            _label = new GUIStyle(GameHud.Small) { fontSize = 22, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Overflow };
        }

        static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
    }
}
