using System;
using UnityEngine;

namespace MoveIt.Games.Sports
{
    public enum VolleyPhase { Serve, Flight, Point, Over }

    /// <summary>What the ball's flight is: who hit it and what comes at its end.</summary>
    public enum VolleyStroke { OurServe, TheirServe, OurBump, OurSet, OurSpike, OurTap, TheirDig, TheirSet, TheirSpike }

    public enum VolleyEvent { Toss, Hit, Bump, Spike, PerfectSpike, Tap, Dig, Missed, PointUs, PointThem, Won, Lost }

    /// <summary>
    /// Beach volleyball, two against two (Kinect Sports' beach volleyball), without a scene. The player and a computer
    /// partner on the near side, two computer players across the net; the players run to the ball by themselves, the
    /// player only has to do the right move at the right moment (a ring closes on the ball):
    /// <list type="bullet">
    /// <item><b>receive</b>: any quick move of the arms as the ball comes (a bump) sends it up to the partner, who sets it
    /// at the net;</item>
    /// <item><b>spike</b>: a jump or a quick arm move as the set ball peaks hits it over: on the moment = a hard spike to a
    /// far corner, a little off = a softer one; no move = the partner taps it over;</item>
    /// <item><b>serve</b>: the ball is tossed; any arm move near its top serves (none in time: the player serves
    /// underhand, softly).</item>
    /// </list>
    /// The opponents dig the softer balls more often, then set and spike back. Rally scoring to <see cref="Target"/>.
    /// Points: 100 a point won, 300 for the match. Positions: x across (+ = the screen's right), y up, z toward their side
    /// (the net at 0, the near baseline at -8).
    /// </summary>
    public sealed class VolleyMatch
    {
        public const int Target = 7;
        public const float NetHeight = 2.43f, HalfWidth = 4f, Length = 8f, Window = 0.38f, Perfect = 0.1f;

        public VolleyPhase Phase { get; private set; } = VolleyPhase.Serve;
        public VolleyStroke Stroke { get; private set; }
        public float PhaseTime { get; private set; }
        public int Us { get; private set; }
        public int Them { get; private set; }
        public bool OurServe { get; private set; } = true;
        public bool Won => Us >= Target;
        public bool Over => Phase == VolleyPhase.Over;
        public Vector3 Ball { get; private set; }
        /// <summary>The flight: from, to, how long; and how far along (s).</summary>
        public Vector3 From { get; private set; }
        public Vector3 To { get; private set; }
        public float Duration { get; private set; }
        public float Apex { get; private set; }
        /// <summary>Where the player (and the partner) stand to play the ball now.</summary>
        public Vector3 PlayerSpot { get; private set; } = new Vector3(1.4f, 0f, -5f);
        public Vector3 PartnerSpot { get; private set; } = new Vector3(-1.4f, 0f, -5f);
        public Vector3 TheirLeft { get; private set; } = new Vector3(-1.6f, 0f, 5f);
        public Vector3 TheirRight { get; private set; } = new Vector3(1.6f, 0f, 5f);
        /// <summary>The player must act (bump, spike, serve) at the end of this flight.</summary>
        public bool PlayerToAct => Phase == VolleyPhase.Flight && Stroke is VolleyStroke.TheirServe or VolleyStroke.TheirSpike or VolleyStroke.OurSet
                                || Phase == VolleyPhase.Serve && OurServe;
        /// <summary>Seconds until the moment to act (negative: it's passing).</summary>
        public float UntilAct => Phase == VolleyPhase.Serve ? TossTop - PhaseTime : Duration - PhaseTime;
        public int Rally { get; private set; }
        public int Spikes { get; private set; }
        public event Action<VolleyEvent> Events;

        public const float TossTop = 1.6f, TossDown = 2.6f;
        readonly System.Random _rng;
        bool _acted;

        public VolleyMatch(int seed)
        {
            _rng = new System.Random(seed);
            Serve(true);
        }

        public int Points => Us * 100 + (Over && Won ? 300 : 0);
        public static int[] Needs => new[] { 300, 500, 750, 1000 };

        float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        void Serve(bool ours)
        {
            OurServe = ours;
            Phase = VolleyPhase.Serve;
            PhaseTime = 0f;
            Rally = 0;
            _acted = false;
            PlayerSpot = ours ? new Vector3(1.4f, 0f, -Length - 0.6f) : new Vector3(1.4f, 0f, -4.5f);
            PartnerSpot = new Vector3(-1.6f, 0f, -4.5f);
            TheirLeft = new Vector3(-1.6f, 0f, 4.5f);
            TheirRight = ours ? new Vector3(1.6f, 0f, 4.5f) : new Vector3(1.5f, 0f, Length + 0.6f);
            Ball = (ours ? PlayerSpot : TheirRight) + Vector3.up * 1.2f;
            Events?.Invoke(VolleyEvent.Toss);
        }

        /// <summary>
        /// One step. The ball flies its arc; at the end of a flight the next touch happens (the computer's by itself, the
        /// player's only if they acted in time).
        /// </summary>
        public void Step(float dt)
        {
            if (Over || dt <= 0f) return;
            PhaseTime += dt;
            switch (Phase)
            {
                case VolleyPhase.Serve:
                    var server = OurServe ? PlayerSpot : TheirRight;
                    float up = PhaseTime < TossDown ? Toss(PhaseTime) : 0f;
                    Ball = server + new Vector3(0.3f, 1.4f + up, 0.2f);
                    if (!OurServe && PhaseTime >= TossTop) Fly(VolleyStroke.TheirServe, Ball, Receive(), R(1.4f, 1.7f), R(4.5f, 6f));
                    else if (OurServe && PhaseTime >= TossDown + 0.2f) Fly(VolleyStroke.OurServe, Ball, Across(), 1.8f, 5f);   // an underhand serve
                    break;
                case VolleyPhase.Flight:
                    float k = Mathf.Clamp01(PhaseTime / Duration);
                    Ball = Vector3.Lerp(From, To, k) + Vector3.up * (4f * Apex * k * (1f - k));
                    if (PhaseTime >= Duration + (PlayerToAct ? Window : 0f)) Arrived();
                    break;
                case VolleyPhase.Point:
                    if (PhaseTime >= 1.8f)
                    {
                        if (Us >= Target || Them >= Target)
                        {
                            Phase = VolleyPhase.Over;
                            Events?.Invoke(Us >= Target ? VolleyEvent.Won : VolleyEvent.Lost);
                        }
                        else Serve(_pointWasOurs);
                    }
                    break;
            }
        }

        bool _pointWasOurs;

        /// <summary>The toss's height above the hands: up to the top at <see cref="TossTop"/>, down by <see cref="TossDown"/>.</summary>
        static float Toss(float t)
        {
            float k = t / TossTop;
            return t <= TossTop ? 1.6f * (1f - (1f - k) * (1f - k)) : 1.6f * (1f - Mathf.Pow((t - TossTop) / (TossDown - TossTop), 2f));
        }

        Vector3 Receive() => new Vector3(R(-1f, 3f), 1.0f, R(-6.5f, -3.5f));
        Vector3 Across() => new Vector3(R(-3f, 3f), 0.2f, R(3.5f, 7f));

        void Fly(VolleyStroke stroke, Vector3 from, Vector3 to, float seconds, float apex)
        {
            Stroke = stroke;
            From = from;
            To = to;
            Duration = seconds;
            Apex = apex;
            Phase = VolleyPhase.Flight;
            PhaseTime = 0f;
            _acted = false;
            if (stroke is VolleyStroke.TheirServe or VolleyStroke.TheirSpike)
            {
                // the player runs to where the ball comes down (the partner covers the other half)
                PlayerSpot = new Vector3(to.x, 0f, to.z - 0.4f);
                PartnerSpot = new Vector3(to.x > 0.5f ? -1.8f : 2.4f, 0f, -4f);
            }
            if (stroke == VolleyStroke.OurSet) PlayerSpot = new Vector3(to.x, 0f, to.z - 0.6f);
            if (stroke is VolleyStroke.OurServe or VolleyStroke.OurSpike or VolleyStroke.OurTap or VolleyStroke.TheirSet)
            {
                // the nearer opponent goes for it
                if (to.x < 0f) TheirLeft = new Vector3(to.x, 0f, to.z + 0.4f);
                else TheirRight = new Vector3(to.x, 0f, to.z + 0.4f);
            }
            Events?.Invoke(VolleyEvent.Hit);
        }

        /// <summary>
        /// The player moved: a quick arm move (<paramref name="armSpeed"/> torso lengths a second) or a jump. Bumps, spikes
        /// or serves when it's their moment (<see cref="Window"/> either side). Returns whether it counted.
        /// </summary>
        public bool Act(float armSpeed, bool jump)
        {
            if (_acted || Over) return false;
            float off = UntilAct;
            if (Phase == VolleyPhase.Serve && OurServe)
            {
                if (Mathf.Abs(off) > Window + 0.3f) return false;
                _acted = true;
                Fly(VolleyStroke.OurServe, Ball, Across(), 1.4f, 4.5f);
                Events?.Invoke(VolleyEvent.Hit);
                return true;
            }
            if (Phase != VolleyPhase.Flight || !PlayerToAct || Mathf.Abs(off) > Window) return false;
            _acted = true;
            if (Stroke == VolleyStroke.OurSet)
            {
                // the spike: on the moment and hard = a hard one to a far corner
                bool perfect = Mathf.Abs(off) <= Perfect;
                Spikes++;
                var to = new Vector3(_rng.NextDouble() < 0.5 ? R(-3.6f, -2f) : R(2f, 3.6f), 0.15f, perfect ? R(5.5f, 7.6f) : R(3f, 6f));
                _spikeQuality = perfect ? 2 : Mathf.Abs(off) <= Window * 0.6f ? 1 : 0;
                Fly(VolleyStroke.OurSpike, Ball, to, perfect ? 0.55f : 0.8f, perfect ? 0.4f : 1f);
                Events?.Invoke(perfect ? VolleyEvent.PerfectSpike : VolleyEvent.Spike);
                return true;
            }
            // a bump: up to the partner by the net
            Fly(VolleyStroke.OurBump, Ball, new Vector3(R(-1.5f, 0.5f), 2.4f, -1.6f), 1.15f, 2.6f);
            Events?.Invoke(VolleyEvent.Bump);
            Rally++;
            return true;
        }

        int _spikeQuality;

        void Arrived()
        {
            switch (Stroke)
            {
                case VolleyStroke.TheirServe:
                case VolleyStroke.TheirSpike:
                    // the player didn't bump it in time
                    Events?.Invoke(VolleyEvent.Missed);
                    Point(false);
                    return;
                case VolleyStroke.OurBump:
                    // the partner sets it high by the net, for the player to spike
                    Fly(VolleyStroke.OurSet, Ball, new Vector3(R(0.6f, 2f), 2.45f, -0.9f), 1.25f, 1.6f);
                    return;
                case VolleyStroke.OurSet:
                    // no spike: the partner taps it over
                    _spikeQuality = -1;
                    Fly(VolleyStroke.OurTap, Ball, Across(), 1.5f, 2.2f);
                    Events?.Invoke(VolleyEvent.Tap);
                    return;
                case VolleyStroke.OurServe:
                case VolleyStroke.OurSpike:
                case VolleyStroke.OurTap:
                    // they dig it, or it's our point: hard spikes are dug less, soft balls and serves more
                    float dig = Stroke == VolleyStroke.OurSpike ? (_spikeQuality == 2 ? 0.22f : _spikeQuality == 1 ? 0.45f : 0.7f)
                              : Stroke == VolleyStroke.OurTap ? 0.85f : 0.75f;
                    if (_rng.NextDouble() < dig)
                    {
                        Events?.Invoke(VolleyEvent.Dig);
                        Fly(VolleyStroke.TheirDig, Ball, new Vector3(R(-1f, 1f), 2.4f, 1.6f), 1.1f, 2.4f);
                    }
                    else Point(true);
                    return;
                case VolleyStroke.TheirDig:
                    // their other player sets it up at the net
                    Fly(VolleyStroke.TheirSet, Ball, new Vector3(R(-1.5f, 1.5f), 2.45f, 0.9f), 1.2f, 1.5f);
                    return;
                case VolleyStroke.TheirSet:
                    Fly(VolleyStroke.TheirSpike, Ball, Receive(), R(0.8f, 1.1f), R(0.6f, 1.2f));
                    return;
            }
        }

        void Point(bool ours)
        {
            if (ours) Us++;
            else Them++;
            _pointWasOurs = ours;
            Phase = VolleyPhase.Point;
            PhaseTime = 0f;
            Ball = new Vector3(Ball.x, 0.15f, Ball.z);
            Events?.Invoke(ours ? VolleyEvent.PointUs : VolleyEvent.PointThem);
        }
    }
}
