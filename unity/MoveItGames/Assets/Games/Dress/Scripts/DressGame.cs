using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MoveIt.Motion;
using UnityEngine;
using UnityEngine.InputSystem;
using Joint = MoveIt.Motion.Joint;
using Random = UnityEngine.Random;

namespace MoveIt.Games.Dress
{
    /// <summary>
    /// "Vishu!": dress up on yourself. The screen is a mirror (the camera picture, framed on the player like Pikturo's
    /// camera mode), and the clothes stick to the body: each piece of art follows the skeleton
    /// (<see cref="OutfitRig"/>), drawn for the exact moment of the camera picture on the screen
    /// (<see cref="PoseTimeline"/>), with skirts, capes and hair swinging, wings flapping, sparkles on the gems.
    ///
    /// Modelled on the camera dress-up mirrors (Barbie's Kinect "Dream Closet": browse the closet with the hands, the
    /// outfit scaled onto the body, a photo; the Disney / Zero10 princess mirror: dresses with animated magic) and the
    /// top dress-up games (Dress to Impress: a theme, a timer, the runway with poses, star ratings; Style Savvy and
    /// Love Nikki: themes judged by what fits; Barbie Fashion Closet: mix and match, then a photo).
    ///
    /// Mirror (the default): categories on the left, the category's items on the right, magic / surprise / show /
    /// photo / clear on top, all pressed by holding a hand on them (or the remote: arrows move, OK presses). Choosing
    /// the worn item again changes its colour. The fashion show: a theme, a minute to dress for it, then the runway
    /// (three poses the camera checks) and three judges with stars; stars unlock the magic fabrics.
    /// </summary>
    public sealed class DressGame : MonoBehaviour
    {
        public enum Mode { Mirror, ShowIntro, ShowDressing, ShowWalk, ShowJudges, ShowResults }

        public enum Kind { Slot, Item, Magic, Surprise, Show, Ready, Photo, Remove, Again, Leave }

        public sealed class Button
        {
            public Kind Kind;
            public int Index;
            /// <summary>Centre and radius in HUD pixels (1080 high, y down), placed every frame from the offset.</summary>
            public Vector2 Center;
            public float Radius;
            /// <summary>Where it sits, in torso lengths from the shoulder centre (x right, y up), and its size.</summary>
            public Vector2 Offset;
            public float Size = 1f;
            public bool Big;
        }

        sealed class Hand
        {
            public Joint Shoulder, Elbow, Wrist;
            public Color Tint;
            public Vector2 Position;
            /// <summary>How fast the cursor moves (HUD pixels a second, smoothed): a hand passing over a bubble isn't pressing it.</summary>
            public float Speed;
            public bool Visible, Seen;
            public Button Hover;
            public float Dwell;
            public bool Pressed;
        }

        [Header("Scene")]
        [SerializeField] Camera cam;
        [SerializeField, Tooltip("Tundu/Outfit")] Material outfitMaterial;
        [SerializeField, Tooltip("Tundu/BodyMask")] Material maskMaterial;
        [SerializeField, Tooltip("Tundu/Fx (particles; the camera picture in scenes built before the mirror)")] Material fxMaterial;
        [SerializeField, Tooltip("Tundu/Mirror: the camera picture as a flattering mirror")] Material mirrorMaterial;
        [SerializeField, Tooltip("Hidden/Tundu/MirrorPrep: its denoise, blur and person passes")] Material mirrorPrepMaterial;

        [Header("Art (tools/dress)")]
        [SerializeField] TextAsset atlasJson;
        [SerializeField] TextAsset uiJson;
        [SerializeField] Texture2D atlas, atlasMask, thumbs, thumbsMask, uiSheet, fxSheet;

        [Header("Hands")]
        [SerializeField, Tooltip("Seconds a hand must stay on a bubble to press it.")] float dwellSeconds = 0.6f;
        [SerializeField, Tooltip("Hold time for bubbles that change a lot (photo, show, clear).")] float bigDwellSeconds = 1f;
        [SerializeField, Tooltip("An arm within this many degrees of hanging straight down is resting (no cursor).")] float restAngle = 30f;

        [Header("Framing (torso lengths, like Pikturo's camera mode)")]
        [SerializeField] float belowHips = 1f;
        [SerializeField] float aboveShoulders = 1.3f;
        [SerializeField, Tooltip("The picture re-frames only when the player is this far (torso lengths) from where the framing put them.")]
        float reframeAfter = 0.55f;

        [Header("Tracking")]
        [SerializeField, Tooltip("Camera frames the picture waits for the poses after it (OutfitTracker): the clothes then neither shake nor trail the body.")]
        int lookaheadFrames = 2;

        [Header("Fashion show")]
        [SerializeField] float dressSeconds = 60f;
        [SerializeField] float poseSeconds = 7f;
        [SerializeField, Tooltip("A pose counts once held this long.")] float poseHold = 0.5f;
        [SerializeField] int posesPerShow = 3;

        [Header("Photos")]
        [SerializeField] int keepPhotos = 60;

        public Mode CurrentMode { get; private set; } = Mode.Mirror;
        public DressLook Look { get; } = new DressLook();
        public DressProgress Progress { get; private set; } = new DressProgress();
        public Slot Category { get; private set; } = Slot.Top;
        public Theme ShowTheme { get; private set; }
        public Verdict[] Verdicts { get; private set; }
        public int ShowStars { get; private set; }
        public float[] PoseScores { get; private set; }
        public ShowPose[] Poses { get; private set; }
        public int PoseIndex { get; private set; }
        public string LastPhoto { get; private set; }
        public Rect Crop => _crop;
        public OutfitPose Pose => _pose;
        public OutfitRenderer Outfit => _outfit;
        public CameraFeed Feed => _feed;
        public bool ShowingDoll => _doll;
        public IReadOnlyList<Button> Buttons => _buttons;
        public OutfitArt Art => _art;
        public bool RightHandHolds { get; private set; } = true;

        const string LookKey = "dress.look", ProgressKey = "dress.progress", HandKey = "dress.rightHand";
        const float ImageUnits = 1000f;

        OutfitArt _art;
        OutfitRenderer _outfit;
        DressFx _fx;
        CameraFeed _feed;
        MotionDebugOverlay _mirror;
        bool _mirrorWasVisible;
        Material _picture, _thumbMaterial;
        MirrorLook _mirrorLook;
        int _requestWidthBefore;
        Texture2D _backdrop;
        RenderTexture _roomLight;
        float _roomLightAt;
        static readonly int RoomLightId = Shader.PropertyToID("_TunduAmbient"), RoomLightOnId = Shader.PropertyToID("_TunduAmbientOn");
        Transform _pictureQuad;
        readonly OutfitPose _pose = new OutfitPose();
        readonly OutfitMotion _motion = new OutfitMotion();
        readonly OutfitTracker _tracker = new OutfitTracker();
        readonly Vector2[] _fitJoints = new Vector2[Skeleton.JointCount];
        readonly float[] _vis = new float[Skeleton.JointCount];
        readonly Vector2[] _camJoints = new Vector2[Skeleton.JointCount];
        readonly float[] _flash = new float[DressCatalog.SlotCount], _pop = new float[DressCatalog.SlotCount];
        readonly bool[] _poofPending = new bool[DressCatalog.SlotCount];
        readonly Hand _left = new Hand { Shoulder = Joint.LeftShoulder, Elbow = Joint.LeftElbow, Wrist = Joint.LeftWrist };
        readonly Hand _right = new Hand { Shoulder = Joint.RightShoulder, Elbow = Joint.RightElbow, Wrist = Joint.RightWrist };
        readonly List<Button> _buttons = new List<Button>();
        readonly System.Random _rng = new System.Random();
        Rect _crop = new Rect(0f, 0f, 16f / 9f, 1f);
        bool _doll = true, _layoutDirty = true, _remoteFocus, _saveLook;
        float _guiWidth, _unit = 327f;
        Vector2 _anchor;
        double _lastSample, _lastShown, _handSampleTime;
        int _focus;
        Mode _laidOutFor = (Mode)(-1);
        string _toast;
        float _toastUntil, _flashScreen, _modeTime, _countdown = -1f, _introUntil, _lastActivity;
        float _poseHeld, _poseBest, _poseDoneAt = -1f, _tick;
        bool _poseDone, _runwayPhoto;
        List<Finish> _unlocked = new List<Finish>();
        int _photoCount;
        Theme _lastTheme = (Theme)(-1);
        GUIStyle _label, _big, _medium, _small, _pill;

        string PhotoDir => Path.Combine(Application.persistentDataPath, "Photos");

        /// <summary>Pixels (HUD) per torso length at the target framing, and where the shoulders sit.</summary>
        float UnitPx => 1080f / (belowHips + 1f + aboveShoulders);
        Vector2 ShoulderPx => new Vector2(_guiWidth * 0.5f, aboveShoulders * UnitPx);

        // ---------------------------------------------------------------- lifecycle

        void Start()
        {
            _art = OutfitArt.Load(atlasJson, uiJson, atlas, atlasMask, thumbs, thumbsMask, uiSheet, fxSheet);
            _shapes = MakeShapes();
            _hud = new HudBatch(_art.Ui, _shapes);
            _outfit = new OutfitRenderer(_art, outfitMaterial, maskMaterial, transform);
            _fx = new DressFx(transform, fxMaterial, fxSheet);
            _thumbMaterial = new Material(outfitMaterial);
            _thumbMaterial.SetTexture("_MainTex", thumbs);
            _thumbMaterial.SetTexture("_MaskTex", thumbsMask);
            _thumbMaterial.SetFloat("_UseExtra", 0f);
            _thumbMaterial.SetFloat("_LinearOut", 0f);
            if (mirrorMaterial != null)
            {
                _picture = new Material(mirrorMaterial);
                if (mirrorPrepMaterial != null) _mirrorLook = new MirrorLook(mirrorPrepMaterial);
            }
            else
            {
                _picture = new Material(fxMaterial);
                _picture.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                _picture.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
                _picture.EnableKeyword("_VIGNETTE");   // the mirror's darker edges, with the picture (DrawMirrorFrame)
            }
            _picture.renderQueue = 2990;
            _backdrop = MakeBackdrop();
            _pictureQuad = MakeQuad("Camera picture", _picture);

            _mirror = MotionSystem.Instance != null ? MotionSystem.Instance.GetComponent<MotionDebugOverlay>() : null;
            if (_mirror != null)
            {
                _mirrorWasVisible = _mirror.Visible;
                _mirror.Visible = false;
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            // the camera's own width (the box runs it at 1024 x 768 for the pose AI): the picture fills the TV, 640 was
            // blown up more than 2x
            _requestWidthBefore = CameraFeed.RequestWidth;
            CameraFeed.RequestWidth = 1024;
#endif
            _feed = CameraFeed.Request();
            _feed.History = 6;
            _tracker.Lookahead = lookaheadFrames;
            _pose.Prefiltered = true;
            Progress = DressProgress.Load(PlayerPrefs.GetString(ProgressKey, ""));
            var saved = DressLook.Load(PlayerPrefs.GetString(LookKey, ""), Progress.Finishes);
            Look.CopyFrom(saved);
#if UNITY_EDITOR
            // hands-off checks (like DevCapture): Temp/moveit_dress_look.txt holds a look to start in (DressLook.Save format)
            string lookFile = Path.Combine(Application.dataPath, "../Temp/moveit_dress_look.txt");
            if (File.Exists(lookFile)) Look.CopyFrom(DressLook.Load(File.ReadAllText(lookFile).Trim(), DressCatalog.FinishStars.Length));
#endif
            RightHandHolds = PlayerPrefs.GetInt(HandKey, 1) == 1;
            _outfit.RightHandHolds = RightHandHolds;
            _left.Tint = Brand.Current.PlayerColor(1);
            _right.Tint = Brand.Current.PlayerColor(0);
            _photoCount = Directory.Exists(PhotoDir) ? Directory.GetFiles(PhotoDir, "*.png").Length : 0;
            _introUntil = Time.unscaledTime + 8f;
            _lastActivity = Time.unscaledTime;
            for (int s = 0; s < _pop.Length; s++) _pop[s] = 1f;
            GameAudio.PlayMusic("dress");
            Debug.Log($"[Dress] Ready: {_art.PieceCount} pieces, {DressCatalog.All.Count()} items, stars {Progress.Stars}, finishes {Progress.Finishes}");
        }

        void OnDestroy()
        {
            SaveLook();
            if (_feed != null)
            {
                _feed.ShowAt = 0;
                _feed.History = 1;
                CameraFeed.Release();
            }
            _feed = null;
            if (_mirror != null) _mirror.Visible = _mirrorWasVisible;
            _outfit?.Dispose();
            _mirrorLook?.Dispose();
            _hud?.Dispose();
            if (_shapes != null) Destroy(_shapes);
            if (_photoPreview != null) _photoPreview.Release();
            if (_requestWidthBefore > 0) CameraFeed.RequestWidth = _requestWidthBefore;
            _fx?.Dispose();
            if (_picture != null) Destroy(_picture);
            if (_thumbMaterial != null) Destroy(_thumbMaterial);
            if (_backdrop != null) Destroy(_backdrop);
            if (_roundTex != null) Destroy(_roundTex);
            if (_ringTex != null) Destroy(_ringTex);
            Shader.SetGlobalFloat(RoomLightOnId, 0f);
            if (_roomLight != null) _roomLight.Release();
        }

        void Update()
        {
            if (GameNavigation.HandleCommonKeys()) return;
            float dt = Time.unscaledDeltaTime;
            _modeTime += dt;
            _guiWidth = Screen.width / (Screen.height / 1080f);
            FitCamera();
            var body = MotionSystem.Instance != null ? MotionSystem.Instance.ActiveBody : null;
            if (body != null && !body.IsTracking) body = null;
            FeedPose(body, dt);
            UpdateFraming(body, dt);
            UpdatePicture(body);
            UpdateRoomLight();
            _motion.Update(_pose, dt);
            Layout(body, dt);
            UpdateHands(body, dt);
            HandleKeys();
            UpdateMode(body, dt);
            Animate(dt);
            var toWorld = ToWorld();
            _outfit.Build(Look, _pose, _motion, _flash, _pop, _dollUnder, toWorld, _guiWidth);
            if (_mirrorLook != null && _feed != null && _feed.IsLive && PerfSwitches.Get("mirrorprep") != "0") _mirrorLook.UpdatePerson(_guiWidth, _pictureST);
            _fx.ToWorld = toWorld;
            for (int s = 0; s < DressCatalog.SlotCount; s++)
                if (_poofPending[s] && _outfit.SlotDrawn[s])
                {
                    _poofPending[s] = false;
                    DressCatalog.Rgb(Math.Max(0, Look.ColorOf((Slot)s)), out float r, out float g, out float b);
                    _fx.Poof(_outfit.SlotCenter[s], new Color(r, g, b), Mathf.Clamp(_pose.T * toWorld.m00 / 300f, 0.6f, 1.4f));
                }
            _fx.Tick(_outfit, _pose, Look.Effect, dt, _guiWidth);
            if (_saveLook)
            {
                _saveLook = false;
                SaveLook();
            }
        }

        void SaveLook()
        {
            PlayerPrefs.SetString(LookKey, Look.Save());
            PlayerPrefs.Save();
        }

        // ---------------------------------------------------------------- the camera picture and the body

        void FitCamera()
        {
            cam.orthographic = true;
            cam.orthographicSize = 540f;
            cam.transform.position = new Vector3(_guiWidth * 0.5f, 540f, -10f);
            _pictureQuad.position = new Vector3(_guiWidth * 0.5f, 540f, 5f);
            _pictureQuad.localScale = new Vector3(_guiWidth, 1080f, 1f);
        }

        /// <summary>The picture's space (1000 per picture height, y up) -> the screen, through the framing.</summary>
        Matrix4x4 ToWorld()
        {
            float k = 1080f / (_crop.height * ImageUnits);
            return Matrix4x4.TRS(new Vector3(-_crop.xMin * ImageUnits * k, -_crop.yMin * ImageUnits * k, 0f), Quaternion.identity, new Vector3(k, k, 1f));
        }

        /// <summary>
        /// Framed on the player from the knees to above the head (the hands reach the bubbles). The framing stays put
        /// while the player sways, steps a little or reaches for a bubble (a moving picture moves the bubbles and the
        /// clothes with it: players saw the screen "moving around"); only when they are clearly elsewhere (more than
        /// <see cref="reframeAfter"/> torso lengths away, or a fifth nearer or farther) does it glide over to them, then
        /// stays put again. It never starts moving while a hand is pressing something or a photo is being taken.
        /// </summary>
        void UpdateFraming(Body body, float dt)
        {
            float imageAspect = body != null ? body.Aspect : _feed != null && _feed.HasFrame ? _feed.Aspect : 16f / 9f;
            float screenAspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            bool player = !_doll && _pose.Valid;
            if (player)
            {
                Vector2 shoulders = _pose.ShoulderC / ImageUnits;
                float unit = Mathf.Max(_pose.T / ImageUnits, 0.02f);
                _playerGoneFor = 0f;
                if (!_framed)
                {
                    _framed = true;
                    _reframing = false;
                    _refShoulder = shoulders;
                    _refUnit = unit;
                }
                bool hold = _left.Hover != null || _right.Hover != null || _countdown >= 0f;
                bool away = Vector2.Distance(shoulders, _refShoulder) > reframeAfter * _refUnit || Mathf.Abs(unit / _refUnit - 1f) > 0.2f;
                if (!_reframing && !hold && away)
                {
                    _reframing = true;
                    Debug.Log($"[Dress] Re-framing: shoulders {shoulders} (were {_refShoulder}), torso {unit:0.000} (was {_refUnit:0.000})");
                }
                if (_reframing)
                {
                    _refShoulder = Vector2.SmoothDamp(_refShoulder, shoulders, ref _refVelocity, 0.32f, Mathf.Infinity, dt);
                    _refUnit = Mathf.SmoothDamp(_refUnit, unit, ref _refUnitVelocity, 0.32f, Mathf.Infinity, dt);
                    if (Vector2.Distance(_refShoulder, shoulders) < 0.03f * unit && Mathf.Abs(_refUnit / unit - 1f) < 0.01f)
                    {
                        _reframing = false;
                        _refVelocity = Vector2.zero;
                        _refUnitVelocity = 0f;
                    }
                }
            }
            else if (_framed && (_playerGoneFor += dt) > 3f)
            {
                _framed = false;   // nobody for a while: the whole picture (and the paper doll in it)
                _reframing = false;
            }
            _crop = _framed ? FrameOn(_refShoulder, _refUnit, imageAspect, screenAspect) : Fit(imageAspect * 0.5f, 0.5f, 1f, imageAspect, screenAspect);
        }

        /// <summary>The framing for shoulders at <paramref name="shoulders"/> (view space) with a torso of <paramref name="unit"/>.</summary>
        Rect FrameOn(Vector2 shoulders, float unit, float imageAspect, float screenAspect)
        {
            float bottom = shoulders.y - (1f + belowHips) * unit, top = shoulders.y + aboveShoulders * unit;
            return Fit(shoulders.x, (top + bottom) * 0.5f, top - bottom, imageAspect, screenAspect);
        }

        // the framing's reference: where the shoulders were (view space) and the torso's length when it was set
        Vector2 _refShoulder, _refVelocity;
        float _refUnit = 0.25f, _refUnitVelocity, _playerGoneFor;
        bool _framed, _reframing;

        static Rect Fit(float cx, float cy, float height, float imageAspect, float screenAspect)
        {
            float h = Mathf.Min(height, 1f), w = h * screenAspect;
            if (w > imageAspect)
            {
                w = imageAspect;
                h = w / screenAspect;
            }
            return new Rect(Mathf.Clamp(cx - w * 0.5f, 0f, imageAspect - w), Mathf.Clamp(cy - h * 0.5f, 0f, 1f - h), w, h);
        }

        void UpdatePicture(Body body)
        {
            bool live = _feed != null && _feed.IsLive;
            if (live)
            {
                float imageAspect = body != null ? body.Aspect : _feed.Aspect;
                Texture tex = _feed.Texture;
                if (_mirrorLook != null && PerfSwitches.Get("mirrorprep") != "0")   // PerfLab: the picture as it comes
                {
                    // the part of the picture on the screen, in its texture coordinates (mirrored: see the offset below)
                    var shown = PerfSwitches.Get("mirrorcrop") == "0" ? new Rect(0f, 0f, 1f, 1f)
                        : new Rect(1f - (_crop.xMin + _crop.width) / imageAspect, _crop.yMin, _crop.width / imageAspect, _crop.height);
                    _mirrorLook.NewFrame(_feed.Texture, _feed.CaptureTime, shown);
                    if (_mirrorLook.Picture != null) tex = _mirrorLook.Picture;
                    if (_mirrorLook.Person != null) _picture.SetTexture("_PersonMap", _mirrorLook.Person);
                    _picture.SetVector("_PersonRect", new Vector4(0f, 0f, 1f / Mathf.Max(1f, _guiWidth), 1f / 1080f));
                    // only a real player: the paper doll's body mask made a sharp, grainy halo of the room round her
                    _picture.SetFloat("_PersonOn", _mirrorLook.Person != null && !_doll && _pose.Valid ? 1f : 0f);
                }
                _picture.SetFloat("_Live", 1f);
                _picture.SetTexture("_BaseMap", tex);
                _pictureST = new Vector4(-_crop.width / imageAspect, _crop.height, 1f - _crop.xMin / imageAspect, _crop.yMin);
                _picture.SetTextureScale("_BaseMap", new Vector2(_pictureST.x, _pictureST.y));
                _picture.SetTextureOffset("_BaseMap", new Vector2(_pictureST.z, _pictureST.w));
            }
            else
            {
                _picture.SetFloat("_Live", 0f);
                _picture.SetTexture("_BaseMap", _backdrop);
                _picture.SetTextureScale("_BaseMap", Vector2.one);
                _picture.SetTextureOffset("_BaseMap", Vector2.zero);
            }
            _picture.SetColor("_BaseColor", Color.white);
            // the dressing-room mirror's darker edges while its frame is up (DrawMirrorFrame)
            bool mirror = CurrentMode == Mode.Mirror || CurrentMode == Mode.ShowDressing || CurrentMode == Mode.ShowIntro;
            _picture.SetColor("_VignetteColor", new Color(16f / 255f, 18f / 255f, 48f / 255f, mirror ? 1f : 0f));
        }

        /// <summary>The mirror's mapping from the screen (0..1) to the camera picture's texture coordinates: scale xy, offset zw.</summary>
        Vector4 _pictureST = new Vector4(1f, 1f, 0f, 0f);

        /// <summary>The camera picture's average colour, for the clothes' light (Tundu/Outfit): a 16 x 9 copy, four
        /// times a second, whose last mip level is the average.</summary>
        void UpdateRoomLight()
        {
            bool live = _feed != null && _feed.IsLive;
            Shader.SetGlobalFloat(RoomLightOnId, live ? 1f : 0f);
            if (!live || Time.unscaledTime < _roomLightAt) return;
            _roomLightAt = Time.unscaledTime + 0.25f;
            if (_roomLight == null)
            {
                _roomLight = new RenderTexture(16, 9, 0, RenderTextureFormat.ARGB32)
                {
                    name = "Room light", useMipMap = true, autoGenerateMips = true, filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                _roomLight.Create();
                Shader.SetGlobalTexture(RoomLightId, _roomLight);
            }
            Graphics.Blit(_feed.Texture, _roomLight);
        }

        /// <summary>
        /// The body the clothes are drawn on: the player's joints fitted for the moment of the camera picture on the
        /// screen (<see cref="OutfitTracker"/>: the picture waits a couple of camera frames for the poses after it, so the
        /// joints come from both sides of its moment: no shaking, no trailing), or the paper doll when nobody is there (it
        /// is also drawn under the clothes while the camera picture is missing).
        /// </summary>
        void FeedPose(Body body, float dt)
        {
            if (body != null && body.SampleTime != _lastSample)
            {
                _lastSample = body.SampleTime;
                _tracker.Add(body.SampleTime, body.Raw, body.Visibility, body.HasWorld ? body.RawWorld : null);
            }
            bool tracking = body != null && _tracker.Count > 0;
            if (_feed != null)
            {
                // the newest picture captured by the moment the poses reach (a few ms of slack for the clocks' rounding)
                _feed.ShowAt = tracking ? (long)Math.Round(_tracker.ShowTime * 1000.0) + 4 : 0;
                _feed.Pump();
            }
            bool live = _feed != null && _feed.IsLive;
            if (tracking)
            {
                if (_doll)
                {
                    _doll = false;
                    _pose.Reset();
                    _motion.Reset();
                    _lastShown = 0;
                    Debug.Log($"[Dress] Player seen (camera picture {(live ? "on" : "not yet")})");
                }
                double t = live ? _feed.CaptureTime / 1000.0 : _tracker.ShowTime;
                if (live && (t < _tracker.Oldest - 0.5 || t > _tracker.Newest + 0.5)) t = _tracker.ShowTime;   // clocks apart
                if (t != _lastShown)
                {
                    _tracker.At(t, _fitJoints, _vis);
                    for (int i = 0; i < _camJoints.Length; i++) _camJoints[i] = _fitJoints[i] * ImageUnits;
                    float camDt = _lastShown > 0 ? Mathf.Clamp((float)(t - _lastShown), 0.001f, 0.2f) : 0f;
                    // the body's turn from the camera AI's depth estimates, when it sends world joints
                    float worldYaw = _tracker.WorldYawAt(t, out float yaw) ? yaw : float.NaN;
                    _pose.Update(_camJoints, _vis, camDt, worldYaw);
                    _lastShown = t;
                }
                _dollUnder = !live;
                return;
            }
            if (!_doll)
            {
                _doll = true;
                _pose.Reset();
                _motion.Reset();
                Debug.Log($"[Dress] Player gone ({_tracker.Swaps} side swaps put right so far)");
                _tracker.Clear();
            }
            _dollUnder = true;
            OutfitPose.DollJoints(new Vector2((_crop.xMin + _crop.width * 0.5f) * ImageUnits, (_crop.yMin + _crop.height * 0.74f) * ImageUnits),
                                  _crop.height * ImageUnits * 0.23f, Time.unscaledTime, _camJoints, _vis);
            _pose.Update(_camJoints, _vis, dt);
        }

        /// <summary>Draw the paper doll under the clothes (nobody there, or no camera picture yet).</summary>
        bool _dollUnder = true;

        // ---------------------------------------------------------------- hands

        /// <summary>
        /// The hands' cursors, on the hands in the picture: from the fitted pose (<see cref="OutfitTracker"/>), so they are
        /// as steady as the clothes and exactly where the picture shows the hands. A hand presses the bubble it rests on
        /// (<see cref="Dwell"/>); a hand hanging at the side has no cursor.
        /// </summary>
        void UpdateHands(Body body, float dt)
        {
            bool interactive = CurrentMode == Mode.Mirror || CurrentMode == Mode.ShowDressing || CurrentMode == Mode.ShowResults;
            foreach (var hand in new[] { _left, _right })
            {
                bool tracked = false;
                if (!_doll && _pose.Valid)
                {
                    float seen = _pose.Seen[(int)hand.Wrist];
                    hand.Seen = hand.Seen ? seen > 0.3f : seen > 0.6f;
                    tracked = hand.Seen;
                }
                if (tracked)
                {
                    // the fingertips: the wrist carried on along the forearm (as far as the elbow is seen)
                    Vector2 w = _pose.J[(int)hand.Wrist], e = _pose.J[(int)hand.Elbow];
                    Vector2 tip = w + (w - e) * (0.3f * _pose.Seen[(int)hand.Elbow]);
                    Vector2 p = ToGui(tip / ImageUnits);
                    float speed = hand.Visible && dt > 0f ? Vector2.Distance(p, hand.Position) / dt : 0f;
                    hand.Speed = Mathf.Lerp(hand.Speed, speed, 1f - Mathf.Exp(-dt / 0.12f));
                    hand.Position = p;
                }
                else
                {
                    hand.Seen = false;
                    hand.Speed = 0f;
                }
                hand.Visible = tracked && interactive && !IsResting(hand);
                Dwell(hand, hand.Visible ? ButtonAt(hand.Position, hand.Hover) : null, dt);
            }
            // development without a camera: the mouse hovers and clicks
            var mouse = Mouse.current;
            if (mouse != null && (body == null || MotionSystem.Instance == null || !MotionSystem.Instance.IsConnected))
            {
                Vector2 m = mouse.position.ReadValue();
                Vector2 gui = new Vector2(m.x / Screen.height * 1080f, 1080f - m.y / Screen.height * 1080f);
                if (mouse.leftButton.wasPressedThisFrame && interactive)
                {
                    var b = ButtonAt(gui, null);
                    if (b != null) Press(b);
                }
            }
        }

        Vector2 ToGui(Vector2 view)
        {
            if (_crop.height <= 0f) return default;
            return new Vector2((view.x - _crop.xMin) / _crop.width * _guiWidth, (1f - (view.y - _crop.yMin) / _crop.height) * 1080f);
        }

        /// <summary>An arm hanging straight down (within <see cref="restAngle"/> of it, stretched) is resting: no cursor.</summary>
        bool IsResting(Hand hand)
        {
            if (_pose.Seen[(int)hand.Elbow] < 0.3f) return false;
            Vector2 s = _pose.J[(int)hand.Shoulder], e = _pose.J[(int)hand.Elbow], w = _pose.J[(int)hand.Wrist];
            if (Vector2.Angle(e - s, Vector2.down) >= restAngle || Vector2.Angle(w - e, Vector2.down) >= restAngle) return false;
            return Vector2.Distance(s, w) >= 0.85f * _pose.T;
        }

        /// <summary>The bubble under <paramref name="gui"/>: a little bigger than it looks, and bigger still for the
        /// bubble the hand is already on (it doesn't slip off with the hand's small wobbles).</summary>
        Button ButtonAt(Vector2 gui, Button current)
        {
            if (current != null && _buttons.Contains(current) && Vector2.Distance(gui, current.Center) <= current.Radius * 1.5f) return current;
            Button best = null;
            float bestD = float.MaxValue;
            foreach (var b in _buttons)
            {
                float d = Vector2.Distance(gui, b.Center);
                if (d <= b.Radius * 1.15f && d < bestD)
                {
                    bestD = d;
                    best = b;
                }
            }
            return best;
        }

        /// <summary>
        /// Holding a hand on a bubble presses it. The time only runs while the hand is nearly still on it (a hand on its
        /// way somewhere passes over bubbles: players found them pressing by themselves); it pauses while the hand moves
        /// and starts over on another bubble.
        /// </summary>
        void Dwell(Hand hand, Button button, float dt)
        {
            if (button != hand.Hover)
            {
                hand.Hover = button;
                hand.Dwell = 0f;
                hand.Pressed = false;
                if (button != null) GameAudio.Sfx("ui_move", 0.3f);
            }
            if (button == null) return;
            _lastActivity = Time.unscaledTime;
            float still = Mathf.Max(1.6f * _unit, 300f);   // HUD pixels a second: ~1.6 torso lengths
            if (hand.Speed < still) hand.Dwell += dt;
            if (!hand.Pressed && hand.Dwell >= DwellTime(button))
            {
                hand.Pressed = true;
                Debug.Log($"[Dress] {(hand == _left ? "Left" : "Right")} hand presses {button.Kind} {button.Index} at {hand.Position}");
                Press(button);
            }
        }

        /// <summary>Seconds to hold: items and categories quickly, the buttons that change the whole look or the screen longer.</summary>
        float DwellTime(Button b) => b.Kind == Kind.Surprise || b.Kind == Kind.Remove ? bigDwellSeconds * 1.3f : b.Big ? bigDwellSeconds : dwellSeconds;

        float Progress01(Hand hand, Button b) =>
            hand.Hover != b || hand.Pressed ? 0f : Mathf.Clamp01(hand.Dwell / DwellTime(b));

        // ---------------------------------------------------------------- layout

        /// <summary>
        /// The bubbles sit at fixed places around the player's shoulders, measured in torso lengths, so every child
        /// reaches them the same way (columns 1.36 out from the middle, from 0.86 above the shoulders to 0.96 below;
        /// the top row 1.14 above, 0.84 either side). They follow the player's framing smoothly and hold still while a
        /// hand is pressing one.
        /// </summary>
        void Layout(Body body, float dt)
        {
            if (_layoutDirty || _laidOutFor != CurrentMode)
            {
                _layoutDirty = false;
                _laidOutFor = CurrentMode;
                _buttons.Clear();
                if (CurrentMode == Mode.Mirror || CurrentMode == Mode.ShowDressing)
                {
                    for (int k = 0; k < DressCatalog.SlotCount; k++)
                        _buttons.Add(new Button { Kind = Kind.Slot, Index = k, Offset = new Vector2(-1.36f, 0.86f - k * 0.303f) });
                    for (int k = 0; k <= 6; k++)
                        _buttons.Add(new Button { Kind = Kind.Item, Index = k - 1, Offset = new Vector2(1.36f, 0.86f - k * 0.303f) });
                    var row = CurrentMode == Mode.Mirror
                        ? new[] { Kind.Magic, Kind.Surprise, Kind.Show, Kind.Photo, Kind.Remove }
                        : new[] { Kind.Magic, Kind.Surprise, Kind.Ready, Kind.Photo, Kind.Remove };
                    for (int k = 0; k < row.Length; k++)
                        _buttons.Add(new Button
                        {
                            Kind = row[k], Offset = new Vector2((k - 2) * 0.42f, 1.14f), Size = 0.92f,
                            Big = row[k] == Kind.Show || row[k] == Kind.Photo || row[k] == Kind.Remove || row[k] == Kind.Ready,
                        });
                }
                else if (CurrentMode == Mode.ShowResults)
                {
                    _buttons.Add(new Button { Kind = Kind.Again, Offset = new Vector2(-0.62f, 1.1f), Size = 1.1f, Big = true });
                    _buttons.Add(new Button { Kind = Kind.Leave, Offset = new Vector2(0.62f, 1.1f), Size = 1.1f, Big = true });
                }
                _focus = Mathf.Clamp(_focus, 0, Mathf.Max(0, _buttons.Count - 1));
            }

            // where the framing put the shoulders on the screen and how long a torso is there: the bubbles stay put
            // while the picture does (UpdateFraming), so a hand reaching for one finds it where it was
            Vector2 anchor = ShoulderPx;
            float unit = UnitPx;
            if (_framed && _crop.height > 0f)
            {
                anchor = ToGui(_refShoulder);
                unit = _refUnit / _crop.height * 1080f;
            }
            _anchor = anchor;
            _unit = unit;
            // never bigger than fits: a player close to the camera (the framing can't zoom out further) made the columns
            // run off the top of the screen. Everything stays on the screen, the layout's shape intact.
            float u = Mathf.Clamp(_unit, 220f, 400f);
            float r = 0.142f * u;
            float top = 0f, bottom = 0f;
            foreach (var b in _buttons)
            {
                bool column = b.Kind == Kind.Slot || b.Kind == Kind.Item;
                top = Mathf.Max(top, b.Offset.y * u + r * b.Size + (b.Kind == Kind.Item ? 70f : 0f));   // the category's name above the items
                bottom = Mathf.Max(bottom, -b.Offset.y * u + r * b.Size + (column ? 10f : 36f));        // labels under the action bubbles
            }
            float minY = top + 12f, maxY = 1080f - bottom - 70f;
            float ay = minY <= maxY ? Mathf.Clamp(_anchor.y, minY, maxY) : (minY + maxY) * 0.5f;
            float edge = 1.36f * u + r + 8f;
            Vector2 a = new Vector2(Mathf.Clamp(_anchor.x, edge, Mathf.Max(edge, _guiWidth - edge)), ay);
            foreach (var b in _buttons)
            {
                b.Radius = r * b.Size;
                b.Center = new Vector2(a.x + b.Offset.x * u, a.y - b.Offset.y * u);
            }
        }

        // ---------------------------------------------------------------- actions

        public void Press(Button b)
        {
            _lastActivity = Time.unscaledTime;
            _introUntil = Mathf.Min(_introUntil, Time.unscaledTime + 1.5f);
            switch (b.Kind)
            {
                case Kind.Slot: SelectCategory((Slot)b.Index); break;
                case Kind.Item: Wear(Category, b.Index); break;
                case Kind.Magic: NextMagic(); break;
                case Kind.Surprise: SurpriseMe(); break;
                case Kind.Show: StartShow(); break;
                case Kind.Ready: StartWalk(); break;
                case Kind.Photo: StartPhoto(); break;
                case Kind.Remove:
                    if (CanUndo) Undo();
                    else RemoveAll();
                    break;
                case Kind.Again: StartShow(); break;
                case Kind.Leave: SetMode(Mode.Mirror); break;
            }
        }

        public void SelectCategory(Slot slot)
        {
            if (slot == Category) return;
            Category = slot;
            GameAudio.Sfx("paint_pick", 0.8f, 0.9f + 0.05f * (int)slot);
            Debug.Log($"[Dress] Category {slot}");
        }

        /// <summary>Puts on item <paramref name="index"/> of the slot (-1 = take it off); the worn one again = next colour.</summary>
        public void Wear(Slot slot, int index)
        {
            var change = Look.Wear(slot, index, Progress.Finishes);
            int s = (int)slot;
            switch (change)
            {
                case DressLook.Change.Wore:
                    _flash[s] = 1f;
                    _pop[s] = 0f;
                    _poofPending[s] = true;
                    GameAudio.Sfx("dress_poof", 0.9f, 1f + 0.04f * s);
                    ShowToast(DressStrings.ItemName(Look.Worn(slot)));
                    break;
                case DressLook.Change.Recolored:
                    _flash[s] = 0.7f;
                    _poofPending[s] = true;
                    GameAudio.Sfx("dress_color", 0.9f, 0.9f + 0.06f * (Look.Shade[s] % 6));
                    var finish = Look.FinishOf(slot);
                    ShowToast(finish != Finish.Plain ? DressStrings.FinishName(finish) + "!" : DressStrings.NewColor(DressStrings.ItemName(Look.Worn(slot))));
                    break;
                case DressLook.Change.Removed:
                    GameAudio.Sfx("dress_remove", 0.8f);
                    ShowToast(DressStrings.Nothing);
                    break;
                default:
                    return;
            }
            _saveLook = true;
            Debug.Log($"[Dress] {change} {slot}: {Look.Worn(slot)?.Id ?? "-"} colour {Look.ColorOf(slot)} {Look.FinishOf(slot)}");
        }

        public void NextMagic()
        {
            var e = Look.NextEffect();
            GameAudio.Sfx(e == Effect.None ? "dress_remove" : "dress_magic", 0.9f, 0.9f + 0.05f * (int)e);
            ShowToast(DressStrings.MagicOn(DressStrings.EffectName(e)));
            _saveLook = true;
            Debug.Log($"[Dress] Magic {e}");
        }

        public void SurpriseMe()
        {
            KeepForUndo();
            var theme = Look.Surprise(_rng, CurrentMode == Mode.ShowDressing ? ShowTheme : (Theme?)null, Progress.Finishes);
            for (int s = 0; s < DressCatalog.SlotCount; s++)
            {
                if (!Look.IsWorn((Slot)s)) continue;
                _flash[s] = 1f;
                _pop[s] = -0.06f * s;   // one after the other
                _poofPending[s] = true;
            }
            GameAudio.Sfx("dress_surprise");
            ShowToast(DressStrings.SurpriseLook(DressStrings.ThemeName(theme)));
            _saveLook = true;
            Debug.Log($"[Dress] Surprise ({theme}): {Look.Save()}");
        }

        public void RemoveAll()
        {
            KeepForUndo();
            Look.Clear();
            GameAudio.Sfx("dress_remove");
            ShowToast(DressStrings.AllRemoved);
            _saveLook = true;
        }

        /// <summary>For a few seconds after a surprise or "remove all" the remove bubble brings the look back: an arm
        /// raised to admire the outfit may rest on those bubbles, and the look was lost.</summary>
        public bool CanUndo => _undoLook != null && Time.unscaledTime < _undoUntil;

        string _undoLook;
        float _undoUntil;

        void KeepForUndo()
        {
            _undoLook = Look.Save();
            _undoUntil = Time.unscaledTime + 6f;
        }

        public void Undo()
        {
            if (!CanUndo) return;
            Look.CopyFrom(DressLook.Load(_undoLook, Progress.Finishes));
            _undoLook = null;
            for (int s = 0; s < DressCatalog.SlotCount; s++)
            {
                if (!Look.IsWorn((Slot)s)) continue;
                _flash[s] = 1f;
                _pop[s] = -0.04f * s;
                _poofPending[s] = true;
            }
            GameAudio.Sfx("dress_surprise", 0.8f, 0.9f);
            ShowToast(DressStrings.LookBack);
            _saveLook = true;
            Debug.Log($"[Dress] Undo: {Look.Save()}");
        }

        public void SwapHands()
        {
            RightHandHolds = !RightHandHolds;
            _outfit.RightHandHolds = RightHandHolds;
            PlayerPrefs.SetInt(HandKey, RightHandHolds ? 1 : 0);
            PlayerPrefs.Save();
            GameAudio.Sfx("paint_pick");
        }

        void ShowToast(string text)
        {
            _toast = text;
            _toastUntil = Time.unscaledTime + 2.2f;
        }

        // ---------------------------------------------------------------- the photo

        public void StartPhoto()
        {
            if (_countdown >= 0f) return;
            _countdown = 3f;
            GameAudio.Sfx("count", 0.9f, 1f, 0f);
        }

        /// <summary>
        /// The camera picture with the outfit and its sparkles (not the buttons), as a PNG on the box only. The picture
        /// is shown as a polaroid for a few seconds (players only saw a line of text before), and the PNG is encoded on
        /// another thread (on the box's cores the encode stalled the game for a moment).
        /// </summary>
        public void TakePhoto()
        {
            const int w = 1920, h = 1080;
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var old = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = old;
            if (_photoPreview == null)
            {
                _photoPreview = new RenderTexture(640, 360, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "Photo preview" };
                _photoPreview.Create();
            }
            Graphics.Blit(rt, _photoPreview);
            _photoShownAt = Time.unscaledTime;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            var active = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(rt);
            byte[] raw = tex.GetRawTextureData();
            var format = tex.graphicsFormat;
            Destroy(tex);
            string path = Path.Combine(PhotoDir, $"tundu-vishu-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
            string dir = PhotoDir;
            int keep = keepPhotos;
            LastPhoto = path;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    byte[] png = ImageConversion.EncodeArrayToPNG(raw, format, (uint)w, (uint)h);
                    Directory.CreateDirectory(dir);
                    File.WriteAllBytes(path, png);
                    var all = Directory.GetFiles(dir, "*.png").OrderBy(f => File.GetLastWriteTimeUtc(f)).ToList();
                    for (int i = 0; i < all.Count - keep; i++) File.Delete(all[i]);
                    _photoCount = Math.Min(all.Count, keep);
                    Debug.Log($"[Dress] Photo saved: {path}");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Dress] Photo not saved: {e.Message}");
                }
            });
            _photoCount = Math.Min(_photoCount + 1, keepPhotos);
            GameAudio.Sfx("paint_photo", vary: 0f);
            _flashScreen = 1f;
        }

        RenderTexture _photoPreview;
        float _photoShownAt = -99f;
        const float PhotoShowSeconds = 3.4f;

        /// <summary>The photo just taken, as a polaroid: it pops up tilted, stays a moment, then flies to the corner.</summary>
        void DrawPhotoPreview(float w)
        {
            float age = Time.unscaledTime - _photoShownAt;
            if (_photoPreview == null || age < 0f || age > PhotoShowSeconds || !WordsPass) return;   // over everything: IMGUI's
            float pop = Back01(Mathf.Clamp01(age / 0.35f));
            float leave = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((age - (PhotoShowSeconds - 0.6f)) / 0.6f));
            float scale = Mathf.Lerp(0.6f, 1f, pop) * Mathf.Lerp(1f, 0.25f, leave);
            Vector2 center = Vector2.Lerp(new Vector2(w * 0.5f, 520f), new Vector2(w - 140f, 980f), leave);
            float pw = 640f * scale, ph = 360f * scale, border = 22f * scale;
            var frame = new Rect(center.x - pw * 0.5f - border, center.y - ph * 0.5f - border, pw + border * 2f, ph + border * 2f + 70f * scale);
            var oldMatrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(-4f * (1f - leave), center);
            PanelNow(new Rect(frame.x + 8f, frame.y + 14f, frame.width, frame.height), new Color(0f, 0f, 0f, 0.35f * (1f - leave)));
            PanelNow(frame, new Color(1f, 0.99f, 0.97f, 1f));
            if (Event.current.type == EventType.Repaint)
                GUI.DrawTexture(new Rect(frame.x + border, frame.y + border, pw, ph), _photoPreview, ScaleMode.StretchToFill, false);
            if (scale > 0.5f)
            {
                var caption = new GUIStyle(_medium) { normal = { textColor = new Color(0.33f, 0.2f, 0.42f) }, alignment = TextAnchor.MiddleCenter };
                GUI.Label(new Rect(frame.x, frame.y + border + ph + 8f * scale, frame.width, 60f * scale), DressStrings.PhotoSaved(_photoCount), caption);
            }
            GUI.matrix = oldMatrix;
        }

        // ---------------------------------------------------------------- the fashion show

        public void StartShow()
        {
            var themes = Enumerable.Range(0, DressCatalog.ThemeCount).Select(t => (Theme)t).Where(t => t != _lastTheme).ToList();
            ShowTheme = themes[_rng.Next(themes.Count)];
            _lastTheme = ShowTheme;
            SetMode(Mode.ShowIntro);
            GameAudio.Sfx("dress_show", vary: 0f);
            Debug.Log($"[Dress] Show: theme {ShowTheme}");
        }

        /// <summary>The runway: <paramref name="poses"/> in this order (tests), or a few random ones.</summary>
        public void StartWalk(params ShowPose[] poses)
        {
            var all = Enum.GetValues(typeof(ShowPose)).Cast<ShowPose>().OrderBy(_ => _rng.Next()).ToArray();
            Poses = poses != null && poses.Length > 0 ? poses : all.Take(Mathf.Clamp(posesPerShow, 1, all.Length)).ToArray();
            PoseScores = new float[Poses.Length];
            PoseIndex = 0;
            _runwayPhoto = false;
            NewPose();
            SetMode(Mode.ShowWalk);
            GameAudio.PlayMusic("runway", 0.6f);
            GameAudio.Sfx("dress_curtain", vary: 0f);
            Debug.Log($"[Dress] Runway: {string.Join(", ", Poses)}");
        }

        void NewPose()
        {
            _poseHeld = 0f;
            _poseBest = 0f;
            _poseDone = false;
            _poseDoneAt = -1f;
            _modeTime = 0f;
        }

        void SetMode(Mode mode)
        {
            CurrentMode = mode;
            _modeTime = 0f;
            _layoutDirty = true;
            if (mode == Mode.Mirror || mode == Mode.ShowIntro || mode == Mode.ShowDressing) GameAudio.PlayMusic("dress", 0.8f);
        }

        void UpdateMode(Body body, float dt)
        {
            if (_countdown >= 0f)
            {
                float before = _countdown;
                _countdown -= dt;
                if (Mathf.Ceil(before) != Mathf.Ceil(_countdown) && _countdown > 0f) GameAudio.Sfx("count", 0.9f, 1f, 0f);
                if (_countdown <= 0f)
                {
                    _countdown = -1f;
                    TakePhoto();
                }
            }
            switch (CurrentMode)
            {
                case Mode.ShowIntro:
                    if (_modeTime > 3.6f) SetMode(Mode.ShowDressing);
                    break;
                case Mode.ShowDressing:
                {
                    float left = dressSeconds - _modeTime;
                    if (left <= 10f && Mathf.Floor(left) != Mathf.Floor(left + dt) && left > 0f) GameAudio.Sfx("dress_tick", 0.6f, 1f, 0f);
                    if (left <= 0f) StartWalk();
                    break;
                }
                case Mode.ShowWalk:
                    UpdateWalk(body, dt);
                    break;
                case Mode.ShowJudges:
                {
                    // a judge every 2.2 s, a star every 0.25 s after it appears
                    for (int j = 0; j < Verdicts.Length; j++)
                    {
                        for (int s = 1; s <= Verdicts[j].Stars; s++)
                        {
                            float at = 0.6f + j * 2.2f + s * 0.25f;
                            if (_modeTime >= at && _modeTime - dt < at) GameAudio.Sfx("dress_star", 0.8f, 0.85f + 0.08f * s, 0f);
                        }
                    }
                    if (_modeTime > 0.6f + Verdicts.Length * 2.2f + 1.6f) ShowResults();
                    break;
                }
                case Mode.ShowResults:
                    if (_modeTime > 20f && Time.unscaledTime - _lastActivity > 12f) SetMode(Mode.Mirror);
                    break;
            }
        }

        void UpdateWalk(Body body, float dt)
        {
            if (_modeTime < 1.2f) return;   // the curtains open
            var pose = Poses[PoseIndex];
            float score = _doll ? 0f : PoseCheck.Score(pose, _pose.J, _pose.T);
            if (!_poseDone)
            {
                _poseBest = Mathf.Max(_poseBest, score);
                _poseHeld = score >= PoseCheck.Pass ? _poseHeld + dt : Mathf.Max(0f, _poseHeld - dt * 2f);
                bool held = _poseHeld >= poseHold;
                bool timeUp = _modeTime > 1.2f + poseSeconds;
                if (held || timeUp || RemoteInput.Confirm)
                {
                    _poseDone = true;
                    _poseDoneAt = _modeTime;
                    bool counted = held || RemoteInput.Confirm;
                    PoseScores[PoseIndex] = counted ? Mathf.Max(0.75f, _poseBest) : _poseBest * 0.6f;
                    if (counted)
                    {
                        GameAudio.Sfx("dress_pose", vary: 0f);
                        _fx.Burst(_pose.HipC * 0.5f + _pose.ShoulderC * 0.5f, 1.2f);
                        _flashScreen = 0.6f;
                        if (!_runwayPhoto)
                        {
                            _runwayPhoto = true;
                            TakePhoto();
                        }
                    }
                    else GameAudio.Sfx("ui_back", 0.7f);
                    Debug.Log($"[Dress] Pose {pose}: {(counted ? "held" : "missed")}, best {_poseBest:0.00}");
                }
            }
            else if (_modeTime - _poseDoneAt > 1.4f)
            {
                PoseIndex++;
                if (PoseIndex >= Poses.Length) StartJudges();
                else
                {
                    NewPose();
                    _modeTime = 1.2f;
                }
            }
        }

        /// <summary>Current pose's score (0..1) and how long it has been held (for the meter).</summary>
        public float PoseMeter => Mathf.Clamp01(_poseHeld / Mathf.Max(0.01f, poseHold));

        void StartJudges()
        {
            float poses = PoseScores.Length > 0 ? PoseScores.Average() : 0f;
            Verdicts = ShowJudge.Judge(Look, ShowTheme, poses);
            ShowStars = ShowJudge.Total(Verdicts);
            SetMode(Mode.ShowJudges);
            Debug.Log($"[Dress] Judges ({ShowTheme}, poses {poses:0.00}): {string.Join(", ", Verdicts.Select(v => $"{v.Judge} {v.Stars} {v.Remark}"))} = {ShowStars}");
        }

        void ShowResults()
        {
            _unlocked = Progress.AddShow(ShowStars);
            PlayerPrefs.SetString(ProgressKey, Progress.Save());
            PlayerPrefs.Save();
            SetMode(Mode.ShowResults);
            GameAudio.Jingle("jingle_win");
            _fx.Confetti(_guiWidth, 140);
            _lastActivity = Time.unscaledTime;
            Debug.Log($"[Dress] Results: {ShowStars} stars, total {Progress.Stars}, unlocked {string.Join(", ", _unlocked)}");
        }

        // ---------------------------------------------------------------- keys and remote

        void HandleKeys()
        {
            var kb = Keyboard.current;
            bool interactive = CurrentMode == Mode.Mirror || CurrentMode == Mode.ShowDressing || CurrentMode == Mode.ShowResults;
            if (kb != null && interactive)
            {
                for (int i = 0; i < DressCatalog.SlotCount; i++)
                    if (kb[Key.Digit1 + i].wasPressedThisFrame) SelectCategory((Slot)i);
                if (kb.fKey.wasPressedThisFrame) StartPhoto();
                if (kb.rKey.wasPressedThisFrame) SurpriseMe();
                if (kb.eKey.wasPressedThisFrame) NextMagic();
                if (kb.xKey.wasPressedThisFrame) RemoveAll();
                if (kb.zKey.wasPressedThisFrame) Undo();
                if (kb.hKey.wasPressedThisFrame) SwapHands();
                if (kb.sKey.wasPressedThisFrame)
                {
                    if (CurrentMode == Mode.ShowDressing) StartWalk();
                    else StartShow();
                }
            }
            if (!interactive || _buttons.Count == 0) return;
            Vector2 dir = RemoteInput.Left ? Vector2.left : RemoteInput.Right ? Vector2.right : RemoteInput.Up ? Vector2.down : RemoteInput.Down ? Vector2.up : Vector2.zero;
            if (dir != Vector2.zero)
            {
                if (!_remoteFocus) _remoteFocus = true;
                else MoveFocus(dir);
                GameAudio.Sfx("ui_move", 0.4f);
                _lastActivity = Time.unscaledTime;
            }
            if (RemoteInput.Confirm)
            {
                _remoteFocus = true;
                Press(_buttons[Mathf.Clamp(_focus, 0, _buttons.Count - 1)]);
            }
        }

        /// <summary>The remote's arrows: to the nearest bubble in that direction (GUI y grows downward).</summary>
        void MoveFocus(Vector2 dir)
        {
            var from = _buttons[Mathf.Clamp(_focus, 0, _buttons.Count - 1)].Center;
            int best = -1;
            float bestCost = float.MaxValue;
            for (int i = 0; i < _buttons.Count; i++)
            {
                Vector2 d = _buttons[i].Center - from;
                float along = Vector2.Dot(d, dir);
                if (along < 8f) continue;
                float cost = along + Mathf.Abs(dir.x != 0f ? d.y : d.x) * 2.5f;
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = i;
                }
            }
            if (best >= 0) _focus = best;
        }

        // ---------------------------------------------------------------- animation

        void Animate(float dt)
        {
            for (int s = 0; s < DressCatalog.SlotCount; s++)
            {
                _flash[s] = Mathf.Max(0f, _flash[s] - dt * 2.2f);
                _pop[s] = Mathf.Min(1f, _pop[s] + dt / 0.38f);
            }
            _flashScreen = Mathf.Max(0f, _flashScreen - dt * 2.5f);
        }

        // ---------------------------------------------------------------- scene objects

        Transform MakeQuad(string name, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var mesh = new Mesh { name = name };
            mesh.SetVertices(new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f) });
            var corners = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.SetUVs(0, corners);
            mesh.SetUVs(1, corners);   // 0..1 across the quad for the vignette (uv0 gets the picture's crop)
            mesh.SetColors(new[] { Color.white, Color.white, Color.white, Color.white });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        /// <summary>The dressing room when there is no camera picture: a night-blue to violet glow with a soft spotlight.</summary>
        static Texture2D MakeBackdrop()
        {
            const int w = 128, h = 72;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "Dressing room", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var top = new Color(0.09f, 0.10f, 0.24f);
            var bottom = new Color(0.36f, 0.20f, 0.48f);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float fy = y / (h - 1f), fx = x / (w - 1f);
                    var c = Color.Lerp(bottom, top, fy);
                    float spot = Mathf.Exp(-((fx - 0.5f) * (fx - 0.5f) * 9f + (fy - 0.45f) * (fy - 0.45f) * 6f));
                    c = Color.Lerp(c, new Color(0.98f, 0.72f, 0.88f), spot * 0.35f);
                    px[y * w + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply(false, true);
            return tex;
        }

        // ---------------------------------------------------------------- drawing

        /// <summary>
        /// The HUD. On the repaint its pictures (bubbles, icons, panels, the mirror's frame and bulbs) are collected in a
        /// first pass and drawn as one mesh (<see cref="HudBatch"/>), then a second pass draws the words, the item
        /// thumbnails and the cursors with IMGUI over them: one IMGUI draw per picture (~140 a frame) cost the box ~30 %
        /// of the main thread and most of the render thread. PerfLab's <c>hud=imgui</c> draws everything with IMGUI as
        /// before (to compare).
        /// </summary>
        void OnGUI()
        {
            if (PerfSwitches.Get("hud") == "off") return;   // PerfLab: the frame without its HUD (to measure)
            float w = GameHud.Begin();
            EnsureStyles();
            if (_hud != null && Event.current.type == EventType.Repaint && PerfSwitches.Get("hud") != "imgui")
            {
                _pass = HudPass.Pictures;
                _hud.Clear();
                DrawHud(w);
                _hud.Draw(GUI.matrix);
                _pass = HudPass.Words;
                DrawHud(w);
            }
            else
            {
                _pass = HudPass.Everything;
                DrawHud(w);
            }
            _pass = HudPass.Everything;
        }

        enum HudPass { Everything, Pictures, Words }
        HudPass _pass;
        HudBatch _hud;
        Texture2D _shapes;

        /// <summary>This pass draws the HUD's pictures and panels (collected for the batch, or with IMGUI).</summary>
        bool PicturesPass => _pass != HudPass.Words;
        /// <summary>This pass draws the words and IMGUI's own pieces (thumbnails, bars, the cursors, the photo).</summary>
        bool WordsPass => _pass != HudPass.Pictures;

        void DrawHud(float w)
        {
            var brand = Brand.Current;
            if (_feed == null || !_feed.IsLive)
            {
                if (MotionSystem.Instance != null && MotionSystem.Instance.IsConnected)
                    Shadowed(new Rect(0, 1000, w, 50), DressStrings.WaitingCamera, _small);
            }
            if (CurrentMode == Mode.Mirror || CurrentMode == Mode.ShowDressing || CurrentMode == Mode.ShowIntro) DrawMirrorFrame(w);
            switch (CurrentMode)
            {
                case Mode.Mirror:
                case Mode.ShowDressing:
                    DrawWardrobe(w, brand);
                    if (CurrentMode == Mode.ShowDressing) DrawShowBanner(w, brand);
                    break;
                case Mode.ShowIntro: DrawShowIntro(w, brand); break;
                case Mode.ShowWalk: DrawRunway(w, brand); break;
                case Mode.ShowJudges: DrawJudges(w, brand); break;
                case Mode.ShowResults: DrawResults(w, brand); break;
            }
            DrawCursor(_left);
            DrawCursor(_right);
            if (CurrentMode == Mode.Mirror && Time.unscaledTime < _introUntil) DrawIntro(w, brand);
            if (Time.unscaledTime < _toastUntil && !string.IsNullOrEmpty(_toast))
            {
                float age = 2.2f - (_toastUntil - Time.unscaledTime);
                float pop = Mathf.LerpUnclamped(0.7f, 1f, age < 0.25f ? Back01(age / 0.25f) : 1f);   // never smaller than its rounded corners
                float fade = Mathf.Clamp01((_toastUntil - Time.unscaledTime) / 0.3f);
                float tw = Mathf.Clamp(TextWidth(_medium, _toast) + 130f, 360f, 1100f);
                var r = new Rect(w * 0.5f - tw * 0.5f * pop, 940f - 40f * pop + 40f, tw * pop, 80f * pop);
                Color rim = SlotColors[(int)Category];
                Panel(Grow(r, 4f), new Color(rim.r, rim.g, rim.b, 0.9f * fade));
                Panel(r, new Color(0.07f, 0.06f, 0.18f, 0.9f * fade));
                UiImage(new Rect(r.x + 22f * pop, r.center.y - 22f * pop, 44f * pop, 44f * pop), "act_magic", new Color(1f, 1f, 1f, fade));
                if (pop > 0.95f) Shadowed(new Rect(r.x + 50f, r.y, r.width - 70f, r.height), _toast, _medium, new Color(1f, 1f, 1f, fade));
            }
            else DrawStatus(w);
            if (_countdown > 0f)
            {
                float frac = _countdown - Mathf.Floor(_countdown);
                float s = 1f + frac * 0.4f;
                var r = new Rect(w * 0.5f - 160f * s, 380f - 160f * s, 320f * s, 320f * s);
                Shadowed(r, Mathf.CeilToInt(_countdown).ToString(), _big, brand.PrimaryColor);
            }
            DrawPhotoPreview(w);
            if (_flashScreen > 0f) Box(new Rect(0, 0, w, 1080f), new Color(1f, 1f, 1f, Mathf.Clamp01(_flashScreen) * 0.8f));
        }

        static readonly Color[] SlotColors =
        {
            new Color(1f, 0.49f, 0.78f), new Color(0.69f, 0.55f, 1f), new Color(1f, 0.79f, 0.24f), new Color(1f, 0.62f, 0.11f),
            new Color(0.43f, 0.76f, 1f), new Color(0.39f, 0.84f, 0.65f), new Color(1f, 0.42f, 0.35f),
        };

        static readonly string[] SlotIcons = { "cat_top", "cat_bottom", "cat_hair", "cat_head", "cat_face", "cat_back", "cat_hand" };

        void DrawWardrobe(float w, Brand brand)
        {
            // soft panels behind the two columns (the HUD is drawn twice a frame: no lists made here)
            Button firstCat = null, lastCat = null, firstItem = null, lastItem = null;
            foreach (var b in _buttons)
            {
                if (b.Kind == Kind.Slot)
                {
                    firstCat ??= b;
                    lastCat = b;
                }
                else if (b.Kind == Kind.Item)
                {
                    firstItem ??= b;
                    lastItem = b;
                }
            }
            if (firstCat != null) ColumnPanel(firstCat, lastCat);
            if (firstItem != null) ColumnPanel(firstItem, lastItem);
            for (int i = 0; i < _buttons.Count; i++) DrawButton(_buttons[i], i == _focus && _remoteFocus, brand);
            // the category's name above the items, in its colour
            if (firstItem != null)
            {
                var first = firstItem;
                string name = DressStrings.SlotName(Category);
                float pw = Mathf.Max(first.Radius * 2f + 24f, TextWidth(_pill, name) + 44f);
                var pill = new Rect(first.Center.x - pw * 0.5f, first.Center.y - first.Radius - 70f, pw, 46f);
                Color c = SlotColors[(int)Category];
                Panel(new Rect(pill.x, pill.y + 4f, pill.width, pill.height), new Color(0f, 0f, 0f, 0.25f));
                Panel(pill, c);
                Shadowed(pill, name, _pill);
            }
            DrawBadge();
        }

        /// <summary>The game's name in the top-left corner, like a sign on the dressing-room mirror.</summary>
        void DrawBadge()
        {
            var r = new Rect(34f, 30f, TextWidth(_pill, DressStrings.Title) + 48f, 50f);
            Panel(Grow(r, 3f), new Color(1f, 1f, 1f, 0.22f));
            Panel(r, new Color(0.07f, 0.06f, 0.18f, 0.72f));
            Shadowed(r, DressStrings.Title, _pill, Brand.Current.PrimaryColor);
        }

        /// <summary>
        /// The camera picture as a dressing-room mirror: a thin glowing frame, warm bulbs around it that twinkle now and
        /// then. The picture's darker edges (the player and the clothes stand out) are drawn with the picture itself
        /// (Tundu/Fx _VIGNETTE, <see cref="UpdatePicture"/>): as a full-screen layer over everything they cost the
        /// box's GPU a whole extra pass every frame.
        /// </summary>
        void DrawMirrorFrame(float w)
        {
            if (Event.current.type != EventType.Repaint || !PicturesPass) return;
            if (_ring == null) MakeRing();
            var frameColor = new Color(1f, 0.95f, 0.88f, 0.45f);
            if (_pass == HudPass.Pictures)
                _hud.Nine(new Rect(14f, 14f, w - 28f, 1052f), RingBorder, RingUv, new Vector2(RingBorder / (float)ShapesWidth, RingBorder / (float)ShapesHeight),
                          frameColor, middle: false);
            else
            {
                var old = GUI.color;
                GUI.color = frameColor;
                FrameEdges(new Rect(14f, 14f, w - 28f, 1052f), _ringTex, _ring.border.left);
                GUI.color = old;
            }
            float t = Time.unscaledTime;
            int n = 0;
            void Bulb(float x, float y)
            {
                float twinkle = 0.5f + 0.5f * Mathf.Sin(t * 1.3f + n * 2.39f);
                twinkle = 0.7f + 0.3f * twinkle * twinkle;
                n++;
                UiImage(new Rect(x - 34f, y - 34f, 68f, 68f), "glow", new Color(1f, 0.85f, 0.55f, 0.4f * twinkle));
                UiImage(new Rect(x - 11f, y - 11f, 22f, 22f), "bulb", new Color(1f, 0.97f, 0.88f, twinkle));
            }
            const float Gap = 132f;
            int across = Mathf.Max(2, Mathf.RoundToInt((w - 220f) / Gap));
            for (int k = 0; k <= across; k++) Bulb(110f + (w - 220f) * k / across, 14f);
            for (int k = 1; k <= 7; k++)
            {
                Bulb(14f, 14f + k * 132f);
                Bulb(w - 14f, 14f + k * 132f);
            }
        }

        GUIStyle _ring;
        Texture2D _ringTex;

        /// <summary>
        /// A 9-slice frame without its middle: the 4 corners and 4 edges of <paramref name="tex"/> (border px wide)
        /// around <paramref name="r"/>. As a GUI.Box the clear middle was still blended over the whole screen.
        /// </summary>
        static void FrameEdges(Rect r, Texture2D tex, int border)
        {
            float b = border, u = border / (float)tex.width, v = border / (float)tex.height;
            float midW = r.width - 2f * b, midH = r.height - 2f * b;
            // texture coordinates start at the bottom: the top row of the screen is the top band (1 - v .. 1)
            void Piece(float x, float y, float pw, float ph, float u0, float v0, float u1, float v1) =>
                GUI.DrawTextureWithTexCoords(new Rect(x, y, pw, ph), tex, new Rect(u0, v0, u1 - u0, v1 - v0), true);
            Piece(r.x, r.y, b, b, 0f, 1f - v, u, 1f);                                  // top left
            Piece(r.x + b, r.y, midW, b, u, 1f - v, 1f - u, 1f);                       // top
            Piece(r.xMax - b, r.y, b, b, 1f - u, 1f - v, 1f, 1f);                      // top right
            Piece(r.x, r.y + b, b, midH, 0f, v, u, 1f - v);                            // left
            Piece(r.xMax - b, r.y + b, b, midH, 1f - u, v, 1f, 1f - v);                // right
            Piece(r.x, r.yMax - b, b, b, 0f, 0f, u, v);                                // bottom left
            Piece(r.x + b, r.yMax - b, midW, b, u, 0f, 1f - u, v);                     // bottom
            Piece(r.xMax - b, r.yMax - b, b, b, 1f - u, 0f, 1f, v);                    // bottom right
        }

        void MakeRing()
        {
            const int Size = 64;
            _ringTex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "Ring", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    px[y * Size + x] = new Color32(255, 255, 255, RingAlpha(x, y));
            _ringTex.SetPixels32(px);
            _ringTex.Apply(false, true);
            _ring = new GUIStyle { normal = { background = _ringTex }, border = new RectOffset(RingBorder, RingBorder, RingBorder, RingBorder) };
        }

        /// <summary>The mirror frame's line with its glow (64 x 64, a 9-slice with 26 px corners).</summary>
        static byte RingAlpha(int x, int y)
        {
            const int Size = 64, Radius = RingBorder;
            const float Line = 2.5f, Glow = 5f;
            float dx = Mathf.Max(0f, Mathf.Max(Radius - x - 0.5f, x + 0.5f - (Size - Radius)));
            float dy = Mathf.Max(0f, Mathf.Max(Radius - y - 0.5f, y + 0.5f - (Size - Radius)));
            float edge = Mathf.Abs(Radius - Glow - Mathf.Sqrt(dx * dx + dy * dy));   // distance to the line
            float a = Mathf.Clamp01(Line - edge + 0.5f) + 0.35f * Mathf.Clamp01(1f - edge / Glow);
            return (byte)(Mathf.Clamp01(a) * 255);
        }

        /// <summary>A panel's rounded rectangle (64 x 64, 24 px corners).</summary>
        static byte PanelAlpha(int x, int y)
        {
            const int Size = 64, Radius = 24;
            float dx = Mathf.Max(0f, Mathf.Max(Radius - x - 0.5f, x + 0.5f - (Size - Radius)));
            float dy = Mathf.Max(0f, Mathf.Max(Radius - y - 0.5f, y + 0.5f - (Size - Radius)));
            float a = Mathf.Clamp01(Radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
            return (byte)(a * 255);
        }

        // The batch's shapes texture: the panel at x 0..63 and the frame's ring at x 72..135, the gap between them filled
        // with copies of their edge columns, so each filters at its edges like IMGUI's clamped textures.
        const int ShapesWidth = 136, ShapesHeight = 64, RingX = 72, RingBorder = 26;
        static readonly Rect PanelUv = new Rect(0f, 0f, 64f / ShapesWidth, 1f);
        static readonly Rect RingUv = new Rect(RingX / (float)ShapesWidth, 0f, 64f / ShapesWidth, 1f);

        static Texture2D MakeShapes()
        {
            var tex = new Texture2D(ShapesWidth, ShapesHeight, TextureFormat.RGBA32, false) { name = "HUD shapes", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[ShapesWidth * ShapesHeight];
            for (int y = 0; y < ShapesHeight; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    px[y * ShapesWidth + x] = new Color32(255, 255, 255, PanelAlpha(x, y));
                    px[y * ShapesWidth + RingX + x] = new Color32(255, 255, 255, RingAlpha(x, y));
                }
                for (int x = 64; x < RingX; x++)
                    px[y * ShapesWidth + x] = x < (64 + RingX) / 2 ? px[y * ShapesWidth + 63] : px[y * ShapesWidth + RingX];
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        static float Back01(float t)
        {
            t = Mathf.Clamp01(t) - 1f;
            return t * t * (2.7f * t + 1.7f) + 1f;
        }

        void ColumnPanel(Button first, Button last)
        {
            float r = first.Radius;
            float x = first.Center.x, y0 = first.Center.y - r - 12f, y1 = last.Center.y + r + 12f;
            var rect = new Rect(x - r - 14f, y0, 2f * r + 28f, y1 - y0);
            Panel(Grow(rect, 3f), new Color(1f, 1f, 1f, 0.2f));
            Panel(rect, new Color(0.07f, 0.07f, 0.2f, 0.55f));
        }

        void DrawButton(Button b, bool focused, Brand brand)
        {
            float progress = Mathf.Max(Progress01(_left, b), Progress01(_right, b));
            bool selected = b.Kind == Kind.Slot && (Slot)b.Index == Category;
            bool worn = b.Kind == Kind.Item && Look.Item[(int)Category] == b.Index && b.Index >= 0;
            float r = b.Radius * (selected || worn ? 1.06f : 1f) * (1f + 0.08f * progress);
            var rect = new Rect(b.Center.x - r, b.Center.y - r, 2f * r, 2f * r);
            Color bubble = b.Kind switch
            {
                Kind.Slot => SlotColors[b.Index],
                Kind.Item => new Color(0.97f, 0.95f, 1f),
                Kind.Show or Kind.Ready or Kind.Again => brand.PrimaryColor,
                Kind.Photo => new Color(0.43f, 0.76f, 1f),
                Kind.Remove or Kind.Leave => new Color(0.75f, 0.72f, 0.85f),
                _ => new Color(0.69f, 0.55f, 1f),
            };
            if (selected || worn) UiImage(Grow(rect, 14f), "glow", worn ? new Color(1f, 0.86f, 0.4f, 0.95f) : new Color(1f, 1f, 1f, 0.9f));
            UiImage(new Rect(rect.x + 1f, rect.y + 6f, rect.width, rect.height), "bubble_button", new Color(0f, 0f, 0.05f, 0.3f));
            UiImage(rect, "bubble_button", bubble);
            switch (b.Kind)
            {
                case Kind.Slot:
                    UiImage(Shrink(rect, 0.56f), SlotIcons[b.Index], Color.white);
                    break;
                case Kind.Item:
                    if (b.Index < 0) UiImage(Shrink(rect, 0.5f), "none", new Color(0.55f, 0.5f, 0.68f));
                    else DrawThumb(Shrink(rect, 0.84f), Category, b.Index);
                    if (worn) DrawShades(rect, Category);
                    break;
                default:
                    UiImage(Shrink(rect, 0.52f), b.Kind switch
                    {
                        Kind.Magic => "act_magic", Kind.Surprise => "act_dice", Kind.Show => "act_show", Kind.Ready or Kind.Again => "act_ready",
                        Kind.Photo => "act_photo", Kind.Leave => "cat_top", Kind.Remove when CanUndo => _art.HasUi("act_undo") ? "act_undo" : "act_ready", _ => "act_remove",
                    }, Color.white);
                    string label = b.Kind switch
                    {
                        Kind.Magic => DressStrings.Magic, Kind.Surprise => DressStrings.Surprise, Kind.Show => DressStrings.Show,
                        Kind.Ready => DressStrings.Ready, Kind.Photo => DressStrings.Photo, Kind.Again => DressStrings.AnotherShow,
                        Kind.Leave => DressStrings.BackToMirror, Kind.Remove when CanUndo => DressStrings.Undo, _ => DressStrings.RemoveAll,
                    };
                    float lw = TextWidth(_label, label) + 30f;
                    var pill = new Rect(b.Center.x - lw * 0.5f, rect.yMax + 2f, lw, 32f);
                    Panel(pill, new Color(0.07f, 0.06f, 0.18f, 0.62f));
                    Shadowed(pill, label, _label);
                    break;
            }
            if (progress > 0f) DwellRing(b.Center, r + 8f, progress, brand.PrimaryColor);
            if (focused) DwellRing(b.Center, r + 8f, 1f, Color.white);
        }

        /// <summary>An item's picture, coloured like it would be worn (Tundu/Outfit on the thumbnail sheet).</summary>
        void DrawThumb(Rect r, Slot slot, int index)
        {
            if (Event.current.type != EventType.Repaint || !WordsPass) return;
            var item = DressCatalog.Items[(int)slot][index];
            int color = Look.Item[(int)slot] == index ? Look.ColorOf(slot) : item.Colors[0];
            DressCatalog.Rgb(color, out float cr, out float cg, out float cb);
            var uv = _art.ThumbRect(item.Id);
            _thumbMaterial.SetVector("_Tint", new Vector4(cr, cg, cb, 1f));
            Graphics.DrawTexture(r, _art.Thumbs, uv, 0, 0, 0, 0, Color.white, _thumbMaterial);
        }

        /// <summary>Little dots under the worn item: its colours, the chosen one bigger (press again for the next).</summary>
        void DrawShades(Rect rect, Slot slot)
        {
            var item = Look.Worn(slot);
            if (item == null) return;
            int n = DressLook.Choices(item, Progress.Finishes);
            float d = 12f, gap = 3f, total = n * d + (n - 1) * gap;
            float x = rect.center.x - total * 0.5f, y = rect.yMax - 2f;
            for (int k = 0; k < n; k++)
            {
                Color c;
                if (k < item.Colors.Length)
                {
                    DressCatalog.Rgb(item.Colors[k], out float r, out float g, out float b);
                    c = new Color(r, g, b);
                }
                else c = Color.HSVToRGB((Time.unscaledTime * 0.3f + k * 0.2f) % 1f, 0.5f, 1f);
                bool on = Look.Shade[(int)slot] == k;
                float s = on ? d * 1.5f : d;
                UiImage(new Rect(x + k * (d + gap) + d * 0.5f - s * 0.5f, y - s * 0.5f, s, s), "bubble_button", c);
            }
        }

        void DwellRing(Vector2 c, float radius, float progress, Color color)
        {
            const int Dots = 18;
            for (int k = 0; k < Dots; k++)
            {
                float f = k / (float)Dots;
                float a = -Mathf.PI * 0.5f + f * Mathf.PI * 2f;
                bool lit = f < progress;
                float s = lit ? 16f : 9f;
                var p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                UiImage(new Rect(p.x - s * 0.5f, p.y - s * 0.5f, s, s), "bulb", lit ? color : new Color(1f, 1f, 1f, 0.25f));
            }
        }

        void DrawCursor(Hand hand)
        {
            if (!hand.Visible || !WordsPass) return;   // over everything: IMGUI's, after the batch
            var p = hand.Position;
            float pulse = 1f + 0.06f * Mathf.Sin(Time.unscaledTime * 8f);
            UiImageNow(new Rect(p.x - 34f * pulse, p.y - 34f * pulse, 68f * pulse, 68f * pulse), "glow", new Color(hand.Tint.r, hand.Tint.g, hand.Tint.b, 0.8f));
            UiImageNow(new Rect(p.x - 20f, p.y - 20f, 40f, 40f), "act_magic", Color.white);
        }

        void DrawIntro(float w, Brand brand)
        {
            var r = new Rect(w * 0.5f - 560f, 300f, 1120f, 300f);
            Panel(r, new Color(0.08f, 0.1f, 0.22f, 0.86f));
            Shadowed(new Rect(r.x, r.y + 16f, r.width, 100f), DressStrings.Title, _big, brand.PrimaryColor);
            Shadowed(new Rect(r.x + 30f, r.y + 120f, r.width - 60f, 50f), DressStrings.Tagline, _small);
            Shadowed(new Rect(r.x + 30f, r.y + 172f, r.width - 60f, 50f), DressStrings.HowTo, _small);
            Shadowed(new Rect(r.x + 30f, r.y + 222f, r.width - 60f, 50f), DressStrings.HowToColor, _small);
        }

        void DrawStatus(float w)
        {
            var motion = MotionSystem.Instance;
            string status = motion == null || !motion.IsConnected
                ? CommonStrings.T("Tastiera: 1-7 kategoritë, shigjetat + ENTER, F foto, R surprizë, E magji, S sfilatë", "Keys: 1-7 categories, arrows + ENTER, F photo, R surprise, E magic, S show")
                : motion.ActiveBody == null ? DressStrings.NoPlayer : null;
            if (status == null || CurrentMode == Mode.ShowWalk || CurrentMode == Mode.ShowJudges) return;
            var r = new Rect(w * 0.5f - 760f, 1010f, 1520f, 56f);
            Panel(r, new Color(0f, 0f, 0f, 0.45f));
            Shadowed(r, status, _label);
        }

        void DrawShowBanner(float w, Brand brand)
        {
            float left = Mathf.Max(0f, dressSeconds - _modeTime);
            var r = new Rect(w * 0.5f - 300f, 840f, 600f, 86f);
            Panel(r, new Color(0.08f, 0.06f, 0.16f, 0.72f));
            UiImage(new Rect(r.x + 12f, r.y + 8f, 70f, 70f), "theme_" + ThemeKey(ShowTheme), Color.white);
            Shadowed(new Rect(r.x + 90f, r.y + 4f, r.width - 190f, 46f), DressStrings.ThemeIs(DressStrings.ThemeName(ShowTheme)), _medium, brand.PrimaryColor);
            Shadowed(new Rect(r.x + 90f, r.y + 46f, r.width - 190f, 34f), DressStrings.ReadyHint, _label);
            var timer = new Rect(r.xMax - 96f, r.y + 8f, 84f, 70f);
            Shadowed(timer, Mathf.CeilToInt(left).ToString(), _medium, left <= 10f ? new Color(1f, 0.45f, 0.4f) : Color.white);
            Box(new Rect(r.x + 8f, r.yMax - 8f, (r.width - 16f) * (left / dressSeconds), 5f), brand.PrimaryColor);
        }

        void DrawShowIntro(float w, Brand brand)
        {
            float t = Mathf.Clamp01(_modeTime / 0.4f);
            var r = new Rect(w * 0.5f - 500f, 200f + (1f - t) * 80f, 1000f, 520f);
            Panel(r, new Color(0.08f, 0.06f, 0.18f, 0.9f * t));
            Shadowed(new Rect(r.x, r.y + 20f, r.width, 110f), DressStrings.ShowTitle, _big, brand.PrimaryColor);
            float s = 220f * (1f + 0.04f * Mathf.Sin(_modeTime * 6f));
            UiImage(new Rect(w * 0.5f - s * 0.5f, r.y + 140f, s, s), "theme_" + ThemeKey(ShowTheme), Color.white);
            Shadowed(new Rect(r.x, r.y + 370f, r.width, 70f), DressStrings.ThemeIs(DressStrings.ThemeName(ShowTheme)), _medium);
            Shadowed(new Rect(r.x, r.y + 440f, r.width, 50f), DressStrings.DressFor(Mathf.RoundToInt(dressSeconds)), _small);
        }

        void DrawRunway(float w, Brand brand)
        {
            // the curtains open over the first second, spotlights sweep
            float open = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_modeTime / 1.2f));
            float cw = 300f, ch = 1080f;
            var curtain = _art.UiRect("curtain");
            float lx = Mathf.Lerp(w * 0.5f - cw, -cw * 0.45f, open);
            float rx = Mathf.Lerp(w * 0.5f, w - cw * 0.55f, open);
            UiPicture(new Rect(lx, 0, cw, ch), curtain, Color.white);
            UiPicture(new Rect(rx + cw, 0, -cw, ch), curtain, Color.white);
            if (open < 1f) return;
            float sweep = Mathf.Sin(Time.unscaledTime * 0.8f) * 120f;
            UiImage(new Rect(w * 0.5f - 520f + sweep, 120f, 1040f, 960f), "glow", new Color(1f, 0.95f, 0.8f, 0.12f));
            if (PoseIndex >= Poses.Length) return;
            var pose = Poses[PoseIndex];
            // the pose card on the right, the meter under it
            var card = new Rect(w - 360f, 190f, 300f, 420f);
            Panel(card, new Color(0.08f, 0.06f, 0.18f, 0.8f));
            Shadowed(new Rect(card.x, card.y + 10f, card.width, 40f), DressStrings.StrikePose, _label);
            float bob = _poseDone ? 1.1f : 1f + 0.05f * Mathf.Sin(Time.unscaledTime * 5f);
            UiImage(new Rect(card.center.x - 110f * bob, card.y + 60f, 220f * bob, 220f * bob), "pose_" + PoseKey(pose), _poseDone ? brand.PrimaryColor : Color.white);
            string poseName = DressStrings.PoseName(pose);
            Shadowed(new Rect(card.x, card.y + 290f, card.width, 60f), poseName,
                     TextWidth(_medium, poseName) > card.width - 24f ? _pill : _medium, brand.PrimaryColor);
            var meter = new Rect(card.x + 30f, card.y + 370f, card.width - 60f, 18f);
            Box(meter, new Color(1f, 1f, 1f, 0.2f));
            Box(new Rect(meter.x, meter.y, meter.width * (_poseDone ? 1f : PoseMeter), meter.height), brand.PrimaryColor);
            for (int k = 0; k < Poses.Length; k++)
                UiImage(new Rect(card.center.x - 45f + k * 32f, card.yMax + 14f, 26f, 26f), k < PoseIndex || (k == PoseIndex && _poseDone) ? "star_full" : "star_empty", Color.white);
            if (_poseDone)
            {
                string text = PoseScores[PoseIndex] >= 0.75f ? (PoseIndex % 2 == 0 ? DressStrings.Beautiful : DressStrings.Wonderful) : DressStrings.NeverMind;
                Shadowed(new Rect(0, 120f, w, 120f), text, _big, brand.PrimaryColor);
            }
            else if (_modeTime < 2.6f && PoseIndex == 0) Shadowed(new Rect(0, 120f, w, 120f), DressStrings.OnStage, _big, brand.PrimaryColor);
        }

        void DrawJudges(float w, Brand brand)
        {
            Shadowed(new Rect(0, 40f, w, 90f), DressStrings.JudgesSay, _medium, brand.PrimaryColor);
            string[] faces = { "judge_cat", "judge_bunny", "judge_dino" };
            float cardW = 420f, gap = 40f, x0 = w * 0.5f - (cardW * 3 + gap * 2) * 0.5f;
            for (int j = 0; j < Verdicts.Length; j++)
            {
                float appear = 0.6f + j * 2.2f;
                if (_modeTime < appear) continue;
                float t = Mathf.Clamp01((_modeTime - appear) / 0.35f);
                var v = Verdicts[j];
                var card = new Rect(x0 + j * (cardW + gap), 560f + (1f - t) * 300f, cardW, 470f);
                Panel(card, new Color(0.08f, 0.06f, 0.18f, 0.86f));
                UiImage(new Rect(card.center.x - 110f, card.y - 120f, 220f, 220f), faces[(int)v.Judge], Color.white);
                Shadowed(new Rect(card.x, card.y + 104f, card.width, 50f), DressStrings.JudgeName(v.Judge), _medium);
                for (int s = 1; s <= 5; s++)
                {
                    bool lit = s <= v.Stars && _modeTime >= appear + s * 0.25f;
                    float size = lit ? 68f : 56f;
                    UiImage(new Rect(card.x + 30f + (s - 1) * 74f + 34f - size * 0.5f, card.y + 190f - size * 0.5f, size, size), lit ? "star_full" : "star_empty", Color.white);
                }
                if (_modeTime >= appear + v.Stars * 0.25f + 0.2f)
                {
                    var bubble = new Rect(card.x + 20f, card.y + 250f, card.width - 40f, 180f);
                    Panel(bubble, new Color(1f, 1f, 1f, 0.95f));
                    if (WordsPass)
                    {
                        var style = new GUIStyle(_small) { normal = { textColor = new Color(0.2f, 0.14f, 0.3f) }, alignment = TextAnchor.MiddleCenter, wordWrap = true };
                        GUI.Label(Shrink(bubble, 0.9f), DressStrings.RemarkText(v.Remark, ShowTheme), style);
                    }
                }
            }
        }

        void DrawResults(float w, Brand brand)
        {
            var r = new Rect(w * 0.5f - 520f, 300f, 1040f, 470f);
            Panel(r, new Color(0.08f, 0.06f, 0.18f, 0.88f));
            Shadowed(new Rect(r.x, r.y + 20f, r.width, 110f), DressStrings.Total(ShowStars), _big, brand.PrimaryColor);
            for (int s = 0; s < 3; s++)
                UiImage(new Rect(w * 0.5f - 150f + s * 100f, r.y + 140f, 90f, 90f), "star_full", Color.white);
            string next = Progress.StarsToNext < 0 ? DressStrings.AllFinishes
                : DressStrings.StarsTo(Progress.StarsToNext, DressStrings.FinishName((Finish)(Progress.Finishes + 1)));
            if (_unlocked.Count > 0)
            {
                Shadowed(new Rect(r.x, r.y + 250f, r.width, 70f), DressStrings.Unlocked(string.Join(", ", _unlocked.Select(DressStrings.FinishName))), _medium, new Color(1f, 0.85f, 0.35f));
                Shadowed(new Rect(r.x, r.y + 320f, r.width, 50f), DressStrings.UnlockedHint, _small);
            }
            else Shadowed(new Rect(r.x, r.y + 270f, r.width, 60f), next, _small);
            if (LastPhoto != null) Shadowed(new Rect(r.x, r.y + 390f, r.width, 50f), DressStrings.PhotoSaved(_photoCount), _label);
            for (int i = 0; i < _buttons.Count; i++) DrawButton(_buttons[i], i == _focus && _remoteFocus, brand);
        }

        static string ThemeKey(Theme t) => t.ToString().ToLowerInvariant();

        static string PoseKey(ShowPose p) => p switch
        {
            ShowPose.Star => "star", ShowPose.Hips => "hips", ShowPose.Heart => "heart", ShowPose.Wave => "wave",
            ShowPose.Super => "super", _ => "plane",
        };

        // ---------------------------------------------------------------- GUI helpers

        void UiImage(Rect r, string name, Color tint)
        {
            if (_art == null || !_art.HasUi(name)) return;
            UiPicture(r, _art.UiRect(name), tint);
        }

        /// <summary>A piece of the interface sheet (in the batch's pass, or with IMGUI when everything is IMGUI's).</summary>
        void UiPicture(Rect r, Rect uv, Color tint)
        {
            if (_art == null || !PicturesPass) return;
            if (_pass == HudPass.Pictures)
            {
                _hud.Image(r, uv, tint);
                return;
            }
            var old = GUI.color;
            GUI.color = tint;
            GUI.DrawTextureWithTexCoords(r, _art.Ui, uv, true);
            GUI.color = old;
        }

        /// <summary>A picture drawn by IMGUI now, over the words (the hands' cursors).</summary>
        void UiImageNow(Rect r, string name, Color tint)
        {
            if (_art == null || !_art.HasUi(name)) return;
            var old = GUI.color;
            GUI.color = tint;
            GUI.DrawTextureWithTexCoords(r, _art.Ui, _art.UiRect(name), true);
            GUI.color = old;
        }

        /// <summary>A rounded panel: its 9 pieces in the batch, or IMGUI's own 9-slice (a small rounded texture: no seams).</summary>
        void Panel(Rect r, Color c)
        {
            if (!PicturesPass) return;
            int corner = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(r.width, r.height) * 0.5f), 0, 24);
            if (_pass == HudPass.Pictures)
            {
                _hud.Nine(r, corner, PanelUv, new Vector2(corner / (float)ShapesWidth, corner / (float)ShapesHeight), c);
                return;
            }
            PanelNow(r, c);
        }

        /// <summary>IMGUI's rounded panel, drawn now (the photo's polaroid lies over everything, words included).</summary>
        void PanelNow(Rect r, Color c)
        {
            if (_round == null)
            {
                const int Size = 64, Radius = 24;
                _roundTex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "Rounded", wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[Size * Size];
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                        px[y * Size + x] = new Color32(255, 255, 255, PanelAlpha(x, y));
                _roundTex.SetPixels32(px);
                _roundTex.Apply(false, true);
                _round = new GUIStyle { normal = { background = _roundTex }, border = new RectOffset(Radius, Radius, Radius, Radius) };
            }
            int corner = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(r.width, r.height) * 0.5f), 0, 24);
            if (_round.border.left != corner) _round.border = new RectOffset(corner, corner, corner, corner);
            var old = GUI.color;
            GUI.color = c;
            GUI.Box(r, GUIContent.none, _round);
            GUI.color = old;
        }

        GUIStyle _round;
        Texture2D _roundTex;

        void Shadowed(Rect r, string text, GUIStyle style, Color? color = null)
        {
            if (WordsPass) GameHud.Text(r, text, style, TextAnchor.MiddleCenter, color);
        }

        /// <summary>A plain rectangle (bars, the photo's flash): IMGUI's, with the words.</summary>
        void Box(Rect r, Color c)
        {
            if (WordsPass) GameHud.Box(r, c);
        }

        /// <summary>A text's width with some room to spare: scaled down (a small window) IMGUI text comes out a
        /// little wider than it measures. Measured once per style, size and text (measuring every label every frame
        /// cost ~1.4 ms a frame on the box).</summary>
        static float TextWidth(GUIStyle style, string text)
        {
            var key = (style, style.fontSize, text);
            if (!_textWidths.TryGetValue(key, out float width))
            {
                if (_textWidths.Count > 200) _textWidths.Clear();
                _textWidths[key] = width = style.CalcSize(new GUIContent(text)).x * 1.12f;
            }
            return width;
        }

        static readonly Dictionary<(GUIStyle, int, string), float> _textWidths = new Dictionary<(GUIStyle, int, string), float>();

        static Rect Shrink(Rect r, float k) => new Rect(r.center.x - r.width * k * 0.5f, r.center.y - r.height * k * 0.5f, r.width * k, r.height * k);

        static Rect Grow(Rect r, float d) => new Rect(r.x - d, r.y - d, r.width + 2f * d, r.height + 2f * d);

        void EnsureStyles()
        {
            if (_label != null) return;
            _big = new GUIStyle(GameHud.Big) { fontSize = 88, wordWrap = false, clipping = TextClipping.Overflow };
            _medium = new GUIStyle(GameHud.Medium) { fontSize = 44, wordWrap = false, clipping = TextClipping.Overflow };
            _small = new GUIStyle(GameHud.Small) { fontSize = 30 };
            _label = new GUIStyle(GameHud.Small) { fontSize = 22, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Overflow };
            _pill = new GUIStyle(GameHud.Small) { fontSize = 28, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Overflow };
        }
    }
}
