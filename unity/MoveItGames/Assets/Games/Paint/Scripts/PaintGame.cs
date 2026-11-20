using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MoveIt.Motion;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using Joint = MoveIt.Motion.Joint;

namespace MoveIt.Games.Paint
{
    /// <summary>
    /// "Pikturo!": paint on the TV with your hands. Reworked on 2026-11-10 so that a child can draw without being
    /// taught (the old screen had every tool, but a child couldn't get a line onto the paper):
    ///
    /// <b>Two ways to paint</b> (<see cref="PaintWay"/>, OK on the remote changes it, remembered). <i>Easy</i>, the
    /// default: raise a hand and it paints, lower it and it stops; either hand, both at once if you like. <i>Two hands</i>
    /// (how Pikturo worked before): one hand paints while the other is raised to the chest. The rules are in
    /// <see cref="PaintPen"/>.
    ///
    /// <b>The paper is within reach.</b> A hand's place on the paper is its place around the body, the same number of
    /// pixels per torso length both ways (<see cref="paperScale"/>): a circle in the air is a circle on the paper (the
    /// old screen stretched the hands' zone over the whole 16:9 screen, 1.44 times wider than tall), the paper's edges
    /// are an easy reach away, and the brushes (left), colours (right) and buttons (top) sit just beyond them, bigger
    /// than before. In camera mode the same layout hangs on the player's picture.
    ///
    /// <b>Colouring pages colour themselves in.</b> On a page the marker and the rainbow stay inside the lines
    /// (<see cref="PageRegions"/>), an area that is mostly coloured fills up, the bucket fills the area a hand rests on,
    /// and when every area is done the picture is finished (stars, a jingle). Each sheet keeps its painting while you
    /// look at the others.
    ///
    /// The brush cursor is drawn about one camera frame in the past, on a parabola fitted through the last quarter
    /// second of raw camera samples (<see cref="HandInterpolator.FitWindow"/>), then through a string stabilizer
    /// (<see cref="CursorRope"/>): the brush the user preferred on 2026-10-20.
    ///
    /// Tools from the classic painting games: colour pots, brush sizes, marker / rainbow / spray / neon / glitter
    /// brushes, stamps (Mario Paint, Kid Pix), eraser, paint bucket, undo, clear, left-right symmetry, colouring pages
    /// (PNGs in Resources/Paint/Pages, tools/paint/pages.py), a dark sheet for glowing colours, and saving a photo.
    ///
    /// <b>Camera mode</b> ("Pikturo mbi veten!"): the camera picture is the paper, so children can paint a crown,
    /// a moustache or wings on themselves. The picture is framed on the player, so the hands line up with their image.
    /// While nobody paints, the framing follows the player, and the painting moves with it.
    /// </summary>
    public sealed class PaintGame : MonoBehaviour
    {
        public enum Mode { Paper, Camera }

        enum ButtonKind { Tool, Color, Undo, Clear, Page, Size, Symmetry, Mode, Photo, Hand }

        sealed class Button
        {
            public ButtonKind Kind;
            public int Index;
            public Rect Rect;
            /// <summary>Seconds a hand must rest on it.</summary>
            public float Hold;
            /// <summary>Keeps pressing while held (cycle buttons, undo).</summary>
            public bool Repeat;
        }

        sealed class Hand
        {
            public readonly PaintPen Pen = new PaintPen();
            public BrushStroke Stroke;
            public Joint Shoulder, Elbow, Wrist;
            public Color Tint;
            public int Owner;
            /// <summary>
            /// The cursor: a parabola through the last 0.25 s of raw samples, one camera frame behind, never past the
            /// newest sample. Positions are HUD pixels divided by the frame's scale (torso lengths on the screen), so the
            /// filter's speed term means the same on the paper and on the camera picture (4 per zone width before = 1.4
            /// per torso length).
            /// </summary>
            public readonly HandInterpolator Track = new HandInterpolator(2f, 1.4f, 2f) { Margin = 1f, FitWindow = 0.25f };
            /// <summary>Between the cursor and what it paints or points at: 6 px of string (1080 px tall screen).</summary>
            public readonly CursorRope Rope = new CursorRope(6f);
            /// <summary>Seen by the camera, through single unsure frames.</summary>
            public readonly HandPresence Presence = new HandPresence();
            /// <summary>Where this hand's cursor is this frame (HUD pixels), how fast it moves (HUD pixels a second,
            /// y down), and whether it is shown.</summary>
            public Vector2 Cursor, Velocity;
            public bool HasCursor, CursorVisible;
            public Button Hover;
            public float Dwell;
            public bool Pressed;
            /// <summary>While painting: the cursor's speeds, oldest first (finds the arm-drop at the end of a stroke).</summary>
            public readonly List<PenTrace> History = new List<PenTrace>();
            /// <summary>The easy way: the brush left the paper a moment ago. Was it leaving (wipe its tail) or will it be
            /// right back (the same stroke)? Decided when we know.</summary>
            public bool TailPending, TailDown;
            public float TailAt;
            /// <summary>The bucket: the area under the hand, how long it has rested there, the last area it filled.</summary>
            public int FillUnder = -1, FillDone = -1;
            public float FillLinger;
        }

        struct Pop
        {
            public Vector2 At;
            public float Start;
            public Color Color;
        }

        [Header("Scene")]
        [SerializeField] Camera cam;
        [SerializeField, Tooltip("Tundu/Fx: paper colour or the camera picture.")] Material backgroundMaterial;
        [SerializeField, Tooltip("Tundu/Fx, premultiplied (One, OneMinusSrcAlpha): the painting.")] Material canvasMaterial;
        [SerializeField, Tooltip("Tundu/Fx, alpha blended: colouring page lines.")] Material lineArtMaterial;
        [SerializeField, Tooltip("Tundu/Brush: stamps into the painting.")] Material brushMaterial;

        [Header("The paper (HUD pixels of a 1080 px tall screen)")]
        [SerializeField, Tooltip("HUD pixels per torso length: a hand's place on the paper is its place around the body. " +
                                 "Bigger = less arm movement, more of the camera's wobble.")]
        float paperScale = 480f;
        [SerializeField, Tooltip("Where the player's shoulders sit on the screen. The paper reaches from 0.87 torso lengths " +
                                 "above them (over the head) to 0.78 below (above the navel: hands resting at the belly " +
                                 "or on the hips are under the paper, they don't paint).")]
        float paperShoulderY = 578f;
        [SerializeField] int paperWidth = 1056;
        [SerializeField] int paperHeight = 792;
        [SerializeField] Color paperColor = new Color(1f, 0.973f, 0.933f);
        [SerializeField, Tooltip("The dark sheet (neon, glitter and white look best on it).")]
        Color darkPaperColor = new Color(0.03f, 0.035f, 0.085f);

        [Header("The camera picture")]
        [SerializeField] int cameraCanvasWidth = 1280;
        [SerializeField] int cameraCanvasHeight = 720;
        [SerializeField, Tooltip("The picture shows from this far below the hips (torso lengths)...")]
        float frameBelowHips = 0.75f;
        [SerializeField, Tooltip("...to this far above the shoulders (room for a crown).")]
        float frameAboveShoulders = 1.15f;

        [Header("Hands")]
        [SerializeField, Tooltip("Seconds a hand must rest on a colour or a brush to pick it.")]
        float quickHoldSeconds = 0.4f;
        [SerializeField, Tooltip("...on undo, pages, size.")]
        float holdSeconds = 0.6f;
        [SerializeField, Tooltip("...on buttons that change a lot (clear, camera, photo).")]
        float bigHoldSeconds = 1f;
        [SerializeField, Tooltip("Cycle buttons (stamps, pages, undo) press again after this much longer.")]
        float repeatSeconds = 1f;
        [SerializeField, Tooltip("A button's time only runs while the hand is slower than this (torso lengths a second).")]
        float holdStillSpeed = 1.6f;
        [SerializeField, Tooltip("An arm within this many degrees of hanging straight down is resting (brush up).")]
        float restAngle = 35f;

        [Header("The end of a stroke")]
        [SerializeField, Tooltip("When the brush lifts because the hand left, the fast movement just before (torso lengths " +
                                 "a second) was the arm leaving, not painting: its wet paint is wiped.")]
        float leavingSpeed = 2f;
        [SerializeField, Tooltip("The easy way: ...and so was the movement down, at this speed or faster, before the arm came to rest.")]
        float droppingSpeed = 0.5f;
        [SerializeField, Tooltip("The easy way: off the paper for this long (or on a button) = it left. Sooner back = the same stroke.")]
        float leftAfterSeconds = 0.35f;
        [SerializeField, Tooltip("Paint stays wet (can still be wiped) this long.")]
        float wetSeconds = 0.5f;
        [SerializeField, Tooltip("...the easy way: longer, a child's arm sinks slowly.")]
        float easyWetSeconds = 1.2f;

        [Header("Two hands: the other hand is the switch")]
        [SerializeField, Tooltip("The switch turns on when the free hand's wrist is this high above the hips " +
                                 "(torso lengths: 0 = hips, 1 = shoulders): lifted to the chest.")]
        float switchOn = 0.6f;
        [SerializeField, Tooltip("...and off below this height. Well below the elbow's height (0.36): the camera sometimes " +
                                 "pulls a wrist held in front of the chest down onto its elbow for a few frames.")]
        float switchOff = 0.2f;

        [Header("Colouring pages")]
        [SerializeField, Tooltip("The bucket fills the area a hand has rested on for this long.")]
        float fillRestSeconds = 0.35f;
        [SerializeField, Tooltip("A brush may stray this far past a line (torso lengths) and still colour the area it was in: " +
                                 "a hand in the air wobbles by 0.03-0.05, a scribble overshoots more.")]
        float lineSlack = 0.12f;

        [Header("Photos")]
        [SerializeField, Tooltip("Newest photos kept on the box (older ones are deleted).")]
        int keepPhotos = 60;

        public static readonly Color[] Palette =
        {
            Hex("#FF5A4E"), Hex("#FF9F1C"), Hex("#FFC93C"), Hex("#9BE15D"), Hex("#2FA84F"), Hex("#31C6F4"),
            Hex("#2F6BFF"), Hex("#7B61FF"), Hex("#FF7EC8"), Hex("#8B5A2B"), Hex("#1E1E24"), Hex("#FFFFFF"),
        };
        /// <summary>Brush diameters in HUD pixels at 480 px per torso length (they scale with the frame).</summary>
        static readonly float[] Sizes = { 18f, 36f, 64f };
        static readonly string[] SizeNames = { "S", "M", "L" };
        static readonly BrushShape[] StampShapes = { BrushShape.Star, BrushShape.Heart, BrushShape.Flower, BrushShape.Footprint, BrushShape.Bubble };
        const int ToolCount = 8;
        const float ReferenceScale = 480f;
        /// <summary>The paper's top edge, torso lengths above the shoulders (it is 1.65 tall).</summary>
        const float PaperAbove = 0.87f;
        /// <summary>The middle of the brush and colour grids, torso lengths below the shoulders (just above them: level
        /// with the paper's middle, and the bottom pots clear of the skeleton mirror in the screen's corner).</summary>
        const float GridMiddle = -0.02f;
        /// <summary>OK held this long on the remote changes the way to paint.</summary>
        const float WayHoldSeconds = 1f;

        public Mode CurrentMode { get; private set; } = Mode.Paper;
        /// <summary>How the brush goes down: the easy way or with two hands. Remembered on the box.</summary>
        public PaintWay Way { get; private set; } = PaintWay.Easy;
        public int ColorIndex { get; private set; }
        public PaintTool Tool { get; private set; } = PaintTool.Marker;
        public int SizeIndex { get; private set; } = 1;
        public bool Mirror { get; private set; }
        public BrushShape StampShape { get; private set; } = BrushShape.Star;
        /// <summary>Colouring page shown, -1 = a blank sheet.</summary>
        public int PageIndex => _sheet >= 1 && _sheet <= PageCount ? _sheet - 1 : -1;
        /// <summary>The dark blank sheet (after the pages).</summary>
        public bool DarkPaper => _sheet == PageCount + 1;
        public int PageCount => _pages?.Length ?? 0;
        public PaintCanvas Canvas => CurrentMode == Mode.Camera ? _cameraCanvas : _paperCanvas;
        /// <summary>The colouring page's areas (null on a blank sheet and in camera mode).</summary>
        public PageRegions Regions => Canvas?.Regions;
        public PaintPen LeftPen => _left.Pen;
        public PaintPen RightPen => _right.Pen;
        /// <summary>Two hands: which hand paints (the other one is the switch). Remembered on the box.</summary>
        public bool RightHanded { get; private set; } = true;
        /// <summary>Two hands: the free hand is raised, the brush paints.</summary>
        public bool SwitchOn { get; private set; }
        /// <summary>Strokes started since the game opened (the hints go away after a few).</summary>
        public int StrokeCount { get; private set; }
        /// <summary>Colouring pages finished since the game opened.</summary>
        public int PicturesFinished { get; private set; }
        /// <summary>Where the paper is on the screen (HUD pixels of a 1080 px tall screen); in camera mode the whole screen.</summary>
        public Rect PaperRect => _paper;
        /// <summary>Camera mode: the part of the camera picture on screen, in view space (Body coordinates).</summary>
        public Rect CameraCrop => _crop;
        public CameraFeed Feed => _feed;
        public string LastPhoto { get; private set; }

        PaintCanvas _paperCanvas, _cameraCanvas;
        Material _bg, _canvasMat, _lineMat, _brush, _shadowMat;
        Mesh _quad;
        Transform _bgQuad, _canvasQuad, _lineQuad, _shadowQuad;
        Texture2D[] _pages;
        Task<PageRegions>[] _pageRegions;
        RenderTexture[] _parked;
        int _sheet;
        bool _celebrated;
        BodyHands _bodyHands;
        CameraFeed _feed;
        readonly Hand _left = new Hand { Owner = 0, Stroke = new BrushStroke(11, 0), Shoulder = Joint.LeftShoulder, Elbow = Joint.LeftElbow, Wrist = Joint.LeftWrist };
        readonly Hand _right = new Hand { Owner = 1, Stroke = new BrushStroke(17, 1), Shoulder = Joint.RightShoulder, Elbow = Joint.RightElbow, Wrist = Joint.RightWrist };
        Hand[] _hands;
        readonly List<Button> _buttons = new List<Button>();
        readonly List<Pop> _pops = new List<Pop>();
        Rect _paper, _toolBar, _colorBar, _topBar;
        float _guiWidth = 1920f;
        // the frame: where the player's shoulders are on the screen and how many HUD pixels a torso length is
        Vector2 _frameHud;
        float _scale = ReferenceScale, _unit;
        float _laidX, _laidY, _laidScale = -1f;
        Mode _laidMode;
        PaintWay _laidWay;
        Rect _crop;
        Vector2 _frameCentre;
        bool _cropValid, _framedSomeone;
        float _cropUnit;
        string _toast;
        float _toastUntil, _flash, _introUntil, _stampSoundAt, _celebrateUntil, _lastPaintAt;
        Color _flashColor = Color.white;
        AudioSource _brushLoop;
        float _brushLevel;
        MotionDebugOverlay _mirror;
        bool _mirrorWasVisible;
        float _mirrorSize;
        GUIStyle _round, _label, _caption;
        int _photoCount;
        double _lastBodySample;
        const string RightHandedKey = "paint.rightHanded", WayKey = "paint.way";

        Hand BrushHand => RightHanded ? _right : _left;
        Hand FreeHand => RightHanded ? _left : _right;
        Hand Other(Hand hand) => hand == _left ? _right : _left;

        string PhotoDir => Path.Combine(Application.persistentDataPath, "Photos");

        /// <summary>Canvas pixels per HUD pixel.</summary>
        float CanvasPerHud => CurrentMode == Mode.Paper ? Canvas.Width / Mathf.Max(1f, _paper.width) : Canvas.Width / Mathf.Max(1f, _guiWidth);
        /// <summary>The brush's diameter on the screen (HUD pixels): the same share of the player's reach in both modes.</summary>
        float BrushHudSize => Sizes[SizeIndex] * _scale / ReferenceScale;

        BrushSettings Settings => new BrushSettings
        {
            Tool = Tool, Color = Palette[ColorIndex], Size = BrushHudSize * CanvasPerHud, StampShape = StampShape, Mirror = Mirror,
            Regions = Canvas.Regions, LineSlack = lineSlack * _scale * CanvasPerHud,
        };

        // ---------------------------------------------------------------- lifecycle

        void Start()
        {
            _hands = new[] { _right, _left };
            _bodyHands = BodyHands.Instance != null ? BodyHands.Instance : FindAnyObjectByType<BodyHands>();
            // The skeleton mirror lives on the app-wide motion rig: small in the corner beside the paper (a child sees
            // that the TV sees them), hidden in camera mode (the picture shows them), and given back to the next scene.
            _mirror = MotionSystem.Instance != null ? MotionSystem.Instance.GetComponent<MotionDebugOverlay>() : null;
            if (_mirror != null)
            {
                _mirrorWasVisible = _mirror.Visible;
                _mirrorSize = _mirror.CornerSize;
            }

            _bg = new Material(backgroundMaterial);
            _canvasMat = new Material(canvasMaterial);
            _lineMat = new Material(lineArtMaterial);
            _brush = new Material(brushMaterial);
            _shadowMat = new Material(lineArtMaterial);
            _shadowMat.SetTexture("_BaseMap", Texture2D.whiteTexture);
            _shadowMat.SetColor("_BaseColor", new Color(0f, 0f, 0f, 0.3f));
            _quad = MakeQuadMesh();
            _shadowQuad = CreateQuad("Paper shadow", _shadowMat, -1);
            _bgQuad = CreateQuad("Background", _bg, 0);
            _canvasQuad = CreateQuad("Painting", _canvasMat, 1);
            _lineQuad = CreateQuad("Colouring page", _lineMat, 2);

            _paperCanvas = new PaintCanvas(paperWidth, paperHeight, _brush);
            _pages = Resources.LoadAll<Texture2D>("Paint/Pages").OrderBy(t => t.name).ToArray();
            _parked = new RenderTexture[_pages.Length + 2];
            FindPageRegions();
            _left.Tint = Brand.Current.PlayerColor(1);
            _right.Tint = Brand.Current.PlayerColor(0);
            _photoCount = Directory.Exists(PhotoDir) ? Directory.GetFiles(PhotoDir, "*.png").Length : 0;
            RightHanded = PlayerPrefs.GetInt(RightHandedKey, 1) == 1;
            Way = PlayerPrefs.GetInt(WayKey, 0) == 1 ? PaintWay.TwoHands : PaintWay.Easy;

            _brushLoop = gameObject.AddComponent<AudioSource>();
            _brushLoop.clip = Resources.Load<AudioClip>("Sfx/paint_brush");
            _brushLoop.loop = true;
            _brushLoop.volume = 0f;
            if (_brushLoop.clip != null) _brushLoop.Play();

            _guiWidth = Screen.width / (Screen.height / GameHud.Height);
            _frameHud = new Vector2(_guiWidth * 0.5f, paperShoulderY);
            _scale = paperScale;
            ApplyWay();
            ApplyMode();
            Layout();
            _introUntil = Time.unscaledTime + 9f;
            GameAudio.PlayMusic("paint");
            Debug.Log($"[Paint] Ready: {_pages.Length} colouring pages, paper {paperWidth}x{paperHeight}, {(Way == PaintWay.Easy ? "the easy way" : "two hands")}");
        }

        void OnDestroy()
        {
            if (_feed != null) CameraFeed.Release();
            _feed = null;
            if (_mirror != null)
            {
                _mirror.Visible = _mirrorWasVisible;
                _mirror.CornerSize = _mirrorSize;
            }
            _paperCanvas?.Dispose();
            _cameraCanvas?.Dispose();
            if (_parked != null)
                foreach (var rt in _parked)
                    if (rt != null)
                    {
                        rt.Release();
                        Destroy(rt);
                    }
            foreach (var m in new[] { _bg, _canvasMat, _lineMat, _brush, _shadowMat })
                if (m != null) Destroy(m);
            if (_quad != null) Destroy(_quad);
        }

        void Update()
        {
            if (GameNavigation.HandleCommonKeys()) return;
            float dt = Time.deltaTime;
            _guiWidth = Screen.width / (Screen.height / GameHud.Height);
            var body = MotionSystem.Instance != null ? MotionSystem.Instance.ActiveBody : null;
            UpdateFrame(body, dt);
            Layout();
            FitQuads();
            HandleKeys();

            bool mouse = _bodyHands != null && _bodyHands.UsingMouse;
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
                foreach (var hand in _hands) UpdateHand(hand, body, dt);
            }
            Canvas.Dry();
            Canvas.Flush();
            Canvas.Present();
            _canvasMat.SetTexture("_BaseMap", Canvas.Shown);
            UpdateSound(dt);
            _flash = Mathf.Max(0f, _flash - dt * 2.5f);
        }

        // ---------------------------------------------------------------- the frame: where a hand is on the screen

        /// <summary>
        /// Paper: the player's shoulders are at a fixed spot of the screen and a torso length is <see cref="paperScale"/>
        /// pixels, wherever they stand. Camera: the picture's framing on the player decides both.
        /// </summary>
        void UpdateFrame(Body body, float dt)
        {
            if (CurrentMode == Mode.Camera)
            {
                UpdateCameraPicture(body, dt);
                if (_framedSomeone && _crop.height > 0f && _cropUnit > 0f)
                {
                    _scale = GameHud.Height * _cropUnit / _crop.height;
                    _frameHud = ViewToHud(_frameCentre);
                }
                else   // nobody has stood in the picture yet: the buttons where a player in the middle would reach them
                {
                    _scale = GameHud.Height / (frameBelowHips + 1f + frameAboveShoulders);
                    _frameHud = new Vector2(_guiWidth * 0.5f, frameAboveShoulders * _scale);
                }
                return;
            }
            _scale = paperScale;
            _frameHud = new Vector2(_guiWidth * 0.5f, paperShoulderY);
        }

        /// <summary>Camera mode: a point of the camera picture (view space) on the screen.</summary>
        Vector2 ViewToHud(Vector2 view) =>
            new Vector2((view.x - _crop.xMin) / _crop.width * _guiWidth, (1f - (view.y - _crop.yMin) / _crop.height) * GameHud.Height);

        /// <summary>A hand's point (<see cref="Body.HandPoint"/>: the wrist carried on along the arm from the steady
        /// shoulder, like BodyHands) on the screen, on a track of the body (Raw, Fast). Until 2026-11-17 the wrist was
        /// pushed along the forearm, which brought in the elbow's errors: on the box's recordings the brush jumped a fifth
        /// to a third more (tools/motion/hand_replay.py), with the same reach.</summary>
        Vector2 HandHud(Body body, Hand hand, Vector3[] track)
        {
            Vector2 point = body.HandPoint(track, hand.Shoulder, hand.Wrist);
            if (CurrentMode == Mode.Camera) return ViewToHud(point);
            // (the paper hangs on the smooth body: shoulder jitter would otherwise shake both cursors)
            Vector2 shoulders = body.ShoulderCenter;
            return new Vector2(_frameHud.x + (point.x - shoulders.x) / _unit * _scale, _frameHud.y - (point.y - shoulders.y) / _unit * _scale);
        }

        // ---------------------------------------------------------------- hands

        /// <summary>
        /// A new camera sample: where are the hands, and (two hands) is the switch up? Position and switch go into the
        /// brush's track together, so a stroke follows the switch at exactly the right moments.
        /// </summary>
        void OnBodySample(Body body)
        {
            double now = Time.unscaledTimeAsDouble;
            float torso = body.TorsoLength;
            if (torso >= 0.02f) _unit = _unit <= 0f ? torso : Mathf.Lerp(_unit, torso, 0.1f);
            bool framed = CurrentMode == Mode.Camera ? _crop.height > 0f && _cropUnit > 0f : _unit >= 0.02f;

            bool rightSeen = _right.Presence.Sample(body.Visibility[(int)_right.Wrist], now);
            bool leftSeen = _left.Presence.Sample(body.Visibility[(int)_left.Wrist], now);
            if (Way == PaintWay.TwoHands)
            {
                var free = FreeHand;
                bool freeSeen = free == _left ? leftSeen : rightSeen;
                bool freeOnToolbar = free.CursorVisible && OverBars(free.Cursor);   // it's pressing a button, not switching
                if (freeOnToolbar || !free.Presence.Tracked(now) || body.FastTorsoLength <= 0.02f) SwitchOn = false;
                else if (freeSeen)
                    SwitchOn = PaintPen.SwitchState(SwitchOn, (body.Fast[(int)free.Wrist].y - body.FastHipCenter.y) / body.FastTorsoLength,
                                                    switchOn, switchOff);
                // (else a frame the camera AI is unsure about: the switch stays as it was)
            }
            else SwitchOn = false;
            if (!framed) return;
            if (rightSeen) _right.Track.Sample(body.SampleTime, HandHud(body, _right, body.Raw) / _scale, SwitchOn, now);
            if (leftSeen) _left.Track.Sample(body.SampleTime, HandHud(body, _left, body.Raw) / _scale, SwitchOn, now);
        }

        void UpdateHand(Hand hand, Body body, float dt)
        {
            double now = Time.unscaledTimeAsDouble;
            bool tracked = body != null && hand.Presence.Tracked(now) && hand.Track.HasSample && _scale > 1f;
            Vector2 pos = hand.Cursor;
            bool switchAt = false;
            if (!tracked)
            {
                hand.Track.Reset();
                hand.Rope.Reset();
                hand.Velocity = Vector2.zero;
                hand.HasCursor = false;
            }
            else
            {
                pos = hand.Rope.Follow((Vector2)hand.Track.Evaluate(now, dt, out switchAt) * _scale);
                hand.Velocity = hand.HasCursor && dt > 0f
                    ? Vector2.Lerp(hand.Velocity, (pos - hand.Cursor) / dt, 1f - Mathf.Exp(-dt / 0.05f))
                    : Vector2.zero;
                hand.HasCursor = true;
            }
            bool onScreen = tracked && pos.x >= 0f && pos.x <= _guiWidth && pos.y >= 0f && pos.y <= GameHud.Height;
            bool resting = tracked && IsResting(body, hand);
            int change;
            if (Way == PaintWay.Easy) change = hand.Pen.UpdateEasy(now, onScreen, pos, InsidePaper(pos), resting, _scale);
            else if (hand == BrushHand) change = hand.Pen.UpdateSwitch(tracked, pos, onScreen, InsidePaper(pos) >= 0f, resting, switchAt);
            else change = hand.Pen.UpdatePointer(onScreen && !resting, pos);   // the switch hand points, it never paints
            hand.Cursor = pos;
            hand.CursorVisible = hand.Pen.Visible;
            Dwell(hand, hand.CursorVisible ? ButtonAt(pos, hand.Hover) : null, dt);
            Apply(hand, change, pos, dt);
        }

        /// <summary>Development without a camera: the mouse is the brush, its button puts it down.</summary>
        void UpdateMouse(float dt)
        {
            var hand = _right;
            var other = _left;
            if (other.TailPending) SettleTail(other);
            other.CursorVisible = false;
            Dwell(other, null, dt);
            _left.Rope.Reset();    // the camera's cursors start fresh after the mouse
            _right.Rope.Reset();
            var zone = _bodyHands.Right.Position;
            var pos = new Vector2(zone.x * _guiWidth, (1f - zone.y) * GameHud.Height);
            bool onScreen = zone.x >= 0f && zone.x <= 1f && zone.y >= 0f && zone.y <= 1f;
            var mouse = Mouse.current;
            bool held = mouse != null && mouse.leftButton.isPressed;
            hand.Velocity = hand.HasCursor && dt > 0f ? Vector2.Lerp(hand.Velocity, (pos - hand.Cursor) / dt, 0.5f) : Vector2.zero;
            hand.HasCursor = true;
            int change = hand.Pen.UpdateMouse(pos, onScreen, onScreen && InsidePaper(pos) >= 0f, held);
            hand.Cursor = pos;
            hand.CursorVisible = onScreen;
            Apply(hand, change, pos, dt);
            var button = onScreen ? ButtonAt(pos, null) : null;
            if (button != null && mouse != null && mouse.leftButton.wasPressedThisFrame) Press(button);
            hand.Hover = button;
            hand.Dwell = 0f;
        }

        /// <summary>Starts, continues or ends this hand's stroke from the pen's state.</summary>
        void Apply(Hand hand, int change, Vector2 hud, float dt)
        {
            float now = Time.unscaledTime;
            var canvasPos = HudToCanvas(hud);
            bool resumed = false;
            if (hand.TailPending)
            {
                if (change > 0 && hand.Pen.LastArrival == PenArrival.Back)
                {
                    hand.TailPending = false;   // off the edge and right back: the same stroke, nothing to wipe
                    resumed = true;
                }
                else if (change > 0 || now - hand.TailAt > leftAfterSeconds || hand.Hover != null || hand.Pen.State == PenState.Away)
                    SettleTail(hand);           // it left for good (a button, the arm dropped, or it stayed away)
            }

            if (change > 0)
            {
                _introUntil = Mathf.Min(_introUntil, now + 0.5f);
                if (!resumed)
                {
                    if (Other(hand).Pen.State != PenState.Painting) PushUndo();   // both hands painting together = one undo step
                    hand.History.Clear();
                    StrokeCount++;
                }
                if (Tool == PaintTool.Fill)
                {
                    hand.FillUnder = hand.FillDone = -1;
                    hand.FillLinger = 0f;
                    if (!resumed) FillAt(hand, canvasPos);
                }
                else
                {
                    hand.Stroke.Begin(Canvas, Settings, canvasPos);
                    if (!resumed)
                    {
                        GameAudio.Sfx("paint_down", 0.5f);
                        Debug.Log($"[Paint] Stroke starts at {canvasPos} ({Tool}, colour {ColorIndex}, {(hand == _left ? "left" : "right")} hand" +
                                  (Way == PaintWay.Easy ? $", arrived by {hand.Pen.LastArrival})" : ")"));
                    }
                }
            }
            else if (change < 0 && hand.Stroke.Active)
            {
                var why = hand.Pen.LastLift;
                if (why == PenLift.Switch)
                {
                    // a deliberate stop (the switch went down, the mouse button up): the stroke ends as it is
                    hand.Stroke.End(Canvas, Settings);
                    Debug.Log($"[Paint] Stroke ends: {hand.Stroke.Distance:0} px");
                }
                else
                {
                    hand.Stroke.Abort();
                    hand.TailAt = now;
                    hand.TailDown = Way == PaintWay.Easy && (why != PenLift.OffPaper || hud.y > _paper.yMax - 0.05f * _scale);
                    if (Way == PaintWay.Easy && why == PenLift.OffPaper) hand.TailPending = true;   // back in a moment, or gone?
                    else SettleTail(hand);
                }
                Sparkle(hand);
            }

            if (hand.Pen.State != PenState.Painting) return;
            _lastPaintAt = now;
            if (Tool == PaintTool.Fill)
            {
                FillWhereItRests(hand, canvasPos, dt);
                return;
            }
            if (!hand.Stroke.Active) return;
            hand.History.Add(new PenTrace { Time = now, Speed = hand.Velocity.magnitude, Down = hand.Velocity.y });
            if (hand.History.Count > 240) hand.History.RemoveAt(0);
            int stamps = hand.Stroke.StampsPlaced;
            hand.Stroke.MoveTo(Canvas, Settings, canvasPos, dt);
            if (hand.Stroke.StampsPlaced > stamps && now > _stampSoundAt)
            {
                _stampSoundAt = now + 0.12f;
                GameAudio.Sfx("paint_stamp", 0.7f, 1f + 0.08f * (hand.Stroke.StampsPlaced % 5), 0.03f);
            }
            Sparkle(hand);
        }

        /// <summary>
        /// The brush lifted because the hand left (it dropped, went for a button, was lost): that last movement wasn't
        /// painting. Wipes the still-wet paint from where the hand got fast (or, the easy way, started down).
        /// </summary>
        void SettleTail(Hand hand)
        {
            hand.TailPending = false;
            bool down = hand.TailDown || (Way == PaintWay.Easy && hand.Pen.State == PenState.Away);
            float since = PaintPen.LeavingSince(hand.History, hand.TailAt, leavingSpeed * _scale, droppingSpeed * _scale, down);
            int wiped = float.IsPositiveInfinity(since) ? 0 : Canvas.Wipe(hand.Owner, since);
            Debug.Log($"[Paint] Stroke ends: {hand.Stroke.Distance:0} px, tail of {wiped} stamps wiped");
        }

        /// <summary>Areas of the colouring page a stroke just filled up: a sparkle on each, and is the picture finished?</summary>
        void Sparkle(Hand hand)
        {
            if (hand.Stroke.Completed.Count == 0) return;
            foreach (int region in hand.Stroke.Completed) AreaDone(region);
            hand.Stroke.Completed.Clear();
        }

        void AreaDone(int region)
        {
            var page = Canvas.Regions;
            if (page == null) return;
            _pops.Add(new Pop { At = CanvasToHud(page.Middle(region)), Start = Time.unscaledTime, Color = Palette[ColorIndex] });
            if (_pops.Count > 12) _pops.RemoveAt(0);
            GameAudio.Sfx("paint_fill", 0.9f, 1f + 0.05f * (page.CountedDone % 6));
            Debug.Log($"[Paint] Area {region} is done ({page.CountedDone} of {page.CountedTotal})");
            if (_celebrated || !page.Finished) return;
            _celebrated = true;
            PicturesFinished++;
            _celebrateUntil = Time.unscaledTime + 4f;
            GameAudio.Jingle("jingle_win");
            Debug.Log($"[Paint] Picture {PageIndex} is finished");
        }

        /// <summary>The bucket where the brush went down: the page's area under it (at once, no read-back), or on a blank
        /// sheet whatever is closed in by paint.</summary>
        void FillAt(Hand hand, Vector2 canvasPos)
        {
            var page = Canvas.Regions;
            int filled;
            if (page != null)
            {
                int region = page.At(canvasPos.x, canvasPos.y);
                Canvas.FillRegion(region, Palette[ColorIndex], behind: false);
                bool wasDone = page.Done(region);
                page.MarkDone(region);
                hand.FillDone = region;
                filled = page.Area(region);
                if (!wasDone) AreaDone(region);
            }
            else filled = Canvas.Fill(canvasPos, Palette[ColorIndex]);
            GameAudio.Sfx(filled > 0 ? "paint_fill" : "ui_back", filled > 0 ? 1f : 0.4f);
            Debug.Log($"[Paint] Fill at {canvasPos} with colour {ColorIndex}: {filled} pixels");
        }

        /// <summary>The bucket while the brush stays down on a colouring page: every area the hand rests on fills.</summary>
        void FillWhereItRests(Hand hand, Vector2 canvasPos, float dt)
        {
            var page = Canvas.Regions;
            if (page == null) return;
            int under = page.At(canvasPos.x, canvasPos.y);
            if (under != hand.FillUnder)
            {
                hand.FillUnder = under;
                hand.FillLinger = 0f;
                return;
            }
            hand.FillLinger += dt;
            if (hand.FillLinger < fillRestSeconds || under == hand.FillDone) return;
            PushUndo();
            FillAt(hand, canvasPos);
        }

        /// <summary>The bucket at a point of the sheet (canvas pixels, row 0 at the bottom), as one undo step.</summary>
        public void BucketAt(Vector2 canvasPoint)
        {
            LiftAll();
            PushUndo();
            FillAt(_right, canvasPoint);
        }

        /// <summary>Ends every stroke as it is and dries the paint (before a toolbar action changes the picture).</summary>
        void LiftAll()
        {
            if (_hands == null) return;
            foreach (var hand in _hands)
            {
                if (hand.TailPending) SettleTail(hand);
                hand.Pen.Lift();
                if (hand.Stroke.Active) hand.Stroke.End(Canvas, Settings);
                Sparkle(hand);
            }
            Canvas.DryAll();
            Canvas.Flush();
            _canvasMat.SetTexture("_BaseMap", Canvas.Shown);
        }

        void PushUndo()
        {
            Canvas.PushUndo();
            var page = Canvas.Regions;
            if (page == null) return;
            ref var slot = ref _pageUndo[_pageUndoHead];
            slot = page.Save(slot);
            _pageUndoHead = (_pageUndoHead + 1) % PaintCanvas.MaxUndo;
            _pageUndoCount = Mathf.Min(_pageUndoCount + 1, PaintCanvas.MaxUndo);
        }

        // what was coloured on the page at each undo step (kept in step with the canvas's own undo pictures)
        readonly PageRegions.Snapshot[] _pageUndo = new PageRegions.Snapshot[PaintCanvas.MaxUndo];
        int _pageUndoHead, _pageUndoCount;

        /// <summary>Holding a hand on a button presses it (a ring fills). The time only runs while the hand is nearly
        /// still: a hand on its way somewhere passes over buttons.</summary>
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
            if (hand.Velocity.magnitude < holdStillSpeed * _scale) hand.Dwell += dt;
            if (!hand.Pressed && hand.Dwell >= button.Hold)
            {
                hand.Pressed = true;
                Press(button);
            }
            else if (hand.Pressed && button.Repeat && hand.Dwell >= button.Hold + repeatSeconds)
            {
                hand.Dwell = button.Hold;
                Press(button);
            }
        }

        float DwellProgress(Hand hand) => hand.Hover == null || hand.Pressed ? 0f : Mathf.Clamp01(hand.Dwell / hand.Hover.Hold);

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

        /// <summary>How far inside the paper a point is (HUD pixels; negative = off it, or over a toolbar).</summary>
        float InsidePaper(Vector2 p)
        {
            float inside = Mathf.Min(Mathf.Min(p.x - _paper.xMin, _paper.xMax - p.x), Mathf.Min(p.y - _paper.yMin, _paper.yMax - p.y));
            if (CurrentMode != Mode.Camera) return inside;
            inside = Mathf.Min(inside, Outside(_toolBar, p));
            inside = Mathf.Min(inside, Outside(_colorBar, p));
            return Mathf.Min(inside, Outside(_topBar, p));
        }

        /// <summary>The distance from a rectangle: positive outside it, negative inside.</summary>
        static float Outside(Rect r, Vector2 p)
        {
            float dx = Mathf.Max(r.xMin - p.x, p.x - r.xMax), dy = Mathf.Max(r.yMin - p.y, p.y - r.yMax);
            if (dx <= 0f && dy <= 0f) return Mathf.Max(dx, dy);
            return new Vector2(Mathf.Max(dx, 0f), Mathf.Max(dy, 0f)).magnitude;
        }

        bool OverBars(Vector2 p) => _toolBar.Contains(p) || _colorBar.Contains(p) || _topBar.Contains(p);

        Vector2 HudToCanvas(Vector2 hud)
        {
            var c = Canvas;
            if (CurrentMode == Mode.Paper)
                return new Vector2((hud.x - _paper.x) / _paper.width * c.Width, (1f - (hud.y - _paper.y) / _paper.height) * c.Height);
            return new Vector2(hud.x / _guiWidth * c.Width, (1f - hud.y / GameHud.Height) * c.Height);
        }

        Vector2 CanvasToHud(Vector2 canvas)
        {
            var c = Canvas;
            if (CurrentMode == Mode.Paper)
                return new Vector2(_paper.x + canvas.x / c.Width * _paper.width, _paper.y + (1f - canvas.y / c.Height) * _paper.height);
            return new Vector2(canvas.x / c.Width * _guiWidth, (1f - canvas.y / c.Height) * GameHud.Height);
        }

        /// <summary>The button under a point: a little bigger than it looks, and bigger still for the one the hand is
        /// already on (it doesn't slip off with the hand's small wobbles).</summary>
        Button ButtonAt(Vector2 hud, Button current)
        {
            if (current != null && _buttons.Contains(current) && Grow(current.Rect, 0.045f * _scale).Contains(hud)) return current;
            Button best = null;
            float bestD = float.MaxValue;
            foreach (var b in _buttons)
            {
                if (!Grow(b.Rect, 0.012f * _scale).Contains(hud)) continue;
                float d = Vector2.Distance(hud, b.Rect.center);
                if (d < bestD)
                {
                    bestD = d;
                    best = b;
                }
            }
            return best;
        }

        static Rect Grow(Rect r, float by) => new Rect(r.x - by, r.y - by, r.width + 2f * by, r.height + 2f * by);

        // ---------------------------------------------------------------- actions

        void Press(Button b)
        {
            switch (b.Kind)
            {
                case ButtonKind.Tool: SelectTool((PaintTool)b.Index); break;
                case ButtonKind.Color: SelectColor(b.Index); break;
                case ButtonKind.Undo: Undo(); break;
                case ButtonKind.Size: NextSize(); break;
                case ButtonKind.Symmetry: ToggleSymmetry(); break;
                case ButtonKind.Page: NextPage(); break;
                case ButtonKind.Hand: SwapHands(); break;
                case ButtonKind.Clear: Clear(); break;
                case ButtonKind.Mode: SetMode(CurrentMode == Mode.Paper ? Mode.Camera : Mode.Paper); break;
                case ButtonKind.Photo: SavePhoto(); break;
            }
        }

        /// <summary>The easy way (raise a hand and it paints) or two hands (one paints while the other is raised).</summary>
        public void SetWay(PaintWay way)
        {
            if (way == Way) return;
            LiftAll();
            Way = way;
            PlayerPrefs.SetInt(WayKey, way == PaintWay.TwoHands ? 1 : 0);
            PlayerPrefs.Save();
            ApplyWay();
            GameAudio.Sfx("ui_select", vary: 0f);
            _introUntil = Time.unscaledTime + 7f;   // the card shows how this way works
            ShowToast(PaintStrings.WayChanged(way));
            Debug.Log($"[Paint] Way: {way}");
        }

        void ApplyWay()
        {
            SwitchOn = false;
            float wet = Way == PaintWay.Easy ? easyWetSeconds : wetSeconds;
            if (_paperCanvas != null) _paperCanvas.WetSeconds = wet;
            if (_cameraCanvas != null) _cameraCanvas.WetSeconds = wet;
            ResetHandTracks();
            _laidScale = -1f;   // (two hands has one more button)
        }

        /// <summary>Two hands, left-handed players: the left hand paints and the right hand is the switch (and back).</summary>
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
            GameAudio.Sfx("paint_pick", 1f, 0.85f + 0.04f * ColorIndex);
            Debug.Log($"[Paint] Colour {ColorIndex}");
        }

        public void NextSize()
        {
            SizeIndex = (SizeIndex + 1) % Sizes.Length;
            GameAudio.Sfx("paint_pick", 1f, 0.9f + 0.15f * SizeIndex);
        }

        public void ToggleSymmetry()
        {
            Mirror = !Mirror;
            GameAudio.Sfx("paint_pick", 1f, Mirror ? 1.2f : 0.9f);
        }

        public void Undo()
        {
            LiftAll();
            if (!Canvas.Undo())
            {
                ShowToast(PaintStrings.NothingToUndo);
                return;
            }
            var page = Canvas.Regions;
            if (page != null && _pageUndoCount > 0)
            {
                _pageUndoHead = (_pageUndoHead + PaintCanvas.MaxUndo - 1) % PaintCanvas.MaxUndo;
                _pageUndoCount--;
                page.Restore(_pageUndo[_pageUndoHead]);
                if (!page.Finished) _celebrated = false;
            }
            GameAudio.Sfx("paint_undo");
        }

        public void Clear()
        {
            LiftAll();
            PushUndo();
            Canvas.Clear();
            Canvas.Regions?.Clear();
            _celebrated = false;
            GameAudio.Sfx("paint_clear");
            _flash = 0.6f;
            _flashColor = CurrentMode == Mode.Paper ? (DarkPaper ? darkPaperColor : paperColor) : Color.white;
            ShowToast(PaintStrings.Cleared);
        }

        /// <summary>The next sheet: blank, each colouring page, the dark sheet, and round again. Every sheet keeps its
        /// painting while the others are shown.</summary>
        public void NextPage()
        {
            if (CurrentMode != Mode.Paper) return;
            SetSheet((_sheet + 1) % (PageCount + 2));
            GameAudio.Sfx("paint_clear", 0.6f, 1.2f);
        }

        /// <summary>Shows a colouring page (0..PageCount-1), or with -1 the blank sheet.</summary>
        public void ShowPage(int page)
        {
            if (CurrentMode != Mode.Paper) return;
            SetSheet(page >= 0 && page < PageCount ? page + 1 : 0);
        }

        void SetSheet(int sheet)
        {
            if (sheet == _sheet) return;
            LiftAll();
            // put the sheet's painting away (each sheet keeps its own), take out the next one's
            ref var parked = ref _parked[_sheet];
            if (parked == null)
            {
                parked = new RenderTexture(_paperCanvas.Texture.descriptor) { name = $"Paint sheet {_sheet}" };
                parked.Create();
            }
            Graphics.CopyTexture(_paperCanvas.Texture, parked);
            _sheet = sheet;
            _paperCanvas.Clear();
            if (_parked[sheet] != null) Graphics.CopyTexture(_parked[sheet], _paperCanvas.Texture);
            _paperCanvas.ForgetUndo();   // undo doesn't reach back onto another sheet
            _pageUndoCount = 0;
            int page = PageIndex;
            _paperCanvas.SetRegions(page >= 0 ? RegionsOf(page) : null);
            _celebrated = _paperCanvas.Regions != null && _paperCanvas.Regions.Finished;
            ApplyMode();
            Debug.Log($"[Paint] Sheet {(page >= 0 ? _pages[page].name : DarkPaper ? "dark" : "blank")}" +
                      (Regions != null ? $": {Regions.Count} areas, {Regions.CountedTotal} to colour" : ""));
        }

        /// <summary>A page's areas (waits for its worker if it is still at it); null when they couldn't be found.</summary>
        PageRegions RegionsOf(int page)
        {
            try
            {
                return _pageRegions[page]?.Result;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Paint] Page {page}: its areas couldn't be found, it colours like a blank sheet: {e.Message}");
                _pageRegions[page] = null;
                return null;
            }
        }

        /// <summary>
        /// Finds every page's areas on worker threads while the game starts (a page is ~0.8 million pixels to walk
        /// through). The lines are read here, on the main thread.
        /// </summary>
        void FindPageRegions()
        {
            _pageRegions = new Task<PageRegions>[_pages.Length];
            int width = paperWidth, height = paperHeight;
            for (int i = 0; i < _pages.Length; i++)
            {
                bool[] walls;
                try
                {
                    var tex = _pages[i];
                    var src = tex.GetPixels32();
                    int sw = tex.width, sh = tex.height;
                    walls = new bool[width * height];
                    for (int y = 0; y < height; y++)
                    {
                        int sy = Mathf.Min(sh - 1, (int)((y + 0.5f) * sh / height));
                        for (int x = 0; x < width; x++)
                        {
                            int sx = Mathf.Min(sw - 1, (int)((x + 0.5f) * sw / width));
                            walls[y * width + x] = src[sy * sw + sx].a > 100;
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Paint] Page {i} isn't readable, it colours like a blank sheet: {e.Message}");
                    continue;
                }
                _pageRegions[i] = Task.Run(() => PageRegions.FromWalls(walls, width, height));
            }
        }

        public void SetMode(Mode mode)
        {
            if (mode == CurrentMode) return;
            LiftAll();
            CurrentMode = mode;
            if (mode == Mode.Camera)
            {
                if (_cameraCanvas == null)
                    _cameraCanvas = new PaintCanvas(cameraCanvasWidth, cameraCanvasHeight, _brush)
                    {
                        WetSeconds = Way == PaintWay.Easy ? easyWetSeconds : wetSeconds,
                    };
                if (_feed == null) _feed = CameraFeed.Request();
                _cropValid = _framedSomeone = false;
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

        /// <summary>The cursors start fresh: their samples were for the other way, hand or picture.</summary>
        void ResetHandTracks()
        {
            foreach (var hand in new[] { _left, _right })
            {
                hand.Track.Reset();
                hand.Rope.Reset();
                hand.HasCursor = false;
                hand.TailPending = false;
            }
        }

        void ApplyMode()
        {
            _canvasMat.SetTexture("_BaseMap", Canvas.Shown);
            bool paper = CurrentMode == Mode.Paper;
            int page = PageIndex;
            _lineQuad.gameObject.SetActive(paper && page >= 0);
            if (paper && page >= 0) _lineMat.SetTexture("_BaseMap", _pages[page]);
            _shadowQuad.gameObject.SetActive(paper);
            if (paper)
            {
                _bg.SetTexture("_BaseMap", Texture2D.whiteTexture);
                _bg.SetTextureScale("_BaseMap", Vector2.one);
                _bg.SetTextureOffset("_BaseMap", Vector2.zero);
                _bg.SetColor("_BaseColor", DarkPaper ? darkPaperColor : paperColor);
            }
            if (cam != null) cam.backgroundColor = paper ? Brand.Current.BackgroundColor : Color.black;
            if (_mirror != null)
            {
                _mirror.Visible = paper && _mirrorWasVisible;
                _mirror.CornerSize = 0.16f;
            }
            _laidScale = -1f;
        }

        /// <summary>Saves the picture (the sheet, or the camera picture with the painting) as a PNG on the box only.</summary>
        public void SavePhoto()
        {
            LiftAll();
            bool paper = CurrentMode == Mode.Paper;
            int w = paper ? 1408 : 1920, h = paper ? 1056 : 1080;
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
                if (kb.mKey.wasPressedThisFrame) ToggleSymmetry();
                if (kb.pKey.wasPressedThisFrame) NextPage();
                if (kb.kKey.wasPressedThisFrame) SetMode(CurrentMode == Mode.Paper ? Mode.Camera : Mode.Paper);
                if (kb.fKey.wasPressedThisFrame) SavePhoto();
                if (kb.sKey.wasPressedThisFrame) NextSize();
                if (kb.hKey.wasPressedThisFrame) SwapHands();
                if (kb.tKey.wasPressedThisFrame) SetWay(Way == PaintWay.Easy ? PaintWay.TwoHands : PaintWay.Easy);
                for (int i = 0; i < ToolCount; i++)
                    if (kb[Key.Digit1 + i].wasPressedThisFrame) SelectTool((PaintTool)i);
            }
            // TV remote: ← → colour, ↑ ↓ brush; OK held for a second = the easy way / two hands (a grown-up's choice:
            // not a hand button, and not a short press a child makes by accident)
            if (RemoteInput.Left) SelectColor((ColorIndex + Palette.Length - 1) % Palette.Length);
            if (RemoteInput.Right) SelectColor((ColorIndex + 1) % Palette.Length);
            if (RemoteInput.Up) SelectTool((PaintTool)(((int)Tool + ToolCount - 1) % ToolCount));
            if (RemoteInput.Down) SelectTool((PaintTool)(((int)Tool + 1) % ToolCount));
            if (!RemoteInput.ConfirmHeld) _okHeld = 0f;
            else if (_okHeld >= 0f)
            {
                _okHeld += Time.unscaledDeltaTime;
                if (_okHeld >= WayHoldSeconds)
                {
                    _okHeld = -1f;   // once per press
                    SetWay(Way == PaintWay.Easy ? PaintWay.TwoHands : PaintWay.Easy);
                }
            }
        }

        float _okHeld;

        // ---------------------------------------------------------------- camera picture

        void UpdateCameraPicture(Body body, float dt)
        {
            bool live = _feed != null && _feed.HasFrame;
            float imageAspect = body != null ? body.Aspect : live ? _feed.Aspect : 16f / 9f;
            float screenAspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            Rect target;
            Vector2 centre;
            if (body != null && body.ShouldersVisible && body.HipsVisible)
            {
                float u = body.TorsoLength;
                _cropUnit = _cropUnit <= 0f ? u : Mathf.Lerp(_cropUnit, u, Mathf.Clamp01(dt * 2f));
                float bottom = body.HipCenter.y - frameBelowHips * _cropUnit;
                float top = body.ShoulderCenter.y + frameAboveShoulders * _cropUnit;
                target = Fit(body.ShoulderCenter.x, (top + bottom) * 0.5f, top - bottom, imageAspect, screenAspect);
                centre = body.ShoulderCenter;
                _framedSomeone = true;
            }
            else if (_cropValid && _framedSomeone)
            {
                // the player stepped out: the picture, the painting on it and the buttons stay as they were framed
                target = _crop;
                centre = _frameCentre;
            }
            else
            {
                target = Fit(imageAspect * 0.5f, 0.5f, 1f, imageAspect, screenAspect);
                centre = new Vector2(imageAspect * 0.5f, 0.62f);
            }

            bool painting = _hands.Any(h => h.Pen.State == PenState.Painting || h.TailPending);
            if (!_cropValid)
            {
                _crop = target;
                _frameCentre = centre;
                _cropValid = true;
            }
            else if (!painting)   // hold the framing still while painting, so strokes stay where they were put
            {
                float k = 1f - Mathf.Exp(-dt / 0.6f);
                bool clearMove = Vector2.Distance(target.center, _crop.center) > 0.04f * _crop.height ||
                                 Mathf.Abs(target.height / _crop.height - 1f) > 0.05f;
                if (clearMove)
                {
                    _crop = new Rect(Vector2.Lerp(_crop.position, target.position, k), Vector2.Lerp(_crop.size, target.size, k));
                    _frameCentre = Vector2.Lerp(_frameCentre, centre, k);
                }
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

        /// <summary>The sheet's quads (paper or camera picture, painting, page lines) where the layout puts the paper.</summary>
        void FitQuads()
        {
            float h = cam.orthographicSize * 2f, w = h * cam.aspect;
            Vector3 origin = cam.transform.position;
            void Place(Transform quad, Rect hud)
            {
                quad.position = new Vector3(origin.x + (hud.center.x / _guiWidth - 0.5f) * w, origin.y + (0.5f - hud.center.y / GameHud.Height) * h, 0f);
                quad.localScale = new Vector3(hud.width / _guiWidth * w, hud.height / GameHud.Height * h, 1f);
            }
            Place(_bgQuad, _paper);
            Place(_canvasQuad, _paper);
            Place(_lineQuad, _paper);
            Place(_shadowQuad, new Rect(_paper.x + 10f, _paper.y + 14f, _paper.width, _paper.height));
        }

        // ---------------------------------------------------------------- layout

        /// <summary>
        /// Everything sits where a hand reaches it, measured in torso lengths from the player's shoulders (x to the
        /// screen's right, y down): the paper from 1.1 to the left to 1.1 to the right and from 0.87 above to 0.78
        /// below; the brushes in two columns left of it (1.25 and 1.49 out), the colours in two columns right of it,
        /// the buttons in a row above (1.0 up: a hand well above the head, they are pressed rarely). The far corners of
        /// the paper and of the colour and brush grids are 1.1-1.2 torso lengths from the nearer shoulder; an arm
        /// reaches 1.37.
        /// </summary>
        void Layout()
        {
            float cx = _frameHud.x, sy = _frameHud.y, s = _scale;
            if (_laidScale > 0f && Mathf.Abs(cx - _laidX) < 1f && Mathf.Abs(sy - _laidY) < 1f && Mathf.Abs(s - _laidScale) < 0.5f &&
                _laidMode == CurrentMode && _laidWay == Way) return;
            _laidX = cx;
            _laidY = sy;
            _laidScale = s;
            _laidMode = CurrentMode;
            _laidWay = Way;
            // the buttons are made anew: a hand on one keeps it only by its kind
            var held = _hands?.Select(h => h.Hover).ToArray();
            _buttons.Clear();

            _paper = CurrentMode == Mode.Paper
                ? new Rect(cx - 1.1f * s, sy - PaperAbove * s, 2.2f * s, 1.65f * s)
                : new Rect(0f, 0f, _guiWidth, GameHud.Height);

            float tool = 0.225f * s;
            for (int i = 0; i < ToolCount; i++)
            {
                int column = i % 2, row = i / 2;   // the marker nearest the paper, the bucket at the bottom
                float x = cx - (1.25f + 0.24f * column) * s, y = sy + (GridMiddle + (row - 1.5f) * 0.27f) * s;
                _buttons.Add(new Button
                {
                    Kind = ButtonKind.Tool, Index = i, Rect = Centred(x, y, tool, tool), Hold = quickHoldSeconds, Repeat = i == (int)PaintTool.Stamp,
                });
            }
            _toolBar = Around(ButtonKind.Tool, 0.03f * s);

            float pot = 0.19f * s;
            for (int i = 0; i < Palette.Length; i++)
            {
                int column = i % 2, row = i / 2;
                float x = cx + (1.25f + 0.225f * column) * s, y = sy + (GridMiddle + (row - 2.5f) * 0.215f) * s;
                _buttons.Add(new Button { Kind = ButtonKind.Color, Index = i, Rect = Centred(x, y, pot, pot), Hold = quickHoldSeconds });
            }
            _colorBar = Around(ButtonKind.Color, 0.03f * s);

            var top = new List<(ButtonKind kind, float hold, bool repeat)> { (ButtonKind.Undo, holdSeconds, true), (ButtonKind.Clear, bigHoldSeconds, false) };
            if (CurrentMode == Mode.Paper && PageCount > 0) top.Add((ButtonKind.Page, holdSeconds + 0.1f, true));
            top.Add((ButtonKind.Size, quickHoldSeconds + 0.1f, true));
            top.Add((ButtonKind.Symmetry, holdSeconds, false));
            if (Way == PaintWay.TwoHands) top.Add((ButtonKind.Hand, holdSeconds, false));
            top.Add((ButtonKind.Mode, bigHoldSeconds, false));
            top.Add((ButtonKind.Photo, bigHoldSeconds, false));
            float bw = 0.235f * s, bh = 0.215f * s, pitch = 0.262f * s;
            float y0 = Mathf.Max(8f, sy - (PaperAbove + 0.03f) * s - bh);
            for (int i = 0; i < top.Count; i++)
                _buttons.Add(new Button
                {
                    Kind = top[i].kind, Hold = top[i].hold, Repeat = top[i].repeat,
                    Rect = new Rect(cx + (i - (top.Count - 1) * 0.5f) * pitch - bw * 0.5f, y0, bw, bh),
                });
            _topBar = new Rect(cx - top.Count * pitch * 0.5f - 0.01f * s, y0 - 0.02f * s, top.Count * pitch + 0.02f * s, bh + 0.04f * s);

            if (held == null) return;
            for (int i = 0; i < held.Length; i++)
                if (held[i] != null)
                    _hands[i].Hover = _buttons.FirstOrDefault(b => b.Kind == held[i].Kind && b.Index == held[i].Index);
        }

        static Rect Centred(float x, float y, float w, float h) => new Rect(x - w * 0.5f, y - h * 0.5f, w, h);

        /// <summary>The panel behind all buttons of a kind.</summary>
        Rect Around(ButtonKind kind, float pad)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var b in _buttons)
            {
                if (b.Kind != kind) continue;
                x0 = Mathf.Min(x0, b.Rect.xMin);
                y0 = Mathf.Min(y0, b.Rect.yMin);
                x1 = Mathf.Max(x1, b.Rect.xMax);
                y1 = Mathf.Max(y1, b.Rect.yMax);
            }
            return new Rect(x0 - pad, y0 - pad, x1 - x0 + 2f * pad, y1 - y0 + 2f * pad);
        }

        // ---------------------------------------------------------------- sound

        void UpdateSound(float dt)
        {
            float target = 0f;
            foreach (var hand in _hands)
            {
                if (hand.Pen.State != PenState.Painting || !hand.Stroke.Active || Tool == PaintTool.Stamp) continue;
                bool mouse = _bodyHands != null && _bodyHands.UsingMouse;
                target = Mathf.Max(target, mouse ? 0.6f : Mathf.Clamp(hand.Velocity.magnitude / (2.7f * _scale), 0.15f, 1f));
            }
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
            bool camera = CurrentMode == Mode.Camera;

            if (camera && (_feed == null || !_feed.IsLive))
                GameHud.Text(new Rect(0, 470, w, 80), PaintStrings.WaitingCamera, GameHud.Medium);

            var panel = brand.SurfaceColor;
            panel.a = camera ? 0.6f : 1f;
            Panel(_toolBar, panel);
            Panel(_colorBar, panel);
            Panel(_topBar, panel);
            foreach (var b in _buttons) DrawButton(b, brand);

            if (Mirror) DrawSymmetryAxis();
            DrawProgress(brand);
            DrawPops();
            foreach (var hand in _hands) DrawCursor(hand);

            float now = Time.unscaledTime;
            if (now < _celebrateUntil) DrawCelebration(w, brand);
            else if (now < _introUntil) DrawIntro(brand);
            if (now < _toastUntil)
            {
                var r = new Rect(w * 0.5f - 500f, 1014f, 1000f, 58f);
                Panel(r, new Color(0f, 0f, 0f, 0.7f));
                GameHud.Text(r, _toast, GameHud.Small);
            }
            else DrawStatus(w);
            DrawWay(brand);
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
            float k = _scale / ReferenceScale;

            if (b.Kind == ButtonKind.Color)
            {
                // a pot of paint: a darker rim, the paint, a shine; the chosen one bigger in a white ring
                float d = r.width * (selected ? 1f : 0.82f);
                var c = Around(r.center, d);
                var color = Palette[b.Index];
                if (selected) Icon(Around(r.center, d + 12f * k), PaintArt.Disc, Color.white);
                Icon(c, PaintArt.Disc, Color.Lerp(color, new Color(0.05f, 0.06f, 0.14f), 0.35f));
                Icon(Around(r.center, d - 12f * k), PaintArt.Disc, color);
                Icon(c, PaintArt.IconTexture(PaintArt.Icon.Shine), Color.white);
                if (progress > 0f) Icon(Around(r.center, d * 0.6f * progress), PaintArt.Disc, Color.white.WithAlpha(0.85f));
                return;
            }

            Panel(r, selected ? brand.PrimaryColor : new Color(1f, 1f, 1f, 0.12f));
            if (progress > 0f)
                GameHud.Box(new Rect(r.x + 6f, r.yMax - 6f - (r.height - 12f) * progress, r.width - 12f, (r.height - 12f) * progress), new Color(1f, 1f, 1f, 0.35f));
            var paint = Palette[ColorIndex];
            var ink = selected ? brand.BackgroundColor : Color.white;
            float iconSize = b.Kind == ButtonKind.Tool ? r.width * 0.74f : r.height * 0.56f;
            var iconRect = b.Kind == ButtonKind.Tool ? Around(r.center, iconSize) : new Rect(r.center.x - iconSize * 0.5f, r.y + r.height * 0.08f, iconSize, iconSize);
            switch (b.Kind)
            {
                case ButtonKind.Tool:
                    switch ((PaintTool)b.Index)
                    {
                        case PaintTool.Marker: Icon(iconRect, PaintArt.IconTexture(PaintArt.Icon.Brush), paint); break;
                        case PaintTool.Rainbow: Icon(iconRect, PaintArt.IconTexture(PaintArt.Icon.Rainbow), Color.white); break;
                        case PaintTool.Spray: Icon(iconRect, PaintArt.IconTexture(PaintArt.Icon.Spray), paint); break;
                        case PaintTool.Neon: Icon(iconRect, PaintArt.IconTexture(PaintArt.Icon.Neon), paint); break;
                        case PaintTool.Glitter: Icon(iconRect, PaintArt.IconTexture(PaintArt.Icon.Glitter), Color.Lerp(paint, Color.white, 0.5f)); break;
                        case PaintTool.Stamp:
                            GUI.color = paint;
                            GUI.DrawTextureWithTexCoords(Shrink(iconRect, 0.9f), PaintArt.BrushAtlas, PaintArt.ShapeUv(StampShape));
                            GUI.color = Color.white;
                            break;
                        case PaintTool.Eraser: Icon(iconRect, PaintArt.IconTexture(PaintArt.Icon.Eraser), Color.white); break;
                        case PaintTool.Fill:
                            Icon(iconRect, PaintArt.IconTexture(PaintArt.Icon.Fill), Color.white);
                            Icon(new Rect(iconRect.x + 4f * k, iconRect.yMax - 30f * k, 26f * k, 26f * k), PaintArt.Disc, paint);
                            break;
                    }
                    return;
                case ButtonKind.Size:
                {
                    float d = Mathf.Min(BrushHudSize, r.height * 0.5f);
                    Icon(Around(new Vector2(r.center.x, r.y + r.height * 0.36f), d), PaintArt.Disc, ink);
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
                    Label(r, PageIndex < 0 ? PaintStrings.Page : $"{PaintStrings.Page} {PageIndex + 1}/{PageCount}", ink);
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

        /// <summary>A hand's cursor: a ring that fills on buttons; on the paper a brush tip in the colour (hollow while the
        /// brush is up); two hands: the switch hand shows whether the brush is down.</summary>
        void DrawCursor(Hand hand)
        {
            var pen = hand.Pen;
            if (!hand.CursorVisible || !pen.Visible) return;
            var g = hand.Cursor;
            if (hand.Hover != null || InsidePaper(g) < 0f)
            {
                Icon(Around(g, 54f), PaintArt.Ring, hand.Tint);
                float p = DwellProgress(hand);
                if (p > 0f) Icon(Around(g, 44f * p), PaintArt.Disc, hand.Tint.WithAlpha(0.8f));
                return;
            }
            if (Way == PaintWay.TwoHands && hand != BrushHand)
            {
                if (SwitchOn)
                {
                    float pulse = 1f + 0.08f * Mathf.Sin(Time.unscaledTime * 10f);
                    Icon(Around(g, 56f * pulse), PaintArt.Disc, Palette[ColorIndex].WithAlpha(0.9f));
                    Icon(Around(g, 64f * pulse), PaintArt.Ring, Color.white);
                }
                else
                {
                    Icon(Around(g, 56f), PaintArt.Ring, hand.Tint.WithAlpha(0.7f));
                    Icon(Around(g, 12f), PaintArt.Disc, hand.Tint.WithAlpha(0.7f));
                }
                return;
            }
            float brush = Mathf.Max(18f, BrushHudSize * (Tool == PaintTool.Eraser ? 2f : 1f));
            var color = Tool == PaintTool.Eraser ? Color.white : Palette[ColorIndex];
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

        /// <summary>Symmetry on: a dotted line down the middle of the sheet (what is painted on one side appears on the other).</summary>
        void DrawSymmetryAxis()
        {
            float x = _paper.center.x;
            var c = Brand.Current.SecondaryColor.WithAlpha(0.55f);
            for (float y = _paper.yMin + 10f; y < _paper.yMax - 16f; y += 34f) GameHud.Box(new Rect(x - 2.5f, y, 5f, 18f), c);
        }

        /// <summary>A colouring page: how many of its areas are coloured (stars on a pill under the sheet's corner).</summary>
        void DrawProgress(Brand brand)
        {
            var page = Regions;
            if (page == null || page.CountedTotal == 0) return;
            int done = page.CountedDone, total = page.CountedTotal;
            var r = new Rect(_paper.xMin, _paper.yMax + 8f, 230f, 50f);
            Panel(r, brand.SurfaceColor);
            GUI.color = done == total ? brand.PrimaryColor : Color.white;
            GUI.DrawTextureWithTexCoords(new Rect(r.x + 10f, r.y + 4f, 42f, 42f), PaintArt.BrushAtlas, PaintArt.ShapeUv(BrushShape.Star));
            GUI.color = Color.white;
            _label.normal.textColor = Color.white;
            _label.alignment = TextAnchor.MiddleLeft;
            GUI.Label(new Rect(r.x + 62f, r.y, 80f, r.height), PaintStrings.Parts(done, total), _label);
            var bar = new Rect(r.x + 132f, r.y + 19f, 84f, 12f);
            GameHud.Box(bar, new Color(1f, 1f, 1f, 0.2f));
            GameHud.Box(new Rect(bar.x, bar.y, bar.width * done / total, bar.height), brand.LeafColor);
        }

        /// <summary>An area of the page filled up: a ring pops open there, with four little stars.</summary>
        void DrawPops()
        {
            float now = Time.unscaledTime;
            for (int i = _pops.Count - 1; i >= 0; i--)
            {
                float t = (now - _pops[i].Start) / 0.7f;
                if (t >= 1f)
                {
                    _pops.RemoveAt(i);
                    continue;
                }
                float ease = 1f - (1f - t) * (1f - t);
                Icon(Around(_pops[i].At, 40f + 150f * ease), PaintArt.Ring, Color.white.WithAlpha(1f - t));
                for (int s = 0; s < 4; s++)
                {
                    float a = s * Mathf.PI * 0.5f + 0.6f;
                    var p = _pops[i].At + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (30f + 80f * ease);
                    GUI.color = Brand.Current.PrimaryColor.WithAlpha(1f - t);
                    GUI.DrawTextureWithTexCoords(Around(p, 34f * (1f - 0.4f * t)), PaintArt.BrushAtlas, PaintArt.ShapeUv(BrushShape.Star));
                }
                GUI.color = Color.white;
            }
        }

        /// <summary>The picture is finished: "Bravo!" and confetti over the sheet.</summary>
        void DrawCelebration(float w, Brand brand)
        {
            float t = 4f - (_celebrateUntil - Time.unscaledTime);
            var colors = Palette;
            for (int i = 0; i < 70; i++)
            {
                // each piece falls on its own, from above the sheet (a fixed pattern: no state to keep)
                float seed = i * 12.9898f;
                float x = _paper.xMin + Mathf.Repeat(Mathf.Sin(seed) * 43758.5f, 1f) * _paper.width;
                float speed = 260f + 220f * Mathf.Repeat(Mathf.Sin(seed * 1.7f) * 2371.3f, 1f);
                float y = _paper.yMin - 60f - 400f * Mathf.Repeat(Mathf.Sin(seed * 2.3f) * 951.7f, 1f) + speed * t;
                if (y < _paper.yMin - 20f || y > _paper.yMax) continue;
                float sway = Mathf.Sin(t * 5f + i) * 14f;
                float size = 12f + 10f * Mathf.Repeat(i * 0.37f, 1f);
                GameHud.Box(new Rect(x + sway, y, size, size * (0.5f + 0.5f * Mathf.Abs(Mathf.Sin(t * 7f + i)))), colors[i % 9]);
            }
            float pop = t < 0.35f ? 1f + 0.25f * Mathf.Sin(Mathf.Clamp01(t / 0.35f) * Mathf.PI) : 1f;
            var r = new Rect(_paper.center.x - 380f, _paper.center.y - 130f, 760f, 240f);
            Panel(r, new Color(0.08f, 0.1f, 0.22f, 0.9f));
            var matrix = GUI.matrix;
            GUI.matrix = matrix * Matrix4x4.TRS(r.center, Quaternion.identity, new Vector3(pop, pop, 1f)) * Matrix4x4.Translate(-r.center);
            GameHud.Text(new Rect(r.x, r.y + 14f, r.width, 130f), PaintStrings.Bravo, GameHud.Big, TextAnchor.MiddleCenter, brand.PrimaryColor);
            GUI.matrix = matrix;
            GameHud.Text(new Rect(r.x, r.y + 150f, r.width, 70f), PaintStrings.PictureDone, GameHud.Small);
        }

        /// <summary>
        /// How to paint, shown by a little figure doing it (a child who can't read sees a hand go up and paint come out):
        /// at the start, and when the way changes. It goes when the first stroke starts.
        /// </summary>
        void DrawIntro(Brand brand)
        {
            bool camera = CurrentMode == Mode.Camera;
            var r = new Rect(_frameHud.x - 500f, Mathf.Max(170f, _paper.center.y - 200f), 1000f, 400f);
            Panel(r, new Color(0.08f, 0.1f, 0.22f, 0.92f));
            DrawFigure(new Vector2(r.x + 170f, r.y + 26f), brand);
            float tx = r.x + 370f, tw = r.width - 395f;
            GameHud.Text(new Rect(tx, r.y + 24f, tw, 100f), camera ? PaintStrings.CameraTitle : PaintStrings.Title,
                camera ? GameHud.Medium : GameHud.Big, TextAnchor.MiddleLeft, brand.PrimaryColor);
            if (Way == PaintWay.Easy)
            {
                GameHud.Text(new Rect(tx, r.y + 130f, tw, 70f), PaintStrings.EasyHowTo, GameHud.Medium, TextAnchor.MiddleLeft);
                GameHud.Text(new Rect(tx, r.y + 205f, tw, 56f), PaintStrings.EasyStop, GameHud.Small, TextAnchor.MiddleLeft);
            }
            else
            {
                GameHud.Text(new Rect(tx, r.y + 130f, tw, 56f), PaintStrings.PaintWith(RightHanded), GameHud.Small, TextAnchor.MiddleLeft);
                GameHud.Text(new Rect(tx, r.y + 186f, tw, 84f), PaintStrings.SwitchHowTo(RightHanded), GameHud.Small, TextAnchor.MiddleLeft);
            }
            GameHud.Text(new Rect(tx, r.y + 280f, tw, 90f), camera ? PaintStrings.CameraHowTo : PageIndex >= 0 ? PaintStrings.PageHowTo : PaintStrings.HowToButtons,
                GameHud.Small, TextAnchor.MiddleLeft, brand.MutedTextColor);
        }

        /// <summary>
        /// The figure on the how-to card, drawn from lines and discs, in a loop of 4 s: the arm goes up, paint comes out
        /// of the hand as it draws a loop, the arm goes down. Two hands: the other hand goes to the chest first, and when
        /// it drops the paint stops although the hand moves on. <paramref name="top"/> = the top of its head's box.
        /// </summary>
        void DrawFigure(Vector2 top, Brand brand)
        {
            float t = Mathf.Repeat(Time.unscaledTime, 4f);
            bool two = Way == PaintWay.TwoHands;
            bool rightPaints = !two || RightHanded;
            float side = rightPaints ? 1f : -1f;   // the screen is a mirror: the right hand is on the screen's right
            float cx = top.x, y0 = top.y;
            Vector2 head = new Vector2(cx, y0 + 52f);
            Vector2 shoulderA = new Vector2(cx + 44f * side, y0 + 118f), shoulderB = new Vector2(cx - 44f * side, y0 + 118f);
            Vector2 restA = new Vector2(cx + 66f * side, y0 + 236f), restB = new Vector2(cx - 66f * side, y0 + 236f);
            Vector2 loopCentre = new Vector2(cx + 118f * side, y0 + 96f);
            Vector2 LoopAt(float k) => loopCentre + new Vector2(Mathf.Cos(-k * Mathf.PI * 2f) * 40f * side, Mathf.Sin(-k * Mathf.PI * 2f) * 40f);
            float Ease(float a, float b) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, t));

            // the painting hand: rest, up to the loop's start, around it, down again
            Vector2 handA = t < 1.5f ? Vector2.Lerp(restA, LoopAt(0f), Ease(0.6f, 1.2f))
                : t < 3f ? LoopAt((t - 1.5f) / 1.5f)
                : Vector2.Lerp(LoopAt(1f), restA, Ease(3f, 3.5f));
            // two hands: the switch hand at the chest from 1.0 to 2.5 s (the paint stops there, the hand goes on)
            float up = two ? Ease(1f, 1.3f) * (1f - Ease(2.5f, 2.8f)) : 0f;
            Vector2 handB = Vector2.Lerp(restB, new Vector2(cx - 58f * side, y0 + 150f), up);
            float paintFrom = two ? 1.5f : 1.5f, paintTo = two ? 2.5f : 3f;

            var skin = new Color(1f, 0.82f, 0.66f);
            var shirt = brand.AccentColor;
            var paint = Palette[ColorIndex];
            // the paint so far (it fades when the loop starts over)
            float fade = t > 3.6f ? 1f - (t - 3.6f) / 0.4f : 1f;
            if (t > paintFrom)
                for (float k = paintFrom; k <= Mathf.Min(t, paintTo); k += 0.03f)
                    Icon(Around(LoopAt((k - 1.5f) / 1.5f), 22f), PaintArt.Disc, paint.WithAlpha(fade));
            // legs, body, head
            var trousers = Color.Lerp(brand.SurfaceColor, Color.white, 0.22f);
            GameHud.Line(new Vector2(cx - 22f, y0 + 228f), new Vector2(cx - 26f, y0 + 330f), trousers, 22f);
            GameHud.Line(new Vector2(cx + 22f, y0 + 228f), new Vector2(cx + 26f, y0 + 330f), trousers, 22f);
            Panel(new Rect(cx - 50f, y0 + 100f, 100f, 140f), shirt);
            var ink = new Color(0.1f, 0.1f, 0.2f);
            Icon(Around(head + new Vector2(0f, -9f), 102f), PaintArt.Disc, new Color(0.36f, 0.22f, 0.13f));   // hair
            Icon(Around(head + new Vector2(0f, 4f), 90f), PaintArt.Disc, skin);
            Icon(Around(head + new Vector2(-16f, 2f), 11f), PaintArt.Disc, ink);
            Icon(Around(head + new Vector2(16f, 2f), 11f), PaintArt.Disc, ink);
            GameHud.Line(head + new Vector2(-11f, 20f), head + new Vector2(0f, 26f), ink, 4f);                 // a smile
            GameHud.Line(head + new Vector2(0f, 26f), head + new Vector2(11f, 20f), ink, 4f);
            Arm(shoulderB, handB, -side, shirt, skin);
            Arm(shoulderA, handA, side, shirt, skin);
            if (two && up > 0.6f)   // the switch is up: a glow on that hand
                Icon(Around(handB, 46f + 6f * Mathf.Sin(t * 12f)), PaintArt.Ring, Color.white);
            if (t > 1.2f && t < paintTo) Icon(Around(handA, 34f), PaintArt.Ring, Color.white);   // the brush is down
        }

        /// <summary>An arm of the how-to figure: two bones of 62 px from the shoulder to the hand, the elbow bent outward.</summary>
        void Arm(Vector2 shoulder, Vector2 hand, float outward, Color sleeve, Color skin)
        {
            const float bone = 62f;
            Vector2 d = hand - shoulder;
            float reach = Mathf.Min(d.magnitude, bone * 1.98f);
            Vector2 dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.down;
            Vector2 mid = shoulder + dir * (reach * 0.5f);
            float bend = Mathf.Sqrt(Mathf.Max(0f, bone * bone - reach * reach * 0.25f));
            Vector2 normal = new Vector2(-dir.y, dir.x);
            if (Vector2.Dot(normal, new Vector2(outward, 0.6f)) < 0f) normal = -normal;   // elbows point out and down
            Vector2 elbow = mid + normal * bend;
            Vector2 end = shoulder + dir * reach;
            GameHud.Line(shoulder, elbow, sleeve, 20f);
            GameHud.Line(elbow, end, skin, 17f);
            Icon(Around(elbow, 20f), PaintArt.Disc, sleeve);
            Icon(Around(end, 26f), PaintArt.Disc, skin);
        }

        void DrawStatus(float w)
        {
            var motion = MotionSystem.Instance;
            float now = Time.unscaledTime;
            if (now < _introUntil || now < _celebrateUntil) return;
            bool anyCursor = _hands.Any(h => h.CursorVisible);
            var brush = BrushHand;
            string status = motion == null || !motion.IsConnected ? $"{PaintStrings.MouseHelp}   ·   {PaintStrings.KeyboardHelp}"
                : motion.ActiveBody == null ? PaintStrings.StepIn
                : Way == PaintWay.Easy ? (!anyCursor && now - _lastPaintAt > 4f ? PaintStrings.RaiseAHand : null)
                : StrokeCount < 3 && !SwitchOn && brush.CursorVisible && InsidePaper(brush.Cursor) >= 0f ? PaintStrings.RaiseToPaint(RightHanded)
                : null;
            if (status == null) return;
            var r = new Rect(340f, 1020f, w - 720f, 50f);
            Panel(r, new Color(0f, 0f, 0f, 0.55f));
            _label.normal.textColor = Color.white;
            _label.alignment = TextAnchor.MiddleCenter;
            int size = _label.fontSize;
            if (status.Length > 60) _label.fontSize = 15;   // (the keyboard help, without a camera service)
            GUI.Label(r, status, _label);
            _label.fontSize = size;
        }

        /// <summary>The way to paint, small in the corner (a grown-up changes it with OK on the remote).</summary>
        void DrawWay(Brand brand)
        {
            var r = new Rect(20f, GameHud.Height - 76f, 300f, 60f);
            Panel(r, brand.SurfaceColor.WithAlpha(0.9f));
            if (_okHeld > 0f) GameHud.Box(new Rect(r.x + 6f, r.yMax - 8f, (r.width - 12f) * Mathf.Clamp01(_okHeld / WayHoldSeconds), 4f), brand.PrimaryColor);
            var hand = PaintArt.IconTexture(PaintArt.Icon.Hand);
            if (Way == PaintWay.TwoHands)
            {
                GUI.DrawTextureWithTexCoords(new Rect(r.x + 8f, r.y + 10f, 40f, 40f), hand, new Rect(1f, 0f, -1f, 1f));
                GUI.DrawTexture(new Rect(r.x + 42f, r.y + 10f, 40f, 40f), hand);
            }
            else GUI.DrawTexture(new Rect(r.x + 24f, r.y + 8f, 44f, 44f), hand);
            _label.alignment = TextAnchor.MiddleLeft;
            _label.normal.textColor = Color.white;
            GUI.Label(new Rect(r.x + 92f, r.y + 4f, 200f, 28f), PaintStrings.WayName(Way), _label);
            _label.normal.textColor = brand.MutedTextColor;
            int size = _label.fontSize;
            _label.fontSize = 17;
            GUI.Label(new Rect(r.x + 92f, r.y + 30f, 204f, 24f), PaintStrings.WayHint, _label);
            _label.fontSize = size;
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

        /// <summary>A button's caption along its bottom edge (smaller with the frame: camera mode's buttons are).</summary>
        void Label(Rect button, string text, Color color)
        {
            float k = _scale / ReferenceScale;
            _caption.fontSize = Mathf.Clamp(Mathf.RoundToInt(22f * k), 14, 22);
            _caption.normal.textColor = color;
            GUI.Label(new Rect(button.x - 10f, button.yMax - 36f * k, button.width + 20f, 32f * k), text, _caption);
        }

        void Panel(Rect r, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            // (a 9-slice box smaller than its border draws a square: the corners shrink with the box)
            int corner = Mathf.Clamp((int)(Mathf.Min(r.width, r.height) * 0.5f) - 1, 2, 20);
            if (_round.border.left != corner) _round.border = _corners[corner] ??= new RectOffset(corner, corner, corner, corner);
            GUI.Box(r, GUIContent.none, _round);
            GUI.color = old;
        }

        readonly RectOffset[] _corners = new RectOffset[21];

        void EnsureStyles()
        {
            if (_round != null) return;
            _round = new GUIStyle { normal = { background = PaintArt.Rounded }, border = new RectOffset(20, 20, 20, 20) };
            _label = new GUIStyle(GameHud.Small) { fontSize = 22, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Overflow };
            _caption = new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter };
        }

        static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
    }
}
