using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Tennis
{
    public enum Stroke { Forehand, Backhand, Overhead }
    public enum ShotKind { Serve, PowerServe, Flat, Topspin, Slice, Lob, Volley, Smash }
    public enum MatchPhase { Serve, Toss, Rally, PointOver, Over }

    /// <summary>Why a point ended.</summary>
    public enum PointReason
    {
        /// <summary>The ball bounced twice on the other side (nobody got it back).</summary>
        Winner,
        Out,
        Net,
    }

    /// <summary>What a swing of the player did.</summary>
    public enum SwingResult
    {
        Hit, Serve, Toss, Early, Late, NoBall,
        /// <summary>On time, but the player stood too far from the ball (the body has to move closer).</summary>
        TooFar,
    }

    public enum MatchEventKind
    {
        Toss, Caught, Hit, Whiff, Bounce, Net, Point,
        /// <summary>The player let a ball of theirs go by without swinging in time.</summary>
        Missed,
        /// <summary>The player swung on time but stood too far from the ball.</summary>
        TooFar,
    }

    /// <summary>One shot: how it was hit.</summary>
    public struct Shot
    {
        public ShotKind Kind;
        public int Team;
        public Stroke Stroke;
        /// <summary>m/s across the court.</summary>
        public float Speed;
        /// <summary>Timing of the player's swing: -1 = as early as counts, 0 = right on time, +1 = as late as counts.
        /// 0 for the computer.</summary>
        public float Timing;
        public bool Perfect;
        /// <summary>Where it is aimed to bounce.</summary>
        public Vector3 Target;
    }

    public struct MatchEvent
    {
        public MatchEventKind Kind;
        public int Team;
        /// <summary>Index into <see cref="TennisMatch.Players"/> (or -1).</summary>
        public int Player;
        public Vector3 Position;
        public Shot Shot;
        public PointReason Reason;
        public ScoreCall Call;
        /// <summary>A bounce inside the court (Bounce events).</summary>
        public bool In;
    }

    /// <summary>A player on the court as the rules see it: where it stands and moves to, and what the view should
    /// animate (a swing with its contact moment, a dive, a cheer).</summary>
    public sealed class CourtPlayer
    {
        public int Index, Team;
        public bool RightHanded = true;
        /// <summary>(x, z) on the court.</summary>
        public Vector2 Position, Velocity, Target;
        public float MaxSpeed = 9f;
        /// <summary>Counts swings: the view starts a swing when it changes.</summary>
        public int Swings;
        public Stroke SwingStroke;
        /// <summary>Match time the racket meets the ball (the view times its swing to it).</summary>
        public double SwingContact;
        /// <summary>The swing connects (false: an air swing).</summary>
        public bool SwingHits;
        public ShotKind SwingKind;
        /// <summary>A dive for a ball out of reach (match time, or -1).</summary>
        public double DiveAt = -1;
        /// <summary>+1 cheering, -1 sad, 0 neither (between points).</summary>
        public int Mood;
        /// <summary>Holds the ball before a serve (the view draws the toss hand).</summary>
        public bool Serving;
        /// <summary>A ball is coming to this player: the racket goes back for <see cref="PrepareStroke"/> and waits (the
        /// Miis do it by themselves), so a swing only has the hit left to draw.</summary>
        public bool Preparing;
        public Stroke PrepareStroke;

        /// <summary>The racket's side in x: +1 = +x.</summary>
        public float RacketSide => Court.RightSign(Team) * (RightHanded ? 1f : -1f);
    }

    /// <summary>The receiving player's next touch of the ball: where and when the racket meets it, and where the player
    /// has to stand for that.</summary>
    public sealed class Contact
    {
        public bool Active;
        public int Team;
        public CourtPlayer Hitter;
        /// <summary>Match time the ball reaches the racket.</summary>
        public double Time;
        /// <summary>Where the racket meets the ball.</summary>
        public Vector3 Point;
        /// <summary>Where the player stands for the stroke: beside the ball on the racket's side, a little behind it.</summary>
        public Vector2 Spot;
        public Stroke Stroke;
        public bool Volley, Smash;
        /// <summary>The player gets there in time: for the computer by running, for the player while the body is near
        /// enough for the glide (<see cref="TennisMatch.AssistSide"/>, <see cref="TennisMatch.AssistDepth"/>).</summary>
        public bool Reachable;
        /// <summary>A swing of the player connected (the hit may be held until the ball arrives).</summary>
        public bool Taken;
        /// <summary>The player's contact no longer follows the body (just before the ball arrives, or once swung at).</summary>
        public bool Locked;
        public bool SwingShown;
    }

    /// <summary>
    /// "Tenis!": a singles match against a computer player, after Wii Sports Tennis. Pure rules (no scene), driven by
    /// <see cref="Step"/>, the player's body position (<see cref="HumanWanted"/>) and swings (<see cref="Swing"/>);
    /// <c>TennisGame</c> draws it.
    /// <list type="bullet">
    /// <item><b>The body moves the player</b> around their half: where they stand in front of the TV is where their
    /// player stands (<see cref="BodyToCourt"/>: a step to the side reaches the sideline, a step toward the TV goes to the
    /// net, back goes behind the baseline). <b>An assist</b> does the last bit: when a ball comes, the player glides from
    /// where the body puts it to the spot beside the ball, up to <see cref="AssistSide"/> sideways and
    /// <see cref="AssistDepth"/> forward or back. Farther than that, the ball goes by unless the body steps toward it.</item>
    /// <item>Which touch: each shot's path is predicted (<see cref="BallPhysics"/>); a ball that would bounce behind the
    /// player is volleyed as it passes them (a high one is smashed, one too high goes over), else it is met after the
    /// bounce as it reaches the player's depth or comes down to waist height. For the player this follows the body until
    /// just before the ball arrives (<see cref="LockBefore"/>).</item>
    /// <item><b>The swing's timing aims the ball</b> (Wii): early pulls it across (to the player's left on a forehand),
    /// late pushes it the other way, on time sends it down the middle. Its speed is the shot's power, swinging up is
    /// topspin (slow and up: a lob), swinging down a slice. A swing counts from <see cref="EarlyWindow"/> before the ball
    /// arrives to <see cref="LateWindow"/> after; within <see cref="PerfectWindow"/> it's a perfect, faster shot. The
    /// player's shots go in, except, against later players (<see cref="TennisLevel.WideFrom"/>), a swing at the very
    /// edge of the window goes wide.</item>
    /// <item>Times are camera capture times: the game passes when the swing really happened, not when it was seen, so
    /// the camera's lag doesn't make every swing late. An early swing's hit waits for the ball; a late one leaves from
    /// where the ball was when the racket met it.</item>
    /// <item><b>Serve</b>: the server stands in place; the ball is tossed (by itself, or by a first swing); any swing
    /// while it's up serves, at the top (<see cref="PowerServeWindow"/>) it's a power serve. Serves always go in; a missed
    /// toss is just caught again.</item>
    /// <item>The computer (<see cref="TennisLevel"/>) runs to every ball by itself; whether it gets one back is decided
    /// as the ball is hit (harder for fast, perfect, far or smashed balls), it aims into the corners and, later, short,
    /// and some of its shots go out or into the net.</item>
    /// </list>
    /// Scoring: <see cref="TennisScore"/> (best of 3 games by default). Practice (<see cref="Practice"/>): no score, the
    /// computer gets every ball back and can time its feeds (tests, a training mode).
    /// </summary>
    public sealed class TennisMatch
    {
        /// <summary>Where a player waits for the ball when nothing else says (metres behind the net): the computer's
        /// home, and the player's when their body stands where it was calibrated.</summary>
        public const float HomeDepth = 12.6f;
        /// <summary>The player meets a bounced ball as it comes down to this height (or as it reaches their depth)...</summary>
        public const float ContactHeight = 1.05f;
        /// <summary>...and never further back than this.</summary>
        public const float BackLimit = 15.5f;
        /// <summary>How close to the net, and how far behind it, the player can go (metres).</summary>
        public const float NearestToNet = 2.5f, Furthest = 15f;
        public const float TossHeight = 1.3f, TossSpeed = 5.8f;
        public static float TossApex => TossSpeed / BallPhysics.Gravity;
        /// <summary>The toss is caught again this long after it went up (it fell back to the hand).</summary>
        public static float TossCatch => 2f * TossApex;

        public float EarlyWindow = 0.4f, LateWindow = 0.25f, PerfectWindow = 0.07f, PowerServeWindow = 0.12f;
        /// <summary>A contact of the player's closes this long after <see cref="LateWindow"/> (a late swing still takes a
        /// moment to reach the game).</summary>
        public float CloseAfter = 0.25f;
        /// <summary>The assist: how far (m) the player glides from where the body puts it to reach a ball, sideways and
        /// forward/back (depth from a front camera is rough: more of it).</summary>
        public float AssistSide = 1.6f, AssistDepth = 2.5f;
        /// <summary>At the hit the player must stand this close (m) to the spot beside the ball.</summary>
        public float ReachTolerance = 0.7f;
        /// <summary>The player's contact stops following the body this long (s) before the ball arrives (the ring and
        /// the timing stay put while the player swings).</summary>
        public float LockBefore = 0.45f;
        /// <summary>Swing speeds (torso lengths per second) from a gentle shot to a full-power one.</summary>
        public float SlowSwing = 3f, FastSwing = 10f;
        /// <summary>Seconds between the end of a point and the next serve (longer after a game).</summary>
        public float PointPause = 2.2f, GamePause = 3.2f, MatchPause = 3.5f;

        public readonly TennisLevel Level;
        public readonly TennisScore Score;
        /// <summary>0 = the player, 1 = the computer.</summary>
        public readonly CourtPlayer[] Players = new CourtPlayer[2];
        public CourtPlayer Human => Players[0];
        public CourtPlayer Cpu => Players[1];
        public readonly Contact Next = new Contact();

        /// <summary>Practice: no score, the computer returns everything (and times its feeds with
        /// <see cref="PracticeContact"/>).</summary>
        public bool Practice;
        /// <summary>Practice feeds: given the earliest moment a feed could reach the player's racket (match time), when it
        /// should. Null = at a normal pace.</summary>
        public Func<double, double> PracticeContact;
        /// <summary>Practice serves: given the earliest moment of the toss's top, when it should be.</summary>
        public Func<double, double> PracticeToss;

        public event Action<MatchEvent> Events;

        public double Time { get; private set; }
        public MatchPhase Phase { get; private set; }
        public BallState Ball;
        /// <summary>The ball is on the court (false: in the server's hand).</summary>
        public bool BallLive { get; private set; }
        public int LastHitter { get; private set; } = -1;
        public bool LastWasServe { get; private set; }
        public Shot LastShot { get; private set; }
        public int RallyHits { get; private set; }
        public bool DeuceCourt { get; private set; } = true;
        public int ServerTeam => Practice ? 0 : Score.Server;
        public CourtPlayer Server => Players[ServerTeam];
        /// <summary>The match time the toss reaches its top (serving).</summary>
        public double TossTop { get; private set; }
        /// <summary>Misses in a row of the player's toss (for a hint).</summary>
        public int TossesMissed { get; private set; }
        /// <summary>The last point's winner, or -1.</summary>
        public int PointWinner { get; private set; } = -1;
        public PointReason LastReason { get; private set; }
        public ScoreCall LastCall { get; private set; }

        // stats for the results screen
        public int HumanHits { get; private set; }
        public int HumanPerfect { get; private set; }
        public int HumanWhiffs { get; private set; }
        public int TooFar { get; private set; }
        public int LongestRally { get; private set; }
        public int PointsWon { get; private set; }
        public int PointsLost { get; private set; }

        readonly System.Random _rng;
        readonly List<PathPoint> _path = new List<PathPoint>();
        readonly List<PathPoint> _scratch = new List<PathPoint>();
        double _ballTime, _tossAt, _tossStart, _nextPointAt, _cpuServeAt, _lastHitTime;
        int _bouncesOnReceiver, _bounce;
        bool _landsIn, _volleyMissed;
        bool _cpuReturns, _heldHit, _cpuServeDone;
        float _heldTiming, _heldPower, _heldVertical;
        bool _heldPerfect;
        Vector2 _wanted = new Vector2(0f, -HomeDepth);

        public TennisMatch(TennisLevel level, int gamesToWin = 2, int seed = 0, int firstServer = 0)
        {
            Level = level;
            Score = new TennisScore(gamesToWin, firstServer);
            _rng = new System.Random(seed);
            for (int i = 0; i < 2; i++) Players[i] = new CourtPlayer { Index = i, Team = i };
            Cpu.MaxSpeed = level.RunSpeed;
            Human.MaxSpeed = 9f;
        }

        /// <summary>The player plays with the racket in this hand (forehand and backhand sides follow).</summary>
        public bool HumanRightHanded
        {
            get => Human.RightHanded;
            set => Human.RightHanded = value;
        }

        /// <summary>Where the player's body asks their player to stand (court x, z), kept on their half. Set it every
        /// frame (<see cref="BodyToCourt"/> from the camera, or keys).</summary>
        public Vector2 HumanWanted
        {
            get => _wanted;
            set => _wanted = new Vector2(Mathf.Clamp(value.x, -Court.HalfWidth - 1f, Court.HalfWidth + 1f),
                                         Mathf.Clamp(value.y, -Furthest, -NearestToNet));
        }

        /// <summary>
        /// Where the body puts the player on the court: <paramref name="lateral"/> = sideways from where the player was
        /// calibrated, torso lengths (+ = right, the screen is a mirror); <paramref name="nearer"/> = how much bigger the
        /// torso looks than at calibration (1.1 = 10 % bigger: the player stepped toward the TV). A step of
        /// <paramref name="fullSide"/> torso lengths reaches the singles sideline; the torso looking ~20 % bigger is at
        /// the net (a step of ~0.5 m from 2.5-3 m), ~10 % smaller is far behind the baseline. A dead zone of 3 % keeps
        /// the depth still while the player only leans or bends.
        /// </summary>
        public static Vector2 BodyToCourt(float lateral, float nearer, float fullSide = 0.9f, float depthPerScale = 50f)
        {
            float x = Mathf.Clamp(lateral / fullSide, -1.35f, 1.35f) * Court.SinglesHalfWidth;
            float d = nearer - 1f;
            d = Mathf.Sign(d) * Mathf.Max(0f, Mathf.Abs(d) - 0.03f);
            float depth = Mathf.Clamp(HomeDepth - d * depthPerScale, NearestToNet, Furthest);
            return new Vector2(x, -depth);
        }

        /// <summary>Starts the match (the first serve).</summary>
        public void Begin() => StartPoint();

        // ------------------------------------------------------------------------------------------------ stepping

        public void Step(float dt)
        {
            if (dt <= 0f) return;
            Time += dt;
            while (_ballTime + BallPhysics.Step <= Time) FixedStep();
            MovePlayers(dt);
            switch (Phase)
            {
                case MatchPhase.Serve:
                    if (Time >= _tossAt) Toss();
                    break;
                case MatchPhase.PointOver:
                    if (Time >= _nextPointAt)
                    {
                        if (!Practice && Score.MatchOver) Phase = MatchPhase.Over;
                        else StartPoint();
                    }
                    break;
            }
        }

        /// <summary>Where the ball is drawn now (between fixed steps).</summary>
        public Vector3 BallPosition
        {
            get
            {
                if (!BallLive) return HandPosition(Server);
                float since = (float)(Time - _ballTime);
                bool resting = Ball.Position.y <= Court.BallRadius + 1e-4f && Mathf.Abs(Ball.Velocity.y) < 1e-3f;
                float g = resting ? 0f : BallPhysics.GravityFor(Ball.Spin);
                var p = Ball.Position + Ball.Velocity * since + Vector3.down * (0.5f * g * since * since);
                p.y = Mathf.Max(Court.BallRadius, p.y);
                return p;
            }
        }

        void FixedStep()
        {
            var e = BallPhysics.Advance(ref Ball, BallPhysics.Step);
            _ballTime += BallPhysics.Step;
            switch (Phase)
            {
                case MatchPhase.Toss:
                    if (ServerTeam == 1 && !_cpuServeDone && _ballTime >= _cpuServeAt) CpuServe();
                    else if (_ballTime >= _tossStart + TossCatch) Caught();
                    break;
                case MatchPhase.Rally:
                    if (e == BallEvent.Net) OnNet();
                    else if (e == BallEvent.Bounce) OnBounce();
                    if (Phase == MatchPhase.Rally) CheckContact();
                    if (Phase == MatchPhase.Rally && _ballTime - _lastHitTime > 9.0) EndPoint(1 - LastHitter, PointReason.Winner);   // never stuck
                    break;
            }
        }

        // ------------------------------------------------------------------------------------------------ points

        void StartPoint()
        {
            Phase = MatchPhase.Serve;
            DeuceCourt = Practice || Score.DeuceCourt;
            BallLive = false;
            LastHitter = -1;
            RallyHits = 0;
            PointWinner = -1;
            Next.Active = false;
            _heldHit = false;
            int s = ServerTeam;
            float serveSide = DeuceCourt ? 1f : -1f;
            foreach (var p in Players)
            {
                p.Mood = 0;
                p.DiveAt = -1;
                p.Serving = false;
                p.Velocity = Vector2.zero;
            }
            // the server stands behind the baseline on the side it serves from; the player receiving stands where the
            // body is, the computer receiving waits on the side the serve comes to
            Place(Server, Court.RightSign(s) * serveSide * 0.9f, Court.Side(s) * 12.4f);
            if (s == 1) Place(Human, _wanted.x, _wanted.y);
            else Place(Cpu, Court.RightSign(1) * serveSide * 2f, Court.Side(1) * HomeDepth);
            Server.Serving = true;
            double earliest = Time + (s == 0 ? 0.9 : 1.4);
            if (Practice && PracticeToss != null) earliest = Math.Max(Time + 0.5, PracticeToss(earliest + TossApex) - TossApex);
            _tossAt = earliest;
        }

        static void Place(CourtPlayer p, float x, float z) => p.Position = p.Target = new Vector2(x, z);

        Vector3 HandPosition(CourtPlayer p) =>
            new Vector3(p.Position.x - p.RacketSide * 0.28f, TossHeight - 0.35f, p.Position.y + Court.Side(1 - p.Team) * 0.25f);

        void Toss()
        {
            Phase = MatchPhase.Toss;
            var server = Server;
            var hand = HandPosition(server) + Vector3.up * 0.35f;
            Ball = new BallState(hand, new Vector3(0f, TossSpeed, 0f));
            _ballTime = Time;
            BallLive = true;
            _tossStart = Time;
            TossTop = Time + TossApex;
            _cpuServeDone = false;
            if (ServerTeam == 1)
            {
                _cpuServeAt = TossTop + Gauss() * 0.04;
                ShowSwing(server, Stroke.Overhead, _cpuServeAt, true, ShotKind.Serve);
            }
            Raise(new MatchEvent { Kind = MatchEventKind.Toss, Team = ServerTeam, Player = server.Index, Position = hand });
        }

        void Caught()
        {
            if (ServerTeam == 0) TossesMissed++;
            Raise(new MatchEvent { Kind = MatchEventKind.Caught, Team = ServerTeam, Player = Server.Index, Position = Ball.Position });
            Phase = MatchPhase.Serve;
            BallLive = false;
            _tossAt = Time + 0.9;
            if (Practice && PracticeToss != null) _tossAt = Math.Max(Time + 0.5, PracticeToss(_tossAt + TossApex) - TossApex);
        }

        void EndPoint(int winner, PointReason reason)
        {
            if (Phase == MatchPhase.PointOver || Phase == MatchPhase.Over) return;
            Phase = MatchPhase.PointOver;
            Next.Active = false;
            _heldHit = false;
            PointWinner = winner;
            LastReason = reason;
            LongestRally = Math.Max(LongestRally, RallyHits);
            var call = ScoreCall.Points;
            if (!Practice)
            {
                call = Score.PointTo(winner);
                if (winner == 0) PointsWon++;
                else PointsLost++;
            }
            LastCall = call;
            foreach (var p in Players)
            {
                p.Mood = p.Team == winner ? 1 : -1;
                p.Target = p.Position;
            }
            _nextPointAt = Time + (call == ScoreCall.Match ? MatchPause : call == ScoreCall.Game ? GamePause : PointPause);
            Raise(new MatchEvent { Kind = MatchEventKind.Point, Team = winner, Player = -1, Position = Ball.Position, Reason = reason, Call = call });
        }

        void OnNet()
        {
            Raise(new MatchEvent { Kind = MatchEventKind.Net, Team = LastHitter, Player = -1, Position = Ball.Position });
            EndPoint(1 - LastHitter, PointReason.Net);
        }

        void OnBounce()
        {
            int side = Court.TeamAt(Ball.Position.z);
            int receiver = 1 - LastHitter;
            bool rolling = Ball.Velocity.y <= 0f;
            if (side != receiver)
            {
                // on the hitter's own side (only possible right at the net): their point is lost
                Raise(new MatchEvent { Kind = MatchEventKind.Bounce, Team = side, Player = -1, Position = Ball.Position, In = false });
                EndPoint(receiver, PointReason.Net);
                return;
            }
            _bouncesOnReceiver++;
            if (_bouncesOnReceiver == 1)
            {
                bool inside = LastWasServe ? Court.InServiceBox(Ball.Position, receiver, DeuceCourt) : Court.InCourt(Ball.Position, receiver);
                Raise(new MatchEvent { Kind = MatchEventKind.Bounce, Team = side, Player = -1, Position = Ball.Position, In = inside });
                if (!inside) EndPoint(receiver, PointReason.Out);
                else if (rolling) EndPoint(LastHitter, PointReason.Winner);
                return;
            }
            Raise(new MatchEvent { Kind = MatchEventKind.Bounce, Team = side, Player = -1, Position = Ball.Position, In = true });
            EndPoint(LastHitter, PointReason.Winner);
        }

        // ------------------------------------------------------------------------------------------------ the player's swings

        /// <summary>
        /// The player swung. <paramref name="time"/> is when the racket met the air, in match time (the camera's capture
        /// time mapped onto the match clock: earlier than now by the camera's lag); <paramref name="speed"/> torso lengths
        /// per second; <paramref name="direction"/> on the screen (y up). Every swing is shown, hit or miss.
        /// </summary>
        public SwingResult Swing(double time, float speed, Vector2 direction)
        {
            time = Math.Min(time, Time);
            float power = Mathf.InverseLerp(SlowSwing, FastSwing, speed);
            float vertical = Mathf.Clamp(direction.y, -1f, 1f);
            if (ServerTeam == 0 && (Phase == MatchPhase.Serve || Phase == MatchPhase.Toss) && Human.Serving)
            {
                if (Phase == MatchPhase.Serve)
                {
                    // a first swing tosses the ball (like Wii's flick up), unless the toss is planned (practice)
                    if (!(Practice && PracticeToss != null) && Time - (_tossAt - 0.9) > 0.3) Toss();
                    ShowSwing(Human, Stroke.Overhead, Time + 0.1, false, ShotKind.Serve);
                    return SwingResult.Toss;
                }
                double sinceToss = time - _tossStart;
                if (sinceToss < 0.12) return Whiff(SwingResult.Early);
                if (sinceToss > TossCatch - 0.12) return Whiff(SwingResult.Late);
                HumanServe(time, power, Math.Abs(time - TossTop) <= PowerServeWindow, (float)((time - TossTop) / 0.45));
                return SwingResult.Serve;
            }

            if (Phase == MatchPhase.Rally && Next.Active && Next.Team == 0 && !Next.Taken)
            {
                double e = time - Next.Time;
                if (e < -EarlyWindow) return Whiff(SwingResult.Early);
                if (e > LateWindow) return Whiff(SwingResult.Late);
                Next.Taken = Next.Locked = true;
                _heldTiming = (float)(e < 0 ? e / EarlyWindow : e / LateWindow);
                _heldPerfect = Math.Abs(e) <= PerfectWindow;
                _heldPower = power;
                _heldVertical = vertical;
                if (_ballTime < Next.Time)
                {
                    _heldHit = true;   // early: the racket waits for the ball
                    ShowSwing(Human, Next.Stroke, Next.Time, true, KindFor(Next, _heldPower, _heldVertical));
                    return SwingResult.Hit;
                }
                ShowSwing(Human, Next.Stroke, Time, true, KindFor(Next, _heldPower, _heldVertical));
                return HumanHit(Math.Max(Next.Time, Math.Min(time, _ballTime))) ? SwingResult.Hit : SwingResult.TooFar;
            }
            return Whiff(SwingResult.NoBall);
        }

        SwingResult Whiff(SwingResult result)
        {
            if (Phase == MatchPhase.Rally || Phase == MatchPhase.Toss) HumanWhiffs++;
            var stroke = Human.Serving ? Stroke.Overhead : Next.Active && Next.Team == 0 ? Next.Stroke : StrokeFor(Human, BallPosition, Human.Position);
            ShowSwing(Human, stroke, Time + 0.1, false, ShotKind.Flat);
            Raise(new MatchEvent { Kind = MatchEventKind.Whiff, Team = 0, Player = 0, Position = BallPosition });
            return result;
        }

        /// <summary>The player's hit (now, or when the ball arrives for an early swing): only if they stand near enough.</summary>
        bool HumanHit(double launchTime)
        {
            var c = Next;
            _heldHit = false;
            if (Vector2.Distance(Human.Position, c.Spot) > ReachTolerance)
            {
                // on time, but too far away: the racket swings past the ball, which goes by
                c.Active = false;
                TooFar++;
                Human.DiveAt = c.Time;
                Raise(new MatchEvent { Kind = MatchEventKind.TooFar, Team = 0, Player = 0, Position = c.Point });
                return false;
            }
            var from = launchTime <= _ballTime ? BallPhysics.At(_path, launchTime).Position : Ball.Position;
            c.Active = false;
            HumanHits++;
            if (_heldPerfect) HumanPerfect++;
            var shot = HumanShot(c, from, _heldTiming, _heldPower, _heldVertical, _heldPerfect);
            Launch(0, Human, from, shot);
            return true;
        }

        static ShotKind KindFor(Contact c, float power, float vertical)
        {
            if (c.Smash) return ShotKind.Smash;
            if (c.Volley) return ShotKind.Volley;
            if (vertical > 0.55f && power < 0.45f) return ShotKind.Lob;
            if (vertical < -0.45f) return ShotKind.Slice;
            if (vertical > 0.3f) return ShotKind.Topspin;
            return ShotKind.Flat;
        }

        /// <summary>The player's shot from their swing: aimed by its timing (early pulls across), powered by its speed.</summary>
        Shot HumanShot(Contact c, Vector3 from, float timing, float power, float vertical, bool perfect)
        {
            var kind = KindFor(c, power, vertical);
            float strokeSide = c.Stroke == Stroke.Backhand ? -c.Hitter.RacketSide : c.Hitter.RacketSide;
            float x = Mathf.Lerp(from.x, 0f, 0.55f) + strokeSide * timing * 3.4f;
            float speed = SpeedFor(kind, power);
            if (perfect) speed *= 1.15f;
            var target = Target(1, x, DepthFor(kind, power));
            // later players punish the window's very edge: way too early or late goes wide (Wii); never a smash
            if (Mathf.Abs(timing) > Level.WideFrom && kind != ShotKind.Smash)
                target.x = Mathf.Sign(strokeSide * timing) * (Court.SinglesHalfWidth + 0.5f + 0.6f * (Mathf.Abs(timing) - Level.WideFrom));
            return new Shot { Kind = kind, Team = 0, Stroke = c.Stroke, Speed = speed, Timing = timing, Perfect = perfect, Target = target };
        }

        static float SpeedFor(ShotKind kind, float power) => kind switch
        {
            ShotKind.Topspin => 12f + 8f * power,
            ShotKind.Slice => 10.5f + 5f * power,
            ShotKind.Lob => 9.5f,
            ShotKind.Volley => 12f + 6f * power,
            ShotKind.Smash => 23f + 3f * power,
            _ => 13f + 8f * power,
        };

        static float DepthFor(ShotKind kind, float power) => kind switch
        {
            ShotKind.Topspin => 8.8f + 1.8f * power,
            ShotKind.Slice => 7f + 2f * power,
            ShotKind.Lob => 10.3f,
            ShotKind.Volley => 5.5f + 2.5f * power,
            ShotKind.Smash => 6f + 2f * power,
            _ => 8.2f + 2.2f * power,
        };

        static float SpinFor(ShotKind kind) => kind switch
        {
            ShotKind.Topspin => 0.75f,
            ShotKind.Slice => -0.7f,
            ShotKind.Lob => 0.3f,
            ShotKind.Volley => 0f,
            ShotKind.Smash => 0.1f,
            ShotKind.PowerServe => 0.05f,
            ShotKind.Serve => 0.3f,
            _ => 0.15f,
        };

        static float ClearanceFor(ShotKind kind) => kind switch
        {
            ShotKind.Topspin => 0.9f,
            ShotKind.Slice => 0.25f,
            ShotKind.Lob => 3f,
            ShotKind.Volley => 0.3f,
            ShotKind.Smash => 0.1f,
            ShotKind.PowerServe => 0.15f,
            ShotKind.Serve => 0.4f,
            _ => 0.45f,
        };

        /// <summary>A bounce spot on <paramref name="team"/>'s half, kept inside the singles lines.</summary>
        static Vector3 Target(int team, float x, float depth) =>
            new Vector3(Mathf.Clamp(x, -Court.SinglesHalfWidth + 0.4f, Court.SinglesHalfWidth - 0.4f), 0f,
                        Court.Side(team) * Mathf.Clamp(depth, 1.5f, Court.HalfLength - 0.4f));

        void HumanServe(double time, float power, bool perfect, float timing)
        {
            var from = TossAt(time);
            var kind = perfect ? ShotKind.PowerServe : ShotKind.Serve;
            // early toward the middle line, late out wide (the deuce box is on the receiver's right)
            float across = Mathf.Lerp(0.6f, Court.SinglesHalfWidth - 0.5f, Mathf.InverseLerp(-1f, 1f, timing));
            float x = Court.RightSign(1) * (DeuceCourt ? 1f : -1f) * across;
            float speed = perfect ? 21f + 2f * power : 14f + 5f * power;
            ShowSwing(Human, Stroke.Overhead, Time, true, kind);
            TossesMissed = 0;
            Human.Serving = false;
            var shot = new Shot { Kind = kind, Team = 0, Stroke = Stroke.Overhead, Speed = speed, Perfect = perfect, Timing = timing,
                                  Target = new Vector3(x, 0f, Court.Side(1) * (Court.ServiceLine - 0.9f)) };
            Launch(0, Human, from, shot);
        }

        Vector3 TossAt(double time)
        {
            float t = (float)Math.Max(0.0, time - _tossStart);
            var hand = HandPosition(Server) + Vector3.up * 0.35f;
            return hand + Vector3.up * (TossSpeed * t - 0.5f * BallPhysics.Gravity * t * t);
        }

        // ------------------------------------------------------------------------------------------------ hitting and planning

        void Launch(int team, CourtPlayer hitter, Vector3 from, Shot shot, bool intoNet = false)
        {
            float spin = intoNet ? 0f : SpinFor(shot.Kind);
            var v = intoNet ? BallPhysics.IntoTheNet(from, shot.Target, shot.Speed)
                            : BallPhysics.Aim(from, shot.Target, shot.Speed, spin, ClearanceFor(shot.Kind), out _);
            Ball = new BallState(from, v, spin);
            BallLive = true;
            Phase = MatchPhase.Rally;
            LastHitter = team;
            LastWasServe = shot.Kind is ShotKind.Serve or ShotKind.PowerServe;
            LastShot = shot;
            _bouncesOnReceiver = 0;
            _lastHitTime = _ballTime;
            RallyHits++;
            foreach (var p in Players) p.Serving = false;
            Raise(new MatchEvent { Kind = MatchEventKind.Hit, Team = team, Player = hitter.Index, Position = from, Shot = shot });
            Plan(1 - team);
            if (team == 1) Cpu.Target = new Vector2(Mathf.Clamp(shot.Target.x * 0.35f, -2f, 2f), Court.Side(1) * HomeDepth);   // back to cover
        }

        /// <summary>
        /// Plans the receiver's touch on the ball just hit. Nothing to plan for a ball going into the net or out (the
        /// receiver lets it go: the point is theirs). The player's touch keeps following their body until it locks.
        /// </summary>
        void Plan(int r)
        {
            BallPhysics.Predict(Ball, _ballTime, 6f, _path, maxBounces: 2);
            Next.Active = false;
            Next.Team = r;
            Next.Taken = Next.Locked = Next.SwingShown = false;
            _volleyMissed = false;
            _bounce = -1;
            _landsIn = false;
            for (int i = 1; i < _path.Count; i++)
            {
                if (_path[i].Event == BallEvent.Net) return;
                if (_path[i].Event == BallEvent.Bounce) { _bounce = i; break; }
            }
            if (_bounce < 0) return;
            var landing = _path[_bounce].Ball.Position;
            _landsIn = Court.TeamAt(landing.z) == r &&
                       (LastWasServe ? Court.InServiceBox(landing, r, DeuceCourt) : Court.InCourt(landing, r));
            if (!_landsIn) return;
            if (r == 0) PlanHuman();
            else PlanCpu();
        }

        /// <summary>The player's touch for where the body stands now: a volley if the ball would bounce behind them,
        /// else after the bounce; the spot to stand on, and whether the glide gets there.</summary>
        void PlanHuman()
        {
            var w = _wanted;
            bool volley = !LastWasServe && !_volleyMissed && !Practice;
            int at = ContactAt(_path, _bounce, 0, -w.y, volley, _ballTime + 0.05, out bool isVolley, AssistDepth * 0.8f);
            if (at < 0)
            {
                Next.Active = false;
                return;
            }
            var point = _path[at].Ball.Position;
            Next.Active = true;
            Next.Hitter = Human;
            Next.Time = _path[at].Time;
            Next.Point = point;
            Next.Volley = isVolley;
            Next.Smash = isVolley && point.y >= 2.1f;
            Next.Stroke = Next.Smash ? Stroke.Overhead : StrokeFor(Human, point, w);
            Next.Spot = SpotFor(Human, Next.Stroke, point);
            Next.Reachable = Practice || AssistScale(Next.Spot - w) <= 1f;
        }

        void PlanCpu()
        {
            int at = ContactAt(_path, _bounce, 1, HomeDepth, false, _ballTime, out _);
            if (at < 0) return;
            var point = _path[at].Ball.Position;
            Next.Active = true;
            Next.Hitter = Cpu;
            Next.Time = _path[at].Time;
            Next.Point = point;
            Next.Volley = Next.Smash = false;
            Next.Stroke = StrokeFor(Cpu, point, Cpu.Position);
            Next.Spot = SpotFor(Cpu, Next.Stroke, point);
            Cpu.Target = Next.Spot;
            float run = Vector2.Distance(Cpu.Position, Next.Spot);
            Next.Reachable = Practice || run / Cpu.MaxSpeed + Level.Reaction <= Next.Time - _ballTime;
            _cpuReturns = Practice || (Next.Reachable && Chance(ReturnChance(run)));
        }

        /// <summary>
        /// Where on a predicted path team <paramref name="r"/>'s player, standing <paramref name="depth"/> metres behind
        /// the net, meets the ball (index, or -1): a volley if it would bounce behind them, as it passes them or, a ball
        /// over their head, up to <paramref name="stepBack"/> further back where it has come down to reach (a smash);
        /// else after the bounce as it reaches their depth on the rise, comes down to waist height or gets
        /// <see cref="BackLimit"/> deep. Only points from <paramref name="after"/> on (the future).
        /// </summary>
        static int ContactAt(List<PathPoint> path, int bounce, int r, float depth, bool volley, double after, out bool isVolley,
                             float stepBack = 0f)
        {
            isVolley = false;
            float side = Court.Side(r);
            if (bounce < 0) return -1;
            if (volley && path[bounce].Ball.Position.z * side > depth + 0.6f)
            {
                for (int i = 1; i < bounce; i++)
                {
                    var p = path[i].Ball.Position;
                    float deep = p.z * side;
                    if (deep < depth - 0.2f || path[i].Time < after) continue;
                    if (deep > depth + stepBack) break;           // too far behind them: it bounces first
                    if (p.y < 0.35f) break;                       // at their feet: played after the bounce
                    if (p.y <= 3.1f)
                    {
                        isVolley = true;
                        return i;
                    }
                }
            }
            for (int i = bounce + 1; i < path.Count; i++)
            {
                var b = path[i].Ball;
                if (path[i].Bounces >= 2) break;
                if (path[i].Time < after) continue;
                float deep = b.Position.z * side;
                if ((deep >= depth - 0.2f && b.Position.y >= 0.3f) || (b.Velocity.y <= 0f && b.Position.y <= ContactHeight) || deep >= BackLimit)
                    return i;
            }
            for (int i = path.Count - 1; i > bounce; i--)
                if (path[i].Time >= after && path[i].Bounces < 2) return i;
            return -1;
        }

        /// <summary>Beside the ball on the racket's side, a little behind it.</summary>
        static Vector2 SpotFor(CourtPlayer p, Stroke stroke, Vector3 ball)
        {
            float racket = stroke == Stroke.Backhand ? -p.RacketSide : p.RacketSide;
            float beside = stroke == Stroke.Overhead ? 0.25f : 0.75f;
            return new Vector2(ball.x - racket * beside, ball.z + Court.Side(p.Team) * 0.35f);
        }

        /// <summary>How far an offset is in units of the assist's reach (1 = its edge): an ellipse, wider in depth.</summary>
        float AssistScale(Vector2 d) => Mathf.Sqrt(d.x * d.x / (AssistSide * AssistSide) + d.y * d.y / (AssistDepth * AssistDepth));

        /// <summary>The computer's chance to get this ball back: its level, less for fast, perfect, smashed or far balls
        /// (later players cope with those better: <see cref="TennisLevel.Pressure"/>). Serves come back more easily than
        /// rally balls (a power serve less so): a player who only serves shouldn't win.</summary>
        float ReturnChance(float run)
        {
            var s = LastShot;
            float hard = 0f;
            if (s.Kind == ShotKind.Serve) hard -= 0.1f;
            else if (s.Kind == ShotKind.PowerServe) hard += 0.06f;
            else
            {
                if (s.Perfect) hard += 0.15f;
                hard += Mathf.Max(0f, s.Speed - 14f) * 0.025f;
                if (s.Kind == ShotKind.Smash) hard += 0.35f;
                if (s.Kind == ShotKind.Lob) hard -= 0.05f;
                hard += Mathf.Max(0f, run - 3f) * 0.04f;
            }
            return Mathf.Clamp(Level.Return - hard * Level.Pressure, 0.15f, 0.98f);
        }

        /// <summary>Forehand when the ball is on the racket's side of where the player stands.</summary>
        static Stroke StrokeFor(CourtPlayer p, Vector3 ball, Vector2 standing) =>
            (ball.x - standing.x) * p.RacketSide >= -0.3f ? Stroke.Forehand : Stroke.Backhand;

        void CheckContact()
        {
            if (!Next.Active) return;
            var c = Next;
            if (c.Team == 1)
            {
                if (!c.SwingShown && _ballTime >= c.Time - 0.32)
                {
                    c.SwingShown = true;
                    if (!c.Reachable) Cpu.DiveAt = c.Time;
                    else ShowSwing(Cpu, c.Stroke, _cpuReturns ? c.Time : c.Time + 0.14, _cpuReturns, ShotKind.Flat);
                }
                if (_ballTime >= c.Time)
                {
                    c.Active = false;
                    if (_cpuReturns && c.Reachable) CpuHit(c);
                }
                return;
            }
            // the player's touch follows the body until shortly before the ball arrives
            if (!c.Locked)
            {
                if (c.Time - _ballTime <= LockBefore) c.Locked = true;
                else
                {
                    PlanHuman();
                    if (!Next.Active) return;
                }
            }
            if (_heldHit && _ballTime >= c.Time)
            {
                HumanHit(c.Time);
                return;
            }
            if (!c.Taken && _ballTime > c.Time + LateWindow + CloseAfter)
            {
                c.Active = false;
                Raise(new MatchEvent { Kind = MatchEventKind.Missed, Team = 0, Player = 0, Position = Ball.Position });
                // a missed volley can still be played after the bounce (forgiving)
                if (c.Volley && _bouncesOnReceiver == 0)
                {
                    _volleyMissed = true;
                    Next.Locked = Next.SwingShown = false;
                    PlanHuman();
                }
            }
        }

        // ------------------------------------------------------------------------------------------------ the computer

        void CpuHit(Contact c)
        {
            var from = Ball.Position;
            Shot shot;
            bool net = false;
            if (Practice)
            {
                shot = new Shot { Kind = ShotKind.Flat, Team = 1, Stroke = c.Stroke, Speed = 12f,
                                  Target = Target(0, _wanted.x + (float)(_rng.NextDouble() * 3.0 - 1.5), 9.6f) };
                if (PracticeContact != null) shot.Speed = FeedSpeed(from, shot.Target, SpinFor(ShotKind.Flat));
            }
            else
            {
                ShotKind kind;
                double roll = _rng.NextDouble();
                bool shortBall = Chance(Level.ShortChance);
                if (shortBall) kind = ShotKind.Slice;
                else kind = roll < Level.LobChance ? ShotKind.Lob : roll < Level.LobChance + 0.2 ? ShotKind.Slice : roll < Level.LobChance + 0.55 ? ShotKind.Topspin : ShotKind.Flat;
                // into the corner away from the player, or back toward them
                float x;
                var human = Human.Position;
                if (Chance(Level.AimAway))
                {
                    float away = Mathf.Abs(human.x) < 0.5f ? (_rng.NextDouble() < 0.5 ? -1f : 1f) : -Mathf.Sign(human.x);
                    x = away * (2.6f + (float)_rng.NextDouble() * 1.2f);
                }
                else x = human.x + (float)(_rng.NextDouble() * 2.4 - 1.2);
                x += Gauss() * Level.Wobble * 3f;
                float power = Mathf.Clamp01(Mathf.InverseLerp(11f, 19f, Level.Speed) + Gauss() * 0.15f);
                float depth = shortBall ? 4.2f + (float)_rng.NextDouble() * 1.6f : DepthFor(kind, power);
                // at the net, the player is passed (to the side) or lobbed (over them)
                if (-human.y < 6f && !shortBall) depth = Mathf.Max(depth, 9f);
                float speed = kind is ShotKind.Lob ? SpeedFor(kind, power)
                    : Level.Speed * (kind == ShotKind.Slice ? (shortBall ? 0.6f : 0.85f) : kind == ShotKind.Topspin ? 0.95f : 1f) + Gauss() * 1.2f;
                var target = Target(0, x, depth);
                if (Chance(Level.Error))
                {
                    // a mistake: long, wide or into the net
                    double which = _rng.NextDouble();
                    if (which < 0.4) target.z = -(Court.HalfLength + 0.8f + (float)_rng.NextDouble());
                    else if (which < 0.75) target.x = Mathf.Sign(x == 0f ? 1f : x) * (Court.SinglesHalfWidth + 0.6f + (float)_rng.NextDouble() * 0.8f);
                    else net = true;
                }
                shot = new Shot { Kind = kind, Team = 1, Stroke = c.Stroke, Speed = speed, Target = target };
            }
            Launch(1, Cpu, from, shot, intoNet: net);
        }

        /// <summary>Practice: the feed's speed that brings the ball to the player's racket when <see cref="PracticeContact"/>
        /// wants it (as close as a speed between 6 and 20 m/s can).</summary>
        float FeedSpeed(Vector3 from, Vector3 target, float spin)
        {
            double ContactAtSpeed(float speed)
            {
                var v = BallPhysics.Aim(from, target, speed, spin, ClearanceFor(ShotKind.Flat), out _);
                BallPhysics.Predict(new BallState(from, v, spin), _ballTime, 6f, _scratch, 2);
                int bounce = _scratch.FindIndex(p => p.Event == BallEvent.Bounce);
                int at = ContactAt(_scratch, bounce, 0, -_wanted.y, false, _ballTime, out _);
                return at < 0 ? _scratch[_scratch.Count - 1].Time : _scratch[at].Time;
            }
            float fast = 20f, slow = 6f;
            double want = PracticeContact(ContactAtSpeed(fast));
            if (ContactAtSpeed(slow) <= want) return slow;
            for (int i = 0; i < 14; i++)
            {
                float mid = (fast + slow) * 0.5f;
                if (ContactAtSpeed(mid) > want) slow = mid;
                else fast = mid;
            }
            return (fast + slow) * 0.5f;
        }

        void CpuServe()
        {
            _cpuServeDone = true;
            var from = Ball.Position;
            bool power = Chance(Level.PowerServeChance);
            float across = Mathf.Lerp(0.6f, Court.SinglesHalfWidth - 0.5f, (float)_rng.NextDouble());
            float x = Court.RightSign(0) * (DeuceCourt ? 1f : -1f) * across;
            float speed = power ? 20f + (float)_rng.NextDouble() * 2f : Level.ServeSpeed + Gauss();
            Cpu.Serving = false;
            var shot = new Shot { Kind = power ? ShotKind.PowerServe : ShotKind.Serve, Team = 1, Stroke = Stroke.Overhead, Speed = speed,
                                  Target = new Vector3(x, 0f, Court.Side(0) * (Court.ServiceLine - 0.9f)) };
            Launch(1, Cpu, from, shot);
        }

        // ------------------------------------------------------------------------------------------------ helpers

        void ShowSwing(CourtPlayer p, Stroke stroke, double contact, bool hits, ShotKind kind)
        {
            p.Swings++;
            p.SwingStroke = stroke;
            p.SwingContact = contact;
            p.SwingHits = hits;
            p.SwingKind = kind;
        }

        /// <summary>The computer runs to its targets. The player goes where the body says, except while serving, and
        /// glides toward the spot beside an incoming ball when it's within the assist's reach (or as far toward it as
        /// the assist goes, when not).</summary>
        void MovePlayers(float dt)
        {
            foreach (var p in Players)
            {
                // a ball coming to them, or their own toss in the air: the racket goes back (behind the head to serve)
                bool tossing = Phase == MatchPhase.Toss && p == Server;
                p.Preparing = tossing || Phase == MatchPhase.Rally && Next.Active && Next.Team == p.Team && !Next.Taken && Next.Time - Time <= 0.6;
                if (p.Preparing) p.PrepareStroke = tossing ? Stroke.Overhead : Next.Stroke;
            }
            if (Human.Serving) Human.Target = Human.Position;
            else if (Phase == MatchPhase.Rally && Next.Active && Next.Team == 0)
            {
                var d = Next.Spot - _wanted;
                float s = AssistScale(d);
                if (s > 1f && !Practice) d /= s;
                Human.Target = _wanted + d;
            }
            else Human.Target = _wanted;
            foreach (var p in Players)
            {
                var to = p.Target - p.Position;
                float dist = to.magnitude;
                var want = dist > 1e-3f ? to / dist * Mathf.Min(p.MaxSpeed, dist * 6f) : Vector2.zero;
                p.Velocity = Vector2.MoveTowards(p.Velocity, want, 45f * dt);
                p.Position += p.Velocity * dt;
            }
        }

        bool Chance(float p) => _rng.NextDouble() < p;

        float Gauss()
        {
            double u1 = 1.0 - _rng.NextDouble(), u2 = _rng.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }

        void Raise(MatchEvent e) => Events?.Invoke(e);

        /// <summary>The planned path of the ball in the air (for drawing hints), oldest first.</summary>
        public IReadOnlyList<PathPoint> Path => _path;
    }
}
