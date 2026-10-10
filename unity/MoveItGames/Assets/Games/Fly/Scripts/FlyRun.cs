using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Fly
{
    public enum ThingKind { Robot, Star, Heart, Fireball }

    public enum ThingState
    {
        /// <summary>Flying in from far ahead (robots slow down to hover; stars and hearts drift past with the world).</summary>
        Approach,
        /// <summary>A robot waiting in front of the hero, getting ready to charge.</summary>
        Hover,
        /// <summary>A robot charging at where the hero was when it started.</summary>
        Lunge,
        /// <summary>Punched: flying off (a fireball punched back flies at the boss).</summary>
        Smashed,
        /// <summary>Finished: the game removes it.</summary>
        Gone,
    }

    public enum FlyPhase { Flight, BossArrives, Boss, LevelClear, Won, Lost }

    public enum FlyEventKind
    {
        LevelStarted, BossAppears, RobotCharges, RobotSmashed, RobotHitHero, RobotMissed, StarCollected, HeartCollected,
        BossThrows, FireballReturned, FireballHitHero, BossHit, BossDefeated, Won, Lost, Whiff,
    }

    public struct FlyEvent
    {
        public FlyEventKind Kind;
        /// <summary>The thing it is about (0 = none).</summary>
        public int ThingId;
        /// <summary>Where it happened, in the flight frame (x right, y up, z ahead of the hero; metres).</summary>
        public Vector3 Position;
        public int Points;
    }

    /// <summary>A robot, star, heart or fireball, in the flight frame: x right, y up (the hero's window), z metres ahead.</summary>
    public sealed class Thing
    {
        public int Id;
        public ThingKind Kind;
        public ThingState State;
        public float StateTime;
        public Vector3 Position, Velocity;
        /// <summary>A charging robot's target (where the hero was), a fireball's target.</summary>
        public Vector3 Target;
        /// <summary>Smashed by a punch (or punched back); false for a robot that charged past and missed.</summary>
        public bool Punched;

        /// <summary>Can a punch hit it (a robot that isn't smashed yet, a fireball on its way to the hero)?</summary>
        public bool Punchable => Kind is ThingKind.Robot or ThingKind.Fireball && State is ThingState.Approach or ThingState.Hover or ThingState.Lunge;
        /// <summary>Hurts the hero if it reaches them.</summary>
        public bool Dangerous => Punchable;
    }

    public sealed class Boss
    {
        public int Hp, MaxHp;
        public Vector3 Position;
        public float Time;
        public float ThrowIn;
        /// <summary>Seconds since it was last hit (for a flinch), large when not recently.</summary>
        public float SinceHit = 99f;
    }

    /// <summary>How hard a level is. Level 1 is gentle (one robot at a time, long warnings, a slow boss).</summary>
    public sealed class LevelSettings
    {
        public float FlightSeconds, RobotEvery, HoverSeconds, PairChance, StarEvery;
        public int BossHp;
        public float ThrowEvery, FireballSeconds, DoubleChance;

        public static LevelSettings For(int level)
        {
            int l = Mathf.Max(1, level) - 1;
            return new LevelSettings
            {
                FlightSeconds = 35f + 4f * l,
                RobotEvery = Mathf.Max(2f, 3.4f - 0.4f * l),
                HoverSeconds = Mathf.Max(1f, 1.9f - 0.3f * l),
                PairChance = Mathf.Min(0.5f, 0.15f * l),
                StarEvery = 2.2f,
                BossHp = 4 + l,
                ThrowEvery = Mathf.Max(1.6f, 2.8f - 0.4f * l),
                FireballSeconds = Mathf.Max(1.5f, 2.4f - 0.3f * l),
                DoubleChance = Mathf.Min(0.5f, 0.2f * l),
            };
        }
    }

    /// <summary>
    /// "Fluturo!" without the pictures: the hero flies forward over the city; robots fly in, hover in front of the hero
    /// and then charge at where the hero is; stars and hearts drift past. Steering moves the hero in its window (sidestep
    /// = left/right, duck = down, jump = up); a punch smashes the most urgent robot within reach. A robot that reaches
    /// the hero costs a heart (moving away while it charges dodges it). After the flight a boss arrives and throws
    /// fireballs at the hero: punch them back to hurt it (or dodge them). Beat the boss = level cleared (hearts filled
    /// up again); beat the third boss = <b>won</b>. No hearts left = <b>lost</b>.
    /// <para>All rules live here (no scene), so tests and simulated players can play whole games; <see cref="FlyGame"/>
    /// draws it and turns the body into steering and punches.</para>
    /// </summary>
    public sealed class FlyRun
    {
        // ---- the world (metres, seconds)
        /// <summary>The hero's window: x within ±HalfWidth, y within ±HalfHeight of the flying height.</summary>
        public float HalfWidth = 2.2f, HalfHeight = 1.2f;
        /// <summary>How fast the world flies past.</summary>
        public float Speed = 16f;
        public float SpawnDistance = 48f;
        /// <summary>Robots stop this far ahead of the hero to hover.</summary>
        public float HoverDistance = 2.8f;
        public float LungeSeconds = 0.38f;
        /// <summary>How quickly the hero follows the steering (per second).</summary>
        public float HeroResponse = 7f;
        // ---- reach (generous: children punch roughly)
        /// <summary>Things this far ahead (z) can be punched...</summary>
        public float ReachNear = -0.4f, ReachFar = 3.6f;
        /// <summary>...if they are this close to the hero sideways/up-down.</summary>
        public float ReachRadius = 1.35f;
        /// <summary>A charging robot or a fireball hits the hero this close; stars and hearts are collected this close.</summary>
        public float BumpRadius = 0.95f, CollectRadius = 1.05f;
        // ---- the boss
        public float BossDistance = 15f, BossArriveSeconds = 2.5f, ReturnSpeed = 24f;
        // ---- the round
        /// <summary>Hearts at the start; every cleared level fills them up again, and up to <see cref="HeartPickups"/>
        /// hearts fly past in a level while some are missing.</summary>
        public int StartHearts = 5, HeartPickups = 2, Levels = 3;
        public float LevelClearSeconds = 3.5f;
        /// <summary>No automatic robots, stars or boss throws (tests, a practice mode): use the Spawn methods.</summary>
        public bool Scripted;

        public int Level { get; private set; }
        public LevelSettings Settings { get; private set; }
        public FlyPhase Phase { get; private set; }
        public float PhaseTime { get; private set; }
        public int Hearts { get; private set; }
        public int Score { get; private set; }
        public int RobotsSmashed { get; private set; }
        public int StarsCollected { get; private set; }
        public int FireballsReturned { get; private set; }
        public int HitsTaken { get; private set; }
        public int HitsTakenThisLevel { get; private set; }
        public Vector2 Hero { get; private set; }
        public Vector2 HeroTarget { get; private set; }
        public Boss Boss { get; private set; }
        public IReadOnlyList<Thing> Things => _things;
        /// <summary>What happened during the last <see cref="Step"/> (sounds, effects, pop-ups), and anything the Spawn,
        /// Throw or BeginBoss calls since then caused.</summary>
        public IReadOnlyList<FlyEvent> Events => _events;
        /// <summary>The flight's progress toward the boss, 0..1.</summary>
        public float FlightProgress => Phase == FlyPhase.Flight ? Mathf.Clamp01(PhaseTime / Settings.FlightSeconds) : 1f;
        public bool Over => Phase is FlyPhase.Won or FlyPhase.Lost;

        readonly List<Thing> _things = new List<Thing>();
        readonly List<FlyEvent> _events = new List<FlyEvent>();
        readonly System.Random _rng;
        int _nextId = 1, _eventsSeen;
        float _robotIn, _starIn, _heartIn;
        int _heartsLeft;
        float _secondThrowIn = -1f;

        public FlyRun(int seed = 0) => _rng = new System.Random(seed);

        /// <summary>A new round from level 1 (or <paramref name="level"/>), full hearts, no score.</summary>
        public void Start(int level = 1)
        {
            Hearts = StartHearts;
            Score = RobotsSmashed = StarsCollected = FireballsReturned = HitsTaken = 0;
            Hero = HeroTarget = Vector2.zero;
            _events.Clear();
            _eventsSeen = 0;
            StartLevel(level);
        }

        void StartLevel(int level)
        {
            Level = level;
            Settings = LevelSettings.For(level);
            _things.Clear();
            Boss = null;
            HitsTakenThisLevel = 0;
            _robotIn = 2.5f;
            _starIn = 0.8f;
            _heartIn = Settings.FlightSeconds * 0.3f;
            _heartsLeft = HeartPickups;
            _secondThrowIn = -1f;
            SetPhase(FlyPhase.Flight);
            Emit(FlyEventKind.LevelStarted, null, Vector3.zero);
        }

        void SetPhase(FlyPhase phase)
        {
            Phase = phase;
            PhaseTime = 0f;
        }

        /// <summary>
        /// Advances the game by <paramref name="dt"/> seconds. <paramref name="steer"/> is where the hero should be in its
        /// window, -1..1 each way; <paramref name="punch"/> is true in the step a punch was seen.
        /// </summary>
        public void Step(float dt, Vector2 steer, bool punch, bool rightHand = true)
        {
            // forget what the last step reported (events from calls made since then stay for this one)
            _events.RemoveRange(0, Mathf.Min(_eventsSeen, _events.Count));
            _eventsSeen = 0;
            if (dt <= 0f) return;
            StepInner(dt, steer, punch, rightHand);
            _eventsSeen = _events.Count;
        }

        void StepInner(float dt, Vector2 steer, bool punch, bool rightHand)
        {
            PhaseTime += dt;
            if (Phase != FlyPhase.Lost)
            {
                HeroTarget = new Vector2(Mathf.Clamp(steer.x, -1f, 1f) * HalfWidth, Mathf.Clamp(steer.y, -1f, 1f) * HalfHeight);
                Hero = Vector2.Lerp(Hero, HeroTarget, 1f - Mathf.Exp(-HeroResponse * dt));
            }

            switch (Phase)
            {
                case FlyPhase.Flight:
                    if (!Scripted) SpawnFlight(dt);
                    if (PhaseTime >= Settings.FlightSeconds && !Scripted && !AnyRobots()) BeginBoss();
                    break;
                case FlyPhase.BossArrives:
                    Boss.Time += dt;
                    Boss.Position = BossHome(Boss.Time) + Vector3.forward * (40f * Mathf.Pow(1f - Mathf.Clamp01(PhaseTime / BossArriveSeconds), 2f));
                    if (PhaseTime >= BossArriveSeconds) SetPhase(FlyPhase.Boss);
                    break;
                case FlyPhase.Boss:
                    UpdateBoss(dt);
                    break;
                case FlyPhase.LevelClear:
                    if (PhaseTime >= LevelClearSeconds) StartLevel(Level + 1);
                    break;
            }

            if (punch && Phase is FlyPhase.Flight or FlyPhase.BossArrives or FlyPhase.Boss) Punch(rightHand);
            UpdateThings(dt);
        }

        // ---------------------------------------------------------------- spawning

        void SpawnFlight(float dt)
        {
            if (PhaseTime >= Settings.FlightSeconds) return;
            _robotIn -= dt;
            _starIn -= dt;
            _heartIn -= dt;
            if (_robotIn <= 0f)
            {
                _robotIn = Settings.RobotEvery * Range(0.85f, 1.15f);
                if (Chance(Settings.PairChance))
                {
                    float y = Range(-0.5f, 0.6f);
                    SpawnRobot(-Range(0.9f, 1.6f), y);
                    SpawnRobot(Range(0.9f, 1.6f), y + Range(-0.3f, 0.3f));
                }
                else SpawnRobot(Range(-1.6f, 1.6f), Range(-0.6f, 0.7f));
                _starIn = Mathf.Max(_starIn, 1.0f);   // keep stars out of a robot's way for a moment
            }
            if (_starIn <= 0f)
            {
                _starIn = Settings.StarEvery * Range(0.8f, 1.2f);
                SpawnStarGroup();
            }
            if (_heartIn <= 0f && _heartsLeft > 0 && Hearts < StartHearts)
            {
                _heartsLeft--;
                _heartIn = Settings.FlightSeconds * 0.3f;
                SpawnHeart(Range(-1.5f, 1.5f), Range(-0.5f, 0.9f));
            }
        }

        /// <summary>A row, an arc or a diagonal of stars: they lead the hero somewhere.</summary>
        void SpawnStarGroup()
        {
            int shape = _rng.Next(3);
            int count = 5;
            float x0 = Range(-1.6f, 1.6f), y0 = Range(-0.9f, 0.9f), x1 = Range(-1.6f, 1.6f), y1 = Range(-0.9f, 0.9f);
            for (int i = 0; i < count; i++)
            {
                float k = i / (count - 1f);
                float x = shape == 0 ? x0 : Mathf.Lerp(x0, x1, k);
                float y = shape == 1 ? y0 + Mathf.Sin(k * Mathf.PI) * 0.6f : shape == 0 ? y0 : Mathf.Lerp(y0, y1, k);
                SpawnStar(Mathf.Clamp(x, -HalfWidth * 0.9f, HalfWidth * 0.9f), Mathf.Clamp(y, -HalfHeight * 0.9f, HalfHeight * 0.9f), i * 2.2f);
            }
        }

        /// <summary>A robot at (x, y) of the hero's window (metres), flying in from far ahead.</summary>
        public Thing SpawnRobot(float x, float y, float distance = -1f) =>
            Add(ThingKind.Robot, new Vector3(x, y, distance > 0f ? distance : SpawnDistance));

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

        bool AnyRobots()
        {
            foreach (var t in _things)
                if (t.Kind == ThingKind.Robot && t.Punchable) return true;
            return false;
        }

        // ---------------------------------------------------------------- the boss

        /// <summary>The boss flies in (also for tests: skips the rest of the flight).</summary>
        public void BeginBoss()
        {
            _things.RemoveAll(t => t.Kind is ThingKind.Star or ThingKind.Heart);
            Boss = new Boss { Hp = Settings.BossHp, MaxHp = Settings.BossHp, ThrowIn = 1.2f };
            Boss.Position = BossHome(0f) + Vector3.forward * 40f;
            SetPhase(FlyPhase.BossArrives);
            Emit(FlyEventKind.BossAppears, null, Boss.Position);
        }

        Vector3 BossHome(float t) =>
            new Vector3(Mathf.Sin(t * 0.55f) * HalfWidth * 0.65f, 0.5f + 0.35f * Mathf.Sin(t * 0.9f), BossDistance);

        void UpdateBoss(float dt)
        {
            Boss.Time += dt;
            Boss.SinceHit += dt;
            Boss.Position = BossHome(Boss.Time);
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

        // ---------------------------------------------------------------- punching

        /// <summary>The punchable things within reach now, the most urgent (nearest ahead) first.</summary>
        public Thing InReach(bool rightHand)
        {
            Thing best = null;
            float bestScore = float.MaxValue;
            foreach (var t in _things)
            {
                if (!t.Punchable || t.Position.z < ReachNear || t.Position.z > ReachFar) continue;
                float side = t.Position.x - Hero.x;
                if (Planar(t) > ReachRadius) continue;
                // the nearest first; a thing on the punching fist's side wins a near tie
                float score = t.Position.z + ((rightHand ? side < -0.3f : side > 0.3f) ? 0.6f : 0f);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = t;
                }
            }
            return best;
        }

        /// <summary>Can anything be punched right now? (The game shows a "punch!" ring.)</summary>
        public bool CanPunch(Thing t) =>
            t.Punchable && t.Position.z >= ReachNear && t.Position.z <= ReachFar && Planar(t) <= ReachRadius;

        float Planar(Thing t) => Vector2.Distance(new Vector2(t.Position.x, t.Position.y), Hero);

        void Punch(bool rightHand)
        {
            var t = InReach(rightHand);
            if (t == null)
            {
                Emit(FlyEventKind.Whiff, null, new Vector3(Hero.x, Hero.y, 1f));
                return;
            }
            Enter(t, ThingState.Smashed);
            t.Punched = true;
            if (t.Kind == ThingKind.Robot)
            {
                RobotsSmashed++;
                Score += 10;
                // knocked away: up, forward, toward the side it was hit from
                float side = Mathf.Sign(t.Position.x - Hero.x + (rightHand ? 0.01f : -0.01f));
                t.Velocity = new Vector3(side * 4f, 5f, 14f);
                Emit(FlyEventKind.RobotSmashed, t, t.Position, 10);
            }
            else
            {
                FireballsReturned++;
                Score += 15;
                var aim = Boss != null ? Boss.Position : t.Position + Vector3.forward * BossDistance;
                t.Velocity = (aim - t.Position).normalized * ReturnSpeed;
                Emit(FlyEventKind.FireballReturned, t, t.Position, 15);
            }
        }

        // ---------------------------------------------------------------- moving things

        void UpdateThings(float dt)
        {
            var hero3 = new Vector3(Hero.x, Hero.y, 0f);
            for (int i = 0; i < _things.Count; i++)
            {
                var t = _things[i];
                t.StateTime += dt;
                switch (t.Kind)
                {
                    case ThingKind.Robot: UpdateRobot(t, dt, hero3); break;
                    case ThingKind.Fireball: UpdateFireball(t, dt); break;
                    default: UpdatePickup(t, dt); break;
                }
            }
            _things.RemoveAll(t => t.State == ThingState.Gone);
        }

        void UpdateRobot(Thing t, float dt, Vector3 hero)
        {
            switch (t.State)
            {
                case ThingState.Approach:
                    // full speed, then braking to hover in front of the hero
                    float gap = t.Position.z - HoverDistance;
                    float step = Mathf.Min(Speed * dt, gap * (1f - Mathf.Exp(-3.5f * dt)));
                    t.Position += Vector3.back * Mathf.Max(step, 0f);
                    if (gap < 0.08f) Enter(t, ThingState.Hover);
                    break;
                case ThingState.Hover:
                    if (t.StateTime >= Settings.HoverSeconds)
                    {
                        Enter(t, ThingState.Lunge);
                        t.Target = hero;
                        t.Velocity = (t.Target - t.Position) / LungeSeconds;
                        Emit(FlyEventKind.RobotCharges, t, t.Position);
                    }
                    break;
                case ThingState.Lunge:
                    t.Position += t.Velocity * dt;
                    if (t.StateTime >= LungeSeconds)
                    {
                        if (Vector2.Distance(new Vector2(t.Target.x, t.Target.y), Hero) <= BumpRadius)
                        {
                            Enter(t, ThingState.Gone);
                            Hurt(FlyEventKind.RobotHitHero, t);
                        }
                        else
                        {
                            Enter(t, ThingState.Smashed);   // whooshes past behind the hero
                            t.Velocity = new Vector3(t.Velocity.x * 0.3f, 1f, -Speed);
                            Emit(FlyEventKind.RobotMissed, t, t.Position);
                        }
                    }
                    break;
                case ThingState.Smashed:
                    t.Position += t.Velocity * dt;
                    t.Velocity += Vector3.down * (9f * dt);
                    if (t.StateTime > 1.2f) Enter(t, ThingState.Gone);
                    break;
            }
        }

        void UpdateFireball(Thing t, float dt)
        {
            t.Position += t.Velocity * dt;
            if (t.State == ThingState.Smashed)
            {
                // punched back: it flies at the boss (which keeps moving: steer toward it)
                if (Boss == null || Phase is FlyPhase.LevelClear or FlyPhase.Won)
                {
                    if (t.StateTime > 1.5f) Enter(t, ThingState.Gone);
                    return;
                }
                t.Velocity = Vector3.Lerp(t.Velocity, (Boss.Position - t.Position).normalized * ReturnSpeed, 1f - Mathf.Exp(-6f * dt));
                if (t.Position.z >= Boss.Position.z - 0.6f)
                {
                    Enter(t, ThingState.Gone);
                    HitBoss(t);
                }
                return;
            }
            if (t.Position.z <= 0f)
            {
                Enter(t, ThingState.Gone);
                if (Vector2.Distance(new Vector2(t.Position.x, t.Position.y), Hero) <= BumpRadius) Hurt(FlyEventKind.FireballHitHero, t);
            }
        }

        void UpdatePickup(Thing t, float dt)
        {
            float before = t.Position.z;
            t.Position += Vector3.back * (Speed * dt);
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

        // ---------------------------------------------------------------- hits, the boss, winning and losing

        void Hurt(FlyEventKind kind, Thing t)
        {
            if (Over || Phase == FlyPhase.LevelClear) return;
            Hearts--;
            HitsTaken++;
            HitsTakenThisLevel++;
            Emit(kind, t, t.Position);
            if (Hearts > 0) return;
            SetPhase(FlyPhase.Lost);
            Emit(FlyEventKind.Lost, null, Vector3.zero);
        }

        void HitBoss(Thing t)
        {
            if (Boss == null || Boss.Hp <= 0 || Phase != FlyPhase.Boss && Phase != FlyPhase.BossArrives) return;
            Boss.Hp--;
            Boss.SinceHit = 0f;
            Emit(FlyEventKind.BossHit, t, Boss.Position);
            if (Boss.Hp > 0) return;

            int points = 100 * Level + (HitsTakenThisLevel == 0 ? 50 : 0);
            Score += points;
            // whatever is still flying fizzles out
            foreach (var other in _things)
                if (other.Punchable) Enter(other, ThingState.Gone);
            Emit(FlyEventKind.BossDefeated, null, Boss.Position, points);
            if (Level >= Levels)
            {
                SetPhase(FlyPhase.Won);
                Emit(FlyEventKind.Won, null, Boss.Position);
                return;
            }
            Hearts = StartHearts;
            SetPhase(FlyPhase.LevelClear);
        }

        void Emit(FlyEventKind kind, Thing t, Vector3 at, int points = 0) =>
            _events.Add(new FlyEvent { Kind = kind, ThingId = t != null ? t.Id : 0, Position = at, Points = points });

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
