using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Kart
{
    public enum KartPhase { Countdown, Racing, Finishing, Over }

    /// <summary>What a gift box gives. Used by raising both hands (the remote's OK).</summary>
    public enum KartItem
    {
        None,
        /// <summary>A strong boost for a couple of seconds, nothing slows the kart meanwhile.</summary>
        Rocket,
        /// <summary>A paint bomb that flies to the kart ahead and spins it out (a shield stops it).</summary>
        Paint,
        /// <summary>A bubble round the kart for a while: the next paint bomb, honey or bump does nothing.</summary>
        Shield,
        /// <summary>A puddle of honey dropped behind: the next kart through it is slowed.</summary>
        Honey,
    }

    public enum KartEventKind
    {
        /// <summary>The countdown's numbers (Value = 3, 2, 1) and the start (Value = 0).</summary>
        Countdown,
        /// <summary>A jump right at "go": a boost off the line.</summary>
        QuickStart,
        /// <summary>A little hop on flat road (a jump that didn't meet a ramp).</summary>
        Hop,
        /// <summary>Off a ramp's lip.</summary>
        TakeOff,
        /// <summary>A trick in the air (Value: 1 = a trick, 2 = both players together; Perfect: right at the lip).</summary>
        Trick,
        /// <summary>Back on the road (Amount = how far it fell, m/s; Value = the trick's boost level, 0 = none).</summary>
        Land,
        DriftStart,
        /// <summary>The drift's sparks got hotter (Value = 1, 2, 3).</summary>
        DriftTier,
        /// <summary>A drift ended (Value = its tier: the boost it gave; 0 = none).</summary>
        DriftEnd,
        /// <summary>Over a boost pad.</summary>
        Pad,
        /// <summary>Into the barrier (Amount = how hard, 0..1).</summary>
        Wall,
        /// <summary>Two karts bumped (Other = the other one).</summary>
        Bump,
        Star,
        /// <summary>Through a gift box (Value = 1 if the kart gets an item: it starts rolling).</summary>
        ItemBox,
        /// <summary>The roll ended: the kart has an item (Value = the item).</summary>
        ItemReady,
        /// <summary>An item used (Value = the item; Other = the paint's target, -1 none).</summary>
        ItemUsed,
        /// <summary>A paint bomb hit (Kart = the one hit, Other = whose bomb).</summary>
        PaintHit,
        /// <summary>A shield stopped a paint bomb, honey or a bump (Kart = the shielded one).</summary>
        ShieldBlock,
        /// <summary>A paint bomb splashed on the road (missed).</summary>
        PaintSplat,
        /// <summary>A kart drove through honey (Other = whose).</summary>
        Honey,
        /// <summary>A new lap starts (Value = its number, 2 .. laps).</summary>
        Lap,
        /// <summary>The last lap starts.</summary>
        FinalLap,
        /// <summary>Over the line for the last time (Value = the place).</summary>
        Finish,
        /// <summary>A player's kart moved to another place (Value = the new place, Amount = the old one).</summary>
        Place,
    }

    public struct KartEvent
    {
        public KartEventKind Kind;
        public int Kart, Other, Value;
        public float Amount;
        public bool Perfect;
        public Vector3 Position;
        public double Time;
    }

    /// <summary>One kart on the track (the rules' state; the game draws it).</summary>
    public sealed class Kart
    {
        public int Index;
        /// <summary>The player slot driving it (-1 = the computer); together, both slots drive kart 0.</summary>
        public int Controller = -1;
        public bool IsHuman => Controller >= 0;
        /// <summary>The computer driver's place in <see cref="KartLevel.Names"/> (-1 = a player).</summary>
        public int Driver = -1;
        public string Name;

        /// <summary>Along the track, wrapped (m from the start line).</summary>
        public float S;
        /// <summary>Along the track since the start line, laps included (negative on the grid).</summary>
        public float Distance;
        /// <summary>Across the road (m, + = left).</summary>
        public float D;
        /// <summary>Heading against the road's (radians, + = toward its left).</summary>
        public float Psi;
        public float Speed;
        /// <summary>Above the road (m) and how fast it rises (m/s): a jump or a hop.</summary>
        public float Height, VSpeed;
        /// <summary>The road surface's extra height under the kart (m): a ramp's run-up.</summary>
        public float Ground;
        public bool Airborne => Height > 0.001f || VSpeed > 0f;
        /// <summary>The wheel as the kart steers now (-1 = full left .. 1 = full right).</summary>
        public float Steer;
        public bool OffRoad;

        public bool Drifting;
        /// <summary>-1 = drifting left, 1 = right.</summary>
        public int DriftDir;
        public float DriftTime;
        public int DriftTier;
        public float BoostTime, BoostPower = 1f;
        /// <summary>A rocket's boost: nothing slows the kart.</summary>
        public float RocketTime;
        /// <summary>Spinning out (a paint bomb), slowed (honey).</summary>
        public float SpinTime, SlowTime;
        public float ShieldTime;
        public KartItem Item;
        /// <summary>The gift box's roll (s left; the item comes when it ends).</summary>
        public float ItemRoll;
        public int Stars;

        /// <summary>Laps finished.</summary>
        public int Lap;
        public bool Finished;
        public double FinishTime;
        public int Place;

        /// <summary>The trick in the air (0 = none, 1 = a trick, 2 = a double trick), when it started, and which one it
        /// is (the game draws a flip, a spin, a barrel roll in turn).</summary>
        public int Trick;
        public double TrickAt;
        public int TrickKind;
        public bool TrickPerfect;
        /// <summary>Off a ramp: when it took off (race time; -1 = not off a ramp).</summary>
        public double TakeOffAt = -1;
        /// <summary>A jump seen before the lip, waiting for the take-off (race time; NaN = none), and the other player's
        /// (together).</summary>
        public double PendingHop = double.NaN, PendingHop2 = double.NaN;
        public int TricksDone, Bumps, WallHits, PaintHits, ItemsUsed, Hops;

        // the computer
        internal float Pace = 1f, Lane, LineAmount = 1f;
        internal float WobblePhase, WobbleFreq;
        internal float MistakeUntil, MistakeSide;
        internal float ItemDecideAt = -1f;
        internal bool MayHitPlayers;
        internal float AiCornerTime;
        // a drift being read from the wheel (players)
        internal float HardTurn, SoftTurn, DriftGrip, DriftHold;
        internal int LastPad = -1;
        internal float BumpCooldown, WallCooldown;
    }

    /// <summary>A paint bomb in flight.</summary>
    public sealed class KartBomb
    {
        public int Owner, Target;
        public float Distance, D, Speed, Life;
        public float S;
        public float Height = 0.8f;
    }

    /// <summary>A puddle of honey on the road.</summary>
    public sealed class KartPuddle
    {
        public int Owner;
        public float S, D, Life;
    }

    /// <summary>
    /// One race of Gara! (2026-11-08): six karts round <see cref="Track"/> for <see cref="Laps"/> laps, as the rules see
    /// it (no scene; KartRaceTests drive it with simulated players). The research behind it (docs in the game's bullet in
    /// CLAUDE.md): Nex Playground's Miniacs, Kinect Joy Ride and Rush (an invisible steering wheel), Forza 4's Kinect mode
    /// (the game drives the pedals), Mario Kart 8 Deluxe (smart steering and auto-accelerate for children; gift boxes and
    /// items), Mario Kart Wii and Excite Truck (a trick off a jump = a boost on landing), Kinect Adventures' River Rush (two
    /// players on one raft).
    /// <list type="bullet">
    /// <item><b>Karts live in track coordinates</b> (<see cref="Kart.S"/> along, <see cref="Kart.D"/> across, the heading
    /// <see cref="Kart.Psi"/> against the road's): the walls, the places, the computer's lines and the smart steering are
    /// simple there. The kart always accelerates; the wheel turns it (tighter at low speed); <b>smart steering</b>
    /// (<see cref="KartLevel.Assist"/>) follows that share of the road's curve by itself, straightens the kart along the
    /// road while the wheel is level and keeps it off the barrier.</item>
    /// <item><b>Drift</b>: the wheel held hard one way (65 % of a full turn for 0.3 s) slides the kart round, the sparks
    /// heating up in three steps; letting the wheel come back gives a boost as long as the sparks were hot.</item>
    /// <item><b>Jumps</b>: off a ramp's lip the kart flies (~0.9 s); a jump of the player's (or the wheel lifted) from
    /// 0.6 s before the lip until 0.5 s after it (scaled by <see cref="KartLevel.WindowScale"/>; the camera's news is late)
    /// does a trick and lands with a boost; together, both players at once a double trick. A jump on flat road is a little
    /// hop; right at "go", a quick start.</item>
    /// <item>Boost pads, lines of stars (each star a little top speed, up to 10, some lost when spun out), gift boxes with
    /// items by place (behind: rockets, ahead: honey and shields), paint bombs flying to the kart ahead.</item>
    /// <item>The computer (<see cref="KartLevel"/>): racing lines inside the corners, a lane each, a little wobble and the
    /// odd wide corner, tricks, drift boosts, items; its speed follows the player's place (the rubber band).</item>
    /// </list>
    /// </summary>
    public sealed class KartRace
    {
        public const float Gravity = 18f, KartLength = 2.3f, KartWidth = 1.6f;
        /// <summary>Seconds of countdown before "go" (race time runs from minus this).</summary>
        public const float CountdownSeconds = 3.2f;
        /// <summary>A ramp's lip height (m), how steeply it throws the kart (rise per metre) and a hop's speed up (m/s).</summary>
        public const float LipHeight = 0.9f, LipSlope = 0.3f, HopSpeed = 3.6f;
        /// <summary>A jump counts for the ramp's trick from this long before the lip until <see cref="TrickLate"/> after it
        /// (s, times <see cref="KartLevel.WindowScale"/>).</summary>
        public const float TrickEarly = 0.6f, TrickLate = 0.5f;
        /// <summary>A quick start: a jump this close to "go" (s).</summary>
        public const float QuickStartBefore = 0.45f, QuickStartAfter = 0.3f;
        /// <summary>The wheel held this hard for <see cref="DriftAfter"/> s starts a drift; under <see cref="DriftKeep"/>
        /// (toward its side) for <see cref="DriftLetGo"/> s ends it.</summary>
        public const float DriftFrom = 0.6f, DriftAfter = 0.25f, DriftKeep = 0.3f, DriftLetGo = 0.12f;
        /// <summary>In a drift, the wheel turned further than where the drift began tightens the line this much (a share of
        /// a full turn's), less widens it.</summary>
        public const float DriftSteer = 0.8f;

        /// <summary>The curvature (1/m) of a full turn of the wheel at speed <paramref name="v"/>: a ~23 m circle at 25 m/s,
        /// 16 m when crawling.</summary>
        public static float Turn(float v) => 1f / (16f + 0.3f * v);
        /// <summary>The drift's tiers (s of drifting) and their boosts (s).</summary>
        public static readonly float[] DriftTiers = { 0.6f, 1.3f, 2.1f }, DriftBoosts = { 0.7f, 1.2f, 1.7f };
        public const float Substep = 1f / 120f;
        /// <summary>The computer's karts drive this much either side of their level's pace (the fastest to the slowest).</summary>
        public const float PaceSpread = 0.02f;
        /// <summary>A race that hasn't ended after this long (s) ends anyway (nobody waits for ever).</summary>
        public const float TimeLimit = 300f;
        /// <summary>Nobody's hands on a player's wheel for this long (s): the kart runs at <see cref="NoWheelPace"/> (no
        /// driver, no racing; the game asks them to hold the wheel). A moment's let-go costs nothing.</summary>
        public const float NoWheelAfter = 1.5f, NoWheelPace = 0.9f;

        public readonly KartTrack Track;
        public readonly KartLevel Level;
        public readonly KartMode Mode;
        public readonly int Laps;
        public readonly Kart[] Karts;
        public readonly List<KartBomb> Paints = new List<KartBomb>();
        public readonly List<KartPuddle> Puddles = new List<KartPuddle>();
        /// <summary>The players (1 or 2).</summary>
        public readonly int Humans;

        public double Time { get; private set; } = -CountdownSeconds;
        public KartPhase Phase { get; private set; } = KartPhase.Countdown;
        public event Action<KartEvent> Events;
        /// <summary>Gift boxes and stars waiting to come back: (feature, index) → race time.</summary>
        public readonly Dictionary<int, double> Gone = new Dictionary<int, double>();
        public int Finishers { get; private set; }
        /// <summary>Practice (tests): the computer never uses items.</summary>
        public bool QuietComputer { get; set; }

        readonly System.Random _rng;
        readonly int _seed;
        readonly float[] _steer = new float[2];
        readonly bool[] _holding = new bool[2];
        readonly float[] _noWheel = new float[2];
        readonly double[] _lastHop = { double.NegativeInfinity, double.NegativeInfinity };
        double _endAt = double.PositiveInfinity;
        float _acc;
        int _countdownShown = 4;

        /// <param name="drivers">The computer's drivers (<see cref="KartLevel.Names"/> places), in kart order after the
        /// players' karts; as many as there are computer karts.</param>
        public KartRace(KartTrack track, KartLevel level, KartMode mode, int laps, IList<int> drivers, int seed)
        {
            Track = track;
            Level = level;
            Mode = mode;
            Laps = Math.Max(1, laps);
            _seed = seed;
            _rng = new System.Random(seed);
            Humans = mode == KartMode.Solo ? 1 : 2;
            int humanKarts = mode == KartMode.Versus ? 2 : 1;
            int count = humanKarts + drivers.Count;
            Karts = new Kart[count];
            // the players start at the back (one computer kart behind a lone player): there are karts to catch
            var paces = new List<float>();
            for (int i = 0; i < drivers.Count; i++) paces.Add(drivers.Count == 1 ? 0f : Mathf.Lerp(PaceSpread, -PaceSpread, i / (drivers.Count - 1f)));
            for (int i = paces.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (paces[i], paces[j]) = (paces[j], paces[i]);
            }
            int ai = 0;
            for (int i = 0; i < count; i++)
            {
                var k = new Kart { Index = i };
                if (i < humanKarts)
                {
                    k.Controller = i;
                    k.Name = null;
                }
                else
                {
                    k.Driver = drivers[ai];
                    k.Name = KartLevel.Names[((k.Driver % KartLevel.Drivers) + KartLevel.Drivers) % KartLevel.Drivers];
                    k.Pace = Level.AiPace * (1f + paces[ai]);
                    k.Lane = (float)(_rng.NextDouble() * 2.0 - 1.0) * 0.8f;
                    k.LineAmount = 0.6f + 0.4f * (float)_rng.NextDouble();
                    k.WobblePhase = (float)(_rng.NextDouble() * Math.PI * 2.0);
                    k.WobbleFreq = 0.25f + 0.2f * (float)_rng.NextDouble();
                    ai++;
                }
                Karts[i] = k;
            }
            // the grid: two columns, the computer's karts first, the players' karts in the last row(s)
            var order = new List<int>();
            for (int i = humanKarts; i < count; i++) order.Add(i);
            if (mode != KartMode.Versus && count > 2) order.Insert(order.Count - 1, 0);   // one computer kart behind a lone player
            else for (int i = 0; i < humanKarts; i++) order.Add(i);
            for (int g = 0; g < order.Count; g++)
            {
                var k = Karts[order[g]];
                GridSlot(g, out float s, out float d);
                k.Distance = s;
                k.S = track.Wrap(s);
                k.D = d;
                k.Place = g + 1;
            }
        }

        /// <summary>Grid slot <paramref name="g"/> (0 = pole): two staggered columns behind the line.</summary>
        public static void GridSlot(int g, out float s, out float d)
        {
            int row = g / 2, column = g % 2;
            s = -5f - row * 6f - column * 2.5f;
            d = column == 0 ? 2.6f : -2.6f;
        }

        /// <summary>The kart player <paramref name="slot"/> drives (together: both drive kart 0).</summary>
        public Kart KartOf(int slot) => Mode == KartMode.Versus ? Karts[Mathf.Clamp(slot, 0, 1)] : Karts[0];

        public bool IsHumanKart(Kart k) => k.Controller >= 0;

        // ------------------------------------------------------------------------------------------------ the players

        /// <summary>Player <paramref name="slot"/>'s wheel now (-1 = full left .. 1 = full right) and whether they hold one
        /// (both hands up; else <paramref name="steer"/> is their lean).</summary>
        public void Steer(int slot, float steer, bool holding)
        {
            if (slot < 0 || slot > 1) return;
            _steer[slot] = Mathf.Clamp(steer, -1f, 1f);
            _holding[slot] = holding;
        }

        /// <summary>The wheel a player kart steers with: its player's, or (together) both players' (the average of those
        /// holding a wheel, else of both leans).</summary>
        float HumanSteer(Kart k)
        {
            if (Mode != KartMode.Together) return _steer[Mathf.Clamp(k.Controller, 0, 1)];
            bool a = _holding[0], b = _holding[1];
            if (a && !b) return _steer[0];
            if (b && !a) return _steer[1];
            return (_steer[0] + _steer[1]) * 0.5f;
        }

        /// <summary>A jump (or the wheel lifted) of player <paramref name="slot"/> that the camera saw at race time
        /// <paramref name="time"/> (it may arrive a little later than it happened).</summary>
        public void Hop(int slot, double time)
        {
            if (slot < 0 || slot > 1 || Phase == KartPhase.Over) return;
            var k = KartOf(slot);
            if (k.Finished) return;
            double previous = _lastHop[slot];
            _lastHop[slot] = time;
            if (time - previous < 0.25) return;   // one jump, one hop
            float scale = Level.WindowScale;
            // the start: a jump right at "go" (seen before it: kept for "go"; a jump much earlier does nothing)
            if (Phase == KartPhase.Countdown || time < QuickStartAfter * scale)
            {
                if (time >= -QuickStartBefore * scale && time <= QuickStartAfter * scale)
                {
                    if (Phase == KartPhase.Countdown) k.PendingHop = time;
                    else if (k.BoostTime <= 0f) QuickStart(k);
                }
                return;
            }
            // off a ramp already: late (the camera's news) but the kart is still flying
            if (k.TakeOffAt >= 0 && k.Airborne && time - k.TakeOffAt <= TrickLate * scale && time >= k.TakeOffAt - TrickEarly * scale)
            {
                if (k.Trick == 0) StartTrick(k, time, k.TakeOffAt);
                else if (Mode == KartMode.Together && k.Trick == 1 && Math.Abs(time - k.TrickAt) < 0.5) UpgradeTrick(k);
                return;
            }
            // before a ramp: the trick waits for the lip (the jump happened Time - time ago: it counts that much earlier)
            if (!k.Airborne && TimeToLip(k, out _) + (Time - time) <= TrickEarly * scale)
            {
                if (double.IsNaN(k.PendingHop)) k.PendingHop = time;
                else if (Mode == KartMode.Together && Math.Abs(time - k.PendingHop) < 0.5) k.PendingHop2 = time;
                return;
            }
            // anywhere else: a little hop
            if (!k.Airborne && k.SpinTime <= 0f)
            {
                k.VSpeed = HopSpeed;
                k.Height = 0.001f;
                k.Hops++;
                Emit(KartEventKind.Hop, k);
            }
        }

        /// <summary>Player <paramref name="slot"/> raised both hands (the remote's OK): their item.</summary>
        public void UseItem(int slot, double time)
        {
            if (slot < 0 || slot > 1 || Phase != KartPhase.Racing) return;
            var k = KartOf(slot);
            if (k.Finished || k.Item == KartItem.None || k.ItemRoll > 0f) return;
            Use(k);
        }

        void QuickStart(Kart k)
        {
            k.BoostTime = Mathf.Max(k.BoostTime, 1.5f);
            k.BoostPower = Mathf.Max(k.BoostPower, 1.4f);
            k.Speed = Mathf.Max(k.Speed, Level.BaseSpeed * 0.6f);
            k.PendingHop = double.NaN;
            Emit(KartEventKind.QuickStart, k);
        }

        /// <summary>Seconds until the kart reaches the next ramp's lip in its lane (∞ if none within 60 m).</summary>
        public float TimeToLip(Kart k, out int feature)
        {
            feature = -1;
            float best = float.PositiveInfinity;
            for (int i = 0; i < Track.Features.Count; i++)
            {
                var f = Track.Features[i];
                if (f.Kind != FeatureKind.Ramp || Mathf.Abs(k.D - f.D) > f.Width * 0.5f + 0.5f) continue;
                float ahead = Track.Ahead(k.S, f.S);
                if (ahead < -0.5f || ahead > 60f) continue;
                float t = Mathf.Max(0f, ahead) / Mathf.Max(4f, k.Speed);
                if (t < best)
                {
                    best = t;
                    feature = i;
                }
            }
            return best;
        }

        // ------------------------------------------------------------------------------------------------ stepping

        public void Step(float dt)
        {
            if (Phase == KartPhase.Over || dt <= 0f) return;
            _acc += Mathf.Min(dt, 0.25f);
            while (_acc >= Substep)
            {
                _acc -= Substep;
                Tick(Substep);
                if (Phase == KartPhase.Over) break;
            }
        }

        void Tick(float dt)
        {
            double before = Time;
            Time += dt;
            if (Phase == KartPhase.Countdown)
            {
                int shown = Mathf.CeilToInt((float)-Time);
                if (shown < _countdownShown && shown >= 1 && shown <= 3)
                {
                    _countdownShown = shown;
                    Emit(new KartEvent { Kind = KartEventKind.Countdown, Value = shown, Kart = -1, Other = -1 });
                }
                if (Time < 0) return;
                Phase = KartPhase.Racing;
                Emit(new KartEvent { Kind = KartEventKind.Countdown, Value = 0, Kart = -1, Other = -1 });
                foreach (var k in Karts)
                {
                    if (k.IsHuman && !double.IsNaN(k.PendingHop)) QuickStart(k);
                    else if (!k.IsHuman && _rng.NextDouble() < Level.AiTricks * 0.6) QuickStart(k);
                    k.PendingHop = double.NaN;
                }
            }
            for (int s = 0; s < 2; s++) _noWheel[s] = _holding[s] ? 0f : _noWheel[s] + dt;
            foreach (var k in Karts) Drive(k, dt);
            Collide(dt);
            FlyPaints(dt);
            Respawn();
            Rank(before);
            if (Phase == KartPhase.Racing && Time > TimeLimit) _endAt = Time;
            if (Phase == KartPhase.Finishing || Time >= _endAt)
            {
                bool all = true;
                foreach (var k in Karts) all &= k.Finished;
                if (all || Time >= _endAt) End();
            }
        }

        /// <summary>The race's end: karts still racing get the time they would finish in at their pace.</summary>
        void End()
        {
            var rest = new List<(Kart k, double t)>();
            foreach (var k in Karts)
                if (!k.Finished)
                {
                    float left = Laps * Track.Length - k.Distance;
                    rest.Add((k, Time + Math.Max(0f, left) / Math.Max(5f, Level.BaseSpeed * k.Pace * 0.95f)));
                }
            rest.Sort((a, b) => a.t.CompareTo(b.t));
            foreach (var (k, t) in rest)
            {
                k.Finished = true;
                k.FinishTime = t;
                k.Place = ++Finishers;
            }
            Phase = KartPhase.Over;
        }

        // ------------------------------------------------------------------------------------------------ one kart

        void Drive(Kart k, float dt)
        {
            bool racing = Phase != KartPhase.Countdown;
            float a = k.IsHuman ? Level.Assist : 1f;
            k.BumpCooldown = Mathf.Max(0f, k.BumpCooldown - dt);
            k.WallCooldown = Mathf.Max(0f, k.WallCooldown - dt);
            if (!racing) return;

            // the wheel (a player's kart drives itself round once it has finished)
            float want = k.IsHuman && !k.Finished ? HumanSteer(k) : AiSteer(k, dt);
            if (k.SpinTime > 0f) want = 0f;
            k.Steer = Mathf.MoveTowards(k.Steer, want, (k.IsHuman ? 9f : 6f) * dt);

            // timers
            k.BoostTime = Mathf.Max(0f, k.BoostTime - dt);
            if (k.BoostTime <= 0f) k.BoostPower = 1f;
            k.RocketTime = Mathf.Max(0f, k.RocketTime - dt);
            k.SpinTime = Mathf.Max(0f, k.SpinTime - dt);
            k.SlowTime = Mathf.Max(0f, k.SlowTime - dt);
            k.ShieldTime = Mathf.Max(0f, k.ShieldTime - dt);
            if (k.ItemRoll > 0f)
            {
                k.ItemRoll -= dt;
                if (k.ItemRoll <= 0f)
                {
                    k.ItemRoll = 0f;
                    k.Item = DrawItem(k);
                    k.ItemDecideAt = (float)Time + 0.6f + (float)_rng.NextDouble() * 2.5f;
                    k.MayHitPlayers = _rng.NextDouble() < Level.AiHitsPlayers;   // decided once per item
                    Emit(KartEventKind.ItemReady, k, value: (int)k.Item);
                }
            }

            // speed
            float target = TargetSpeed(k);
            float accel = k.Speed < target ? 8f + Mathf.Max(0f, target - k.Speed) * (k.BoostTime > 0f ? 1.2f : 0.25f) : (k.SpinTime > 0f ? 22f : 12f);
            k.Speed = Mathf.MoveTowards(k.Speed, target, accel * dt);

            // drifting (on the road, fast enough)
            if (k.IsHuman && !k.Finished) ReadDrift(k, dt);
            else AiDriftStep(k, dt);
            if (k.Drifting)
            {
                k.DriftTime += dt;
                int tier = k.DriftTime >= DriftTiers[2] ? 3 : k.DriftTime >= DriftTiers[1] ? 2 : k.DriftTime >= DriftTiers[0] ? 1 : 0;
                if (tier > k.DriftTier)
                {
                    k.DriftTier = tier;
                    Emit(KartEventKind.DriftTier, k, value: tier);
                }
            }

            // turning: a full turn of the wheel is a ~23 m circle at speed, tighter when slow. A drift slides round the
            // road by itself (Mario Kart 8 Deluxe's drift with smart steering): eased in over 0.3 s, it follows the road's
            // curve, and the wheel only tightens the line (turned further than where the drift began) or widens it (less),
            // so a child's hard turn through a corner carries them round it instead of into the inside barrier
            float v = k.Speed;
            float turn = Turn(v);
            float steer = k.Steer;
            float follow = a;
            float omega = Mathf.Clamp(-steer * v * turn, -2.4f, 2.4f);
            if (k.IsHuman && !k.Finished)   // (the computer's drift is its sparks and boost: it steers its own line)
            {
                k.DriftGrip = Mathf.MoveTowards(k.DriftGrip, k.Drifting ? 1f : 0f, dt / 0.3f);
                if (k.DriftGrip > 0f)
                {
                    float into = Mathf.Max(k.Steer * k.DriftDir, 0f) - k.DriftHold;
                    float drift = Mathf.Clamp(-k.DriftDir * into * DriftSteer * v * turn, -2.4f, 2.4f);
                    omega = Mathf.Lerp(omega, drift, k.DriftGrip);
                    follow = Mathf.Lerp(a, 1f, k.DriftGrip);
                }
            }
            if (k.Airborne) omega *= 0.3f;
            float kappa = Track.Curvature(k.S);
            float sDot = v * Mathf.Cos(k.Psi) / Mathf.Max(0.3f, 1f - kappa * k.D);
            float dpsi = omega - (1f - follow) * kappa * sDot;
            // the road's edge: smart steering turns the kart away from it and takes its speed toward it (on Easy it never
            // leaves the road; on Hard most of it gets through, onto the grass and into the barrier)
            float edge = Mathf.InverseLerp(Track.HalfWidth - 2.5f, Track.HalfWidth - 0.4f, Mathf.Abs(k.D));
            if (!k.Airborne)
            {
                // smart steering: straighten along the road while the wheel is level, turn away from the barrier
                float level = k.Drifting ? 0f : 1f - Mathf.Clamp01(Mathf.Abs(steer) / 0.35f);
                dpsi -= a * 2.4f * k.Psi * level;
                if (edge > 0f && k.Psi * Mathf.Sign(k.D) > 0f) dpsi -= a * 4f * k.Psi * edge;
            }
            k.Psi = Mathf.Clamp(k.Psi + dpsi * dt, -1.1f, 1.1f);

            // moving (smart steering also takes the speed toward the barrier off near it: on Easy it's never reached)
            float across = v * Mathf.Sin(k.Psi);
            if (edge > 0f && across * k.D > 0f) across *= 1f - a * Mathf.Sqrt(edge);
            k.D += across * dt;
            float ds = sDot * dt;
            float sBefore = k.S, distBefore = k.Distance;
            k.S = Track.Wrap(k.S + ds);
            k.Distance += ds;

            // the road: ramps, flying, landing
            Surface(k, sBefore, distBefore, dt);
            // the barrier and the grass
            Walls(k);
            // the road's things
            if (!k.Finished) Pickups(k, sBefore);
            Laps_(k, distBefore);

            // the computer's items
            if (!k.IsHuman && k.Item != KartItem.None && k.ItemRoll <= 0f && !QuietComputer) AiItem(k);
        }

        float TargetSpeed(Kart k)
        {
            float v = Level.BaseSpeed;
            if (!k.IsHuman) v *= k.Pace * RubberBand(k);
            else if (!k.Finished && NobodyDriving(k)) v *= NoWheelPace;
            v *= 1f + 0.008f * Mathf.Min(10, k.Stars);
            if (k.OffRoad && k.RocketTime <= 0f) v *= 0.62f;
            if (k.SlowTime > 0f && k.RocketTime <= 0f) v *= 0.55f;
            if (k.BoostTime > 0f) v = Mathf.Max(v, Level.BaseSpeed * k.BoostPower * (1f + 0.008f * Mathf.Min(10, k.Stars)));
            if (k.SpinTime > 0f) v *= 0.3f;
            if (k.Finished) v *= 0.75f;
            return v;
        }

        /// <summary>No hands on this player kart's wheel for a while (together: neither player's).</summary>
        public bool NobodyDriving(Kart k)
        {
            if (!k.IsHuman) return false;
            if (Mode == KartMode.Together) return _noWheel[0] > NoWheelAfter && _noWheel[1] > NoWheelAfter;
            return _noWheel[Mathf.Clamp(k.Controller, 0, 1)] > NoWheelAfter;
        }

        /// <summary>The computer's speed against the players' places: ahead of them it eases off, behind it catches up.</summary>
        float RubberBand(Kart k)
        {
            float best = float.NegativeInfinity, worst = float.PositiveInfinity;
            foreach (var h in Karts)
                if (h.IsHuman)
                {
                    best = Mathf.Max(best, h.Distance);
                    worst = Mathf.Min(worst, h.Distance);
                }
            if (float.IsInfinity(best)) return 1f;
            // only across real gaps: racing close to the player is fair (the pace and the boosts decide), far ahead the
            // computer waits, far behind it catches up (nobody races alone)
            if (k.Distance > best) return 1f - Level.AiEaseAhead * Mathf.InverseLerp(20f, 120f, k.Distance - best);
            if (k.Distance < worst) return 1f + Level.AiCatchUp * Mathf.InverseLerp(40f, 150f, worst - k.Distance);
            return 1f;
        }

        /// <summary>A player's drift: the wheel held hard one way starts it, letting it come back ends it (and boosts if the
        /// sparks were hot).</summary>
        void ReadDrift(Kart k, float dt)
        {
            bool can = !k.Airborne && k.Speed > 14f && k.SpinTime <= 0f && !k.OffRoad && !k.Finished;
            if (!k.Drifting)
            {
                k.HardTurn = can && Mathf.Abs(k.Steer) >= DriftFrom ? k.HardTurn + dt : 0f;
                if (k.HardTurn >= DriftAfter)
                {
                    k.Drifting = true;
                    k.DriftDir = k.Steer > 0f ? 1 : -1;
                    k.DriftHold = Mathf.Clamp(Mathf.Abs(k.Steer), 0.55f, 0.85f);
                    k.DriftTime = 0f;
                    k.DriftTier = 0;
                    k.SoftTurn = 0f;
                    Emit(KartEventKind.DriftStart, k, value: k.DriftDir);
                }
                return;
            }
            if (!can) k.SoftTurn = DriftLetGo + 1f;   // spun, flew or left the road: ends now, no boost if it can't drive
            else k.SoftTurn = k.Steer * k.DriftDir < DriftKeep ? k.SoftTurn + dt : 0f;
            if (k.SoftTurn >= DriftLetGo) EndDrift(k, boost: can || k.Airborne);
        }

        void EndDrift(Kart k, bool boost)
        {
            int tier = boost ? k.DriftTier : 0;
            k.Drifting = false;
            k.HardTurn = 0f;
            if (tier > 0)
            {
                k.BoostTime = Mathf.Max(k.BoostTime, DriftBoosts[tier - 1]);
                k.BoostPower = Mathf.Max(k.BoostPower, 1.3f + 0.05f * tier);
            }
            Emit(KartEventKind.DriftEnd, k, value: tier);
            k.DriftTier = 0;
            k.DriftTime = 0f;
        }

        void Surface(Kart k, float sBefore, float distBefore, float dt)
        {
            // a ramp's run-up lifts the road under the kart; its lip throws it
            k.Ground = 0f;
            for (int i = 0; i < Track.Features.Count; i++)
            {
                var f = Track.Features[i];
                if (f.Kind != FeatureKind.Ramp || Mathf.Abs(k.D - f.D) > f.Width * 0.5f) continue;
                float into = Track.Ahead(f.S - f.Length, k.S);
                if (into >= 0f && into <= f.Length && !k.Airborne) k.Ground = LipHeight * into / f.Length;
                // crossed the lip this step (on the ground)
                float was = Track.Ahead(f.S, sBefore), now = Track.Ahead(f.S, k.S);
                if (was < 0f && now >= 0f && now < 5f && !k.Airborne && k.Speed > 3f) TakeOff(k, i);
            }
            if (k.Height > 0f || k.VSpeed > 0f)
            {
                k.VSpeed -= Gravity * dt;
                k.Height += k.VSpeed * dt;
                if (k.Height <= 0f)
                {
                    float fall = -k.VSpeed;
                    k.Height = 0f;
                    k.VSpeed = 0f;
                    Land(k, fall);
                }
            }
        }

        void TakeOff(Kart k, int feature)
        {
            k.Height = LipHeight;
            k.Ground = 0f;
            k.VSpeed = k.Speed * LipSlope;
            k.TakeOffAt = Time;
            if (k.Drifting) EndDrift(k, boost: true);
            Emit(KartEventKind.TakeOff, k, value: feature);
            if (k.IsHuman)
            {
                // a jump seen just before the lip
                if (!double.IsNaN(k.PendingHop) && Time - k.PendingHop <= TrickEarly * Level.WindowScale + 0.05)
                {
                    StartTrick(k, k.PendingHop, Time);
                    if (Mode == KartMode.Together && !double.IsNaN(k.PendingHop2)) UpgradeTrick(k);
                }
            }
            else if (_rng.NextDouble() < Level.AiTricks) StartTrick(k, Time - 0.1 + _rng.NextDouble() * 0.2, Time);
            k.PendingHop = k.PendingHop2 = double.NaN;
        }

        void StartTrick(Kart k, double hop, double lip)
        {
            k.Trick = 1;
            k.TrickAt = Time;
            k.TrickKind = k.TricksDone % 3;
            k.TrickPerfect = Math.Abs(hop - lip) <= 0.2;
            k.TricksDone++;
            Emit(new KartEvent { Kind = KartEventKind.Trick, Kart = k.Index, Other = -1, Value = 1, Perfect = k.TrickPerfect, Position = Pos(k), Time = Time });
        }

        void UpgradeTrick(Kart k)
        {
            k.Trick = 2;
            Emit(new KartEvent { Kind = KartEventKind.Trick, Kart = k.Index, Other = -1, Value = 2, Perfect = k.TrickPerfect, Position = Pos(k), Time = Time });
        }

        void Land(Kart k, float fall)
        {
            int trick = k.Trick;
            if (trick > 0)
            {
                float seconds = (trick == 2 ? 1.8f : 1.4f) + (k.TrickPerfect ? 0.4f : 0f);
                k.BoostTime = Mathf.Max(k.BoostTime, seconds);
                k.BoostPower = Mathf.Max(k.BoostPower, trick == 2 ? 1.55f : 1.45f);
            }
            Emit(new KartEvent { Kind = KartEventKind.Land, Kart = k.Index, Other = -1, Value = trick, Amount = fall, Perfect = k.TrickPerfect, Position = Pos(k), Time = Time });
            k.Trick = 0;
            k.TakeOffAt = -1;
            k.TrickPerfect = false;
        }

        void Walls(Kart k)
        {
            float limit = Track.Wall - KartWidth * 0.5f;
            bool off = Mathf.Abs(k.D) > Track.HalfWidth && !k.Airborne;
            k.OffRoad = off;
            if (Mathf.Abs(k.D) <= limit) return;
            float side = Mathf.Sign(k.D);
            k.D = side * limit;
            if (k.Psi * side > 0f)
            {
                // into the barrier: bounced off it, slowed by how square it hit (a new contact; scraping along it only
                // turns the kart away)
                float hit = Mathf.Abs(Mathf.Sin(k.Psi));
                k.Psi = -k.Psi * 0.35f;
                if (k.WallCooldown > 0f) return;
                k.WallCooldown = 0.4f;
                float keep = Mathf.Clamp(1f - 0.55f * hit, 0.7f, 1f);
                if (k.IsHuman) keep = Mathf.Max(keep, Level.BumpKeep);
                if (k.RocketTime <= 0f) k.Speed *= keep;
                if (k.Drifting) EndDrift(k, boost: false);
                k.WallHits++;
                Emit(KartEventKind.Wall, k, amount: Mathf.Clamp01(hit * 3f));
            }
        }

        void Laps_(Kart k, float distBefore)
        {
            if (k.Finished) return;
            int lapBefore = distBefore < 0f ? -1 : (int)(distBefore / Track.Length);
            int lapNow = k.Distance < 0f ? -1 : (int)(k.Distance / Track.Length);
            if (lapNow <= lapBefore || lapNow < 1) return;
            k.Lap = lapNow;
            if (lapNow >= Laps)
            {
                float over = k.Distance - Laps * Track.Length;
                k.Finished = true;
                k.FinishTime = Time - over / Mathf.Max(1f, k.Speed);
                k.Place = ++Finishers;
                if (k.Drifting) EndDrift(k, boost: false);
                Emit(KartEventKind.Finish, k, value: k.Place);
                if (k.IsHuman)
                {
                    bool humansDone = true;
                    foreach (var h in Karts) humansDone &= !h.IsHuman || h.Finished;
                    if (humansDone && Phase == KartPhase.Racing)
                    {
                        Phase = KartPhase.Finishing;
                        _endAt = Time + 5.0;
                    }
                }
                return;
            }
            Emit(KartEventKind.Lap, k, value: lapNow + 1);
            if (lapNow == Laps - 1) Emit(KartEventKind.FinalLap, k, value: lapNow + 1);
        }

        // ------------------------------------------------------------------------------------------------ the road's things

        /// <summary>A gift box's or star's key in <see cref="Gone"/>.</summary>
        public static int Key(int feature, int index) => feature * 64 + index;

        public bool Present(int feature, int index) => !Gone.ContainsKey(Key(feature, index));

        /// <summary>Box <paramref name="index"/> of a row: across the road.</summary>
        public static float BoxD(TrackFeature f, int index) => f.D - f.Width * 0.5f + (index + 0.5f) * f.Width / Math.Max(1, f.Count);

        /// <summary>Star <paramref name="index"/> of a line: along the road.</summary>
        public static float StarS(TrackFeature f, int index) => f.S + (f.Count <= 1 ? 0f : index * f.Length / (f.Count - 1));

        void Pickups(Kart k, float sBefore)
        {
            for (int i = 0; i < Track.Features.Count; i++)
            {
                var f = Track.Features[i];
                switch (f.Kind)
                {
                    case FeatureKind.Boost:
                    {
                        float into = Track.Ahead(f.S, k.S);
                        bool on = into >= 0f && into <= f.Length && Mathf.Abs(k.D - f.D) <= f.Width * 0.5f && !k.Airborne;
                        if (on && k.LastPad != i)
                        {
                            k.LastPad = i;
                            k.BoostTime = Mathf.Max(k.BoostTime, 1.2f);
                            k.BoostPower = Mathf.Max(k.BoostPower, 1.45f);
                            Emit(KartEventKind.Pad, k, value: i);
                        }
                        else if (!on && k.LastPad == i && (into > f.Length + 2f || into < -2f)) k.LastPad = -1;
                        break;
                    }
                    case FeatureKind.Items:
                    {
                        if (Mathf.Abs(Track.Ahead(f.S, k.S)) > 1.6f || k.Height > 2f) break;
                        for (int b = 0; b < f.Count; b++)
                        {
                            if (!Present(i, b) || Mathf.Abs(k.D - BoxD(f, b)) > 1.5f) continue;
                            Gone[Key(i, b)] = Time + 2.5;
                            bool gets = k.Item == KartItem.None && k.ItemRoll <= 0f && !k.Finished;
                            if (gets) k.ItemRoll = 1.0f;
                            Emit(new KartEvent { Kind = KartEventKind.ItemBox, Kart = k.Index, Other = -1, Value = gets ? 1 : 0, Position = Track.World(f.S, BoxD(f, b), 1f), Time = Time });
                        }
                        break;
                    }
                    case FeatureKind.Stars:
                    {
                        if (k.Height > 1.6f) break;
                        float rel = Track.Ahead(f.S, k.S);
                        if (rel < -2f || rel > f.Length + 2f) break;
                        for (int st = 0; st < f.Count; st++)
                        {
                            if (!Present(i, st)) continue;
                            float s = StarS(f, st);
                            if (Mathf.Abs(Track.Ahead(s, k.S)) > 1.4f || Mathf.Abs(k.D - f.D) > 1.3f) continue;
                            Gone[Key(i, st)] = Time + 12.0;
                            k.Stars++;
                            Emit(new KartEvent { Kind = KartEventKind.Star, Kart = k.Index, Other = -1, Value = k.Stars, Position = Track.World(s, f.D, 1f), Time = Time });
                        }
                        break;
                    }
                }
            }
            // honey on the road
            for (int i = Puddles.Count - 1; i >= 0; i--)
            {
                var p = Puddles[i];
                if (k.Airborne || Mathf.Abs(Track.Ahead(p.S, k.S)) > 1.6f || Mathf.Abs(k.D - p.D) > 1.8f) continue;
                if (p.Owner == k.Index && p.Life > 38f) continue;   // just dropped
                Puddles.RemoveAt(i);
                if (k.RocketTime > 0f) continue;
                if (k.ShieldTime > 0f)
                {
                    k.ShieldTime = 0f;
                    Emit(KartEventKind.ShieldBlock, k, other: p.Owner);
                    continue;
                }
                k.SlowTime = 1.3f;
                k.BoostTime = 0f;
                k.BoostPower = 1f;
                if (k.Drifting) EndDrift(k, boost: false);
                Emit(KartEventKind.Honey, k, other: p.Owner);
            }
        }

        void Respawn()
        {
            if (Gone.Count == 0) return;
            List<int> back = null;
            foreach (var kv in Gone)
                if (Time >= kv.Value) (back ??= new List<int>()).Add(kv.Key);
            if (back != null) foreach (int key in back) Gone.Remove(key);
        }

        // ------------------------------------------------------------------------------------------------ items

        /// <summary>A gift box's item by place: behind, rockets; in the middle, paint; ahead, honey and shields (players
        /// get more rockets on the gentle levels).</summary>
        KartItem DrawItem(Kart k)
        {
            float rank = Karts.Length <= 1 ? 0.5f : (k.Place - 1f) / (Karts.Length - 1f);
            float rocket = 0.05f + 0.65f * rank + (k.IsHuman ? Level.RocketBias * rank : 0f);
            float paint = 0.15f + 0.4f * (1f - Mathf.Abs(rank - 0.5f) * 2f);
            float shield = 0.05f + 0.3f * (1f - rank);
            float honey = 0.45f * (1f - rank);
            if (k.Place == 1) paint *= 0.3f;   // nobody ahead
            float total = rocket + paint + shield + honey;
            float r = (float)_rng.NextDouble() * total;
            if ((r -= rocket) < 0f) return KartItem.Rocket;
            if ((r -= paint) < 0f) return KartItem.Paint;
            if ((r -= shield) < 0f) return KartItem.Shield;
            return KartItem.Honey;
        }

        /// <summary>The kart a paint bomb from <paramref name="k"/> flies to: the next one ahead in the race, within 160 m
        /// (-1 = none).</summary>
        public int PaintTarget(Kart k)
        {
            int best = -1;
            float gap = 160f;
            foreach (var o in Karts)
            {
                if (o == k || o.Finished) continue;
                float g = o.Distance - k.Distance;
                if (g > 0.5f && g < gap)
                {
                    gap = g;
                    best = o.Index;
                }
            }
            return best;
        }

        void Use(Kart k)
        {
            var item = k.Item;
            k.Item = KartItem.None;
            k.ItemsUsed++;
            int other = -1;
            switch (item)
            {
                case KartItem.Rocket:
                    k.RocketTime = 2.5f;
                    k.BoostTime = Mathf.Max(k.BoostTime, 2.5f);
                    k.BoostPower = Mathf.Max(k.BoostPower, 1.6f);
                    k.SpinTime = 0f;
                    k.SlowTime = 0f;
                    break;
                case KartItem.Paint:
                    other = PaintTarget(k);
                    Paints.Add(new KartBomb
                    {
                        Owner = k.Index, Target = other, Distance = k.Distance + 2f, S = Track.Wrap(k.S + 2f), D = k.D,
                        Speed = Mathf.Max(k.Speed, 10f) + 17f, Life = other >= 0 ? 6f : 2.2f,
                    });
                    break;
                case KartItem.Shield:
                    k.ShieldTime = 12f;
                    break;
                case KartItem.Honey:
                    Puddles.Add(new KartPuddle { Owner = k.Index, S = Track.Wrap(k.S - 3.5f), D = k.D, Life = 40f });
                    break;
            }
            Emit(KartEventKind.ItemUsed, k, value: (int)item, other: other);
        }

        void FlyPaints(float dt)
        {
            for (int i = Paints.Count - 1; i >= 0; i--)
            {
                var p = Paints[i];
                p.Life -= dt;
                p.Distance += p.Speed * dt;
                p.S = Track.Wrap(p.Distance);
                var t = p.Target >= 0 ? Karts[p.Target] : null;
                if (t != null && !t.Finished)
                {
                    p.D = Mathf.MoveTowards(p.D, t.D, 10f * dt);
                    float gap = t.Distance - p.Distance;
                    if (gap < 1.2f && gap > -3f && Mathf.Abs(t.D - p.D) < 1.7f && t.Height < 2.5f)
                    {
                        Paints.RemoveAt(i);
                        Hit(t, p.Owner);
                        continue;
                    }
                    if (gap < -3f) p.Life = Mathf.Min(p.Life, 0.4f);
                }
                if (Mathf.Abs(p.D) > Track.Wall) p.Life = 0f;
                if (p.Life <= 0f)
                {
                    Paints.RemoveAt(i);
                    Emit(new KartEvent { Kind = KartEventKind.PaintSplat, Kart = p.Owner, Other = -1, Position = Track.World(p.S, p.D, 0.2f), Time = Time });
                }
            }
            for (int i = Puddles.Count - 1; i >= 0; i--)
            {
                Puddles[i].Life -= dt;
                if (Puddles[i].Life <= 0f) Puddles.RemoveAt(i);
            }
        }

        void Hit(Kart t, int owner)
        {
            if (t.RocketTime > 0f) return;
            if (t.ShieldTime > 0f)
            {
                t.ShieldTime = 0f;
                Emit(KartEventKind.ShieldBlock, t, other: owner);
                return;
            }
            t.SpinTime = 1.1f;
            int lost = Mathf.Min(3, t.Stars);
            t.Stars -= lost;
            t.PaintHits++;
            if (t.Drifting) EndDrift(t, boost: false);
            t.BoostTime = 0f;
            t.BoostPower = 1f;
            Emit(new KartEvent { Kind = KartEventKind.PaintHit, Kart = t.Index, Other = owner, Value = lost, Position = Pos(t), Time = Time });
        }

        // ------------------------------------------------------------------------------------------------ karts meeting

        void Collide(float dt)
        {
            for (int i = 0; i < Karts.Length; i++)
                for (int j = i + 1; j < Karts.Length; j++)
                {
                    Kart a = Karts[i], b = Karts[j];
                    if (Phase == KartPhase.Countdown) continue;
                    if (Mathf.Abs(a.Height - b.Height) > 1.2f) continue;
                    float along = Track.Ahead(a.S, b.S);   // + = b is ahead of a
                    float across = b.D - a.D;
                    if (Mathf.Abs(along) >= KartLength || Mathf.Abs(across) >= KartWidth) continue;
                    // push apart across the road (a rocket pushes, it isn't pushed); the one behind can't drive through
                    float overlap = KartWidth - Mathf.Abs(across);
                    float side = across >= 0f ? 1f : -1f;
                    float shareA = a.RocketTime > 0f ? 0f : b.RocketTime > 0f ? 1f : 0.5f;
                    a.D -= side * overlap * shareA;
                    b.D += side * overlap * (1f - shareA);
                    var behind = along >= 0f ? a : b;
                    var ahead = along >= 0f ? b : a;
                    bool rearEnd = Mathf.Abs(along) > KartLength * 0.45f;
                    if (rearEnd && behind.Speed > ahead.Speed && behind.RocketTime <= 0f)
                        behind.Speed = Mathf.Lerp(behind.Speed, ahead.Speed, 0.6f);
                    if (a.BumpCooldown <= 0f && b.BumpCooldown <= 0f)
                    {
                        a.BumpCooldown = b.BumpCooldown = 0.5f;
                        a.Bumps++;
                        b.Bumps++;
                        // running into the kart ahead ends the drift (side by side, both carry on)
                        if (rearEnd && behind.Drifting && behind.ShieldTime <= 0f && behind.RocketTime <= 0f) EndDrift(behind, boost: false);
                        Emit(new KartEvent { Kind = KartEventKind.Bump, Kart = a.Index, Other = b.Index, Position = (Pos(a) + Pos(b)) * 0.5f, Time = Time });
                    }
                }
        }

        // ------------------------------------------------------------------------------------------------ places

        void Rank(double before)
        {
            var order = new List<Kart>(Karts);
            order.Sort((x, y) =>
            {
                if (x.Finished != y.Finished) return x.Finished ? -1 : 1;
                if (x.Finished) return x.Place.CompareTo(y.Place);
                return y.Distance.CompareTo(x.Distance);
            });
            for (int i = 0; i < order.Count; i++)
            {
                var k = order[i];
                int place = i + 1;
                if (k.Finished || k.Place == place) continue;
                int old = k.Place;
                k.Place = place;
                if (k.IsHuman && Phase != KartPhase.Countdown)
                    Emit(new KartEvent { Kind = KartEventKind.Place, Kart = k.Index, Other = -1, Value = place, Amount = old, Position = Pos(k), Time = Time });
            }
        }

        // ------------------------------------------------------------------------------------------------ the computer

        float AiSteer(Kart k, float dt)
        {
            float v = Mathf.Max(8f, k.Speed);
            float look = 10f + 0.55f * v;
            float kap = Track.CurvatureAhead(k.S, 4f, look + 8f);
            // the racing line: inside the coming corner, a lane each, a little wobble
            float half = Track.HalfWidth - 1.8f;
            float target = Mathf.Clamp(kap * 160f, -1f, 1f) * half * k.LineAmount + k.Lane * 2.2f;
            target += Mathf.Sin((float)Time * k.WobbleFreq * 2f * Mathf.PI + k.WobblePhase) * Level.AiWobble;
            // the odd corner taken wide
            if ((float)Time < k.MistakeUntil) target += k.MistakeSide * 4.5f;
            else if (Mathf.Abs(kap) > 1f / 45f && _rng.NextDouble() < Level.AiMistakes * dt / 30f)
            {
                k.MistakeUntil = (float)Time + 1.6f;
                k.MistakeSide = -Mathf.Sign(kap);
            }
            // a pad or a gift box ahead (the stronger, the more often), else keep the lane
            int aim = AiAim(k);
            if (aim >= 0)
            {
                var f = Track.Features[aim];
                target = f.Kind == FeatureKind.Items ? NearestBox(f, k.D) : f.D;
            }
            // out of the way of a kart just ahead
            foreach (var o in Karts)
            {
                if (o == k) continue;
                float g = Track.Ahead(k.S, o.S);
                if (g > 0.5f && g < 12f && Mathf.Abs(o.D - target) < 2f && o.Speed < k.Speed + 1f)
                    target = o.D + (target >= o.D ? 2.4f : -2.4f);
            }
            target = Mathf.Clamp(target, -half, half);
            float psiTarget = Mathf.Atan2(target - k.D, look);
            return Mathf.Clamp((k.Psi - psiTarget) * 2.6f, -1f, 1f);
        }

        /// <summary>The pad or gift box a computer kart drives for from 60 m before it (-1 = none): each kart decides each one
        /// once a lap (a fixed hash of the kart, the feature and the lap), the stronger computer going for more of them.</summary>
        int AiAim(Kart k)
        {
            if (QuietComputer) return -1;
            for (int i = 0; i < Track.Features.Count; i++)
            {
                var f = Track.Features[i];
                if (f.Kind != FeatureKind.Boost && f.Kind != FeatureKind.Items) continue;
                float a = Track.Ahead(k.S, f.S);
                if (a < 3f || a > 60f) continue;
                if (f.Kind == FeatureKind.Items && (k.Item != KartItem.None || k.ItemRoll > 0f)) continue;
                int lap = Mathf.FloorToInt((k.Distance + a) / Track.Length);
                if (Hash01(k.Index, i, lap) < Level.AiItems * 0.8f) return i;
            }
            return -1;
        }

        float Hash01(int a, int b, int c)
        {
            unchecked
            {
                uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(c * 83492791) ^ (uint)_seed;
                h ^= h >> 13;
                h *= 0x5bd1e995;
                h ^= h >> 15;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        float NearestBox(TrackFeature f, float d)
        {
            float best = BoxD(f, 0);
            for (int b = 1; b < f.Count; b++)
                if (Mathf.Abs(BoxD(f, b) - d) < Mathf.Abs(best - d)) best = BoxD(f, b);
            return best;
        }

        /// <summary>The computer drifts through corners it can (tight enough): the sparks heat while the corner lasts, and a
        /// boost on its way out if this kart's drift came off (<see cref="KartLevel.AiDrifts"/>).</summary>
        void AiDriftStep(Kart k, float dt)
        {
            float kap = Track.Curvature(k.S);
            bool corner = Mathf.Abs(kap) > 1f / 50f && !k.Airborne && k.Speed > 14f && k.SpinTime <= 0f && !k.OffRoad;
            if (k.Drifting)
            {
                // out of the corner (or knocked out of it): the sparks' boost if they were hot and it's still driving
                if (!corner) EndDrift(k, boost: k.SpinTime <= 0f && !k.OffRoad);
                return;
            }
            if (!corner)
            {
                k.AiCornerTime = 0f;
                return;
            }
            float before = k.AiCornerTime;
            k.AiCornerTime += dt;
            // decided once per corner, a quarter of a second into it
            if (before < 0.25f && k.AiCornerTime >= 0.25f && _rng.NextDouble() < Level.AiDrifts)
            {
                k.Drifting = true;
                k.DriftDir = kap > 0f ? -1 : 1;
                k.DriftTime = 0f;
                k.DriftTier = 0;
                Emit(KartEventKind.DriftStart, k, value: k.DriftDir);
            }
        }

        void AiItem(Kart k)
        {
            if (k.Finished || Time < k.ItemDecideAt) return;
            switch (k.Item)
            {
                case KartItem.Rocket:
                    if (Mathf.Abs(Track.CurvatureAhead(k.S, 0f, 40f)) < 0.02f || Time > k.ItemDecideAt + 3f) Use(k);
                    break;
                case KartItem.Paint:
                {
                    int t = PaintTarget(k);
                    if (t < 0) break;
                    var target = Karts[t];
                    float gap = target.Distance - k.Distance;
                    bool allowed = !target.IsHuman || k.MayHitPlayers;
                    if (gap < 70f && allowed && _rng.NextDouble() < Level.AiItems) Use(k);
                    else k.ItemDecideAt = (float)Time + 1.2f;   // later, maybe at another kart
                    break;
                }
                case KartItem.Shield:
                    Use(k);
                    break;
                case KartItem.Honey:
                {
                    bool chased = false;
                    foreach (var o in Karts)
                    {
                        float g = k.Distance - o.Distance;
                        if (o != k && g > 2f && g < 25f) chased = true;
                    }
                    if (chased || Time > k.ItemDecideAt + 6f) Use(k);
                    break;
                }
            }
        }

        // ------------------------------------------------------------------------------------------------ events

        public Vector3 Pos(Kart k) => Track.World(k.S, k.D, k.Ground + k.Height);

        void Emit(KartEventKind kind, Kart k, int value = 0, int other = -1, float amount = 0f) =>
            Emit(new KartEvent { Kind = kind, Kart = k.Index, Other = other, Value = value, Amount = amount, Position = Pos(k), Time = Time });

        void Emit(KartEvent e)
        {
            e.Time = Time;
            Events?.Invoke(e);
        }

        /// <summary>The order the karts are in now (place 1 first).</summary>
        public Kart[] Order()
        {
            var order = (Kart[])Karts.Clone();
            Array.Sort(order, (x, y) => x.Place.CompareTo(y.Place));
            return order;
        }

        /// <summary>Practice and tests: puts a kart somewhere.</summary>
        public void Place(Kart k, float distance, float d, float speed)
        {
            k.Distance = distance;
            k.S = Track.Wrap(distance);
            k.D = d;
            k.Speed = speed;
            k.Psi = 0f;
        }

        /// <summary>Tests: skips the countdown.</summary>
        public void SkipCountdown()
        {
            while (Phase == KartPhase.Countdown) Tick(Substep);
        }
    }
}
