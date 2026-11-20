using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Boxing
{
    /// <summary>The hero's punches as the camera tells them apart (by the fist's way): straight (a jab at the TV, any
    /// punch that isn't the other two), a hook (across), an uppercut (up).</summary>
    public enum Blow { Straight, Hook, Uppercut }

    public enum Outcome
    {
        Hit,
        /// <summary>Into the guard: a little damage (the chip) at most.</summary>
        Blocked,
        /// <summary>The head (or the body) wasn't where the punch went: no damage, the attacker is open (staggered).</summary>
        Dodged,
        /// <summary>A punch that met nothing (the opponent slipped it).</summary>
        Missed,
        /// <summary>A punch that landed in the opponent's wind-up and stopped its attack.</summary>
        Interrupted,
        /// <summary>A fireball punched back at its thrower.</summary>
        Returned,
    }

    public enum BoutPhase
    {
        /// <summary>"Round N", the bell.</summary>
        Starting,
        Fighting,
        /// <summary>Someone is down and the referee counts (<see cref="Bout.DownFighter"/>).</summary>
        Down,
        /// <summary>Between rounds, in the corners.</summary>
        Rest,
        Over,
    }

    public enum Ending { None, KO, TKO, Decision }

    /// <summary>What the opponent is doing (the view poses it from this and the time in it).</summary>
    public enum Foe
    {
        /// <summary>In its stance behind <see cref="Bout.Guard"/>, bobbing, deciding.</summary>
        Ready,
        /// <summary>The tell: its arm drawn back for <see cref="Bout.Attack"/>.</summary>
        WindUp,
        /// <summary>The punch on its way to <see cref="PlannedAttack.Aim"/>.</summary>
        Strike,
        /// <summary>The punch at the hero, the result waiting for the camera's news (a hitstop: at most
        /// <see cref="Bout.MaxWait"/>).</summary>
        Impact,
        /// <summary>After its punch: open for a moment.</summary>
        Recover,
        /// <summary>Its punch met nothing: off balance, open, punches count nearly double.</summary>
        Staggered,
        /// <summary>Just hit.</summary>
        Hurt,
        /// <summary>Dizzy after a combo: stars round its head, open, punches count half again.</summary>
        Dizzy,
        /// <summary>Covering up after punches in a row: both gloves tight in front of the head and the body; only an
        /// uppercut gets between them (and breaks the cover).</summary>
        Covering,
        /// <summary>Its gloves snapped to where a punch was going.</summary>
        Blocking,
        /// <summary>Its head moved aside: punches at the head miss.</summary>
        Slipping,
        /// <summary>Showing off after landing a punch: open.</summary>
        Taunt,
        /// <summary>Throwing a fireball (the final villain).</summary>
        Throwing,
        /// <summary>Waiting while the hero's super punch comes.</summary>
        Frozen,
        Down,
        GettingUp,
        Out,
        Won,
        Lost,
    }

    public enum HeroState { Fighting, Super, Down, GettingUp, Out, Won, Lost }

    /// <summary>What the player's body shows in one camera sample (or the keys held).</summary>
    public struct HeroPose
    {
        /// <summary>Both gloves up in front of the face.</summary>
        public bool Guard;
        /// <summary>Leaning or stepped to a side, as a call: -1 = the screen's left (the player's own left, mirrored), 1 =
        /// right (the moves reader's threshold; <see cref="HeadX"/> is where the head really is).</summary>
        public int Lean;
        public bool Duck;
        /// <summary>Both arms straight up: the super punch, when the power is full.</summary>
        public bool ArmsUp;
        /// <summary>The head's place against where the player stands (metres in the fight): sideways (the screen's right
        /// +, a lean and a step), and how far it went down (a duck).</summary>
        public float HeadX, HeadDrop;
        /// <summary>The hips' sideways place (a step moves them, a lean hardly): where body blows land.</summary>
        public float BodyX;

        public Vector2 Head => new Vector2(HeadX, -HeadDrop);

        public static HeroPose None => default;

        /// <summary>The keys' pose: a lean (or a step) or a duck moves the head as far as a real one does.</summary>
        public static HeroPose Keys(bool guard, int lean, bool duck) => new HeroPose
        {
            Guard = guard, Lean = lean, Duck = duck,
            HeadX = lean * KeyMove, HeadDrop = duck ? KeyMove : 0f, BodyX = lean * KeyMove * 0.8f,
        };

        /// <summary>How far a key's lean or duck moves the head (m).</summary>
        public const float KeyMove = 0.26f;
    }

    /// <summary>The opponent's attack under way.</summary>
    public struct PlannedAttack
    {
        public Attack Attack;
        /// <summary>The side it comes from (hooks, haymakers, lightning): -1 = the screen's left (the hero's left: the
        /// opponent's right hand), 1 = right; 0 = straight.</summary>
        public int Side;
        /// <summary>A counter or a combo's next punch: a short tell.</summary>
        public bool Quick;
        /// <summary>More punches after this one (a combo).</summary>
        public int Follow;
        /// <summary>Where it goes: the hero's head (body blows: the hips, x only) as the opponent expects it (metres from
        /// where the hero stands, x = the screen's right, y up), fixed at <see cref="Committed"/>.</summary>
        public Vector2 Aim;
        /// <summary>When its aim was fixed (match time): moving the head after it takes it out of the punch's way.</summary>
        public double Committed;
        public bool Special => Campaign.IsSpecial(Attack);
        public bool Body => Attack == Attack.Body;
    }

    public enum BoutEventKind
    {
        RoundStart, RoundEnd, Rest,
        /// <summary>The hero threw a punch (it lands <see cref="Bout.PunchTravel"/> later).</summary>
        Thrown,
        /// <summary>The hero's punch arrived: <see cref="BoutEvent.Outcome"/>.</summary>
        Landed,
        WindUp, Strike,
        /// <summary>The opponent's punch arrived: <see cref="BoutEvent.Outcome"/>.</summary>
        Impact,
        GuardChange, Dizzy, Taunt,
        /// <summary>The opponent covers up after punches in a row.</summary>
        Covers,
        /// <summary>The opponent blocks or slips the hero's punch on its way (<see cref="BoutEvent.Outcome"/>: blocked or
        /// missed is what it tries).</summary>
        Defends,
        Knockdown, Count, GotUp, KO, TKO, Decision,
        PowerFull, SuperStart, SuperHit,
        FireballThrown,
        /// <summary>The fireball arrived: hit, dodged, or punched back (<see cref="Outcome.Returned"/>: it flies back).</summary>
        FireballAt,
        /// <summary>A returned fireball hit its thrower.</summary>
        FireballBack,
        Over,
    }

    public struct BoutEvent
    {
        public BoutEventKind Kind;
        /// <summary>Who it is about: 0 = the hero, 1 = the opponent (the one knocked down, hit, getting up).</summary>
        public int Fighter;
        public Outcome Outcome;
        public Attack Attack;
        public int Side;
        public Blow Blow;
        public bool Right, Body;
        public float Damage;
        /// <summary>A counter (into a staggered or taunting opponent, or stopping its wind-up), or an uppercut through its
        /// cover.</summary>
        public bool Counter;
        /// <summary>An uppercut that broke the opponent's cover.</summary>
        public bool CoverBroken;
        public int Combo, Count, Round;
        public Ending Ending;
        public bool HeroWon;
        /// <summary>The opponent's punch: where it went, where the hero's head was (metres).</summary>
        public Vector2 Aim, Head;
    }

    /// <summary>
    /// One fight: the hero (the player) against an opponent of the climb, Wii Sports Boxing's rules made for a camera and
    /// children (2026-11-19 overhaul, user: "fun like the Wii box game; don't over-AI it, keep it well designed"). Pure
    /// logic: <see cref="Step"/> runs it; the game feeds the camera's news with the match time it saw it
    /// (<see cref="Punch"/>, <see cref="Pose"/>, <see cref="SawUntil"/>), tests feed simulated players.
    /// <list type="bullet">
    /// <item><b>Punching</b> (any quick move of a fist, <see cref="BoutMoves"/>): a punch lands <see cref="PunchTravel"/>
    /// after the camera's news, on what the opponent is doing then. Its gloves stop what they cover (high: the head, but an
    /// uppercut comes up between them; low: the body); everything else lands. Into a stagger, a taunt or a dizzy head it
    /// counts more; into its wind-up it may stop the attack. Punches in a row: it covers up (only an uppercut gets
    /// through, and breaks the cover), then dizzy (<see cref="BoxingLevel.ComboToDizzy"/>).</item>
    /// <item><b>Defending</b>: its punches are physical, aimed at where it sees the head (the hips for a body blow),
    /// fixed a moment into the wind-up (<see cref="PlannedAttack.Committed"/>); what the camera saw just before the impact
    /// decides (<see cref="BoxingLevel.DefenceWindow"/>): the head out of the punch's way = dodged (Wii's slow-motion
    /// moment: the opponent staggers, open for a counter); gloves up = blocked (not an uppercut, not a power punch). The
    /// impact waits up to <see cref="MaxWait"/> for the camera's news (a hitstop): the box's camera lag never decides.</item>
    /// <item><b>The opponent decides</b> (Bout.Brain.cs): it sees the hero with a reaction time and picks its punch for
    /// the opening (the body or an uppercut against raised gloves, a hook at the side the hero leans to, an uppercut at a
    /// ducker), throws combos, blocks or slips some punches and hits back. Each opponent fights its own way
    /// (<see cref="OpponentProfile"/>).</item>
    /// <item><b>Rounds</b>: <see cref="BoxingLevel.Rounds"/> of <see cref="BoxingLevel.RoundSeconds"/> (the clock stops
    /// while someone is down). Health at zero = a knockdown and the referee's count; the opponent gets up or not by how
    /// many times it has been down, the hero gets up by punching quickly before ten. Three knockdowns in a round = TKO;
    /// after the last round, a decision.</item>
    /// <item><b>The super punch</b>: dodges, blocks and counters fill the hero's power; full, both arms up = the hero rises
    /// and dives at the opponent.</item>
    /// </list>
    /// </summary>
    public sealed partial class Bout
    {
        public readonly BoxingLevel Level;
        public OpponentProfile Opponent => Level.Opponent;
        readonly System.Random _rng;

        /// <summary>The hero's punch lands this long after the game learns of it (s): the glove's way to the opponent (the
        /// camera already saw the fist get fast, early in the real punch).</summary>
        public static float PunchTravel(Blow blow) => blow switch { Blow.Hook => 0.13f, Blow.Uppercut => 0.14f, _ => 0.1f };
        /// <summary>The longest the opponent's punch waits at the hero for the camera's news (s).</summary>
        public const float MaxWait = 0.25f;
        /// <summary>The defence that counts is the one the camera saw this long before the impact and earlier (s): the
        /// player's move must start before the glove arrives, not after.</summary>
        public const float DecideLead = 0.05f;
        public const float HeroMax = 100f;
        /// <summary>Hero punches landed within this of each other (s) make a combo.</summary>
        public const float ComboGap = 1.2f;
        public const float HurtTime = 0.3f, BlockTime = 0.18f, SlipTime = 0.4f, CoverTime = 1.15f, TauntTime = 1.0f,
                           FallTime = 1.1f, GetUpTime = 1.0f;
        public const float SuperRise = 0.55f, SuperDive = 0.35f;
        /// <summary>A punch this long before a fireball arrives (s) or this long after punches it back.</summary>
        public const float ReturnEarly = 0.45f, ReturnLate = 0.2f;
        /// <summary>A punch landing in a stagger, a taunt, a dizzy head, a wind-up it stopped, through the cover.</summary>
        public const float StaggerHit = 1.8f, TauntHit = 1.5f, DizzyHit = 1.5f, InterruptHit = 1.25f, CoverBreakHit = 1.3f;

        // ------------------------------------------------------------------ the geometry of punches (metres)

        /// <summary>A hook goes this far past its aim (toward the far side) before it ends: leaning away beyond it (or
        /// under it) escapes; a haymaker's swing goes further.</summary>
        public const float HookPast = 0.06f;
        /// <summary>A body blow's reach round its aim (the hips): a step aside escapes, a lean hardly moves them.</summary>
        public const float BodyRadius = 0.15f;

        /// <summary>
        /// Whether the opponent's punch <paramref name="a"/> meets a hero whose head is at <paramref name="head"/> (x = the
        /// screen's right, y up: a duck is negative) and hips at <paramref name="bodyX"/> (metres from where the hero
        /// stands), <paramref name="r"/> being how close to its line it still hits (<see cref="BoxingLevel.HitRadius"/>).
        /// Straight punches (a jab, a cross, a rocket, a fireball) hit round a point; hooks and haymakers sweep across from
        /// their side at a height (under it, or far enough away from their side, escapes); an uppercut comes up a line
        /// (aside escapes, ducking doesn't); a sweep goes across at head height (only under it); lightning strikes its side
        /// (lean away); a body blow the hips.
        /// </summary>
        public static bool Meets(PlannedAttack a, Vector2 head, float bodyX, float r)
        {
            var aim = a.Aim;
            switch (a.Attack)
            {
                case Boxing.Attack.Body:
                    return Mathf.Abs(bodyX - aim.x) <= BodyRadius;
                case Boxing.Attack.Hook:
                case Boxing.Attack.Haymaker:
                {
                    bool big = a.Attack == Boxing.Attack.Haymaker;
                    float rr = big ? r * 1.3f : r, past = big ? HookPast * 1.8f : HookPast;
                    if (head.y < aim.y - rr) return false;   // under it
                    int from = a.Side == 0 ? -1 : a.Side;
                    // it travels away from its side: the head beyond its end on the far side is safe
                    return -from * (head.x - aim.x) <= past + rr;
                }
                case Boxing.Attack.Uppercut:
                    return Mathf.Abs(head.x - aim.x) <= r;
                case Boxing.Attack.Sweep:
                    return head.y >= aim.y - r * 1.2f;
                case Boxing.Attack.Lightning:
                {
                    int from = a.Side == 0 ? -1 : a.Side;
                    return from * (head.x - aim.x) >= -r * 1.2f;
                }
                case Boxing.Attack.Rocket:
                    return (head - aim).magnitude <= r * 1.4f;
                case Boxing.Attack.Fireball:
                    return (head - aim).magnitude <= r * 1.5f;
                default:
                    return (head - aim).magnitude <= r;
            }
        }

        /// <summary>Whether raised gloves stop <paramref name="attack"/>: every ordinary punch but the uppercut (it comes up
        /// between them); power punches go through.</summary>
        public static bool GuardStops(Attack attack) => !Campaign.IsSpecial(attack) && attack != Boxing.Attack.Uppercut;

        /// <summary>
        /// A pose that takes the head (or the hips) out of <paramref name="a"/>'s way by <paramref name="move"/> metres, for
        /// the simulated players and the game playing itself (people learn this from the punches): <paramref name="pick"/>
        /// chooses among the ways out (a lean either way, a duck, a step).
        /// </summary>
        public static HeroPose Escape(PlannedAttack a, float move, int pick)
        {
            int side = a.Side == 0 ? -1 : a.Side;
            int either = (pick & 1) == 0 ? -1 : 1;
            HeroPose Lean(int s) => new HeroPose { Lean = s, HeadX = a.Aim.x + s * move, BodyX = s * move * 0.3f };
            HeroPose Duck() => new HeroPose { Duck = true, HeadDrop = move, HeadX = a.Aim.x * 0.5f };
            HeroPose Step(int s) => new HeroPose { Lean = s, HeadX = a.Aim.x + s * move, BodyX = a.Aim.x + s * move };
            switch (a.Attack)
            {
                case Boxing.Attack.Body: return Step(either);
                case Boxing.Attack.Hook: case Boxing.Attack.Haymaker: return pick % 3 == 2 ? Duck() : Lean(-side);
                case Boxing.Attack.Uppercut: return Lean(either);
                case Boxing.Attack.Sweep: return Duck();
                case Boxing.Attack.Lightning: return Lean(-side);
                default: return pick % 3 == 2 ? Duck() : Lean(either);
            }
        }

        // ------------------------------------------------------------------ the state

        public double Time { get; private set; }
        public BoutPhase Phase { get; private set; } = BoutPhase.Starting;
        public float PhaseTime { get; private set; }
        public int Round { get; private set; } = 1;
        public float RoundClock { get; private set; }
        public Ending Ending { get; private set; }
        public bool HeroWon { get; private set; }

        public float HeroHealth { get; private set; } = HeroMax;
        /// <summary>The super punch's power (0 .. 1).</summary>
        public float Power { get; private set; }
        public HeroState Hero { get; private set; } = HeroState.Fighting;
        public float HeroStateTime { get; private set; }
        public int HeroKnockdowns { get; private set; }
        /// <summary>Getting up: punches thrown so far and needed.</summary>
        public int GetUpProgress { get; private set; }
        public int GetUpNeeded { get; private set; }
        /// <summary>The newest pose the camera told.</summary>
        public HeroPose LastPose { get; private set; }

        public float Health { get; private set; }
        public float MaxHealth => Level.MaxHealth;
        public Foe State { get; private set; } = Foe.Ready;
        /// <summary>How long it has been in <see cref="State"/> and how long that state lasts (s).</summary>
        public float StateTime { get; private set; }
        public float StateLength { get; private set; }
        public Guard Guard { get; private set; } = Guard.High;
        /// <summary>The attack under way (or the last).</summary>
        public PlannedAttack Attack { get; private set; }
        public int Knockdowns { get; private set; }
        /// <summary>The hero's punches landed in a row.</summary>
        public int Combo { get; private set; }
        /// <summary>Who is down (0 hero, 1 opponent, -1 nobody) and the count.</summary>
        public int DownFighter { get; private set; } = -1;
        public int Count { get; private set; }

        /// <summary>A fireball on its way.</summary>
        public bool FireballFlying { get; private set; }
        public double FireballThrown { get; private set; }
        public double FireballArrives { get; private set; }
        public Vector2 FireballAim { get; private set; }

        // stats
        public int Thrown { get; private set; }
        public int Landed { get; private set; }
        public int BlockedPunches { get; private set; }
        public int Counters { get; private set; }
        public int BestCombo { get; private set; }
        public int Dodges { get; private set; }
        public int Blocks { get; private set; }
        public int HitsTaken { get; private set; }
        public int Supers { get; private set; }
        public int FireballsReturned { get; private set; }
        public int CoversBroken { get; private set; }
        /// <summary>The opponent's side: punches it threw (at the head, at the body), its defences that worked, its
        /// counters.</summary>
        public int FoeThrown { get; private set; }
        public int FoeHead { get; private set; }
        public int FoeBody { get; private set; }
        public int FoeDefended { get; private set; }
        public int FoeCounters { get; private set; }

        public readonly List<BoutEvent> Events = new List<BoutEvent>();

        // the camera's news: poses with their match time, how far it has told everything
        readonly List<(double time, HeroPose pose)> _poses = new List<(double, HeroPose)>();
        double _sawUntil = double.NegativeInfinity;
        // the hero's punches on their way (land at "lands"), and every punch's camera time for fireballs
        readonly List<(double lands, double seen, bool right, Blow blow, bool body, float speed)> _punches =
            new List<(double, double, bool, Blow, bool, float)>();
        readonly List<double> _recentPunches = new List<double>();

        float _dizzyCooldown;
        double _lastLanded = double.NegativeInfinity, _impactAt;
        int _roundKnockdownsHero, _roundKnockdownsFoe, _finishingCombo;
        bool _staysDown;
        int _getUpAt;
        float _countTimer;
        // per round: knockdowns inflicted and damage dealt (as shares of the other's health)
        readonly List<(int heroDowns, int foeDowns, float heroShare, float foeShare)> _rounds = new List<(int, int, float, float)>();
        int _roundHeroDowns, _roundFoeDowns;
        float _roundHeroShare, _roundFoeShare;
        bool _powerAnnounced;

        public Bout(BoxingLevel level, int seed = 1)
        {
            Level = level;
            _rng = new System.Random(seed);
            Health = level.MaxHealth;
            RoundClock = level.RoundSeconds;
            InitBrain();
        }

        float Rand() => (float)_rng.NextDouble();

        // ------------------------------------------------------------------ the camera's news

        /// <summary>The hero threw a punch the camera saw at match time <paramref name="time"/>: on the hero's
        /// <paramref name="right"/> or left hand, its kind, at the body or the head, how fast (torso lengths a second).</summary>
        public void Punch(double time, bool right, Blow blow, bool body, float speed)
        {
            _recentPunches.Add(time);
            if (Phase == BoutPhase.Down && DownFighter == 0 && Hero == HeroState.Down)
            {
                // getting up: punches, before ten
                GetUpProgress++;
                return;
            }
            if (Phase != BoutPhase.Fighting || Hero != HeroState.Fighting) return;
            Thrown++;
            _punches.Add((Time + PunchTravel(blow), time, right, blow, body, speed));
            Add(new BoutEvent { Kind = BoutEventKind.Thrown, Fighter = 0, Right = right, Blow = blow, Body = body });
            SeesPunch(right, blow, body);
        }

        /// <summary>What the hero's body showed in a camera sample at match time <paramref name="time"/> (every sample, in
        /// order; keys: every step).</summary>
        public void Pose(double time, HeroPose pose)
        {
            if (_poses.Count > 0 && time < _poses[_poses.Count - 1].time) time = _poses[_poses.Count - 1].time;
            _poses.Add((time, pose));
            LastPose = pose;
            while (_poses.Count > 2 && Time - _poses[1].time > 4.0) _poses.RemoveAt(0);
        }

        /// <summary>The camera has told everything the hero did up to match time <paramref name="time"/>.</summary>
        public void SawUntil(double time)
        {
            if (time > _sawUntil) _sawUntil = time;
        }

        /// <summary>The super punch from a key (the camera's is both arms up, in <see cref="Pose"/>).</summary>
        public bool Super()
        {
            if (Power < 1f || Phase != BoutPhase.Fighting || Hero != HeroState.Fighting) return false;
            if (State is Foe.Down or Foe.GettingUp or Foe.Out) return false;
            Power = 0f;
            _powerAnnounced = false;
            Supers++;
            SetHero(HeroState.Super);
            _punches.Clear();
            FireballFlying = false;
            SetState(Foe.Frozen, SuperRise + SuperDive);
            Add(new BoutEvent { Kind = BoutEventKind.SuperStart, Fighter = 0 });
            return true;
        }

        /// <summary>The pose the camera saw at match time <paramref name="at"/> (the newest one at or before it).</summary>
        HeroPose PoseAt(double at)
        {
            for (int i = _poses.Count - 1; i >= 0; i--)
                if (_poses[i].time <= at) return _poses[i].pose;
            return _poses.Count > 0 ? _poses[0].pose : default;
        }

        // ------------------------------------------------------------------ the clock

        public void Step(float dt)
        {
            if (dt <= 0f) return;
            Time += dt;
            PhaseTime += dt;
            HeroStateTime += dt;
            switch (Phase)
            {
                case BoutPhase.Starting:
                    if (PhaseTime >= BoxingLevel.StartSeconds)
                    {
                        SetPhase(BoutPhase.Fighting);
                        Add(new BoutEvent { Kind = BoutEventKind.RoundStart, Round = Round });
                    }
                    break;
                case BoutPhase.Fighting:
                    Fight(dt);
                    break;
                case BoutPhase.Down:
                    Counting(dt);
                    break;
                case BoutPhase.Rest:
                    if (PhaseTime >= BoxingLevel.RestSeconds)
                    {
                        Round++;
                        RoundClock = Level.RoundSeconds;
                        _roundKnockdownsHero = _roundKnockdownsFoe = 0;
                        SetPhase(BoutPhase.Starting);
                        SetState(Foe.Ready, 0f);
                        SetHero(HeroState.Fighting);
                        RoundBegins();
                    }
                    break;
            }
        }

        void SetPhase(BoutPhase phase)
        {
            Phase = phase;
            PhaseTime = 0f;
        }

        void SetState(Foe state, float length)
        {
            State = state;
            StateTime = 0f;
            StateLength = length;
        }

        void SetHero(HeroState state)
        {
            Hero = state;
            HeroStateTime = 0f;
        }

        void Add(BoutEvent e)
        {
            e.Round = Round;
            Events.Add(e);
        }

        void Fight(float dt)
        {
            StateTime += dt;
            if (Hero == HeroState.Super)
            {
                if (HeroStateTime >= SuperRise + SuperDive) SuperHit();
                return;
            }
            if (Hero == HeroState.GettingUp)
            {
                if (HeroStateTime >= GetUpTime) SetHero(HeroState.Fighting);
                return;
            }
            RoundClock -= dt;
            if (RoundClock <= 0f)
            {
                RoundClock = 0f;
                EndRound();
                return;
            }
            // the camera's super: both arms up with full power
            if (Power >= 1f && LastPose.ArmsUp && Super()) return;

            for (int i = 0; i < _punches.Count; i++)
            {
                if (_punches[i].lands > Time) continue;
                var p = _punches[i];
                _punches.RemoveAt(i--);
                Land(p.right, p.blow, p.body, p.speed);
                if (Phase != BoutPhase.Fighting) return;
            }
            Brain(dt);
            if (Phase == BoutPhase.Fighting) FireballStep();
            while (_recentPunches.Count > 0 && Time - _recentPunches[0] > 3.0) _recentPunches.RemoveAt(0);
        }

        // ------------------------------------------------------------------ the hero's punches

        void Land(bool right, Blow blow, bool body, float speed)
        {
            var e = new BoutEvent { Kind = BoutEventKind.Landed, Fighter = 1, Right = right, Blow = blow, Body = body };
            switch (State)
            {
                case Foe.Down: case Foe.GettingUp: case Foe.Out: case Foe.Won: case Foe.Lost: case Foe.Frozen:
                    return;
            }
            float damage = BoxingLevel.PunchDamage(blow, speed);
            bool head = !body;
            // its defences: a slip takes the head away; the cover holds everything but an uppercut; the guard what it covers
            if (State == Foe.Slipping && head)
            {
                e.Outcome = Outcome.Missed;
                FoeDefended++;
                Add(e);
                return;
            }
            bool breaksCover = false;
            if (State == Foe.Covering)
            {
                if (head && blow == Blow.Uppercut) breaksCover = true;
                else
                {
                    Block(e, damage);
                    return;
                }
            }
            else if (State is Foe.Ready or Foe.Blocking || State == Foe.WindUp && StateTime < StateLength * GuardedWindUp)
            {
                if (Covers(Guard, blow, body))
                {
                    Block(e, damage);
                    return;
                }
            }
            float mult = 1f;
            bool counter = false;
            switch (State)
            {
                case Foe.WindUp:
                    if (!Attack.Special && Rand() < Level.InterruptChance)
                    {
                        mult = InterruptHit;
                        counter = true;
                        e.Outcome = Outcome.Interrupted;
                    }
                    break;
                case Foe.Staggered: mult = StaggerHit; counter = true; break;
                case Foe.Taunt: mult = TauntHit; counter = true; break;
                case Foe.Dizzy: mult = DizzyHit; break;
                case Foe.Covering: mult = CoverBreakHit; counter = true; break;
            }
            damage *= mult;
            Health -= damage;
            Landed++;
            if (counter) Counters++;
            if (breaksCover) CoversBroken++;
            _roundHeroShare += damage / MaxHealth;
            Combo = Time - _lastLanded <= ComboGap ? Combo + 1 : 1;
            _lastLanded = Time;
            BestCombo = Math.Max(BestCombo, Combo);
            AddPower(counter ? Level.PowerCounter : Level.PowerHit);
            e.Damage = damage;
            e.Counter = counter;
            e.CoverBroken = breaksCover;
            e.Combo = Combo;
            if (e.Outcome != Outcome.Interrupted) e.Outcome = Outcome.Hit;
            Add(e);
            if (Health <= 0f)
            {
                Health = 0f;
                KnockDown(1);
                return;
            }
            TookPunch(body, e.Outcome == Outcome.Interrupted, breaksCover);
        }

        /// <summary>Through the first part of its wind-up (this share) it keeps its guard; after it, it's committed to its
        /// punch and open (a punch then may stop it).</summary>
        public const float GuardedWindUp = 0.5f;

        /// <summary>Whether <paramref name="guard"/> stops a punch: a high guard covers the head (an uppercut comes up
        /// between the gloves), a low one the body.</summary>
        public static bool Covers(Guard guard, Blow blow, bool body) =>
            guard == Guard.High ? !body && blow != Blow.Uppercut : guard == Guard.Low && body;

        void Block(BoutEvent e, float damage)
        {
            float chip = damage * 0.08f;
            Health = Mathf.Max(0.5f, Health - chip);
            _roundHeroShare += chip / MaxHealth;
            BlockedPunches++;
            FoeDefended++;
            e.Outcome = Outcome.Blocked;
            e.Damage = chip;
            Add(e);
            PunchBlocked();
        }

        void AddPower(float amount)
        {
            if (Hero != HeroState.Fighting) return;
            Power = Mathf.Min(1f, Power + amount);
            if (Power >= 1f && !_powerAnnounced)
            {
                _powerAnnounced = true;
                Add(new BoutEvent { Kind = BoutEventKind.PowerFull, Fighter = 0 });
            }
        }

        void SuperHit()
        {
            float damage = Level.SuperShare * MaxHealth;
            Health -= damage;
            _roundHeroShare += damage / MaxHealth;
            SetHero(HeroState.Fighting);
            Add(new BoutEvent { Kind = BoutEventKind.SuperHit, Fighter = 1, Damage = damage });
            if (Health <= 0f)
            {
                Health = 0f;
                KnockDown(1);
                return;
            }
            SetState(Foe.Dizzy, Level.DizzyTime);
            Hold(Level.DizzyTime + 0.6f);
        }

        // ------------------------------------------------------------------ its punches arrive

        /// <summary>The impact: where the hero's head (hips) and gloves were just before it decides it.</summary>
        void Resolve()
        {
            var a = Attack;
            double decide = _impactAt - DecideLead;
            var outcome = Defend(a, decide, out int how, out Vector2 head);
            var e = new BoutEvent
            {
                Kind = BoutEventKind.Impact, Fighter = 0, Attack = a.Attack, Side = a.Side, Outcome = outcome, Aim = a.Aim, Head = head,
            };
            switch (outcome)
            {
                case Outcome.Dodged:
                    Dodges++;
                    AddPower(Level.PowerDodge);
                    break;
                case Outcome.Blocked:
                    Blocks++;
                    e.Damage = Level.DamageOf(a.Attack) * Level.BlockChip;
                    Hurt(e.Damage);
                    AddPower(Level.PowerBlock);
                    break;
                default:
                    HitsTaken++;
                    e.Damage = Level.DamageOf(a.Attack);
                    Hurt(e.Damage);
                    Combo = 0;
                    break;
            }
            Add(e);
            if (HeroHealth <= 0f)
            {
                HeroHealth = 0f;
                KnockDown(0);
                return;
            }
            AfterPunch(a, outcome);
        }

        void Hurt(float damage)
        {
            HeroHealth -= damage;
            _roundFoeShare += damage / HeroMax;
        }

        /// <summary>What the hero's defence does against <paramref name="a"/>, judged on what the camera saw from
        /// <see cref="BoxingLevel.DefenceWindow"/> before <paramref name="decide"/> up to it: the head (or the hips) out of
        /// the punch's way at any moment of it = dodged; else gloves up at any moment = blocked (what they stop).
        /// <paramref name="how"/>: 0 the guard, 1 aside, 2 under, -1 nothing; <paramref name="head"/> where the head was
        /// (its escape, if it escaped).</summary>
        Outcome Defend(PlannedAttack a, double decide, out int how, out Vector2 head)
        {
            bool guard = false, escaped = false, ducked = false;
            head = Vector2.zero;
            bool any = false;
            double from = decide - Level.DefenceWindow;
            for (int i = _poses.Count - 1; i >= 0; i--)
            {
                var (t, p) = _poses[i];
                if (t > decide) continue;
                // the pose held at the window's start counts too (the sample before it)
                if (!any) head = p.Head;
                any = true;
                guard |= p.Guard;
                if (!escaped && !Meets(a, p.Head, p.BodyX, Level.HitRadius))
                {
                    escaped = true;
                    head = p.Head;
                    ducked = p.HeadDrop > 0.08f && Mathf.Abs(p.HeadX - a.Aim.x) < Mathf.Max(0.08f, Level.HitRadius);
                }
                if (t <= from) break;
            }
            if (!any && !Meets(a, Vector2.zero, 0f, Level.HitRadius)) escaped = true;
            how = escaped ? (ducked ? 2 : 1) : guard ? 0 : -1;
            if (escaped) return Outcome.Dodged;
            if (guard && GuardStops(a.Attack)) return Outcome.Blocked;
            return Outcome.Hit;
        }

        // ------------------------------------------------------------------ fireballs

        void Throw()
        {
            FireballFlying = true;
            FireballThrown = Time;
            FireballArrives = Time + Level.FireballFlight;
            FireballAim = Attack.Aim;
            SetState(Foe.Throwing, 0.55f);
            Add(new BoutEvent { Kind = BoutEventKind.FireballThrown, Fighter = 1, Attack = Boxing.Attack.Fireball, Aim = Attack.Aim });
            Hold(Level.FireballFlight + 0.4f);
        }

        void FireballStep()
        {
            if (!FireballFlying || Time < FireballArrives) return;
            // wait for the camera's news about the moment it arrives
            if (_sawUntil < FireballArrives + ReturnLate && Time - FireballArrives < MaxWait) return;
            FireballFlying = false;
            bool punched = false;
            foreach (double t in _recentPunches)
                if (t >= FireballArrives - ReturnEarly && t <= FireballArrives + ReturnLate) punched = true;
            var e = new BoutEvent { Kind = BoutEventKind.FireballAt, Fighter = 0, Attack = Boxing.Attack.Fireball, Aim = FireballAim };
            if (punched)
            {
                e.Outcome = Outcome.Returned;
                FireballsReturned++;
                Add(e);
                AddPower(Level.PowerDodge * 1.5f);
                float damage = 0.1f * MaxHealth;
                Health -= damage;
                _roundHeroShare += damage / MaxHealth;
                Add(new BoutEvent { Kind = BoutEventKind.FireballBack, Fighter = 1, Damage = damage, Counter = true });
                if (Health <= 0f)
                {
                    Health = 0f;
                    KnockDown(1);
                    return;
                }
                if (State is Foe.Ready or Foe.Recover or Foe.Throwing) SetState(Foe.Hurt, 0.7f);
                return;
            }
            var a = new PlannedAttack { Attack = Boxing.Attack.Fireball, Aim = FireballAim };
            var outcome = Defend(a, FireballArrives - DecideLead, out _, out var head);
            e.Outcome = outcome == Outcome.Blocked ? Outcome.Hit : outcome;   // gloves don't stop fire
            e.Head = head;
            if (outcome == Outcome.Dodged)
            {
                Dodges++;
                AddPower(Level.PowerDodge);
                Add(e);
                return;
            }
            HitsTaken++;
            e.Damage = Level.DamageOf(Boxing.Attack.Fireball);
            Hurt(e.Damage);
            Add(e);
            if (HeroHealth <= 0f)
            {
                HeroHealth = 0f;
                KnockDown(0);
            }
        }

        // ------------------------------------------------------------------ knockdowns and the count

        void KnockDown(int fighter)
        {
            _punches.Clear();
            FireballFlying = false;
            DownFighter = fighter;
            Count = 0;
            _countTimer = -FallTime;
            SetPhase(BoutPhase.Down);
            if (fighter == 1)
            {
                Knockdowns++;
                _roundFoeDowns++;
                _roundKnockdownsFoe++;
                _finishingCombo = Combo;
                SetState(Foe.Down, 0f);
                // it stays down after enough knockdowns, a knockdown sooner after a big combo (Wii Sports)
                _staysDown = Knockdowns >= Level.DownsToKO || Knockdowns >= Level.DownsToKO - 1 && _finishingCombo >= 6;
                _getUpAt = Math.Min(9, 3 + 2 * Knockdowns + _rng.Next(0, 2));
            }
            else
            {
                HeroKnockdowns++;
                _roundHeroDowns++;
                _roundKnockdownsHero++;
                SetHero(HeroState.Down);
                SetState(Foe.Ready, 0f);
                GetUpProgress = 0;
                GetUpNeeded = Level.GetUpPunches + 2 * (HeroKnockdowns - 1);
            }
            Combo = 0;
            Add(new BoutEvent { Kind = BoutEventKind.Knockdown, Fighter = fighter });
        }

        void Counting(float dt)
        {
            bool tko = DownFighter == 1 ? _roundKnockdownsFoe >= 3 : _roundKnockdownsHero >= 3;
            if (tko)
            {
                if (PhaseTime >= FallTime + 0.6f) Finish(DownFighter == 1, Ending.TKO);
                return;
            }
            float interval = DownFighter == 0 ? Level.HeroCountInterval : BoxingLevel.CountInterval;
            if (DownFighter == 0 && GetUpProgress >= GetUpNeeded && _countTimer >= 0f)
            {
                GetUp(0);
                return;
            }
            _countTimer += dt;
            if (_countTimer < interval) return;
            _countTimer -= interval;
            Count++;
            Add(new BoutEvent { Kind = BoutEventKind.Count, Fighter = DownFighter, Count = Count });
            if (DownFighter == 1 && !_staysDown && Count >= _getUpAt)
            {
                GetUp(1);
                return;
            }
            if (Count >= 10) Finish(DownFighter == 1, Ending.KO);
        }

        void GetUp(int fighter)
        {
            if (fighter == 1)
            {
                Health = MaxHealth * Mathf.Max(0.25f, 0.75f - 0.15f * Knockdowns);
                SetState(Foe.GettingUp, GetUpTime);
                ChangeGuard(Guard.High, 2.5f);
            }
            else
            {
                HeroHealth = HeroMax * Level.HeroGetUpHealth * Mathf.Pow(0.85f, HeroKnockdowns - 1);
                SetHero(HeroState.GettingUp);
                SetState(Foe.Ready, 0f);
            }
            Hold(1.6f);
            DownFighter = -1;
            Count = 0;
            SetPhase(BoutPhase.Fighting);
            Add(new BoutEvent { Kind = BoutEventKind.GotUp, Fighter = fighter });
        }

        void EndRound()
        {
            _punches.Clear();
            FireballFlying = false;
            _rounds.Add((_roundHeroDowns, _roundFoeDowns, _roundHeroShare, _roundFoeShare));
            _roundHeroDowns = _roundFoeDowns = 0;
            _roundHeroShare = _roundFoeShare = 0f;
            Add(new BoutEvent { Kind = BoutEventKind.RoundEnd });
            if (Round >= Level.Rounds)
            {
                Finish(DecisionForHero(), Ending.Decision);
                return;
            }
            // the corners: a breather
            HeroHealth = Mathf.Min(HeroMax, HeroHealth + (HeroMax - HeroHealth) * 0.3f);
            Health = Mathf.Min(MaxHealth, Health + (MaxHealth - Health) * 0.2f);
            SetPhase(BoutPhase.Rest);
            SetState(Foe.Ready, 0f);
            Combo = 0;
            Add(new BoutEvent { Kind = BoutEventKind.Rest });
        }

        /// <summary>After the last round: rounds won by knockdowns, an even round by damage dealt; even, the damage over
        /// the whole fight; even again, the opponent (a draw is no win).</summary>
        public bool DecisionForHero()
        {
            int hero = 0, foe = 0;
            float heroShare = 0f, foeShare = 0f;
            foreach (var r in _rounds)
            {
                heroShare += r.heroShare;
                foeShare += r.foeShare;
                if (r.foeDowns != r.heroDowns)
                {
                    if (r.foeDowns > r.heroDowns) hero++;
                    else foe++;
                }
                else if (Mathf.Abs(r.heroShare - r.foeShare) > 0.02f)
                {
                    if (r.heroShare > r.foeShare) hero++;
                    else foe++;
                }
            }
            if (hero != foe) return hero > foe;
            // even: the hero must have done more (only guarding all fight is no win)
            return heroShare > foeShare + 0.01f;
        }

        void Finish(bool heroWon, Ending ending)
        {
            HeroWon = heroWon;
            Ending = ending;
            _punches.Clear();
            FireballFlying = false;
            SetPhase(BoutPhase.Over);
            if (heroWon)
            {
                SetHero(HeroState.Won);
                SetState(ending == Ending.Decision ? Foe.Lost : Foe.Out, 0f);
            }
            else
            {
                SetHero(ending == Ending.Decision ? HeroState.Lost : HeroState.Out);
                SetState(Foe.Won, 0f);
            }
            Add(new BoutEvent
            {
                Kind = ending == Ending.KO ? BoutEventKind.KO : ending == Ending.TKO ? BoutEventKind.TKO : BoutEventKind.Decision,
                Fighter = heroWon ? 1 : 0, Ending = ending, HeroWon = heroWon,
            });
            Add(new BoutEvent { Kind = BoutEventKind.Over, Ending = ending, HeroWon = heroWon });
        }

        /// <summary>The stars a won fight earns: one for the win, one for never going down, one for a knockout before the
        /// last round.</summary>
        public int Stars
        {
            get
            {
                if (Phase != BoutPhase.Over || !HeroWon) return 0;
                int stars = 1;
                if (HeroKnockdowns == 0) stars++;
                if (Ending != Ending.Decision && Round < Level.Rounds) stars++;
                return stars;
            }
        }

        /// <summary>Test hook: the hero's health and power (a fight set up mid-way).</summary>
        public void SetHero(float health, float power)
        {
            HeroHealth = health;
            Power = power;
        }

        /// <summary>Test hook: the opponent's health.</summary>
        public void SetHealth(float health) => Health = Mathf.Clamp(health, 0.01f, MaxHealth);

        /// <summary>Test hook: the opponent holds <paramref name="guard"/> for a long while and doesn't attack on its own (it
        /// still defends if it can: <see cref="Passive"/> stops that too).</summary>
        public void HoldGuard(Guard guard, float seconds = 100f)
        {
            Guard = guard;
            _guardUntil = Time + seconds;
            Hold(seconds);
        }

        /// <summary>Test hook: the opponent starts <paramref name="attack"/> now (from the fighting stance), aimed where the
        /// hero's head is (or at <paramref name="aim"/>), fixed at once.</summary>
        public void Throw(Attack attack, int side = 0, int follow = 0, Vector2? aim = null)
        {
            if (Phase != BoutPhase.Fighting) return;
            var a = new PlannedAttack { Attack = attack, Side = Campaign.FromSide(attack) ? (side == 0 ? -1 : side) : 0, Follow = follow };
            Begin(a, aim);
        }

        /// <summary>Test hook: skips to the fighting (after the bell).</summary>
        public void Begin()
        {
            while (Phase == BoutPhase.Starting) Step(0.05f);
        }

        /// <summary>Test hook: the round clock.</summary>
        public void SetClock(float seconds) => RoundClock = seconds;
    }
}
