using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Tennis
{
    /// <summary>Who plays (Wii Sports Tennis's own choices for one and two players).</summary>
    public enum TennisMode
    {
        /// <summary>One player against one computer player: singles (2026-11-07, user: "the second character per team is
        /// not needed anymore"; before, the player moved both players of a doubles team).</summary>
        Solo,
        /// <summary>Two players on one team against the computer's pair, one player each, one at the back and one at the
        /// net (Wii's "one up, one back"): they change places every game, except that whoever's turn it is to serve
        /// plays from the back (player 1 serves first), so both get the busy back and both serve.</summary>
        Together,
        /// <summary>Two players against each other, singles: player 1 on the near end (team 0), player 2 on the far one
        /// (1). The game splits the screen, each half seen from behind its player.</summary>
        Versus,
    }

    /// <summary>How a racket is swung. <see cref="Scoop"/> (low to high in front) is only ever drawn: a player's swing
    /// straight up at no ball (at a ball in reach, a swing up is a lob).</summary>
    public enum Stroke { Forehand, Backhand, Overhead, Scoop }
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
        /// <summary>The ball came near enough, but the player stood too far from it (the body has to move closer).</summary>
        TooFar,
        /// <summary>A swing toward the coming ball's side before it arrived: the racket going back, getting ready (it
        /// neither hits nor counts as too early).</summary>
        Backswing,
        /// <summary>The racket swung down at a ball near enough: it missed at once (a swing up hits: a lob).</summary>
        Vertical,
        /// <summary>A swing toward the ball's side while it was in reach but still coming (a backswing, or the player
        /// swinging toward where they see the ball): the ball will be hit by it unless a stroke follows before the ball
        /// arrives, which then hits instead (2026-11-07, user: "the shots with the right hand left to right when the ball
        /// comes to the left side are not registered that well").</summary>
        Held,
    }

    public enum MatchEventKind
    {
        Toss, Caught, Hit, Bounce, Net, Point,
        /// <summary>The player swung while no ball of theirs was near enough (too early or too late).</summary>
        Whiff,
        /// <summary>A ball of the player's went by without a swing at it.</summary>
        Missed,
        /// <summary>The player swung in time but stood too far from the ball.</summary>
        TooFar,
        /// <summary>The player swung down at a ball near enough and missed it (<see cref="SwingResult.Vertical"/>).</summary>
        Vertical,
    }

    /// <summary>One shot: how it was hit.</summary>
    public struct Shot
    {
        public ShotKind Kind;
        public int Team;
        public Stroke Stroke;
        /// <summary>m/s across the court, as it flies (its own speed times <see cref="Pace"/>).</summary>
        public float Speed;
        /// <summary>How hard it was hit (0..1): a player's swing speed, less for a mistimed one. A weak ball (a soft or
        /// mistimed shot, a lob) invites the computer's attack (<see cref="TennisLevel.Attack"/>).</summary>
        public float Power;
        /// <summary>The rally's pace it flies at (<see cref="BallState.Pace"/>, <see cref="TennisMatch.PaceFor"/>).</summary>
        public float Pace;
        /// <summary>When the player's swing came: -1 = as early as counts (the ball still well before them: it goes
        /// across), 0 = right as it reached them, +1 = as late as counts (down the line). The serve: against the toss's
        /// top. 0 for the computer.</summary>
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
    /// animate (a swing with its contact moment, a dive, a cheer). The players' own toys swing when their player swings
    /// (2026-11-06: they no longer hold the racket where the camera puts the hand, which shook with the pose model's
    /// noise): the hit timed to the ball, any other swing at once, as fast as the hand went.</summary>
    public sealed class CourtPlayer
    {
        public int Index, Team;
        /// <summary>On the court: in singles each team's second player (<see cref="TennisMatch.Players"/> 1 and 3) is not,
        /// it never moves or plays and the game doesn't draw it.</summary>
        public bool Active = true;
        /// <summary>Plays at the net (volleys) rather than from the baseline. Two players on one team change places
        /// between games (<see cref="TennisMode.Together"/>).</summary>
        public bool AtNet;
        /// <summary>The player (0 = player 1, 1 = player 2) whose body and swings move it, or -1: the computer.</summary>
        public int Controller = -1;
        public bool RightHanded = true;
        /// <summary>(x, z) on the court.</summary>
        public Vector2 Position, Velocity, Target;
        /// <summary>How fast it runs (m/s), and how long after the other side's hit it sets off for the ball (s).</summary>
        public float MaxSpeed = 9f, Reaction;
        /// <summary>A quicker speed (m/s) for the ball in play, or 0: a ball of the computer's sure first returns
        /// (<see cref="TennisMatch.SureReturns"/>) it must get to.</summary>
        public float Hurry;
        /// <summary>Match time it may start running to <see cref="Target"/> (its reaction to a shot); it waits until then.</summary>
        public double RunAt;
        /// <summary>Counts swings: the view starts a swing when it changes.</summary>
        public int Swings;
        public Stroke SwingStroke;
        /// <summary>Match time the racket meets the ball (the view times its swing to it).</summary>
        public double SwingContact;
        /// <summary>The swing connects (false: an air swing).</summary>
        public bool SwingHits;
        public ShotKind SwingKind;
        /// <summary>How hard the swing is (0..1): the player's hand speed (the stroke is drawn quicker), 0.5 for the
        /// computer.</summary>
        public float SwingPower = 0.5f;
        /// <summary>The swing is drawn from wherever the racket is, straight to the hit, without going back first (a
        /// player's air swing: it follows their hand at once).</summary>
        public bool SwingQuick;
        /// <summary>Where the ball is when the racket meets it (a hit's swing; <see cref="SwingReaches"/>): the toy
        /// reaches for it, its racket's head right there (2026-11-06: Wii's Miis reach into the ball).</summary>
        public Vector3 SwingPoint;
        public bool SwingReaches;
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

    /// <summary>The receiving team's next touch of the ball: who meets it, where and when it is best met, and where that
    /// player stands for it.</summary>
    public sealed class Contact
    {
        public bool Active;
        public int Team;
        public CourtPlayer Hitter;
        /// <summary>Match time the ball reaches the hitter's racket.</summary>
        public double Time;
        /// <summary>Where the ball is then.</summary>
        public Vector3 Point;
        /// <summary>Where the hitter stands to meet it there (it runs there by itself): beside the ball on the side it
        /// passes, a little behind.</summary>
        public Vector2 Spot;
        public Stroke Stroke;
        /// <summary>The ball's side of the hitter: +1 = +x.</summary>
        public float Side;
        /// <summary>The net player meets it before it bounces.</summary>
        public bool Volley, Smash;
        /// <summary>The hitter gets to <see cref="Spot"/> in time (it runs at its speed after its reaction). A ball out of
        /// reach goes by whatever the swing: the toy dives for it.</summary>
        public bool Reachable;
        /// <summary>A swing of the player's connected (the hit may wait for the ball).</summary>
        public bool Taken;
        /// <summary>The ball went by untouched (<see cref="MatchEventKind.Missed"/> was raised).</summary>
        public bool Missed;
        public bool SwingShown;
    }

    /// <summary>
    /// "Tenis!", after Wii Sports Tennis. Pure rules (no scene), driven by <see cref="Step"/> and the player's swings
    /// (<see cref="Swing"/>); <c>TennisGame</c> draws it.
    /// <list type="bullet">
    /// <item><b>Singles</b> (<see cref="Singles"/>, since 2026-11-07: one player against the computer, and two players
    /// against each other): one player a side on the singles court, from the baseline. <b>Doubles</b> when two players play
    /// together against the computer's pair (<see cref="Players"/>: team 0's two, then team 1's): one plays from the
    /// baseline, one at the net (<see cref="NetDepth"/>). <b>Everyone runs to the ball by
    /// themselves</b>, the player's toys too (2026-11-07, user: "the characters move on their own and go near where the
    /// ball will end up; depending on the difficulty not all balls need to be reached"; before, the body's sideways steps
    /// moved them): as a shot leaves the other side, whoever will meet it sets off after its reaction
    /// (<see cref="CourtPlayer.Reaction"/>) for the spot beside the ball at its speed (<see cref="CourtPlayer.MaxSpeed"/>:
    /// the difficulty's, <see cref="TennisLevel.HumanRunSpeed"/>); a ball it can't get to in time goes by (it dives for it).
    /// After its team's shot the back player goes back to cover the court, the net player to its place at the net, a step
    /// to the side of the back player (<see cref="NetOffset"/>), so it doesn't stand in front of the ball coming to the
    /// back player.</item>
    /// <item><b>Who meets the ball</b> (each shot's path is predicted, <see cref="BallPhysics"/>): the net player volleys
    /// a ball that passes within its reach (<see cref="NetReach"/>) before bouncing (never a serve); else the back player
    /// meets it after the bounce, as it reaches the baseline or comes down to waist height.</item>
    /// <item><b>A swing hits when the ball is near enough</b> (Wii): a quick move of either hand, either way across, or
    /// the racket hand up, from when the ball is <see cref="EarlyReach"/> before the point where it is met until it is
    /// <see cref="LateReach"/> past it (at least <see cref="MinEarly"/> / <see cref="MinLate"/> seconds), with the player
    /// near it. When it came aims the ball: early pulls it across, late sends it down the line, right as it arrives
    /// straight. How fast the hand went sideways is the power: the faster, the faster and deeper the shot, and a fast one
    /// right on time (within <see cref="PerfectReach"/>, at least <see cref="PerfectPower"/>) is a perfect shot, the
    /// hardest to return (2026-11-07, user: "horizontal swings depending on the speed should provide the best shot").
    /// Tilted up or straight up is topspin, a low-to-high drive as fast as the hand (2026-11-07 evening, user: "too many
    /// lob shots, be more lenient": their swings went up at 70-90 degrees and 13 of 22 hits were lobs); only a gentle scoop
    /// straight up (<see cref="LobFrom"/>) lobs; down is a slice; a ball met above <see cref="SmashHeight"/> is smashed. An
    /// early swing's hit waits for the ball. A swing toward the ball's side while it is still coming is the backswing
    /// (<see cref="BackswingBefore"/>): in reach it holds a hit (<see cref="SwingResult.Held"/>), which the stroke after
    /// it replaces, else it hits as the ball arrives (a player's sense of the ball's side is the court's, the toy's is
    /// where it stands). <b>A strong swing down</b> (steeper than <see cref="VerticalLimit"/>, quicker than
    /// <see cref="VerticalMissSpeed"/>) of the racket hand at a ball in reach <b>misses it at once</b>. The player's
    /// shots go in: a mistimed one is weaker (slower, shorter), and only against the strongest
    /// (<see cref="TennisLevel.WideFrom"/>) and between two players does a swing at the window's very edge go wide.</item>
    /// <item><b>The rally speeds up</b>: every ball flies at the rally's pace (<see cref="PaceFor"/>,
    /// <see cref="BallState.Pace"/>): the serve slow, each hit after it quicker. The arcs stay the same, so where a
    /// racket meets the ball doesn't change, only how soon.</item>
    /// <item>Times are camera capture times: the game passes when the camera saw the swing, so its lag doesn't make every
    /// swing late; a late swing's ball leaves from where it was then; the end of a point the player could still have
    /// played waits for the camera to catch up (<see cref="HitLag"/>, <see cref="SawUntil"/>).</item>
    /// <item><b>Serve</b>: the back player serves; the ball is tossed (by itself, or by a first swing); any swing while
    /// it's up serves, at the top (<see cref="PowerServeWindow"/>) it's a power serve. Serves always go in; a missed toss
    /// is just caught again.</item>
    /// <item>The computer (<see cref="TennisLevel"/>) moves its players by themselves; <b>its first
    /// <see cref="SureReturns"/> returns of every point are sure</b> (it gets there, returns the ball in, within the
    /// player's reach, and the player's toy gets to them: <see cref="SureRally"/>), after them whether it gets a ball back
    /// is decided as the ball is hit (harder for fast, perfect, far or smashed balls), it aims into the corners, past or
    /// over the player's net player, attacks weak balls and, later, plays short; some of its shots go out or into the
    /// net.</item>
    /// </list>
    /// The doubles court's alleys are in after the serve in doubles only. Scoring: <see cref="TennisScore"/> (best of 3
    /// games by default). Practice (<see cref="Practice"/>): no score, the computer's back player returns everything and
    /// can time its feeds, the net players stay out of it (tests, a training mode).
    /// <para><b>Two players</b> (<see cref="Mode"/>, 2026-11-05): each player is a slot (0 = player 1, 1 = player 2) with
    /// their own swings (<see cref="Swing(int, double, float, Vector2)"/>, which hit only for the players that slot
    /// controls: <see cref="CourtPlayer.Controller"/>) and camera clock (<see cref="SawUntil(int, double)"/>). Together:
    /// one player each against the computer, changing places between the back and the net every game (the server plays
    /// from the back), the net player reaching a little further (<see cref="TogetherNetReach"/>) and keeping to its
    /// player's side of the room (<see cref="NetSide"/>). Versus: singles, every rule the same on both ends; a swing at
    /// the window's very edge goes wide (<see cref="VersusWideFrom"/>), so a point can end on a mistake.</para>
    /// </summary>
    public sealed class TennisMatch
    {
        /// <summary>Where a back player waits for the ball (metres behind the net), a little behind the baseline.</summary>
        public const float HomeDepth = 12.6f;
        /// <summary>Where a net player stands (metres behind the net).</summary>
        public const float NetDepth = 4.6f;
        /// <summary>A back player meets a bounced ball as it comes down to this height (or as it reaches their depth, if
        /// it's no higher than <see cref="HighestContact"/> then)...</summary>
        public const float ContactHeight = 1.05f, HighestContact = 1.7f;
        /// <summary>...and never further back than this.</summary>
        public const float BackLimit = 15.5f;
        /// <summary>How close to the net a back player runs in for a short ball (metres).</summary>
        public const float NearestToNet = 2.5f;
        /// <summary>A net player volleys a ball passing between these heights (m).</summary>
        public const float VolleyLow = 0.35f, VolleyHigh = 2.4f;
        /// <summary>A ball met higher than this (m) is smashed.</summary>
        public const float SmashHeight = 2.0f;
        /// <summary>The toss leaves the hand this high (m) this fast (m/s): up to ~1.9 m, where the smaller toy's big racket
        /// reaches it with a little jump (2026-11-06: 1 m at 5 m/s went up to ~2.3 m, twice the toy's height).</summary>
        public const float TossHeight = 0.95f, TossSpeed = 4.4f;
        public static float TossApex => TossSpeed / BallPhysics.Gravity;
        /// <summary>The toss is caught again this long after it went up (it fell back to the hand).</summary>
        public static float TossCatch => 2f * TossApex;

        /// <summary>A swing counts from when the ball is this far (m, along its flight) before the point where it is met
        /// to this far past it, but over at least <see cref="MinEarly"/> before and <see cref="MinLate"/> after
        /// (seconds): "near enough" (Wii).</summary>
        public float EarlyReach = 3.5f, LateReach = 2.0f, MinEarly = 0.3f, MinLate = 0.18f;
        /// <summary>A swing within this distance (m) of the ball's arrival is a perfect, faster shot.</summary>
        public float PerfectReach = 0.6f;
        /// <summary>A swing's ball still comes when the camera saw the swing this late at most (s): a point the player
        /// could still have played ends only once the camera has caught up, or this long after.</summary>
        public float HitLag = 0.35f;
        public float PowerServeWindow = 0.12f;
        /// <summary>How far sideways (m) a net player reaches for a volley from where it stands.</summary>
        public float NetReach = 1.3f;
        /// <summary>A team's net player waits this far to the side (the team's own x, - = left) of where its back player
        /// covers the court: off the line from the camera to the back player, so it never hides the ball coming to them.</summary>
        public float NetOffset = -1.4f;
        /// <summary>A run is counted this much longer (s) than its distance over the runner's speed: setting off and
        /// slowing down into the spot.</summary>
        public const float RunMargin = 0.15f;
        /// <summary>Together: the net player, moved by a player of their own, reaches this far for a volley (else it got a
        /// ball for every five of the back player's: two simulated players, 2026-11-05).</summary>
        public const float TogetherNetReach = 1.7f;
        /// <summary>The players' shots go wide when their timing is beyond this share of the window (early or late):
        /// against the computer the difficulty's (<see cref="TennisLevel.WideFrom"/>: never but against the strongest);
        /// between two players <see cref="VersusWideFrom"/> (else two good players' rallies went on and on: their shots
        /// always went in and their toys got to every ball).</summary>
        public float WideFrom;
        public const float VersusWideFrom = 0.75f;
        /// <summary>A shot is perfect (15 % faster, the hardest to return) only from a sideways swing at least this strong
        /// (<see cref="SlowSwing"/>..<see cref="FastSwing"/>: 0.35 = 5.5 torso lengths a second) right on time.</summary>
        public float PerfectPower = 0.35f;
        /// <summary>A shot timed this far from the ball's arrival (share of the window, early or late) or more starts losing
        /// power, down to <see cref="MistimedPower"/> of it at the window's edge.</summary>
        public float MistimedFrom = 0.4f, MistimedPower = 0.55f;
        /// <summary>At the hit the player must stand this close (m) to the spot beside the ball.</summary>
        public float ReachTolerance = 0.7f;
        /// <summary>Swing speeds (torso lengths per second, sideways) from a gentle shot to a full-power one.</summary>
        public float SlowSwing = 3f, FastSwing = 10f;
        /// <summary>A swing whose direction is more up or down than this (its vertical share: 0.77 = 50 degrees from
        /// level) is a vertical swing: the racket hand's down misses a ball in reach at once, up lobs it. The user's real
        /// swings (2026-11-06, the box's log) went within ~25 degrees of level, arms raised or dropped at ~75.</summary>
        public float VerticalLimit = 0.77f;
        /// <summary>A swing down misses a ball in reach at once only when it is clearly strong (torso lengths per
        /// second): the arm coming back down after a small flick up measured 3.1 (2026-11-07, the flick test's scripted
        /// player with the box's glitches), just over the gentle swing's 3.</summary>
        public float VerticalMissSpeed = 3.6f;
        /// <summary>A sideways swing toward the coming ball's side while the ball is still more than this (s) from its
        /// hitter is the backswing (the racket going back): no "too early", and in reach it holds a hit that a stroke
        /// after it replaces (<see cref="SwingResult.Held"/>). The user's real backswings came ~0.3 s before the stroke
        /// (2026-11-06); closer than this any sideways swing hits.</summary>
        public float BackswingBefore = 0.12f;
        /// <summary>A held backswing (<see cref="SwingResult.Held"/>) hits once the camera has shown this long (s) past the
        /// ball's arrival with no stroke after it (a stroke seen until then has reached the game and hits instead: the
        /// camera's lag and the swing detector's confirmation), from where the ball was met, like any swing that reached the
        /// game late.</summary>
        public float HeldSeen = 0.1f;
        /// <summary>Seconds between the end of a point and the next serve (longer after a game).</summary>
        public float PointPause = 2.2f, GamePause = 3.2f, MatchPause = 3.5f;

        public readonly TennisLevel Level;
        public readonly TennisScore Score;
        public readonly TennisMode Mode;
        /// <summary>
        /// Against the computer, its first this many returns of every point are sure (2026-11-07 evening, user: "at minimum
        /// in all game modes vs cpu make the bot hit at least 5 times, after that it should depend on the difficulty; it's
        /// not fun to make 1-hit points all the time"): it gets to the ball and returns it, never into the net or out and
        /// never out of the player's reach (no attack, no corner), the player's toy gets to those balls, and the player's
        /// shots go in. After them the difficulty decides. (The player's own misses still end a point.)
        /// </summary>
        public int SureReturns = 5;
        /// <summary>The point is still in its sure part: a computer's side has returned fewer than <see cref="SureReturns"/>
        /// balls (never in practice or between two players).</summary>
        public bool SureRally => !Practice && !IsHuman(1) && CpuReturnsThisPoint < SureReturns;
        /// <summary>The computer's returns (not its serve) in the point being played.</summary>
        public int CpuReturnsThisPoint { get; private set; }
        // the ball in play is one of the sure part's: the player's toy gets to it
        bool _sureBall;
        /// <summary>One player a side (Solo, Versus); doubles only when two players play together.</summary>
        public readonly bool Singles;
        /// <summary>Half the court's width in play after the serve (m): the singles or the doubles lines.</summary>
        public float HalfWidth => Singles ? Court.SinglesHalfWidth : Court.HalfWidth;
        /// <summary>The court's width against the doubles court's: aims scale with it.</summary>
        float WidthScale => HalfWidth / Court.HalfWidth;
        /// <summary>Team 0's two, then team 1's (team 0 = the near end: the player, or player 1). Each team's first one
        /// starts at the back, the second at the net (in singles it isn't on the court: <see cref="CourtPlayer.Active"/>);
        /// in <see cref="TennisMode.Together"/> they change places.</summary>
        public readonly CourtPlayer[] Players = new CourtPlayer[4];
        /// <summary>Team 0's back and net player, team 1's (the computer's in one-team modes). In singles the net players
        /// are off the court.</summary>
        public CourtPlayer Human => Back(0);
        public CourtPlayer HumanNet => NetPlayer(0);
        public CourtPlayer Cpu => Back(1);
        public CourtPlayer CpuNet => NetPlayer(1);
        public CourtPlayer Back(int team) => Players[team * 2].AtNet ? Players[team * 2 + 1] : Players[team * 2];
        public CourtPlayer NetPlayer(int team) => Players[team * 2].AtNet ? Players[team * 2] : Players[team * 2 + 1];
        /// <summary>A player (not the computer) moves this team.</summary>
        public bool IsHuman(int team) => Players[team * 2].Controller >= 0 || Players[team * 2 + 1].Controller >= 0;
        /// <summary>How many players play (1 or 2).</summary>
        public int Humans => Mode == TennisMode.Solo ? 1 : 2;
        /// <summary>The team a player's slot plays on.</summary>
        public int TeamOf(int slot) => Mode == TennisMode.Versus ? slot : 0;
        public readonly Contact Next = new Contact();

        /// <summary>Practice: no score, the computer's back player returns everything (and times its feeds with
        /// <see cref="PracticeContact"/>), the net players don't play.</summary>
        public bool Practice;
        /// <summary>Practice feeds: given the earliest moment a feed could reach the player's racket (match time), when it
        /// should. Null = at a normal pace. Feeds are aimed at <see cref="SlowestFeed"/> to 20 m/s (slower arcs came too
        /// high to reach), then flown at the rally's pace like every shot; one that would come too early even at the
        /// slowest is fed later, like a ball machine.</summary>
        public Func<double, double> PracticeContact;
        public float SlowestFeed = 12f;
        /// <summary>Practice serves: given the earliest moment of the toss's top, when it should be.</summary>
        public Func<double, double> PracticeToss;

        public event Action<MatchEvent> Events;

        public double Time { get; private set; }
        public MatchPhase Phase { get; private set; }
        public BallState Ball;
        /// <summary>The ball is on the court (false: in the server's hand).</summary>
        public bool BallLive { get; private set; }
        /// <summary>The team that hit the ball last, or -1.</summary>
        public int LastHitter { get; private set; } = -1;
        public bool LastWasServe { get; private set; }
        public Shot LastShot { get; private set; }
        public int RallyHits { get; private set; }
        public bool DeuceCourt { get; private set; } = true;
        public int ServerTeam => Practice ? 0 : Score.Server;
        public CourtPlayer Server => Back(ServerTeam);
        /// <summary>The match time the toss reaches its top (serving).</summary>
        public double TossTop { get; private set; }
        /// <summary>Misses in a row of the player's toss (for a hint).</summary>
        public int TossesMissed { get; private set; }
        /// <summary>The last point's winner, or -1.</summary>
        public int PointWinner { get; private set; } = -1;
        public PointReason LastReason { get; private set; }
        public ScoreCall LastCall { get; private set; }

        /// <summary>One player's numbers for the results screen.</summary>
        public sealed class PlayerStats
        {
            /// <summary><see cref="Vertical"/>: balls missed by swinging up or down at them.</summary>
            public int Hits, Volleys, Perfect, Whiffs, Missed, TooFar, Aces, Vertical;
        }

        /// <summary>The rally's pace after <paramref name="hits"/> hits (the serve is hit 0): the ladder's
        /// <see cref="TennisLevel.ServePace"/>, <see cref="TennisLevel.PaceStep"/> quicker each hit, at most
        /// <see cref="TennisLevel.TopPace"/>.</summary>
        public float PaceFor(int hits) => Mathf.Min(Level.TopPace, Level.ServePace + Level.PaceStep * Mathf.Max(0, hits));

        /// <summary>The pace of the ball in play (1 in the server's hand).</summary>
        public float Pace => BallLive ? Ball.TimeScale : 1f;
        /// <summary>Per player (slot 0 = player 1).</summary>
        public readonly PlayerStats[] Stats = { new PlayerStats(), new PlayerStats() };

        // stats for the results screen (both players together)
        public int HumanHits => Stats[0].Hits + Stats[1].Hits;
        public int HumanVolleys => Stats[0].Volleys + Stats[1].Volleys;
        public int HumanPerfect => Stats[0].Perfect + Stats[1].Perfect;
        public int HumanWhiffs => Stats[0].Whiffs + Stats[1].Whiffs;
        public int HumanMissed => Stats[0].Missed + Stats[1].Missed;
        public int TooFar => Stats[0].TooFar + Stats[1].TooFar;
        public int LongestRally { get; private set; }
        /// <summary>Points won and lost by team 0.</summary>
        public int PointsWon { get; private set; }
        public int PointsLost { get; private set; }

        readonly System.Random _rng;
        readonly List<PathPoint> _path = new List<PathPoint>();
        readonly List<PathPoint> _scratch = new List<PathPoint>();
        double _ballTime, _tossAt, _tossStart, _nextPointAt, _cpuServeAt, _lastHitTime;
        int _bouncesOnReceiver, _bounce;
        bool _landsIn, _netMissed;
        bool _cpuReturns, _cpuServeDone, _heldHit;
        float _heldTiming, _heldPower, _heldVertical, _heldTempo;
        bool _heldPerfect;
        // a held backswing (SwingResult.Held): hits once the camera has shown past the ball's arrival, unless a stroke
        // replaces it
        bool _heldSoft;
        readonly double[] _sawUntil = { double.NegativeInfinity, double.NegativeInfinity };
        // together: which game the places were last set for, and how many of their team's service games have started
        int _rolesGame = -1, _ourServiceGames;
        // a point a player could still have played: ended once the camera has caught up with _endBallTime
        bool _endPending;
        int _endWinner, _endTeam;
        PointReason _endReason;
        double _endBallTime, _endAt;
        // practice: a feed waiting for its moment (the ball machine)
        bool _feedPending;
        double _feedAt;
        Vector3 _feedFrom;
        Shot _feedShot;

        public TennisMatch(TennisLevel level, int gamesToWin = 2, int seed = 0, int firstServer = 0,
                           TennisMode mode = TennisMode.Solo)
        {
            Level = level;
            Mode = mode;
            Score = new TennisScore(gamesToWin, firstServer);
            _rng = new System.Random(seed);
            Singles = mode != TennisMode.Together;
            for (int i = 0; i < 4; i++) Players[i] = new CourtPlayer { Index = i, Team = i / 2, AtNet = i % 2 == 1 };
            int[] controllers = mode switch
            {
                TennisMode.Together => new[] { 0, 1, -1, -1 },
                TennisMode.Versus => new[] { 0, -1, 1, -1 },
                _ => new[] { 0, -1, -1, -1 },
            };
            for (int i = 0; i < 4; i++)
            {
                Players[i].Controller = controllers[i];
                Players[i].Active = !Singles || i % 2 == 0;
                // the players' toys run by themselves, as quick as the difficulty makes them
                bool human = controllers[i] >= 0;
                Players[i].MaxSpeed = human ? level.HumanRunSpeed : level.RunSpeed;
                Players[i].Reaction = human ? level.HumanReaction : level.Reaction;
            }
            WideFrom = mode == TennisMode.Versus ? VersusWideFrom : level.WideFrom;
            if (mode == TennisMode.Together) NetReach = TogetherNetReach;
        }

        /// <summary>The player plays with the racket in this hand (forehand and backhand sides follow).</summary>
        public bool HumanRightHanded
        {
            get => Players[0].RightHanded;
            set => SetRightHanded(0, value);
        }

        /// <summary>Player <paramref name="slot"/> holds the racket in the right hand (or the left).</summary>
        public void SetRightHanded(int slot, bool right)
        {
            foreach (var p in Players)
                if (p.Controller == slot) p.RightHanded = right;
        }

        /// <summary>Together: which side of the court (-1 = the team's left, +1 its right) each player's toy keeps to while
        /// it waits at the net, so it isn't in front of the ball coming to their partner (player 1 stands on the left of
        /// the room).</summary>
        public readonly float[] NetSide = { -1f, 1f };

        /// <summary>Where a players' team's net player waits sideways (court x) while its back player covers
        /// <paramref name="backX"/>: <see cref="NetOffset"/> to the team's left of it (or to the right, where the left
        /// would be off the court); together, on its own player's <see cref="NetSide"/>.</summary>
        public float NetHomeX(int team, float backX)
        {
            var net = NetPlayer(team);
            float sign = Court.RightSign(team), limit = Court.HalfWidth - 0.6f;
            if (net.Controller >= 0 && net.Controller != Back(team).Controller)
                return Mathf.Clamp(sign * NetSide[net.Controller] * Mathf.Abs(NetOffset), -limit, limit);
            float x = Mathf.Clamp(backX + sign * NetOffset, -limit, limit);
            if (Mathf.Abs(x - backX) < Mathf.Abs(NetOffset) * 0.7f) x = Mathf.Clamp(backX - sign * NetOffset, -limit, limit);
            return x;
        }

        /// <summary>Where a team's back player goes to cover the court after its own shot to <paramref name="targetX"/>: the
        /// middle of its baseline, a little toward the side the ball went (the reply's likely angle).</summary>
        static Vector2 Cover(int team, float targetX) =>
            new Vector2(Mathf.Clamp(targetX * 0.35f, -2.2f, 2.2f), Court.Side(team) * HomeDepth);

        // the players' teams: where their two wait while the ball isn't coming to them (set at the start of a point and
        // after each of their shots)
        readonly Vector2[] _backHome = new Vector2[2], _netHome = new Vector2[2];

        /// <summary>A players' team's two wait here: the back player at <paramref name="back"/>, the net player at its
        /// place beside it.</summary>
        void SetHomes(int team, Vector2 back)
        {
            _backHome[team] = back;
            _netHome[team] = new Vector2(NetHomeX(team, back.x), Court.Side(team) * NetDepth);
        }

        /// <summary>The camera has shown player 1 up to this match time (<see cref="SawUntil(int, double)"/>).</summary>
        public void SawUntil(double time) => SawUntil(0, time);

        /// <summary>The camera has shown player <paramref name="slot"/> up to this match time (every camera sample of
        /// them, with its capture time; or now, playing with keys): their swings from before it have all arrived.</summary>
        public void SawUntil(int slot, double time) => _sawUntil[slot] = Math.Max(_sawUntil[slot], Math.Min(time, Time));

        /// <summary>Starts the match (the first serve).</summary>
        public void Begin() => StartPoint();

        /// <summary>Ends the match now, won by <paramref name="team"/> (tests: a tournament played through quickly).</summary>
        public void Award(int team)
        {
            while (!Score.MatchOver) Score.PointTo(team);
            Phase = MatchPhase.Over;
            BallLive = false;
            Next.Active = false;
            _endPending = _heldHit = _heldSoft = false;
        }

        // ------------------------------------------------------------------------------------------------ stepping

        public void Step(float dt)
        {
            if (dt <= 0f) return;
            Time += dt;
            while (_ballTime + BallPhysics.Step <= Time) FixedStep();
            MovePlayers(dt);
            if (_endPending && (Time >= _endAt || SeenUntilTeam(_endTeam) >= _endBallTime)) EndPoint(_endWinner, _endReason);
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
                float g = BallPhysics.Resting(Ball) ? 0f : BallPhysics.GravityOf(Ball);
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
                    if (Server.Controller < 0 && !_cpuServeDone && _ballTime >= _cpuServeAt) CpuServe();
                    else if (_ballTime >= _tossStart + TossCatch) Caught();
                    break;
                case MatchPhase.Rally:
                    if (_feedPending)
                    {
                        // the old ball rolls away unnoticed until the computer feeds the next one
                        if (_ballTime >= _feedAt)
                        {
                            _feedPending = false;
                            Launch(1, Cpu, _feedFrom, _feedShot);
                        }
                        break;
                    }
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
            // together: the two change places every game, except that whoever's turn it is to serve (player 1 first, then
            // player 2, ...) plays from the back; before the team's first serve, whoever won't serve it plays back first
            if (Mode == TennisMode.Together && !Practice && Score.GamesPlayed != _rolesGame)
            {
                int nextServer = _ourServiceGames % 2;
                int back;
                if (Score.Server == 0)
                {
                    back = nextServer;
                    _ourServiceGames++;
                }
                else back = _rolesGame < 0 ? 1 - nextServer : (Players[0].AtNet ? 0 : 1);
                _rolesGame = Score.GamesPlayed;
                Players[0].AtNet = back != 0;   // Players[0] is player 1's toy, Players[1] player 2's
                Players[1].AtNet = back == 0;
            }
            DeuceCourt = Practice || Score.DeuceCourt;
            BallLive = false;
            LastHitter = -1;
            RallyHits = 0;
            PointWinner = -1;
            Next.Active = false;
            _heldHit = _heldSoft = _endPending = _feedPending = _sureBall = false;
            CpuReturnsThisPoint = 0;
            int s = ServerTeam;
            float serveSide = DeuceCourt ? 1f : -1f;
            foreach (var p in Players)
            {
                p.Mood = 0;
                p.DiveAt = -1;
                p.Serving = false;
                p.Velocity = Vector2.zero;
            }
            // the server behind the baseline on the side it serves from, the receiver on the side the serve comes to, the
            // net players at the net (the players' a step to the side of their back player, out of the camera's line)
            int r = 1 - s;
            Place(Server, Court.RightSign(s) * serveSide * 0.9f, Court.Side(s) * 12.4f);
            Place(Back(r), Court.RightSign(r) * serveSide * 2.2f, Court.Side(r) * HomeDepth);
            for (int team = 0; team < 2; team++)
            {
                if (IsHuman(team)) SetHomes(team, Back(team).Position);
                if (!NetPlayer(team).Active) continue;   // singles: nobody at the net
                if (IsHuman(team)) Place(NetPlayer(team), _netHome[team].x, _netHome[team].y);
                else Place(NetPlayer(team), -Court.RightSign(team) * (s == team ? serveSide : -serveSide) * 1.6f, Court.Side(team) * NetDepth);
            }
            Server.Serving = true;
            double earliest = Time + (Server.Controller >= 0 ? 0.9 : 1.4);
            if (Practice && PracticeToss != null) earliest = Math.Max(Time + 0.5, PracticeToss(earliest + TossApex) - TossApex);
            _tossAt = earliest;
        }

        static void Place(CourtPlayer p, float x, float z)
        {
            p.Position = p.Target = new Vector2(x, z);
            p.RunAt = 0;
        }

        Vector3 HandPosition(CourtPlayer p) =>
            new Vector3(p.Position.x - p.RacketSide * 0.25f, TossHeight - 0.35f, p.Position.y + Court.Side(1 - p.Team) * 0.22f);

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
            if (server.Controller < 0)
            {
                _cpuServeAt = TossTop + Gauss() * 0.04;
                ShowSwing(server, Stroke.Overhead, _cpuServeAt, true, ShotKind.Serve, point: TossAt(_cpuServeAt));
            }
            Raise(new MatchEvent { Kind = MatchEventKind.Toss, Team = ServerTeam, Player = server.Index, Position = hand });
        }

        void Caught()
        {
            if (Server.Controller >= 0) TossesMissed++;
            Raise(new MatchEvent { Kind = MatchEventKind.Caught, Team = ServerTeam, Player = Server.Index, Position = Ball.Position });
            Phase = MatchPhase.Serve;
            BallLive = false;
            _tossAt = Time + 0.9;
            if (Practice && PracticeToss != null) _tossAt = Math.Max(Time + 0.5, PracticeToss(_tossAt + TossApex) - TossApex);
        }

        void EndPoint(int winner, PointReason reason)
        {
            _endPending = false;
            if (Phase == MatchPhase.PointOver || Phase == MatchPhase.Over) return;
            Phase = MatchPhase.PointOver;
            Next.Active = false;
            _heldHit = _heldSoft = false;
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

        /// <summary>A ball got past the receiving team: the point is the hitter's, but if players receive it only once the
        /// camera has caught up with this moment (a swing from just before it may still arrive).</summary>
        void EndPointUnlessHit(int winner, PointReason reason)
        {
            if (_endPending) return;
            int receiver = LastHitter < 0 ? 1 - winner : 1 - LastHitter;
            if (!IsHuman(receiver) || SeenUntilTeam(receiver) >= _ballTime)
            {
                EndPoint(winner, reason);
                return;
            }
            _endPending = true;
            _endWinner = winner;
            _endTeam = receiver;
            _endReason = reason;
            _endBallTime = _ballTime;
            _endAt = Time + HitLag;
        }

        /// <summary>The camera has told everything about player <paramref name="slot"/> up to this match time (or will never
        /// tell more).</summary>
        double SeenUntil(int slot) => Math.Max(_sawUntil[Mathf.Clamp(slot, 0, 1)], _ballTime - HitLag);

        /// <summary>The same for every player of a team.</summary>
        double SeenUntilTeam(int team)
        {
            double seen = double.PositiveInfinity;
            foreach (var p in Players)
                if (p.Team == team && p.Controller >= 0) seen = Math.Min(seen, SeenUntil(p.Controller));
            return double.IsPositiveInfinity(seen) ? _ballTime : seen;
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
                bool inside = LastWasServe ? Court.InServiceBox(Ball.Position, receiver, DeuceCourt) : Court.InCourt(Ball.Position, receiver, !Singles);
                Raise(new MatchEvent { Kind = MatchEventKind.Bounce, Team = side, Player = -1, Position = Ball.Position, In = inside });
                if (!inside) EndPoint(receiver, PointReason.Out);
                else if (rolling) EndPointUnlessHit(LastHitter, PointReason.Winner);
                return;
            }
            if (_bouncesOnReceiver == 2) Raise(new MatchEvent { Kind = MatchEventKind.Bounce, Team = side, Player = -1, Position = Ball.Position, In = true });
            EndPointUnlessHit(LastHitter, PointReason.Winner);
        }

        // ------------------------------------------------------------------------------------------------ the player's swings

        /// <summary>Player 1 swung (<see cref="Swing(int, double, float, Vector2, bool, bool)"/>).</summary>
        public SwingResult Swing(double time, float speed, Vector2 direction, bool racketHand = true, bool gentle = false) =>
            Swing(0, time, speed, direction, racketHand, gentle);

        /// <summary>
        /// Player <paramref name="slot"/> swung (a quick move of either hand): <paramref name="time"/> is when the camera
        /// saw it, in match time (earlier than now by the camera's lag); <paramref name="speed"/> torso lengths per
        /// second; <paramref name="direction"/> on the player's own (mirrored) screen: x = their right, y = up;
        /// <paramref name="racketHand"/>: the hand holding the racket (the other one's swings up or down are nothing);
        /// <paramref name="gentle"/>: a small, slow move (<see cref="SwingEvent.Gentle"/>, like a flick of a Wii remote).
        /// Serving, a first swing tosses the ball and a swing while it's up serves (any way). In a rally a sideways swing,
        /// or the racket hand's up, hits when the ball is near enough to whichever of the slot's players meets it, gentle or
        /// not; a strong swing down misses it at once; a swing toward the ball's side before it's close is the backswing,
        /// which holds a hit if the ball is in reach (the stroke after it hits instead). The slot's toys swing with the
        /// player (the hit timed to the ball), except for a gentle move that hits nothing: anywhere but at a ball in reach
        /// the hand is just moving (2026-11-06: small moves count now, and the toys mustn't twitch with every one of
        /// them).
        /// </summary>
        public SwingResult Swing(int slot, double time, float speed, Vector2 direction, bool racketHand = true, bool gentle = false)
        {
            time = Math.Min(time, Time);
            SawUntil(slot, time);
            float vertical = Mathf.Clamp(direction.y, -1f, 1f);
            bool steep = Mathf.Abs(vertical) >= VerticalLimit;
            // the shot's power: how fast the hand went sideways; the toys' strokes are drawn as quick as the hand went
            float power = Mathf.InverseLerp(SlowSwing, FastSwing, speed * Mathf.Abs(direction.x));
            float tempo = Mathf.InverseLerp(SlowSwing, FastSwing, speed);
            var server = Server;
            if (server.Controller == slot && (Phase == MatchPhase.Serve || Phase == MatchPhase.Toss) && server.Serving)
            {
                if (Phase == MatchPhase.Serve)
                {
                    // a first swing tosses the ball (like Wii's flick up), unless the toss is planned (practice); the
                    // racket goes back behind the head by itself while it's up
                    if (!(Practice && PracticeToss != null) && Time - (_tossAt - 0.9) > 0.3) Toss();
                    return SwingResult.Toss;
                }
                double sinceToss = time - _tossStart;
                if (sinceToss < 0.12) return SwingResult.Early;
                if (sinceToss > TossCatch - 0.12) return SwingResult.Late;
                // a serve is swung any way (an overhead is up and down): its power is the hand's whole speed
                HumanServe(time, Mathf.InverseLerp(SlowSwing, FastSwing, speed), Math.Abs(time - TossTop) <= PowerServeWindow,
                           (float)((time - TossTop) / 0.45));
                return SwingResult.Serve;
            }

            if (Phase == MatchPhase.Rally && Next.Active && (!Next.Taken || _heldSoft) && Next.Hitter.Controller == slot && LastHitter == 1 - Next.Team)
            {
                var c = Next;
                double e = c.Time - time;   // > 0: the ball is still coming
                Window(c, out double early, out double late);
                bool inReach = e <= early && -e <= late;
                bool up = vertical >= VerticalLimit;
                if (steep && !up)
                {
                    // down: the racket hand's strong swing misses a ball in reach at once (a hit already held stands);
                    // anything else (the free hand, a gentle move, a ball still far) is just the arm moving
                    if (!racketHand || gentle || speed < VerticalMissSpeed || _heldSoft) return SwingResult.NoBall;
                    if (!inReach) return AirSwing(slot, direction, tempo, SwingResult.NoBall);
                    return VerticalMiss(slot, c, direction, tempo);
                }
                // up: the racket hand hits too (2026-11-07, "the upwards moves should count as well"): a low-to-high drive
                // as fast as the hand (topspin), a lob only from a gentle scoop straight up (evening: "too many lobs"); the
                // free hand going up (balance, a wave) is nothing
                if (up && !racketHand) return SwingResult.NoBall;
                if (up)
                {
                    power = Mathf.InverseLerp(SlowSwing, FastSwing, speed);
                    if (!gentle || vertical < LobFrom) vertical = Mathf.Min(vertical, 0.5f);
                }
                // the racket going back toward the ball's side while it's still coming: getting ready (the toy's racket is
                // back already). In reach it holds a hit, which the stroke after it replaces: the player may well have
                // swung at the ball (their sense of its side is the court's, the toy's where it stands), and a backhand's
                // backswing is long
                bool towardTheBall = !up && direction.x * c.Side * Court.RightSign(c.Team) > 0f;
                if (towardTheBall && e > BackswingBefore)
                {
                    if (!inReach) return SwingResult.Backswing;
                    Hold(c, e, early, late, power, vertical, tempo, soft: true);
                    return SwingResult.Held;
                }
                // too early (a swing long before the ball is no attempt at it: the arm coming back, getting ready); a gentle
                // move out of reach is just the hand moving
                if (e > early)
                    return gentle ? SwingResult.NoBall
                        : AirSwing(slot, direction, tempo, Whiff(slot, e <= early + 0.6 ? SwingResult.Early : SwingResult.NoBall));
                if (-e > late) return gentle || _heldSoft ? SwingResult.NoBall : AirSwing(slot, direction, tempo, Whiff(slot, SwingResult.Late));
                Hold(c, e, early, late, power, vertical, tempo, soft: false);
                AirSwing(slot, direction, tempo, SwingResult.Hit, except: c.Hitter);   // a doubles partner swings along
                if (_ballTime < c.Time)
                {
                    // early: the racket waits for the ball, and reaches into it where it is met (if its toy gets there)
                    _heldHit = true;
                    ShowSwing(c.Hitter, c.Stroke, c.Time, true, KindFor(c, power, vertical), tempo,
                              point: c.Reachable ? c.Point : (Vector3?)null);
                    return SwingResult.Hit;
                }
                // a late swing meets the ball a little past the player (at most 0.6 m: the racket's reach), however late
                // it reached the game; the toy reaches for it there (not when it stands too far: that ball goes by)
                double latest = c.Time + 0.6 / Math.Max(4f, BallSpeedAt(c));
                double launch = Math.Max(c.Time, Math.Min(Math.Min(time, latest), _ballTime));
                bool near = Vector2.Distance(c.Hitter.Position, c.Spot) <= ReachTolerance;
                ShowSwing(c.Hitter, c.Stroke, Time, true, KindFor(c, power, vertical), tempo,
                          point: near ? LaunchPoint(launch) : (Vector3?)null);
                return HumanHit(launch) ? SwingResult.Hit : SwingResult.TooFar;
            }
            if ((steep && !racketHand) || gentle) return SwingResult.NoBall;
            // a hit already held (an early swing's, waiting for the ball): its toy keeps its stroke
            var keep = Phase == MatchPhase.Rally && Next.Active && Next.Taken && _heldHit && Next.Hitter.Controller == slot ? Next.Hitter : null;
            return AirSwing(slot, direction, tempo, Whiff(slot, SwingResult.NoBall), except: keep);
        }

        /// <summary>
        /// A swing in reach takes the ball: its timing (-1 = as early as counts .. +1 as late), whether it's perfect (right
        /// on time and strong: <see cref="PerfectPower"/>; a lob never), its power and tilt. A <paramref name="soft"/> one
        /// (a backswing toward the ball's side) only holds it: it hits once the camera has shown <see cref="HeldSeen"/>
        /// past the ball's arrival unless a stroke came, its timing kept within the window's early 60 % (never wide).
        /// </summary>
        void Hold(Contact c, double e, double early, double late, float power, float vertical, float tempo, bool soft)
        {
            c.Taken = true;
            float timing = (float)(e > 0 ? -e / early : -e / late);
            _heldTiming = soft ? Mathf.Max(-0.6f, timing) : timing;
            _heldPerfect = !soft && vertical < LobFrom && power >= PerfectPower && Math.Abs(e) * BallSpeedAt(c) <= PerfectReach;
            _heldPower = power;
            _heldVertical = vertical;
            _heldTempo = tempo;
            _heldSoft = soft;
            _heldHit = soft;   // a firm one is held only when it came early (the caller says)
        }

        /// <summary>
        /// The racket swung up or down at a ball in reach: it misses it at once (the toy swings up or down past it), the
        /// ball goes on; a volley let through is still the back player's after the bounce (like one the net player
        /// couldn't reach).
        /// </summary>
        SwingResult VerticalMiss(int slot, Contact c, Vector2 direction, float tempo)
        {
            c.Active = false;
            Stats[slot].Vertical++;
            AirSwing(slot, direction, tempo, SwingResult.Vertical);
            Raise(new MatchEvent { Kind = MatchEventKind.Vertical, Team = c.Team, Player = c.Hitter.Index, Position = BallPosition });
            if (c.Volley) PlanAfterVolley();
            return SwingResult.Vertical;
        }

        /// <summary>
        /// The slot's toys swing with the player, at once and as fast as the hand went (but <paramref name="except"/>, the
        /// one hitting the ball): a swing across toward the side away from the racket is a forehand, toward the racket's
        /// side a backhand, up a scoop, down a chop. Returns <paramref name="result"/>.
        /// </summary>
        SwingResult AirSwing(int slot, Vector2 direction, float tempo, SwingResult result, CourtPlayer except = null)
        {
            bool steep = Mathf.Abs(direction.y) >= VerticalLimit;
            foreach (var p in Players)
            {
                if (p.Controller != slot || p == except) continue;
                Stroke stroke = steep ? (direction.y < 0f ? Stroke.Overhead : Stroke.Scoop)
                    : direction.x * (p.RightHanded ? 1f : -1f) < 0f ? Stroke.Forehand : Stroke.Backhand;
                ShowSwing(p, stroke, Time, false, ShotKind.Flat, tempo, quick: true);
            }
            return result;
        }

        /// <summary>How long before and after the ball's arrival a swing counts: the ball within
        /// <see cref="EarlyReach"/> / <see cref="LateReach"/> of it, and at least <see cref="MinEarly"/> /
        /// <see cref="MinLate"/> seconds (a slow ball early in a rally stays near for longer).</summary>
        public void Window(Contact c, out double early, out double late)
        {
            float speed = Mathf.Max(4f, BallSpeedAt(c));
            float scale = IsHuman(1) ? 1f : Mathf.Max(1f, Level.WindowScale);   // longer against the gentle computer opponents
            early = Math.Max(MinEarly, EarlyReach / speed) * scale;
            late = Math.Max(MinLate, LateReach / speed) * scale;
        }

        /// <summary>The ball's speed (m/s, along the court) where it is met.</summary>
        float BallSpeedAt(Contact c)
        {
            var b = BallPhysics.At(_path, c.Time);
            return new Vector2(b.Velocity.x, b.Velocity.z).magnitude;
        }

        /// <summary>A swing too early or too late for the ball coming (a swing between balls is just a swing: the arm
        /// coming back after a hit, a wave).</summary>
        SwingResult Whiff(int slot, SwingResult result)
        {
            if (result is not (SwingResult.Early or SwingResult.Late)) return result;
            Stats[slot].Whiffs++;
            Raise(new MatchEvent { Kind = MatchEventKind.Whiff, Team = Next.Team, Player = Next.Hitter.Index, Position = BallPosition });
            return result;
        }

        /// <summary>The player's hit (now, or when the ball arrives for an early swing): only if the hitter stands near
        /// enough.</summary>
        bool HumanHit(double launchTime)
        {
            var c = Next;
            var stats = Stats[Mathf.Max(0, c.Hitter.Controller)];
            _heldHit = _heldSoft = false;
            _endPending = false;
            // (a ball of the computer's sure part is always got to: a toy a little late still reaches it)
            bool sure = _sureBall && IsHuman(c.Team) && !IsHuman(1 - c.Team);
            if (!sure && Vector2.Distance(c.Hitter.Position, c.Spot) > ReachTolerance)
            {
                // in time, but too far away: the racket swings past the ball, which goes on
                c.Active = false;
                stats.TooFar++;
                c.Hitter.DiveAt = c.Time;
                Raise(new MatchEvent { Kind = MatchEventKind.TooFar, Team = c.Team, Player = c.Hitter.Index, Position = c.Point });
                if (c.Volley) PlanAfterVolley();
                return false;
            }
            var from = LaunchPoint(launchTime);
            c.Active = false;
            stats.Hits++;
            if (c.Volley) stats.Volleys++;
            if (_heldPerfect) stats.Perfect++;
            var shot = HumanShot(c, from, _heldTiming, _heldPower, _heldVertical, _heldPerfect);
            Launch(c.Team, c.Hitter, from, shot);
            return true;
        }

        /// <summary>Where the ball in play was (or is) at match time <paramref name="time"/>: where a hit then sends it
        /// from.</summary>
        Vector3 LaunchPoint(double time) => time <= _ballTime ? BallPhysics.At(_path, time).Position : Ball.Position;

        /// <summary>The shot a swing makes: tilted up topspin, a gentle scoop straight up a lob (<see cref="Swing"/> keeps
        /// every other swing up under it), down a slice.</summary>
        ShotKind KindFor(Contact c, float power, float vertical)
        {
            if (c.Smash) return ShotKind.Smash;
            if (c.Volley) return ShotKind.Volley;
            if (vertical >= LobFrom) return ShotKind.Lob;
            if (vertical < -0.42f) return ShotKind.Slice;
            if (vertical > 0.3f) return ShotKind.Topspin;
            return ShotKind.Flat;
        }

        /// <summary>The player's shot from their swing: aimed by when it came (early pulls across), powered by its speed;
        /// mistimed (<see cref="MistimedFrom"/>) it loses power, so it's easier to return.</summary>
        Shot HumanShot(Contact c, Vector3 from, float timing, float power, float vertical, bool perfect)
        {
            var kind = KindFor(c, power, vertical);
            power *= Mathf.Lerp(1f, MistimedPower, Mathf.InverseLerp(MistimedFrom, 1f, Mathf.Abs(timing)));
            float x = Mathf.Lerp(from.x, 0f, 0.55f) + c.Side * timing * 3.6f * WidthScale;
            float speed = SpeedFor(kind, power);
            if (perfect) speed *= 1.15f;
            var target = Target(1 - c.Team, x, DepthFor(kind, power));
            // the strongest (and two players) punish the window's very edge: way too early or late goes wide (Wii); never
            // a smash or a lob
            if (Mathf.Abs(timing) > WideFrom && kind is not (ShotKind.Smash or ShotKind.Lob) && !SureRally)
                target.x = Mathf.Sign(c.Side * timing) * (HalfWidth + 0.5f + 0.6f * (Mathf.Abs(timing) - WideFrom));
            return new Shot { Kind = kind, Team = c.Team, Stroke = c.Stroke, Speed = speed, Power = power, Timing = timing, Perfect = perfect, Target = target };
        }

        /// <summary>A shot of the player's this weak (<see cref="Shot.Power"/>) or a lob is one the computer may attack.</summary>
        public float WeakShot = 0.35f;

        /// <summary>A gentle swing (<see cref="SwingEvent.Gentle"/>) steeper than this (its vertical share) lobs; every other
        /// swing up is a topspin drive (2026-11-07 evening: lobs from every swing up were too many).</summary>
        public float LobFrom = 0.9f;

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
            ShotKind.Lob => 9.8f + 1f * power,
            ShotKind.Volley => 6.5f + 3f * power,
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

        /// <summary>A bounce spot on <paramref name="team"/>'s half, kept inside the lines in play (singles or doubles).</summary>
        Vector3 Target(int team, float x, float depth) =>
            new Vector3(Mathf.Clamp(x, -HalfWidth + 0.45f, HalfWidth - 0.45f), 0f,
                        Court.Side(team) * Mathf.Clamp(depth, 1.5f, Court.HalfLength - 0.4f));

        void HumanServe(double time, float power, bool perfect, float timing)
        {
            var server = Server;
            int s = server.Team, r = 1 - s;
            var from = TossAt(time);
            var kind = perfect ? ShotKind.PowerServe : ShotKind.Serve;
            // early toward the middle line, late out wide (the deuce box is on the receiver's right)
            float across = Mathf.Lerp(0.6f, Court.SinglesHalfWidth - 0.5f, Mathf.InverseLerp(-1f, 1f, timing));
            float x = Court.RightSign(r) * (DeuceCourt ? 1f : -1f) * across;
            float speed = perfect ? 21f + 2f * power : 14f + 5f * power;
            ShowSwing(server, Stroke.Overhead, Time, true, kind, power, point: from);
            TossesMissed = 0;
            server.Serving = false;
            var shot = new Shot { Kind = kind, Team = s, Stroke = Stroke.Overhead, Speed = speed, Perfect = perfect, Timing = timing,
                                  Target = new Vector3(x, 0f, Court.Side(r) * (Court.ServiceLine - 0.9f)) };
            Launch(s, server, from, shot);
        }

        Vector3 TossAt(double time)
        {
            float t = (float)Math.Max(0.0, time - _tossStart);
            var hand = HandPosition(Server) + Vector3.up * 0.35f;
            return hand + Vector3.up * (TossSpeed * t - 0.5f * BallPhysics.Gravity * t * t);
        }

        // ------------------------------------------------------------------------------------------------ hitting and planning

        /// <summary>A shot leaves <paramref name="from"/>: aimed at its own speed (<see cref="Shot.Speed"/>, the arc), then
        /// flown at the rally's pace (<see cref="PaceFor"/>: the same arc, slower at the serve, quicker every hit).</summary>
        void Launch(int team, CourtPlayer hitter, Vector3 from, Shot shot, bool intoNet = false)
        {
            float spin = intoNet ? 0f : SpinFor(shot.Kind);
            var v = intoNet ? BallPhysics.IntoTheNet(from, shot.Target, shot.Speed)
                            : BallPhysics.Aim(from, shot.Target, shot.Speed, spin, ClearanceFor(shot.Kind), out _);
            float pace = PaceFor(RallyHits);
            shot.Pace = pace;
            shot.Speed *= pace;
            Ball = new BallState(from, v * pace, spin, pace);
            BallLive = true;
            Phase = MatchPhase.Rally;
            LastHitter = team;
            LastWasServe = shot.Kind is ShotKind.Serve or ShotKind.PowerServe;
            LastShot = shot;
            _bouncesOnReceiver = 0;
            _lastHitTime = _ballTime;
            _netMissed = false;
            RallyHits++;
            foreach (var p in Players)
            {
                p.Serving = false;
                p.Hurry = 0f;
            }
            Raise(new MatchEvent { Kind = MatchEventKind.Hit, Team = team, Player = hitter.Index, Position = from, Shot = shot });
            Plan(1 - team);
            int other = 1 - team;
            // the hitter's team goes back to cover the court (the players' two by themselves, like the computer's)
            if (IsHuman(team)) SetHomes(team, Cover(team, shot.Target.x));
            else Back(team).Target = Cover(team, shot.Target.x);
            if (!Singles && IsHuman(team) && !IsHuman(other) && (!Next.Active || Next.Hitter != NetPlayer(other)))   // the computer's net player shadows the shot
                NetPlayer(other).Target = new Vector2(Mathf.Clamp(CrossingX(Court.Side(other) * NetDepth, NetPlayer(other).Position.x) * 0.6f, -2.6f, 2.6f),
                                                      Court.Side(other) * NetDepth);
        }

        /// <summary>Where the ball in flight crosses depth <paramref name="z"/> (x), or <paramref name="otherwise"/>.</summary>
        float CrossingX(float z, float otherwise)
        {
            for (int i = 1; i < _path.Count; i++)
            {
                float a = _path[i - 1].Ball.Position.z, b = _path[i].Ball.Position.z;
                if ((a - z) * (b - z) <= 0f) return _path[i].Ball.Position.x;
            }
            return otherwise;
        }

        /// <summary>How far ahead a shot's path is predicted (s): a serve at the slowest pace takes ~3 s to its second
        /// bounce.</summary>
        const float PredictSeconds = 8f;

        /// <summary>
        /// Plans the receiving team's touch on the ball just hit: who meets it, where, and whether they get there. Nothing
        /// to plan for a ball going into the net or out (the receivers let it go: the point is theirs).
        /// </summary>
        void Plan(int r)
        {
            BallPhysics.Predict(Ball, _ballTime, PredictSeconds, _path, maxBounces: 2);
            Next.Active = false;
            Next.Team = r;
            Next.Taken = Next.SwingShown = Next.Missed = false;
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
                       (LastWasServe ? Court.InServiceBox(landing, r, DeuceCourt) : Court.InCourt(landing, r, !Singles));
            if (!_landsIn) return;
            if (IsHuman(r)) PlanHuman(r);
            else PlanCpu();
        }

        /// <summary>
        /// Where on the predicted path a net player standing at <paramref name="x"/> volleys the ball (index, or -1): the
        /// ball crossing its depth before bouncing, within <paramref name="reach"/> sideways and between
        /// <see cref="VolleyLow"/> and <see cref="VolleyHigh"/> (a lob goes over). Never a serve. Only points from
        /// <paramref name="after"/> on.
        /// </summary>
        int VolleyAt(int team, float x, float reach, double after)
        {
            if (LastWasServe || _bounce < 0) return -1;
            float side = Court.Side(team);
            for (int i = 1; i < _bounce; i++)
            {
                var p = _path[i].Ball.Position;
                if (p.z * side < NetDepth - 0.15f) continue;
                if (_path[i].Time < after) return -1;
                if (Mathf.Abs(p.x - x) <= reach && p.y >= VolleyLow && p.y <= VolleyHigh) return i;
                return -1;
            }
            return -1;
        }

        /// <summary>
        /// A players' team's touch, planned as the ball leaves the other side: the net player volleys a ball passing
        /// within its reach (if it gets there in time), else the back player meets it after the bounce. The hitter sets off
        /// for the spot beside the ball after its reaction and runs there by itself (<see cref="MovePlayers"/>);
        /// <see cref="Contact.Reachable"/> says whether it makes it. <paramref name="seenComing"/>: planned again after a
        /// volley went by (the back player has seen the ball coming: no reaction).
        /// </summary>
        void PlanHuman(int team, bool seenComing = false)
        {
            var net = NetPlayer(team);
            var back = Back(team);
            double SetOff(CourtPlayer p) => _ballTime + (seenComing ? 0.0 : p.Reaction);
            int at = Practice || _netMissed || Singles ? -1 : VolleyAt(team, net.Position.x, NetReach, _ballTime + 0.05);
            // a volley the net player can't get to in time (a quick ball) is the back player's after the bounce
            if (at >= 0 && !Reaches(net, Beside(net, team, _path[at].Ball.Position, true, out _), SetOff(net), _path[at].Time)) at = -1;
            var hitter = at >= 0 ? net : back;
            if (at < 0) at = ContactAt(_path, _bounce, team, HomeDepth, _ballTime + 0.05);
            if (at < 0)
            {
                Next.Active = false;
                return;
            }
            var point = _path[at].Ball.Position;
            bool volley = hitter == net;
            var spot = Beside(hitter, team, point, volley, out float side);
            Next.Active = true;
            Next.Hitter = hitter;
            Next.Time = _path[at].Time;
            Next.Point = point;
            Next.Side = side;
            Next.Volley = volley;
            Next.Smash = point.y >= SmashHeight;
            Next.Stroke = Next.Smash ? Stroke.Overhead : side * hitter.RacketSide > 0f ? Stroke.Forehand : Stroke.Backhand;
            Next.Spot = spot;
            hitter.RunAt = SetOff(hitter);
            Next.Reachable = Practice || Reaches(hitter, spot, hitter.RunAt, Next.Time);
            if (!Next.Reachable && _sureBall)
            {
                // a ball of the computer's sure part: the toy gets there (quicker than its difficulty's speed if it must)
                Hurry(hitter, Vector2.Distance(hitter.Position, spot), (float)(Next.Time - hitter.RunAt) - RunMargin);
                Next.Reachable = true;
            }
        }

        /// <summary>The fastest a toy runs to get to a sure ball (m/s): a sprint, not a teleport.</summary>
        public const float HurryLimit = 18f;

        /// <summary><paramref name="p"/> runs <paramref name="distance"/> m in <paramref name="seconds"/> for the ball in play
        /// (<see cref="CourtPlayer.Hurry"/>, until the next shot).</summary>
        static void Hurry(CourtPlayer p, float distance, float seconds) =>
            p.Hurry = Mathf.Min(HurryLimit, Mathf.Max(p.MaxSpeed, distance / Mathf.Max(0.12f, seconds) * 1.1f));

        /// <summary>Where a player stands to meet the ball at <paramref name="point"/>: beside it on the racket's side if
        /// it's there or nearly (else the backhand's), a little behind it (a volley at the net player's depth, a ground
        /// stroke between <see cref="NearestToNet"/> and <see cref="BackLimit"/>). <paramref name="side"/> = the ball's side
        /// of the player (+1 = +x).</summary>
        static Vector2 Beside(CourtPlayer p, int team, Vector3 point, bool volley, out float side)
        {
            side = (point.x - p.Position.x) * p.RacketSide >= -0.3f ? p.RacketSide : -p.RacketSide;
            float behind = Court.Side(team);   // away from the net
            var spot = new Vector2(point.x - side * (volley ? 0.6f : 0.75f), point.z + behind * 0.22f);
            spot.y = volley ? behind * NetDepth : behind * Mathf.Clamp(spot.y * behind, NearestToNet, BackLimit);
            return spot;
        }

        /// <summary>Whether <paramref name="p"/>, setting off at <paramref name="setOff"/>, gets to
        /// <paramref name="spot"/> by <paramref name="time"/> (ball times) at its speed.</summary>
        bool Reaches(CourtPlayer p, Vector2 spot, double setOff, double time) =>
            setOff + Vector2.Distance(p.Position, spot) / Mathf.Max(0.1f, p.MaxSpeed) + RunMargin <= time;

        /// <summary>A volley went by (missed or too far): the back player may still play it after the bounce.</summary>
        void PlanAfterVolley()
        {
            _netMissed = true;
            Next.Taken = Next.SwingShown = Next.Missed = false;
            PlanHuman(Next.Team, seenComing: true);
        }

        void PlanCpu()
        {
            int at = Practice || Singles ? -1 : VolleyAt(1, CpuNet.Position.x, 1.3f, _ballTime + 0.05);
            bool volley = at >= 0;
            var hitter = volley ? CpuNet : Cpu;
            if (!volley) at = ContactAt(_path, _bounce, 1, HomeDepth, _ballTime);
            if (at < 0) return;
            var point = _path[at].Ball.Position;
            Next.Active = true;
            Next.Hitter = hitter;
            Next.Time = _path[at].Time;
            Next.Point = point;
            Next.Volley = volley;
            Next.Smash = volley && point.y >= SmashHeight;
            Next.Stroke = Next.Smash ? Stroke.Overhead : StrokeFor(hitter, point, hitter.Position);
            Next.Spot = SpotFor(hitter, Next.Stroke, point);
            hitter.Target = Next.Spot;
            float run = Vector2.Distance(hitter.Position, Next.Spot);
            float time = (float)(Next.Time - _ballTime) - Level.Reaction * (volley ? 0.5f : 1f);
            Next.Reachable = Practice || run / hitter.MaxSpeed <= time;
            // the sure part of a point: it gets there (quicker if it must) and returns the ball
            bool sure = SureRally;
            if (sure && !Next.Reachable)
            {
                Hurry(hitter, run, time);
                Next.Reachable = true;
            }
            _cpuReturns = Practice || sure || (Next.Reachable && Chance(ReturnChance(run, volley)));
        }

        /// <summary>
        /// Where on a predicted path team <paramref name="r"/>'s back player, standing <paramref name="depth"/> metres
        /// behind the net, meets the ball after the bounce (index, or -1): as it reaches their depth (if a racket reaches it
        /// there), comes down to waist height or gets <see cref="BackLimit"/> deep. Only points from
        /// <paramref name="after"/> on (the future).
        /// </summary>
        static int ContactAt(List<PathPoint> path, int bounce, int r, float depth, double after)
        {
            float side = Court.Side(r);
            if (bounce < 0) return -1;
            for (int i = bounce + 1; i < path.Count; i++)
            {
                var b = path[i].Ball;
                if (path[i].Bounces >= 2) break;
                if (path[i].Time < after) continue;
                float deep = b.Position.z * side;
                if ((deep >= depth - 0.2f && b.Position.y >= 0.3f && b.Position.y <= HighestContact) ||
                    (b.Velocity.y <= 0f && b.Position.y <= ContactHeight) || deep >= BackLimit)
                    return i;
            }
            for (int i = path.Count - 1; i > bounce; i--)
                if (path[i].Time >= after && path[i].Bounces < 2) return i;
            return -1;
        }

        /// <summary>A computer's player stands beside the ball on the racket's side, a little behind it.</summary>
        static Vector2 SpotFor(CourtPlayer p, Stroke stroke, Vector3 ball)
        {
            float racket = stroke == Stroke.Backhand ? -p.RacketSide : p.RacketSide;
            float beside = stroke == Stroke.Overhead ? 0.25f : 0.7f;
            return new Vector2(ball.x - racket * beside, ball.z + Court.Side(p.Team) * 0.3f);
        }

        /// <summary>The computer's chance to get this ball back: its level, less for fast, perfect, smashed or far balls
        /// (later players cope with those better: <see cref="TennisLevel.Pressure"/>). Serves come back more easily than
        /// rally balls (a power serve less so): a player who only serves shouldn't win. A volley at the net has less time.</summary>
        float ReturnChance(float run, bool volley)
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
                if (volley) hard += 0.08f;
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
            if (!IsHuman(c.Team))
            {
                if (!c.SwingShown && _ballTime >= c.Time - 0.32)
                {
                    c.SwingShown = true;
                    if (!c.Reachable) c.Hitter.DiveAt = c.Time;
                    else ShowSwing(c.Hitter, c.Stroke, _cpuReturns ? c.Time : c.Time + 0.14, _cpuReturns, ShotKind.Flat,
                                   point: _cpuReturns ? c.Point : (Vector3?)null);
                }
                if (_ballTime >= c.Time)
                {
                    c.Active = false;
                    if (_cpuReturns && c.Reachable) CpuHit(c);
                }
                return;
            }
            if (_heldHit && _heldSoft && _ballTime >= c.Time && SeenUntil(c.Hitter.Controller) >= c.Time + HeldSeen)
            {
                // a held backswing and no stroke after it: it hits, from where the ball was met (the toy swings through it)
                bool near = Vector2.Distance(c.Hitter.Position, c.Spot) <= ReachTolerance;
                ShowSwing(c.Hitter, c.Stroke, Time, true, KindFor(c, _heldPower, _heldVertical), _heldTempo,
                          point: near ? LaunchPoint(c.Time) : (Vector3?)null);
                HumanHit(c.Time);
                return;
            }
            if (_heldHit && !_heldSoft && _ballTime >= c.Time)
            {
                HumanHit(c.Time);
                return;
            }
            // gone by: the camera has shown everything up to past the latest moment a swing still counts, and none came
            Window(c, out _, out double late);
            if (!c.Taken && !c.Missed && SeenUntil(c.Hitter.Controller) > c.Time + late + 0.05)
            {
                if (c.Volley)
                {
                    // the net player let it through: the back player may still play it after the bounce (forgiving)
                    PlanAfterVolley();
                    return;
                }
                c.Missed = true;
                Stats[Mathf.Max(0, c.Hitter.Controller)].Missed++;
                if (!c.Reachable) c.Hitter.DiveAt = c.Time;
                Raise(new MatchEvent { Kind = MatchEventKind.Missed, Team = c.Team, Player = c.Hitter.Index, Position = Ball.Position });
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
                                  Target = Target(0, _backHome[0].x + (float)(_rng.NextDouble() * 3.0 - 1.5), 9.6f) };
                if (PracticeContact != null && !Feed(from, ref shot, out double wait))
                {
                    _feedPending = true;
                    _feedAt = _ballTime + wait;
                    _feedFrom = from;
                    _feedShot = shot;
                    Next.Active = false;
                    return;
                }
            }
            else
            {
                ShotKind kind;
                double roll = _rng.NextDouble();
                // the sure part of a point (its first returns): back within the player's reach, in, no attack
                bool sure = SureRally;
                // a weak ball of the player's (soft, mistimed, a lob) invites an attack: quick and deep into the far corner
                // (the stronger the opponent, the more often)
                var last = LastShot;
                bool attack = !sure && !c.Volley && IsHuman(last.Team) && !LastWasServe && (last.Kind == ShotKind.Lob || last.Power < WeakShot)
                              && Chance(Level.Attack);
                bool shortBall = !c.Volley && !attack && Chance(Level.ShortChance);
                if (c.Volley) kind = c.Smash ? ShotKind.Smash : ShotKind.Volley;
                else if (attack) kind = ShotKind.Flat;
                else if (shortBall) kind = ShotKind.Slice;
                else kind = roll < Level.LobChance ? ShotKind.Lob : roll < Level.LobChance + 0.2 ? ShotKind.Slice : roll < Level.LobChance + 0.55 ? ShotKind.Topspin : ShotKind.Flat;
                // into the corner away from the player's back player, or back toward them
                float x;
                var back = Human.Position;
                if (attack)
                {
                    float away = Mathf.Abs(back.x) < 0.3f ? (_rng.NextDouble() < 0.5 ? -1f : 1f) : -Mathf.Sign(back.x);
                    x = away * (HalfWidth - 0.35f);
                }
                else if (!sure && Chance(Level.AimAway))
                {
                    float away = Mathf.Abs(back.x) < 0.5f ? (_rng.NextDouble() < 0.5 ? -1f : 1f) : -Mathf.Sign(back.x);
                    x = away * (3.2f + (float)_rng.NextDouble() * 1.5f) * WidthScale;
                }
                else x = back.x + (float)(_rng.NextDouble() * 2.6 - 1.3) * WidthScale;
                x += Gauss() * Level.Wobble * 3f;
                float power = attack ? 1f : Mathf.Clamp01(Mathf.InverseLerp(11f, 19f, Level.Speed) + Gauss() * 0.15f);
                float depth = shortBall ? 4.2f + (float)_rng.NextDouble() * 1.6f : DepthFor(kind, power);
                // past the player's net player: aimed away from it or lobbed over it (later players more often)
                if (!sure && !Singles && !c.Volley && kind != ShotKind.Lob && Mathf.Abs(Mathf.Lerp(from.x, x, CrossShare(from.z, depth)) - HumanNet.Position.x) < NetReach
                    && Chance(0.3f + 0.5f * Level.AimAway))
                {
                    if (Chance(0.35f)) kind = ShotKind.Lob;
                    else x = HumanNet.Position.x + Mathf.Sign(x - HumanNet.Position.x == 0f ? 1f : x - HumanNet.Position.x) * (NetReach + 1.2f);
                }
                float speed = kind is ShotKind.Lob ? SpeedFor(kind, power)
                    : kind is ShotKind.Volley or ShotKind.Smash ? Level.Speed + 2f + (kind == ShotKind.Smash ? 4f : 0f)
                    : attack ? Level.Speed * 1.15f + 1.5f
                    : Level.Speed * (kind == ShotKind.Slice ? (shortBall ? 0.6f : 0.85f) : kind == ShotKind.Topspin ? 0.95f : 1f) + Gauss() * 1.2f;
                if (kind == ShotKind.Lob) depth = DepthFor(kind, power);
                var target = Target(0, x, depth);
                // a mistake now and then (less often returning a serve: a player who only serves shouldn't win on them)
                if (!sure && Chance(Level.Error * (LastWasServe ? 0.35f : 1f)))
                {
                    // a mistake: long, wide or into the net
                    double which = _rng.NextDouble();
                    if (which < 0.4) target.z = -(Court.HalfLength + 0.8f + (float)_rng.NextDouble());
                    else if (which < 0.75) target.x = Mathf.Sign(x == 0f ? 1f : x) * (HalfWidth + 0.6f + (float)_rng.NextDouble() * 0.8f);
                    else net = true;
                }
                shot = new Shot { Kind = kind, Team = 1, Stroke = c.Stroke, Speed = speed, Power = power, Target = target };
                _sureBall = sure;
                CpuReturnsThisPoint++;
            }
            Launch(1, c.Hitter, from, shot, intoNet: net);
        }

        /// <summary>How far along a shot from depth <paramref name="fromZ"/> to <paramref name="depth"/> on the player's
        /// half it passes the player's net player (0..1).</summary>
        static float CrossShare(float fromZ, float depth) => Mathf.Clamp01((fromZ - -NetDepth) / Mathf.Max(0.5f, fromZ + depth));

        /// <summary>Practice: a feed to the player that reaches them when <see cref="PracticeContact"/> wants it (as close
        /// as a speed between <see cref="SlowestFeed"/> and 20 m/s can). False when even the slowest would come too early:
        /// then it should leave <paramref name="wait"/> seconds later.</summary>
        bool Feed(Vector3 from, ref Shot shot, out double wait)
        {
            wait = 0;
            float spin = SpinFor(ShotKind.Flat);
            var target = shot.Target;
            float pace = PaceFor(RallyHits);   // as Launch will fly it
            double ContactAtSpeed(float speed)
            {
                var v = BallPhysics.Aim(from, target, speed, spin, ClearanceFor(ShotKind.Flat), out _);
                BallPhysics.Predict(new BallState(from, v * pace, spin, pace), _ballTime, PredictSeconds, _scratch, 2);
                int bounce = _scratch.FindIndex(p => p.Event == BallEvent.Bounce);
                int at = ContactAt(_scratch, bounce, 0, HomeDepth, _ballTime);
                return at < 0 ? _scratch[_scratch.Count - 1].Time : _scratch[at].Time;
            }
            float fast = 20f, slow = SlowestFeed;
            double want = PracticeContact(ContactAtSpeed(fast));
            shot.Speed = slow;
            double slowest = ContactAtSpeed(slow);
            if (slowest <= want)
            {
                wait = want - slowest;
                return wait < 0.02;
            }
            for (int i = 0; i < 14; i++)
            {
                float mid = (fast + slow) * 0.5f;
                if (ContactAtSpeed(mid) > want) slow = mid;
                else fast = mid;
            }
            shot.Speed = (fast + slow) * 0.5f;
            return true;
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
            _sureBall = SureRally;   // the player's toy gets to the computer's serve too
            var shot = new Shot { Kind = power ? ShotKind.PowerServe : ShotKind.Serve, Team = 1, Stroke = Stroke.Overhead, Speed = speed,
                                  Target = new Vector3(x, 0f, Court.Side(0) * (Court.ServiceLine - 0.9f)) };
            Launch(1, Cpu, from, shot);
        }

        // ------------------------------------------------------------------------------------------------ helpers

        /// <summary>Tells the view to draw a swing (<see cref="CourtPlayer.Swings"/> counts them): its racket meets the ball
        /// (or the air) at <paramref name="contact"/>, as hard as <paramref name="power"/>; a <paramref name="quick"/> one
        /// goes from wherever the racket is straight to the hit; with <paramref name="point"/> (a hit) the racket's head
        /// meets the ball there.</summary>
        static void ShowSwing(CourtPlayer p, Stroke stroke, double contact, bool hits, ShotKind kind, float power = 0.5f, bool quick = false,
                              Vector3? point = null)
        {
            p.Swings++;
            p.SwingStroke = stroke;
            p.SwingContact = contact;
            p.SwingHits = hits;
            p.SwingKind = kind;
            p.SwingPower = Mathf.Clamp01(power);
            p.SwingQuick = quick;
            p.SwingReaches = point.HasValue;
            p.SwingPoint = point ?? Vector3.zero;
        }

        /// <summary>Everyone runs to their targets (a player who has just seen a shot leave the other side sets off after
        /// its reaction: <see cref="CourtPlayer.RunAt"/>). The players' two by themselves, like the computer's: the one
        /// meeting the next ball to the spot beside it, the other (and the hitter after its shot) to where they wait
        /// (<see cref="SetHomes"/>); a serving back player stays put, and between points everyone stays where they are.</summary>
        void MovePlayers(float dt)
        {
            foreach (var p in Players)
            {
                // a ball coming to them, or their own toss in the air: the racket goes back (behind the head to serve; a
                // held backswing keeps it there)
                bool tossing = Phase == MatchPhase.Toss && p == Server;
                p.Preparing = tossing || Phase == MatchPhase.Rally && Next.Active && Next.Hitter == p && (!Next.Taken || _heldSoft) && Next.Time - Time <= 0.6;
                if (p.Preparing) p.PrepareStroke = tossing ? Stroke.Overhead : Next.Stroke;
            }
            for (int team = 0; team < 2; team++)
            {
                var back = Back(team);
                var net = NetPlayer(team);
                if (!IsHuman(team))
                {
                    if (Phase != MatchPhase.Rally && !net.Serving && Phase != MatchPhase.PointOver)
                        net.Target = new Vector2(net.Target.x, Court.Side(team) * NetDepth);
                    continue;
                }
                if (Phase is MatchPhase.PointOver or MatchPhase.Over) continue;   // cheering or sulking where they are
                // (an early swing's hit waiting for the ball keeps its hitter on the spot: walking off it, the hit came
                // out "too far")
                bool ours = Phase == MatchPhase.Rally && Next.Active && Next.Team == team && (!Next.Taken || _heldHit) && !Next.Missed;
                if (back.Serving) back.Target = back.Position;
                else back.Target = ours && Next.Hitter == back ? Next.Spot : _backHome[team];
                net.Target = ours && Next.Hitter == net ? Next.Spot : _netHome[team];
            }
            foreach (var p in Players)
            {
                if (!p.Active) continue;
                var to = p.Target - p.Position;
                float dist = to.magnitude;
                bool reacting = Time < p.RunAt;   // has just seen the shot: not off yet
                float top = Mathf.Max(p.MaxSpeed, p.Hurry);
                var want = dist > 1e-3f && !reacting ? to / dist * Mathf.Min(top, dist * 6f) : Vector2.zero;
                p.Velocity = Vector2.MoveTowards(p.Velocity, want, (p.Hurry > p.MaxSpeed ? 90f : 60f) * dt);
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
