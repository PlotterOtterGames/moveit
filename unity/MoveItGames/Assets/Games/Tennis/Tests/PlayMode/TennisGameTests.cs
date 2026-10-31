using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using MoveIt.Motion;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MoveIt.Games.Tennis.Tests
{
    /// <summary>
    /// Tenis! with the scripted player (fake_sender.py --script tennis: hands up, then one right-arm swing every 3.4 s:
    /// serve, forehand, backhand, forehand, lob, backhand, slice, forehand, three times). In practice the computer times
    /// its feeds (and the toss) to the next scripted swing, so every swing has a ball: the body serves, then returns
    /// feed after feed with forehands and backhands, a lob from the slow upward swing and a slice from the chops down, on
    /// time. And --script tennis_move: steps to the right, the left, toward the camera and away move the player across
    /// the court, to the net and behind the baseline. Screenshots: unity/captures/tennis_*.png.
    /// </summary>
    public class TennisGameTests
    {
        const int MotionPort = 5021;
        const double Period = 3.4;

        Process _sender;
        TennisGame _game;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("Tennis");
            yield return null;
            yield return null;
            _game = Object.FindAnyObjectByType<TennisGame>();
            Assert.IsNotNull(_game, "Tennis scene has no TennisGame");
            var motion = MotionSystem.Instance;
            motion.enabled = false;
            typeof(MotionSystem).GetField("port", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(motion, MotionPort);
            motion.enabled = true;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_sender != null && !_sender.HasExited) _sender.Kill();
            _sender?.Dispose();
            _sender = null;
            if (MotionSystem.Instance != null) Object.Destroy(MotionSystem.Instance.gameObject);   // survives scene loads
            yield return null;
        }

        [UnityTest, Timeout(150000)]
        public IEnumerator Practice_TheBodysSwings_ServeAndReturnFeedAfterFeed()
        {
            var swings = new List<(SwingEvent swing, double at, SwingResult result)>();
            var hits = new List<Shot>();
            double lastSwing = -1;
            _game.Practice = true;
            _game.SwingHandled += (s, at, r) =>
            {
                swings.Add((s, at, r));
                lastSwing = at;
                Log($"swing {s.Speed:0.0} torso/s ({s.Direction.x:0.0}, {s.Direction.y:0.0}) at {at:0.00}: {r}");
            };
            _game.MatchEvents += e =>
            {
                if (e.Kind != MatchEventKind.Hit || e.Team != 0) return;
                hits.Add(e.Shot);
                Log($"hit {e.Shot.Kind} {e.Shot.Stroke} timing {e.Shot.Timing:0.00}{(e.Shot.Perfect ? " perfect" : "")}");
            };
            // the next scripted swing at or after the earliest moment the ball can be there (none seen yet: a normal pace)
            double Next(double earliest)
            {
                if (lastSwing < 0) return earliest + 0.5;
                double t = lastSwing;
                while (t < earliest) t += Period;
                return t;
            }
            _game.PracticeContact = Next;
            _game.PracticeToss = Next;

            bool shotServe = false, shotComing = false, shotHit = false;
            int hitsShot = 0;
            yield return RunFakePlayer(() =>
            {
                var m = _game.Match;
                if (_game.Current != TennisGame.Phase.Playing) return null;
                if (!shotServe && m.Phase == MatchPhase.Toss && m.Time > m.TossTop - 0.25) { shotServe = true; return "tennis_serve.png"; }
                if (!shotComing && m.Phase == MatchPhase.Rally && m.LastHitter == 1 && m.Next.Active && m.Next.Time - m.Time < 0.5)
                {
                    shotComing = true;
                    return "tennis_coming.png";
                }
                if (!shotHit && hits.Count >= 3 && hitsShot == 0)
                {
                    hitsShot = hits.Count;
                    return null;
                }
                if (!shotHit && hitsShot > 0 && m.LastHitter == 0 && m.Phase == MatchPhase.Rally)
                {
                    shotHit = true;
                    return "tennis_hit.png";
                }
                return null;
            });

            var rally = hits.Where(h => h.Kind is not (ShotKind.Serve or ShotKind.PowerServe)).ToList();
            Log($"{swings.Count} swings ({string.Join(", ", swings.GroupBy(s => s.result).Select(g => $"{g.Key} {g.Count()}"))}); " +
                $"{hits.Count} hits: {string.Join(", ", hits.Select(h => h.Kind))}; perfect {hits.Count(h => h.Perfect)}");
            Assert.AreEqual(TennisGame.Phase.Playing, _game.Current, "raising both hands started the game");
            Assert.GreaterOrEqual(swings.Count, 15, "the scripted swings were seen while playing");
            Assert.IsTrue(hits.Exists(h => h.Kind is ShotKind.Serve or ShotKind.PowerServe), "a swing served");
            Assert.GreaterOrEqual(rally.Count, 8, "feed after feed came back off the body's swings");
            Assert.IsTrue(rally.Exists(h => h.Stroke == Stroke.Forehand) && rally.Exists(h => h.Stroke == Stroke.Backhand),
                          "forehands and backhands (the players pick by where the ball is)");
            Assert.IsTrue(rally.Exists(h => h.Kind == ShotKind.Lob), "the slow upward swing lobs");
            Assert.IsTrue(rally.Exists(h => h.Kind == ShotKind.Slice), "a chop down slices");
            Assert.GreaterOrEqual(rally.Count(h => Mathf.Abs(h.Timing) < 0.35f), rally.Count * 3 / 4,
                                  "swings timed to the ball count as on time: the timing works on capture times");
        }

        [UnityTest, Timeout(90000)]
        public IEnumerator Steering_StepsMoveThePlayerAroundTheirHalf()
        {
            _game.FirstServer = 1;   // the player receives: their player follows the body (a server stands still)
            float right = 0f, left = 0f, nearest = 99f, furthest = 0f;
            bool shotRight = false, shotNet = false;
            yield return RunFakePlayer(() =>
            {
                if (_game.Current != TennisGame.Phase.Playing) return null;
                var wanted = _game.Match.HumanWanted;
                var at = _game.Match.Human.Position;
                right = Mathf.Max(right, wanted.x);
                left = Mathf.Min(left, wanted.x);
                nearest = Mathf.Min(nearest, -wanted.y);
                furthest = Mathf.Max(furthest, -wanted.y);
                if (!shotRight && at.x > 3f) { shotRight = true; return "tennis_step_right.png"; }
                if (!shotNet && -at.y < 5f) { shotNet = true; return "tennis_at_net.png"; }
                return null;
            }, "tennis_move", 60f);
            Log($"the body put the player from x {left:0.0} to {right:0.0}, from {nearest:0.0} m to {furthest:0.0} m behind the net");
            Assert.Greater(right, 3f, "a step to the right: near the right sideline");
            Assert.Less(left, -3f, "a step to the left: near the left sideline");
            Assert.Less(nearest, 5f, "a step toward the TV: at the net");
            Assert.Greater(furthest, 14f, "a step back: behind the baseline");
        }

        /// <summary>
        /// The racket, close up and frame by frame: a player alone, driven like the rules drive it at 60 fps through
        /// ready, the racket going back for a coming ball, forehands and backhands (early, on time, a quick one that
        /// reaches the game late), an air swing followed at once by a real one, the toss and a serve, a smash, a cheer:
        /// the racket's head never jumps between frames (a swing used to start from a fixed pose: a quick one skipped its
        /// backswing and the racket teleported). Close-ups from behind (the game's view) of each stage:
        /// unity/captures/racket_*.png.
        /// </summary>
        [UnityTest, Timeout(60000)]
        public IEnumerator Racket_MovesWithoutJumps_StringsFaceTheNet()
        {
            _game.enabled = false;
            var views = Object.FindObjectsByType<TennisPlayerView>(FindObjectsSortMode.None);
            Assert.IsNotEmpty(views);
            var actor = views[0].GetComponentInChildren<MeshRenderer>().sharedMaterial;
            var trail = views[0].GetComponentInChildren<TrailRenderer>().sharedMaterial;
            foreach (var v in views) v.gameObject.SetActive(false);
            GameObject.Find("Ball")?.SetActive(false);
            var look = new TennisPlayerView.Look
            {
                Shirt = new Color(0.19f, 0.78f, 0.96f), Accent = new Color(1f, 0.79f, 0.24f), Shorts = Color.white,
                Skin = new Color(0.97f, 0.8f, 0.66f), Hair = new Color(0.25f, 0.16f, 0.1f), Shoes = new Color(1f, 0.35f, 0.31f),
            };
            var view = TennisPlayerView.Create("Close-up", actor, trail, look);
            var p = new CourtPlayer { Team = 0, Position = new Vector2(0f, -12f) };
            var cam = Camera.main;
            cam.transform.position = new Vector3(0.9f, 2.1f, -15.6f);   // behind and above, like the game's camera, near
            cam.transform.LookAt(new Vector3(0.35f, 1.15f, -11.6f));
            cam.fieldOfView = 40f;

            const float dt = 1f / 60f;
            double now = 0;
            float worst = 0f;
            string worstAt = "";
            var byPhase = new Dictionary<string, float>();
            Vector3 last = Vector3.zero;
            bool first = true;
            string phase = "";
            void Frame()
            {
                now += dt;
                view.Animate(p, now, dt, true);
                var tip = view.RacketWorld;
                float step = first ? 0f : Vector3.Distance(tip, last);
                byPhase[phase] = Mathf.Max(byPhase.TryGetValue(phase, out float before) ? before : 0f, step);
                if (step > worst)
                {
                    worst = step;
                    worstAt = phase;
                }
                first = false;
                last = tip;
            }
            void Run(string name, float seconds)
            {
                phase = name;
                for (int i = 0; i < Mathf.RoundToInt(seconds * 60f); i++) Frame();
            }
            void Swing(Stroke stroke, double contactIn)
            {
                p.Swings++;
                p.SwingStroke = stroke;
                p.SwingContact = now + contactIn;
            }
            IEnumerator Shot(string file)
            {
                yield return Screenshot(file);
            }

            Run("ready", 1f);
            yield return Shot("racket_ready.png");
            p.Preparing = true;
            p.PrepareStroke = Stroke.Forehand;
            Run("racket back for a forehand", 0.6f);
            yield return Shot("racket_forehand_back.png");
            Swing(Stroke.Forehand, 0.25);            // early: the hit waits for the ball
            p.Preparing = false;
            Run("forehand to the hit", 0.25f);
            yield return Shot("racket_forehand_hit.png");
            Run("forehand follow-through", 0.15f);
            yield return Shot("racket_forehand_follow.png");
            Run("back to ready", 0.8f);
            p.Preparing = true;
            p.PrepareStroke = Stroke.Backhand;
            Run("racket back for a backhand", 0.6f);
            yield return Shot("racket_backhand_back.png");
            p.Preparing = false;
            Swing(Stroke.Backhand, 0.0);             // on time, reaching the game late: only the hit is left
            Run("backhand to the hit", 0.08f);
            yield return Shot("racket_backhand_hit.png");
            Run("backhand follow-through and back", 1f);
            Swing(Stroke.Forehand, 0.1);             // an air swing from ready (no ball)...
            Run("air swing", 0.12f);
            Swing(Stroke.Forehand, 0.1);             // ...and at once a real one, mid-swing
            Run("a swing restarted mid-swing", 1f);
            p.Serving = true;
            Run("holding the ball to serve", 0.8f);
            yield return Shot("racket_serve_stance.png");
            p.Preparing = true;                      // the toss: the racket goes behind the head (as the rules say)
            p.PrepareStroke = Stroke.Overhead;
            Run("the toss", 0.5f);
            yield return Shot("racket_serve_toss.png");
            Swing(Stroke.Overhead, 0.0);             // at the top: only the hit is left
            p.Serving = p.Preparing = false;
            Run("serve to the hit", 0.12f);
            yield return Shot("racket_serve_hit.png");
            Run("serve follow-through and back", 1f);
            Swing(Stroke.Overhead, 0.3);             // a smash at the net
            Run("smash", 1f);
            p.Mood = 1;
            Run("cheer", 0.8f);
            p.Mood = -1;
            Run("sulk", 0.8f);
            p.Mood = 0;
            Run("back to ready after a point", 1f);
            Object.Destroy(view.gameObject);

            Log($"the racket head's biggest step between two frames: {worst:0.000} m ({worstAt}); by phase: " +
                string.Join(", ", byPhase.Select(kv => $"{kv.Key} {kv.Value:0.00}")));
            // a full swing moves the head ~0.15-0.2 m a frame at 60 fps; a jump was a metre or more
            Assert.Less(worst, 0.3f, $"the racket jumped {worst:0.00} m in one frame during: {worstAt}");
        }

        static void Log(string text) => UnityEngine.Debug.Log($"[Test] {text}");

        /// <summary>Runs the scripted player; everyFrame may return a screenshot file name.</summary>
        IEnumerator RunFakePlayer(System.Func<string> everyFrame, string script = "tennis", float seconds = 120f)
        {
            string serviceDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../motion-service-py"));
            string python = Path.Combine(serviceDir, ".venv/Scripts/python.exe");
            Assume.That(File.Exists(python), $"Python venv not found at {python}");
            _sender = Process.Start(new ProcessStartInfo(python, $"fake_sender.py --script {script} --port {MotionPort} --once --video-control-port 0")
            {
                WorkingDirectory = serviceDir,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!_sender.HasExited && Time.realtimeSinceStartup < deadline)
            {
                string shot = everyFrame();
                if (shot != null) yield return Screenshot(shot);
                else yield return null;
            }
            Assert.IsTrue(_sender.HasExited, $"fake_sender.py --script {script} did not finish in time");
        }

        static IEnumerator Screenshot(string name)
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../captures"));
            Directory.CreateDirectory(dir);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, name));
            yield return null;
        }
    }
}
