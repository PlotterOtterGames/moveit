using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Motion
{
    /// <summary>
    /// Two players in front of one camera (two-player games; Tenis! first, 2026-11-05): which tracked body is player 1
    /// and which player 2, and for each what the games read: where they stood still (the middle of their own steps, like
    /// <see cref="BodyGestures"/>' calibration), how far they've stepped sideways since, raised hands, and whether they
    /// stand where the game needs them.
    /// <list type="bullet">
    /// <item>Player 1 stands on the left as the players see the screen (it's a mirror), player 2 on the right. While
    /// <see cref="Locked"/> is off (waiting screens) the two are sorted that way every frame, each keeping their own
    /// calibration. During a match (<see cref="Locked"/>) a number is a side of the room: the bodies are kept sorted
    /// left to right (the wrong way round for <see cref="SideSwapAfter"/> s, by more than <see cref="SideMargin"/>, and
    /// they change slots), and a slot keeps its calibration (where that side's player stood) when its body is lost and
    /// found again. Two people standing close together can make the tracker follow the wrong person (2026-11-06: the
    /// players swapped numbers after standing close together); the sides can't swap.</item>
    /// <item>Each one calibrates by standing still <see cref="StillTime"/> s where <see cref="Area"/> allows; both hands
    /// above the head for <see cref="HandsUpHold"/> s marks them <see cref="Slot.Ready"/> (the game clears it).</item>
    /// <item>Closer together than <see cref="MinApart"/> torso lengths they'd bump into each other stepping sideways:
    /// <see cref="TooClose"/>.</item>
    /// </list>
    /// Pure logic on the tracker's bodies (no scene): the game updates it once a frame.
    /// </summary>
    public sealed class PlayerSlots
    {
        public const int Count = 2;

        /// <summary>Seconds a player stands still to calibrate, and how still (torso lengths per second).</summary>
        public float StillTime = 0.6f, StillSpeed = 0.6f;
        /// <summary>Hands up: both wrists this far above the nose (torso lengths), held this long (s).</summary>
        public float HandsUpAboveNose = 0.15f, HandsUpHold = 0.25f;
        /// <summary>Torso centres closer than this (torso lengths): too close to play side by side.</summary>
        public float MinApart = 1.4f;
        /// <summary>Where the game needs each player (null = anywhere).</summary>
        public PlayArea Area;
        /// <summary>On during a match: a number is a side of the room (see the class).</summary>
        public bool Locked;
        /// <summary>During a match the two bodies change slots when they are the wrong way round (player 1's right of
        /// player 2's) by more than this many torso lengths for this long (s, the camera's time): not when they just
        /// stand level.</summary>
        public float SideMargin = 0.3f, SideSwapAfter = 0.25f;
        /// <summary>How many times the bodies changed slots during matches.</summary>
        public int SideSwaps { get; private set; }

        public readonly Slot[] Slots = { new Slot(0), new Slot(1) };
        /// <summary>Both are in view and stand too close together.</summary>
        public bool TooClose { get; private set; }
        /// <summary>Players in view (a tracked body).</summary>
        public int Present { get; private set; }

        public Slot this[int index] => Slots[index];

        public sealed class Slot
        {
            /// <summary>0 = player 1, 1 = player 2.</summary>
            public int Index { get; internal set; }
            public Body Body { get; internal set; }
            /// <summary>In view now (the camera saw them within the last quarter second).</summary>
            public bool Present => Body != null && Body.IsTracking;
            public bool Calibrated { get; private set; }
            public float CalibrationProgress => Calibrated ? 1f : Mathf.Clamp01(_still / Mathf.Max(0.01f, _stillTime));
            /// <summary>Where they stood still (view units) and their torso length then: the unit of their steps.</summary>
            public float BaselineX { get; private set; }
            public float BaselineTorso { get; private set; }
            /// <summary>Sideways from where they stood still, torso lengths (+ = right on the screen).</summary>
            public float LateralOffset { get; private set; }
            /// <summary>Both hands above the head right now.</summary>
            public bool HandsUp { get; private set; }
            /// <summary>Raised both hands since the game last cleared it.</summary>
            public bool Ready { get; set; }
            /// <summary>Stands where <see cref="PlayerSlots.Area"/> needs them.</summary>
            public readonly PlacementCheck Placement = new PlacementCheck();
            /// <summary>The body's last sample handled (seconds, the camera's clock).</summary>
            public double SampleTime { get; private set; }

            float _stillTime, _still, _stillSumX, _stillSumTorso;
            int _stillSamples;
            float _handsTimer;
            bool _handsLatched;
            Vector3 _prevCenter;

            public Slot(int index) => Index = index;

            /// <summary>This slot's body from now on. keepCalibration: during a match the slot's calibration (where
            /// its side's player stood) stays.</summary>
            internal void Assign(Body body, bool keepCalibration = false)
            {
                if (Body == body) return;
                Body = body;
                if (!keepCalibration)
                {
                    Calibrated = false;
                    LateralOffset = 0f;
                }
                Ready = HandsUp = false;
                _still = _stillSumX = _stillSumTorso = 0f;
                _stillSamples = 0;
                _handsTimer = 0f;
                _handsLatched = false;
                SampleTime = 0;
                Placement.Reset();
            }

            /// <summary>Where they stand now is the middle of their steps (a match starts).</summary>
            public void Recenter()
            {
                if (Body == null || !Calibrated) return;
                BaselineX = Body.FastTorsoCenter.x;
                LateralOffset = 0f;
            }

            internal void Update(PlayerSlots owner)
            {
                var body = Body;
                if (body == null || body.SampleTime == SampleTime) return;
                float dt = SampleTime > 0 ? (float)(body.SampleTime - SampleTime) : 0f;
                if (dt > 0f && dt < Body.MinSpeedInterval) return;
                SampleTime = body.SampleTime;
                Placement.Update(body, owner.Area);
                var center = body.FastTorsoCenter;
                float torso = body.FastTorsoLength;
                if (dt <= 0f || dt > 0.5f || torso < 0.03f)
                {
                    _prevCenter = center;
                    return;
                }
                _stillTime = owner.StillTime;
                if (!Calibrated)
                {
                    float speed = Vector2.Distance(center, _prevCenter) / dt / torso;
                    if (!body.ShouldersVisible || speed > owner.StillSpeed || Placement.Blocks)
                    {
                        _still = _stillSumX = _stillSumTorso = 0f;
                        _stillSamples = 0;
                    }
                    else
                    {
                        _still += dt;
                        _stillSumX += center.x;
                        _stillSumTorso += torso;
                        _stillSamples++;
                        if (_still >= owner.StillTime)
                        {
                            BaselineX = _stillSumX / _stillSamples;
                            BaselineTorso = _stillSumTorso / _stillSamples;
                            Calibrated = true;
                        }
                    }
                }
                else
                {
                    LateralOffset = (center.x - BaselineX) / BaselineTorso;
                    // the body's size follows slowly (stepping closer to the camera)
                    BaselineTorso = Mathf.Lerp(BaselineTorso, torso, 0.25f * dt);
                }
                _prevCenter = center;

                // both hands above the head: ready (once per raise: the hands come down below the shoulders in between)
                var fast = body.Fast;
                float unit = Calibrated ? BaselineTorso : torso;
                float line = fast[(int)Joint.Nose].y + owner.HandsUpAboveNose * unit;
                HandsUp = body.IsVisible(Joint.LeftWrist) && body.IsVisible(Joint.RightWrist) &&
                          fast[(int)Joint.LeftWrist].y > line && fast[(int)Joint.RightWrist].y > line;
                if (HandsUp)
                {
                    _handsTimer += dt;
                    if (!_handsLatched && _handsTimer >= owner.HandsUpHold && !Placement.Blocks)
                    {
                        _handsLatched = true;
                        Ready = true;
                    }
                }
                else
                {
                    _handsTimer = 0f;
                    float shoulders = body.FastShoulderCenter.y;
                    if (fast[(int)Joint.LeftWrist].y < shoulders || fast[(int)Joint.RightWrist].y < shoulders) _handsLatched = false;
                }
            }
        }

        /// <summary>The slot <paramref name="body"/> plays in, or null.</summary>
        public Slot Of(Body body)
        {
            foreach (var s in Slots)
                if (s.Body == body && body != null) return s;
            return null;
        }

        /// <summary>Both players are in view, calibrated and stand where the game needs them, not too close together.</summary>
        public bool BothCanPlay => Present == Count && !TooClose && Slots[0].Calibrated && Slots[1].Calibrated &&
                                   !Slots[0].Placement.Blocks && !Slots[1].Placement.Blocks;

        /// <summary>Once a frame: the tracker's bodies (<see cref="BodyTracker.Bodies"/>).</summary>
        public void Update(IReadOnlyList<Body> bodies)
        {
            // players the tracker gave up on (lost longer than its timeout) leave their number free (in a match keeping
            // the side's calibration for whoever steps back in)
            foreach (var s in Slots)
                if (s.Body != null && !Contains(bodies, s.Body)) s.Assign(null, Locked);
            // newcomers take free numbers: the leftmost the lowest
            _newcomers.Clear();
            foreach (var b in bodies)
                if (Of(b) == null) _newcomers.Add(b);
            _newcomers.Sort((a, b) => a.TorsoCenter.x.CompareTo(b.TorsoCenter.x));
            foreach (var b in _newcomers)
                foreach (var s in Slots)
                    if (s.Body == null)
                    {
                        s.Assign(b, Locked);
                        break;
                    }
            var b0 = Slots[0].Body;
            var b1 = Slots[1].Body;
            if (b0 != null && b1 != null)
            {
                if (!Locked)
                {
                    // waiting screens: player 1 is whoever stands on the left; crossing over swaps the numbers, each
                    // keeping their own calibration and raised hands (a little past each other first: no flicker)
                    if (b0.TorsoCenter.x > b1.TorsoCenter.x + 0.1f * b1.TorsoLength)
                    {
                        (Slots[0], Slots[1]) = (Slots[1], Slots[0]);
                        Slots[0].Index = 0;
                        Slots[1].Index = 1;
                    }
                    _reversedSince = -1;
                }
                else
                {
                    // a match: the sides stay, the bodies move to the right side once they have been the wrong way round
                    // for a moment (the tracker followed the wrong person after the two stood close together)
                    double now = System.Math.Max(b0.SampleTime, b1.SampleTime);
                    float unit = Mathf.Max(0.03f, (b0.TorsoLength + b1.TorsoLength) * 0.5f);
                    if (b0.TorsoCenter.x > b1.TorsoCenter.x + SideMargin * unit)
                    {
                        if (_reversedSince < 0) _reversedSince = now;
                        if (now - _reversedSince >= SideSwapAfter)
                        {
                            Slots[0].Assign(b1, true);
                            Slots[1].Assign(b0, true);
                            SideSwaps++;
                            _reversedSince = -1;
                        }
                    }
                    else _reversedSince = -1;
                }
            }
            else _reversedSince = -1;
            Present = 0;
            foreach (var s in Slots)
            {
                s.Update(this);
                if (s.Present) Present++;
            }
            TooClose = false;
            if (Present == Count)
            {
                var a = Slots[0].Body;
                var b = Slots[1].Body;
                float unit = Mathf.Max(0.03f, (a.FastTorsoLength + b.FastTorsoLength) * 0.5f);
                TooClose = Mathf.Abs(a.FastTorsoCenter.x - b.FastTorsoCenter.x) / unit < MinApart;
            }
        }

        /// <summary>Where each calibrated player stands now is the middle of their steps.</summary>
        public void Recenter()
        {
            foreach (var s in Slots) s.Recenter();
        }

        /// <summary>Clears every player's "ready".</summary>
        public void ClearReady()
        {
            foreach (var s in Slots) s.Ready = false;
        }

        /// <summary>Forgets both players.</summary>
        public void Clear()
        {
            foreach (var s in Slots) s.Assign(null);
            Present = 0;
            TooClose = false;
        }

        readonly List<Body> _newcomers = new List<Body>();
        double _reversedSince = -1;

        static bool Contains(IReadOnlyList<Body> bodies, Body body)
        {
            for (int i = 0; i < bodies.Count; i++)
                if (bodies[i] == body) return true;
            return false;
        }
    }
}
