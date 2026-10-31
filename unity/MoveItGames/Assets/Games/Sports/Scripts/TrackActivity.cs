using System.Collections.Generic;
using MoveIt.Motion;
using UnityEngine;
using UnityEngine.InputSystem;
using Joint = MoveIt.Motion.Joint;

namespace MoveIt.Games.Sports
{
    /// <summary>
    /// Sporte!'s 100 metres (Kinect Sports' sprint) in the athletics stadium: four runners, the player's athlete in the
    /// lane nearest the camera. A line-up while the how-to shows, "on your marks" (kneeling in the blocks), "set" (hips
    /// up), the gun; running on the spot runs (<see cref="RunDetector"/>, <see cref="TrackRace"/>), filmed from the side
    /// as in the broadcasts, faster and wider as the runner speeds up; the last metres face the runners coming at the
    /// line and a lean forward (<see cref="DipDetector"/>) dips; the finish goes slow, then the results board. A ghost bar
    /// runs at the pace of the player's best. Without a camera: ← → pressed in turn run, ↓ (or space) leans.
    /// </summary>
    public sealed class TrackActivity : Activity
    {
        static readonly Color Sun = SportHud.Sun, Coral = SportHud.Coral, Sky = SportHud.Sky, Grape = SportHud.Grape, Leaf = SportHud.Leaf;
        static readonly Color Night = SportHud.Night;
        const float LaneW = 1.22f, TrackIn = -4.88f;

        TrackRace _race;
        readonly RunDetector _run = new RunDetector();
        readonly DipDetector _dip = new DipDetector();
        Venue _venue;
        readonly Athlete[] _runners = new Athlete[4];
        readonly int[] _display = new int[4];
        Transform _ghost;
        ShotCamera _shot;
        double _lastSample;
        float _keySpeed;
        readonly Queue<float> _keySteps = new Queue<float>();
        int _lastKey;
        float _overFor, _finishAt = -1f, _slowAt = -1f, _gunAt = -10f;
        float _best;
        readonly List<(string text, string sub, Color colour, float at, float life)> _banners = new List<(string, string, Color, float, float)>();
        MaterialPropertyBlock _tint;

        public TrackRace Race => _race;
        public RunDetector Running => _run;

        public override string Id => "sprint";
        public override string Title => SportsStrings.SprintTitle;
        public override string HowTo => SportsStrings.SprintHowTo;
        public override string KeyboardHelp => SportsStrings.RunKeys;
        /// <summary>Running on the spot: the feet in the picture, a little room above.</summary>
        public override PlayArea Area { get; } = new PlayArea(BodyPart.Ankles, side: 0.3f, above: 0.3f);
        public override string Music => "track";
        public override Color Colour => Coral;
        public override int[] MedalNeeds => TrackRace.Needs;
        public override string ResultLine => _race != null && _race.Human.Finished ? SportsStrings.RaceResult(_race.Human.Finish, _race.HumanPlace) : "";

        protected override void OnBuild()
        {
            Daylight.Afternoon.Apply(Kit);
            _run.Reset();
            _dip.Reset();
            _tint = new MaterialPropertyBlock();
            _venue = Venue.Load(Venue.Kind.Track, Kit.Root, seed: 11, occupancy: 0.8f);
            _race = new TrackRace(Random.Range(0, 1 << 20), SportsStrings.Us);
            _race.Events += OnRace;
            Color[] vests = { Sky, Grape, Leaf };
            int k = 0;
            for (int i = 0; i < 4; i++)
            {
                var r = _race.Runners[i];
                AthleteLook look;
                if (r.Human)
                {
                    look = SportsGame.MyLook(Wear.Track).Team(Coral, Night, Color.white, Sun);
                    look.WearsHeadband = true;
                    look.Headband = Sun;
                    _display[i] = 0;
                }
                else
                {
                    look = AthleteLook.Person(101 + i * 17, Wear.Track).Team(vests[k], Night, Color.white);
                    look.WearsHeadband = k == 1;
                    look.Headband = Coral;
                    look.Shoes = k == 0 ? Sun : k == 1 ? Coral : Sky;
                    _display[i] = ++k;
                }
                _runners[i] = Athlete.Create($"Runner {r.Name}", look, Kit.Root);
                _runners[i].Play("Idle", 0f, from: Random.value * 2f);
            }
            _best = PlayerPrefs.GetInt("sports.best.sprint", -1) is int b && b > 0 ? 22f - b / 100f : -1f;
            if (_best > 0f && Kit.Fx != null)
            {
                // like the broadcasts' record line: a glowing band painted across the player's lane
                _ghost = PartMesh.Show("Best pace", Kit.Root, PartMesh.Primitive(PrimitiveType.Quad), Kit.Fx).transform;
                _ghost.localScale = new Vector3(LaneW * 0.96f, 0.22f, 1f);
                _tint.SetColor("_BaseColor", new Color(1f, 0.85f, 0.3f, 0.85f));
                _ghost.GetComponent<Renderer>().SetPropertyBlock(_tint);
                _ghost.gameObject.SetActive(false);
            }
            _shot = new ShotCamera(Kit.Cam);
            Place(0f);
            var pos = WantedCamera(out var look2, out float fov);
            _shot.Cut(pos, look2, fov);
        }

        public override void Dispose()
        {
            UnityEngine.Time.timeScale = 1f;
            _venue?.Dispose();
        }

        /// <summary>The x of a runner's lane: the player's athlete nearest the side camera (lane 3), the others outside.</summary>
        float LaneX(int runner) => TrackIn + (2.5f + _display[runner]) * LaneW;

        void Say(string text, Color colour, string sub = null, float life = 1.4f) => _banners.Add((text, sub, colour, UnityEngine.Time.unscaledTime, life));

        // ---------------------------------------------------------------- play

        public override void Step(float dt, bool playing)
        {
            float speed = ReadBody(dt);
            if (playing)
            {
                Time += dt;
                if (KeyDown(Key.DownArrow) || KeyDown(Key.Space)) _race.Dip();
                _race.Step(dt, speed);
                Points = _race.Points;
                if (_race.Phase == RacePhase.Over) _overFor += dt;
                if (_overFor > 4.5f) Finished = true;
            }
            Place(dt);
            SlowMotion();
            var pos = WantedCamera(out var look, out float fov);
            _shot.Follow(pos, look, fov, _race.Phase == RacePhase.Running ? 5f : 2.2f, Kit.Fx3.ShakeOffset);
        }

        /// <summary>The camera's new samples to the run and lean detectors; returns the running speed (m/s). Keyboard:
        /// ← → pressed in turn are steps.</summary>
        float ReadBody(float dt)
        {
            var body = MotionSystem.Instance != null ? MotionSystem.Instance.ActiveBody : null;
            if (body != null && body.IsTracking && body.SampleTime != _lastSample)
            {
                _lastSample = body.SampleTime;
                var f = body.Fast;
                float legs = Mathf.Min(Mathf.Max(body.Visibility[(int)Joint.LeftKnee], body.Visibility[(int)Joint.LeftAnkle]),
                                       Mathf.Max(body.Visibility[(int)Joint.RightKnee], body.Visibility[(int)Joint.RightAnkle]));
                _run.Update(body.SampleTime, f[(int)Joint.LeftKnee].y, f[(int)Joint.RightKnee].y, f[(int)Joint.LeftAnkle].y, f[(int)Joint.RightAnkle].y,
                            legs, f[(int)Joint.LeftWrist].y, f[(int)Joint.RightWrist].y, body.FastTorsoLength);
                if (_dip.Update(body.SampleTime, f[(int)Joint.Nose].y, body.FastHipCenter.y, body.FastTorsoLength) && _race.Dip())
                    Debug.Log($"[Sports] Lean at the line, {TrackRace.Length - _race.Human.Distance:0.0} m out");
            }
            // keyboard: steps from ← → pressed in turn (button mashing, like the old track games)
            int key = KeyDown(Key.LeftArrow) || KeyDown(Key.A) ? -1 : KeyDown(Key.RightArrow) || KeyDown(Key.D) ? 1 : 0;
            if (key != 0 && key != _lastKey)
            {
                _keySteps.Enqueue(UnityEngine.Time.time);
                _lastKey = key;
            }
            while (_keySteps.Count > 0 && UnityEngine.Time.time - _keySteps.Peek() > 1.1f) _keySteps.Dequeue();
            float keyCadence = _keySteps.Count >= 2 ? _keySteps.Count / 1.1f : 0f;
            _keySpeed = Mathf.Lerp(_keySpeed, Mathf.Min(_run.MaxSpeed, Mathf.Max(0f, keyCadence - 0.6f) * _run.SpeedPerStep), 1f - Mathf.Exp(-3f * dt));
            return Mathf.Max(_run.Speed, _keySpeed);
        }

        void OnRace(RaceEvent e, Runner r, int value)
        {
            switch (e)
            {
                case RaceEvent.Set:
                    GameAudio.Sfx("count", vary: 0f);
                    _venue?.Fans?.Buzz(0.0f);
                    Say(SportsStrings.SetReady, Sun, life: 1.2f);
                    break;
                case RaceEvent.Gun:
                    GameAudio.Sfx("sport_gun", vary: 0f);
                    _gunAt = UnityEngine.Time.time;
                    _venue?.Fans?.Cheer(0.6f, 2f);
                    _venue?.Fans?.Buzz(0.35f);
                    Kit.Fx3.Puff(new Vector3(TrackIn + LaneW * 8.5f, 1.8f, -1.5f), new Color(1f, 1f, 1f, 0.8f), 0.7f, false, 0.8f, Vector3.up * 0.5f);
                    Say(SportsStrings.Go, Leaf, life: 0.9f);
                    break;
                case RaceEvent.FalseStart:
                    GameAudio.Sfx("sport_whistle", vary: 0f);
                    GameAudio.Sfx("tennis_aww", 0.6f);
                    Say(SportsStrings.FalseStart, Coral, life: 1.8f);
                    break;
                case RaceEvent.Dip:
                    GameAudio.Sfx("whoosh", 0.8f);
                    break;
                case RaceEvent.Finish:
                    if (r.Human)
                    {
                        _finishAt = UnityEngine.Time.time;
                        _slowAt = UnityEngine.Time.unscaledTime;
                        GameAudio.Sfx(value == 1 ? "sport_finish" : "sport_clear", vary: 0f);
                        _venue?.Fans?.Cheer(value == 1 ? 1f : 0.6f, 3f);
                        Kit.Fx3.Shake(0.1f);
                        if (value == 1) Kit.Fx3.Confetti(new Vector3(LaneX(_race.HumanIndex), 3f, 101f), 50, new[] { Sun, Coral, Sky, Grape, Leaf });
                    }
                    break;
            }
        }

        // ---------------------------------------------------------------- the athletes

        void Place(float dt)
        {
            float t = UnityEngine.Time.time;
            for (int i = 0; i < 4; i++)
            {
                var r = _race.Runners[i];
                var a = _runners[i];
                string want;
                float speed = 1f;
                switch (_race.Phase)
                {
                    case RacePhase.Marks:
                        want = "Marks";
                        break;
                    case RacePhase.Set:
                        want = "SetPosition";
                        break;
                    default:
                        if (r.Finished && r.Speed < 4f)
                        {
                            int place = _race.PlaceOf(r);
                            want = place == 1 ? "Cheer" : place <= 2 ? "FistPump" : r.Human && place == 4 ? "Sad" : "Walk";
                            speed = want == "Walk" ? 0.8f : 1f;
                        }
                        else if (r.Dipped && !r.Finished) want = "Dip";
                        else if (r.Speed > 3.2f || (!r.Human && _race.Phase == RacePhase.Running))
                        {
                            want = "Sprint";
                            speed = Mathf.Clamp(r.Speed / AthleteData.Find("Sprint").Speed, 0.55f, 2.6f);
                        }
                        else if (r.Speed > 0.6f)
                        {
                            want = "Run";
                            speed = Mathf.Clamp(r.Speed / AthleteData.Find("Run").Speed, 0.5f, 1.6f);
                        }
                        else want = _race.Phase == RacePhase.Running ? "Ready" : "Idle";
                        break;
                }
                // before the race (the how-to, the countdown): the line-up behind the blocks
                bool lineUp = _race.Phase == RacePhase.Marks && _race.PhaseTime <= 0f;
                if (lineUp) want = "Idle";
                if (a.Playing != want) a.Play(want, want == "Sprint" && a.Playing == "SetPosition" ? 0.12f : 0.22f, speed);
                else a.SetSpeed(speed);
                float z = r.Distance - (_race.Phase is RacePhase.Marks or RacePhase.Set ? 0.62f : 0.2f);
                if (lineUp) z = -2.4f;
                var at = new Vector3(LaneX(i), 0f, z);
                a.transform.SetPositionAndRotation(Vector3.Lerp(a.transform.position, at, dt <= 0f ? 1f : 1f - Mathf.Exp(-20f * dt)),
                                                   Quaternion.identity);
                if (lineUp) a.LookAt(Kit.Cam.transform.position);
                else a.LookAt(null);
            }
            // the ghost of the best: a soft bar in the player's lane at the best's pace
            if (_ghost != null)
            {
                bool show = _race.Phase == RacePhase.Running && !_race.Human.Finished && _best > 0f;
                _ghost.gameObject.SetActive(show);
                if (show)
                {
                    float d = Mathf.Min(TrackRace.Length, _race.Time * TrackRace.Length / _best);
                    _ghost.SetPositionAndRotation(new Vector3(LaneX(_race.HumanIndex), 0.03f, d), Quaternion.Euler(90f, 0f, 0f));
                }
            }
        }

        void SlowMotion()
        {
            if (_slowAt < 0f) return;
            float t = UnityEngine.Time.unscaledTime - _slowAt;
            UnityEngine.Time.timeScale = t < 0.8f ? 0.3f : t < 1.3f ? Mathf.Lerp(0.3f, 1f, (t - 0.8f) / 0.5f) : 1f;
            if (t >= 1.3f) _slowAt = -1f;
        }

        /// <summary>The shots: the line-up from in front; low beside the player in the blocks; the broadcast's side
        /// tracking shot (wider with speed); the runners coming at the line; after the finish, in front of the player.</summary>
        Vector3 WantedCamera(out Vector3 look, out float fov)
        {
            var me = _race.Human;
            float x = LaneX(_race.HumanIndex);
            if (_race.Phase == RacePhase.Marks && _race.PhaseTime <= 0f)
            {
                look = new Vector3(TrackIn + 4.5f * LaneW, 1.2f, -2.4f);
                fov = 46f;
                return new Vector3(TrackIn + 4.0f * LaneW, 1.7f, 6.5f);
            }
            if (_race.Phase is RacePhase.Marks or RacePhase.Set)
            {
                look = new Vector3(x + 0.6f, 0.6f, 0.4f);
                fov = 44f;
                return new Vector3(x - 3.8f, 1.0f, 2.6f);
            }
            if (me.Finished && UnityEngine.Time.time - _finishAt > 0.5f)
            {
                var at = _runners[_race.HumanIndex].transform.position;
                look = at + Vector3.up * 1.1f;
                fov = 44f;
                return at + new Vector3(-2.6f, 1.6f, 4.2f);
            }
            float k = Mathf.Clamp01(me.Speed / 9f);
            if (TrackRace.Length - me.Distance < 14f || me.Finished)
            {
                // head-on down the lanes from past the line, a long lens (the finish's posts stay out of the picture)
                look = new Vector3(x + 0.9f, 1.05f, Mathf.Min(me.Distance + 1f, 101f));
                fov = 30f;
                return new Vector3(x - 1.6f, 1.5f, 116f);
            }
            look = new Vector3(x + 1.4f, 1.0f, me.Distance + 2.2f + k * 1.4f);
            fov = Mathf.Lerp(44f, 56f, k);
            return new Vector3(x - 6.2f + k * 0.8f, 1.45f, me.Distance + 0.4f);
        }

        // ---------------------------------------------------------------- HUD

        public override void DrawHud(float width)
        {
            var me = _race.Human;
            bool line = _race.Phase == RacePhase.Marks && _race.PhaseTime <= 0f;
            if (line && Kit.Cam != null)
            {
                // the line-up: each runner's name over their head
                for (int i = 0; i < 4; i++)
                {
                    var sp = Kit.Cam.WorldToScreenPoint(_runners[i].transform.position + Vector3.up * 2.25f);
                    if (sp.z <= 0f) continue;
                    var g = GameHud.FromScreen(sp);
                    var r = _race.Runners[i];
                    SportHud.Pill(g.x, g.y - 30f, r.Name, r.Human ? Sun : Color.white, 30);
                }
            }
            if (_race.Phase == RacePhase.Marks && !line && _race.PhaseTime < 1.5f)
                SportHud.Banner(width, 330f, SportsStrings.OnYourMarks, Sky, _race.PhaseTime, 1.5f, 110);
            // the race clock and the order
            if (_race.Phase is RacePhase.Running or RacePhase.Over)
            {
                float shown = me.Finished ? me.Finish : _race.Time;
                var clock = new Rect(36f, 28f, 300f, 92f);
                SportHud.Panel(clock, Night, 20f, new Color(1f, 1f, 1f, 0.15f));
                SportHud.Text(clock, $"{shown:00.00}", 64, me.Finished ? Sun : Color.white, TextAnchor.MiddleCenter, 0f);
                var order = new List<Runner>(_race.Runners);
                order.Sort((a, b) => a.Finished && b.Finished ? a.Finish.CompareTo(b.Finish) : a.Finished ? -1 : b.Finished ? 1 : b.Distance.CompareTo(a.Distance));
                for (int i = 0; i < order.Count; i++)
                {
                    var r = order[i];
                    var row = new Rect(36f, 132f + i * 50f, 300f, 44f);
                    SportHud.Rounded(row, r.Human ? new Color(Sun.r, Sun.g, Sun.b, 0.95f) : new Color(Night.r, Night.g, Night.b, 0.78f), 12f);
                    var tc = r.Human ? Night : Color.white;
                    SportHud.Text(new Rect(row.x + 14f, row.y, 40f, row.height), (i + 1).ToString(), 30, tc, TextAnchor.MiddleLeft, 0f);
                    SportHud.Text(new Rect(row.x + 54f, row.y, 150f, row.height), r.Name, 30, tc, TextAnchor.MiddleLeft, 0f);
                    if (r.Finished)
                        SportHud.Text(new Rect(row.x, row.y, row.width - 14f, row.height), $"{r.Finish:0.00}", 28, tc, TextAnchor.MiddleRight, 0f);
                }
                if (me.Reaction > 0f && UnityEngine.Time.time - _gunAt < 3.5f)
                    SportHud.Pill(186f, 340f, SportsStrings.Reaction(me.Reaction), Leaf, 28);
            }
            if (_race.Phase == RacePhase.Running && !me.Finished)
            {
                // the speed meter, and the call to lean in the last metres
                float k = Mathf.Clamp01(me.Speed / _run.MaxSpeed);
                SportHud.Meter(new Rect(width / 2f - 340f, 970f, 680f, 52f), k, Coral, Leaf, SportsStrings.Speed);
                if (me.Speed < 4f && _race.Time > 1.5f) SportHud.Pill(width / 2f, 860f, SportsStrings.RunFaster, Sun, 38);
                if (TrackRace.Length - me.Distance < TrackRace.DipWindow && !me.Dipped)
                    SportHud.Banner(width, 330f, SportsStrings.Lean, Sun, 0.5f, 10f, 120);
                if (_ghost != null && _ghost.gameObject.activeSelf && Kit.Cam != null)
                {
                    var sp = Kit.Cam.WorldToScreenPoint(_ghost.position + Vector3.up * 0.4f);
                    if (sp.z > 0f)
                    {
                        var g = GameHud.FromScreen(sp);
                        SportHud.Pill(g.x, g.y - 20f, SportsStrings.PersonalBest, Sun, 24, null, 0.85f);
                    }
                }
            }
            if (me.Finished && UnityEngine.Time.time - _finishAt < 1.6f)
                SportHud.Banner(width, 330f, SportsStrings.PhotoFinish, Grape, UnityEngine.Time.time - _finishAt, 1.6f, 110,
                                $"{SportsStrings.Place(_race.HumanPlace)}  ·  {me.Finish:0.00} s");
            _banners.RemoveAll(b => UnityEngine.Time.unscaledTime - b.at > b.life);
            if (_banners.Count > 0)
            {
                var b = _banners[_banners.Count - 1];
                SportHud.Banner(width, 330f, b.text, b.colour, UnityEngine.Time.unscaledTime - b.at, b.life, 120, b.sub);
            }
        }
    }
}
