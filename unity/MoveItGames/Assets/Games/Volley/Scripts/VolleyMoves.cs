using System;
using System.Collections.Generic;
using MoveIt.Games.Tennis;
using UnityEngine;

namespace MoveIt.Games.Volley
{
    /// <summary>What the player's hands do, as the volleyball reads it (2026-11-07, user: "when the player raises 2 hands it
    /// raises the ball for another person, if they hit with 1 hand then they hit on the other side; depending on the speed
    /// of the arm, the speed of the hit; to defend the player must do another movement").</summary>
    public enum Move
    {
        None,
        /// <summary>The defend move: both hands together, low in front (the forearm pass, Kinect Sports' "bump"). A ball
        /// coming over the net is received only with it.</summary>
        Bump,
        /// <summary>Both hands up: the ball goes up for the teammate.</summary>
        Set,
        /// <summary>A quick move of one hand: the ball goes over the net, as fast as the hand went.</summary>
        Hit,
    }

    /// <summary>One move the camera saw: a pose in one sample (it repeats every sample it is held), or a hit.</summary>
    public struct MoveEvent
    {
        public Move Move;
        /// <summary>Capture time (the camera's clock): the sample a pose was seen in, a hit's moment (its start plus
        /// <see cref="SwingDetector.ContactLead"/>).</summary>
        public double Time;
        /// <summary>A hit: how fast the hand went (torso lengths per second) and which way on the (mirrored) screen
        /// (x = the screen's right, y = up), and which hand.</summary>
        public float Speed;
        public Vector2 Direction;
        public bool RightHand;
    }

    /// <summary>
    /// Reads the volleyball's three moves from a front camera, forgiving and robust against the pose model's glitches:
    /// <list type="bullet">
    /// <item><b>Poses</b> (<see cref="Move.Bump"/>, <see cref="Move.Set"/>) are what the shoulders, the wrists and the
    /// distances between them show (the most stable things the model gives): both wrists well up (above the shoulders by
    /// <see cref="SetAbove"/> torso lengths, about the nose) = set; both well down (<see cref="BumpBelow"/>), close
    /// together (<see cref="BumpApart"/>) and in front of the body (<see cref="BumpCentre"/>) = the bump. Arms hanging at
    /// the sides are ~0.7 torso lengths apart: no bump. Each wrist goes through a median of three samples (a one-frame
    /// glitch never gets through) and a pose counts once seen in <see cref="PoseSamples"/> samples in a row. A pose is
    /// reported every sample it is held: the rules take one seen any time before the ball arrives, so players can get
    /// ready early, which also hides the camera's lag (two players get half the samples each on the TV box).</item>
    /// <item><b>Hits</b> are Tenis!'s glitch-proof swings (<see cref="SwingDetector"/>: raw samples, a median of three,
    /// a move carried over several frames, not all in one step, not straight back), from either hand and any way (two
    /// detectors, one per hand), made with one hand: the other hand moved less than <see cref="StillShare"/> of the way
    /// (raising both hands for a set or bringing them together for a bump moves both: no hit), and not steeply up
    /// (<see cref="UpLimit"/>: raising the hand to hit is the wind-up; the hit is the swing after it, down or across).</item>
    /// </list>
    /// <see cref="Settled"/> is the capture time up to which every move has been reported (the median is a sample late, a
    /// swing is reported once its fastest moment has passed): the rules wait for it before they decide a touch that
    /// hasn't been made yet. In torso lengths, so the same numbers fit a child and an adult at any distance. Pure logic:
    /// tests feed it samples.
    /// </summary>
    public sealed class VolleyMoves
    {
        /// <summary>Both wrists this far above the shoulders' line (torso lengths): about the nose.</summary>
        public float SetAbove = 0.3f;
        /// <summary>Both wrists this far below the shoulders' line...</summary>
        public float BumpBelow = 0.3f;
        /// <summary>...at most this far apart (across: arms hanging at the sides are ~0.7)...</summary>
        public float BumpApart = 0.5f;
        /// <summary>...and their middle within this of the shoulders' middle (across).</summary>
        public float BumpCentre = 0.55f;
        /// <summary>A pose needs both wrists at least this sure (a hand hidden behind the other is less sure, still roughly
        /// where it is).</summary>
        public float PoseVisibility = 0.25f;
        /// <summary>A pose counts once seen in this many samples in a row (after the median: a glitch of one or two frames
        /// gets through a median of three for a sample or two, never three; a held pose waits 0.1 s, at 15 samples a second
        /// 0.2 s).</summary>
        public int PoseSamples = 3;
        /// <summary>A hit is one hand's: the other moved less than this share of the hitting hand's way meanwhile.</summary>
        public float StillShare = 0.7f;
        /// <summary>The hands' moves before a hit's start that count as part of it (s).</summary>
        public float HitLookBack = 0.12f;
        /// <summary>A hand going up more steeply than this (its direction's up share: 0.7 = 45 degrees) is getting ready to
        /// hit (the wind-up of a spike or a serve, raising a hand), not a hit.</summary>
        public float UpLimit = 0.7f;

        /// <summary>One swing detector per hand: each reports only its own hand's swings, any way (no free hand).</summary>
        readonly SwingDetector _right = new SwingDetector { RightHanded = true, VerticalLimit = 0f };
        readonly SwingDetector _left = new SwingDetector { RightHanded = false, VerticalLimit = 0f };

        /// <summary>The pose now (counted), and since when (capture time).</summary>
        public Move Pose { get; private set; }
        public double PoseSince { get; private set; }
        /// <summary>Every move that started before this capture time has been reported.</summary>
        public double Settled { get; private set; } = double.NegativeInfinity;
        /// <summary>Moves of the hands taken for the pose model's glitches and ignored.</summary>
        public int Ignored => _right.Ignored + _left.Ignored;
        /// <summary>Hits dropped because both hands moved (on the way into a pose, or out of it).</summary>
        public int TwoHanded { get; private set; }
        /// <summary>A hand's quick moves up (getting ready to hit): no hits.</summary>
        public int WindUps { get; private set; }
        /// <summary>The swing detectors (tests and tuning: thresholds, speeds).</summary>
        public SwingDetector RightSwings => _right;
        public SwingDetector LeftSwings => _left;

        // a median of three per wrist (relative to the shoulders, torso lengths, y up), a sample late
        readonly double[] _t = new double[3];
        readonly Vector2[] _l = new Vector2[3], _r = new Vector2[3];
        readonly float[] _lv = new float[3], _rv = new float[3];
        int _count;
        // the medians of the last second: the hands' ways for the one-hand check
        readonly List<(double time, Vector2 left, Vector2 right)> _history = new List<(double, Vector2, Vector2)>();
        Move _candidate;
        int _candidateSamples;

        public void Reset()
        {
            _right.Reset();
            _left.Reset();
            _count = 0;
            _history.Clear();
            Pose = Move.None;
            _candidate = Move.None;
            _candidateSamples = 0;
            Settled = double.NegativeInfinity;
        }

        /// <summary>
        /// One camera sample (once per sample, its capture time): the shoulders' middle, the torso length (view units) and
        /// both wrists with their visibility, as the camera saw them (raw). Adds what it saw to <paramref name="seen"/>: the
        /// pose held now (one event every sample) and a hit.
        /// </summary>
        public void Update(double time, Vector2 shoulders, float torso, Vector2 leftWrist, float leftVisibility, Vector2 rightWrist,
                           float rightVisibility, List<MoveEvent> seen)
        {
            if (torso < 1e-4f) return;
            bool r = _right.Update(time, shoulders, torso, leftWrist, leftVisibility, rightWrist, rightVisibility, out var rs);
            bool l = _left.Update(time, shoulders, torso, leftWrist, leftVisibility, rightWrist, rightVisibility, out var ls);

            // the poses, on the medians of the wrists (the same sample the swing detectors work on)
            if (!Median(time, (leftWrist - shoulders) / torso, leftVisibility, (rightWrist - shoulders) / torso, rightVisibility,
                        out double at, out var lp, out float lv, out var rp, out float rv))
                return;
            _history.Add((at, lp, rp));
            while (_history.Count > 0 && at - _history[0].time > 1.0) _history.RemoveAt(0);
            var pose = Classify(lp, lv, rp, rv);
            if (pose == _candidate) _candidateSamples++;
            else
            {
                _candidate = pose;
                _candidateSamples = 1;
            }
            var counted = _candidate != Move.None && _candidateSamples >= PoseSamples ? _candidate : Move.None;
            if (counted != Pose)
            {
                Pose = counted;
                PoseSince = at;
            }
            if (Pose != Move.None) seen?.Add(new MoveEvent { Move = Pose, Time = at });

            // a hit: either hand's swing, made with one hand
            if (r && rs.RightHand) Hit(rs, seen);
            if (l && !ls.RightHand) Hit(ls, seen);
            Settled = Math.Min(at, Math.Min(_right.PendingSince, _left.PendingSince));
        }

        void Hit(SwingEvent s, List<MoveEvent> seen)
        {
            if (s.Direction.y > UpLimit)
            {
                WindUps++;
                return;
            }
            if (!OneHanded(s))
            {
                TwoHanded++;
                return;
            }
            seen?.Add(new MoveEvent { Move = Move.Hit, Time = s.Time, Speed = s.Speed, Direction = s.Direction, RightHand = s.RightHand });
        }

        /// <summary>The swinging hand went further than the other one (by <see cref="StillShare"/>) since the swing's start
        /// (a little before it): both hands moving is a pose being made or undone, not a hit.</summary>
        bool OneHanded(SwingEvent s)
        {
            if (_history.Count < 2) return true;
            double from = s.Time - _right.ContactLead - HitLookBack;
            int first = 0;
            while (first < _history.Count - 1 && _history[first].time < from) first++;
            if (first > 0) first--;
            var a = _history[first];
            var b = _history[_history.Count - 1];
            float hitting = ((s.RightHand ? b.right : b.left) - (s.RightHand ? a.right : a.left)).magnitude;
            float other = ((s.RightHand ? b.left : b.right) - (s.RightHand ? a.left : a.right)).magnitude;
            return other < StillShare * Mathf.Max(hitting, 0.05f);
        }

        /// <summary>The pose two wrists (relative to the shoulders' middle, torso lengths, y up) make.</summary>
        public Move Classify(Vector2 left, float leftVisibility, Vector2 right, float rightVisibility)
        {
            if (leftVisibility < PoseVisibility || rightVisibility < PoseVisibility) return Move.None;
            if (left.y >= SetAbove && right.y >= SetAbove) return Move.Set;
            if (left.y <= -BumpBelow && right.y <= -BumpBelow && Mathf.Abs(left.x - right.x) <= BumpApart &&
                Mathf.Abs((left.x + right.x) * 0.5f) <= BumpCentre)
                return Move.Bump;
            return Move.None;
        }

        bool Median(double time, Vector2 l, float lv, Vector2 r, float rv, out double at, out Vector2 lp, out float lvis, out Vector2 rp,
                    out float rvis)
        {
            if (_count == 3)
            {
                for (int i = 0; i < 2; i++)
                {
                    _t[i] = _t[i + 1];
                    _l[i] = _l[i + 1];
                    _r[i] = _r[i + 1];
                    _lv[i] = _lv[i + 1];
                    _rv[i] = _rv[i + 1];
                }
                _count = 2;
            }
            _t[_count] = time;
            _l[_count] = l;
            _r[_count] = r;
            _lv[_count] = lv;
            _rv[_count] = rv;
            _count++;
            at = 0;
            lp = rp = default;
            lvis = rvis = 0f;
            if (_count < 3) return false;
            at = _t[1];
            if (_t[2] - _t[1] > 0.25 || _t[1] - _t[0] > 0.25)
            {
                // across a gap the neighbours say nothing about this sample
                lp = _l[1];
                rp = _r[1];
                lvis = _lv[1];
                rvis = _rv[1];
                return true;
            }
            lp = new Vector2(Mid(_l[0].x, _l[1].x, _l[2].x), Mid(_l[0].y, _l[1].y, _l[2].y));
            rp = new Vector2(Mid(_r[0].x, _r[1].x, _r[2].x), Mid(_r[0].y, _r[1].y, _r[2].y));
            lvis = Mathf.Min(_lv[1], Mathf.Max(_lv[0], _lv[2]));
            rvis = Mathf.Min(_rv[1], Mathf.Max(_rv[0], _rv[2]));
            return true;
        }

        static float Mid(float a, float b, float c) => Mathf.Max(Mathf.Min(a, b), Mathf.Min(Mathf.Max(a, b), c));
    }
}
