using System;
using MoveIt.Motion;
using UnityEngine;
using Joint = MoveIt.Motion.Joint;

namespace MoveIt.Games.Dress
{
    /// <summary>How a piece follows the body (tools/dress/outfits.py explains each one).</summary>
    public enum Rig { Torso, Skirt, Tail, Arm, Leg, Head, Hair, Face, Back, Cape, Hand }

    /// <summary>Drawing order, back to front. Back pieces hide where the body is, Bottom and Top where the arms are.</summary>
    public enum Layer { Back, Doll, Bottom, Top, Sleeve, HairFront, Head, Face, Hand }

    public enum HandMode { Along, Upright, Hang, Float }

    /// <summary>One piece of art in the outfit atlas (outfit_atlas.json).</summary>
    public sealed class PieceDef
    {
        public string Name, Item;
        public Rig Rig;
        public Layer Layer;
        /// <summary>Atlas rect in pixels (y down from the top of the atlas).</summary>
        public float X, Y, W, H;
        /// <summary>Where body-template (0, 0) is in the cell (pixels, y down), and pixels per torso length.</summary>
        public float OriginX, OriginY, Ppt;
        /// <summary>Drawn on both sides (sleeves, legs, wings): the left one is the mirror image.</summary>
        public bool BothSides;
        public Vector2 Pivot;
        public float Flap;
        public HandMode Mode;
        public Vector2 Tip, Knot;
        public float Hang = float.PositiveInfinity;
        /// <summary>Glint points (x, y, size in template units), three floats each.</summary>
        public float[] Sparkles = Array.Empty<float>();
        /// <summary>The art's half width (template units) at evenly spaced heights from ProfileY0 to ProfileY1: the
        /// cross-section a turning body wraps it around.</summary>
        public float[] Profile = Array.Empty<float>();
        public float ProfileY0, ProfileY1 = 1f;

        /// <summary>The art's half width at template height <paramref name="y"/> (0 when there is no profile).</summary>
        public float ProfileAt(float y)
        {
            int n = Profile.Length;
            if (n == 0) return 0f;
            if (n == 1 || ProfileY1 <= ProfileY0) return Profile[0];
            float f = Mathf.Clamp01((y - ProfileY0) / (ProfileY1 - ProfileY0)) * (n - 1);
            int i = Mathf.Min((int)f, n - 2);
            return Mathf.Lerp(Profile[i], Profile[i + 1], f - i);
        }

        /// <summary>Template point of a cell point (u, v in 0..1 across the cell, v down).</summary>
        public Vector2 TemplateAt(float u, float v) => new Vector2((u * W - OriginX) / Ppt, (v * H - OriginY) / Ppt);

        /// <summary>Mesh grid size: bending pieces get more vertices, and pieces that wrap around a turning body more
        /// columns (the turn curves them across).</summary>
        public void GridSize(out int cols, out int rows)
        {
            switch (Rig)
            {
                case Rig.Torso: cols = 11; rows = 7; break;
                case Rig.Skirt: cols = 11; rows = 9; break;
                case Rig.Cape: cols = 7; rows = 9; break;
                case Rig.Tail: cols = 9; rows = 12; break;
                case Rig.Arm: case Rig.Leg: cols = 3; rows = 10; break;
                case Rig.Hair: cols = 9; rows = 9; break;
                case Rig.Head: case Rig.Face: cols = 9; rows = 3; break;
                default: cols = 2; rows = 2; break;
            }
        }
    }

    /// <summary>
    /// A piece's placement this frame: either a stack of rows (each a centre, a direction and a half width on screen,
    /// bilinear in between and beyond) or a similarity (an origin and two axes), plus the extras: wing flapping
    /// around a pivot, hair that hangs below a line, a sway offset that grows toward the hem.
    /// </summary>
    public sealed class RigFrame
    {
        public bool Valid, Mirror;
        public int Rows;
        public readonly float[] RowY = new float[3], RowH = new float[3], RowHalf = new float[3];
        public readonly Vector2[] RowC = new Vector2[3], RowD = new Vector2[3];
        public float SwayFrom = float.PositiveInfinity, SwayLen = 1f;
        public Vector2 Sway;

        public bool Similar;
        public Vector2 Origin, TOrigin, AxisX, AxisY;

        public float HangY = float.PositiveInfinity;
        public Vector2 HangOrigin, HangAxisX, HangAxisY, HairSway;

        public Vector2 Pivot;
        public float FlapCos = 1f, FlapSin;

        /// <summary>How the piece turns with the body (<see cref="Turn"/>), the turn angle and the section's shape;
        /// <see cref="BackSheet"/>: map onto the back of the section.</summary>
        public WrapKind Wrap;
        public float WrapYaw, WrapDepth, WrapRadius;
        public PieceDef WrapDef;
        public bool BackSheet;
        /// <summary>The last point <see cref="Map"/> placed faces away from the camera (collapsed onto the silhouette).</summary>
        public bool Hidden;

        /// <summary>A template point (torso lengths, y down) on the screen (pixels, y up).</summary>
        public Vector2 Map(Vector2 t)
        {
            if (FlapSin != 0f)
            {
                Vector2 d = t - Pivot;
                t = Pivot + new Vector2(d.x * FlapCos - d.y * FlapSin, d.x * FlapSin + d.y * FlapCos);
            }
            if (Mirror) t.x = -t.x;
            Hidden = false;
            if (Wrap != WrapKind.None) t.x = Turn.Apply(Wrap, t.x, t.y, WrapYaw, WrapDepth, WrapRadius, WrapDef, BackSheet, out Hidden);
            if (Similar)
            {
                Vector2 p = Origin + AxisX * (t.x - TOrigin.x) + AxisY * (t.y - TOrigin.y);
                if (t.y > HangY)
                {
                    float w = Smooth(HangY, HangY + 0.35f, t.y);
                    Vector2 hang = HangOrigin + HangAxisX * t.x + HangAxisY * (t.y - HangY);
                    p = Vector2.LerpUnclamped(p, hang, w) + HairSway * (w * w);
                }
                return p;
            }
            int i = Rows == 3 && t.y >= RowY[1] ? 1 : 0;
            float k = (t.y - RowY[i]) / (RowY[i + 1] - RowY[i]);
            float h = Mathf.Max(Mathf.LerpUnclamped(RowH[i], RowH[i + 1], k), 0.05f);
            float s = t.x / h;
            Vector2 a = RowC[i] + RowD[i] * (s * RowHalf[i]);
            Vector2 b = RowC[i + 1] + RowD[i + 1] * (s * RowHalf[i + 1]);
            Vector2 q = Vector2.LerpUnclamped(a, b, k);
            if (t.y > SwayFrom)
            {
                float f = (t.y - SwayFrom) / SwayLen;
                q += Sway * Mathf.Min(f * f, 4f);
            }
            return q;
        }

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        public void Reset()
        {
            Valid = Mirror = Similar = false;
            Rows = 0;
            SwayFrom = HangY = float.PositiveInfinity;
            SwayLen = 1f;
            Sway = HairSway = Vector2.zero;
            FlapCos = 1f;
            FlapSin = 0f;
            Wrap = WrapKind.None;
            WrapYaw = WrapDepth = WrapRadius = 0f;
            WrapDef = null;
            BackSheet = Hidden = false;
        }

        public void SetWrap(WrapKind kind, float yaw, float depth, float radius, PieceDef def)
        {
            Wrap = kind;
            WrapYaw = yaw;
            WrapDepth = depth;
            WrapRadius = radius;
            WrapDef = def;
        }

        public void SetRow(int i, float y, float h, Vector2 c, Vector2 d, float half)
        {
            RowY[i] = y;
            RowH[i] = h;
            RowC[i] = c;
            RowD[i] = d;
            RowHalf[i] = half;
            Rows = Math.Max(Rows, i + 1);
        }

        public void SetSimilar(Vector2 origin, Vector2 tOrigin, Vector2 axisX, Vector2 axisY)
        {
            Similar = true;
            Origin = origin;
            TOrigin = tOrigin;
            AxisX = axisX;
            AxisY = axisY;
        }
    }

    /// <summary>How a piece turns when the body (or the head) turns away from the camera.</summary>
    public enum WrapKind
    {
        None,
        /// <summary>Around a rounded body: an elliptical cross-section as wide as the art at each height (tops, skirts,
        /// hats; depth / width = <c>depth</c>). The art covers the front and, mirrored, the back (a second sheet).</summary>
        Section,
        /// <summary>On a fixed round surface of <c>radius</c> (the face, the hair over the forehead); wider parts
        /// stick out from its sides. Front only.</summary>
        Surface,
        /// <summary>A flat piece <c>depth</c> behind the turning axis (wings, capes, the hair behind the head): seen
        /// from both sides.</summary>
        Plane,
    }

    /// <summary>
    /// 2D clothes on a turning body. Squashing a picture when the shoulders come closer looks flat and glitches (the
    /// shoulder line shrinks to nothing and flips). Instead each piece lies on a 3D cross-section that turns by the
    /// body's yaw and is projected back: the front of a dress slides toward the side the player turns to, the side that
    /// turns away compresses into the silhouette (points behind it collapse onto it, so the mesh never folds), a round
    /// skirt keeps its width while its pattern turns, wings and capes behind the back move the other way. A wrapped
    /// piece has two sheets: the front, and the back (the same art, mirrored, as if seen through the body); each shows
    /// only where it faces the camera, so together they cover the whole silhouette at every angle and the turn is
    /// continuous all the way round. Yaw: 0 facing the camera, + = the chest turns toward the screen's right (the
    /// screen-right shoulder goes back).
    /// </summary>
    public static class Turn
    {
        /// <summary>
        /// Where a point <paramref name="s"/> across the front of an elliptical section (-1..1, the sides at +-1; depth
        /// / width <paramref name="k"/>) lands after turning by <paramref name="yaw"/>, in units of the section's half
        /// width. Points facing away from the camera collapse onto the silhouette (<paramref name="hidden"/>). Beyond
        /// +-1 the point is on a flap sticking out sideways from the section's edge.
        /// </summary>
        public static float Wrap(float s, float yaw, float k, out bool hidden)
        {
            float c = Mathf.Cos(yaw), sn = Mathf.Sin(yaw);
            float beyond = Mathf.Abs(s) - 1f;
            if (beyond > 0f)
            {
                float sign = Mathf.Sign(s);
                float edge = Wrap(sign, yaw, k, out hidden);
                float flap = sign * (1f + beyond) * c;
                if (sign * flap > sign * edge)
                {
                    hidden = false;
                    return flap;
                }
                return edge;
            }
            float phi = Mathf.Asin(s);
            // the front faces the camera where its outline still moves outward; past that it is behind the silhouette
            hidden = false;
            if (sn >= 0f)
            {
                float phi0 = Mathf.Atan2(c, k * sn);
                if (phi > phi0) { phi = phi0; hidden = true; }
            }
            else
            {
                float phi0 = -Mathf.Atan2(c, -k * sn);
                if (phi < phi0) { phi = phi0; hidden = true; }
            }
            return Mathf.Sin(phi) * c + k * Mathf.Cos(phi) * sn;
        }

        public static float Wrap(float s, float yaw, float k) => Wrap(s, yaw, k, out _);

        /// <summary>The same point on the back sheet (the art mirrored onto the back of the section).</summary>
        public static float WrapBack(float s, float yaw, float k, out bool hidden) => Wrap(-s, yaw + Mathf.PI, k, out hidden);

        /// <summary>How far inside the art's edge the sides of a <see cref="WrapKind.Section"/> are when seen side on.</summary>
        public const float SeamInset = 0.1f;

        /// <summary>A flat piece <paramref name="depth"/> behind the turning axis.</summary>
        public static float Plane(float x, float yaw, float depth) => x * Mathf.Cos(yaw) - depth * Mathf.Sin(yaw);

        /// <summary>The template x of a point after the turn (see <see cref="WrapKind"/>); <paramref name="back"/>: on
        /// the back sheet.</summary>
        public static float Apply(WrapKind kind, float x, float y, float yaw, float depth, float radius, PieceDef def, bool back,
                                  out bool hidden)
        {
            hidden = false;
            switch (kind)
            {
                case WrapKind.Section:
                {
                    Section(def, y, yaw, depth, radius, out float a, out float k);
                    return a * (back ? WrapBack(x / a, yaw, k, out hidden) : Wrap(x / a, yaw, k, out hidden));
                }
                case WrapKind.Surface:
                    return radius * Wrap(x / radius, yaw, 1f, out hidden);
                case WrapKind.Plane:
                    return Plane(x, yaw, depth);
                default:
                    return x;
            }
        }

        /// <summary>
        /// A <see cref="WrapKind.Section"/>'s half width <paramref name="a"/> and depth / width <paramref name="k"/> at
        /// template height <paramref name="y"/>. The sides (where front and back meet, and the art is stretched the most
        /// when seen side on) are a little inside the art's edge, so there is fabric there, not its outline; the
        /// outermost rim folds round them. The section keeps its depth.
        /// </summary>
        public static void Section(PieceDef def, float y, float yaw, float depth, float radius, out float a, out float k)
        {
            float w = Mathf.Max(def != null ? def.ProfileAt(y) : 0f, radius);
            a = Mathf.Max(w * (1f - SeamInset * Mathf.Abs(Mathf.Sin(yaw))), radius);
            k = depth * w / a;
        }

        /// <summary>Where (in s, -1..1) a sheet turns away from the camera: past it the points collapse onto the
        /// silhouette (+-1 when the whole sheet faces the camera).</summary>
        public static float Horizon(float yaw, float k, bool back)
        {
            if (back) return -Horizon(yaw + Mathf.PI, k, false);
            float c = Mathf.Cos(yaw), sn = Mathf.Sin(yaw);
            return sn >= 0f ? Mathf.Sin(Mathf.Atan2(c, k * sn)) : Mathf.Sin(-Mathf.Atan2(c, -k * sn));
        }

        /// <summary>Whether a wrapped piece shows its back sheet at this turn.</summary>
        public static bool HasBack(WrapKind kind, float yaw) =>
            kind == WrapKind.Section && (Mathf.Abs(Mathf.Sin(yaw)) > 0.02f || Mathf.Cos(yaw) < 0f);

        /// <summary>How wide a body of depth / width <paramref name="k"/> looks turned by <paramref name="yaw"/>.</summary>
        public static float Silhouette(float yaw, float k)
        {
            float c = Mathf.Cos(yaw), sn = Mathf.Sin(yaw);
            return Mathf.Sqrt(c * c + k * k * sn * sn);
        }
    }

    /// <summary>
    /// The body the outfit is fitted to, on the screen (pixels, y up): every joint filtered (light One Euro, body
    /// relative: the camera picture is exact, so lag shows as the clothes trailing the body), joints the camera
    /// can't see estimated from the ones it can (knees below the frame, a hidden elbow), and the steadier measures
    /// the rig needs (torso length, the head's size and tilt).
    /// </summary>
    public sealed class OutfitPose
    {
        // template proportions (tools/dress/draw.py)
        public const float TemplateEyes = 0.128f, TemplateHeadWidth = 0.30f, TemplateEyeY = -0.47f;

        public float MinCutoff = 1.5f, Beta = 6f;

        public readonly Vector2[] J = new Vector2[Skeleton.JointCount];
        /// <summary>How sure each joint is (0..1, smoothed): rigs fade sleeves and hand items with it.</summary>
        public readonly float[] Seen = new float[Skeleton.JointCount];
        public bool Valid { get; private set; }

        /// <summary>Torso length on screen (pixels): one template unit.</summary>
        public float T { get; private set; }
        public Vector2 ShoulderC => (J[(int)Joint.LeftShoulder] + J[(int)Joint.RightShoulder]) * 0.5f;
        public Vector2 HipC => (J[(int)Joint.LeftHip] + J[(int)Joint.RightHip]) * 0.5f;
        /// <summary>Unit vector down the torso (shoulders to hips).</summary>
        public Vector2 Down { get; private set; } = Vector2.down;
        public float ShoulderAngle { get; private set; }
        /// <summary>The middle between the eyes.</summary>
        public Vector2 HeadCenter { get; private set; }
        /// <summary>The head's turning axis: behind the eyes, so a turned face slides across the head (<see cref="Turn"/>).</summary>
        public Vector2 SkullCenter { get; private set; }
        public float HeadRoll { get; private set; }
        /// <summary>Pixels per template unit for the head (from the head's width when facing the camera).</summary>
        public float HeadScale { get; private set; }
        /// <summary>The body's turn (radians, 0 = facing the camera, + = the chest turns toward the screen's right).</summary>
        public float Yaw { get; private set; }
        /// <summary>The head's own turn (same sign), from the nose against the ears.</summary>
        public float HeadYaw { get; private set; }
        /// <summary>Half the shoulder and hip widths as they are when facing the camera (pixels): the clothes keep these
        /// and the turn narrows them, so a turn or raised arms (shoulders come closer) don't shrink a dress.</summary>
        public float FrontShoulderHalf => _frontShoulders * T;
        public float FrontHipHalf => _frontHips * T;
        /// <summary>Unit vector across the body toward the screen's right: across the torso's axis, following the
        /// shoulder line's tilt while it can be trusted (facing the camera).</summary>
        public Vector2 Across { get; private set; } = Vector2.right;

        /// <summary>Template units (torso lengths) from the head's axis to the eyes.</summary>
        public const float EyeDepth = 0.15f;

        readonly OneEuroFilter3[] _filters = new OneEuroFilter3[Skeleton.JointCount];
        readonly Vector2[] _good = new Vector2[Skeleton.JointCount];
        readonly bool[] _hasGood = new bool[Skeleton.JointCount];
        readonly OneEuroFilter _torso = new OneEuroFilter(0.6f, 0.5f), _roll = new OneEuroFilter(1.2f, 0.8f),
            _shoulder = new OneEuroFilter(1.2f, 0.8f), _yaw = new OneEuroFilter(1.0f, 0.6f), _headYaw = new OneEuroFilter(1.5f, 0.6f);
        float _headWidth, _frontShoulders, _frontHips, _frontHead, _yawSign = 1f;

        public OutfitPose()
        {
            for (int i = 0; i < _filters.Length; i++) _filters[i] = new OneEuroFilter3(MinCutoff, Beta);
        }

        public void Reset()
        {
            Valid = false;
            foreach (var f in _filters) f.Reset();
            _torso.Reset();
            _roll.Reset();
            _shoulder.Reset();
            _yaw.Reset();
            _headYaw.Reset();
            Array.Clear(_hasGood, 0, _hasGood.Length);
            Array.Clear(Seen, 0, Seen.Length);
            _headWidth = _frontShoulders = _frontHips = _frontHead = 0f;
            Yaw = HeadYaw = 0f;
        }

        /// <summary>
        /// A new body sample on the screen: joints (pixels, y up), visibilities, seconds since the last one, and the
        /// body's turn from the camera AI's depth estimates (<see cref="WorldYaw"/>) when it has them (NaN: guessed
        /// from the shoulders' width and where the nose is).
        /// </summary>
        public void Update(Vector2[] joints, float[] visibility, float dt, float worldYaw = float.NaN)
        {
            _worldYaw = worldYaw;
            float rawTorso = Vector2.Distance(Mid(joints, Joint.LeftShoulder, Joint.RightShoulder), Mid(joints, Joint.LeftHip, Joint.RightHip));
            bool torsoSeen = visibility[(int)Joint.LeftShoulder] > 0.3f && visibility[(int)Joint.RightShoulder] > 0.3f;
            if (!Valid && (!torsoSeen || rawTorso < 4f)) return;
            T = !Valid ? rawTorso : _torso.Filter(torsoSeen && rawTorso > 4f ? rawTorso : T, dt, Mathf.Max(T, 1f));
            float scale = Mathf.Max(T, 1f);
            for (int i = 0; i < Skeleton.JointCount; i++)
            {
                bool seen = visibility[i] >= 0.4f;
                Seen[i] = !Valid ? (seen ? 1f : 0f) : Mathf.MoveTowards(Seen[i], seen ? 1f : 0f, dt / 0.15f);
                if (!seen) continue;
                _good[i] = _filters[i].Filter(joints[i], dt, scale);
                _hasGood[i] = true;
            }
            Valid = true;
            Estimate();
            Derive(dt);
        }

        /// <summary>Joints that were never seen, or not for a while, come from the body's proportions.</summary>
        void Estimate()
        {
            Vector2 E(Joint j) => _hasGood[(int)j] ? _good[(int)j] : default;
            Vector2 ls = E(Joint.LeftShoulder), rs = E(Joint.RightShoulder);
            if (!_hasGood[(int)Joint.LeftHip] || !_hasGood[(int)Joint.RightHip])
            {
                Vector2 c = (ls + rs) * 0.5f, side = (rs - ls) * 0.3f;
                Put(Joint.LeftHip, c + Vector2.down * T - side, 0f);
                Put(Joint.RightHip, c + Vector2.down * T + side, 0f);
            }
            Blend(Joint.LeftShoulder, ls);
            Blend(Joint.RightShoulder, rs);
            Blend(Joint.LeftHip, E(Joint.LeftHip));
            Blend(Joint.RightHip, E(Joint.RightHip));
            Vector2 sc = (J[(int)Joint.LeftShoulder] + J[(int)Joint.RightShoulder]) * 0.5f;
            Vector2 hc = (J[(int)Joint.LeftHip] + J[(int)Joint.RightHip]) * 0.5f;
            Vector2 down = (hc - sc).sqrMagnitude > 1f ? (hc - sc).normalized : Vector2.down;
            Vector2 across = new Vector2(-down.y, down.x);         // toward the screen's right when upright
            if (Vector2.Dot(across, J[(int)Joint.RightShoulder] - J[(int)Joint.LeftShoulder]) < 0) across = -across;
            // arms: an elbow hangs below its shoulder, a wrist carries on along the upper arm
            foreach (var (s, e, w, sign) in new[] { (Joint.LeftShoulder, Joint.LeftElbow, Joint.LeftWrist, -1f), (Joint.RightShoulder, Joint.RightElbow, Joint.RightWrist, 1f) })
            {
                Vector2 elbowGuess = J[(int)s] + down * (0.64f * T) + across * (sign * 0.1f * T);
                Blend(e, elbowGuess, true);
                Vector2 upper = J[(int)e] - J[(int)s];
                Vector2 wristGuess = J[(int)e] + (upper.sqrMagnitude > 1f ? upper.normalized : down) * (0.57f * T);
                Blend(w, wristGuess, true);
            }
            // legs: knees below the hips, ankles below the knees
            foreach (var (h, k, a) in new[] { (Joint.LeftHip, Joint.LeftKnee, Joint.LeftAnkle), (Joint.RightHip, Joint.RightKnee, Joint.RightAnkle) })
            {
                Blend(k, J[(int)h] + down * (0.92f * T), true);
                Vector2 thigh = J[(int)k] - J[(int)h];
                Blend(a, J[(int)k] + (thigh.sqrMagnitude > 1f ? thigh.normalized : down) * (0.93f * T), true);
            }
            // the face: eyes and ears above the shoulders when they can't be seen
            Vector2 up = -down;
            Blend(Joint.Nose, sc + up * (0.38f * T), true);
            Blend(Joint.LeftEye, sc + up * (0.47f * T) - across * (0.064f * T), true);
            Blend(Joint.RightEye, sc + up * (0.47f * T) + across * (0.064f * T), true);
            Blend(Joint.LeftEar, sc + up * (0.43f * T) - across * (0.15f * T), true);
            Blend(Joint.RightEar, sc + up * (0.43f * T) + across * (0.15f * T), true);
        }

        /// <summary>The joint's filtered sample where it is seen, faded toward <paramref name="guess"/> where it isn't.</summary>
        void Blend(Joint j, Vector2 guess, bool guessed = false)
        {
            int i = (int)j;
            if (!guessed)
            {
                J[i] = _hasGood[i] ? _good[i] : guess;
                return;
            }
            J[i] = _hasGood[i] ? Vector2.Lerp(guess, _good[i], Seen[i]) : guess;
        }

        void Put(Joint j, Vector2 p, float seen)
        {
            _good[(int)j] = p;
            _hasGood[(int)j] = true;
            Seen[(int)j] = seen;
        }

        void Derive(float dt)
        {
            Vector2 sc = ShoulderC, hc = HipC;
            Down = (hc - sc).sqrMagnitude > 1f ? (hc - sc).normalized : Vector2.down;
            Vector2 axisAcross = new Vector2(-Down.y, Down.x);    // the screen's right for an upright body
            Vector2 sh = J[(int)Joint.RightShoulder] - J[(int)Joint.LeftShoulder];
            Vector2 hp = J[(int)Joint.RightHip] - J[(int)Joint.LeftHip];

            // How wide shoulders and hips are when facing the camera, in torso lengths (stepping closer changes nothing):
            // a wider measure is taken at once (facing again), a narrower one only very slowly (a new pose of a player).
            float sr = 0.5f * sh.magnitude / Mathf.Max(T, 1f), hr = 0.5f * hp.magnitude / Mathf.Max(T, 1f);
            _frontShoulders = Front(_frontShoulders, sr, 0.25f, 0.6f, dt);
            _frontHips = Front(_frontHips, hr, 0.12f, 0.42f, dt);

            // The turn: from the depth of the shoulders and hips when the camera AI gives it, else from how narrow the
            // shoulders look and which way the nose has gone (a narrow chest with the nose in the middle is raised arms)
            float yaw;
            if (!float.IsNaN(_worldYaw)) yaw = _worldYaw;
            else
            {
                float c = Mathf.Clamp01(sr / (0.9f * Mathf.Max(_frontShoulders, 0.05f)));
                float nose = Vector2.Dot(J[(int)Joint.Nose] - sc, axisAcross) / Mathf.Max(T, 1f);
                if (Mathf.Abs(nose) > 0.05f) _yawSign = Mathf.Sign(nose);
                float evidence = Mathf.Max(Smooth(0.03f, 0.11f, Mathf.Abs(nose)), c < 0.45f ? 1f : 0f);
                yaw = _yawSign * Mathf.Acos(c) * evidence;
            }
            Yaw = _yaw.Filter(Unwrap(yaw, Yaw), dt);

            // Across the body: the shoulder line while it is long and points the right way, else across the torso axis
            float trust = Mathf.Clamp01(Mathf.Cos(Yaw)) * Smooth(0.5f, 0.8f, sr / Mathf.Max(_frontShoulders, 0.05f));
            Vector2 line = sh.sqrMagnitude > 1f && Vector2.Dot(sh, axisAcross) > 0f ? sh.normalized : axisAcross;
            Across = Vector2.Lerp(axisAcross, line, trust * 0.8f).normalized;
            ShoulderAngle = _shoulder.Filter(Unwrap(Mathf.Atan2(Across.y, Across.x), ShoulderAngle), dt);

            Vector2 le = J[(int)Joint.LeftEye], re = J[(int)Joint.RightEye];
            Vector2 lr = J[(int)Joint.LeftEar], rr = J[(int)Joint.RightEar];
            HeadCenter = (le + re) * 0.5f;
            // seen from behind the eye and ear lines point to the screen's left: the head's roll is taken within 90
            // degrees of the shoulders (a head can't be upside down on them)
            Vector2 eyeLine = re - le, earLine = rr - lr;
            if (Vector2.Dot(eyeLine, Across) < 0f) eyeLine = -eyeLine;
            if (Vector2.Dot(earLine, Across) < 0f) earLine = -earLine;
            float roll = Mathf.Atan2(eyeLine.y, eyeLine.x);
            if (Seen[(int)Joint.LeftEar] > 0.5f && Seen[(int)Joint.RightEar] > 0.5f)
                roll = Mathf.Atan2(eyeLine.y + earLine.y, eyeLine.x + earLine.x);
            // a turned head's eye line gets short and unreliable: lean on the body's tilt then
            float eyeTrust = Smooth(0.25f, 0.6f, eyeLine.magnitude / Mathf.Max(_headWidth * TemplateEyes / TemplateHeadWidth, 1f));
            roll = ShoulderAngle + (Unwrap(roll, ShoulderAngle) - ShoulderAngle) * eyeTrust;
            HeadRoll = _roll.Filter(Unwrap(roll, HeadRoll), dt);
            // The head's width: both measures shrink when the head turns, so the widest recent one is kept (it grows at
            // once, shrinks slowly), and it stays in a sensible range for the body (children's heads are bigger).
            float eyes = eyeLine.magnitude, ears = earLine.magnitude;
            float target = Mathf.Max(eyes * (TemplateHeadWidth / TemplateEyes), ears);
            target = Mathf.Clamp(target, 0.22f * T, 0.62f * T);
            _headWidth = _headWidth <= 0f ? target : target > _headWidth
                ? Mathf.Lerp(_headWidth, target, 1f - Mathf.Exp(-dt / 0.12f))
                : Mathf.Lerp(_headWidth, target, 1f - Mathf.Exp(-dt / 1.5f));
            // as wide as when facing the camera (in torso lengths, like the shoulders): a turned head keeps its hats' size
            _frontHead = Front(_frontHead, target / Mathf.Max(T, 1f), 0.22f, 0.62f, dt);
            float frontHead = _frontHead * T;
            HeadScale = Mathf.Max(_headWidth, 0.92f * frontHead) / TemplateHeadWidth;

            // The head's turn: the nose sits about 0.62 head widths in front of the ears, so its offset from their middle
            // gives the angle; the head's axis is then behind the eyes by their depth
            Vector2 headAcross = new Vector2(Mathf.Cos(HeadRoll), Mathf.Sin(HeadRoll));
            float noseOff = Vector2.Dot(J[(int)Joint.Nose] - (lr + rr) * 0.5f, headAcross);
            float headYaw = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(noseOff / (0.62f * Mathf.Max(frontHead, 1f)), -1f, 1f)), -1.4f, 1.4f);
            // the nose can't tell a face turned 45 degrees from the back of a head turned 135: the body's turn can
            if (Mathf.Cos(Yaw) < 0f) headYaw = Mathf.Sign(Yaw) * Mathf.PI - headYaw;
            HeadYaw = _headYaw.Filter(Unwrap(headYaw + Mathf.Round((Yaw - headYaw) / (2f * Mathf.PI)) * 2f * Mathf.PI, HeadYaw), dt);
            SkullCenter = HeadCenter - headAcross * (HeadScale * EyeDepth * Mathf.Sin(HeadYaw));
        }

        float _worldYaw = float.NaN;

        static float Front(float current, float sample, float min, float max, float dt)
        {
            sample = Mathf.Clamp(sample, min, max);
            if (current <= 0f) return sample;
            return Mathf.Lerp(current, sample, 1f - Mathf.Exp(-dt / (sample > current ? 0.15f : 25f)));
        }

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// The body's turn from the camera AI's world joints (metres, mirrored like the view, z = depth with negative
        /// toward the camera): the shoulders' and hips' left-to-right vectors seen from above.
        /// </summary>
        public static float WorldYaw(Vector3 leftShoulder, Vector3 rightShoulder, Vector3 leftHip, Vector3 rightHip)
        {
            Vector3 v = (rightShoulder - leftShoulder) + 0.6f * (rightHip - leftHip);
            return Mathf.Atan2(v.z, v.x);
        }

        static float Unwrap(float angle, float reference)
        {
            while (angle - reference > Mathf.PI) angle -= 2f * Mathf.PI;
            while (angle - reference < -Mathf.PI) angle += 2f * Mathf.PI;
            return angle;
        }

        static Vector2 Mid(Vector2[] j, Joint a, Joint b) => (j[(int)a] + j[(int)b]) * 0.5f;

        /// <summary>
        /// The paper doll's pose (nobody in front of the camera): the template body standing at <paramref name="center"/>
        /// (shoulder centre, pixels), <paramref name="t"/> pixels per torso length, breathing and swaying a little.
        /// </summary>
        public static void DollJoints(Vector2 center, float t, float time, Vector2[] joints, float[] visibility)
        {
            float sway = Mathf.Sin(time * 1.4f) * 0.025f, breathe = Mathf.Sin(time * 2.1f) * 0.012f;
            float swing = Mathf.Sin(time * 1.4f + 0.6f) * 0.04f, tilt = Mathf.Sin(time * 0.9f) * 0.05f;
            Vector2 P(float x, float y) => center + new Vector2((x + sway * (1f - y * 0.2f)) * t, -(y - breathe * (y < 0.5f ? 1f : 0f)) * t);
            Set(Joint.LeftShoulder, P(-0.40f, 0f));
            Set(Joint.RightShoulder, P(0.40f, 0f));
            Set(Joint.LeftElbow, P(-0.50f - swing, 0.63f));
            Set(Joint.RightElbow, P(0.50f + swing, 0.63f));
            Set(Joint.LeftWrist, P(-0.55f - swing * 1.6f, 1.19f));
            Set(Joint.RightWrist, P(0.55f + swing * 1.6f, 1.19f));
            Set(Joint.LeftHip, P(-0.24f, 1.0f));
            Set(Joint.RightHip, P(0.24f, 1.0f));
            Set(Joint.LeftKnee, P(-0.25f, 1.92f));
            Set(Joint.RightKnee, P(0.25f, 1.92f));
            Set(Joint.LeftAnkle, P(-0.25f, 2.85f));
            Set(Joint.RightAnkle, P(0.25f, 2.85f));
            Vector2 head = P(0f, -0.47f);
            float c = Mathf.Cos(tilt), s = Mathf.Sin(tilt);
            Vector2 H(float x, float y)
            {
                Vector2 d = new Vector2(x * t, -(y + 0.47f) * t);
                return head + new Vector2(d.x * c - d.y * s, d.x * s + d.y * c);
            }
            Set(Joint.Nose, H(0f, -0.38f));
            Set(Joint.LeftEye, H(-0.064f, -0.47f));
            Set(Joint.RightEye, H(0.064f, -0.47f));
            Set(Joint.LeftEar, H(-0.15f, -0.43f));
            Set(Joint.RightEar, H(0.15f, -0.43f));

            void Set(Joint j, Vector2 p)
            {
                joints[(int)j] = p;
                visibility[(int)j] = 1f;
            }
        }
    }

    /// <summary>
    /// The outfit's secondary motion (on the frame clock): skirts and capes lag and swing when the body moves, hair
    /// sways, wings flap (faster when the arms move), a bag swings from the hand, a balloon floats on its string.
    /// </summary>
    public sealed class OutfitMotion
    {
        public Vector2 Skirt { get; private set; }
        public Vector2 Cape { get; private set; }
        public Vector2 Hair { get; private set; }
        public float Flap { get; private set; }
        public float Time { get; private set; }

        readonly Spring _skirt = new Spring(1.7f, 0.22f), _cape = new Spring(1.1f, 0.3f), _hair = new Spring(1.9f, 0.25f);
        readonly Pendulum[] _bag = { new Pendulum(), new Pendulum() }, _balloon = { new Pendulum(), new Pendulum() };
        float _phase, _armSpeed;
        Vector2 _lastLeft, _lastRight;
        bool _started;

        public void Reset()
        {
            _skirt.Reset();
            _cape.Reset();
            _hair.Reset();
            foreach (var p in _bag) p.Reset();
            foreach (var p in _balloon) p.Reset();
            _started = false;
        }

        public void Update(OutfitPose pose, float dt)
        {
            if (!pose.Valid || dt <= 0f) return;
            dt = Mathf.Min(dt, 0.05f);
            Time += dt;
            float t = pose.T;
            Skirt = Vector2.ClampMagnitude(_skirt.Step(pose.HipC, dt), 0.35f * t);
            Cape = Vector2.ClampMagnitude(_cape.Step(pose.ShoulderC, dt), 0.55f * t)
                   + new Vector2(Mathf.Sin(Time * 2.3f) * 0.03f * t, 0f);
            Hair = Vector2.ClampMagnitude(_hair.Step(pose.HeadCenter, dt), 0.25f * t);

            Vector2 l = pose.J[(int)Joint.LeftWrist], r = pose.J[(int)Joint.RightWrist];
            if (_started)
            {
                float speed = ((l - _lastLeft).magnitude + (r - _lastRight).magnitude) / (dt * Mathf.Max(t, 1f));
                _armSpeed = Mathf.Lerp(_armSpeed, speed, 1f - Mathf.Exp(-dt / 0.3f));
            }
            _lastLeft = l;
            _lastRight = r;
            _started = true;
            _phase += dt * 2f * Mathf.PI * (0.7f + Mathf.Min(_armSpeed, 4f) * 0.45f);
            Flap = Mathf.Sin(_phase);
            for (int side = 0; side < 2; side++)
            {
                Vector2 grip = Grip(pose, side == 1);
                _bag[side].Step(grip, 0.30f * t, new Vector2(0f, -21f * t), 0.985f, dt);
                _balloon[side].Step(grip, 1.0f * t, new Vector2(Mathf.Sin(Time * 0.8f + side) * 2f * t, 12f * t), 0.97f, dt);
            }
        }

        /// <summary>Where a hand holds things: the palm, a quarter forearm past the wrist.</summary>
        public static Vector2 Grip(OutfitPose pose, bool right)
        {
            Vector2 w = pose.J[(int)(right ? Joint.RightWrist : Joint.LeftWrist)];
            Vector2 e = pose.J[(int)(right ? Joint.RightElbow : Joint.LeftElbow)];
            return w + (w - e) * 0.25f;
        }

        public Vector2 BagDirection(bool right) => _bag[right ? 1 : 0].Direction;
        public Vector2 BalloonPosition(bool right) => _balloon[right ? 1 : 0].End;

        /// <summary>A damped spring: the offset of a point that lags behind a moving anchor.</summary>
        sealed class Spring
        {
            readonly float _omega, _zeta;
            Vector2 _pos, _vel;
            bool _init;

            public Spring(float hz, float damping)
            {
                _omega = 2f * Mathf.PI * hz;
                _zeta = damping;
            }

            public void Reset() => _init = false;

            public Vector2 Step(Vector2 anchor, float dt)
            {
                if (!_init)
                {
                    _pos = anchor;
                    _vel = Vector2.zero;
                    _init = true;
                }
                Vector2 acc = (anchor - _pos) * (_omega * _omega) - _vel * (2f * _zeta * _omega);
                _vel += acc * dt;
                _pos += _vel * dt;
                return _pos - anchor;
            }
        }

        /// <summary>A weight on a string from a moving point (Verlet): a bag hangs and swings, a balloon floats.</summary>
        sealed class Pendulum
        {
            Vector2 _end, _prev;
            bool _init;
            public Vector2 Direction { get; private set; } = Vector2.down;
            public Vector2 End => _end;

            public void Reset() => _init = false;

            public void Step(Vector2 anchor, float length, Vector2 gravity, float keep, float dt)
            {
                if (!_init)
                {
                    _end = _prev = anchor + gravity.normalized * length;
                    _init = true;
                }
                Vector2 v = (_end - _prev) * keep;
                _prev = _end;
                _end += v + gravity * (dt * dt);
                Vector2 d = _end - anchor;
                Direction = d.sqrMagnitude > 1e-4f ? d.normalized : gravity.normalized;
                _end = anchor + Direction * length;
            }
        }
    }

    /// <summary>Builds every piece's <see cref="RigFrame"/> from the pose and the motion.</summary>
    public static class OutfitRig
    {
        /// <summary>Template rows (tools/dress/draw.py).</summary>
        public const float ShoulderHalf = 0.40f, HipHalf = 0.24f, WaistY = 0.8f, KneeY = 1.92f, AnkleY = 2.85f;

        /// <summary>Depth / width of the body's cross-section for tops, skirts and the tail, the head's radius for the
        /// face and the hair over the forehead, and how far behind the axis wings, capes and back hair hang
        /// (template units), see <see cref="Turn"/>.</summary>
        public const float TorsoDepth = 0.62f, SkirtDepth = 0.9f, TailDepth = 0.85f, FaceRadius = 0.16f, HairRadius = 0.19f,
            HatRadius = 0.16f, WingDepth = 0.28f, CapeDepth = 0.22f, BackHairDepth = 0.12f;

        public static float WaistHalf => Mathf.Lerp(ShoulderHalf, HipHalf, WaistY);

        /// <summary>Fills <paramref name="f"/> for one piece (the left copy when <paramref name="left"/>). False: not shown.</summary>
        public static bool Build(PieceDef def, OutfitPose pose, OutfitMotion motion, bool left, RigFrame f)
        {
            f.Reset();
            if (!pose.Valid) return false;
            float T = pose.T;
            var J = pose.J;
            Vector2 ls = J[(int)Joint.LeftShoulder], rs = J[(int)Joint.RightShoulder];
            Vector2 lh = J[(int)Joint.LeftHip], rh = J[(int)Joint.RightHip];
            Vector2 sc = (ls + rs) * 0.5f, hc = (lh + rh) * 0.5f;
            Vector2 across = pose.Across;
            switch (def.Rig)
            {
                case Rig.Torso:
                    // the widths as when facing the camera: the turn narrows them (Turn), raised arms don't
                    f.SetRow(0, 0f, ShoulderHalf, sc, across, pose.FrontShoulderHalf);
                    f.SetRow(1, 1f, HipHalf, hc, across, pose.FrontHipHalf);
                    f.SetWrap(WrapKind.Section, pose.Yaw, TorsoDepth, 0.05f, def);
                    break;
                case Rig.Skirt:
                case Rig.Tail:
                {
                    Waist(pose, out Vector2 wc, out Vector2 wd, out float wh);
                    f.SetRow(0, WaistY, WaistHalf, wc, wd, wh);
                    Vector2 kc = (J[(int)Joint.LeftKnee] + J[(int)Joint.RightKnee]) * 0.5f;
                    Vector2 leg = kc - wc;
                    float len = Mathf.Clamp(leg.magnitude, 0.8f * T, 1.45f * T);
                    Vector2 down = leg.sqrMagnitude > 1f ? Vector2.Lerp(pose.Down, leg.normalized, 0.7f).normalized : pose.Down;
                    if (def.Rig == Rig.Skirt)
                    {
                        // rigid below the waist: knees close together must not pinch a ball gown
                        f.SetRow(1, KneeY, WaistHalf, wc + down * len, wd, wh);
                        f.SwayFrom = WaistY;
                        f.SwayLen = KneeY - WaistY;
                        f.Sway = motion.Skirt;
                        f.SetWrap(WrapKind.Section, pose.Yaw, SkirtDepth, 0.05f, def);
                    }
                    else
                    {
                        float knees = (J[(int)Joint.RightKnee] - J[(int)Joint.LeftKnee]).magnitude;
                        f.SetRow(1, KneeY, 0.29f, wc + down * len, Normal(down, wd), 0.29f * T * Mathf.Max(1f, knees / (0.5f * T)));
                        Vector2 ac = (J[(int)Joint.LeftAnkle] + J[(int)Joint.RightAnkle]) * 0.5f;
                        Vector2 shin = ac - (wc + down * len);
                        float shinLen = Mathf.Clamp(shin.magnitude, 0.6f * T, 1.3f * T);
                        Vector2 sd = shin.sqrMagnitude > 1f ? shin.normalized : down;
                        float ankles = (J[(int)Joint.RightAnkle] - J[(int)Joint.LeftAnkle]).magnitude;
                        f.SetRow(2, AnkleY, 0.16f, wc + down * len + sd * shinLen, Normal(sd, wd), 0.16f * T * Mathf.Max(1f, ankles / (0.5f * T)));
                        f.SwayFrom = KneeY;
                        f.SwayLen = AnkleY - KneeY;
                        f.Sway = motion.Skirt * 0.6f;
                        f.SetWrap(WrapKind.Section, pose.Yaw, TailDepth, 0.05f, def);
                    }
                    break;
                }
                case Rig.Arm:
                case Rig.Leg:
                {
                    bool arm = def.Rig == Rig.Arm;
                    Joint a = arm ? (left ? Joint.LeftShoulder : Joint.RightShoulder) : (left ? Joint.LeftHip : Joint.RightHip);
                    Joint b = arm ? (left ? Joint.LeftElbow : Joint.RightElbow) : (left ? Joint.LeftKnee : Joint.RightKnee);
                    Joint c = arm ? (left ? Joint.LeftWrist : Joint.RightWrist) : (left ? Joint.LeftAnkle : Joint.RightAnkle);
                    Vector2 pa = J[(int)a], pb = J[(int)b], pc = J[(int)c];
                    Vector2 d1 = Perp(pb - pa, pose.Down), d2 = Perp(pc - pb, pose.Down);
                    Vector2 mid = (d1 + d2).sqrMagnitude > 1e-4f ? (d1 + d2).normalized : d1;
                    float y1 = arm ? 0.64f : 0.92f, y2 = arm ? 1.21f : 1.85f;
                    f.SetRow(0, 0f, 1f, pa, d1, T);
                    f.SetRow(1, y1, 1f, pb, mid, T);
                    f.SetRow(2, y2, 1f, pc, d2, T);
                    f.Mirror = left;
                    break;
                }
                case Rig.Head:
                case Rig.Hair:
                case Rig.Face:
                {
                    // on the head's axis: a turned face slides across the head and the hair, hats and glasses with it
                    float s = pose.HeadScale;
                    Vector2 ax = Rot(new Vector2(1f, 0f), pose.HeadRoll), ay = Rot(new Vector2(0f, -1f), pose.HeadRoll);
                    f.SetSimilar(pose.SkullCenter, new Vector2(0f, OutfitPose.TemplateEyeY), ax * s, ay * s);
                    if (def.Rig == Rig.Face || def.Layer == Layer.Doll) f.SetWrap(WrapKind.Surface, pose.HeadYaw, 1f, FaceRadius, def);
                    else if (def.Rig == Rig.Head) f.SetWrap(WrapKind.Section, pose.HeadYaw, 1f, HatRadius, def);   // a ring around the head
                    else if (def.Layer == Layer.Back) f.SetWrap(WrapKind.Plane, pose.HeadYaw, BackHairDepth, 0f, def);
                    else f.SetWrap(WrapKind.Surface, pose.HeadYaw, 1f, HairRadius, def);
                    if (def.Rig == Rig.Hair && !float.IsInfinity(def.Hang))
                    {
                        f.HangY = def.Hang;
                        f.HangOrigin = pose.SkullCenter + ay * (s * (def.Hang - OutfitPose.TemplateEyeY));
                        f.HangAxisX = new Vector2(s, 0f);
                        f.HangAxisY = new Vector2(0f, -s);
                        f.HairSway = motion.Hair;
                    }
                    break;
                }
                case Rig.Back:
                {
                    Vector2 ax = Rot(new Vector2(1f, 0f), pose.ShoulderAngle), ay = Rot(new Vector2(0f, -1f), pose.ShoulderAngle);
                    f.SetSimilar(sc, Vector2.zero, ax * T, ay * T);
                    float angle = motion.Flap * def.Flap * Mathf.Deg2Rad;
                    f.Pivot = def.Pivot;
                    f.FlapCos = Mathf.Cos(angle);
                    f.FlapSin = Mathf.Sin(angle);
                    f.Mirror = left;
                    f.SetWrap(WrapKind.Plane, pose.Yaw, WingDepth, 0f, def);   // on the back: seen edge-on from the side
                    break;
                }
                case Rig.Cape:
                {
                    float half = Mathf.Max(pose.FrontShoulderHalf, 0.6f * ShoulderHalf * T);
                    f.SetRow(0, 0f, ShoulderHalf, sc, across, half);
                    f.SetRow(1, 1f, ShoulderHalf, sc + pose.Down * T, across, half);
                    f.SetRow(2, 2.4f, ShoulderHalf, sc + pose.Down * (2.4f * T), across, half);
                    f.SwayFrom = 0f;
                    f.SwayLen = 2.4f;
                    f.Sway = motion.Cape;
                    f.SetWrap(WrapKind.Plane, pose.Yaw, CapeDepth, 0f, def);
                    break;
                }
                case Rig.Hand:
                {
                    bool right = !left;
                    Vector2 grip = OutfitMotion.Grip(pose, right);
                    Vector2 w = J[(int)(right ? Joint.RightWrist : Joint.LeftWrist)], e = J[(int)(right ? Joint.RightElbow : Joint.LeftElbow)];
                    Vector2 fore = (w - e).sqrMagnitude > 1f ? (w - e).normalized : Vector2.down;
                    Vector2 up;
                    Vector2 origin = grip, tOrigin = Vector2.zero;
                    switch (def.Mode)
                    {
                        // a wand or a microphone points along the forearm, but never down: held up like a child
                        // holds it (a hanging arm still holds it upright, a raised arm points it up and out)
                        case HandMode.Along: up = (fore + Vector2.up * 1.2f).normalized; break;
                        case HandMode.Upright: up = (Vector2.up + fore * 0.3f).normalized; break;
                        case HandMode.Hang: up = -motion.BagDirection(right); break;
                        default:
                            Vector2 b = motion.BalloonPosition(right);
                            up = (b - grip).sqrMagnitude > 1f ? (b - grip).normalized : Vector2.up;
                            origin = b;
                            tOrigin = def.Knot;
                            break;
                    }
                    Vector2 rt = new Vector2(up.y, -up.x);
                    f.SetSimilar(origin, tOrigin, rt * T, -up * T);
                    f.Mirror = left;
                    break;
                }
            }
            f.Valid = true;
            return true;
        }

        /// <summary>The waist line: the torso's row at 0.8 of the way from the shoulders to the hips.</summary>
        public static void Waist(OutfitPose pose, out Vector2 center, out Vector2 dir, out float half)
        {
            center = Vector2.Lerp(pose.ShoulderC, pose.HipC, WaistY);
            dir = pose.Across;
            half = Mathf.Max(Mathf.Lerp(pose.FrontShoulderHalf, pose.FrontHipHalf, WaistY), 0.6f * WaistHalf * pose.T);
        }

        /// <summary>Unit vector across a bone that points down it: a hanging bone gets the screen's right.</summary>
        static Vector2 Perp(Vector2 bone, Vector2 fallbackDown)
        {
            Vector2 b = bone.sqrMagnitude > 1f ? bone.normalized : fallbackDown;
            return new Vector2(-b.y, b.x);
        }

        /// <summary>A unit vector across <paramref name="along"/>, on the same side as <paramref name="like"/>.</summary>
        static Vector2 Normal(Vector2 along, Vector2 like)
        {
            Vector2 n = new Vector2(-along.y, along.x);
            return Vector2.Dot(n, like) < 0f ? -n : n;
        }

        static Vector2 Rot(Vector2 v, float a)
        {
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }
    }
}
