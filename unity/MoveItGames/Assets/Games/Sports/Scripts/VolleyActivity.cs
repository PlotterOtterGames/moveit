using System.Collections.Generic;
using MoveIt.Motion;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using Joint = MoveIt.Motion.Joint;

namespace MoveIt.Games.Sports
{
    /// <summary>
    /// "Volejboll" (Kinect Sports' beach volleyball): two against two on a beach of the Albanian coast, the sea and the
    /// mountains behind the net. The player's athlete and a partner (the player's colours) against two; everyone runs
    /// to the ball by themselves and plays it with their own move, timed to the ball (serve, bump, set, spike, dig,
    /// block); the player only has to move at the right moment (a ring closes on the ball, <see cref="VolleyMatch"/>):
    /// any quick arm move bumps, serves or spikes (a jump spikes too). Points end with a high five or a slump; the
    /// bleachers cheer. Without a camera: space.
    /// </summary>
    public sealed class VolleyActivity : Activity
    {
        static readonly Color Sun = SportHud.Sun, Coral = SportHud.Coral, Sky = SportHud.Sky, Grape = SportHud.Grape, Leaf = SportHud.Leaf;
        static readonly Color Night = SportHud.Night;

        VolleyMatch _match;
        readonly ArmFlick _flick = new ArmFlick { MinSpeed = 2.8f };
        Venue _venue;
        Athlete _you, _partner, _left, _right, _ref;
        Transform _ball, _ring, _shadow;
        TrailRenderer _trail;
        Material _trailMat;
        ShotCamera _shot;
        MaterialPropertyBlock _tint;
        double _lastSample;
        float _spin, _overFor;
        bool _computerTouchPlayed, _servePlayed;
        float _celebrateUntil;
        bool _usPoint;
        Athlete _digger;
        readonly List<(string text, string sub, Color colour, float at, float life)> _banners = new List<(string, string, Color, float, float)>();
        GameObject _net;

        public VolleyMatch Match => _match;
        public int Moves { get; private set; }

        public override string Id => "volley";
        public override string Title => SportsStrings.VolleyTitle;
        public override string HowTo => SportsStrings.VolleyHowTo;
        public override string KeyboardHelp => SportsStrings.VolleyKeys;
        /// <summary>Arm moves and jumps: the knees in the picture, room above.</summary>
        public override PlayArea Area { get; } = new PlayArea(BodyPart.Knees, side: 0.4f, above: 0.4f);
        public override string Music => "volley";
        public override Color Colour => new Color(1f, 0.6f, 0.25f);
        public override int[] MedalNeeds => VolleyMatch.Needs;
        public override string ResultLine => _match == null ? "" : SportsStrings.VolleyResult(_match.Us, _match.Them);

        protected override void OnBuild()
        {
            _match = new VolleyMatch(Random.Range(0, 1 << 20));
            _match.Events += OnMatch;
            _flick.Reset();
            _tint = new MaterialPropertyBlock();
            Daylight.Beach.Apply(Kit);
            _venue = Venue.Load(Venue.Kind.Beach, Kit.Root, seed: 5, occupancy: 0.9f, water: Kit.Water);
            if (SportsArt.Instance?.net != null) _net = BuildNet(SportsArt.Instance.net);

            var us = SportsGame.MyLook(Wear.Beach).Team(Sun, Sky, Sky, Color.white);
            _you = Athlete.Create("You", us, Kit.Root);
            var partner = AthleteLook.Person(31, Wear.Beach).Team(Sun, Sky, Sky, Color.white);
            _partner = Athlete.Create("Partner", partner, Kit.Root);
            AthleteLook Them(int seed)
            {
                var l = AthleteLook.Person(seed, Wear.Beach).Team(Coral, Night, Coral, Sun);
                l.WearsHeadband = seed % 2 == 0;
                l.Headband = Sun;
                return l;
            }
            _left = Athlete.Create("Opponent L", Them(52), Kit.Root);
            _right = Athlete.Create("Opponent R", Them(57), Kit.Root);
            var referee = AthleteLook.Person(88, Wear.Football).Team(Night, Night, Night, Sun);
            _ref = Athlete.Create("Referee", referee, Kit.Root);
            _ref.transform.SetPositionAndRotation(new Vector3(5.6f, 1.64f, 0f), Quaternion.Euler(0f, -90f, 0f));
            _ref.Play("Idle", 0f);
            foreach (var a in new[] { _left, _right }) a.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            _you.transform.position = _match.PlayerSpot;
            _partner.transform.position = _match.PartnerSpot;
            _left.transform.position = _match.TheirLeft;
            _right.transform.position = _match.TheirRight;
            foreach (var a in new[] { _you, _partner, _left, _right }) a.Play("Ready", 0f, from: Random.value);

            _trailMat = Kit.Fx != null ? new Material(Kit.Fx) : null;
            _ball = SportsBalls.Create("Ball", SportsBalls.Volleyball, Kit.Actor, _trailMat, Kit.Root, Sun);
            _trail = _ball.GetComponent<TrailRenderer>();
            _shadow = PartMesh.Show("Ball shadow", Kit.Root, PartMesh.Primitive(PrimitiveType.Quad), Kit.Fx).transform;
            _shadow.rotation = Quaternion.Euler(90f, 0f, 0f);
            _tint.SetColor("_BaseColor", new Color(0.35f, 0.22f, 0.1f, 0.3f));
            _shadow.GetComponent<Renderer>().SetPropertyBlock(_tint);
            _ring = PartMesh.Show("Ring", Kit.Root, ArtShapes.Ring(0.4f), Kit.Marker).transform;
            _ring.gameObject.SetActive(false);
            var p = Kit.Puppet;
            p.KeyRoot = Vector2.zero;
            p.KeyRig = null;
            _shot = new ShotCamera(Kit.Cam);
            var pos = WantedCamera(out var look, out float fov);
            _shot.Cut(pos, look, fov);
        }

        public override void Dispose()
        {
            if (Kit.Water != null) Kit.Water.SetFloat("_FlowOffset", 0f);
            _venue?.Dispose();
            if (_trailMat != null) Object.Destroy(_trailMat);
        }

        /// <summary>The net's mesh between the posts: 1 m deep, its cords in 10 cm cells (Tundu/Net).</summary>
        GameObject BuildNet(Material material)
        {
            var go = new GameObject("Net");
            go.transform.SetParent(Kit.Root, false);
            float w = 4.85f, top = VolleyMatch.NetHeight - 0.07f, bottom = VolleyMatch.NetHeight - 1.0f;
            var mesh = new Mesh { name = "Volleyball net" };
            mesh.SetVertices(new List<Vector3> { new Vector3(-w, bottom, 0f), new Vector3(-w, top, 0f), new Vector3(w, top, 0f), new Vector3(w, bottom, 0f) });
            mesh.SetUVs(0, new List<Vector2> { new Vector2(0f, 0f), new Vector2(0f, (top - bottom) / 0.1f), new Vector2(2f * w / 0.1f, (top - bottom) / 0.1f), new Vector2(2f * w / 0.1f, 0f) });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.On;
            return go;
        }

        void Say(string text, Color colour, string sub = null, float life = 1.4f) => _banners.Add((text, sub, colour, UnityEngine.Time.unscaledTime, life));

        // ---------------------------------------------------------------- play

        public override void Step(float dt, bool playing)
        {
            ReadBody(playing);
            if (playing && Kit.UsingKeys && KeyDown(Key.Space)) Act(6f, false);
            if (playing)
            {
                Time += dt;
                _match.Step(dt);
                Points = _match.Points;
                if (_match.Over) _overFor += dt;
                if (_overFor > 3f) Finished = true;
            }
            if (Kit.Water != null) Kit.Water.SetFloat("_FlowOffset", UnityEngine.Time.time * 0.25f);
            Direct();
            Arrange(dt);
        }

        void ReadBody(bool playing)
        {
            if (playing && Kit.Input != null && Kit.Input.JumpTriggered && !Kit.UsingKeys) Act(0f, true);
            var body = MotionSystem.Instance != null ? MotionSystem.Instance.ActiveBody : null;
            if (body == null || !body.IsTracking || body.SampleTime == _lastSample) return;
            _lastSample = body.SampleTime;
            var f = body.Fast;
            if (!_flick.Update(body.SampleTime, body.FastShoulderCenter, body.FastTorsoLength, f[(int)Joint.LeftWrist], body.Visibility[(int)Joint.LeftWrist],
                               f[(int)Joint.RightWrist], body.Visibility[(int)Joint.RightWrist], out var flick)) return;
            Moves++;
            if (playing) Act(flick.Speed, false);
        }

        void Act(float speed, bool jump)
        {
            var m = _match;
            var stroke = m.Phase == VolleyPhase.Serve ? VolleyStroke.OurServe : m.Stroke;
            bool counted = m.Act(speed, jump);
            Debug.Log($"[Sports] Volley move ({(jump ? "jump" : $"arm {speed:0.0}")}) {m.UntilAct:0.00} s from the moment: {(counted ? "played" : "nothing to play")}");
            if (!counted) return;
            // the player's athlete plays its move, the contact now
            string clip = stroke == VolleyStroke.OurServe ? "Serve" : stroke == VolleyStroke.OurSet ? "Spike" : "Bump";
            var c = AthleteData.Find(clip);
            _you.PlayThen(clip, "Ready", 0.06f, 1f, 0.3f, from: Mathf.Max(0f, c.Contact - 0.05f));
        }

        void OnMatch(VolleyEvent e)
        {
            var m = _match;
            switch (e)
            {
                case VolleyEvent.Toss:
                    _servePlayed = false;
                    _celebrateUntil = 0f;
                    if (_trail != null)
                    {
                        _trail.Clear();
                        _trail.emitting = false;
                    }
                    if (m.OurServe) Say(SportsStrings.YourServe, Sun, life: 1.3f);
                    break;
                case VolleyEvent.Hit:
                    GameAudio.Sfx("sport_volley", 0.8f);
                    if (_trail != null) _trail.emitting = m.Stroke is VolleyStroke.OurSpike or VolleyStroke.TheirSpike;
                    _computerTouchPlayed = false;
                    _venue?.Fans?.Buzz(m.Stroke is VolleyStroke.OurSet or VolleyStroke.TheirSet ? 0.45f : 0.2f);
                    break;
                case VolleyEvent.Bump:
                    Kit.Fx3.Puff(_you.transform.position + Vector3.up * 0.05f, new Color(0.95f, 0.85f, 0.6f, 0.8f), 0.8f, false, 0.5f, Vector3.up);
                    break;
                case VolleyEvent.Spike:
                    GameAudio.Sfx("tennis_power", 0.8f);
                    Kit.Fx3.Shake(0.08f);
                    break;
                case VolleyEvent.PerfectSpike:
                    GameAudio.Sfx("tennis_power");
                    Kit.Fx3.Shake(0.16f);
                    Kit.Fx3.Sparks(m.Ball, 16, Sun, 4f, 0.18f);
                    Say(SportsStrings.PerfectSpike, Sun, life: 1.2f);
                    break;
                case VolleyEvent.Tap:
                    _partner.PlayThen("Set", "Ready", 0.06f, 1f, 0.3f, from: AthleteData.Find("Set").Contact - 0.05f);
                    break;
                case VolleyEvent.Dig:
                    GameAudio.Sfx("sport_volley", 0.6f, 0.9f);
                    break;
                case VolleyEvent.Missed:
                    _you.PlayThen("Dig", "Ready", 0.08f, 1.3f, 0.4f, from: 0.1f);
                    break;
                case VolleyEvent.PointUs:
                case VolleyEvent.PointThem:
                    bool ours = e == VolleyEvent.PointUs;
                    _usPoint = ours;
                    _celebrateUntil = UnityEngine.Time.time + 1.7f;
                    GameAudio.Sfx(ours ? "tennis_cheer" : "tennis_aww", 0.8f);
                    _venue?.Fans?.Cheer(ours ? 0.95f : 0.4f, 2.5f);
                    Kit.Fx3.Puff(new Vector3(m.Ball.x, 0.1f, m.Ball.z), new Color(0.95f, 0.85f, 0.6f, 0.9f), 1.3f, false, 0.6f, Vector3.up);
                    Say(ours ? SportsStrings.PointUs : SportsStrings.PointThem, ours ? Sun : Coral, life: 1.5f);
                    if (_trail != null) _trail.emitting = false;
                    Celebrate(ours);
                    break;
                case VolleyEvent.Won:
                    _venue?.Fans?.Cheer(1f, 4f);
                    Say(SportsStrings.MatchWon, Sun, life: 2.6f);
                    _you.Play("Cheer", 0.3f);
                    _partner.Play("Cheer", 0.3f, from: 0.3f);
                    _left.Play("Sad", 0.4f);
                    _right.Play("HandsOnHead", 0.4f);
                    break;
                case VolleyEvent.Lost:
                    Say(SportsStrings.MatchLost, Coral, life: 2.6f);
                    _you.Play("Sad", 0.4f);
                    _partner.Play("HandsOnHead", 0.4f);
                    _left.Play("Cheer", 0.3f);
                    _right.Play("Cheer", 0.3f, from: 0.4f);
                    break;
            }
        }

        /// <summary>A point: the winners high-five (turned to each other), the others slump.</summary>
        void Celebrate(bool ours)
        {
            var (a, b) = ours ? (_you, _partner) : (_left, _right);
            var (c, d) = ours ? (_left, _right) : (_you, _partner);
            // the partner comes over (the camera cuts to them: nobody sees the step)
            var across = ours ? Vector3.right : Vector3.left;
            b.transform.position = a.transform.position + across * 0.95f;
            var mid = (a.transform.position + b.transform.position) * 0.5f;
            a.transform.rotation = Quaternion.LookRotation(Flat(mid - a.transform.position, a.transform.forward));
            b.transform.rotation = Quaternion.LookRotation(Flat(mid - b.transform.position, b.transform.forward));
            a.PlayThen("HighFive", "Ready", 0.15f, 1f, 0.3f);
            b.PlayThen("HighFive", "Ready", 0.15f, 1f, 0.3f);
            c.PlayThen("Sad", "Ready", 0.25f, 1.2f, 0.4f);
            d.PlayThen(ours ? "HandsOnHead" : "Sad", "Ready", 0.25f, 1.2f, 0.4f);
        }

        static Vector3 Flat(Vector3 v, Vector3 fallback)
        {
            v.y = 0f;
            return v.sqrMagnitude < 1e-4f ? fallback : v;
        }

        // ---------------------------------------------------------------- the others' moves, timed to the ball

        void Direct()
        {
            var m = _match;
            if (m.Phase == VolleyPhase.Serve)
            {
                // the server tosses and hits: the clip's contact at the top of the toss
                var serve = AthleteData.Find("Serve");
                var server = m.OurServe ? _you : _right;
                if (!_servePlayed && m.PhaseTime >= VolleyMatch.TossTop - serve.Contact)
                {
                    _servePlayed = true;
                    server.PlayThen("Serve", "Ready", 0.1f, 1f, 0.3f);
                }
                return;
            }
            if (m.Phase != VolleyPhase.Flight || _computerTouchPlayed) return;
            float until = m.Duration - m.PhaseTime;
            Athlete who = null;
            string clip = null;
            switch (m.Stroke)
            {
                case VolleyStroke.OurBump:
                    who = _partner;
                    clip = "Set";
                    break;
                case VolleyStroke.OurServe:
                case VolleyStroke.OurSpike:
                case VolleyStroke.OurTap:
                    who = _digger = m.To.x < 0f ? _left : _right;
                    clip = m.Stroke == VolleyStroke.OurSpike ? "Dig" : "Bump";
                    break;
                case VolleyStroke.TheirDig:
                    who = Setter;
                    clip = "Set";
                    break;
                case VolleyStroke.TheirSet:
                    who = _digger;
                    clip = "Spike";
                    break;
            }
            if (who == null) return;
            var c = AthleteData.Find(clip);
            if (until <= c.Contact)
            {
                _computerTouchPlayed = true;
                who.PlayThen(clip, "Ready", 0.08f, 1f, 0.35f, from: Mathf.Max(0f, c.Contact - until));
            }
        }

        // ---------------------------------------------------------------- drawing

        void Arrange(float dt)
        {
            var m = _match;
            float glide = 1f - Mathf.Exp(-4.5f * dt);
            bool celebrating = UnityEngine.Time.time < _celebrateUntil;
            Move(_you, YouSpot(), glide, celebrating);
            Move(_partner, PartnerSpot(), glide, celebrating);
            Move(_left, TheirSpot(true), glide, celebrating);
            Move(_right, TheirSpot(false), glide, celebrating);
            var ball = m.Ball;
            foreach (var a in new[] { _you, _partner, _left, _right, _ref }) a.LookAt(ball);

            _spin += dt * (m.Stroke is VolleyStroke.OurSpike or VolleyStroke.TheirSpike ? 1500f : 520f);
            _ball.SetPositionAndRotation(ball, Quaternion.Euler(_spin, _spin * 0.4f, 0f));
            _shadow.position = new Vector3(ball.x, 0.02f, ball.z);
            _shadow.localScale = Vector3.one * (0.45f + ball.y * 0.07f);
            // the ring closes on the ball at the player's moment
            float until = m.UntilAct;
            bool ring = m.PlayerToAct && until < 1f && until > -VolleyMatch.Window;
            _ring.gameObject.SetActive(ring);
            if (ring)
            {
                _ring.SetPositionAndRotation(ball, Kit.Cam.transform.rotation);
                _ring.localScale = Vector3.one * Mathf.Lerp(0.5f, 2.2f, Mathf.Clamp01(until));
                var c = Mathf.Abs(until) <= VolleyMatch.Perfect ? Leaf : Color.Lerp(Sun, Color.white, Mathf.Clamp01(until));
                _tint.SetColor("_BaseColor", c);
                _ring.GetComponent<Renderer>().SetPropertyBlock(_tint);
            }
            var pos = WantedCamera(out var look, out float fov);
            _shot.Follow(pos, look, fov, celebrating ? 2.5f : 2.2f, Kit.Fx3.ShakeOffset);
        }

        /// <summary>Glides an athlete to its spot (shuffling sideways when it is far), or leaves it be while it
        /// celebrates or dives.</summary>
        void Move(Athlete a, Vector3 spot, float glide, bool celebrating)
        {
            string playing = a.Playing;
            bool busy = celebrating || playing is "Dig" or "HighFive" or "Spike" or "Serve" or "Sad" or "HandsOnHead" or "Cheer";
            var at = a.transform.position;
            var d = spot - at;
            d.y = 0f;
            if (!busy)
            {
                a.transform.position = Vector3.Lerp(at, spot, glide);
                var face = a == _left || a == _right ? Vector3.back : Vector3.forward;
                a.transform.rotation = Quaternion.Slerp(a.transform.rotation, Quaternion.LookRotation(face), glide);
                if (d.magnitude > 0.6f && playing is "Ready" or "ShuffleL" or "ShuffleR")
                {
                    // sideways in the athlete's own frame: its left is -x when it faces +z
                    float side = Vector3.Dot(d, a.transform.right);
                    a.Play(side < 0f ? "ShuffleL" : "ShuffleR", 0.15f, 1.2f);
                }
                else if (d.magnitude < 0.25f && playing is "ShuffleL" or "ShuffleR") a.Play("Ready", 0.2f);
            }
        }

        Vector3 YouSpot()
        {
            var m = _match;
            // the bump and the spike: stand so the ball meets the arms
            if (m.Phase == VolleyPhase.Flight && m.Stroke == VolleyStroke.OurSet) return new Vector3(m.To.x - 0.22f, 0f, m.To.z - 0.33f);
            return m.PlayerSpot;
        }

        Vector3 PartnerSpot()
        {
            var m = _match;
            if (m.Phase == VolleyPhase.Flight && m.Stroke == VolleyStroke.OurBump) return new Vector3(m.To.x, 0f, m.To.z - 0.3f);
            return m.PartnerSpot;
        }

        /// <summary>Their setter: the one who didn't dig.</summary>
        Athlete Setter => _digger == _left ? _right : _left;

        Vector3 TheirSpot(bool left)
        {
            var m = _match;
            var a = left ? _left : _right;
            if (m.Phase == VolleyPhase.Flight && _digger != null)
            {
                // facing us: an athlete's own right hand is at its -x (the spike's hand)
                if (m.Stroke == VolleyStroke.TheirDig && a == Setter) return new Vector3(m.To.x, 0f, m.To.z + 0.3f);
                if (m.Stroke == VolleyStroke.TheirSet && a == _digger) return new Vector3(m.To.x + 0.22f, 0f, m.To.z + 0.33f);
            }
            return left ? m.TheirLeft : m.TheirRight;
        }

        /// <summary>Behind our baseline, a little to the player's side; closer and lower after a point.</summary>
        Vector3 WantedCamera(out Vector3 look, out float fov)
        {
            var m = _match;
            var you = _you != null ? _you.transform.position : m.PlayerSpot;
            if (UnityEngine.Time.time < _celebrateUntil && _you != null)
            {
                var pair = _usPoint ? (_you.transform.position + _partner.transform.position) * 0.5f : (_left.transform.position + _right.transform.position) * 0.5f;
                look = pair + Vector3.up * 1.15f;
                fov = 38f;
                return pair + new Vector3(_usPoint ? 1.8f : -1.8f, 1.55f, _usPoint ? 5.2f : -5.2f);
            }
            bool spikeMoment = m.Phase == VolleyPhase.Flight && m.Stroke == VolleyStroke.OurSet;
            look = new Vector3(you.x * 0.25f, 1.4f, spikeMoment ? 1.2f : 0.5f);
            fov = spikeMoment ? 44f : 48f;
            return new Vector3(you.x * 0.45f, spikeMoment ? 3.8f : 4.4f, spikeMoment ? -11.5f : -14f);
        }

        // ---------------------------------------------------------------- HUD

        public override void DrawHud(float width)
        {
            var m = _match;
            SportHud.Scoreboard(36f, 28f, SportsStrings.Us, Sun, m.Us, SportsStrings.Them, Coral, m.Them, SportsStrings.FirstTo(VolleyMatch.Target));
            if (m.PlayerToAct)
            {
                float until = m.UntilAct;
                if (until <= 0.7f && until >= -VolleyMatch.Window && Kit.Cam != null)
                {
                    string what = m.Phase == VolleyPhase.Serve ? SportsStrings.Serve : m.Stroke == VolleyStroke.OurSet ? SportsStrings.Spike : SportsStrings.Bump;
                    var sp = Kit.Cam.WorldToScreenPoint(m.Ball + Vector3.up * 0.6f);
                    if (sp.z > 0f)
                    {
                        var g = GameHud.FromScreen(sp);
                        SportHud.Pill(g.x, g.y - 70f, what, Leaf, 40);
                    }
                }
            }
            _banners.RemoveAll(b => UnityEngine.Time.unscaledTime - b.at > b.life);
            if (_banners.Count > 0)
            {
                var b = _banners[_banners.Count - 1];
                SportHud.Banner(width, 330f, b.text, b.colour, UnityEngine.Time.unscaledTime - b.at, b.life, 120, b.sub);
            }
        }
    }
}
