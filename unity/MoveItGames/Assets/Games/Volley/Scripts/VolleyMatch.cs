using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Volley
{
    /// <summary>How the ball was played (what the toy's animation shows).</summary>
    public enum Touch
    {
        None,
        /// <summary>An overhand jump serve at the top of the toss.</summary>
        Serve,
        /// <summary>The forearm pass: the defend move, the ball up to the teammate (or a bump-set).</summary>
        Bump,
        /// <summary>Both hands up: the ball up for the teammate to hit.</summary>
        Set,
        /// <summary>A jump and a one-hand hit over the net, as fast as the hand went.</summary>
        Spike,
        /// <summary>A soft ball over the net (a set or a bump on the third touch, a one-hand push on the second).</summary>
        Push,
        /// <summary>The wrong move at a ball coming over the net: it flies off the arms (out).</summary>
        Shank,
        /// <summary>A block at the net: a smash stopped and sent straight back down into the hitter's court.</summary>
        Block,
    }

    /// <summary>
    /// What an attack over the net is (2026-11-08, user: "at the moment the ball is shot from the enemy, the player chooses
    /// between low hands save, or high hands block on the net; depending on the type of shot the chosen save is either
    /// correct or wrong"): the computer decides its own as it sets up its hitter (and shows it by the hitter's look); a
    /// player's is their hit's (versus).
    /// </summary>
    public enum Attack
    {
        None,
        /// <summary>A soft ball over with no jump at the net (a push, a serve): received low, any time before it comes.</summary>
        Free,
        /// <summary>A spike from a jump at the net: <b>blocked</b> with both hands up as the hitter hits; too hard to receive
        /// low (it flies off the arms).</summary>
        Smash,
        /// <summary>A tip from the same jump, softly over the block: <b>received</b> low; a block is beaten (the ball drops
        /// behind it).</summary>
        Tip,
    }

    /// <summary>What the defending player showed as the other side attacked (<see cref="VolleyMatch.ChoiceAt"/>).</summary>
    public enum Defence { None, Block, Receive }

    public enum VolleyPhase { Serve, Toss, Rally, PointOver, Over }

    /// <summary>Why a point ended (for the calls).</summary>
    public enum PointReason
    {
        /// <summary>The ball came down in the court and nobody played it (or the player's move came too late).</summary>
        Landed,
        Out,
        Net,
        /// <summary>The ball came over the net and was received with the wrong move (both hands up or one hand).</summary>
        Wrong,
        /// <summary>A smash blocked straight back down: the blockers' point.</summary>
        Block,
        /// <summary>A tip over a block: it dropped behind the blocker.</summary>
        OverBlock,
        /// <summary>A smash received low: too hard, it flew off the arms.</summary>
        TooHard,
    }

    public enum VolleyEventKind
    {
        Toss, Caught, Touch, Land, Net, Point,
        /// <summary>A ball of the player's went by without a move for it (the hint: what to do).</summary>
        Missed,
        /// <summary>A ball the player's toy couldn't get to (a hard attack into a gap, on the harder difficulties).</summary>
        OutOfReach,
        /// <summary>A computer's toy dived for a ball and missed it.</summary>
        Dive,
        /// <summary>The other side hit a jumping attack at a player's team: what it was (<see cref="VolleyEvent.Attack"/>),
        /// what the player chose at that moment (<see cref="VolleyEvent.Choice"/>) and whether it was the right one.</summary>
        Defence,
    }

    public struct VolleyEvent
    {
        public VolleyEventKind Kind;
        public int Team;
        /// <summary>Index into <see cref="VolleyMatch.Players"/> (or -1).</summary>
        public int Player;
        public Vector3 Position;
        public Touch Touch;
        /// <summary>A touch's power (0..1: a hit's hand speed) and whether it was a perfect spike.</summary>
        public float Power;
        public bool Perfect;
        /// <summary>A touch made after the ball had passed its point (the camera's news came late): from lower down.</summary>
        public bool Late;
        /// <summary>The touch's number for its team (1 = the receive, 2, 3; 0 = the serve).</summary>
        public int Number;
        /// <summary>A landing inside the court (<see cref="VolleyEventKind.Land"/>).</summary>
        public bool In;
        public PointReason Reason;
        /// <summary>The move the player made (a wrong one at the receive, or none).</summary>
        public Move Move;
        /// <summary>The attack the ball came from (a receive's, a miss's, a <see cref="VolleyEventKind.Defence"/>), the
        /// defending player's choice and whether it was the right one.</summary>
        public Attack Attack;
        public Defence Choice;
        public bool Correct;
    }

    /// <summary>The ball's flight between two touches: launched from <see cref="From"/> at <see cref="Start"/> (match
    /// time), gravity pulling it down (a floaty set or toss feels gravity less; the rally's pace scales it).</summary>
    public struct Flight
    {
        public Vector3 From, Velocity;
        public double Start;
        public float Gravity;

        public Vector3 At(double t)
        {
            float s = (float)(t - Start);
            return From + Velocity * s + Vector3.down * (0.5f * Gravity * s * s);
        }

        public Vector3 VelocityAt(double t) => Velocity + Vector3.down * (Gravity * (float)(t - Start));

        /// <summary>From <paramref name="from"/> to <paramref name="to"/> in <paramref name="seconds"/>.</summary>
        public static Flight Between(Vector3 from, Vector3 to, float seconds, float gravity, double start)
        {
            seconds = Mathf.Max(0.05f, seconds);
            var v = (to - from) / seconds;
            v.y += 0.5f * gravity * seconds;
            return new Flight { From = from, Velocity = v, Start = start, Gravity = gravity };
        }

        /// <summary>How long a flight from height <paramref name="fromY"/> up to <paramref name="apex"/> and down to
        /// <paramref name="toY"/> takes.</summary>
        public static float Seconds(float fromY, float apex, float toY, float gravity) =>
            Mathf.Sqrt(2f * Mathf.Max(0f, apex - fromY) / gravity) + Mathf.Sqrt(2f * Mathf.Max(0f, apex - toY) / gravity);

        /// <summary>Seconds after <see cref="Start"/> the ball comes down through height <paramref name="h"/> (after its top),
        /// or -1 if it never gets that high.</summary>
        public float DownThrough(float h)
        {
            float d = Velocity.y * Velocity.y + 2f * Gravity * (From.y - h);
            return d < 0f ? -1f : (Velocity.y + Mathf.Sqrt(d)) / Gravity;
        }
    }

    /// <summary>A toy on the court as the rules see it: where it stands and runs to, and what the view animates (a touch
    /// with the moment the ball is met, getting ready for a coming ball, a dive, a cheer).</summary>
    public sealed class VolleyPlayer
    {
        public int Index, Team;
        /// <summary>The player (0 = player 1, 1 = player 2) whose moves play it, or -1: the computer.</summary>
        public int Controller = -1;
        /// <summary>(x, z) on the court.</summary>
        public Vector2 Position, Velocity, Target;
        public float MaxSpeed = 7f, Reaction = 0.15f;
        /// <summary>A quicker speed (m/s) for the coming ball, or 0 (a ball it must get to: its own team's pass, a ball of
        /// the computer's sure part).</summary>
        public float Hurry;
        /// <summary>Match time it may start running to <see cref="Target"/>.</summary>
        public double RunAt;
        /// <summary>Counts touches: the view starts one when it changes.</summary>
        public int Touches;
        public Touch TouchKind;
        /// <summary>Match time the ball is met, and where.</summary>
        public double TouchTime;
        public Vector3 TouchPoint;
        public float TouchPower;
        /// <summary>A hit's hand (the toy hits with the same hand, mirrored like the screen).</summary>
        public bool TouchRight = true;
        /// <summary>The ball is coming to this toy: the move it wants (the view gets ready: the arms low for a bump, up for a
        /// set, the run-up and jump for a hit) and when and where it arrives.</summary>
        public Move Expect;
        public double ExpectTime;
        public Vector3 ExpectPoint;
        /// <summary>A dive for a ball (match time), or -1.</summary>
        public double DiveAt = -1;
        /// <summary>+1 cheering, -1 sad, 0 neither (between points).</summary>
        public int Mood;
        /// <summary>Holds the ball to serve, or tosses it.</summary>
        public bool Serving;
        /// <summary>The attack its coming third touch will be (the computer's, decided as it's set up: the view shows it, a
        /// smash with the hitting arm drawn back and a big jump, a tip with the hand forward and a little jump, a free ball
        /// with the hands up and no jump), or <see cref="Volley.Attack.None"/>; from <see cref="TellAt"/> on it looks like
        /// what it is (a disguised tip looks like a smash until then).</summary>
        public Attack Attack;
        public double TellAt;
        /// <summary>A block: a jump at the net with both hands up, its top at this match time (-1: none).</summary>
        public double BlockAt = -1;

        public Vector3 World => new Vector3(Position.x, 0f, Position.y);
    }

    /// <summary>A jumping attack on its way (from the set to the hitter's hit) at a player's team: who defends and when they
    /// choose. The block is what they show at <see cref="DecideAt"/>, a moment before the hit (the camera's news of it is in
    /// by the hit).</summary>
    public sealed class Threat
    {
        public bool Active;
        /// <summary>The attacking team and its hitter.</summary>
        public int Team;
        public VolleyPlayer Hitter;
        /// <summary>The computer's attack (decided at its set), or <see cref="Volley.Attack.None"/>: a player's is their hit's
        /// (versus).</summary>
        public Attack Kind;
        /// <summary>When the hitter will hit (the set's top), and the moment of the defending player's choice.</summary>
        public double HitTime, DecideAt;
        /// <summary>When the attack shows by the hitter's look (the set; a disguised tip only just before the hit).</summary>
        public double TellAt;
        /// <summary>The defending player (slot) and the toy that blocks (the one in front of the hitter).</summary>
        public int Defender = -1;
        public VolleyPlayer Blocker;
    }

    /// <summary>The next touch of the ball: who meets it, where and when, from when the player's moves count for it.</summary>
    public sealed class Contact
    {
        public bool Active;
        public int Team;
        public VolleyPlayer Player;
        /// <summary>1 = the receive (the ball came over the net), 2, 3 (the third must go over); 0 = the serve.</summary>
        public int Number;
        public double Time;
        public Vector3 Point;
        /// <summary>The toy gets there in time (else it dives and the ball comes down).</summary>
        public bool Reachable = true;
        /// <summary>It gets there only with a dive (the touch is a dig from low down).</summary>
        public bool Dive;
        /// <summary>Moves count from this match time on (the previous touch).</summary>
        public double Open;
        /// <summary>A receive: the attack the ball came from (a smash can't be received: it flies off the arms) and what the
        /// player chose as it was hit.</summary>
        public Attack Attack;
        public Defence Chose;
        /// <summary>The ball came down past the lowest it can be played (match time), or +infinity.</summary>
        public double LowAt = double.PositiveInfinity;
        /// <summary>The computer's touch comes off (a dig can fail).</summary>
        public bool CpuPlays = true;
        /// <summary>Past its moment with no move for it yet: waiting for the camera to catch up.</summary>
        public bool Waiting;
        /// <summary>A ball of the computer's sure part (its first returns of every rally).</summary>
        public bool Sure;
        /// <summary>The player's toy didn't get a move for it: <see cref="VolleyEventKind.Missed"/> was raised.</summary>
        public bool Missed;
    }

    /// <summary>
    /// "Volejboll!": beach volleyball, two against two, after Kinect Sports' beach volleyball and aligned with Tenis!
    /// (2026-11-07, user: "take the tennis game changes, align the volley game to match: the difficulty modes, effects, vs
    /// and together modes with 2 people; don't do it time based / quick event based: two hands up raise the ball for
    /// another person, one hand hits it to the other side, the arm's speed is the hit's speed; to defend the player must
    /// do another movement"). Pure rules (no scene), driven by <see cref="Step"/> and the players' moves
    /// (<see cref="See"/>); <c>VolleyGame</c> draws it.
    /// <list type="bullet">
    /// <item><b>The toys run to the ball by themselves</b> (Tenis!'s since 2026-11-07): the player only makes the moves.
    /// How quick they are is the difficulty's (<see cref="VolleyLevel.HumanRunSpeed"/>).</item>
    /// <item><b>Three touches a side</b>, the toys taking turns: a ball coming over the net (a serve, a soft ball, a tip)
    /// is <b>received with the bump</b> (both hands together low in front, <see cref="Move.Bump"/>; both hands up or one
    /// hand at it shanks it out); the bump sends it up to the teammate by the net, where <b>both hands up set it</b> for
    /// the first toy (a bump there sets it too, less well; one hand pushes it over); the set comes down at the net and
    /// <b>one hand hits it over</b>: a jump and a spike as fast as the hand went (a pose there just pushes it over,
    /// softly). One player plays both toys of their team (the one the ball comes to); two players together a toy each
    /// (player 1's covers the left half).</item>
    /// <item><b>Defending is a choice at the moment of the other side's hit</b> (2026-11-08, user: "the player can keep the
    /// hands together before the ball is shot ... at the moment the ball is shot the player chooses between low hands
    /// save or high hands block; depending on the type of shot the chosen save is correct or wrong"; after Kinect Sports'
    /// block (arms up at the net) and Switch Sports' (a block stops a spike, a lob goes over it)). As a team sets up its
    /// hitter (<see cref="Coming"/>) the defending player shows <b>both hands up = block</b> or <b>hands together low =
    /// receive</b>; what they show at <see cref="Threat.DecideAt"/> (<see cref="BlockLead"/> before the hit) is their
    /// choice. A <b>smash</b> is stopped only by the block (back down into their court: a point); received low it flies
    /// off the arms. A <b>tip</b> goes over a block (their point) and is received low. A <b>free ball</b> (no jump: a
    /// push, a serve) is simply received, any time before it comes. Holding one pose is no defence any more: the
    /// computer mixes its attacks (<see cref="VolleyLevel.FreeBall"/>, <see cref="VolleyLevel.TipShare"/>) and shows
    /// them by its hitter's look (<see cref="VolleyLevel.TellBefore"/>: a "!" on Easy, the run-up on Medium, disguised
    /// tips on Hard, where it also tips more against a player who blocks a lot).</item>
    /// <item><b>Moves are poses and hits, not timing</b>: a pose counts if it was seen any time from the previous touch
    /// until the ball arrives (and a little after): players get ready early, as real players do, which also hides the
    /// camera's lag. A hit counts from <see cref="HitEarly"/> before the ball arrives (an early one waits for it) to
    /// <see cref="HitLate"/> after; the set's top is where it's hit, so a late hit (the camera's news arrives late) still
    /// meets the ball nearly where it was. A ball with no move for it waits for the camera
    /// (<see cref="SawUntil(int, double)"/>), then is played from wherever it is by then (lower: a save), or comes down.
    /// The best move counts (<see cref="Priority"/>): the bump at the receive, the set at the second touch, the hit at the
    /// third, so a pose on the way into another one, or arms raised in joy after a hit, don't change the touch.</item>
    /// <item><b>The player's hits go in</b>: a spike's speed is the hand's, aimed into the open court, clearing the net;
    /// a perfect one (strong and on time) is the hardest to dig.</item>
    /// <item><b>The computer</b> (<see cref="VolleyLevel"/>) digs, sets and attacks by itself; <b>its first
    /// <see cref="SureReturns"/> returns of every rally are sure</b> (Tenis!'s rule, user 2026-11-07: "make the bot hit at
    /// least 5 times, after that it should depend on the difficulty"): it digs everything, and sends free balls within the
    /// player's toys' reach. After them a hard attack of the player's is dug less often, and the computer smashes, tips
    /// or sends a free ball (the player must read which), aims into the gaps, and now and then hits the net or out.</item>
    /// <item><b>The rally speeds up</b> (Tenis!'s pace): each time the ball crosses the net it flies
    /// <see cref="VolleyLevel.PaceStep"/> quicker (velocity x pace, gravity x pace squared: the same arcs).</item>
    /// <item><b>Two players</b> (<see cref="Mode"/>): each player is a slot with their own moves and camera clock. The TV
    /// box's pose AI shares its runs between two people (each seen ~15 times a second instead of 30, a little later), so
    /// with two players the ball flies <see cref="TwoPlayerPace"/> slower, the late windows are
    /// <see cref="TwoPlayerLate"/> longer and the rules wait longer for the camera (2026-11-07, user: "on tennis the
    /// processing of hits in 2 player mode is slower, take that into account").</item>
    /// </list>
    /// Positions: x across (+ = team 0's right), y up, z toward team 1's side (the net at 0, team 0's baseline at -8).
    /// Scoring: <see cref="VolleyScore"/>. Practice (<see cref="Practice"/>): no score, the computer returns everything and
    /// times the player's balls to <see cref="PracticeContact"/> (tests).
    /// </summary>
    public sealed class VolleyMatch
    {
        // ------------------------------------------------------------------------------------------------ the court
        public const float HalfWidth = 4f, HalfLength = 8f;
        /// <summary>The net's top (m): a children's net, as high as the chibis are tall (they're toys with big heads and
        /// short arms: their mittens reach a ball ~2 m up at the top of a jump).</summary>
        public const float NetHeight = 1.5f;
        public const float BallRadius = 0.13f;
        /// <summary>Where the ball is met (its middle, m), for a chibi (1.52 m, its head 0.66 m of it, its arms 0.34 m): the
        /// bump on the forearms in front of the belly, the set over the head (the arms up beside it), the spike at the top
        /// of a big cartoon jump (~1.1 m), the serve at the top of a little one; below <see cref="LowestTouch"/> it can't
        /// be played any more.</summary>
        public const float BumpHeight = 0.55f, SetHeight = 1.65f, SpikeHeight = 2.3f, ServeHeight = 1.6f, LowestTouch = 0.3f;
        /// <summary>A block meets a smash this high, just over the net (its mittens at the top of a little jump).</summary>
        public const float BlockHeight = 2.05f;
        /// <summary>The toss leaves the hand here and tops out at <see cref="ServeHeight"/> + 0.15.</summary>
        public const float TossFrom = 0.85f;
        public const float Gravity = 9.81f;
        /// <summary>Sets and tosses are floaty (a volleyball is light, and the hit at their top gets time: a set rises for
        /// ~0.6 s, and a hit 0.25 s late finds the ball 0.12 m lower).</summary>
        public const float FloatyGravity = 0.4f;
        /// <summary>The pass to the setter tops out this high (m).</summary>
        public const float PassApex = 3.6f;
        /// <summary>A toy that can't get to the ball in time dives for it: this far (m) at the end of its run.</summary>
        public const float DiveReach = 1.2f;
        /// <summary>Where the toys wait (metres from the net, and across their own half).</summary>
        public const float HomeDepth = 5f, HomeAcross = 1.8f, SetDepth = 1.1f, AttackDepth = 0.8f, AttackAcross = 1.6f, ServeDepth = 8.8f;

        public static float Side(int team) => team == 0 ? -1f : 1f;
        /// <summary>+1 when the team's own right is +x (team 0, facing +z), -1 for team 1.</summary>
        public static float RightSign(int team) => team == 0 ? 1f : -1f;
        /// <summary>A spot in a team's frame (metres from the net, across its own half: + = its right) on the court.</summary>
        public static Vector2 Spot(int team, float across, float depth) => new Vector2(RightSign(team) * across, Side(team) * depth);
        public static int TeamAt(float z) => z < 0f ? 0 : 1;
        public static bool InCourt(Vector3 p, int team) =>
            Mathf.Abs(p.x) <= HalfWidth + BallRadius && TeamAt(p.z) == team && Mathf.Abs(p.z) <= HalfLength + BallRadius;

        // ------------------------------------------------------------------------------------------------ the moves' windows
        /// <summary>A hit counts from this long (s) before the ball arrives (an early one waits for it) to
        /// <see cref="HitLate"/> after it; poses from the previous touch to <see cref="PoseLate"/> after. Scaled by
        /// <see cref="VolleyLevel.WindowScale"/>.</summary>
        public float HitEarly = 0.65f, HitLate = 0.25f, PoseLate = 0.15f;
        /// <summary>A move from the camera reaches the game at most this late (s): a touch the player could still make
        /// waits for the camera this long.</summary>
        public float HitLag = 0.35f;
        /// <summary>Two players: the late windows this much longer and the wait for the camera too (each is seen half as
        /// often, a little later), the ball <see cref="TwoPlayerPace"/> as fast.</summary>
        public float TwoPlayerLate = 0.1f, TwoPlayerPace = 0.88f;
        /// <summary>A hit within this of the ball's arrival (s), strong (<see cref="PerfectPower"/>) is a perfect spike.</summary>
        public float PerfectWindow = 0.22f, PerfectPower = 0.55f;
        /// <summary>Hand speeds (torso lengths a second) from a gentle hit to a full-power one.</summary>
        public float SlowHit = 1.6f, FastHit = 9f;
        /// <summary>The player's spikes fly this fast across (m/s at pace 1) from a gentle to a full-power one.</summary>
        public float SpikeSlow = 9f, SpikeFast = 19f;

        // ------------------------------------------------------------------------------------------------ the match
        public readonly VolleyLevel Level;
        public readonly VolleyScore Score;
        public readonly VolleyMode Mode;
        /// <summary>The computer's first this many returns of every rally are sure (it digs everything and attacks gently
        /// within reach); after them the difficulty decides.</summary>
        public int SureReturns = 2;
        public bool SureRally => !Practice && !IsHuman(1) && CpuReturnsThisRally < SureReturns;
        public int CpuReturnsThisRally { get; private set; }
        /// <summary>Team 0's two (index 0 = its left), then team 1's.</summary>
        public readonly VolleyPlayer[] Players = new VolleyPlayer[4];
        public bool IsHuman(int team) => Players[team * 2].Controller >= 0;
        public int Humans => Mode == VolleyMode.Solo ? 1 : 2;
        public int TeamOf(int slot) => Mode == VolleyMode.Versus ? slot : 0;
        public readonly Contact Next = new Contact();
        public bool Practice;
        /// <summary>Practice: given the touch's number (0 the serve's toss, 1 the receive, 2, 3) and the earliest moment the
        /// player's toy could meet the ball, when it should (tests time the balls to a scripted player's moves).</summary>
        public Func<int, double, double> PracticeContact;
        public event Action<VolleyEvent> Events;

        public double Time { get; private set; }
        public VolleyPhase Phase { get; private set; }
        public Flight Ball;
        /// <summary>The ball is in play (false: in the server's hand).</summary>
        public bool BallLive { get; private set; }
        /// <summary>The ball came down and lies in the sand (a point is about to end, or a late save may still come).</summary>
        public bool BallResting { get; private set; }
        /// <summary>The last touch (for the trail and the sounds).</summary>
        public Touch LastTouch { get; private set; }
        public float LastPower { get; private set; }
        public int LastTeam { get; private set; } = -1;
        /// <summary>Times the ball crossed the net in this rally (the serve too).</summary>
        public int Crossings { get; private set; }
        public int LongestRally { get; private set; }
        public int PointWinner { get; private set; } = -1;
        public PointReason LastReason { get; private set; }
        public int PointsWon { get; private set; }
        public int PointsLost { get; private set; }
        /// <summary>The team with the ball and how many touches it has had.</summary>
        public int Possession { get; private set; } = -1;
        public int TeamTouches { get; private set; }
        public VolleyPlayer Server => Players[Score.Server * 2 + _serverIndex[Score.Server]];
        /// <summary>Tosses of the player's in a row without a serve (for a hint).</summary>
        public int TossesMissed { get; private set; }

        /// <summary>One player's numbers for the results.</summary>
        public sealed class PlayerStats
        {
            public int Bumps, Sets, Spikes, Pushes, Perfect, Serves, Aces, Missed, Wrong, OutOfReach;
            /// <summary>The defence: smashes blocked, tips that went over a block, smashes received low (too hard), smashes
            /// with neither (no block), tips received.</summary>
            public int Blocks, Beaten, TooHard, Unblocked, TipsDug;
            public int Touches => Bumps + Sets + Spikes + Pushes + Serves + Blocks;
        }
        public readonly PlayerStats[] Stats = { new PlayerStats(), new PlayerStats() };

        /// <summary>The rally's pace after the ball crossed the net <paramref name="crossings"/> times (the serve is 0).</summary>
        public float PaceFor(int crossings) =>
            Mathf.Min(Level.TopPace, Level.ServePace + Level.PaceStep * Mathf.Max(0, crossings)) * (Mode == VolleyMode.Solo ? 1f : TwoPlayerPace);
        public float Pace => PaceFor(Crossings);

        readonly System.Random _rng;
        readonly int[] _serverIndex = new int[2];
        readonly double[] _sawUntil = { double.NegativeInfinity, double.NegativeInfinity };
        readonly List<Seen>[] _seen = { new List<Seen>(), new List<Seen>() };
        double _tossAt, _tossStart, _cpuServeAt, _nextAt, _restSince;
        bool _cpuServing, _sureBall;
        int _tosses;
        Vector3 _restAt;
        // the ball heading for the sand: whose point it is when it gets there (the hitting team's unless it lands out)
        int _hitter = -1;
        bool _goesOut;
        float _lastZ;

        /// <summary>A pose let go for this long (s) and taken again is a new move.</summary>
        public float PoseGap = 0.25f;
        // per player: the pose (and its last sample) the last touch was made with
        readonly (Move move, double time)[] _used = { (Move.None, 0), (Move.None, 0) };
        bool _late;

        // ------------------------------------------------------------------------------------------------ the defence
        /// <summary>The defending player's choice is what they show this long (s) before the other side's hit (two players:
        /// <see cref="TwoPlayerLate"/> more): by the hit the camera has told it (its lag, and a pose counts after three
        /// samples), so the block is decided before the ball reaches it. As the hitter swings, the hands are up or low.</summary>
        public float BlockLead = 0.15f;
        /// <summary>A pose counts as held at the moment of the choice if it was seen within this long (s) before it.</summary>
        public float ChoiceHold = 0.3f;
        /// <summary>The computer's hitter waits at most this long (s) at the top of its jump for the camera's news of the
        /// defending player's choice (the set's top hardly moves).</summary>
        public float DefenceWait = 0.2f;
        /// <summary>Versus: a player's hit at the third touch this gentle (its power, 0..1) is a tip, a stronger one a
        /// smash.</summary>
        public float TipPower = 0.3f;
        /// <summary>The jumping attack on its way at a player's team (from the set to the hit), or inactive.</summary>
        public readonly Threat Coming = new Threat();
        /// <summary>Practice: the computer's <paramref name="n"/>-th attack (0, 1, ...): what it is (tests: a smash to block
        /// and a free ball to receive by turns); null = free balls.</summary>
        public Func<int, Attack> PracticeAttack;
        int _cpuAttacks;
        /// <summary>How often the defending player has blocked lately (0..1): a strong computer tips more against a player
        /// who blocks a lot, smashes more against one who doesn't (<see cref="VolleyLevel.Adapt"/>).</summary>
        public float BlockRate { get; private set; } = 0.5f;
        // a smash on its way into a block: when it gets there and who blocks; a tip over a block; the next ball over the net
        // (what PlanReceive marks its receive with)
        double _blockAt = -1;
        VolleyPlayer _blocker;
        double _blockChoiceTime;
        bool _overBlock;
        Attack _incoming;
        Defence _incomingChoice;

        struct Seen
        {
            public Move Move;
            public double Time;
            public float Speed;
            public Vector2 Direction;
            public bool RightHand;
        }

        public VolleyMatch(VolleyLevel level, int pointsToWin = 7, int seed = 0, int firstServer = 0, VolleyMode mode = VolleyMode.Solo)
        {
            Level = level;
            Mode = mode;
            Score = new VolleyScore(pointsToWin, firstServer);
            _rng = new System.Random(seed);
            // the team serving first starts with its first toy (player 1's when two play together); the other team's
            // first serve flips its index to 0 too
            _serverIndex[firstServer] = 0;
            _serverIndex[1 - firstServer] = 1;
            for (int i = 0; i < 4; i++) Players[i] = new VolleyPlayer { Index = i, Team = i / 2 };
            int[] controllers = mode switch
            {
                VolleyMode.Together => new[] { 0, 1, -1, -1 },
                VolleyMode.Versus => new[] { 0, 0, 1, 1 },
                _ => new[] { 0, 0, -1, -1 },
            };
            for (int i = 0; i < 4; i++)
            {
                var p = Players[i];
                p.Controller = controllers[i];
                bool human = p.Controller >= 0;
                p.MaxSpeed = human ? level.HumanRunSpeed : 7.5f;
                p.Reaction = human ? level.HumanReaction : 0.12f;
            }
            if (mode != VolleyMode.Solo) HitLag += TwoPlayerLate + 0.02f;
        }

        // ------------------------------------------------------------------------------------------------ the players' moves

        /// <summary>
        /// Player <paramref name="slot"/> made <paramref name="move"/>, seen by the camera at match time
        /// <paramref name="time"/> (earlier than now by the camera's lag): a pose every sample it is held, a hit once with
        /// its hand speed (torso lengths a second) and direction (screen: x right, y up).
        /// </summary>
        public void See(int slot, Move move, double time, float speed = 0f, Vector2 direction = default, bool rightHand = true)
        {
            if (move == Move.None || slot < 0 || slot > 1) return;
            var list = _seen[slot];
            list.Add(new Seen { Move = move, Time = time, Speed = speed, Direction = direction, RightHand = rightHand });
            // a few seconds are plenty (moves count from the previous touch)
            while (list.Count > 0 && Time - list[0].Time > 6.0) list.RemoveAt(0);
        }

        /// <summary>The camera has told everything about player <paramref name="slot"/> up to this match time (every move
        /// that started before it has been reported; playing with keys: now).</summary>
        public void SawUntil(int slot, double time)
        {
            if (slot < 0 || slot > 1) return;
            _sawUntil[slot] = Math.Max(_sawUntil[slot], Math.Min(time, Time));
        }

        double SeenUntil(int slot) => _sawUntil[Mathf.Clamp(slot, 0, 1)];

        /// <summary>
        /// What player <paramref name="slot"/> showed at match time <paramref name="at"/>, as a defence: the pose held then
        /// (its last sample at most <see cref="ChoiceHold"/> before it): both hands up = <see cref="Defence.Block"/>, hands
        /// together low = <see cref="Defence.Receive"/>. A pose a touch of theirs was just made with counts once let go and
        /// taken again (<see cref="PoseGap"/>).
        /// </summary>
        public Defence ChoiceAt(int slot, double at) => ChoiceAt(slot, at, ChoiceHold);

        Defence ChoiceAt(int slot, double at, double hold)
        {
            if (slot < 0 || slot > 1) return Defence.None;
            var list = _seen[slot];
            double fresh = Fresh(slot);
            var (used, _) = _used[slot];
            var last = new Seen { Move = Move.None, Time = double.NegativeInfinity };
            foreach (var s in list)
            {
                if (s.Move is not (Move.Set or Move.Bump) || s.Time > at || s.Time < at - hold) continue;
                if (s.Move == used && s.Time < fresh) continue;
                if (s.Time >= last.Time) last = s;
            }
            return last.Move == Move.Set ? Defence.Block : last.Move == Move.Bump ? Defence.Receive : Defence.None;
        }

        /// <summary>The defence player <paramref name="slot"/> shows now, as far as the camera has told (the HUD lights it,
        /// the blocker goes to the net for it): the pose of their latest samples (the camera's news is a little late).</summary>
        public Defence ShowsNow(int slot) => ChoiceAt(slot, Time, ChoiceHold + HitLag * 0.5);

        /// <summary>From when the pose player <paramref name="slot"/>'s last touch was made with counts again (it was let go
        /// for <see cref="PoseGap"/> and taken again), or -infinity.</summary>
        double Fresh(int slot)
        {
            var (used, usedAt) = _used[slot];
            if (used == Move.None) return double.NegativeInfinity;
            double last = usedAt;
            foreach (var s in _seen[slot])
            {
                if (s.Move != used || s.Time <= usedAt) continue;
                if (s.Time - last > PoseGap) return s.Time;
                last = s.Time;
            }
            return double.PositiveInfinity;
        }

        float Scale => Level.WindowScale;
        double Late(bool hit) => (hit ? HitLate : PoseLate) * Scale + (Mode == VolleyMode.Solo ? 0f : TwoPlayerLate);

        /// <summary>Which move a touch wants most (a higher rank wins when a player made several in its window): the bump
        /// at the receive (the defend move), the set at the second touch, the hit at the third and at the serve.</summary>
        public static int Priority(int number, Move move) => number switch
        {
            1 => move switch { Move.Bump => 3, Move.Set => 2, Move.Hit => 1, _ => 0 },
            2 => move switch { Move.Set => 3, Move.Hit => 2, Move.Bump => 1, _ => 0 },
            3 => move switch { Move.Hit => 3, Move.Set => 2, Move.Bump => 1, _ => 0 },
            _ => move == Move.Hit ? 3 : 0,
        };

        /// <summary>The move that may be made at once when the ball arrives: the touch's own (the bump at the receive, a hit
        /// at the third, any at the second). Anything else waits for the camera (the right one may still be on its way).</summary>
        static bool Ready(int number, Move move) => number switch
        {
            1 => move == Move.Bump,
            2 => move != Move.None,
            _ => move == Move.Hit,
        };

        /// <summary>The best move player <paramref name="slot"/> made for contact <paramref name="c"/> (in its windows,
        /// before the ball got too low), or none.</summary>
        Seen Best(int slot, Contact c)
        {
            var best = new Seen { Move = Move.None };
            int rank = 0;
            double poseTo = Math.Min(c.Time + Late(false), c.LowAt), hitTo = Math.Min(c.Time + Late(true), c.LowAt);
            double hitFrom = Math.Max(c.Open, c.Time - HitEarly * Scale);
            int k = Mathf.Clamp(slot, 0, 1);
            var list = _seen[k];
            // the pose the player's last touch was made with counts again only once let go (a gap) and taken again
            var (used, _) = _used[k];
            double fresh = Fresh(k);
            foreach (var s in list)
            {
                bool hit = s.Move == Move.Hit;
                if (hit ? s.Time < hitFrom || s.Time > hitTo : s.Time < c.Open || s.Time > poseTo) continue;
                if (s.Move == used && s.Time < fresh) continue;
                int r = Priority(c.Number, s.Move);
                // the strongest of several hits; the latest sample of a held pose
                if (r > rank || (r == rank && r > 0 && (hit ? s.Speed > best.Speed : s.Time > best.Time)))
                {
                    rank = r;
                    best = s;
                }
            }
            return best;
        }

        // ------------------------------------------------------------------------------------------------ stepping

        /// <summary>Starts the match (the first serve).</summary>
        public void Begin() => StartPoint();

        /// <summary>Ends the match now, won by <paramref name="team"/> (tests: a tournament played through quickly).</summary>
        public void Award(int team)
        {
            while (!Score.MatchOver) Score.PointTo(team);
            Phase = VolleyPhase.Over;
            BallLive = false;
            Next.Active = false;
        }

        public Vector3 BallPosition
        {
            get
            {
                if (!BallLive) return HandPosition(Server);
                if (BallResting) return _restAt;
                var p = Ball.At(Time);
                p.y = Mathf.Max(BallRadius, p.y);
                return p;
            }
        }

        public Vector3 BallVelocity => BallLive && !BallResting ? Ball.VelocityAt(Time) : Vector3.zero;

        public void Step(float dt)
        {
            if (dt <= 0f) return;
            Time += dt;
            MovePlayers(dt);
            switch (Phase)
            {
                case VolleyPhase.Serve:
                    if (Time >= _tossAt) Toss();
                    break;
                case VolleyPhase.Toss:
                    StepToss();
                    break;
                case VolleyPhase.Rally:
                    StepRally();
                    break;
                case VolleyPhase.PointOver:
                    if (Time >= _nextAt)
                    {
                        if (!Practice && Score.MatchOver) Phase = VolleyPhase.Over;
                        else StartPoint();
                    }
                    break;
            }
        }

        // ------------------------------------------------------------------------------------------------ points and serves

        void StartPoint()
        {
            Phase = VolleyPhase.Serve;
            BallLive = BallResting = false;
            Next.Active = false;
            LastTouch = Touch.None;
            LastTeam = -1;
            Crossings = 0;
            CpuReturnsThisRally = 0;
            PointWinner = -1;
            Possession = -1;
            TeamTouches = 0;
            _hitter = -1;
            _goesOut = _sureBall = false;
            _tosses = 0;
            Coming.Active = false;
            _blockAt = -1;
            _blocker = null;
            _overBlock = false;
            _incoming = Attack.None;
            _incomingChoice = Defence.None;
            int s = Practice ? 0 : Score.Server;
            foreach (var p in Players)
            {
                p.Mood = 0;
                p.DiveAt = -1;
                p.BlockAt = -1;
                p.Attack = Attack.None;
                p.Serving = false;
                p.Expect = Move.None;
                p.Velocity = Vector2.zero;
                p.Hurry = 0f;
            }
            for (int team = 0; team < 2; team++)
            {
                for (int k = 0; k < 2; k++)
                {
                    var p = Players[team * 2 + k];
                    float across = k == 0 ? -HomeAcross : HomeAcross;
                    if (team == s)
                    {
                        // the server behind the baseline on its own half, the partner a little forward on the other
                        bool serving = k == _serverIndex[team];
                        Place(p, serving ? Spot(team, k == 0 ? -1.2f : 1.2f, ServeDepth) : Spot(team, across * 0.9f, HomeDepth - 1f));
                    }
                    else Place(p, Spot(team, across, HomeDepth));
                }
            }
            Server.Serving = true;
            double earliest = Time + (Server.Controller >= 0 ? 0.9 : 1.3);
            if (Practice && PracticeContact != null) earliest = Math.Max(Time + 0.5, PracticeContact(0, earliest));
            _tossAt = earliest;
        }

        static void Place(VolleyPlayer p, Vector2 at)
        {
            p.Position = p.Target = at;
            p.RunAt = 0;
        }

        /// <summary>Where the ball is held before a toss: in front of the server, at its belly.</summary>
        Vector3 HandPosition(VolleyPlayer p) => new Vector3(p.Position.x, TossFrom, p.Position.y - Side(p.Team) * 0.3f);

        void Toss()
        {
            Phase = VolleyPhase.Toss;
            var server = Server;
            float g = Gravity * FloatyGravity;
            float rise = ServeHeight + 0.15f - TossFrom;
            Ball = new Flight { From = HandPosition(server), Velocity = Vector3.up * Mathf.Sqrt(2f * g * rise), Start = Time, Gravity = g };
            BallLive = true;
            BallResting = false;
            _tossStart = Time;
            _cpuServing = server.Controller < 0;
            double top = Time + Mathf.Sqrt(2f * rise / g);
            // the serve's touch: a hit while the ball is up (the computer's at its top)
            Next.Active = true;
            Next.Team = server.Team;
            Next.Player = server;
            Next.Number = 0;
            Next.Time = top;
            Next.Point = Ball.At(top);
            Next.Open = Time;
            Next.LowAt = double.PositiveInfinity;
            Next.Waiting = Next.Missed = false;
            Next.Reachable = true;
            server.Expect = Move.Hit;
            server.ExpectTime = top;
            server.ExpectPoint = Next.Point;
            _cpuServeAt = top - 0.02;
            Raise(new VolleyEvent { Kind = VolleyEventKind.Toss, Team = server.Team, Player = server.Index, Position = Ball.From });
        }

        void StepToss()
        {
            var server = Server;
            if (_cpuServing)
            {
                if (Time >= _cpuServeAt) Serve(server, Mathf.Lerp(0.35f, 0.8f, (float)_rng.NextDouble()), false);
                return;
            }
            // a hit while the ball is up serves (as hard as the hand went)
            double tossTop = Next.Time;
            var hit = BestServeHit(server.Controller);
            if (hit.Move == Move.Hit)
            {
                Serve(server, Power(hit.Speed), hit.RightHand);
                return;
            }
            // back in the hand: tossed again (the toy serves softly by itself after a few: the game never stalls)
            if (Ball.At(Time).y <= TossFrom && Time > tossTop)
            {
                bool caughtUp = SeenUntil(server.Controller) >= Time - 0.05 || Time - tossTop > HitLag + 0.6;
                if (!caughtUp) return;
                TossesMissed++;
                _tosses++;
                if (_tosses >= 3 && !Practice)
                {
                    Serve(server, 0.15f, true);
                    return;
                }
                BallLive = false;
                Next.Active = false;
                Phase = VolleyPhase.Serve;
                _tossAt = Time + 0.8;
                if (Practice && PracticeContact != null) _tossAt = Math.Max(Time + 0.5, PracticeContact(0, _tossAt));
                Raise(new VolleyEvent { Kind = VolleyEventKind.Caught, Team = server.Team, Player = server.Index, Position = BallPosition });
            }
        }

        /// <summary>A hit of the server's player while the toss is up (from a little after it left the hand).</summary>
        Seen BestServeHit(int slot)
        {
            var best = new Seen { Move = Move.None };
            foreach (var s in _seen[Mathf.Clamp(slot, 0, 1)])
                if (s.Move == Move.Hit && s.Time >= _tossStart + 0.15 && s.Time <= Time && (best.Move == Move.None || s.Speed > best.Speed)) best = s;
            return best;
        }

        float Power(float handSpeed) => Mathf.InverseLerp(SlowHit, FastHit, handSpeed);

        void Serve(VolleyPlayer server, float power, bool right)
        {
            int team = server.Team, other = 1 - team;
            server.Serving = false;
            server.Expect = Move.None;
            TossesMissed = server.Controller >= 0 ? 0 : TossesMissed;
            var from = Ball.At(Time);
            from.y = Mathf.Max(from.y, TossFrom + 0.2f);
            bool cpu = server.Controller < 0;
            float pace = PaceFor(0);
            // where it lands: the player's serves anywhere in, the computer's within reach while its rally is sure
            Vector3 to;
            if (cpu && (SureRally || Practice)) to = Near(other, NearestHuman(other, Spot(other, 0f, 5f)).Position, 1.2f, 3.5f, 6.8f);
            else to = Land(other, Rand(-3.2f, 3.2f), Rand(3.5f, 7.2f));
            float speed = (cpu ? Level.ServeSpeed * Rand(0.9f, 1.08f) : Mathf.Lerp(8f, 14f, power)) * pace;
            var f = Over(from, to, speed, Gravity * pace * pace, 0.45f);
            if (Practice && PracticeContact != null && cpu) f = PracticeBall(from, other);
            server.Touches++;
            server.TouchKind = Touch.Serve;
            server.TouchTime = Time;
            server.TouchPoint = from;
            server.TouchPower = power;
            server.TouchRight = right;
            if (server.Controller >= 0) Stats[server.Controller].Serves++;
            Phase = VolleyPhase.Rally;
            _serveTeam = team;
            _incoming = Attack.Free;
            Launch(server, f, Touch.Serve, power, 0, sure: cpu && (SureRally || Practice));
        }

        int _serveTeam = -1;

        void EndPoint(int winner, PointReason reason)
        {
            if (Phase is VolleyPhase.PointOver or VolleyPhase.Over) return;
            Phase = VolleyPhase.PointOver;
            Next.Active = false;
            Coming.Active = false;
            _blockAt = -1;
            PointWinner = winner;
            LastReason = reason;
            LongestRally = Math.Max(LongestRally, Crossings);
            bool over = false;
            if (!Practice)
            {
                int before = Score.Server;
                over = Score.PointTo(winner);
                // a team winning the serve back sends its other toy to serve (beach volleyball's order)
                if (Score.Server != before) _serverIndex[winner] = 1 - _serverIndex[winner];
                if (winner == 0) PointsWon++;
                else PointsLost++;
                // an ace: the player's serve came down untouched
                if (winner == _serveTeam && Crossings == 1 && reason == PointReason.Landed && IsHuman(winner) && LastTouch == Touch.Serve)
                    Stats[Server.Controller >= 0 ? Server.Controller : 0].Aces++;
            }
            foreach (var p in Players)
            {
                p.Mood = p.Team == winner ? 1 : -1;
                p.Target = p.Position;
                p.Expect = Move.None;
                p.Serving = false;
            }
            _nextAt = Time + (over ? 3.2 : 2.3);
            Raise(new VolleyEvent { Kind = VolleyEventKind.Point, Team = winner, Player = -1, Position = BallPosition, Reason = reason });
        }

        // ------------------------------------------------------------------------------------------------ the rally

        void StepRally()
        {
            // a smash reaching the block: back down into the hitter's court
            if (_blockAt >= 0 && Time >= _blockAt)
            {
                DoBlock();
                return;
            }
            TrackDefence();
            var p = BallPosition;
            // the net: a ball crossing it below the tape (between the posts) drops back on the hitter's side
            if (!BallResting && Mathf.Sign(p.z) != Mathf.Sign(_lastZ) && _lastZ != 0f && Mathf.Abs(Ball.Velocity.z) > 1e-4f)
            {
                float s = -Ball.From.z / Ball.Velocity.z;
                var at = Ball.At(Ball.Start + s);
                if (s > 0f && Mathf.Abs(at.x) < HalfWidth + 0.9f && at.y < NetHeight + BallRadius * 0.6f)
                {
                    OnNet(at);
                    return;
                }
            }
            if (!BallResting && p.z != 0f) _lastZ = p.z;
            if (Next.Active && Time >= Next.Time) TryTouch();
            if (Phase != VolleyPhase.Rally) return;
            if (!BallResting && Ball.At(Time).y <= BallRadius) Rest();
            if (BallResting && !(Next.Active && Next.Waiting) && Phase == VolleyPhase.Rally) Landed();
        }

        /// <summary>The ball came down in the sand: it lies there (while a late move may still save it).</summary>
        void Rest()
        {
            var at = Ball.At(Time);
            at.y = BallRadius;
            _restAt = at;
            _restSince = Time;
            BallResting = true;
            bool inside = InCourt(at, TeamAt(at.z));
            Raise(new VolleyEvent { Kind = VolleyEventKind.Land, Team = TeamAt(at.z), Player = -1, Position = at, In = inside });
        }

        /// <summary>The ball lies in the sand: a team that let its own ball come down on its side loses the point (in or
        /// out: a set nobody hit, a shank); a ball that came over the net is the receiver's loss if it came down in their
        /// court, the hitter's if out.</summary>
        void Landed()
        {
            var at = _restAt;
            int side = TeamAt(at.z), last = LastTeam;
            bool inside = InCourt(at, side);
            if (last < 0) last = 1 - side;
            if (side == last) EndPoint(1 - last, LastTouch == Touch.Shank ? (_tooHard ? PointReason.TooHard : PointReason.Wrong) : PointReason.Landed);
            else if (inside && !_goesOut)
                EndPoint(last, LastTouch == Touch.Block ? PointReason.Block : _overBlock ? PointReason.OverBlock : PointReason.Landed);
            else EndPoint(1 - last, PointReason.Out);
        }

        bool _tooHard;

        void OnNet(Vector3 at)
        {
            int hitter = _hitter >= 0 ? _hitter : LastTeam;
            Raise(new VolleyEvent { Kind = VolleyEventKind.Net, Team = hitter, Player = -1, Position = new Vector3(at.x, Mathf.Min(at.y, NetHeight - 0.2f), 0f) });
            // it drops on the hitter's side
            Ball = Flight.Between(new Vector3(at.x, at.y, Side(hitter) * 0.15f), new Vector3(at.x, BallRadius, Side(hitter) * 0.7f), 0.45f, Gravity, Time);
            Next.Active = false;
            _goesOut = false;
            EndPoint(1 - hitter, PointReason.Net);
        }

        /// <summary>The ball's next touch is due (or past due, waiting for the camera): the computer's is made now; a
        /// player's with the best move they made for it, at once if it's the touch's own move, else once the camera has
        /// caught up (from wherever the ball is by then).</summary>
        void TryTouch()
        {
            var c = Next;
            var who = c.Player;
            if (who.Controller < 0)
            {
                // the computer's hitter hangs at the top of its jump until the camera has told what the defending player
                // showed at the moment of the choice (a moment at most: the set's top hardly moves)
                if (c.Number == 3 && Coming.Active && Coming.Defender >= 0 && SeenUntil(Coming.Defender) < Coming.DecideAt &&
                    Time < c.Time + DefenceWait)
                    return;
                CpuTouch(c);
                return;
            }
            if (!c.Reachable)
            {
                // the toy couldn't get there: it dives, the ball comes down
                if (!c.Missed)
                {
                    c.Missed = true;
                    who.DiveAt = c.Time;
                    who.Expect = Move.None;
                    Stats[who.Controller].OutOfReach++;
                    Raise(new VolleyEvent { Kind = VolleyEventKind.OutOfReach, Team = c.Team, Player = who.Index, Position = c.Point, Number = c.Number });
                }
                Next.Active = false;
                return;
            }
            int slot = who.Controller;
            // too low to play from here on (the ball keeps falling while the camera catches up)
            var ball = Ball.At(Time);
            if (double.IsPositiveInfinity(c.LowAt) && ball.y <= LowestTouch) c.LowAt = Time;
            var best = Best(slot, c);
            if (best.Move != Move.None && Ready(c.Number, best.Move))
            {
                HumanTouch(c, best);
                return;
            }
            c.Waiting = true;
            // the receive waits only as long as a late bump could still come (the ball falls fast there); the second and
            // third touches as long as a late hit (the third's ball is at the top of the set: it hardly moves)
            double late = c.Number == 1 ? Late(false) : Late(true);
            double until = Math.Min(c.Time + late, c.LowAt);
            bool caughtUp = SeenUntil(slot) >= until;
            bool timeout = Time >= c.Time + late + HitLag;
            if (!caughtUp && !timeout) return;
            best = Best(slot, c);
            c.Waiting = false;
            if (best.Move != Move.None)
            {
                HumanTouch(c, best);
                return;
            }
            // no move for it: it comes down (a smash nobody blocked: the toy dives at it)
            c.Missed = true;
            Next.Active = false;
            who.Expect = Move.None;
            if (c.Attack == Attack.Smash)
            {
                who.DiveAt = Time;
                Stats[slot].Unblocked++;
            }
            else Stats[slot].Missed++;
            Raise(new VolleyEvent
            {
                Kind = VolleyEventKind.Missed, Team = c.Team, Player = who.Index, Position = ball, Number = c.Number, Attack = c.Attack, Choice = c.Chose,
            });
        }

        /// <summary>A player's toy plays the ball with <paramref name="move"/>: from where the ball is now (lower than its
        /// point if the camera's news came late).</summary>
        void HumanTouch(Contact c, Seen move)
        {
            var who = c.Player;
            int slot = who.Controller, team = c.Team;
            var from = BallResting ? _restAt + Vector3.up * 0.1f : Ball.At(Time);
            from.y = Mathf.Max(from.y, LowestTouch * 0.8f);
            bool late = Time - c.Time > 0.06;
            float timing = (float)(move.Time - c.Time);
            BallResting = false;
            _late = late;
            // a pose used for this touch: held on, it isn't the next touch's move (that one needs it let go and taken again)
            if (move.Move != Move.Hit) _used[slot] = (move.Move, move.Time);
            Next.Active = false;
            who.Expect = Move.None;
            switch (c.Number)
            {
                case 1:
                    if (c.Attack == Attack.Smash)
                    {
                        // a smash can't be received: it flies off the arms (it had to be blocked)
                        Stats[slot].TooHard++;
                        Shank(who, from, move.Move, true);
                    }
                    else if (move.Move == Move.Bump)
                    {
                        Stats[slot].Bumps++;
                        if (c.Attack == Attack.Tip) Stats[slot].TipsDug++;
                        Pass(who, from, late, Touch.Bump);
                    }
                    else Shank(who, from, move.Move);
                    return;
                case 2:
                    if (move.Move == Move.Hit) Push(who, from, Power(move.Speed), move.RightHand, true);
                    else
                    {
                        if (move.Move == Move.Set) Stats[slot].Sets++;
                        else Stats[slot].Bumps++;
                        SetUp(who, from, move.Move == Move.Set ? Touch.Set : Touch.Bump, late);
                    }
                    return;
                default:
                    if (move.Move == Move.Hit)
                    {
                        // versus: the other player's choice, as this hit came (their camera has told about as much by now)
                        float power = Power(move.Speed);
                        var defence = Defence.None;
                        bool versusAttack = Coming.Active && Coming.Hitter == who && Coming.Defender >= 0;
                        var blocker = versusAttack ? Coming.Blocker : null;
                        if (versusAttack)
                        {
                            Coming.DecideAt = move.Time - 0.05;
                            defence = Decide(Coming.Defender, Coming.DecideAt);
                        }
                        Coming.Active = false;
                        if (versusAttack && power < TipPower) Tip(who, from, move.RightHand, defence, blocker);
                        else Spike(who, from, power, move.Direction, move.RightHand, timing, late, versusAttack ? defence : (Defence?)null, blocker);
                    }
                    else
                    {
                        Coming.Active = false;
                        Push(who, from, 0.25f, true, false, move.Move == Move.Set ? Touch.Set : Touch.Bump);
                    }
                    return;
            }
        }

        /// <summary>The computer's toy plays the ball (a dig can fail: it dives and the ball comes down).</summary>
        void CpuTouch(Contact c)
        {
            var who = c.Player;
            var from = Ball.At(Time);
            Next.Active = false;
            who.Expect = Move.None;
            switch (c.Number)
            {
                case 1:
                    if (!c.CpuPlays)
                    {
                        who.DiveAt = Time;
                        Raise(new VolleyEvent { Kind = VolleyEventKind.Dive, Team = c.Team, Player = who.Index, Position = from, Number = 1 });
                        return;
                    }
                    Pass(who, from, false, Touch.Bump);
                    return;
                case 2:
                    SetUp(who, from, Touch.Set, false);
                    return;
                default:
                    CpuAttack(who, from);
                    return;
            }
        }

        // ------------------------------------------------------------------------------------------------ the touches

        /// <summary>The receive: up to the teammate by the net (a late one from lower down, a little off).</summary>
        void Pass(VolleyPlayer who, Vector3 from, bool late, Touch kind)
        {
            int team = who.Team;
            var setter = Mate(who);
            float across = Mathf.Clamp(Across(team, setter.Position.x) * 0.3f, -0.9f, 0.9f) + (late ? Rand(-0.6f, 0.6f) : Rand(-0.2f, 0.2f));
            var spot = Spot(team, across, SetDepth + (late ? Rand(0f, 0.6f) : 0f));
            var to = new Vector3(spot.x, SetHeight, spot.y);
            float pace = Pace, g = Gravity * pace * pace;
            float apex = Mathf.Max(from.y + 0.8f, (late ? PassApex - 0.6f : PassApex) + Rand(-0.2f, 0.2f));
            float seconds = Flight.Seconds(from.y, apex, to.y, Gravity) / pace;
            if (who.Controller >= 0) seconds = PracticeSeconds(2, seconds);
            var f = Flight.Between(from, to, seconds, g, Time);
            Touched(who, kind, from, 0.3f);
            Launch(who, f, kind, 0.3f, 1);
            PlanMate(setter, 2, f.At(f.Start + seconds), Time + seconds, Move.Set);
        }

        /// <summary>The set: up for the teammate at the net, its top right where the hit meets it (a late hit still finds it
        /// there); a bump-set or a late one is a little off.</summary>
        void SetUp(VolleyPlayer who, Vector3 from, Touch kind, bool late)
        {
            int team = who.Team;
            var hitter = Mate(who);
            // the computer decides its attack now, and its hitter shows it from here on (a free ball is set lower, for a
            // push without a jump)
            var attack = hitter.Controller < 0 ? PlanCpuAttack() : Attack.None;
            hitter.Attack = attack;
            // the hitter attacks on its own half (player 1's toy on the left)
            float side = Mode == VolleyMode.Together && hitter.Controller >= 0 ? (hitter.Controller == 0 ? -1f : 1f)
                : Mathf.Sign(Across(team, hitter.Position.x) == 0f ? (hitter.Index % 2 == 0 ? -1f : 1f) : Across(team, hitter.Position.x));
            float off = kind == Touch.Bump || late ? 0.35f : 0.1f;
            var spot = Spot(team, side * AttackAcross + Rand(-off, off), AttackDepth + Rand(0f, off) + (attack == Attack.Free ? 0.5f : 0f));
            float pace = Pace, g = Gravity * FloatyGravity * pace * pace;
            Vector3 to;
            Flight f;
            float seconds;
            if (hitter.Controller < 0 && attack != Attack.Free)
            {
                // the computer's attacking set goes up high and comes down to its hitter's jump: a second or more to see the
                // attack coming (a set's top at the hit gave 0.4-0.7 s: too little for a child to read it and raise the arms)
                to = new Vector3(spot.x, SpikeHeight, spot.y);
                float g0 = Gravity * FloatyGravity;
                seconds = Flight.Seconds(from.y, Mathf.Max(from.y, SpikeHeight) + Level.SetAbove, SpikeHeight, g0) / pace;
                if (PracticeAttack != null) seconds = PracticeSeconds(4, seconds);
                f = Flight.Between(from, to, seconds, g0 * pace * pace, Time);
            }
            else
            {
                float top = attack == Attack.Free ? SetHeight + 0.2f : SpikeHeight - (kind == Touch.Bump ? 0.08f : 0f);
                float rise = Mathf.Max(0.35f, top - from.y);
                seconds = Mathf.Sqrt(2f * rise / (Gravity * FloatyGravity)) / pace;
                if (who.Controller >= 0) seconds = PracticeSeconds(3, seconds);
                else if (PracticeAttack != null) seconds = PracticeSeconds(4, seconds);
                // the top of the arc at the hitter's spot, at the moment it's hit
                g = 2f * rise / (seconds * seconds);
                to = new Vector3(spot.x, from.y + rise, spot.y);
                var v = new Vector3((to.x - from.x) / seconds, g * seconds, (to.z - from.z) / seconds);
                f = new Flight { From = from, Velocity = v, Start = Time, Gravity = g };
            }
            Touched(who, kind, from, 0.3f);
            Launch(who, f, kind, 0.3f, 2);
            PlanMate(hitter, 3, to, Time + seconds, attack == Attack.Free ? Move.Set : Move.Hit);
            ThreatFrom(hitter, attack, Time + seconds);
        }

        /// <summary>What the computer's next attack will be (at its set): the first of every rally a free ball (a warm-up
        /// exchange), then a free ball now and then, else a smash or a tip (the player's defence: from its second return
        /// on, sure or not); a strong pair tips more against a player who blocks a lot.</summary>
        Attack PlanCpuAttack()
        {
            if (Practice) return PracticeAttack != null ? PracticeAttack(_cpuAttacks) : Attack.Free;
            if (CpuReturnsThisRally == 0) return Attack.Free;
            if (Chance(Level.FreeBall)) return Attack.Free;
            float tip = Level.TipShare;
            if (tip > 0f && Level.Adapt > 0f) tip = Mathf.Clamp(tip + Level.Adapt * (BlockRate - 0.5f) * 2f, 0.05f, 0.8f);
            return Chance(tip) ? Attack.Tip : Attack.Smash;
        }

        /// <summary>
        /// A team set up its hitter for <paramref name="attack"/> at <paramref name="hitTime"/>: if it jumps at a player's
        /// team (a smash or a tip; a player's own attack in versus is known only at the hit), that player defends. Their
        /// toy in front of the hitter blocks (two players together: the one whose half the hitter attacks into), and their
        /// choice is what they show <see cref="BlockLead"/> before the hit. The computer's attack shows by the hitter's look
        /// from the set (Easy also puts a "!" over a smasher), a disguised tip on Hard only <see cref="VolleyLevel.TellBefore"/>
        /// before the hit.
        /// </summary>
        void ThreatFrom(VolleyPlayer hitter, Attack attack, double hitTime)
        {
            Coming.Active = false;
            hitter.TellAt = Time;
            int defending = 1 - hitter.Team;
            if (attack == Attack.Free || !IsHuman(defending)) return;
            var blocker = BlockerFor(defending, hitter.Position.x);
            Coming.Active = true;
            Coming.Team = hitter.Team;
            Coming.Hitter = hitter;
            Coming.Kind = attack;
            Coming.HitTime = hitTime;
            Coming.DecideAt = hitTime - BlockLead - (Mode == VolleyMode.Solo ? 0f : TwoPlayerLate);
            bool disguised = attack == Attack.Tip && Chance(Level.DisguiseShare);
            Coming.TellAt = attack == Attack.None ? hitTime : disguised ? Math.Max(Time, hitTime - Level.TellBefore) : Time;
            Coming.Blocker = blocker;
            Coming.Defender = blocker.Controller;
            hitter.TellAt = Coming.TellAt;
        }

        /// <summary>The defending toy that blocks a hitter at <paramref name="x"/>: two players together, the toy whose half
        /// it faces; else the one nearer across.</summary>
        VolleyPlayer BlockerFor(int team, float x)
        {
            var a = Players[team * 2];
            var b = Players[team * 2 + 1];
            if (Mode == VolleyMode.Together && IsHuman(team)) return Across(team, x) < 0f ? a : b;
            return Mathf.Abs(a.Position.x - x) <= Mathf.Abs(b.Position.x - x) ? a : b;
        }

        /// <summary>The defending player's choice at its moment (what the camera saw then); how often they block lately.</summary>
        Defence Decide(int slot, double at)
        {
            var choice = ChoiceAt(slot, at);
            BlockRate += 0.35f * ((choice == Defence.Block ? 1f : 0f) - BlockRate);
            _blockChoiceTime = at;
            return choice;
        }

        /// <summary>While an attack is set up at a player's team: its blocker goes to the net in front of the hitter, to jump
        /// as it hits, while the player shows the block (both hands up), and back while they don't.</summary>
        void TrackDefence()
        {
            var t = Coming;
            if (!t.Active || t.Defender < 0 || t.Blocker == null) return;
            var b = t.Blocker;
            if (ShowsNow(t.Defender) == Defence.Block)
            {
                var spot = new Vector2(Mathf.Clamp(t.Hitter.Position.x, -HalfWidth + 0.6f, HalfWidth - 0.6f), Side(b.Team) * 0.5f);
                if (b.BlockAt < 0 || (b.Target - spot).sqrMagnitude > 0.04f)
                {
                    b.Target = spot;
                    b.RunAt = Math.Min(b.RunAt, Time);
                    Hurry(b, spot, Math.Max(Time + 0.15, t.HitTime - 0.35));
                }
                b.BlockAt = t.HitTime + 0.02;
            }
            else if (b.BlockAt >= 0)
            {
                b.BlockAt = -1;
                b.Hurry = 0f;
                b.Target = Spot(b.Team, Across(b.Team, b.Position.x) < 0f ? -HomeAcross : HomeAcross, HomeDepth - 1.5f);
            }
        }

        /// <summary>A spike: over the net as fast as the hand went, aimed into the open court (the swing's way leans it to a
        /// side), always clearing the net and landing in.</summary>
        void Spike(VolleyPlayer who, Vector3 from, float power, Vector2 direction, bool right, float timing, bool late,
                   Defence? defence = null, VolleyPlayer blocker = null)
        {
            int team = who.Team, other = 1 - team;
            bool perfect = power >= PerfectPower && Mathf.Abs(timing) <= PerfectWindow && !late;
            if (late) power *= 0.8f;
            float pace = Pace;
            float speed = Mathf.Lerp(SpikeSlow, SpikeFast, power) * (perfect ? 1.1f : 1f) * pace;
            // into the open court: the spot farthest from the defenders, leaned to the side the hand swung
            var to = OpenSpot(other, -direction.x * 1.5f);
            var f = Over(from, to, speed, Gravity * pace * pace, 0.15f);
            Touched(who, Touch.Spike, from, power, right);
            if (who.Controller >= 0)
            {
                Stats[who.Controller].Spikes++;
                if (perfect) Stats[who.Controller].Perfect++;
            }
            if (defence.HasValue)
            {
                // versus: a smash at the other player, who chose as it came
                Attacked(who, from, Attack.Smash, f, power, defence.Value, blocker, perfect, false);
                return;
            }
            _incoming = Attack.Smash;
            Launch(who, f, Touch.Spike, power, 3, perfect);
        }

        /// <summary>Versus: a gentle hit at the third touch is a tip, softly over the net (and over a block) and short.</summary>
        void Tip(VolleyPlayer who, Vector3 from, bool right, Defence defence, VolleyPlayer blocker)
        {
            int other = 1 - who.Team;
            var f = TipFlight(from, other, defence == Defence.Block ? blocker : null, BlockerHalf(other, blocker));
            Touched(who, Touch.Push, from, 0.15f, right);
            if (who.Controller >= 0) Stats[who.Controller].Pushes++;
            Attacked(who, from, Attack.Tip, f, 0.15f, defence, blocker, false, false);
        }

        /// <summary>A tip from <paramref name="from"/>: softly over the net, high enough to clear a block's hands, coming down
        /// short (just behind <paramref name="overBlock"/> when it beats a block; else into the defending
        /// <paramref name="half"/>: -1 its left, +1 its right, 0 anywhere).</summary>
        Flight TipFlight(Vector3 from, int other, VolleyPlayer overBlock, int half)
        {
            float pace = Pace, g = Gravity * pace * pace;
            Vector3 to;
            if (overBlock != null) to = Land(other, Across(other, overBlock.Position.x) + Rand(-0.6f, 0.6f), Rand(1.3f, 2.1f));
            else if (half != 0) to = Land(other, half * Rand(0.6f, 3.2f), Rand(1.2f, 2.4f));
            else to = Land(other, Rand(-3f, 3f), Rand(1.2f, 2.4f));
            float apex = Mathf.Max(from.y, NetHeight) + 0.65f;
            return Flight.Between(from, to, Flight.Seconds(from.y, apex, to.y, Gravity) / pace, g, Time);
        }

        /// <summary>Two players together: the half of the defending team the attack goes into (its blocker's: -1 left, +1
        /// right), else 0 (anywhere).</summary>
        int BlockerHalf(int team, VolleyPlayer blocker) =>
            Mode == VolleyMode.Together && IsHuman(team) && blocker != null ? (blocker.Index % 2 == 0 ? -1 : 1) : 0;

        /// <summary>
        /// A jumping attack hit at a player's team, and what they chose as it came: a smash into a block goes back down into
        /// the hitter's court (<see cref="DoBlock"/>: the blockers' point), a tip over a block drops behind it (their
        /// point); else the ball goes on into the court, its receive marked with the attack and the choice (a smash can't
        /// be received; a tip read right is always reached).
        /// </summary>
        void Attacked(VolleyPlayer hitter, Vector3 from, Attack kind, Flight f, float power, Defence choice, VolleyPlayer blocker, bool perfect, bool sure)
        {
            int other = 1 - hitter.Team;
            int slot = blocker != null ? blocker.Controller : -1;
            bool correct = kind == Attack.Smash ? choice == Defence.Block : choice == Defence.Receive;
            Raise(new VolleyEvent
            {
                Kind = VolleyEventKind.Defence, Team = other, Player = blocker != null ? blocker.Index : -1, Position = from, Attack = kind,
                Choice = choice, Correct = correct, Power = power,
            });
            var touch = kind == Attack.Smash ? Touch.Spike : Touch.Push;
            if (blocker != null && choice == Defence.Block)
            {
                if (kind == Attack.Smash)
                {
                    // to the block: just over the net on the blocker's side, at its hands' height at the top of its jump
                    float across = new Vector2(f.Velocity.x, f.Velocity.z).magnitude;
                    var b = new Vector3(Mathf.Clamp(blocker.Position.x, from.x - 0.7f, from.x + 0.7f), BlockHeight, Side(other) * 0.25f);
                    float t = Mathf.Clamp((b - from).magnitude / Mathf.Max(6f, across), 0.06f, 0.2f);
                    var toBlock = Flight.Between(from, b, t, f.Gravity, Time);
                    blocker.BlockAt = Time + t;
                    _blockAt = Time + t;
                    _blocker = blocker;
                    _incoming = Attack.None;
                    Launch(hitter, toBlock, touch, power, 3, perfect, playable: false);
                    return;
                }
                // a tip over the block: the blocker jumps for nothing, the ball drops behind it, the other toy dives too late
                blocker.BlockAt = blocker.BlockAt >= 0 ? Math.Min(blocker.BlockAt, Time + 0.1) : Time + 0.1;
                if (slot >= 0) Stats[slot].Beaten++;
                var mate = Mate(blocker);
                float down = f.DownThrough(BallRadius);
                var lands = f.At(f.Start + Mathf.Max(0f, down));
                mate.Target = new Vector2(lands.x, lands.z + Side(other) * 0.6f);
                mate.RunAt = Time + 0.1;
                mate.DiveAt = f.Start + Mathf.Max(0.2f, down - 0.08f);
                _incoming = Attack.None;
                Launch(hitter, f, touch, power, 3, playable: false);
                _overBlock = true;
                return;
            }
            _incoming = kind;
            _incomingChoice = choice;
            Launch(hitter, f, touch, power, 3, perfect, sure: sure);
        }

        /// <summary>A smash reached the block: straight back down into the hitter's court, steep and quick (nobody gets to it:
        /// the blockers' point).</summary>
        void DoBlock()
        {
            var blocker = _blocker;
            _blockAt = -1;
            _blocker = null;
            if (blocker == null) return;
            int team = blocker.Team, other = 1 - team;
            var from = Ball.At(Time);
            float pace = Pace;
            var to = Land(other, Across(other, from.x) + Rand(-0.9f, 0.9f), Rand(1.8f, 3.2f));
            var f = Flight.Between(from, to, Rand(0.34f, 0.42f) / Mathf.Sqrt(pace), Gravity * pace * pace, Time);
            Touched(blocker, Touch.Block, from, 0.8f);
            if (blocker.Controller >= 0)
            {
                Stats[blocker.Controller].Blocks++;
                // the block's pose was this touch's: held on, it isn't the next set
                _used[blocker.Controller] = (Move.Set, _blockChoiceTime);
            }
            _incoming = Attack.None;
            Launch(blocker, f, Touch.Block, 0.8f, 1, playable: false);
        }

        /// <summary>A soft ball over the net (a pose on the third touch, a one-hand push on the second): a lob deep into
        /// their court, easy to dig.</summary>
        void Push(VolleyPlayer who, Vector3 from, float power, bool right, bool oneHand, Touch shown = Touch.Push)
        {
            int team = who.Team, other = 1 - team;
            var to = Land(other, Rand(-2.8f, 2.8f), Rand(3.8f, 6.8f));
            float pace = Pace, g = Gravity * pace * pace;
            // slow and high, and clear of the net even from low down near it (a late one)
            var f = Over(from, to, Mathf.Lerp(5f, 8f, power) * pace, g, 0.4f);
            Touched(who, shown == Touch.Push ? Touch.Push : shown, from, power, right);
            if (who.Controller >= 0) Stats[who.Controller].Pushes++;
            _incoming = Attack.Free;
            Launch(who, f, Touch.Push, power * 0.5f, oneHand ? 2 : 3);
        }

        /// <summary>The wrong move at a ball coming over the net (or any move at a smash: <paramref name="tooHard"/>): it
        /// flies off the arms, out.</summary>
        void Shank(VolleyPlayer who, Vector3 from, Move move, bool tooHard = false)
        {
            int team = who.Team;
            float x = Mathf.Sign(from.x == 0f ? 1f : from.x) * Rand(HalfWidth + 1.2f, HalfWidth + 3f);
            var to = new Vector3(x, BallRadius, Side(team) * Rand(4f, 10f));
            float pace = Pace;
            var f = Flight.Between(from, to, Flight.Seconds(from.y, from.y + 1.2f, BallRadius, Gravity) / pace, Gravity * pace * pace, Time);
            Touched(who, Touch.Shank, from, 0.2f);
            LastTeam = team;
            bool late = _late;
            _late = false;
            if (who.Controller >= 0) Stats[who.Controller].Wrong++;
            LastTouch = Touch.Shank;
            Ball = f;
            BallLive = true;
            BallResting = false;
            _hitter = team;
            _goesOut = true;
            _tooHard = tooHard;
            var attack = Next.Attack;
            Next.Active = false;
            Raise(new VolleyEvent
            {
                Kind = VolleyEventKind.Touch, Team = team, Player = who.Index, Position = from, Touch = Touch.Shank, Number = 1, Move = move, Late = late,
                Attack = attack, Choice = Next.Chose,
            });
        }

        /// <summary>The computer's third touch, as decided at its set (<see cref="PlanCpuAttack"/>): a free ball (its sure ones
        /// within reach), a smash or a tip at the player's team (who chose as it came), or now and then into the net or
        /// out.</summary>
        void CpuAttack(VolleyPlayer who, Vector3 from)
        {
            int team = who.Team, other = 1 - team;
            float pace = Pace, g = Gravity * pace * pace;
            bool sure = SureRally || Practice;
            var kind = who.Attack == Attack.None ? Attack.Free : who.Attack;
            who.Attack = Attack.None;
            // the defending player's choice at its moment
            bool threat = Coming.Active && Coming.Hitter == who && Coming.Defender >= 0;
            var blocker = threat ? Coming.Blocker : null;
            var choice = threat ? Decide(Coming.Defender, Coming.DecideAt) : Defence.None;
            Coming.Active = false;
            CpuReturnsThisRally++;
            _cpuAttacks++;
            Flight f;
            float power;
            if (kind == Attack.Free)
            {
                // a soft ball over: high and slow
                if (Practice && PracticeContact != null) f = PracticeBall(from, other);
                else
                {
                    var to = sure || !Chance(Level.AimAway) ? Near(other, NearestHuman(other, Spot(other, 0f, 4.5f)).Position, 1.3f, 3f, 6.8f)
                        : OpenSpot(other, 0f);
                    f = Over(from, to, Rand(6.5f, 8.5f) * pace, g, 0.4f);
                }
                power = 0.2f;
                Touched(who, Touch.Push, from, power);
                _incoming = Attack.Free;
                Launch(who, f, Touch.Push, power, 3, sure: sure);
                return;
            }
            if (!sure && Chance(Level.Error))
            {
                // a mistake: into the net, or out
                power = 0.6f;
                bool outBall = false;
                if (Chance(0.5f))
                {
                    // straight into the net, below the tape
                    var to = new Vector3(Rand(-2.5f, 2.5f), Rand(0.6f, NetHeight - 0.3f), Side(other) * 0.05f);
                    f = Flight.Between(from, to, Mathf.Max(0.12f, (from - to).magnitude / (Level.AttackSpeed * pace)), g, Time);
                }
                else
                {
                    var to = Chance(0.5f) ? new Vector3(Rand(-3f, 3f), BallRadius, Side(other) * Rand(HalfLength + 0.6f, HalfLength + 2f))
                        : new Vector3(Mathf.Sign(Rand(-1f, 1f)) * Rand(HalfWidth + 0.5f, HalfWidth + 1.5f), BallRadius, Side(other) * Rand(3f, 7f));
                    f = Over(from, to, Level.AttackSpeed * pace, g, 0.2f);
                    outBall = true;
                }
                Touched(who, Touch.Spike, from, power);
                _incoming = Attack.Smash;
                Launch(who, f, Touch.Spike, power, 3, goesOut: outBall);
                return;
            }
            int half = BlockerHalf(other, blocker);
            if (kind == Attack.Tip)
            {
                // softly over the net (and over a block), short
                power = 0.15f;
                f = TipFlight(from, other, choice == Defence.Block ? blocker : null, half);
                Touched(who, Touch.Push, from, power);
            }
            else
            {
                // a smash: hard, into a gap or at one of the player's toys (two together: the defending player's half)
                power = Mathf.InverseLerp(9f, 19f, Level.AttackSpeed);
                Vector3 to;
                if (half != 0) to = Land(other, half * Rand(0.8f, 3.3f), Rand(3f, 7f));
                else if (!sure && Chance(Level.AimAway)) to = OpenSpot(other, 0f);
                else to = Near(other, NearestHuman(other, Spot(other, 0f, 4.5f)).Position, 1.6f, 3f, 7.2f);
                f = Over(from, to, Level.AttackSpeed * Rand(0.9f, 1.1f) * pace, g, 0.15f);
                Touched(who, Touch.Spike, from, power);
            }
            if (threat) Attacked(who, from, kind, f, power, choice, blocker, false, sure);
            else
            {
                _incoming = kind;
                _incomingChoice = Defence.None;
                Launch(who, f, kind == Attack.Smash ? Touch.Spike : Touch.Push, power, 3, sure: sure);
            }
        }

        // ------------------------------------------------------------------------------------------------ flights and plans

        void Touched(VolleyPlayer who, Touch kind, Vector3 at, float power, bool right = true)
        {
            who.Touches++;
            who.TouchKind = kind;
            who.TouchTime = Time;
            who.TouchPoint = at;
            who.TouchPower = power;
            who.TouchRight = right;
            who.Hurry = 0f;
        }

        /// <summary>The ball leaves <paramref name="who"/>'s touch (its team's <paramref name="number"/>-th; 0 = the serve):
        /// its flight, the event, and who plays it next (a pass inside the team: the caller plans it; a ball over the net
        /// that isn't <paramref name="playable"/>, into a block or blocked down or over a block, nobody).</summary>
        void Launch(VolleyPlayer who, Flight f, Touch kind, float power, int number, bool perfect = false, bool goesOut = false, bool sure = false,
                    bool playable = true)
        {
            int team = who.Team;
            _sureBall = sure;
            _overBlock = _tooHard = false;
            Ball = f;
            BallLive = true;
            BallResting = false;
            LastTouch = kind;
            LastPower = power;
            LastTeam = team;
            _hitter = team;
            _goesOut = goesOut;
            _lastPerfect = perfect;
            _lastZ = f.From.z == 0f ? Side(team) * 0.01f : f.From.z;
            Raise(new VolleyEvent
            {
                Kind = VolleyEventKind.Touch, Team = team, Player = who.Index, Position = f.From, Touch = kind, Power = power, Perfect = perfect,
                Number = number, Late = _late, Attack = number == 3 || kind == Touch.Serve ? _incoming : Attack.None,
            });
            _late = false;
            bool over = kind is Touch.Serve or Touch.Spike or Touch.Push or Touch.Block;
            if (!over)
            {
                Possession = team;
                TeamTouches = number;
                return;   // PlanMate follows
            }
            Crossings++;
            Possession = 1 - team;
            TeamTouches = 0;
            Next.Active = false;
            // the hitting team falls back to defend
            for (int k = 0; k < 2; k++)
            {
                var p = Players[team * 2 + k];
                if (p.Expect != Move.None) continue;
                p.Target = Spot(team, k == 0 ? -HomeAcross : HomeAcross, HomeDepth - 0.4f);
                p.RunAt = Time + 0.25;
            }
            if (!_goesOut && playable) PlanReceive(1 - team);
            _incoming = Attack.None;
            _incomingChoice = Defence.None;
        }

        /// <summary>A ball coming over the net to <paramref name="team"/>: who receives it (the nearer toy; two players
        /// together, the one whose half it comes to), where and when it meets it, whether it gets there; the computer's
        /// dig is decided now (the toy dives for one it misses); the other toy goes to the net to set.</summary>
        void PlanReceive(int team)
        {
            float s = Ball.DownThrough(BumpHeight);
            if (s < 0f) return;
            var point = Ball.At(Ball.Start + s);
            // a ball landing out isn't played (the point is the receiver's when it comes down)
            var landing = Ball.At(Ball.Start + Mathf.Max(s, Ball.DownThrough(BallRadius)));
            if (!InCourt(landing, team)) return;
            double time = Ball.Start + s;
            var receiver = Receiver(team, point);
            var mate = Mate(receiver);
            var c = Next;
            c.Active = true;
            c.Team = team;
            c.Player = receiver;
            c.Number = 1;
            c.Time = time;
            c.Point = point;
            c.Open = Math.Max(OpenFrom(team), Time - 1.0);
            c.Attack = _incoming == Attack.None ? Attack.Free : _incoming;
            c.Chose = _incomingChoice;
            c.LowAt = double.PositiveInfinity;
            c.Waiting = c.Missed = false;
            c.Sure = receiver.Controller >= 0 && (_sureBall || Practice);
            c.Dive = false;
            var spot = Stand(team, point, 0.32f);
            receiver.Target = spot;
            receiver.RunAt = Time + receiver.Reaction;
            // (a smash can't be received: the toy runs at it without getting ready)
            receiver.Expect = c.Attack == Attack.Smash && receiver.Controller >= 0 ? Move.None : Move.Bump;
            receiver.ExpectTime = time;
            receiver.ExpectPoint = point;
            if (receiver.Controller >= 0)
            {
                // it gets there, or near enough to dive the rest of the way (a dig from low down); a smash and a tip read
                // right always (the choice decided those)
                c.Reachable = Reaches(receiver, spot, time, 0f);
                if (!c.Reachable && (c.Sure || c.Attack == Attack.Smash || c.Attack == Attack.Tip && c.Chose == Defence.Receive))
                {
                    Hurry(receiver, spot, time);
                    c.Reachable = true;
                }
                if (!c.Reachable && Reaches(receiver, spot, time, DiveReach))
                {
                    c.Reachable = c.Dive = true;
                    receiver.DiveAt = time;
                }
                c.CpuPlays = true;
            }
            else
            {
                // the computer's toys get there; whether the dig comes off is the level's (sure while its rally is)
                Hurry(receiver, spot, time);
                c.Reachable = true;
                c.CpuPlays = SureRally || Practice || Chance(DigChance());
            }
            // the other one goes to the net to set
            var setSpot = Spot(team, Mathf.Clamp(Across(team, point.x) * 0.3f, -0.9f, 0.9f), SetDepth + 0.3f);
            mate.Target = setSpot;
            mate.RunAt = Time + mate.Reaction;
            mate.Expect = Move.None;
        }

        /// <summary>A pass inside a team: its toy <paramref name="who"/> meets it at <paramref name="point"/> at
        /// <paramref name="time"/> (always: a team's own pass is never out of reach).</summary>
        void PlanMate(VolleyPlayer who, int number, Vector3 point, double time, Move wants)
        {
            var c = Next;
            c.Active = true;
            c.Team = who.Team;
            c.Player = who;
            c.Number = number;
            c.Time = time;
            c.Point = point;
            c.Open = Time + 0.12;
            c.Attack = Attack.None;
            c.Chose = Defence.None;
            c.LowAt = double.PositiveInfinity;
            c.Waiting = c.Missed = false;
            c.Reachable = true;
            c.CpuPlays = true;
            c.Sure = false;
            var spot = Stand(who.Team, point, number == 3 ? 0.22f : 0.3f);
            who.Target = spot;
            who.RunAt = Time + 0.05;
            Hurry(who, spot, time);
            who.Expect = wants;
            who.ExpectTime = time;
            who.ExpectPoint = point;
            var mate = Mate(who);
            if (mate.Expect == Move.None)
            {
                // the one who just played it backs off a little to cover
                mate.Target = Spot(who.Team, Across(who.Team, mate.Position.x) < 0f ? -HomeAcross : HomeAcross, number == 3 ? 3.2f : HomeDepth - 1.2f);
                mate.RunAt = Time + 0.2;
            }
        }

        /// <summary>Moves count from here: the hitting team's previous touch (the toss of a serve), when the receiver could
        /// first see the ball coming.</summary>
        double OpenFrom(int receiving)
        {
            int hitting = 1 - receiving;
            double latest = double.NegativeInfinity;
            for (int k = 0; k < 2; k++)
            {
                var p = Players[hitting * 2 + k];
                if (p.TouchTime < Time - 0.05) latest = Math.Max(latest, p.TouchTime);
            }
            // never before the receiving team's own last touch (a pose held for that one isn't this one's)
            for (int k = 0; k < 2; k++) latest = Math.Max(latest, Players[receiving * 2 + k].TouchTime + 0.12);
            return double.IsNegativeInfinity(latest) ? Time - 1.0 : latest;
        }

        /// <summary>Who receives a ball coming down at <paramref name="point"/>: two players together, the one whose half it
        /// is (player 1's toy covers the left); else the toy that gets there first.</summary>
        VolleyPlayer Receiver(int team, Vector3 point)
        {
            var a = Players[team * 2];
            var b = Players[team * 2 + 1];
            if (Mode == VolleyMode.Together && IsHuman(team))
            {
                float across = Across(team, point.x);
                if (across < -0.25f) return a;
                if (across > 0.25f) return b;
            }
            var spot = Stand(team, point, 0.32f);
            float ta = (a.Position - spot).magnitude / a.MaxSpeed, tb = (b.Position - spot).magnitude / b.MaxSpeed;
            return ta <= tb ? a : b;
        }

        VolleyPlayer Mate(VolleyPlayer p) => Players[p.Team * 2 + (1 - p.Index % 2)];

        /// <summary>Across a team's own half: + = its right.</summary>
        static float Across(int team, float x) => x * RightSign(team);

        /// <summary>Where a toy stands to meet a ball at <paramref name="point"/>: a little behind it (toward its baseline).</summary>
        static Vector2 Stand(int team, Vector3 point, float behind) => new Vector2(point.x, point.z + Side(team) * behind);

        /// <summary>The toy gets to <paramref name="spot"/> by <paramref name="time"/> (after its reaction, with a little
        /// margin), or within <paramref name="dive"/> m of it (it dives the rest).</summary>
        bool Reaches(VolleyPlayer p, Vector2 spot, double time, float dive) =>
            Time + p.Reaction + Mathf.Max(0f, (spot - p.Position).magnitude - dive) / Mathf.Max(0.1f, p.MaxSpeed) + 0.15 <= time;

        /// <summary>A quicker speed so <paramref name="p"/> gets to <paramref name="spot"/> by <paramref name="time"/>.</summary>
        void Hurry(VolleyPlayer p, Vector2 spot, double time)
        {
            float seconds = (float)(time - Time - Math.Min(p.Reaction, 0.08)) - 0.05f;
            float need = (spot - p.Position).magnitude / Mathf.Max(0.12f, seconds);
            p.Hurry = need > p.MaxSpeed ? Mathf.Min(need, 22f) : 0f;
            if (p.Hurry > 0f) p.RunAt = Math.Min(p.RunAt, Time + 0.03);
        }

        /// <summary>How likely the computer's toy digs the ball coming now: harder for a fast or perfect spike.</summary>
        float DigChance()
        {
            float difficulty = LastTouch switch
            {
                Touch.Spike => 0.25f + 0.6f * LastPower + (_lastPerfect ? 0.12f : 0f),
                Touch.Serve => 0.1f + 0.3f * LastPower,
                _ => 0f,
            };
            return Mathf.Clamp(Level.Dig - Level.Pressure * difficulty, 0.05f, 0.98f);
        }

        bool _lastPerfect;

        /// <summary>
        /// A ball over the net from <paramref name="from"/> landing at <paramref name="to"/>, <paramref name="speed"/> m/s
        /// across: slower and higher until it clears the net by <paramref name="clearance"/> (a spike from low down comes
        /// out flatter).
        /// </summary>
        Flight Over(Vector3 from, Vector3 to, float speed, float g, float clearance)
        {
            float flat = new Vector2(to.x - from.x, to.z - from.z).magnitude;
            float seconds = Mathf.Max(0.18f, flat / Mathf.Max(1f, speed));
            float k = Mathf.Abs(from.z) / Mathf.Max(0.01f, Mathf.Abs(to.z - from.z));
            for (int i = 0; i < 40; i++)
            {
                var f = Flight.Between(from, to, seconds, g, Time);
                if (Mathf.Sign(from.z) == Mathf.Sign(to.z) || f.At(Time + seconds * k).y >= NetHeight + BallRadius + clearance) return f;
                seconds *= 1.08f;
            }
            return Flight.Between(from, to, seconds, g, Time);
        }

        /// <summary>Practice: a flight from <paramref name="from"/> straight to the receiving toy's bump at the moment the
        /// test asks for.</summary>
        Flight PracticeBall(Vector3 from, int team)
        {
            var receiver = NearestHuman(team, Spot(team, 0f, HomeDepth));
            var point = new Vector3(receiver.Position.x, BumpHeight, receiver.Position.y - Side(team) * 0.32f);
            float natural = Mathf.Max(1.0f, (new Vector2(point.x - from.x, point.z - from.z)).magnitude / 9f);
            double at = Math.Max(Time + natural, PracticeContact(1, Time + natural));
            var f = Flight.Between(from, point, (float)(at - Time), Gravity, Time);
            // keep it over the net
            if (f.At(Time + (at - Time) * Mathf.Abs(from.z) / Mathf.Max(0.01f, Mathf.Abs(point.z - from.z))).y < NetHeight + 0.4f)
                f = Flight.Between(from, point, (float)(at - Time), Gravity * 0.6f, Time);
            return f;
        }

        /// <summary>Practice: a pass of the player's team, its flight time stretched to the moment the test asks for (the
        /// computer's own passes keep their pace).</summary>
        float PracticeSeconds(int number, float seconds)
        {
            if (!Practice || PracticeContact == null) return seconds;
            double at = PracticeContact(number, Time + seconds * 0.85);
            return Mathf.Max(seconds * 0.85f, (float)(at - Time));
        }

        /// <summary>A landing spot in <paramref name="team"/>'s court (its own frame).</summary>
        static Vector3 Land(int team, float across, float depth)
        {
            var s = Spot(team, Mathf.Clamp(across, -HalfWidth + 0.4f, HalfWidth - 0.4f), Mathf.Clamp(depth, 1f, HalfLength - 0.4f));
            return new Vector3(s.x, BallRadius, s.y);
        }

        /// <summary>A spot near <paramref name="at"/> (within <paramref name="radius"/>), in the court between the depths.</summary>
        Vector3 Near(int team, Vector2 at, float radius, float minDepth, float maxDepth)
        {
            var off = new Vector2(Rand(-radius, radius), Rand(-radius, radius));
            var p = at + off;
            float depth = Mathf.Clamp(Mathf.Abs(p.y), minDepth, maxDepth), across = Mathf.Clamp(Across(team, p.x), -HalfWidth + 0.5f, HalfWidth - 0.5f);
            return Land(team, across, depth);
        }

        /// <summary>The open court: of a few spots, the one farthest from the defending toys (leaned by
        /// <paramref name="lean"/> metres across, + = the receiving team's right).</summary>
        Vector3 OpenSpot(int team, float lean)
        {
            Vector3 best = Land(team, 0f, 5f);
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < 4; i++)
            {
                var c = Land(team, Rand(-3.3f, 3.3f) + lean, Rand(3.2f, 7.3f));
                float d = float.PositiveInfinity;
                for (int k = 0; k < 2; k++) d = Mathf.Min(d, (Players[team * 2 + k].Position - new Vector2(c.x, c.z)).magnitude);
                float score = d + Rand(0f, 1.2f);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }
            return best;
        }

        VolleyPlayer NearestHuman(int team, Vector2 to)
        {
            var a = Players[team * 2];
            var b = Players[team * 2 + 1];
            return (a.Position - to).sqrMagnitude <= (b.Position - to).sqrMagnitude ? a : b;
        }

        // ------------------------------------------------------------------------------------------------ moving

        void MovePlayers(float dt)
        {
            foreach (var p in Players)
            {
                var before = p.Position;
                if (Time >= p.RunAt)
                {
                    float speed = p.Hurry > 0f ? p.Hurry : p.MaxSpeed;
                    var d = p.Target - p.Position;
                    float dist = d.magnitude;
                    if (dist > 1e-4f) p.Position += d * Mathf.Min(1f, speed * dt / dist);
                }
                p.Velocity = (p.Position - before) / dt;
            }
        }

        // ------------------------------------------------------------------------------------------------ helpers

        float Rand(float a, float b) => a + (float)_rng.NextDouble() * (b - a);
        bool Chance(float p) => _rng.NextDouble() < p;

        void Raise(VolleyEvent e) => Events?.Invoke(e);
    }
}
