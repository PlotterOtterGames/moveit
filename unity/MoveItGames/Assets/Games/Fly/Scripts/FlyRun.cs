using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Fly
{
    /// <summary>Robots (rush fighters, throwers), small drones flying in swarms, stars, hearts, the boss's fireballs and
    /// the throwers' sparks.</summary>
    public enum ThingKind { Robot, Star, Heart, Fireball, Drone }

    public enum ThingState
    {
        /// <summary>Robots: flying in to their place. Drones: flying at the hero in formation. Stars and hearts:
        /// drifting past with the world. Fireballs and sparks: flying at the hero.</summary>
        Approach,
        /// <summary>A robot at its place (a thrower throwing, a fighter getting ready for its next go).</summary>
        Wait,
        /// <summary>A robot charging in from its side to its spot beside the hero (a punch reaches it on the way).</summary>
        Advance,
        /// <summary>At its spot right beside the hero, about to strike: its eye glows red. Punch it now.</summary>
        Windup,
        /// <summary>Striking at where the hero was when it started (move away to dodge).</summary>
        Lunge,
        /// <summary>Punched but not out (an armoured robot): knocked back a moment, then it comes again at once.</summary>
        Stagger,
        /// <summary>Backing off to its side after striking.</summary>
        Retreat,
        /// <summary>Out: flying off. A fireball or spark punched back flies at whoever threw it.</summary>
        Smashed,
        /// <summary>Finished: the game removes it.</summary>
        Gone,
    }

    public enum FlyPhase
    {
        /// <summary>Flying along the streets (turning at crossroads), collecting stars, punching through drone swarms.</summary>
        Flight,
        /// <summary>"Stop!": the hero brakes, the first robots of the rush come from all sides.</summary>
        Stop,
        /// <summary>Hovering in the street, robots charging in groups from the left, the right, ahead and above.</summary>
        Rush,
        /// <summary>The district's champion (or the boss, its fireballs beaten) comes face to face for the clash.</summary>
        ClashIntro,
        /// <summary>Fist to fist: whoever punches faster pushes the bar to the other side (see <see cref="ClashState"/>).</summary>
        Clash,
        /// <summary>Every third district: the giant robot flies in, then throws fireballs to punch back.</summary>
        BossArrives, Boss,
        /// <summary>The district is saved: a cheer, then the hero flies on to the next one (faster, harder).</summary>
        Cleared,
        Lost,
    }

    public enum FlyEventKind
    {
        DistrictStarted, SwarmComes, DroneSmashed, DroneBumped, SwarmCleared,
        RushStarts, GroupComes, RushCleared,
        RobotAttacks, RobotCharges, RobotHit, RobotSmashed, RobotHitHero, RobotMissed,
        ClashStarts, ClashGo, ClashPunch, ClashRobotPunch, ClashFlurry, ClashLost, ClashWon,
        StarCollected, HeartCollected,
        SparkThrown, BossAppears, BossThrows, FireballReturned, FireballHitHero, BossHit, BossDefeated,
        NewRecord, NewEnemy, Lost, Whiff,
    }

    /// <summary>Kinds of enemies, each shown with a card the first time a run meets it.</summary>
    public enum EnemyKind { Robot, Drone, Armoured, Thrower, Dart, Champion, Boss }

    public struct FlyEvent
    {
        public FlyEventKind Kind;
        /// <summary>The thing it is about (0 = none).</summary>
        public int ThingId;
        /// <summary>Where it happened, in the flight frame (x right, y up, z ahead of the hero; metres).</summary>
        public Vector3 Position;
        public int Points;
        /// <summary>Extra detail: the district for DistrictStarted, the clash's length (s) for ClashWon, the enemy kind
        /// for NewEnemy, robots or drones in a rush, group or swarm.</summary>
        public float Value;
    }

    /// <summary>A robot, drone, star, heart, fireball or spark, in the flight frame: x right, y up (the hero's window), z
    /// metres ahead.</summary>
    public sealed class Thing
    {
        public int Id;
        public ThingKind Kind;
        public ThingState State;
        public float StateTime;
        public Vector3 Position, Velocity;
        /// <summary>Where a striking robot or a fireball is going (the hero's place when it started).</summary>
        public Vector3 Target;
        /// <summary>A robot's place to wait at (a thrower's spot behind, a fighter's spot at its side to back off to).</summary>
        public Vector3 Home;
        /// <summary>A fighter's spot beside the hero (offset from the hero, metres), where it winds up.</summary>
        public Vector2 Spot;
        public int Hp = 1, MaxHp = 1;
        /// <summary>A robot that throws sparks from behind; for a spark, the robot that threw it (0 = the boss).</summary>
        public bool Thrower;
        public int Owner;
        /// <summary>A small, quick robot (it charges in twice as fast).</summary>
        public bool Dart;
        /// <summary>A drone's swarm (counted to see whether all of it was smashed).</summary>
        public int Swarm;
        /// <summary>A thrower's time to its next spark, a fighter's wait before it charges.</summary>
        public float Timer;
        /// <summary>Hit by a punch (or punched back); false for a robot that struck and missed.</summary>
        public bool Punched;
        /// <summary>A robot's spark (smaller and slower than the boss's fireballs).</summary>
        public bool Small;

        /// <summary>An armoured robot: two punches.</summary>
        public bool Big => MaxHp > 1;

        /// <summary>Can a punch hit it, if it is within reach?</summary>
        public bool Punchable => Kind switch
        {
            ThingKind.Robot => State is ThingState.Approach or ThingState.Wait or ThingState.Advance or ThingState.Windup or ThingState.Lunge
                                   or ThingState.Retreat || State == ThingState.Stagger && StateTime > 0.12f,
            ThingKind.Drone => State == ThingState.Approach,
            ThingKind.Fireball => State == ThingState.Approach,
            _ => false,
        };

        /// <summary>Hurts the hero if it reaches them.</summary>
        public bool Dangerous => Kind == ThingKind.Robot ? State == ThingState.Lunge : Kind == ThingKind.Fireball && State == ThingState.Approach;

        /// <summary>A robot coming at the hero right now.</summary>
        public bool Attacking => Kind == ThingKind.Robot && State is ThingState.Advance or ThingState.Windup or ThingState.Lunge;

        public bool Alive => State is not (ThingState.Smashed or ThingState.Gone);
    }

    /// <summary>The giant robot of every third district: it throws fireballs (punch them back); beaten, it comes in for
    /// the final clash.</summary>
    public sealed class Boss
    {
        public int Hp, MaxHp;
        public Vector3 Position;
        public float Time;
        public float ThrowIn;
        /// <summary>Seconds since it was last hit (for a flinch), large when not recently.</summary>
        public float SinceHit = 99f;
    }

    /// <summary>
    /// Fist to fist (Dragon Ball's beam clash, Mario Party's tug of war, as punches): every punch of the hero pushes the
    /// bar toward the champion, every one of the champion's toward the hero. The bar at the champion = knocked out; at the
    /// hero = a heart lost, the bar back to the middle, and on. The champion punches at <see cref="Rate"/> a second, in
    /// bursts now and then (a flurry: punch harder then), and tires after a long clash.
    /// </summary>
    public sealed class ClashState
    {
        /// <summary>-1 (the hero is beaten) .. 0 (even) .. 1 (the champion is knocked out).</summary>
        public float Bar;
        /// <summary>The champion's punches a second (before flurries and tiredness).</summary>
        public float Rate;
        /// <summary>Seconds of clash so far (from "go").</summary>
        public float Time;
        /// <summary>Seconds left of a flurry (it punches faster), and of the hero getting up after losing the bar.</summary>
        public float Flurry, Recover;
        /// <summary>The boss's final clash (else the district's champion).</summary>
        public bool WithBoss;
        /// <summary>Where the champion is (the boss's own position for the boss).</summary>
        public Vector3 Position;
        public bool Won;
        public int HeroPunches, ChampionPunches;
        /// <summary>The champion's punches a second right now.</summary>
        public float CurrentRate;
    }

    /// <summary>How hard a district is. It never stops getting harder (slowly, with limits): the run ends when the hearts
    /// do.</summary>
    public sealed class DistrictSettings
    {
        public int District;
        /// <summary>Flying: speed (m/s), the stretch to the rush (m), drone swarms on the way and drones in each.</summary>
        public float FlightSpeed, FlightMeters;
        public int Swarms, SwarmSize;
        /// <summary>The rush: groups of robots, robots in each group, throwers in the whole rush.</summary>
        public int Groups, GroupSize, Throwers;
        /// <summary>Shares of armoured robots (two punches) and darts (twice as quick).</summary>
        public float ArmouredShare, DartShare;
        /// <summary>How fast fighters charge in (m/s) and how long one winds up beside the hero before it strikes.</summary>
        public float ChargeSpeed, WindupSeconds;
        /// <summary>The champion's punches a second in the clash.</summary>
        public float ClashRate;
        /// <summary>A boss district (every third): fireballs to punch back, then the final clash.</summary>
        public bool Boss;
        public int BossHp;
        public float ThrowEvery, FireballSeconds, DoubleChance;

        public static DistrictSettings For(int district)
        {
            int d = Mathf.Max(1, district), k = d - 1;
            bool boss = d % 3 == 0;
            return new DistrictSettings
            {
                District = d,
                FlightSpeed = Mathf.Min(16f + 0.8f * k, 23f),
                FlightMeters = 150f + 10f * Mathf.Min(k, 5),
                Swarms = d >= 4 ? 3 : 2,
                SwarmSize = Mathf.Min(4 + k / 2, 8),
                Groups = Mathf.Min(2 + k / 3, 6),
                GroupSize = Mathf.Min(3 + k / 3, 7),
                Throwers = d >= 3 ? Mathf.Min(1 + (d - 3) / 3, 3) : 0,
                ArmouredShare = d >= 2 ? Mathf.Min(0.15f + 0.05f * (d - 2), 0.4f) : 0f,
                DartShare = d >= 4 ? Mathf.Min(0.2f + 0.04f * (d - 4), 0.4f) : 0f,
                ChargeSpeed = Mathf.Min(2.6f + 0.2f * k, 5.5f),
                WindupSeconds = Mathf.Max(1f - 0.05f * k, 0.5f),
                ClashRate = Mathf.Min(1.7f + 0.25f * k, 4.2f) + (boss ? 0.4f : 0f),
                Boss = boss,
                BossHp = 3 + d / 3,
                ThrowEvery = Mathf.Max(2.4f - 0.15f * k, 1.4f),
                FireballSeconds = Mathf.Max(2.2f - 0.1f * k, 1.5f),
                DoubleChance = Mathf.Min(0.08f * k, 0.5f),
            };
        }
    }

    /// <summary>
    /// "Fluturo!" without the pictures: an endless punch 'em up in the air, district after district, until the hearts run
    /// out. A district: fly a stretch of street (stars to collect, drone swarms to punch through: each drone drops a
    /// star) → "Ndal!": a rush, robots charging in groups from the left, the right, ahead and above at once (any punch hits
    /// the most urgent robot in reach; one that reaches the hero winds up a moment and strikes) → the clash: the district's
    /// champion face to face, fist to fist, whoever punches faster wins (<see cref="ClashState"/>). Every third district
    /// the boss: punch its fireballs back, then the final clash. Each district is faster and harder, gives a heart back
    /// and meets new kinds of robots. Hits in a row without getting hurt build a combo (more points).
    /// <para>All rules live here (no scene), so tests and simulated players can play whole games; <see cref="FlyGame"/>
    /// draws it and turns the body into steering and punches.</para>
    /// </summary>
    public sealed class FlyRun
    {
        // ---- the world (metres, seconds)
        /// <summary>The hero's window: x within ±HalfWidth, y within ±HalfHeight of the flying height.</summary>
        public float HalfWidth = 2.2f, HalfHeight = 1.2f;
        public float SpawnDistance = 48f;
        /// <summary>Fighters wind up this far in front of the hero, <see cref="SpotRadius"/> to its side, above or ahead.</summary>
        public float StrikeDistance = 2.3f, SpotRadius = 1.15f;
        public float LungeSeconds = 0.3f;
        /// <summary>How quickly the hero follows the steering (per second).</summary>
        public float HeroResponse = 7f;
        /// <summary>Drones fly at the hero this fast (m/s, whatever the hero's speed: they fly too).</summary>
        public float DroneSpeed = 9f;
        // ---- reach (generous: children punch roughly, and a punch should always hit something)
        /// <summary>Things this far ahead (z) can be punched...</summary>
        public float ReachNear = -0.4f, ReachFar = 5.5f;
        /// <summary>...if they are this close to the hero sideways/up-down (every spot is).</summary>
        public float ReachRadius = 1.9f;
        /// <summary>A striking robot, a fireball or a drone hits the hero this close; stars and hearts are collected this close.</summary>
        public float BumpRadius = 0.95f, CollectRadius = 1.05f;
        // ---- the clash
        /// <summary>How far one punch pushes the bar, the hero's and the champion's alike: the faster puncher wins.</summary>
        public float HeroPush = 0.14f, ChampionPush = 0.14f;
        /// <summary>The champion stands this far ahead (the boss further: it's huge).</summary>
        public float ClashDistance = 2.4f, BossClashDistance = 3.4f;
        /// <summary>After this long (s) the champion tires: its punches slow down (no clash lasts for ever).</summary>
        public float TiresAfter = 9f;
        public float ClashIntroSeconds = 1.6f, ClashRecoverSeconds = 1f;
        // ---- the boss
        public float BossDistance = 15f, BossArriveSeconds = 2.5f, ReturnSpeed = 24f;
        // ---- the run
        /// <summary>Hearts at the start (and the most); every district saved gives one back.</summary>
        public int StartHearts = 5;
        public float StopSeconds = 1f, ClearedSeconds = 2.4f;
        /// <summary>A combo lasts while hits come within this long of each other and the hero isn't hurt.</summary>
        public float ComboSeconds = 3f;
        /// <summary>No automatic swarms, rushes, robots, attacks, stars or boss moves (tests, a practice mode): use the methods.</summary>
        public bool Scripted;
        /// <summary>The best score so far (the game sets it): passing it emits <see cref="FlyEventKind.NewRecord"/> once.</summary>
        public int Record;

        public int District { get; private set; }
        public DistrictSettings Settings { get; private set; }
        public FlyPhase Phase { get; private set; }
        public float PhaseTime { get; private set; }
        public int Hearts { get; private set; }
        public int Score { get; private set; }
        public int RobotsSmashed { get; private set; }
        public int DronesSmashed { get; private set; }
        /// <summary>Stars this run: picked up, dropped by drones, won in clashes and from bosses (the outfits' currency).</summary>
        public int StarsCollected { get; private set; }
        public int FireballsReturned { get; private set; }
        public int ClashesWon { get; private set; }
        public int BossesBeaten { get; private set; }
        public int SwarmsCleared { get; private set; }
        /// <summary>Rushes beaten without losing a heart.</summary>
        public int CleanRushes { get; private set; }
        /// <summary>The quickest clash won this run (s), large before one.</summary>
        public float QuickestClash { get; private set; } = 999f;
        public int HitsTaken { get; private set; }
        /// <summary>Hits in a row without being hurt; the best this round.</summary>
        public int Combo { get; private set; }
        public int BestCombo { get; private set; }
        /// <summary>Seconds played (the run's length).</summary>
        public float Seconds { get; private set; }
        /// <summary>How fast the world flies past right now (the route follows it): full speed in flight, braking to a
        /// hover for the rush, the clash and the boss.</summary>
        public float CurrentSpeed { get; private set; }
        /// <summary>Metres flown in this district's flight.</summary>
        public float Distance { get; private set; }
        public Vector2 Hero { get; private set; }
        public Vector2 HeroTarget { get; private set; }
        public Boss Boss { get; private set; }
        /// <summary>The clash under way (or just finished), null otherwise.</summary>
        public ClashState Clash { get; private set; }
        public IReadOnlyList<Thing> Things => _things;
        /// <summary>What happened during the last <see cref="Step"/> (sounds, effects, pop-ups), and anything the Spawn,
        /// Throw, BeginRush, BeginClash or BeginBoss calls since then caused.</summary>
        public IReadOnlyList<FlyEvent> Events => _events;
        /// <summary>This flight's progress to the rush, 0..1; 1 when not flying.</summary>
        public float FlightProgress => Phase == FlyPhase.Flight ? Mathf.Clamp01(Distance / Settings.FlightMeters) : 1f;
        public bool Over => Phase == FlyPhase.Lost;
        /// <summary>Robots of the rush still to beat (in the street or still to come).</summary>
        public int RobotsLeft
        {
            get
            {
                int n = _groupsLeft * Settings.GroupSize + _throwersLeft;
                foreach (var t in _things)
                    if (t.Kind == ThingKind.Robot && t.Alive) n++;
                return n;
            }
        }
        /// <summary>The robots of the whole rush (for the HUD).</summary>
        public int RushSize { get; private set; }

        readonly List<Thing> _things = new List<Thing>();
        readonly List<FlyEvent> _events = new List<FlyEvent>();
        readonly HashSet<EnemyKind> _met = new HashSet<EnemyKind>();
        readonly Dictionary<int, (int total, int smashed, bool spoiled)> _swarms = new Dictionary<int, (int, int, bool)>();
        readonly System.Random _rng;
        int _nextId = 1, _eventsSeen, _swarmsSent, _nextSwarm = 1, _groupsLeft, _rushHits, _throwersLeft;
        float _starIn, _heartIn, _groupIn, _sinceGroup, _sinceHit = 99f, _secondThrowIn = -1f, _championPunchIn, _flurryIn;
        bool _heartSent, _recordSent;

        public FlyRun(int seed = 0) => _rng = new System.Random(seed);

        /// <summary>A new run from district 1 (or <paramref name="district"/>), full hearts, no score.</summary>
        public void Start(int district = 1)
        {
            Hearts = StartHearts;
            Score = RobotsSmashed = DronesSmashed = StarsCollected = FireballsReturned = ClashesWon = BossesBeaten = 0;
            SwarmsCleared = CleanRushes = HitsTaken = Combo = BestCombo = 0;
            QuickestClash = 999f;
            Seconds = 0f;
            Hero = HeroTarget = Vector2.zero;
            CurrentSpeed = 0f;
            District = 0;
            _met.Clear();
            _recordSent = false;
            _events.Clear();
            _eventsSeen = 0;
            StartDistrict(district);
        }

        void StartDistrict(int district)
        {
            if (District > 0) Hearts = Mathf.Min(StartHearts, Hearts + 1);   // a heart back for every district saved
            District = district;
            Settings = DistrictSettings.For(district);
            _things.RemoveAll(t => t.Kind is ThingKind.Robot or ThingKind.Fireball || !t.Alive);
            Boss = null;
            Clash = null;
            Distance = 0f;
            _starIn = 1f;
            _heartIn = 4f;
            _heartSent = false;
            _swarmsSent = 0;
            _groupsLeft = _throwersLeft = 0;
            SetPhase(FlyPhase.Flight);
            Emit(FlyEventKind.DistrictStarted, null, Vector3.zero, value: district);
        }

        void SetPhase(FlyPhase phase)
        {
            Phase = phase;
            PhaseTime = 0f;
        }

        /// <summary>
        /// Advances the game by <paramref name="dt"/> seconds. <paramref name="steer"/> is where the hero should be in its
        /// window, -1..1 each way; <paramref name="punch"/> is true in the step a punch was seen (with
        /// <paramref name="bothHands"/>, both fists at once: two punches).
        /// </summary>
        public void Step(float dt, Vector2 steer, bool punch, bool rightHand = true, bool bothHands = false)
        {
            // forget what the last step reported (events from calls made since then stay for this one)
            _events.RemoveRange(0, Mathf.Min(_eventsSeen, _events.Count));
            _eventsSeen = 0;
            if (dt <= 0f) return;
            StepInner(dt, steer, punch, rightHand, bothHands);
            _eventsSeen = _events.Count;
        }

        void StepInner(float dt, Vector2 steer, bool punch, bool rightHand, bool bothHands)
        {
            PhaseTime += dt;
            _sinceHit += dt;
            if (Phase != FlyPhase.Lost) Seconds += dt;
            if (Combo > 0 && _sinceHit > ComboSeconds) Combo = 0;
            if (Phase != FlyPhase.Lost)
            {
                HeroTarget = new Vector2(Mathf.Clamp(steer.x, -1f, 1f) * HalfWidth, Mathf.Clamp(steer.y, -1f, 1f) * HalfHeight);
                Hero = Vector2.Lerp(Hero, HeroTarget, 1f - Mathf.Exp(-HeroResponse * dt));
            }
            // full speed in flight (and taking off again once a district is saved), a quick brake for everything else
            bool flying = Phase == FlyPhase.Flight || Phase == FlyPhase.Cleared && PhaseTime > 1f;
            float want = flying ? Settings.FlightSpeed : 0f;
            CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, want, (want > CurrentSpeed ? 10f : 16f) * dt);
            if (Phase == FlyPhase.Flight) Distance += CurrentSpeed * dt;

            switch (Phase)
            {
                case FlyPhase.Flight:
                    if (Scripted) break;
                    SpawnPickups(dt);
                    SpawnSwarms();
                    if (Distance >= Settings.FlightMeters) BeginRush();
                    break;
                case FlyPhase.Stop:
                    if (!Scripted) DirectRush(dt);
                    if (PhaseTime >= StopSeconds) SetPhase(FlyPhase.Rush);
                    break;
                case FlyPhase.Rush:
                    if (!Scripted) DirectRush(dt);
                    if (RushOver()) EndRush();
                    break;
                case FlyPhase.ClashIntro:
                case FlyPhase.Clash:
                    UpdateClash(dt, punch, bothHands);
                    break;
                case FlyPhase.BossArrives:
                    Boss.Time += dt;
                    Boss.Position = BossHome(Boss.Time) + Vector3.forward * (40f * Mathf.Pow(1f - Mathf.Clamp01(PhaseTime / BossArriveSeconds), 2f));
                    if (PhaseTime >= BossArriveSeconds) SetPhase(FlyPhase.Boss);
                    break;
                case FlyPhase.Boss:
                    UpdateBoss(dt);
                    break;
                case FlyPhase.Cleared:
                    if (PhaseTime >= ClearedSeconds) StartDistrict(District + 1);
                    break;
            }

            if (punch && Phase is FlyPhase.Flight or FlyPhase.Stop or FlyPhase.Rush or FlyPhase.BossArrives or FlyPhase.Boss)
            {
                Punch(rightHand);
                if (bothHands) Punch(!rightHand);
            }
            UpdateThings(dt);
            if (!_recordSent && Record > 0 && Score > Record)
            {
                _recordSent = true;
                Emit(FlyEventKind.NewRecord, null, Vector3.zero, Score);
            }
        }

        // ---------------------------------------------------------------- flying: stars, hearts, drone swarms

        void SpawnPickups(float dt)
        {
            _starIn -= dt;
            _heartIn -= dt;
            // nothing new in the last stretch: it would still be in the air when the hero stops
            if (Distance > Settings.FlightMeters - 25f) return;
            if (_starIn <= 0f)
            {
                _starIn = 1.7f * Range(0.8f, 1.2f);
                SpawnStarGroup();
            }
            if (_heartIn <= 0f && !_heartSent && Hearts < StartHearts)
            {
                _heartSent = true;
                SpawnHeart(Range(-1.5f, 1.5f), Range(-0.5f, 0.9f));
            }
        }

        /// <summary>A row, an arc or a diagonal of stars: they lead the hero somewhere.</summary>
        void SpawnStarGroup()
        {
            int shape = _rng.Next(3);
            const int count = 5;
            float x0 = Range(-1.6f, 1.6f), y0 = Range(-0.9f, 0.9f), x1 = Range(-1.6f, 1.6f), y1 = Range(-0.9f, 0.9f);
            for (int i = 0; i < count; i++)
            {
                float k = i / (count - 1f);
                float x = shape == 0 ? x0 : Mathf.Lerp(x0, x1, k);
                float y = shape == 1 ? y0 + Mathf.Sin(k * Mathf.PI) * 0.6f : shape == 0 ? y0 : Mathf.Lerp(y0, y1, k);
                SpawnStar(Mathf.Clamp(x, -HalfWidth * 0.9f, HalfWidth * 0.9f), Mathf.Clamp(y, -HalfHeight * 0.9f, HalfHeight * 0.9f), i * 2.2f);
            }
        }

        public Thing SpawnStar(float x, float y, float extraDistance = 0f) =>
            Add(ThingKind.Star, new Vector3(x, y, SpawnDistance + extraDistance));

        public Thing SpawnHeart(float x, float y, float extraDistance = 0f) =>
            Add(ThingKind.Heart, new Vector3(x, y, SpawnDistance + extraDistance));

        Thing Add(ThingKind kind, Vector3 position)
        {
            var t = new Thing { Id = _nextId++, Kind = kind, Position = position, State = ThingState.Approach };
            _things.Add(t);
            return t;
        }

        /// <summary>The flight's swarms, spread along it (none in the last stretch before the rush).</summary>
        void SpawnSwarms()
        {
            if (_swarmsSent >= Settings.Swarms) return;
            float at = (Settings.FlightMeters - 40f) * (_swarmsSent + 0.6f) / Settings.Swarms;
            if (Distance < at) return;
            _swarmsSent++;
            SpawnSwarm((Formation)_rng.Next(5), Settings.SwarmSize);
        }

        public enum Formation { Line, Vee, Column, Ring, Diagonal }

        /// <summary>A swarm of <paramref name="count"/> drones flying at the hero in a formation, centred at (x, y) or
        /// somewhere in its window (also for tests). Punch them as they come; each one drops a star; all of them = a bonus.</summary>
        public void SpawnSwarm(Formation formation, int count, float x = float.NaN, float y = float.NaN)
        {
            int id = _nextSwarm++;
            float cx = float.IsNaN(x) ? Range(-1.3f, 1.3f) : x, cy = float.IsNaN(y) ? Range(-0.6f, 0.7f) : y;
            for (int i = 0; i < count; i++)
            {
                float k = count == 1 ? 0.5f : i / (count - 1f);
                float around = Mathf.PI * 2f * i / count;
                var offset = formation switch
                {
                    Formation.Line => new Vector3((k - 0.5f) * 3f, 0f, 0f),
                    Formation.Vee => new Vector3((k - 0.5f) * 3f, Mathf.Abs(k - 0.5f) * 1.2f, Mathf.Abs(k - 0.5f) * 5f),
                    Formation.Column => new Vector3(0f, 0f, i * 1.6f),
                    Formation.Ring => new Vector3(Mathf.Cos(around) * 1.3f, Mathf.Sin(around) * 0.9f, 0f),
                    _ => new Vector3((k - 0.5f) * 3f, (0.5f - k) * 1.4f, i * 1.1f),
                };
                var d = Add(ThingKind.Drone, new Vector3(Mathf.Clamp(cx + offset.x, -HalfWidth - 0.6f, HalfWidth + 0.6f),
                                                         Mathf.Clamp(cy + offset.y, -HalfHeight - 0.2f, HalfHeight + 0.4f), 32f + offset.z));
                d.Swarm = id;
            }
            _swarms[id] = (count, 0, false);
            Meet(EnemyKind.Drone);
            Emit(FlyEventKind.SwarmComes, null, new Vector3(cx, cy, 32f), value: count);
        }

        /// <summary>
        /// The hero turned into another street (<see cref="FlyRoute"/>): stars and hearts ahead were over the old street
        /// (they would float over the houses now), so they go and new ones come soon; a heart that went comes again.
        /// Robots, drones and fireballs fly: they follow the hero round the corner.
        /// </summary>
        public void Turned()
        {
            int hearts = 0;
            for (int i = _things.Count - 1; i >= 0; i--)
            {
                var t = _things[i];
                if (t.Kind is ThingKind.Star or ThingKind.Heart && t.Position.z > 2f)
                {
                    if (t.Kind == ThingKind.Heart) hearts++;
                    _things.RemoveAt(i);
                }
            }
            if (hearts > 0)
            {
                _heartSent = false;
                _heartIn = Mathf.Min(_heartIn, 2f);
            }
            _starIn = Mathf.Min(_starIn, 0.7f);
        }

        // ---------------------------------------------------------------- the rush

        enum Side { Left, Right, Ahead, Above }

        /// <summary>"Stop!": the hero brakes and the rush begins: <see cref="DistrictSettings.Groups"/> groups of robots
        /// (or <paramref name="groups"/>; 0 for tests that spawn their own).</summary>
        public void BeginRush(int groups = -1)
        {
            _things.RemoveAll(t => t.Kind is ThingKind.Star or ThingKind.Heart or ThingKind.Drone && t.State != ThingState.Smashed);
            _groupsLeft = groups >= 0 ? groups : Settings.Groups;
            _throwersLeft = groups == 0 ? 0 : Settings.Throwers;
            RushSize = _groupsLeft * Settings.GroupSize + _throwersLeft;
            _groupIn = 0.3f;
            _sinceGroup = 0f;
            _rushHits = 0;
            SetPhase(FlyPhase.Stop);
            Emit(FlyEventKind.RushStarts, null, Vector3.forward * 6f, value: RushSize);
        }

        /// <summary>The rush's director: the next group comes when the last one is nearly beaten (or has had its time),
        /// fighters who struck come again after a breather, throwers lob sparks from behind.</summary>
        void DirectRush(float dt)
        {
            _groupIn -= dt;
            _sinceGroup += dt;
            int fighters = 0;
            foreach (var t in _things)
                if (t.Kind == ThingKind.Robot && t.Alive && !t.Thrower) fighters++;
            if (_groupsLeft > 0 && (_groupIn <= 0f || fighters <= 1 && _sinceGroup > 1.2f))
                SpawnGroup();

            for (int i = 0, n = _things.Count; i < n; i++)
            {
                var t = _things[i];
                if (t.Kind != ThingKind.Robot || t.State != ThingState.Wait) continue;
                t.Timer -= dt;
                if (t.Timer > 0f) continue;
                if (t.Thrower && (fighters > 0 || _groupsLeft > 0 || t.StateTime < 6f))
                {
                    // a thrower lobs a spark from behind; once it is the last one left it comes in itself
                    t.Timer = Range(2.6f, 3.6f);
                    ThrowSpark(t);
                }
                else Attack(t);
            }
        }

        /// <summary>A group of robots charging in at once from several sides (also for tests). The rush's throwers come with
        /// the first groups.</summary>
        public void SpawnGroup()
        {
            int count = Settings.GroupSize;
            _groupsLeft = Mathf.Max(0, _groupsLeft - 1);
            _groupIn = 4.5f;
            _sinceGroup = 0f;
            // sides for this group: shuffled, at least two different ones
            var sides = new List<Side>();
            for (int i = 0; i < count; i++) sides.Add((Side)(i % 4));
            for (int i = sides.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (sides[i], sides[j]) = (sides[j], sides[i]);
            }
            var perSide = new Dictionary<Side, int>();
            for (int i = 0; i < sides.Count; i++)
            {
                var side = sides[i];
                perSide.TryGetValue(side, out int already);
                perSide[side] = already + 1;
                bool dart = Settings.DartShare > 0f && Chance(Settings.DartShare);
                bool armoured = !dart && Chance(Settings.ArmouredShare);
                // a new kind shows up for sure in the district that brings it
                if (i == 0 && Settings.DartShare > 0f && !_met.Contains(EnemyKind.Dart)) dart = true;
                else if (i == 0 && Settings.ArmouredShare > 0f && !_met.Contains(EnemyKind.Armoured)) armoured = true;
                // one after another, quickly: a flurry to punch through, not a wall
                SpawnFighter(side, already, armoured ? 2 : 1, dart, i * 0.35f + Range(0f, 0.2f));
            }
            if (_throwersLeft > 0)
            {
                _throwersLeft--;
                SpawnThrower(Range(-1.6f, 1.6f));
            }
            Emit(FlyEventKind.GroupComes, null, Vector3.forward * 4f, value: count);
        }

        /// <summary>A fighter charging in from <paramref name="side"/> after <paramref name="delay"/> s; the
        /// <paramref name="nth"/> from that side takes a spot beside the others.</summary>
        void SpawnFighter(Side side, int nth, int hp, bool dart, float delay)
        {
            float spread = (nth % 2 == 0 ? 1f : -1f) * ((nth + 1) / 2) * 0.5f;   // 0, +0.5, -0.5, +1 ... radians apart
            float angle = side switch { Side.Left => Mathf.PI, Side.Right => 0f, Side.Above => Mathf.PI / 2f, _ => -Mathf.PI / 2f } + spread;
            var spot = side == Side.Ahead
                ? new Vector2(spread * 1.2f, -0.05f)                                       // ahead: right in front
                : new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * SpotRadius;
            var entry = side switch
            {
                Side.Left => new Vector3(-6.5f, Range(-0.6f, 0.8f), Range(1.6f, 3.2f)),
                Side.Right => new Vector3(6.5f, Range(-0.6f, 0.8f), Range(1.6f, 3.2f)),
                Side.Above => new Vector3(Range(-1.5f, 1.5f), 4.8f, Range(2f, 3.5f)),
                _ => new Vector3(Range(-1.5f, 1.5f), Range(-0.3f, 0.9f), 16f),
            };
            var t = Add(ThingKind.Robot, entry);
            t.Hp = t.MaxHp = hp;
            t.Dart = dart;
            t.Spot = spot;
            // where it backs off to after striking: toward its side, a little ahead
            t.Home = side switch
            {
                Side.Left => new Vector3(-3.6f, 0.3f, 3.4f),
                Side.Right => new Vector3(3.6f, 0.3f, 3.4f),
                Side.Above => new Vector3(spot.x, 2.6f, 3.6f),
                _ => new Vector3(spot.x, 0.4f, 7f),
            };
            t.Timer = delay;
            Enter(t, ThingState.Advance);
            Meet(dart ? EnemyKind.Dart : hp > 1 ? EnemyKind.Armoured : EnemyKind.Robot);
        }

        void SpawnThrower(float x)
        {
            var t = SpawnRobot(x, Range(0.4f, 0.9f), Range(7.5f, 9f), thrower: true);
            t.Position = new Vector3(x * 2f, 4f, 18f);
            Meet(EnemyKind.Thrower);
        }

        /// <summary>A robot whose place is (x, y, distance): it flies in from far ahead and waits there (throwers throw from
        /// it; tests use it).</summary>
        public Thing SpawnRobot(float x, float y, float distance, int hp = 1, bool thrower = false)
        {
            var t = Add(ThingKind.Robot, new Vector3(x, y + 2f, SpawnDistance));
            t.Home = new Vector3(x, y, distance);
            t.Spot = new Vector2(Mathf.Clamp(x, -SpotRadius, SpotRadius), 0f);
            t.Hp = t.MaxHp = hp;
            t.Thrower = thrower;
            t.Timer = Range(1.2f, 2.2f);
            return t;
        }

        /// <summary>Sends a robot to attack: it charges in to its spot beside the hero, winds up, strikes (also for tests).</summary>
        public void Attack(Thing robot)
        {
            robot.Timer = 0f;
            Enter(robot, ThingState.Advance);
            Emit(FlyEventKind.RobotAttacks, robot, robot.Position);
        }

        /// <summary>A fighter's spot: beside the hero (it follows the hero).</summary>
        Vector3 StrikePoint(Thing robot) =>
            new Vector3(Mathf.Clamp(Hero.x + robot.Spot.x, -HalfWidth - 1.2f, HalfWidth + 1.2f),
                        Mathf.Clamp(Hero.y + robot.Spot.y, -HalfHeight - 0.4f, HalfHeight + 1.2f), StrikeDistance);

        /// <summary>A thrower lobs a spark at the hero: punch it back at the thrower, or dodge it.</summary>
        public Thing ThrowSpark(Thing thrower)
        {
            var start = thrower.Position + new Vector3(0f, 0.1f, -0.5f);
            var t = Add(ThingKind.Fireball, start);
            t.Small = true;
            t.Owner = thrower.Id;
            t.Target = new Vector3(Hero.x, Hero.y, 0f);
            t.Velocity = (t.Target - start) / 1.9f;
            Emit(FlyEventKind.SparkThrown, t, start);
            return t;
        }

        bool RushOver()
        {
            if (Phase != FlyPhase.Rush || _groupsLeft > 0 || _throwersLeft > 0) return false;
            foreach (var t in _things)
                if (t.Kind == ThingKind.Robot && t.Alive || t.Kind == ThingKind.Fireball && t.State == ThingState.Approach) return false;
            return true;
        }

        void EndRush()
        {
            if (_rushHits == 0) CleanRushes++;
            Score += 20 * District;
            Emit(FlyEventKind.RushCleared, null, Vector3.forward * 3f, 20 * District, _rushHits);
            if (Settings.Boss) BeginBoss();
            else BeginClash(false);
        }

        // ---------------------------------------------------------------- the clash

        /// <summary>The champion (or, <paramref name="withBoss"/>, the boss) comes face to face for the clash (also for
        /// tests).</summary>
        public void BeginClash(bool withBoss)
        {
            // (may come from a fireball hitting the boss, while things are being moved: they're removed after)
            foreach (var t in _things)
                if (t.Kind == ThingKind.Fireball && t.State == ThingState.Approach) Enter(t, ThingState.Gone);
            Clash = new ClashState
            {
                Rate = Settings.ClashRate, WithBoss = withBoss,
                Position = withBoss && Boss != null ? Boss.Position : new Vector3(Hero.x, Hero.y + 2.5f, 26f),
            };
            Clash.CurrentRate = Clash.Rate;
            _championPunchIn = 0.6f / Clash.Rate;
            _flurryIn = Range(2.5f, 3.5f);
            SetPhase(FlyPhase.ClashIntro);
            if (!withBoss) Meet(EnemyKind.Champion);
            Emit(FlyEventKind.ClashStarts, null, Clash.Position, value: withBoss ? 1f : 0f);
        }

        Vector3 ClashSpot => new Vector3(Hero.x, Hero.y + (Clash.WithBoss ? 0.6f : 0.45f), Clash.WithBoss ? BossClashDistance : ClashDistance);

        void UpdateClash(float dt, bool punch, bool bothHands)
        {
            var c = Clash;
            // the champion stays face to face (it follows the hero)
            c.Position = Vector3.Lerp(c.Position, ClashSpot, 1f - Mathf.Exp(-(Phase == FlyPhase.ClashIntro ? 3.5f : 8f) * dt));
            if (Boss != null && c.WithBoss) Boss.Position = c.Position;
            if (Phase == FlyPhase.ClashIntro)
            {
                if (PhaseTime >= ClashIntroSeconds)
                {
                    SetPhase(FlyPhase.Clash);
                    Emit(FlyEventKind.ClashGo, null, c.Position);
                }
                return;
            }
            if (c.Won) return;
            c.Time += dt;
            if (c.Recover > 0f)
            {
                c.Recover -= dt;
                return;
            }
            // the champion: a steady rhythm, flurries now and then, tired after a long clash
            c.Flurry = Mathf.Max(0f, c.Flurry - dt);
            _flurryIn -= dt;
            if (_flurryIn <= 0f)
            {
                _flurryIn = Range(3f, 4.5f);
                c.Flurry = 0.9f;
                Emit(FlyEventKind.ClashFlurry, null, c.Position);
            }
            float tired = c.Time > TiresAfter ? Mathf.Max(0.45f, 1f - 0.07f * (c.Time - TiresAfter)) : 1f;
            c.CurrentRate = c.Rate * (c.Flurry > 0f ? 1.6f : 1f) * tired;
            _championPunchIn -= dt;
            if (_championPunchIn <= 0f && !Scripted)
            {
                _championPunchIn = Range(0.85f, 1.15f) / c.CurrentRate;
                ChampionPunch();
            }
            if (punch)
            {
                HeroClashPunch();
                if (bothHands) HeroClashPunch();
            }
            if (c.Bar >= 1f) WinClash();
            else if (c.Bar <= -1f) LoseClash();
        }

        /// <summary>One of the champion's punches (also for tests).</summary>
        public void ChampionPunch()
        {
            if (Clash == null || Phase != FlyPhase.Clash || Clash.Won || Clash.Recover > 0f) return;
            Clash.Bar -= ChampionPush;
            Clash.ChampionPunches++;
            Emit(FlyEventKind.ClashRobotPunch, null, Vector3.Lerp(new Vector3(Hero.x, Hero.y, 0.4f), Clash.Position, 0.35f));
            if (Clash.Bar <= -1f) LoseClash();
        }

        void HeroClashPunch()
        {
            if (Clash.Bar >= 1f) return;
            Clash.Bar += HeroPush;
            Clash.HeroPunches++;
            int bonus = ComboUp();
            Score += 2 + bonus / 3;
            Emit(FlyEventKind.ClashPunch, null, Vector3.Lerp(new Vector3(Hero.x, Hero.y, 0.4f), Clash.Position, 0.5f), 2 + bonus / 3);
        }

        void WinClash()
        {
            var c = Clash;
            c.Bar = 1f;
            c.Won = true;
            ClashesWon++;
            QuickestClash = Mathf.Min(QuickestClash, c.Time);
            int points = (c.WithBoss ? 150 : 60) * District;
            Score += points;
            StarsCollected += c.WithBoss ? 25 : 10;
            Emit(FlyEventKind.ClashWon, null, c.Position, points, c.Time);
            if (c.WithBoss)
            {
                BossesBeaten++;
                Emit(FlyEventKind.BossDefeated, null, c.Position, points);
                foreach (var other in _things)
                    if (other.Punchable) Enter(other, ThingState.Gone);   // whatever is still flying fizzles out
            }
            SetPhase(FlyPhase.Cleared);
        }

        void LoseClash()
        {
            Clash.Bar = 0f;
            Clash.Recover = ClashRecoverSeconds;
            Hurt(FlyEventKind.ClashLost, null, new Vector3(Hero.x, Hero.y, 0.5f));
        }

        // ---------------------------------------------------------------- the boss

        /// <summary>The boss flies in (also for tests).</summary>
        public void BeginBoss()
        {
            _things.RemoveAll(t => t.Kind is ThingKind.Star or ThingKind.Heart or ThingKind.Drone);
            Boss = new Boss { Hp = Settings.BossHp, MaxHp = Settings.BossHp, ThrowIn = 1.2f };
            Boss.Position = BossHome(0f) + Vector3.forward * 40f;
            _secondThrowIn = -1f;
            SetPhase(FlyPhase.BossArrives);
            Meet(EnemyKind.Boss);
            Emit(FlyEventKind.BossAppears, null, Boss.Position);
        }

        Vector3 BossHome(float t) =>
            new Vector3(Mathf.Sin(t * 0.55f) * HalfWidth * 0.65f, 0.5f + 0.35f * Mathf.Sin(t * 0.9f), BossDistance);

        void UpdateBoss(float dt)
        {
            Boss.Time += dt;
            Boss.SinceHit += dt;
            Boss.Position = Vector3.Lerp(Boss.Position, BossHome(Boss.Time), 1f - Mathf.Exp(-4f * dt));
            if (Scripted) return;
            Boss.ThrowIn -= dt;
            if (Boss.ThrowIn <= 0f)
            {
                Boss.ThrowIn = Settings.ThrowEvery * Range(0.85f, 1.15f);
                Throw(new Vector3(Hero.x, Hero.y, 0f));
                if (Chance(Settings.DoubleChance)) _secondThrowIn = 0.45f;
            }
            if (_secondThrowIn > 0f)
            {
                _secondThrowIn -= dt;
                if (_secondThrowIn <= 0f)
                {
                    // the second one goes beside the hero: staying put is safe from it, dodging the wrong way isn't
                    float side = Hero.x > 0f ? -1f : 1f;
                    Throw(new Vector3(Mathf.Clamp(Hero.x + side * 1.3f, -HalfWidth, HalfWidth), Hero.y, 0f));
                }
            }
        }

        /// <summary>The boss throws a fireball at <paramref name="target"/> (the hero's window, z = 0).</summary>
        public Thing Throw(Vector3 target)
        {
            if (Boss == null) return null;
            var start = Boss.Position + new Vector3(0f, -0.3f, -1.2f);
            var t = Add(ThingKind.Fireball, start);
            t.Target = target;
            t.Velocity = (target - start) / Settings.FireballSeconds;
            Emit(FlyEventKind.BossThrows, t, start);
            return t;
        }

        void HitBoss(Thing t, int points)
        {
            if (Boss == null || Boss.Hp <= 0 || Phase is not (FlyPhase.Boss or FlyPhase.BossArrives)) return;
            Boss.Hp--;
            Boss.SinceHit = 0f;
            Emit(FlyEventKind.BossHit, t, Boss.Position, points);
            if (Boss.Hp > 0) return;
            // its fireballs beaten: the final clash, face to face
            BeginClash(true);
        }

        // ---------------------------------------------------------------- punching

        /// <summary>The punchable thing within reach now: the most urgent (a robot striking, then the one nearest to
        /// striking, then the nearest ahead); one on the punching fist's side wins a near tie.</summary>
        public Thing InReach(bool rightHand)
        {
            Thing best = null;
            float bestScore = float.MaxValue;
            foreach (var t in _things)
            {
                if (!CanPunch(t)) continue;
                float side = t.Position.x - Hero.x;
                float urgency = t.State == ThingState.Lunge ? 4f
                    : t.State == ThingState.Windup ? 1f + 2f * Mathf.Clamp01(t.StateTime / Settings.WindupSeconds) : 0f;
                float score = t.Position.z - urgency + ((rightHand ? side < -0.3f : side > 0.3f) ? 0.6f : 0f);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = t;
                }
            }
            return best;
        }

        /// <summary>Could a punch hit <paramref name="t"/> right now? (The game rings what a punch would hit.)</summary>
        public bool CanPunch(Thing t) =>
            t.Punchable && t.Position.z >= ReachNear && t.Position.z <= ReachFar && Planar(t.Position) <= ReachRadius;

        float Planar(Vector3 p) => Vector2.Distance(new Vector2(p.x, p.y), Hero);

        void Punch(bool rightHand)
        {
            var t = InReach(rightHand);
            if (t == null)
            {
                Emit(FlyEventKind.Whiff, null, new Vector3(Hero.x, Hero.y, 1f));
                return;
            }
            switch (t.Kind)
            {
                case ThingKind.Robot:
                    HitRobot(t, rightHand, 1);
                    return;
                case ThingKind.Drone:
                    SmashDrone(t, rightHand);
                    return;
            }
            // a fireball or spark punched back: it flies at whoever threw it
            Enter(t, ThingState.Smashed);
            t.Punched = true;
            FireballsReturned++;
            int points = 15 + ComboUp();
            Score += points;
            var aim = Owner(t)?.Position ?? (Boss != null ? Boss.Position : t.Position + Vector3.forward * BossDistance);
            t.Velocity = (aim - t.Position).normalized * ReturnSpeed;
            Emit(FlyEventKind.FireballReturned, t, t.Position, points);
        }

        /// <summary>One more hit in a row: the combo grows, and so do the points (0 for the first hit).</summary>
        int ComboUp()
        {
            Combo = _sinceHit <= ComboSeconds ? Combo + 1 : 1;
            _sinceHit = 0f;
            BestCombo = Mathf.Max(BestCombo, Combo);
            return 3 * Mathf.Min(Combo - 1, 10);
        }

        void HitRobot(Thing t, bool rightHand, int damage)
        {
            t.Hp -= damage;
            t.Punched = true;
            int bonus = ComboUp();
            float side = Mathf.Sign(t.Position.x - Hero.x + (rightHand ? 0.01f : -0.01f));
            if (t.Hp > 0)
            {
                // knocked back a little: it comes straight back for a quick second punch
                Enter(t, ThingState.Stagger);
                t.Velocity = new Vector3(side * 1.4f, 1f, 3f);
                Score += 5 + bonus;
                Emit(FlyEventKind.RobotHit, t, t.Position, 5 + bonus);
                return;
            }
            Enter(t, ThingState.Smashed);
            RobotsSmashed++;
            int points = (t.Big ? 20 : t.Dart ? 15 : 10) + bonus;
            Score += points;
            t.Velocity = new Vector3(side * 4f, 5f, 14f);   // knocked away: up, forward, toward the side it was hit from
            Emit(FlyEventKind.RobotSmashed, t, t.Position, points);
        }

        void SmashDrone(Thing t, bool rightHand)
        {
            Enter(t, ThingState.Smashed);
            t.Punched = true;
            DronesSmashed++;
            StarsCollected++;   // each drone drops a star
            int points = 5 + ComboUp();
            Score += points;
            t.Velocity = new Vector3(Mathf.Sign(t.Position.x - Hero.x + (rightHand ? 0.01f : -0.01f)) * 4f, 4f, 10f);
            Emit(FlyEventKind.DroneSmashed, t, t.Position, points);
            if (!_swarms.TryGetValue(t.Swarm, out var s)) return;
            s.smashed++;
            _swarms[t.Swarm] = s;
            if (s.smashed < s.total || s.spoiled) return;
            SwarmsCleared++;
            Score += 25;
            StarsCollected += 3;
            Emit(FlyEventKind.SwarmCleared, t, t.Position, 25, s.total);
        }

        Thing Owner(Thing spark)
        {
            if (spark.Owner == 0) return null;
            foreach (var t in _things)
                if (t.Id == spark.Owner && t.Alive) return t;
            return null;
        }

        // ---------------------------------------------------------------- moving things

        void UpdateThings(float dt)
        {
            for (int i = 0; i < _things.Count; i++)
            {
                var t = _things[i];
                if (t.State == ThingState.Gone) continue;   // finished during this step (removed below)
                t.StateTime += dt;
                switch (t.Kind)
                {
                    case ThingKind.Robot: UpdateRobot(t, dt); break;
                    case ThingKind.Drone: UpdateDrone(t, dt); break;
                    case ThingKind.Fireball: UpdateFireball(t, dt); break;
                    default: UpdatePickup(t, dt); break;
                }
            }
            _things.RemoveAll(t => t.State == ThingState.Gone);
        }

        void UpdateRobot(Thing t, float dt)
        {
            switch (t.State)
            {
                case ThingState.Approach:
                case ThingState.Retreat:
                    if (FlyTo(t, t.Home, t.State == ThingState.Approach ? 26f : 8f, dt))
                    {
                        Enter(t, ThingState.Wait);
                        if (!t.Thrower) t.Timer = Range(0.6f, 1.1f);   // a breather, then it comes again
                    }
                    break;
                case ThingState.Wait:
                    break;
                case ThingState.Advance:
                    if (t.Timer > 0f)
                    {
                        t.Timer -= dt;   // its group is coming in one after another
                        break;
                    }
                    float speed = Settings.ChargeSpeed * (t.Dart ? 1.9f : 1f);
                    if (FlyTo(t, StrikePoint(t), speed, dt, ease: false) || t.StateTime > 4f) Enter(t, ThingState.Windup);
                    break;
                case ThingState.Windup:
                    // stays at its spot beside the hero (it follows), then strikes at where the hero is
                    t.Position = Vector3.Lerp(t.Position, StrikePoint(t), 1f - Mathf.Exp(-4f * dt));
                    if (t.StateTime >= Settings.WindupSeconds)
                    {
                        Enter(t, ThingState.Lunge);
                        t.Target = new Vector3(Hero.x, Hero.y, 0f);
                        t.Velocity = (t.Target - t.Position) / LungeSeconds;
                        Emit(FlyEventKind.RobotCharges, t, t.Position);
                    }
                    break;
                case ThingState.Lunge:
                    t.Position += t.Velocity * dt;
                    if (t.StateTime >= LungeSeconds)
                    {
                        if (Vector2.Distance(new Vector2(t.Target.x, t.Target.y), Hero) <= BumpRadius) Hurt(FlyEventKind.RobotHitHero, t, t.Position);
                        else Emit(FlyEventKind.RobotMissed, t, t.Position);
                        Enter(t, ThingState.Retreat);
                    }
                    break;
                case ThingState.Stagger:
                    t.Position += t.Velocity * dt;
                    t.Velocity *= Mathf.Exp(-5f * dt);
                    if (t.StateTime > 0.5f)
                    {
                        if (t.Thrower) Enter(t, ThingState.Retreat);
                        else Attack(t);
                    }
                    break;
                case ThingState.Smashed:
                    t.Position += t.Velocity * dt;
                    t.Velocity += Vector3.down * (9f * dt);
                    if (t.StateTime > 1.2f) Enter(t, ThingState.Gone);
                    break;
            }
        }

        void UpdateDrone(Thing t, float dt)
        {
            if (t.State == ThingState.Smashed)
            {
                t.Position += t.Velocity * dt;
                t.Velocity += Vector3.down * (9f * dt);
                if (t.StateTime > 1f) Enter(t, ThingState.Gone);
                return;
            }
            float before = t.Position.z;
            t.Position += Vector3.back * (DroneSpeed * dt);
            if (before > 0f && t.Position.z <= 0f)
            {
                // it got past: the swarm isn't complete any more; flying into it bumps the hero (the combo is gone)
                if (_swarms.TryGetValue(t.Swarm, out var s)) _swarms[t.Swarm] = (s.total, s.smashed, true);
                if (Vector2.Distance(new Vector2(t.Position.x, t.Position.y), Hero) <= BumpRadius)
                {
                    Combo = 0;
                    Emit(FlyEventKind.DroneBumped, t, t.Position);
                }
            }
            if (t.Position.z < -5f) Enter(t, ThingState.Gone);
        }

        /// <summary>Moves toward <paramref name="to"/> (easing in when <paramref name="ease"/>); true once there.</summary>
        static bool FlyTo(Thing t, Vector3 to, float maxSpeed, float dt, bool ease = true)
        {
            var d = to - t.Position;
            float dist = d.magnitude;
            if (dist < 0.08f) return true;
            float speed = ease ? Mathf.Clamp(dist * 4f, 2.5f, maxSpeed) : maxSpeed;
            float step = Mathf.Min(dist, speed * dt);
            t.Position += d / dist * step;
            return dist - step < 0.08f;
        }

        void UpdateFireball(Thing t, float dt)
        {
            t.Position += t.Velocity * dt;
            if (t.State == ThingState.Smashed)
            {
                // punched back: it flies at whoever threw it (who keeps moving: steer toward it)
                var owner = Owner(t);
                if (owner == null && (Boss == null || t.Small || Phase != FlyPhase.Boss && Phase != FlyPhase.BossArrives))
                {
                    if (t.StateTime > 1.5f) Enter(t, ThingState.Gone);
                    return;
                }
                var aim = owner != null ? owner.Position : Boss.Position;
                t.Velocity = Vector3.Lerp(t.Velocity, (aim - t.Position).normalized * ReturnSpeed, 1f - Mathf.Exp(-6f * dt));
                if (t.Position.z >= aim.z - 0.6f)
                {
                    Enter(t, ThingState.Gone);
                    if (owner != null) HitRobot(owner, true, owner.Hp);
                    else HitBoss(t, 0);
                }
                return;
            }
            if (t.Position.z <= 0f)
            {
                Enter(t, ThingState.Gone);
                if (Vector2.Distance(new Vector2(t.Position.x, t.Position.y), Hero) <= BumpRadius) Hurt(FlyEventKind.FireballHitHero, t, t.Position);
            }
        }

        void UpdatePickup(Thing t, float dt)
        {
            float before = t.Position.z;
            t.Position += Vector3.back * (CurrentSpeed * dt);
            if (before > 0f && t.Position.z <= 0f && Vector2.Distance(new Vector2(t.Position.x, t.Position.y), Hero) <= CollectRadius)
            {
                Enter(t, ThingState.Gone);
                if (t.Kind == ThingKind.Star)
                {
                    StarsCollected++;
                    Score += 5;
                    Emit(FlyEventKind.StarCollected, t, t.Position, 5);
                }
                else
                {
                    Hearts = Mathf.Min(StartHearts, Hearts + 1);
                    Emit(FlyEventKind.HeartCollected, t, t.Position);
                }
                return;
            }
            if (t.Position.z < -6f) Enter(t, ThingState.Gone);
        }

        static void Enter(Thing t, ThingState state)
        {
            t.State = state;
            t.StateTime = 0f;
        }

        // ---------------------------------------------------------------- hits, losing, new enemies

        void Hurt(FlyEventKind kind, Thing t, Vector3 at)
        {
            if (Over || Phase == FlyPhase.Cleared) return;
            Hearts--;
            HitsTaken++;
            _rushHits++;
            Combo = 0;
            Emit(kind, t, at);
            if (Hearts > 0) return;
            SetPhase(FlyPhase.Lost);
            Emit(FlyEventKind.Lost, null, Vector3.zero);
        }

        /// <summary>The first time this run meets a kind of enemy: the game shows its card.</summary>
        void Meet(EnemyKind kind)
        {
            if (_met.Add(kind)) Emit(FlyEventKind.NewEnemy, null, Vector3.zero, value: (int)kind);
        }

        void Emit(FlyEventKind kind, Thing t, Vector3 at, int points = 0, float value = 0f) =>
            _events.Add(new FlyEvent { Kind = kind, ThingId = t != null ? t.Id : 0, Position = at, Points = points, Value = value });

        float Range(float a, float b) => a + (float)_rng.NextDouble() * (b - a);
        bool Chance(float p) => _rng.NextDouble() < p;

        // ---------------------------------------------------------------- steering from the body

        /// <summary>
        /// The hero's place in its window (-1..1 each way) from the player's body, in torso lengths from where they stood
        /// at calibration: a step of <paramref name="fullSide"/> to the side reaches the edge; ducking down
        /// <paramref name="fullDown"/> reaches the bottom; <paramref name="up"/> (0..1) lifts the hero (a jump).
        /// </summary>
        public static Vector2 Steer(float lateral, float vertical, float up, float fullSide = 0.85f, float fullDown = 0.45f,
                                    float deadZone = 0.06f)
        {
            float x = Mathf.Sign(lateral) * Mathf.Max(0f, Mathf.Abs(lateral) - deadZone) / (fullSide - deadZone);
            float y = vertical < -deadZone ? (vertical + deadZone) / (fullDown - deadZone) : 0f;
            y = Mathf.Max(y, -1f) + up * (1f - Mathf.Max(y, -1f));
            return new Vector2(Mathf.Clamp(x, -1f, 1f), Mathf.Clamp(y, -1f, 1f));
        }
    }
}
