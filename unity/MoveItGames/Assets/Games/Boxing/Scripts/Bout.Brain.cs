using System;
using UnityEngine;

namespace MoveIt.Games.Boxing
{
    /// <summary>
    /// The opponent's mind: a boxer that decides, kept small and readable (2026-11-19 overhaul, user: "don't over-AI it,
    /// keep it well designed"; before it, "I want the enemy as an actual AI making decisions"). Every opponent runs the same
    /// few rules; its <see cref="OpponentProfile"/> and its place in the climb set how well:
    /// <list type="bullet">
    /// <item><b>It sees</b> the hero with its own reaction time (<see cref="BoxingLevel.Reaction"/>): the guard, where the
    /// head and the hips are.</item>
    /// <item><b>It attacks</b> on its own rhythm (<see cref="BoxingLevel.AttackGap"/>, sooner against open gloves), and
    /// picks the punch for the opening it sees: the body or an uppercut against raised gloves, a hook from the side the hero
    /// leans to, an uppercut at a ducker, straight punches at an open guard. A smart one (<see cref="BoxingLevel.Smart"/>)
    /// picks the best nearly always, a weak one nearly at random. Combos, and a champion's power punch now and then.</item>
    /// <item><b>It aims</b> where it sees the head (the hips for a body blow), following it through the first part of its
    /// wind-up (<see cref="BoxingLevel.Track"/>), then the aim is fixed: a head that moves now escapes.</item>
    /// <item><b>It defends</b> some of the hero's punches as they come (<see cref="BoxingLevel.DefendSkill"/>): its gloves
    /// to where the punch goes, or a slip; then it may hit straight back (<see cref="BoxingLevel.CounterRate"/>).</item>
    /// <item><b>It reacts</b> to being hit: a flinch; after punches in a row it covers up (an uppercut breaks the cover);
    /// after more, it's dizzy. Its guard drifts to where it was being hit.</item>
    /// </list>
    /// Two have a trick of their own: the robot's fixed rhythm (<see cref="OpponentProfile.Rhythm"/>) and the shadow, who
    /// learns the hero's favourite punch and guards against it (<see cref="OpponentProfile.Mirror"/>).
    /// </summary>
    public sealed partial class Bout
    {
        /// <summary>Test hook: it neither attacks nor defends on its own (its stance guard still covers).</summary>
        public bool Passive { get; set; }
        /// <summary>The aim is fixed (the moment in the wind-up after which a head that moves escapes).</summary>
        public bool Committed => _committed;
        /// <summary>The hero's favourite punch as the shadow has learnt it (0 straight at the head, 1 hook, 2 uppercut, 3 at
        /// the body), -1 none yet.</summary>
        public int Favourite => _favourite;

        double _holdUntil = double.NegativeInfinity, _guardUntil = double.NegativeInfinity, _thinkAt, _nextAttack, _guardChangeAt;
        bool _committed, _counterNext;
        float _commitAt;
        int _rhythmStep, _favourite = -1;
        bool _lastRight;
        // where the hero's punches have been landing lately (head, body), and which kinds the hero throws (the shadow)
        float _hitHead, _hitBody;
        readonly float[] _kinds = new float[4];

        /// <summary>After this many punches landed in a row it covers up (four for the gentlest).</summary>
        int CoverAfter => Level.Strength < 0.3f ? 4 : 3;

        /// <summary>The robot's beat: from one punch's start to the next's (s), room for its slowest punch and a pause.</summary>
        double Beat => Level.Tell + Level.StrikeTime + MaxWait + Level.Recover + Level.AttackGap * 0.5f;

        /// <summary>What it saw of the hero.</summary>
        struct Seen
        {
            public bool Guard, Valid;
            public Vector2 Head;
            public float BodyX;
        }

        void InitBrain()
        {
            _thinkAt = 0.6;
            _nextAttack = 0.9 + Level.AttackGap * 0.4;
            Guard = PreferredGuard();
            _guardChangeAt = Level.GuardHold;
        }

        void RoundBegins()
        {
            _counterNext = false;
            _nextAttack = Time + BoxingLevel.StartSeconds + 0.6 + Level.AttackGap * 0.3;
            Hold(0.6f);
        }

        /// <summary>No attacks of its own for <paramref name="seconds"/>.</summary>
        void Hold(float seconds)
        {
            if (Time + seconds > _holdUntil) _holdUntil = Time + seconds;
        }

        // ------------------------------------------------------------------ seeing the hero

        /// <summary>The hero as it sees them now: the camera's pose its reaction time ago.</summary>
        Seen Perceive()
        {
            if (_poses.Count == 0) return new Seen { Valid = false };
            double at = Math.Min(Time - Level.Reaction, _sawUntil);
            var p = PoseAt(at);
            return new Seen { Valid = true, Guard = p.Guard, Head = p.Head, BodyX = p.BodyX };
        }

        /// <summary>The hero's punch, as it learns of it: it may block or slip it.</summary>
        void SeesPunch(bool right, Blow blow, bool body)
        {
            _lastRight = right;
            int kind = body ? 3 : (int)blow;
            for (int k = 0; k < _kinds.Length; k++) _kinds[k] *= 0.85f;
            _kinds[kind] += 1f;
            if (Opponent.Mirror)
            {
                int best = 0;
                for (int k = 1; k < _kinds.Length; k++) if (_kinds[k] > _kinds[best]) best = k;
                _favourite = _kinds[best] >= 2.5f ? best : -1;
            }
            if (Passive) return;
            bool ready = State is Foe.Ready or Foe.Blocking
                         || State == Foe.Recover && StateTime > StateLength * 0.5f
                         || State == Foe.WindUp && StateTime < StateLength * GuardedWindUp * 0.6f && !Attack.Special;
            if (!ready) return;
            if (Covers(Guard, blow, body))
            {
                // its gloves are there already: it keeps them there, and may punch back out of the block
                if (State == Foe.WindUp) CancelAttack();
                if (State != Foe.Blocking) SetState(Foe.Blocking, BlockTime * 2f);
                if (Rand() < Level.CounterRate * 0.6f) _counterNext = true;
                return;
            }
            float p = Level.DefendSkill * (State == Foe.Recover ? 0.5f : 1f);
            // the shadow reads the hero's favourite punch
            if (Opponent.Mirror && kind == _favourite) p = Mathf.Min(0.85f, p + 0.35f);
            if (Rand() >= p) return;
            if (State == Foe.WindUp) CancelAttack();
            // a slip (the head aside: not against a body blow), or the gloves to where it goes (an uppercut can't be
            // blocked with raised gloves: a slip)
            bool slip = !body && (blow == Blow.Uppercut || Rand() < Opponent.Slip);
            if (slip) SetState(Foe.Slipping, SlipTime);
            else
            {
                ChangeGuard(body ? Guard.Low : Guard.High, 0.9f);
                SetState(Foe.Blocking, BlockTime * 2.5f);
            }
            Add(new BoutEvent { Kind = BoutEventKind.Defends, Fighter = 1, Blow = blow, Body = body, Right = right,
                                Outcome = slip ? Outcome.Missed : Outcome.Blocked });
            _counterNext = Rand() < Level.CounterRate;
        }

        /// <summary>The hero's punch went into its gloves.</summary>
        void PunchBlocked()
        {
            if (State is Foe.Ready or Foe.Blocking) SetState(Foe.Blocking, Mathf.Max(BlockTime, StateLength - StateTime));
        }

        /// <summary>A hero punch landed: what it does to it (a trade, a flinch, covering up, dizzy).</summary>
        void TookPunch(bool body, bool interrupted, bool coverBroken)
        {
            if (body) _hitBody += 1f;
            else _hitHead += 1f;
            if (State is Foe.Strike or Foe.Impact or Foe.Throwing) return;   // a trade: its punch comes anyway
            if (State == Foe.WindUp && !interrupted) return;                 // it takes it and punches
            if (interrupted) CancelAttack();
            if (Combo >= Level.ComboToDizzy && _dizzyCooldown <= 0f || coverBroken && Combo >= Level.ComboToDizzy - 1 && _dizzyCooldown <= 0f)
            {
                SetState(Foe.Dizzy, Level.DizzyTime);
                _dizzyCooldown = 6f;
                _counterNext = false;
                Add(new BoutEvent { Kind = BoutEventKind.Dizzy, Fighter = 1, Combo = Combo });
                return;
            }
            if (State is Foe.Dizzy or Foe.Staggered or Foe.Taunt) return;   // open: more punches until its time is up
            if (Combo >= CoverAfter && !coverBroken)
            {
                // punches in a row: it covers up (an uppercut gets through), and may punch back out of it
                ChangeGuard(_hitBody > _hitHead ? Guard.Low : Guard.High, CoverTime + 1f);
                SetState(Foe.Covering, CoverTime);
                if (Rand() < Level.CounterRate * 0.5f) _counterNext = true;
                Add(new BoutEvent { Kind = BoutEventKind.Covers, Fighter = 1, Combo = Combo });
                return;
            }
            SetState(Foe.Hurt, HurtTime);
            // a strong one shrugs a punch off and hits straight back (mashing at it isn't free)
            if (Rand() < Level.CounterRate * 0.3f * Level.Strength) _counterNext = true;
        }

        // ------------------------------------------------------------------ every step

        void Brain(float dt)
        {
            if (_dizzyCooldown > 0f) _dizzyCooldown -= dt;
            _hitHead *= Mathf.Exp(-dt / 3f);
            _hitBody *= Mathf.Exp(-dt / 3f);
            switch (State)
            {
                case Foe.Ready:
                    if (Time >= _guardUntil && Time >= _guardChangeAt) ChangeGuard(PreferredGuard(), 0f);
                    Think();
                    break;
                case Foe.WindUp:
                    if (!_committed && StateTime >= _commitAt) Commit();
                    if (StateTime < StateLength) break;
                    if (!_committed) Commit();
                    if (Attack.Attack == Boxing.Attack.Fireball)
                    {
                        Throw();
                        break;
                    }
                    SetState(Foe.Strike, Level.StrikeTime);
                    Add(new BoutEvent { Kind = BoutEventKind.Strike, Fighter = 1, Attack = Attack.Attack, Side = Attack.Side, Aim = Attack.Aim });
                    break;
                case Foe.Strike:
                    if (StateTime < StateLength) break;
                    _impactAt = Time;
                    SetState(Foe.Impact, MaxWait);
                    goto case Foe.Impact;
                case Foe.Impact:
                    // the hitstop: the result once the camera has told what the hero did just before the impact
                    if (_sawUntil < _impactAt - DecideLead && StateTime < MaxWait) break;
                    Resolve();
                    break;
                case Foe.Blocking:
                case Foe.Slipping:
                case Foe.Covering:
                    // out of a defence that worked, straight into the counter
                    if (_counterNext && StateTime >= Mathf.Min(StateLength, BlockTime) * 0.8f && !Passive && Time >= _holdUntil)
                    {
                        StartCounter();
                        break;
                    }
                    if (StateTime >= StateLength) SetState(Foe.Ready, 0f);
                    break;
                case Foe.Recover: case Foe.Staggered: case Foe.Hurt: case Foe.Dizzy:
                case Foe.Taunt: case Foe.Throwing: case Foe.GettingUp:
                    if (StateTime >= StateLength) SetState(Foe.Ready, 0f);
                    break;
            }
        }

        // ------------------------------------------------------------------ deciding

        void Think()
        {
            if (Passive || Time < _holdUntil) return;
            if (_counterNext)
            {
                StartCounter();
                return;
            }
            if (Opponent.Rhythm)
            {
                // a machine: its punches in order, each starting on its beat (the beat is its weakness: it can be read)
                if (Time < _nextAttack) return;
                var m = Opponent.Moves[_rhythmStep % Opponent.Moves.Length].attack;
                var a = new PlannedAttack { Attack = m, Side = _rhythmStep / Opponent.Moves.Length % 2 == 0 ? -1 : 1 };
                _rhythmStep++;
                double beat = Math.Max(_nextAttack, Time - 0.1);
                Begin(a);
                _nextAttack = beat + Beat;
                return;
            }
            if (Time < _thinkAt) return;
            _thinkAt = Time + Level.ThinkInterval * (0.7f + 0.6f * Rand());
            var seen = Perceive();
            // open gloves are a reason to go now
            double due = _nextAttack - (seen.Valid && !seen.Guard ? Level.AttackGap * 0.25 * Level.Smart : 0.0);
            if (Time < due)
            {
                // nothing to do yet: once in a while a champion shows off when it's winning
                if (Rand() < Opponent.Taunt * 0.02f && Health / MaxHealth > HeroHealth / HeroMax + 0.2f)
                {
                    SetState(Foe.Taunt, TauntTime);
                    Add(new BoutEvent { Kind = BoutEventKind.Taunt, Fighter = 1 });
                }
                return;
            }
            Decide(seen);
        }

        /// <summary>The punch to throw, by what it sees: each of its punches weighted by how open the hero is to it.</summary>
        void Decide(Seen seen)
        {
            var o = Opponent;
            bool guard = seen.Valid && seen.Guard;
            bool ducking = seen.Valid && seen.Head.y < -0.08f;
            float lean = seen.Valid ? seen.Head.x : 0f;
            // a power punch now and then (champions): more against raised gloves (they go through)
            if (o.Specials.Length > 0 && Rand() < Level.SpecialShare * (guard ? 1.4f : 1f))
            {
                var special = o.Specials[_rng.Next(o.Specials.Length)];
                Begin(new PlannedAttack { Attack = special, Side = SideFor(special, lean) });
                return;
            }
            Span<float> worth = stackalloc float[8];
            int n = Math.Min(8, o.Moves.Length);
            for (int i = 0; i < n; i++) worth[i] = o.Moves[i].weight * Opening(o.Moves[i].attack, guard, ducking, lean);
            var m = o.Moves[Choose(worth.Slice(0, n))].attack;
            int follow = Rand() < Level.ComboChance ? (Rand() < 0.3f ? 2 : 1) : 0;
            Begin(new PlannedAttack { Attack = m, Side = SideFor(m, lean), Follow = follow });
        }

        /// <summary>Picks an option by what it's worth: a smart boxer the best one (nearly always), a weak one nearly at
        /// random (the worths flattened).</summary>
        int Choose(ReadOnlySpan<float> worth)
        {
            float sharp = 1f / Mathf.Lerp(1.6f, 0.3f, Level.Smart);
            float total = 0f;
            Span<float> w = stackalloc float[worth.Length];
            for (int i = 0; i < worth.Length; i++)
            {
                w[i] = worth[i] > 0f ? Mathf.Pow(worth[i], sharp) : 0f;
                total += w[i];
            }
            if (total <= 0f) return 0;
            float r = Rand() * total;
            for (int i = 0; i < w.Length; i++)
            {
                if (r < w[i]) return i;
                r -= w[i];
            }
            return w.Length - 1;
        }

        /// <summary>How open the hero is to <paramref name="m"/> as it sees them.</summary>
        public static float Opening(Attack m, bool guard, bool ducking, float lean) => m switch
        {
            Boxing.Attack.Jab => guard ? 0.5f : 1.2f,
            Boxing.Attack.Cross => guard ? 0.4f : 1.5f,
            Boxing.Attack.Hook => (guard ? 0.5f : 1.3f) * (Mathf.Abs(lean) > 0.05f ? 1.7f : 1f),
            Boxing.Attack.Uppercut => (guard ? 1.6f : 0.8f) * (ducking ? 2f : 1f),
            Boxing.Attack.Body => guard ? 1.6f : 0.8f,
            _ => 1f,
        };

        /// <summary>The side a hook (a haymaker, lightning) comes from: the side the hero leans to (a hook catches a lean),
        /// else either.</summary>
        int SideFor(Attack m, float lean)
        {
            if (!Campaign.FromSide(m)) return 0;
            if (Mathf.Abs(lean) > 0.04f && Rand() < 0.4f + 0.5f * Level.Smart) return lean > 0f ? 1 : -1;
            return Rand() < 0.5f ? -1 : 1;
        }

        // ------------------------------------------------------------------ its attacks

        /// <summary>Starts the wind-up of <paramref name="a"/>; the aim follows the hero until <see cref="BoxingLevel.Track"/>
        /// of the wind-up, then it's fixed.</summary>
        void Begin(PlannedAttack a, Vector2? aim = null)
        {
            if (!Campaign.FromSide(a.Attack)) a.Side = 0;
            else if (a.Side == 0) a.Side = Rand() < 0.5f ? -1 : 1;
            float tell = Level.TellFor(a.Attack) * (a.Quick ? 0.55f : 1f);
            if (Opponent.TellJitter > 0f && !a.Special) tell *= 1f + (Rand() * 2f - 1f) * Opponent.TellJitter;
            a.Committed = double.NaN;
            Attack = a;
            _committed = false;
            _commitAt = tell * (a.Quick ? 0.5f : Level.Track);
            if (aim.HasValue)
            {
                var fixedAttack = Attack;
                fixedAttack.Aim = aim.Value;
                fixedAttack.Committed = Time;
                Attack = fixedAttack;
                _committed = true;
            }
            SetState(Foe.WindUp, tell);
            FoeThrown++;
            if (a.Body) FoeBody++;
            else FoeHead++;
            Add(new BoutEvent { Kind = BoutEventKind.WindUp, Fighter = 1, Attack = a.Attack, Side = a.Side });
            if (!_committed && _commitAt <= 0f) Commit();
        }

        /// <summary>Fixes the aim: where it sees the hero's head (the hips) now, with its error.</summary>
        void Commit()
        {
            _committed = true;
            var a = Attack;
            var seen = Perceive();
            Vector2 aim;
            if (!seen.Valid) aim = Vector2.zero;
            else if (a.Body) aim = new Vector2(seen.BodyX, -0.45f);
            else aim = seen.Head;
            aim += new Vector2(Gauss(), Gauss() * 0.6f) * Level.AimError;
            if (a.Attack == Boxing.Attack.Sweep) aim.y = 0f;   // across at a standing head's height: under it escapes
            else if (a.Attack is Boxing.Attack.Hook or Boxing.Attack.Haymaker)
                aim.y = Mathf.Min(aim.y, 0f);   // a swing at the head's height (never high over a standing head)
            a.Aim = aim;
            a.Committed = Time;
            Attack = a;
        }

        float Gauss()
        {
            float u = Mathf.Max(1e-6f, Rand()), v = Rand();
            return Mathf.Sqrt(-2f * Mathf.Log(u)) * Mathf.Cos(2f * Mathf.PI * v);
        }

        static bool Has((Attack attack, float weight)[] moves, Attack a)
        {
            foreach (var m in moves) if (m.attack == a) return true;
            return false;
        }

        /// <summary>Straight out of a defence: a quick punch at what the hero left open by punching (the side of the
        /// punching hand).</summary>
        void StartCounter()
        {
            _counterNext = false;
            FoeCounters++;
            var o = Opponent;
            Attack m = Has(o.Moves, Boxing.Attack.Cross) ? Boxing.Attack.Cross : Boxing.Attack.Jab;
            if (Has(o.Moves, Boxing.Attack.Hook) && Rand() < 0.35f) m = Boxing.Attack.Hook;
            Begin(new PlannedAttack { Attack = m, Side = _lastRight ? 1 : -1, Quick = true });
        }

        void CancelAttack()
        {
            if (State is not (Foe.WindUp or Foe.Strike)) return;
            var a = Attack;
            a.Follow = 0;
            Attack = a;
            SetState(Foe.Ready, 0f);
            _nextAttack = Math.Max(_nextAttack, Time + Level.AttackGap * 0.6);
        }

        /// <summary>After its punch resolved: the combo's next punch, or open for a moment (staggered after a dodge),
        /// or showing off after a hit.</summary>
        void AfterPunch(PlannedAttack a, Outcome outcome)
        {
            // the robot keeps its beat (set when the punch started); the others vary their rhythm
            if (!Opponent.Rhythm) _nextAttack = Time + Level.AttackGap * (0.6f + 0.8f * Rand());
            if (a.Follow > 0 && outcome != Outcome.Dodged)
            {
                var next = new PlannedAttack { Attack = ComboNext(a.Attack, outcome), Quick = true, Follow = a.Follow - 1 };
                next.Side = Campaign.FromSide(next.Attack) ? (next.Attack == a.Attack ? -a.Side : (a.Side == 0 ? 1 : a.Side)) : 0;
                Begin(next);
                return;
            }
            if (outcome == Outcome.Dodged)
            {
                SetState(Foe.Staggered, Level.Staggered * (a.Special ? 1.3f : 1f));
                return;
            }
            if (outcome == Outcome.Hit && Rand() < Opponent.Taunt * 0.6f)
            {
                SetState(Foe.Taunt, TauntTime);
                Add(new BoutEvent { Kind = BoutEventKind.Taunt, Fighter = 1 });
                return;
            }
            SetState(Foe.Recover, Level.Recover * (outcome == Outcome.Blocked ? 1f : 0.6f));
        }

        /// <summary>A combo's next punch: the natural pairs (a jab then the cross, a cross then the hook, a hook then an
        /// uppercut...), round the gloves if the first was blocked.</summary>
        Attack ComboNext(Attack first, Outcome outcome)
        {
            var o = Opponent;
            Attack want = first switch
            {
                Boxing.Attack.Jab => Boxing.Attack.Cross,
                Boxing.Attack.Cross => Boxing.Attack.Hook,
                Boxing.Attack.Hook => Rand() < 0.5f ? Boxing.Attack.Body : Boxing.Attack.Uppercut,
                Boxing.Attack.Body => Boxing.Attack.Hook,
                Boxing.Attack.Uppercut => Boxing.Attack.Cross,
                _ => Boxing.Attack.Jab,
            };
            if (outcome == Outcome.Blocked && Rand() < Level.Smart)
                want = Has(o.Moves, Boxing.Attack.Body) ? Boxing.Attack.Body : Boxing.Attack.Uppercut;
            if (Has(o.Moves, want)) return want;
            return Campaign.IsSpecial(first) ? o.Moves[0].attack : first;
        }

        // ------------------------------------------------------------------ its guard

        Guard PreferredGuard()
        {
            // the shadow guards against the hero's favourite punch
            if (Opponent.Mirror && _favourite >= 0 && Rand() < 0.8f) return _favourite == 3 ? Guard.Low : Guard.High;
            // it covers where it was being hit
            if (_hitHead + _hitBody > 1.5f && Rand() < Level.Smart) return _hitBody > _hitHead ? Guard.Low : Guard.High;
            if (Rand() < Level.OpenShare) return Guard.Open;
            float high = Opponent.HighGuard, low = Opponent.LowGuard * Mathf.Lerp(0.3f, 1f, Level.Strength);
            if (high + low <= 0f) return Guard.Open;
            return Rand() * (high + low) < high ? Guard.High : Guard.Low;
        }

        /// <summary>Its gloves to <paramref name="guard"/>, kept there at least <paramref name="seconds"/>.</summary>
        void ChangeGuard(Guard guard, float seconds)
        {
            _guardChangeAt = Time + Level.GuardHold * (0.7f + 0.6f * Rand());
            if (seconds > 0f) _guardUntil = Math.Max(_guardUntil, Time + seconds);
            if (guard == Guard) return;
            Guard = guard;
            Add(new BoutEvent { Kind = BoutEventKind.GuardChange, Fighter = 1 });
        }
    }
}
