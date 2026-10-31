using System.Collections.Generic;
using MoveIt.Motion;
using UnityEngine;
using UnityEngine.InputSystem;
using Joint = MoveIt.Motion.Joint;

namespace MoveIt.Games.Sports
{
    /// <summary>
    /// "Futboll" (Kinect Sports' football) in a full stadium: a match of five rounds against the Yjet, each an attack
    /// and a defence (<see cref="FootballMatch"/>). Attacking, the player's athlete waits at the ball: a teammate's pass
    /// to shoot first time, a free kick over a wall, a penalty; a kick (<see cref="KickDetector"/>, the foot that kicked
    /// plays the kick) shoots, the keeper dives, a goal goes into slow motion from the side. Defending, the player is the
    /// keeper (gloves, their own colours): the athlete moves across the goal with the player's steps, its gloves where
    /// the player's hands are, and dives when the player stretches toward the shot; any part of the body saves. The crowd
    /// stands for goals and saves. Without a camera: space kicks (← → held aim, ↑ held lifts it), ← → move the keeper.
    /// </summary>
    public sealed class FootballActivity : Activity
    {
        static readonly Color Sun = SportHud.Sun, Coral = SportHud.Coral, Sky = SportHud.Sky, Leaf = SportHud.Leaf, Grape = SportHud.Grape;
        static readonly Color Night = SportHud.Night;
        const float GoalLine = 52.5f;

        FootballMatch _match;
        readonly KickDetector _kicks = new KickDetector();
        Venue _venue;
        GoalNet _farNet, _ourNet;
        Athlete _you, _youKeeper, _keeper, _ref, _striker;
        readonly Athlete[] _mates = new Athlete[2], _away = new Athlete[4];
        Transform _ball, _ring;
        TrailRenderer _trail;
        ShotCamera _shot;
        MaterialPropertyBlock _tint;
        Material _trailMat;
        double _lastSample;
        float _keyX, _spin;
        readonly Vector3[] _touches = new Vector3[10];
        int _touchCount;
        float _overFor;
        bool _diving, _slowDone;
        float _slowAt = -1f;
        readonly List<(string text, string sub, Color colour, float at, float life)> _banners = new List<(string, string, Color, float, float)>();
        float _flashHome, _flashAway;

        public FootballMatch Match => _match;
        public int KicksSeen { get; private set; }

        public override string Id => "football";
        public override string Title => SportsStrings.FootballTitle;
        public override string HowTo => SportsStrings.FootballHowTo;
        public override string KeyboardHelp => SportsStrings.FootballKeys;
        /// <summary>Kicks need the feet in the picture; keeping needs room to step across the goal.</summary>
        public override PlayArea Area { get; } = new PlayArea(BodyPart.Ankles, side: 0.8f, above: 0.3f);
        public override string Music => "football";
        public override Color Colour => new Color(0.22f, 0.66f, 0.32f);
        public override int[] MedalNeeds => FootballMatch.Needs;
        public override string ResultLine => _match == null ? "" : $"{SportsStrings.Score(_match.Goals, _match.Conceded)}    {SportsStrings.FootballResult(_match.Goals, _match.Saves)}";

        // ---------------------------------------------------------------- building

        static AthleteLook Home(int seed) => AthleteLook.Person(seed, Wear.Football).Team(Sky, Color.white, Sky);

        static AthleteLook Away(int seed)
        {
            var look = AthleteLook.Person(seed, Wear.Football).Team(Coral, Night, Coral, Sun);
            look.Shoes = Color.white;
            return look;
        }

        protected override void OnBuild()
        {
            _match = new FootballMatch(Random.Range(0, 1 << 20));
            _match.Events += OnMatch;
            _kicks.Reset();
            _tint = new MaterialPropertyBlock();
            Daylight.Stadium.Apply(Kit);
            _venue = Venue.Load(Venue.Kind.Football, Kit.Root, seed: 7, occupancy: 0.82f);
            var art = SportsArt.Instance;
            if (art != null && art.net != null)
            {
                _farNet = GoalNet.Build(Kit.Root, GoalLine, 1f, art.net);
                _ourNet = GoalNet.Build(Kit.Root, -GoalLine, -1f, art.net);
            }

            var you = SportsGame.MyLook(Wear.Football);
            you.Team(Sky, Color.white, Sky);
            _you = Athlete.Create("You", you, Kit.Root);
            var keeperLook = SportsGame.MyLook(Wear.Football);
            keeperLook.Team(Sun, Night, Sun, Night);
            keeperLook.WearsGloves = true;
            keeperLook.Gloves = Leaf;
            _youKeeper = Athlete.Create("You (keeper)", keeperLook, Kit.Root);
            for (int i = 0; i < 2; i++) _mates[i] = Athlete.Create($"Teammate {i}", Home(21 + i * 5), Kit.Root);
            for (int i = 0; i < 4; i++) _away[i] = Athlete.Create($"Opponent {i}", Away(41 + i * 3), Kit.Root);
            var their = AthleteLook.Person(63, Wear.Football).Team(Leaf, Night, Leaf, Color.white);
            their.WearsGloves = true;
            their.Gloves = Color.white;
            _keeper = Athlete.Create("Their keeper", their, Kit.Root);
            _striker = Athlete.Create("Striker", Away(77), Kit.Root);
            var referee = AthleteLook.Person(90, Wear.Football).Team(Night, Night, Night, Sun);
            referee.Shoes = Night;
            _ref = Athlete.Create("Referee", referee, Kit.Root);

            _ball = SportsBalls.Create("Ball", SportsBalls.Football, Kit.Actor, TrailMaterial(), Kit.Root, Color.white);
            _trail = _ball.GetComponent<TrailRenderer>();
            _ring = PartMesh.Show("Kick ring", Kit.Root, ArtShapes.Ring(0.42f), Kit.Marker).transform;
            _ring.gameObject.SetActive(false);
            var p = Kit.Puppet;
            p.SideGain = 1f;
            p.JumpGain = 1.4f;
            p.MaxSide = FootballMatch.HalfWidth - 0.4f;
            p.KeyRoot = Vector2.zero;
            p.KeyRig = null;
            _shot = new ShotCamera(Kit.Cam);
            SetUpRound();
            Arrange(0f);
        }

        Material TrailMaterial()
        {
            if (Kit.Fx == null) return null;
            _trailMat = new Material(Kit.Fx);
            return _trailMat;
        }

        public override void Begin() => SayRound();

        public override void Dispose()
        {
            UnityEngine.Time.timeScale = 1f;
            _venue?.Dispose();
            if (_trailMat != null) Object.Destroy(_trailMat);
        }

        /// <summary>Where a point of the rules' space is in the world: the far goal in attack, ours in defence.</summary>
        Vector3 W(Vector3 p) => _match.Attacking ? new Vector3(p.x, p.y, GoalLine - _match.Distance + p.z) : new Vector3(p.x, p.y, -GoalLine + p.z);

        static Quaternion Facing(Vector3 from, Vector3 to)
        {
            var d = to - from;
            d.y = 0f;
            return d.sqrMagnitude < 1e-4f ? Quaternion.identity : Quaternion.LookRotation(d);
        }

        void Place(Athlete a, Vector3 rulesPos, Vector3 rulesLook, bool show = true)
        {
            a.gameObject.SetActive(show);
            if (!show) return;
            var at = W(rulesPos);
            a.transform.SetPositionAndRotation(at, Facing(at, W(rulesLook)));
        }

        /// <summary>Everyone to their places for the round's attack or defence, in their waiting clip.</summary>
        void SetUpRound()
        {
            _diving = false;
            _slowDone = false;
            _slowAt = -1f;
            UnityEngine.Time.timeScale = 1f;
            foreach (var a in AllAthletes()) a.gameObject.SetActive(false);
            if (_trail != null)
            {
                _trail.Clear();
                _trail.emitting = false;
            }
            var m = _match;
            float d = m.Distance;
            if (m.Attacking)
            {
                var goal = new Vector3(0f, 0f, d);
                Place(_you, new Vector3(-0.05f, 0f, -0.05f), goal);
                _you.Play("Ready", 0f);
                Place(_keeper, new Vector3(0f, 0f, d - 0.45f), Vector3.zero);
                _keeper.Play("KeeperReady", 0f);
                Place(_ref, new Vector3(7f, 0f, 5f), new Vector3(0f, 0f, 3f));
                _ref.Play("Idle", 0f);
                switch (m.Situation)
                {
                    case FootballSituation.Pass:
                        float side = m.PassFrom;
                        Place(_mates[0], new Vector3(side + Mathf.Sign(side) * 0.45f, 0f, 3.9f), new Vector3(0f, 0f, 0.35f));
                        _mates[0].Play("Ready", 0f);
                        Place(_mates[1], new Vector3(-side * 1.1f, 0f, 7.5f), goal);
                        _mates[1].Play("Ready", 0f);
                        Place(_away[0], new Vector3(2.6f, 0f, d - 7f), Vector3.zero);
                        Place(_away[1], new Vector3(-3.2f, 0f, d - 8.5f), Vector3.zero);
                        _away[0].Play("Ready", 0f);
                        _away[1].Play("Ready", 0f, 0.9f);
                        break;
                    case FootballSituation.FreeKick:
                        for (int i = 0; i < 4; i++)
                        {
                            Place(_away[i], new Vector3(m.WallX + (i - 1.5f) * 0.56f, 0f, FootballMatch.WallDistance), Vector3.zero);
                            _away[i].Play("Stand", 0f);
                        }
                        Place(_mates[0], new Vector3(-6f, 0f, 9f), goal);
                        _mates[0].Play("Ready", 0f);
                        Place(_mates[1], new Vector3(5f, 0f, 12f), goal);
                        _mates[1].Play("Ready", 0f);
                        break;
                    default:
                        // a penalty: the others wait at the edge of the box
                        float edge = d - 16.5f - 1f;
                        Place(_mates[0], new Vector3(-4f, 0f, edge), goal);
                        Place(_mates[1], new Vector3(6f, 0f, edge - 0.5f), goal);
                        Place(_away[0], new Vector3(-6.5f, 0f, edge + 0.3f), goal);
                        Place(_away[1], new Vector3(3.5f, 0f, edge - 0.2f), goal);
                        foreach (var a in new[] { _mates[0], _mates[1], _away[0], _away[1] }) a.Play("Idle", 0f, from: Random.value);
                        break;
                }
            }
            else
            {
                Place(_youKeeper, new Vector3(0f, 0f, 0.45f), new Vector3(0f, 0f, d));
                _youKeeper.Play("KeeperReady", 0f);
                Place(_striker, new Vector3(0.095f, 0f, d + 3.6f), Vector3.zero);
                _striker.Play("Ready", 0f);
                Place(_ref, new Vector3(-7f, 0f, 11f), new Vector3(0f, 0f, 6f));
                _ref.Play("Idle", 0f);
                bool penalty = d < 12f;
                Place(_mates[0], penalty ? new Vector3(-4f, 0f, 17.5f) : new Vector3(-3.4f, 0f, 7.5f), new Vector3(0f, 0f, d));
                Place(_mates[1], penalty ? new Vector3(5f, 0f, 17.8f) : new Vector3(3.8f, 0f, 9f), new Vector3(0f, 0f, d));
                Place(_away[0], penalty ? new Vector3(-6f, 0f, 18f) : new Vector3(-5f, 0f, d + 2f), Vector3.zero);
                foreach (var a in new[] { _mates[0], _mates[1], _away[0] }) a.Play("Ready", 0f, from: Random.value);
            }
            if (_shot != null)
            {
                var pos = WantedCamera(out var look, out float fov);
                _shot.Cut(pos, look, fov);
            }
        }

        IEnumerable<Athlete> AllAthletes()
        {
            yield return _you;
            yield return _youKeeper;
            yield return _keeper;
            yield return _ref;
            yield return _striker;
            foreach (var a in _mates) yield return a;
            foreach (var a in _away) yield return a;
        }

        void SayRound()
        {
            var m = _match;
            if (m.Attacking)
            {
                switch (m.Situation)
                {
                    case FootballSituation.FreeKick:
                        Say(SportsStrings.FreeKick, Grape, SportsStrings.FreeKickSub);
                        break;
                    case FootballSituation.Penalty:
                        Say(SportsStrings.Penalty, Grape, SportsStrings.PenaltySub);
                        break;
                    default:
                        Say(SportsStrings.Attack, Sky, SportsStrings.AttackSub);
                        break;
                }
            }
            else Say(SportsStrings.Defend, Sun, m.Distance < 12f ? SportsStrings.PenaltyAgainst : SportsStrings.DefendSub);
            GameAudio.Sfx("sport_whistle", 0.8f, vary: 0f);
        }

        void Say(string text, Color colour, string sub = null, float life = 1.7f) => _banners.Add((text, sub, colour, UnityEngine.Time.unscaledTime, life));

        // ---------------------------------------------------------------- play

        public override void Step(float dt, bool playing)
        {
            KeyPlay(dt, playing);
            ReadBody(playing);
            Touches();
            if (playing)
            {
                Time += dt;
                _match.Step(dt, _touches, _touchCount);
                Points = _match.Points;
                if (_match.Over) _overFor += dt;
                if (_overFor > 3f) Finished = true;
            }
            Direct();
            Arrange(dt);
            SlowMotion();
        }

        void ReadBody(bool playing)
        {
            var body = MotionSystem.Instance != null ? MotionSystem.Instance.ActiveBody : null;
            if (body == null || !body.IsTracking || body.SampleTime == _lastSample) return;
            _lastSample = body.SampleTime;
            var f = body.Fast;
            if (!_kicks.Update(body.SampleTime, body.FastHipCenter, body.FastTorsoLength, f[(int)Joint.LeftAnkle], body.Visibility[(int)Joint.LeftAnkle],
                               f[(int)Joint.RightAnkle], body.Visibility[(int)Joint.RightAnkle], out var kick)) return;
            KicksSeen++;
            if (!playing || !_match.Attacking) return;
            var g = Kit.Gestures;
            float lean = g != null && g.IsCalibrated ? Mathf.Clamp(g.LateralOffset / 0.6f, -1f, 1f) : 0f;
            bool met = _match.Kick(kick.Direction, kick.Speed, lean);
            if (met) Kicked(kick.RightFoot ? 1 : 0);
            Debug.Log($"[Sports] Kick {(kick.RightFoot ? "right" : "left")} {kick.Speed:0.0} torso/s dir ({kick.Direction.x:0.0}, {kick.Direction.y:0.0}) lean {lean:0.0}: {(met ? $"shot -> {_match.Predicted}" : "no ball")}");
        }

        void KeyPlay(float dt, bool playing)
        {
            var p = Kit.Puppet;
            float axis = KeyAxis(Key.LeftArrow, Key.RightArrow);
            if (_match.Attacking)
            {
                if (playing && Kit.UsingKeys && KeyDown(Key.Space))
                {
                    var dir = new Vector2(axis * 0.7f, KeyHeld(Key.UpArrow) ? 0.8f : 0.2f).normalized;
                    if (_match.Kick(dir, KeyHeld(Key.UpArrow) ? 7f : 6f, 0f)) Kicked(1);
                }
                p.KeyRoot = Vector2.zero;
                p.KeyRig = ToyPoses.Idle(UnityEngine.Time.time);
            }
            else
            {
                _keyX = Mathf.Clamp(_keyX + axis * 4f * dt, -FootballMatch.HalfWidth + 0.5f, FootballMatch.HalfWidth - 0.5f);
                p.KeyRoot = new Vector2(_keyX, 0f);
                var rig = ToyRig.Standing;
                rig.LeftArm = rig.RightArm = new ToyLimb(40f, 45f, 30f);   // a keeper's ready stance
                rig.LeftLeg = rig.RightLeg = new ToyLimb(5f, 10f, 25f);
                rig.Hips.y -= 0.08f;
                rig.Lean = 12f;
                p.KeyRig = rig;
            }
        }

        /// <summary>The player's athlete kicks with the foot the player kicked with, its contact moment now.</summary>
        void Kicked(int foot)
        {
            string clip = foot == 1 ? "KickR" : "KickL";
            var c = AthleteData.Find(clip);
            _you.PlayThen(clip, "Idle", 0.05f, 1f, 0.3f, from: Mathf.Max(0f, c.Contact - 0.06f));
        }

        /// <summary>The keeper's body in the goal mouth (x across, y up, z = radius); keyboard: a hand where the ball comes,
        /// if it's within a step.</summary>
        void Touches()
        {
            _touchCount = 0;
            if (_match.Attacking) return;
            var p = Kit.Puppet;
            void Add(Vector3 at, float r) => _touches[_touchCount++] = new Vector3(at.x, at.y, r);
            Add(p.Hand(false), 0.16f);
            Add(p.Hand(true), 0.16f);
            Add(p.Foot(false), 0.13f);
            Add(p.Foot(true), 0.13f);
            Add(p.Head, 0.17f);
            Add(p.Chest, 0.26f);
            Add(p[Joint.LeftKnee], 0.1f);
            Add(p[Joint.RightKnee], 0.1f);
            if (Kit.UsingKeys && _match.Phase == FootballPhase.Flight && Mathf.Abs(_match.Target.x - p.Root.x) < 1.1f)
                Add(new Vector3(_match.Target.x, _match.Target.y, 0f), 0.2f);
        }

        void OnMatch(FootballEvent e)
        {
            var m = _match;
            switch (e)
            {
                case FootballEvent.Attack:
                case FootballEvent.Defend:
                    SetUpRound();
                    SayRound();
                    break;
                case FootballEvent.Pass:
                    GameAudio.Sfx("sport_kick", 0.55f);
                    break;
                case FootballEvent.Kicked:
                    GameAudio.Sfx("sport_kick");
                    _venue?.Fans?.Buzz(0.3f);
                    if (_trail != null) _trail.emitting = true;
                    KeeperReacts();
                    if (m.Situation == FootballSituation.FreeKick)
                        for (int i = 0; i < 4; i++) _away[i].PlayThen("Block", "Stand", 0.05f, 1.2f, 0.3f, from: 0.12f + i * 0.03f);
                    break;
                case FootballEvent.Missed:
                    GameAudio.Sfx("sport_miss");
                    Say(SportsStrings.Missed, Coral, life: 1.4f);
                    _you.Play("HandsOnHead", 0.3f);
                    break;
                case FootballEvent.Goal:
                    GameAudio.Sfx("sport_net");
                    GameAudio.Sfx("tennis_cheer", 0.9f);
                    GameAudio.Jingle("jingle_win", 0.4f);
                    _farNet?.Hit(W(m.Ball), new Vector3(m.BallVelocity.x, m.BallVelocity.y, 1f) * 18f);
                    _venue?.Fans?.Cheer(1f, 3.5f);
                    _flashHome = 1f;
                    Kit.Fx3.Confetti(W(m.Ball) + Vector3.up * 1.5f, 50, new[] { Sun, Coral, Sky, Grape, Leaf });
                    Say(SportsStrings.Goal, Sun, life: 2.2f);
                    _you.Play("Airplane", 0.25f);
                    foreach (var a in _mates) if (a.gameObject.activeSelf) a.Play("Cheer", 0.3f, from: Random.value);
                    _keeper.ShowFace(Athlete.Face.Sad, 2.5f);
                    break;
                case FootballEvent.Saved:
                    GameAudio.Sfx("sport_glove");
                    GameAudio.Sfx("tennis_aww", 0.6f);
                    Say(SportsStrings.KeeperSaved, Coral, life: 1.8f);
                    _you.Play("HandsOnHead", 0.3f);
                    _keeper.ShowFace(Athlete.Face.Joy, 2f);
                    break;
                case FootballEvent.Post:
                    GameAudio.Sfx("sport_thud");
                    GameAudio.Sfx("tennis_aww", 0.7f);
                    Kit.Fx3.Shake(0.15f);
                    Say(SportsStrings.Post, Sun, life: 1.8f);
                    _you.Play("HandsOnHead", 0.3f);
                    break;
                case FootballEvent.Blocked:
                    GameAudio.Sfx("sport_bump");
                    Say(SportsStrings.Wall, Coral, life: 1.8f);
                    _you.Play("Sad", 0.3f);
                    break;
                case FootballEvent.Wide:
                    GameAudio.Sfx("tennis_aww", 0.7f);
                    Say(SportsStrings.Wide, Coral, life: 1.6f);
                    if (m.Attacking) _you.Play("HandsOnHead", 0.3f);
                    else
                    {
                        _youKeeper.Play("FistPump", 0.2f);
                        _striker.Play("HandsOnHead", 0.3f);
                    }
                    break;
                case FootballEvent.Shot:
                    GameAudio.Sfx("sport_kick");
                    if (_trail != null) _trail.emitting = true;
                    _venue?.Fans?.Buzz(0.3f);
                    break;
                case FootballEvent.PlayerSaved:
                    GameAudio.Sfx("sport_glove");
                    GameAudio.Sfx("tennis_cheer", 0.8f);
                    _venue?.Fans?.Cheer(0.9f, 3f);
                    _youKeeper.Flash(Sun, 0.35f);
                    Kit.Fx3.Sparks(W(m.Ball), 14, Sun, 3f, 0.16f);
                    Say(SportsStrings.Saved, Sky, life: 2f);
                    if (!_diving) _youKeeper.PlayThen("FistPump", "Cheer", 0.2f);
                    _striker.Play("HandsOnHead", 0.3f);
                    _flashHome = 0.6f;
                    break;
                case FootballEvent.Conceded:
                    GameAudio.Sfx("sport_net");
                    GameAudio.Sfx("tennis_aww", 0.6f);
                    _ourNet?.Hit(W(m.Ball), new Vector3(m.BallVelocity.x, m.BallVelocity.y, -1f) * 16f);
                    _flashAway = 1f;
                    Say(SportsStrings.TheyScored, Coral, life: 1.8f);
                    if (!_diving) _youKeeper.Play("Sad", 0.4f);
                    _striker.Play("Airplane", 0.25f);
                    break;
                case FootballEvent.Over:
                    GameAudio.Sfx("sport_whistle", vary: 0f);
                    bool won = m.Won, draw = m.Goals == m.Conceded;
                    Say(won ? SportsStrings.MatchWin : draw ? SportsStrings.MatchDraw : SportsStrings.MatchLose, won ? Sun : draw ? Sky : Coral, life: 2.8f);
                    if (won) _venue?.Fans?.Cheer(1f, 4f);
                    (m.Attacking ? _you : _youKeeper).Play(won ? "Cheer" : draw ? "Clap" : "Sad", 0.3f);
                    break;
            }
        }

        /// <summary>Their keeper dives the way the rules say, the moment the ball is struck.</summary>
        void KeeperReacts()
        {
            var m = _match;
            float x = m.KeeperTarget;
            if (Mathf.Abs(x) < 0.6f) _keeper.Play(m.Target.y > 1.5f ? "JumpCatch" : "Ready", 0.08f, from: 0.1f);
            else _keeper.Play(x > 0f ? "DiveL" : "DiveR", 0.08f, from: 0.08f);   // facing us: its left is the world's +x
        }

        // ---------------------------------------------------------------- the film

        void Direct()
        {
            var m = _match;
            // the passer passes as the rules start the pass (its contact in time)
            if (m.Attacking && m.Situation == FootballSituation.Pass && m.Phase == FootballPhase.Ready)
            {
                var pass = AthleteData.Find(m.PassFrom > 0 ? "PassL" : "PassR");
                if (m.PhaseTime >= 1.4f - pass.Contact && _mates[0].Playing != pass.Name)
                    _mates[0].PlayThen(pass.Name, "Ready", 0.1f, 1f, 0.3f);
            }
            if (!m.Attacking && m.Phase == FootballPhase.RunUp)
            {
                // the striker's run-up, the shot timed to its kick
                var kick = AthleteData.Find("KickR");
                if (m.PhaseTime < 1.25f - kick.Contact)
                {
                    if (_striker.Playing != "Run") _striker.Play("Run", 0.15f, 1.3f);
                }
                else if (_striker.Playing != "KickR") _striker.PlayThen("KickR", "Idle", 0.08f);
                if (m.PhaseTime > 0.3f && _away[0].gameObject.activeSelf && _away[0].Playing != "Run") _away[0].Play("Run", 0.2f, 1.1f);
            }
        }

        void Arrange(float dt)
        {
            var m = _match;
            var p = Kit.Puppet;
            if (m.Attacking)
            {
                // their keeper's dive across the goal
                if (m.Phase is FootballPhase.Flight or FootballPhase.Outcome && Mathf.Abs(m.KeeperTarget) >= 0.6f)
                    _keeper.transform.position = W(new Vector3(m.KeeperTarget * 0.62f * Mathf.SmoothStep(0f, 1f, m.KeeperDive), 0f, m.Distance - 0.45f));
                var ball = W(m.Ball);
                foreach (var a in new[] { _keeper, _away[0], _away[1], _mates[1], _ref })
                    if (a.gameObject.activeSelf) a.LookAt(ball + Vector3.up * 0.2f);
            }
            else
            {
                // the player's keeper: across the goal with their steps, the gloves on their hands
                float rootX = p.Root.x;
                var keeperAt = W(new Vector3(rootX, Mathf.Max(0f, p.Root.y), 0.45f));
                if (!_diving) _youKeeper.transform.position = Vector3.Lerp(_youKeeper.transform.position, keeperAt, 1f - Mathf.Exp(-14f * dt));
                bool body = !Kit.UsingKeys && p.BodySeen;
                for (int s = 0; s < 2; s++)
                {
                    var hand = p.Hand(s == 1);
                    var wh = W(new Vector3(hand.x, Mathf.Max(0.2f, hand.y), 0.75f));
                    _youKeeper.Reach(s, body && !_diving ? wh : (Vector3?)null);
                }
                _youKeeper.LookAt(W(m.Ball));
                // stretching toward a shot that is about to arrive: the athlete dives that way
                if (m.Phase == FootballPhase.Flight && !_diving)
                {
                    float until = m.Ball.z / Mathf.Max(1f, -m.BallVelocity.z);
                    float dx = m.Target.x - rootX;
                    bool stretching = Kit.UsingKeys ? Mathf.Abs(dx) > 0.9f
                                    : Mathf.Abs(dx) > 0.9f && (dx > 0 ? p.Hand(true).x - rootX > 0.45f : rootX - p.Hand(false).x > 0.45f);
                    if (until < 0.34f && stretching)
                    {
                        _diving = true;
                        _youKeeper.Play(dx > 0 ? "DiveR" : "DiveL", 0.06f, 1.1f, 0.12f);   // facing away from us: its right is +x
                    }
                }
                foreach (var a in new[] { _mates[0], _mates[1], _ref })
                    if (a.gameObject.activeSelf) a.LookAt(W(m.Ball) + Vector3.up * 0.2f);
                // the striker comes up to the ball
                if (m.Phase == FootballPhase.RunUp)
                {
                    float k = Mathf.Clamp01(m.PhaseTime / 1.2f);
                    _striker.transform.position = W(new Vector3(0.095f, 0f, Mathf.Lerp(m.Distance + 3.6f, m.Distance - 0.2f, k)));
                    if (_away[0].gameObject.activeSelf) _away[0].transform.position += _away[0].transform.forward * (2.4f * dt);
                }
            }
            // the ball: rolling, flying, spinning; its ring as the moment to kick comes
            var ballW = W(m.Ball);
            float speed = m.BallVelocity.magnitude + (m.Phase == FootballPhase.Pass ? 4f : 0f);
            _spin += speed * dt * 330f;
            _ball.SetPositionAndRotation(ballW, Quaternion.Euler(_spin, _spin * 0.27f, 0f));
            float until2 = m.UntilKick;
            bool ring = m.Attacking && !float.IsNaN(until2) && until2 > -FootballMatch.KickWindow && until2 < 1f;
            _ring.gameObject.SetActive(ring);
            if (ring)
            {
                _ring.SetPositionAndRotation(ballW, Kit.Cam.transform.rotation);
                float size = m.Phase == FootballPhase.Aim ? 0.75f + 0.15f * Mathf.Sin(UnityEngine.Time.time * 6f) : Mathf.Lerp(0.35f, 1.6f, Mathf.Clamp01(until2));
                _ring.localScale = Vector3.one * size;
                var c = m.Phase == FootballPhase.Aim || Mathf.Abs(until2) < 0.15f ? Leaf : Color.Lerp(Sun, Color.white, Mathf.Clamp01(until2));
                _tint.SetColor("_BaseColor", c);
                _ring.GetComponent<Renderer>().SetPropertyBlock(_tint);
            }
            var pos = WantedCamera(out var look, out float fov);
            bool slow = _slowAt >= 0f && UnityEngine.Time.unscaledTime - _slowAt < 1.7f;
            _shot.Follow(pos, look, fov, slow ? 2.5f : 3.2f, Kit.Fx3.ShakeOffset);
            _flashHome = Mathf.MoveTowards(_flashHome, 0f, UnityEngine.Time.unscaledDeltaTime * 0.8f);
            _flashAway = Mathf.MoveTowards(_flashAway, 0f, UnityEngine.Time.unscaledDeltaTime * 0.8f);
        }

        /// <summary>Where the camera wants to be: behind the player's athlete looking at the goal; following the shot;
        /// low beside the goal in slow motion; behind the net while keeping.</summary>
        Vector3 WantedCamera(out Vector3 look, out float fov)
        {
            var m = _match;
            if (m.Attacking)
            {
                var goal = new Vector3(0f, 1.3f, GoalLine);
                if (_slowAt >= 0f && UnityEngine.Time.unscaledTime - _slowAt < 1.7f)
                {
                    // slow motion from beside the goal, the ball coming in
                    float side = m.Target.x >= 0f ? 1f : -1f;
                    look = Vector3.Lerp(goal, W(m.Ball), 0.6f);
                    fov = 44f;
                    return new Vector3(side * 9.5f, 1.6f, GoalLine - 7.5f);
                }
                var you = W(new Vector3(-0.05f, 0f, -0.05f));
                if (m.Phase is FootballPhase.Flight or FootballPhase.Outcome)
                {
                    var ball = W(m.Ball);
                    look = Vector3.Lerp(goal, ball, 0.35f);
                    fov = 46f;
                    return you + new Vector3(0.6f, 2.6f, -3.6f) + Vector3.forward * (Mathf.Clamp01(m.PhaseTime) * 2.5f);
                }
                look = Vector3.Lerp(goal, W(m.Ball), 0.15f) + Vector3.down * 0.2f;
                fov = 50f;
                return you + new Vector3(0.9f, 2.2f, -4.4f);
            }
            var p = Kit.Puppet;
            look = new Vector3(p.Root.x * 0.25f, 1.2f, -GoalLine + 13f);
            fov = 54f;
            return new Vector3(p.Root.x * 0.3f, 2.25f, -GoalLine - 4.6f);
        }

        /// <summary>A goal on its way: the last moments in slow motion.</summary>
        void SlowMotion()
        {
            var m = _match;
            if (m.Attacking && m.Phase == FootballPhase.Flight && m.Predicted == ShotEnd.Goal && !_slowDone)
            {
                float until = (m.Distance - m.Ball.z) / Mathf.Max(1f, m.BallVelocity.z);
                if (until < 0.32f)
                {
                    _slowDone = true;
                    _slowAt = UnityEngine.Time.unscaledTime;
                }
            }
            if (_slowAt < 0f) return;
            float t = UnityEngine.Time.unscaledTime - _slowAt;
            UnityEngine.Time.timeScale = t < 0.9f ? 0.3f : t < 1.4f ? Mathf.Lerp(0.3f, 1f, (t - 0.9f) / 0.5f) : 1f;
            if (t >= 1.7f) _slowAt = -1f;
        }

        // ---------------------------------------------------------------- HUD

        public override void DrawHud(float width)
        {
            var m = _match;
            SportHud.Scoreboard(36f, 28f, SportsStrings.Us, Sky, m.Goals, SportsStrings.Them, Coral, m.Conceded,
                                SportsStrings.Round(m.Round, FootballMatch.Rounds), _flashHome, _flashAway);
            // the kick prompt by the ball
            float until = m.UntilKick;
            if (m.Attacking && !float.IsNaN(until) && until < 0.7f && until > -FootballMatch.KickWindow && Kit.Cam != null)
            {
                var sp = Kit.Cam.WorldToScreenPoint(W(m.Ball) + Vector3.up * 0.9f);
                if (sp.z > 0f)
                {
                    var g = GameHud.FromScreen(sp);
                    SportHud.Pill(g.x, g.y - 70f, m.Phase == FootballPhase.Aim ? SportsStrings.KickReady : SportsStrings.KickNow, Leaf, 40);
                }
            }
            _banners.RemoveAll(b => UnityEngine.Time.unscaledTime - b.at > b.life);
            if (_banners.Count > 0)
            {
                var b = _banners[_banners.Count - 1];
                SportHud.Banner(width, 330f, b.text, b.colour, UnityEngine.Time.unscaledTime - b.at, b.life, 130, b.sub);
            }
        }
    }
}
