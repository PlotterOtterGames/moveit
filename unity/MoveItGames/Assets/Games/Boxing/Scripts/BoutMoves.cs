using System;
using System.Collections.Generic;
using MoveIt.Motion;
using UnityEngine;
using Joint = MoveIt.Motion.Joint;

namespace MoveIt.Games.Boxing
{
    /// <summary>A punch the camera saw: when (capture time), which hand, what kind, at the body or the head, how fast.</summary>
    public struct BoutPunch
    {
        public double Time;
        public bool Right;
        public Blow Blow;
        public bool Body;
        /// <summary>Torso lengths a second (the fist in the picture).</summary>
        public float Speed;
        /// <summary>How it was seen (the log): the fist's way in the picture, the arm straightening (a second), by which
        /// rule (P = the punch detector, D = going down at the body).</summary>
        public Vector2 Direction;
        public float Extension;
        public char Rule;
    }

    /// <summary>
    /// Reads Superboks!'s moves from a front camera, like Wii Sports Boxing's and Kinect Sports Boxing's, forgiving and
    /// robust against the pose model's glitches (the guard puts the hands in front of the face and chest, where the box's
    /// model makes a wrist jump onto its elbow for a frame or a few: docs/hardware.md):
    /// <list type="bullet">
    /// <item><b>Punches</b>: <see cref="PunchDetector"/> (any quick move of a fist; a jab straight at the TV by the arm
    /// straightening in 3D) fed with a median of three of each joint, so a one-frame glitch is never a punch. Its kind
    /// from the fist's way: up = an uppercut, across toward the other side = a hook, else straight; at the body when the
    /// fist is low (<see cref="BodyBelow"/>). Both fists going up together is raising the arms, no punch.</item>
    /// <item><b>The guard</b>: both wrists in front of the face (from just under the shoulders' line to above the head,
    /// near the middle), the arms bent (in 3D when the camera sends world joints): held <see cref="PoseSamples"/>
    /// samples.</item>
    /// <item><b>Leaning</b> to a side: the shoulders' middle out over the hips (a lean) or away from where it stands (a
    /// step), with a gap between on and off; <b>ducking</b>: the shoulders lower than where they stand. Where it stands
    /// follows the player slowly (quickly while they are neither leaning nor ducking), so stepping to a new place isn't a
    /// lean for long.</item>
    /// <item><b>Both arms up</b> (the wrists well above the head): the super punch.</item>
    /// </list>
    /// In torso lengths, so the same numbers fit a child and an adult at any distance. The poses come out a sample late
    /// (the median), <see cref="Settled"/> says up to when. Pure logic: tests feed it simulated bodies.
    /// </summary>
    public sealed class BoutMoves
    {
        /// <summary>A lean: the shoulders' middle this far (torso lengths) out over the hips' middle, on (and off).</summary>
        public float TiltOn = 0.2f, TiltOff = 0.12f;
        /// <summary>...or this far from where the player stands.</summary>
        public float ShiftOn = 0.32f, ShiftOff = 0.18f;
        /// <summary>A duck: the shoulders this far below where they stand (on, off).</summary>
        public float DuckOn = 0.28f, DuckOff = 0.16f;
        /// <summary>The guard: both wrists between these heights relative to the shoulders' line, within
        /// <see cref="GuardAcross"/> of their middle, the arms bent (straightness under <see cref="GuardBent"/>).</summary>
        public float GuardLow = -0.32f, GuardHigh = 0.85f, GuardAcross = 0.7f, GuardBent = 0.86f;
        /// <summary>Both arms up: both wrists this far above the shoulders' line.</summary>
        public float ArmsUpAbove = 0.75f;
        /// <summary>A pose counts once seen in this many samples in a row (after the median).</summary>
        public int PoseSamples = 2;
        public float PoseVisibility = 0.25f;
        /// <summary>A fist this far below the shoulders' line (torso lengths) punches at the body.</summary>
        public float BodyBelow = -0.38f;
        /// <summary>Up share of a punch's way that makes it an uppercut, across share (toward the other side) a hook.</summary>
        public float UpShare = 0.62f, HookShare = 0.7f;
        /// <summary>Where the player stands follows them at this rate (per second), and this rate while leaning or ducking
        /// (a step to a new place stops reading as a lean within a few seconds; a dodge lasts well under one).</summary>
        public float Follow = 0.8f, FollowAway = 0.45f;
        /// <summary>The arm straightening this fast (its straightness per second, 3D) when a punch is seen makes it a
        /// straight punch, whichever way the fist moved in the picture (a jab at a camera below the face rises in the
        /// picture).</summary>
        public float StraightExtension = 1.4f;
        /// <summary>An uppercut's arm straightens slower than this (straightness a second).</summary>
        public float UpperMaxExtension = 2.2f;
        /// <summary>A punch must be carried by more than one step: no step of the last three more than this share of their
        /// sum (a glitch is all in one step; the median of three lets one of two frames through).</summary>
        public float MaxStepShare = 0.72f;

        readonly PunchDetector _punch = new PunchDetector();
        public PunchDetector Punches => _punch;

        static readonly Joint[] Used =
        {
            Joint.Nose, Joint.LeftShoulder, Joint.RightShoulder, Joint.LeftElbow, Joint.RightElbow, Joint.LeftWrist, Joint.RightWrist,
            Joint.LeftHip, Joint.RightHip,
        };

        // the last three samples of each joint used (picture, world, visibility) and their times
        readonly double[] _t = new double[3];
        readonly Vector3[,] _p = new Vector3[3, Skeleton.JointCount], _w = new Vector3[3, Skeleton.JointCount];
        readonly float[,] _v = new float[3, Skeleton.JointCount];
        int _count;
        readonly Vector3[] _mp = new Vector3[Skeleton.JointCount], _mw = new Vector3[Skeleton.JointCount];
        readonly float[] _mv = new float[Skeleton.JointCount];

        bool _based;
        float _torso, _baseX, _baseY;
        int _guardRun, _upRun;
        double _lastAt = double.NegativeInfinity;

        /// <summary>The pose now (counted) and its continuous parts (for drawing): how far leaned (-1..1 of a full lean,
        /// signed) and ducked (0..1).</summary>
        public HeroPose Pose { get; private set; }
        public float LeanAmount { get; private set; }
        /// <summary>A torso length in the fight's metres (<see cref="HeroPose.HeadX"/>): an adult's is about half a metre;
        /// a child's lean of the same share of its body moves its head as far in the fight.</summary>
        public const float MetresPerTorso = 0.5f;
        public float DuckAmount { get; private set; }
        /// <summary>The wrists relative to the shoulders' middle (torso lengths, x = the screen's right, y up), median.</summary>
        public Vector2 LeftWrist { get; private set; }
        public Vector2 RightWrist { get; private set; }
        /// <summary>Every move up to this capture time has been reported.</summary>
        public double Settled { get; private set; } = double.NegativeInfinity;
        /// <summary>Torso length (picture units) the moves are measured in.</summary>
        public float Torso => _torso;
        /// <summary>Punches dropped because both fists went up together (raising the arms).</summary>
        public int RaisesIgnored { get; private set; }
        /// <summary>Punches dropped as the pose model's glitches (a jump in one step).</summary>
        public int GlitchesIgnored { get; private set; }

        /// <summary>A pending punch is real if the hand's next step goes on the same way by at least this share of the
        /// step it was seen on.</summary>
        public float ContinueShare = 0.3f;
        /// <summary>A punch at the body from the guard (the picture shows it going down): the fist ends at least this far
        /// below its shoulder, its arm in the picture no longer than <see cref="ShortArm"/> (torso lengths; dropped arms
        /// hang ~1.2 long).</summary>
        public float DownEnds = 0.3f, ShortArm = 0.9f;
        /// <summary>...seen this fast (torso lengths a second): quicker than a punch elsewhere, since lowering a fist (to
        /// get an uppercut ready, to rest) looks just like it, only slower.</summary>
        public float DownSpeed = 3.5f;
        /// <summary>With world joints: seen this fast, the arm straightening by this much (its straightness) over the
        /// last three samples.</summary>
        public float DownSpeedWorld = 2.6f, DownStraighten = 0.12f;
        /// <summary>...and the fist going forward (toward the camera) by this much (world torso lengths, the depth as squeezed
        /// as the camera service sends it) over those samples: a fist dropped to get an uppercut ready goes down, not forward.</summary>
        public float DownForward = 0.12f;
        /// <summary>...starting no higher than this above its shoulder (torso lengths: the chin; arms lowered from above the
        /// head pass the same way).</summary>
        public float DownFrom = 0.6f;
        /// <summary>A punch seen only by the arm straightening (3D) must also move in the picture at least this fast
        /// (torso lengths a second): world joints alone jitter.</summary>
        public float StraightMinSpeed = 0.6f;
        /// <summary>No second punch of a hand within this long (s) of one seen going down.</summary>
        public float DownCooldown = 0.3f;
        /// <summary>Within this long (s) after a punch, the same fist heading back toward where it came from (its
        /// shoulder, the guard) is the way back, not a punch (from a punch at the body that is upward, like an
        /// uppercut).</summary>
        public float BackSeconds = 0.7f;

        // per hand, the last four medians: the wrist relative to its shoulder (torso lengths), the arm's straightness; their
        // times
        readonly Vector2[,] _rel = new Vector2[2, 4];
        readonly float[,] _reach = new float[2, 4], _fwd = new float[2, 4];
        readonly double[] _times = new double[4];
        int _history;
        readonly Pending[] _pending = new Pending[2];
        readonly double[] _lastPunch = { double.NegativeInfinity, double.NegativeInfinity };
        readonly bool[] _lastDown = new bool[2];
        readonly Vector2[] _from = new Vector2[2];

        public void Reset()
        {
            _punch.Reset();
            _count = 0;
            _based = false;
            _guardRun = _upRun = 0;
            _history = 0;
            _pending[0] = _pending[1] = default;
            _lastPunch[0] = _lastPunch[1] = double.NegativeInfinity;
            Pose = default;
            LeanAmount = DuckAmount = 0f;
            Settled = double.NegativeInfinity;
            _lastAt = double.NegativeInfinity;
        }

        /// <summary>Where the player stands now is the middle (a fight's start).</summary>
        public void Recenter() => _based = false;

        /// <summary>
        /// One camera sample (once per sample, its capture time): the joints as the camera saw them (raw, mirrored view
        /// space: x = the screen's right, y up), their visibility, the world joints (metres, y up) when
        /// <paramref name="hasWorld"/>. Punches seen go to <paramref name="punches"/>; <see cref="Pose"/> is the pose of
        /// the sample a step back (the median).
        /// </summary>
        public void Update(double time, Vector3[] raw, float[] visibility, Vector3[] world, bool hasWorld, List<BoutPunch> punches)
        {
            if (_count > 0 && time <= _t[(_count - 1)]) return;
            if (_count == 3)
            {
                for (int k = 0; k < 2; k++)
                {
                    _t[k] = _t[k + 1];
                    foreach (var j in Used)
                    {
                        int i = (int)j;
                        _p[k, i] = _p[k + 1, i];
                        _w[k, i] = _w[k + 1, i];
                        _v[k, i] = _v[k + 1, i];
                    }
                }
                _count = 2;
            }
            _t[_count] = time;
            foreach (var j in Used)
            {
                int i = (int)j;
                _p[_count, i] = raw[i];
                _w[_count, i] = hasWorld && world != null ? world[i] : Vector3.zero;
                _v[_count, i] = visibility[i];
            }
            _count++;
            if (_count < 3) return;
            double at = _t[1];
            bool gap = _t[2] - _t[1] > 0.25 || _t[1] - _t[0] > 0.25;
            foreach (var j in Used)
            {
                int i = (int)j;
                if (gap)
                {
                    _mp[i] = _p[1, i];
                    _mw[i] = _w[1, i];
                    _mv[i] = _v[1, i];
                    continue;
                }
                _mp[i] = Mid3(_p[0, i], _p[1, i], _p[2, i]);
                _mw[i] = Mid3(_w[0, i], _w[1, i], _w[2, i]);
                _mv[i] = Mathf.Min(_v[1, i], Mathf.Max(_v[0, i], _v[2, i]));
            }
            Sample(at, hasWorld, punches);
        }

        void Sample(double at, bool hasWorld, List<BoutPunch> punches)
        {
            Vector2 sl = _mp[(int)Joint.LeftShoulder], sr = _mp[(int)Joint.RightShoulder];
            Vector2 hl = _mp[(int)Joint.LeftHip], hr = _mp[(int)Joint.RightHip];
            var shoulders = (sl + sr) * 0.5f;
            var hips = (hl + hr) * 0.5f;
            bool hipsSeen = _mv[(int)Joint.LeftHip] >= 0.3f && _mv[(int)Joint.RightHip] >= 0.3f;
            bool shouldersSeen = _mv[(int)Joint.LeftShoulder] >= 0.3f && _mv[(int)Joint.RightShoulder] >= 0.3f;
            if (!shouldersSeen) return;
            float dt = double.IsNegativeInfinity(_lastAt) ? 0f : (float)Math.Min(0.25, at - _lastAt);
            _lastAt = at;
            // the torso's length: the shoulders to the hips when the hips are seen, else what it was (or the shoulders'
            // width, roughly half of it, for a start)
            float measured = hipsSeen ? Vector2.Distance(shoulders, hips) : Vector2.Distance(sl, sr) * 1.9f;
            if (_torso <= 0f) _torso = measured;
            else if (hipsSeen) _torso += (measured - _torso) * Mathf.Min(1f, dt * 2f);
            float T = Mathf.Max(_torso, 1e-4f);
            if (!_based)
            {
                _baseX = shoulders.x;
                _baseY = shoulders.y;
                _based = true;
            }

            // leaning: over the hips, or away from where the player stands
            float tilt = hipsSeen ? (shoulders.x - hips.x) / T : 0f;
            float shift = (shoulders.x - _baseX) / T;
            float lean = Mathf.Abs(tilt) / TiltOn >= Mathf.Abs(shift) / ShiftOn ? tilt / TiltOn : shift / ShiftOn;
            int side = Pose.Lean;
            if (side == 0)
            {
                if (Mathf.Abs(tilt) >= TiltOn || Mathf.Abs(shift) >= ShiftOn) side = lean > 0f ? 1 : -1;
            }
            else if (Mathf.Sign(tilt) != side && Mathf.Sign(shift) != side || Mathf.Abs(tilt) < TiltOff && Mathf.Abs(shift) < ShiftOff)
                side = 0;
            LeanAmount = Mathf.Clamp(lean, -1.5f, 1.5f) / 1.5f;

            // ducking: the shoulders lower than where they stand
            float drop = (_baseY - shoulders.y) / T;
            bool duck = Pose.Duck ? drop >= DuckOff : drop >= DuckOn;
            DuckAmount = Mathf.Clamp01(drop / (DuckOn * 1.6f));

            // where the player stands follows them, slowly while they lean or duck
            float rate = side == 0 && !duck ? Follow : FollowAway;
            float k = Mathf.Min(1f, dt * rate);
            _baseX += (shoulders.x - _baseX) * k;
            if (!duck && drop < DuckOff) _baseY += (shoulders.y - _baseY) * k;
            else _baseY += (shoulders.y - _baseY) * Mathf.Min(1f, dt * FollowAway);

            // the gloves: the guard, both arms up
            var lw = ((Vector2)_mp[(int)Joint.LeftWrist] - shoulders) / T;
            var rw = ((Vector2)_mp[(int)Joint.RightWrist] - shoulders) / T;
            LeftWrist = lw;
            RightWrist = rw;
            bool wristsSeen = _mv[(int)Joint.LeftWrist] >= PoseVisibility && _mv[(int)Joint.RightWrist] >= PoseVisibility;
            bool guard = wristsSeen && Guards(lw, true, hasWorld) && Guards(rw, false, hasWorld);
            bool up = wristsSeen && lw.y >= ArmsUpAbove && rw.y >= ArmsUpAbove;
            _guardRun = guard ? _guardRun + 1 : 0;
            _upRun = up ? _upRun + 1 : 0;

            Pose = new HeroPose
            {
                Guard = _guardRun >= PoseSamples,
                Lean = side,
                Duck = duck,
                ArmsUp = _upRun >= PoseSamples + 1,
                // where the head and the hips are against where the player stands, in the fight's metres (a torso length
                // is half a metre: the same move counts the same for a child and an adult)
                HeadX = shift * MetresPerTorso,
                HeadDrop = Mathf.Max(0f, drop) * MetresPerTorso,
                BodyX = (shift - tilt) * MetresPerTorso,
            };

            // punches, on the medians
            ArmSample Arm(bool right)
            {
                var s = right ? Joint.RightShoulder : Joint.LeftShoulder;
                var e = right ? Joint.RightElbow : Joint.LeftElbow;
                var w = right ? Joint.RightWrist : Joint.LeftWrist;
                return new ArmSample
                {
                    Shoulder = _mp[(int)s], Elbow = _mp[(int)e], Wrist = _mp[(int)w],
                    WorldShoulder = _mw[(int)s], WorldElbow = _mw[(int)e], WorldWrist = _mw[(int)w],
                    Visibility = Mathf.Min(_mv[(int)e], _mv[(int)w]),
                };
            }
            float worldTorso = 0f;
            if (hasWorld)
            {
                var ws = (_mw[(int)Joint.LeftShoulder] + _mw[(int)Joint.RightShoulder]) * 0.5f;
                var wh = (_mw[(int)Joint.LeftHip] + _mw[(int)Joint.RightHip]) * 0.5f;
                worldTorso = Vector3.Distance(ws, wh);
            }
            // each hand's last moves: the wrist relative to its shoulder in the picture, the arm's straightness
            for (int h = 0; h < 2; h++)
            {
                for (int q = 3; q > 0; q--)
                {
                    _rel[h, q] = _rel[h, q - 1];
                    _reach[h, q] = _reach[h, q - 1];
                    _fwd[h, q] = _fwd[h, q - 1];
                }
                var arm = Arm(h == 1);
                _rel[h, 0] = ((Vector2)arm.Wrist - (Vector2)arm.Shoulder) / T;
                _reach[h, 0] = hasWorld ? Straightness(arm.WorldShoulder, arm.WorldElbow, arm.WorldWrist) : 0f;
                // forward of the shoulders' middle (both shoulders: half the noise of one)
                _fwd[h, 0] = hasWorld && worldTorso > 1e-3f
                    ? -(arm.WorldWrist.z - (_mw[(int)Joint.LeftShoulder].z + _mw[(int)Joint.RightShoulder].z) * 0.5f) / worldTorso : 0f;
            }
            for (int q = 3; q > 0; q--) _times[q] = _times[q - 1];
            _times[0] = at;
            _history = Math.Min(_history + 1, 4);

            // a punch waiting for its next sample: real if the hand carries on the same way, a glitch if it stops or goes back
            for (int h = 0; h < 2; h++)
            {
                if (!_pending[h].Active) continue;
                var pend = _pending[h];
                _pending[h].Active = false;
                bool goesOn = Vector2.Dot(_rel[h, 0] - _rel[h, 1], pend.Step) >= ContinueShare * pend.Step.sqrMagnitude
                              || pend.ByReach && pend.ReachStep > 0.02f && _reach[h, 0] - _reach[h, 1] >= ContinueShare * pend.ReachStep;
                if (goesOn) Emit(pend.Punch, punches, pend.Down);
                else GlitchesIgnored++;
            }

            bool[] seenNow = new bool[2];
            if (_punch.Update(at, Arm(false), Arm(true), T, worldTorso, out var info))
            {
                int h = info.RightHand ? 1 : 0;
                if (info.BothHands && info.Direction.y >= UpShare)
                    RaisesIgnored++;   // both fists up together: raising the arms (the super, the guard), no punch
                else if (info.Direction.y >= 0.5f && OtherMoves(h, up: true))
                    RaisesIgnored++;   // both arms going up, a sample apart
                else if (info.Straightened && info.Speed < StraightMinSpeed)
                    GlitchesIgnored++;   // only the world joints moved: their noise
                else if (!(at - _lastPunch[h] < BackSeconds &&
                           (_lastDown[h] && (at - _lastPunch[h] < DownCooldown || info.Direction.y > 0.5f) || GoesBack(h, info.Direction))))
                {
                    // (not the fist coming back from a punch)
                    var wrist = info.RightHand ? rw : lw;
                    Blow blow = Kind(info, wrist);
                    var punch = new BoutPunch
                    {
                        Time = at, Right = info.RightHand, Blow = blow, Body = blow != Blow.Uppercut && wrist.y <= BodyBelow, Speed = info.Speed,
                        Direction = info.Direction, Extension = info.Extension, Rule = 'P',
                    };
                    seenNow[h] = true;
                    // a punch seen with the arm straightening fast is checked on that (a jab barely moves in the picture)
                    bool byReach = info.Straightened || hasWorld && info.Extension >= StraightExtension;
                    if (Carried(h, byReach)) Emit(punch, punches, false);
                    else _pending[h] = PendingOf(h, punch, byReach, false);
                }
            }

            // a punch at the body from the guard: the picture shows the fist going down fast, like arms being dropped (which
            // the punch detector leaves alone), but dropped arms hang long beside the body while a punch ends low in front,
            // the arm short in the picture (it points at the camera)
            if (_history >= 4)
                for (int h = 0; h < 2; h++)
                {
                    if (seenNow[h] || _pending[h].Active || at - _lastPunch[h] < DownCooldown) continue;
                    float span = (float)(_times[0] - _times[3]);
                    if (span < 0.05f) continue;
                    var move = _rel[h, 0] - _rel[h, 3];
                    float speed = move.magnitude / span;
                    // with world joints the arm must straighten as it goes (lowering a fist to get an uppercut ready doesn't)
                    float straighten = _reach[h, 0] - _reach[h, 3];
                    if (hasWorld ? speed < DownSpeedWorld || straighten < DownStraighten || _reach[h, 0] < 0.72f : speed < DownSpeed) continue;
                    if (-move.y < 0.7f * move.magnitude) continue;
                    var end_ = _rel[h, 0];
                    if (end_.y > -DownEnds || end_.magnitude > ShortArm) continue;
                    if (_rel[h, 3].y > DownFrom || OtherMoves(h, up: false)) continue;   // arms lowered from above, both arms
                    if (hasWorld && _fwd[h, 0] - _fwd[h, 3] < DownForward) continue;      // dropped to get an uppercut ready
                    if (_mv[(int)(h == 1 ? Joint.RightWrist : Joint.LeftWrist)] < 0.3f) continue;
                    var punch = new BoutPunch
                    {
                        Time = at, Right = h == 1, Blow = Blow.Straight, Body = true, Speed = speed, Direction = move.normalized,
                        Extension = straighten / span, Rule = 'D',
                    };
                    if (Carried(h, false)) Emit(punch, punches, true);
                    else _pending[h] = PendingOf(h, punch, false, true);
                }
            Settled = at;

            bool Guards(Vector2 wrist, bool left, bool world)
            {
                if (wrist.y < GuardLow || wrist.y > GuardHigh || Mathf.Abs(wrist.x) > GuardAcross) return false;
                if (!world) return true;
                var s = _mw[(int)(left ? Joint.LeftShoulder : Joint.RightShoulder)];
                var e = _mw[(int)(left ? Joint.LeftElbow : Joint.RightElbow)];
                var w = _mw[(int)(left ? Joint.LeftWrist : Joint.RightWrist)];
                float bones = Vector3.Distance(s, e) + Vector3.Distance(e, w);
                return bones < 1e-4f || Vector3.Distance(s, w) / bones < GuardBent;
            }
        }

        /// <summary>A real move is carried by more than one sample: the last three steps of hand <paramref name="h"/> (in
        /// the picture, or of the arm's straightening) aren't all in one of them. A punch
        /// that is (a glitch, or a quick real punch caught at its first step) waits a sample to see whether it goes on.</summary>
        bool Carried(int h, bool byReach)
        {
            if (_history < 4) return true;
            return Spread(h, false) || byReach && Spread(h, true);
        }

        /// <summary>The last three steps of hand <paramref name="h"/> (in the picture, or of the arm's straightening) aren't
        /// all in one of them.</summary>
        bool Spread(int h, bool reach)
        {
            float sum = 0f, max = 0f;
            for (int k = 0; k < 3; k++)
            {
                float step = reach ? Mathf.Max(0f, _reach[h, k] - _reach[h, k + 1]) : (_rel[h, k] - _rel[h, k + 1]).magnitude;
                sum += step;
                max = Mathf.Max(max, step);
            }
            return sum > 1e-5f && max <= MaxStepShare * sum;
        }

        /// <summary>A punch caught at a step that was most of its move: it waits for the next sample, which must go on the
        /// way that biggest step went.</summary>
        Pending PendingOf(int h, BoutPunch punch, bool byReach, bool down)
        {
            var p = new Pending { Active = true, Punch = punch, ByReach = byReach, Down = down };
            for (int k = 0; k < 3; k++)
            {
                var step = _rel[h, k] - _rel[h, k + 1];
                if (step.sqrMagnitude > p.Step.sqrMagnitude) p.Step = step;
                p.ReachStep = Mathf.Max(p.ReachStep, _reach[h, k] - _reach[h, k + 1]);
            }
            return p;
        }

        /// <summary>The other hand went the same way (up, or down) over the last three samples, at least half as far: both
        /// arms rising or lowering.</summary>
        bool OtherMoves(int h, bool up)
        {
            if (_history < 4) return false;
            float sign = up ? 1f : -1f;
            float mine = sign * (_rel[h, 0].y - _rel[h, 3].y), other = sign * (_rel[1 - h, 0].y - _rel[1 - h, 3].y);
            return mine > 0.1f && other >= 0.5f * mine;
        }

        void Emit(BoutPunch punch, List<BoutPunch> punches, bool down)
        {
            int h = punch.Right ? 1 : 0;
            _lastPunch[h] = punch.Time;
            _lastDown[h] = down;
            _from[h] = _rel[h, Math.Min(3, _history - 1)];
            punches?.Add(punch);
        }

        /// <summary>The fist moving <paramref name="direction"/> heads back toward where its last punch started (the
        /// guard, the shoulder): the way back.</summary>
        bool GoesBack(int h, Vector2 direction)
        {
            var back = _from[h] - _rel[h, 1];
            return back.magnitude > 0.12f && Vector2.Dot(direction, back.normalized) > 0.5f;
        }

        struct Pending
        {
            public bool Active, ByReach, Down;
            public BoutPunch Punch;
            public Vector2 Step;
            public float ReachStep;
        }

        static float Straightness(Vector3 s, Vector3 e, Vector3 w)
        {
            float bones = Vector3.Distance(s, e) + Vector3.Distance(e, w);
            return bones > 1e-5f ? Vector3.Distance(s, w) / bones : 1f;
        }

        /// <summary>A punch's kind from the fist's way when it was seen: the arm straightening fast = straight (a jab at
        /// the TV); else up = an uppercut, across toward the body's other side = a hook (a left fist going right), else
        /// straight.</summary>
        public Blow Kind(PunchInfo info, Vector2 wrist)
        {
            var d = info.Direction;
            if (info.Straightened || d.sqrMagnitude < 0.25f) return Blow.Straight;
            // clearly up, the arm not shooting straight (a jab at a low camera rises too, but straightens fast)
            if (d.y >= UpShare && info.Extension < UpperMaxExtension) return Blow.Uppercut;
            if (info.Extension >= StraightExtension) return Blow.Straight;
            float across = info.RightHand ? -d.x : d.x;
            return across >= HookShare ? Blow.Hook : Blow.Straight;
        }

        static Vector3 Mid3(Vector3 a, Vector3 b, Vector3 c) => new Vector3(Mid(a.x, b.x, c.x), Mid(a.y, b.y, c.y), Mid(a.z, b.z, c.z));
        static float Mid(float a, float b, float c) => Mathf.Max(Mathf.Min(a, b), Mathf.Min(Mathf.Max(a, b), c));
    }
}
