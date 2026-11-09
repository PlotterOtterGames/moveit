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
    /// Tenis! (singles since 2026-11-07) with the scripted player (fake_sender.py --script tennis: hands up, then one
    /// right-arm swing every 3.4 s: serve, forehand, backhand, forehand, lob, backhand, slice, forehand, straight up, three
    /// times). In practice the computer times its feeds (and the toss) to the next scripted swing, so every swing has a ball
    /// coming near: the body serves, then each swing hits the feed back, on whichever side it comes, topspin from the
    /// scoop up across and from the swing straight up (since 2026-11-07 evening only a gentle scoop lobs), a slice from
    /// the chop down; the rally speeds up. And --script tennis_move: the
    /// body's steps to the sides, toward the camera and away move nothing (since 2026-11-07 the toys run to the ball by
    /// themselves). And the rackets' strokes, frame by frame. Screenshots: unity/captures/tennis_*.png, racket_*.png.
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
            _game.NextMode = TennisMode.Solo;   // the remembered choice may be a two-player one
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
            // the last scripted swing seen (the arm coming back after a swing can count as a slow swing: those don't
            // count here)
            double lastSwing = -1;
            int whiffs = 0, missed = 0;
            _game.Practice = true;
            _game.SwingHandled += (s, at, r) =>
            {
                swings.Add((s, at, r));
                if (s.Speed >= 8f) lastSwing = at;
                Log($"swing {s.Speed:0.0} torso/s ({s.Direction.x:0.0}, {s.Direction.y:0.0}) at {at:0.00}: {r}");
            };
            _game.MatchEvents += e =>
            {
                if (e.Kind == MatchEventKind.Whiff) { whiffs++; Log("a swing too early or too late"); }
                if (e.Kind == MatchEventKind.Missed) { missed++; Log("missed: the ball went by"); }
                if (e.Kind != MatchEventKind.Hit || e.Team != 0) return;
                hits.Add(e.Shot);
                Log($"hit {e.Shot.Kind} {e.Shot.Stroke} {e.Shot.Speed:0.0} m/s, met at {e.Shot.Timing:0.00}{(e.Shot.Perfect ? " perfect" : "")}");
            };
            // the next scripted swing at or after the earliest moment the ball can be there (none seen yet: a normal pace),
            // counted from the last one (the match clock drifts a little against the script's over the minutes)
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
                $"{hits.Count} hits: {string.Join(", ", hits.Select(h => $"{h.Kind} {h.Stroke}"))}; perfect {hits.Count(h => h.Perfect)}, " +
                $"whiffs {whiffs}, missed {missed}");
            Assert.AreEqual(TennisGame.Phase.Playing, _game.Current, "raising both hands started the game");
            Assert.AreEqual(2, _game.Views.Count(v => v != null), "singles: one player a side");
            Assert.IsTrue(_game.Views[0] != null && _game.Views[2] != null && _game.Views[1] == null && _game.Views[3] == null,
                          "the singles players: the player's and the computer's back players");
            Assert.IsTrue(!_game.Views[0].Low && _game.Views[2].Low,
                          "one view: the near player in full detail, the far one as a low-detail chibi (Chibi_Low loaded)");
            Assert.GreaterOrEqual(swings.Count, 15, "the scripted swings were seen while playing");
            Assert.IsTrue(hits.Exists(h => h.Kind is ShotKind.Serve or ShotKind.PowerServe), "a swing served");
            // 21 strokes in the script; the rally starts slow (the serve at 0.6 of its speed), so after the serve and a
            // lob the computer's feed can't reach the next 3.4 s slot of the script and comes to the one after
            // (2026-11-06: 13 hits, two strokes a cycle with no ball yet)
            Assert.GreaterOrEqual(rally.Count, 11, "feed after feed came back off a swing as it came near");
            Assert.IsTrue(rally.Exists(h => h.Stroke == Stroke.Forehand) && rally.Exists(h => h.Stroke == Stroke.Backhand),
                          "the same swing hits balls on either side");
            Assert.IsTrue(rally.Exists(h => h.Kind == ShotKind.Topspin), "the scoop up across drives the ball (topspin)");
            Assert.IsTrue(rally.Exists(h => h.Kind == ShotKind.Slice), "the chop down slices");
            Assert.IsFalse(swings.Exists(s => s.result == SwingResult.Vertical && s.swing.Direction.y > 0f),
                           "a swing up never misses at once (2026-11-07: at a ball in reach it drives it)");
            Assert.IsFalse(rally.Exists(h => h.Kind == ShotKind.Lob), "a strong swing up is no lob (only a gentle scoop straight up is)");
            Assert.IsFalse(hits.Exists(h => h.Pace <= 0f), "every shot flies at the rally's pace");
            Assert.Greater(hits.Max(h => h.Pace), hits.Min(h => h.Pace) + 0.2f, "the rally sped up");
            Assert.GreaterOrEqual(rally.Count(h => Mathf.Abs(h.Timing) < 0.5f), rally.Count * 3 / 4,
                                  "swings timed to the ball count as on time: capture times take the camera's lag out");
        }

        /// <summary>
        /// Small flicks of the forearm (a third of a torso, gentle, like a Wii remote's: the user, 2026-11-06, "moving the
        /// whole arm is tiring"), with the box's pose-model glitches thrown in (fake_sender.py --script tennis_flick
        /// --glitches 1: a wrist flipped away or pulled onto its elbow once a second, more often than the box does): the
        /// flicks serve and hit feed after feed, forehands and backhands; a small flick straight up is no miss (at a ball in
        /// reach it lobs, else it's nothing); the glitches are ignored. Screenshot: tennis_flick_hit.png.
        /// </summary>
        [UnityTest, Timeout(150000)]
        public IEnumerator Practice_SmallFlicks_HitTheFeeds_GlitchesAreIgnored()
        {
            var swings = new List<(SwingEvent swing, double at, SwingResult result)>();
            var hits = new List<Shot>();
            // the script's rhythm: every stroke's flick is across, 1.6 s into its 3.4 s segment, so each sideways flick
            // sets the beat (the segments drift a little against the match clock); the serve's flicks are up and down, and
            // the toss's flick, earlier in its segment, may be the one that serves (2026-11-07: synced to it, the beat was
            // 0.6 s off and every feed came before its flick), so a serve only sets the first beat
            double lastSwing = -1;
            _game.Practice = true;
            _game.SwingHandled += (s, at, r) =>
            {
                swings.Add((s, at, r));
                if (Mathf.Abs(s.Direction.x) > 0.7f) lastSwing = at;
                else if (lastSwing < 0 && r == SwingResult.Serve) lastSwing = at;
                Log($"swing {(s.Gentle ? "gentle " : "")}{s.Speed:0.0} torso/s ({s.Direction.x:0.0}, {s.Direction.y:0.0}) at {at:0.00}: {r}");
            };
            _game.MatchEvents += e =>
            {
                if (e.Kind != MatchEventKind.Hit || e.Team != 0) return;
                hits.Add(e.Shot);
                Log($"hit {e.Shot.Kind} {e.Shot.Stroke} {e.Shot.Speed:0.0} m/s, met at {e.Shot.Timing:0.00}");
            };
            double Next(double earliest)
            {
                if (lastSwing < 0) return earliest + 0.5;
                double t = lastSwing;
                while (t < earliest) t += Period;
                return t;
            }
            // (tossed on the strokes' beat too: any flick serves, so a point lost to a flick the glitches hid goes on with
            // the next flicks; tossed only for the script's serve segments, the rest of that cycle was lost, 2026-11-07)
            _game.PracticeContact = Next;
            _game.PracticeToss = Next;

            bool shot = false;
            int seenHits = 0;
            yield return RunFakePlayer(() =>
            {
                var m = _game.Match;
                if (_game.Current != TennisGame.Phase.Playing || shot) return null;
                if (hits.Count >= 3 && seenHits == 0) seenHits = hits.Count;
                if (seenHits > 0 && hits.Count > seenHits && m.LastHitter == 0 && m.Phase == MatchPhase.Rally)
                {
                    shot = true;
                    return "tennis_flick_hit.png";
                }
                return null;
            }, "tennis_flick --glitches 1");

            var rally = hits.Where(h => h.Kind is not (ShotKind.Serve or ShotKind.PowerServe)).ToList();
            int ignored = _game.GlitchesIgnored;
            Log($"{swings.Count} swings ({swings.Count(s => s.swing.Gentle)} gentle; {string.Join(", ", swings.GroupBy(s => s.result).Select(g => $"{g.Key} {g.Count()}"))}); " +
                $"{hits.Count} hits ({string.Join(", ", hits.Select(h => $"{h.Kind} {h.Stroke}"))}); {ignored} glitches ignored");
            Assert.AreEqual(TennisGame.Phase.Playing, _game.Current, "raising both hands started the game");
            Assert.IsTrue(hits.Exists(h => h.Kind is ShotKind.Serve or ShotKind.PowerServe), "the flicks served");
            // (the glitches hide a flick now and then: the point is lost and the script's serve may come at the wrong moment;
            // 5-8 rally hits in the runs of 2026-11-07)
            Assert.GreaterOrEqual(rally.Count, 4, "feed after feed came back off a small flick");
            Assert.IsTrue(rally.Exists(h => h.Stroke == Stroke.Forehand) && rally.Exists(h => h.Stroke == Stroke.Backhand),
                          "forehand and backhand flicks");
            Assert.GreaterOrEqual(swings.Count(s => s.swing.Gentle), 6, "small flicks are gentle swings");
            // a small flick up is no instant miss: only a strong swing up or down is (2026-11-07: an injected glitch now and
            // then makes the detector read the flick up as strong, 5.2 torso lengths/s; that's the detector's to reject,
            // SwingDetectorTests, and then the rule applies as to any strong swing)
            var verticals = swings.FindAll(s => s.result == SwingResult.Vertical);
            Assert.IsFalse(verticals.Exists(s => s.swing.Gentle || s.swing.Speed < new TennisMatch(TennisLevel.For(1)).VerticalMissSpeed),
                           "a small flick up is no instant miss");
            if (verticals.Count > 0) Log($"{verticals.Count} swing(s) up or down read as strong (a glitch over a flick): an instant miss each");
            Assert.Greater(ignored, 0, "the pose model's glitches were seen and ignored");
        }

        /// <summary>
        /// The toys run to the ball by themselves (2026-11-07): the scripted player steps far to both sides, toward the TV
        /// and away (fake_sender.py --script tennis_move) while the computer serves; the player's back player still waits
        /// for every serve on the side it comes to and runs to it, whatever the body does. Screenshot: tennis_auto_run.png.
        /// </summary>
        [UnityTest, Timeout(90000)]
        public IEnumerator Steps_DontMoveThePlayers_TheyRunToTheBallThemselves()
        {
            _game.FirstServer = 1;   // the computer serves: the player's back player receives
            float bodyLeft = 0f, bodyRight = 0f, worstWait = 0f, worstArrival = 0f;
            int serves = 0, arrivals = 0;
            double planned = -1;
            bool shot = false;
            var input = BodyInput.Instance;
            yield return RunFakePlayer(() =>
            {
                if (_game.Current != TennisGame.Phase.Playing) return null;
                var m = _game.Match;
                var g = input != null ? input.Gestures : null;
                if (g != null && g.IsCalibrated)
                {
                    bodyLeft = Mathf.Min(bodyLeft, g.LateralOffset);
                    bodyRight = Mathf.Max(bodyRight, g.LateralOffset);
                }
                // waiting for the serve: on the side it comes to (2.2 m out), not where the body is
                if (m.Phase is MatchPhase.Serve or MatchPhase.Toss) worstWait = Mathf.Max(worstWait, Mathf.Abs(Mathf.Abs(m.Human.Position.x) - 2.2f));
                var n = m.Next;
                if (m.Phase == MatchPhase.Rally && n.Active && n.Team == 0 && n.Reachable && System.Math.Abs(n.Time - planned) > 1e-6 && m.Time >= n.Time - 0.02)
                {
                    planned = n.Time;
                    arrivals++;
                    worstArrival = Mathf.Max(worstArrival, Vector2.Distance(n.Hitter.Position, n.Spot));
                    if (m.LastWasServe) serves++;
                    if (!shot) { shot = true; return "tennis_auto_run.png"; }
                }
                return null;
            }, "tennis_move", 60f);
            Log($"the body went from {bodyLeft:0.00} to {bodyRight:0.00} torso lengths sideways; the back player waited within {worstWait:0.00} m of " +
                $"the receiving spot; {arrivals} balls reached ({serves} serves), worst {worstArrival:0.00} m from the spot");
            Assert.Less(bodyLeft, -0.4f, "the body stepped far to the left");
            Assert.Greater(bodyRight, 0.4f, "and far to the right");
            Assert.Less(worstWait, 0.3f, "the back player waited for the serve where it comes, whatever the body did");
            Assert.GreaterOrEqual(serves, 2, "serves came (the script steps for 18 s: two or three serves)");
            Assert.Less(worstArrival, 0.7f, "it ran to every ball it could reach by itself");
        }

        /// <summary>
        /// The racket, close up and frame by frame. The computer's: a player alone, driven like the rules drive it at 60
        /// fps through ready, the racket going back for a coming ball, forehands and backhands (early, on time, a quick one
        /// that reaches the game late), an air swing followed at once by a real one, the toss and a serve, a smash, a
        /// cheer: the racket's head never jumps between frames (a swing used to start from a fixed pose: a quick one
        /// skipped its backswing and the racket teleported). Then the player's own swings as the rules show them since
        /// 2026-11-06 (no more racket held where the camera puts the hand): quick air swings both ways, gentle and hard, up
        /// and down, one restarted mid-swing, a hit whose ball is already there: a stroke from wherever the racket is,
        /// never a jump. Close-ups from behind (the game's view) of each stage: unity/captures/racket_*.png.
        /// </summary>
        [UnityTest, Timeout(60000)]
        public IEnumerator Racket_MovesWithoutJumps_TheComputersAndTheLiveOne()
        {
            _game.enabled = false;
            var views = Object.FindObjectsByType<TennisPlayerView>(FindObjectsSortMode.None);
            Assert.IsNotEmpty(views);
            var actor = views[0].GetComponentInChildren<MeshRenderer>().sharedMaterial;
            var trail = views[0].GetComponentInChildren<TrailRenderer>().sharedMaterial;
            foreach (var v in views) v.gameObject.SetActive(false);
            GameObject.Find("Ball")?.SetActive(false);
            var look = TennisLooks.Player();
            var view = TennisPlayerView.Create("Close-up", actor, trail, look);
            var p = new CourtPlayer { Team = 0, Position = new Vector2(0f, -12f) };
            var cam = Camera.main;
            cam.transform.position = new Vector3(0.8f, 1.75f, -14.9f);   // behind and above, like the game's camera, near
            cam.transform.LookAt(new Vector3(0.3f, 0.85f, -11.6f));
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
            void Swing(Stroke stroke, double contactIn, float power = 0.5f, bool quick = false, Vector3? point = null)
            {
                p.Swings++;
                p.SwingStroke = stroke;
                p.SwingContact = now + contactIn;
                p.SwingPower = power;
                p.SwingQuick = quick;
                p.SwingReaches = point.HasValue;
                p.SwingPoint = point ?? Vector3.zero;
            }
            // the racket is 1.55x its old size since 2026-11-06: its head travels that much further for the same turn
            float jump = 0.4f * TennisPlayerView.RacketReach / 0.42f;
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
            Log($"the racket head's biggest step between two frames: {worst:0.000} m ({worstAt}); by phase: " +
                string.Join(", ", byPhase.Select(kv => $"{kv.Key} {kv.Value:0.00}")));
            // a full swing moves the head ~0.3-0.5 m a frame at 60 fps (the racket's length); a jump was a metre or more
            Assert.Less(worst, jump, $"the racket jumped {worst:0.00} m in one frame during: {worstAt}");

            // hits reach into the ball (Wii's Miis): the racket's head passes through where the ball is met, near, far (a
            // lunge), high (a hop), on the backhand side, and the serve's toss; still no jumps
            worst = 0f;
            byPhase.Clear();
            var toy = new Vector3(p.Position.x, 0f, p.Position.y);
            var reaches = new (string name, Stroke stroke, Vector3 offset, string shot)[]
            {
                ("reach: forehand", Stroke.Forehand, new Vector3(0.75f, 1.05f, 0.25f), "racket_reach_forehand.png"),
                ("reach: far forehand (lunge)", Stroke.Forehand, new Vector3(1.45f, 0.9f, 0.3f), "racket_reach_far.png"),
                ("reach: high forehand (hop)", Stroke.Forehand, new Vector3(0.7f, 1.75f, 0.2f), "racket_reach_high.png"),
                ("reach: low volley", Stroke.Forehand, new Vector3(0.6f, 0.4f, 0.35f), "racket_reach_low.png"),
                ("reach: backhand", Stroke.Backhand, new Vector3(-0.8f, 1.1f, 0.25f), "racket_reach_backhand.png"),
                ("reach: serve", Stroke.Overhead, new Vector3(-0.2f, 1.85f, 0.25f), "racket_reach_serve.png"),
            };
            foreach (var (name, stroke, offset, shot) in reaches)
            {
                Run($"{name}: ready", 0.6f);
                var point = toy + offset;
                Swing(stroke, 0.4, point: point);
                phase = name;
                float closest = float.MaxValue;
                bool shotTaken = false;
                for (int i = 0; i < 24 + 30; i++)
                {
                    Frame();
                    closest = Mathf.Min(closest, Vector3.Distance(view.RacketWorld, point));
                    if (!shotTaken && i == 23)
                    {
                        shotTaken = true;
                        var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                        marker.transform.position = point;
                        marker.transform.localScale = Vector3.one * 0.3f;
                        yield return Shot(shot);
                        Object.Destroy(marker);
                    }
                }
                Log($"{name}: the racket's head came within {closest:0.000} m of the ball");
                Assert.Less(closest, 0.12f, $"{name}: the racket's head meets the ball (closest {closest:0.00} m)");
            }
            Log($"reaching: the racket head's biggest step between two frames: {worst:0.000} m ({worstAt})");
            Assert.Less(worst, jump, $"the reaching racket jumped {worst:0.00} m in one frame during: {worstAt}");

            // the player's own swings (the rules show each one: quick from wherever the racket is, as hard as the hand)
            worst = 0f;
            byPhase.Clear();
            first = true;
            Run("player: ready", 0.8f);
            Swing(Stroke.Forehand, 0.0, power: 0.2f, quick: true);   // a gentle swing across, at nothing
            Run("player: gentle forehand", 0.12f);
            yield return Shot("racket_player_forehand.png");
            Run("player: gentle forehand follow-through", 0.6f);
            Swing(Stroke.Backhand, 0.0, power: 1f, quick: true);     // a hard one the other way
            Run("player: hard backhand", 0.06f);
            yield return Shot("racket_player_backhand.png");
            Run("player: hard backhand follow-through", 0.6f);
            Swing(Stroke.Forehand, 0.0, power: 0.8f, quick: true);   // and back again mid-way
            Run("player: forehand", 0.05f);
            Swing(Stroke.Backhand, 0.0, power: 0.8f, quick: true);
            Run("player: a swing restarted mid-swing", 0.8f);
            Swing(Stroke.Scoop, 0.0, power: 0.6f, quick: true);      // straight up (a miss)
            Run("player: up", 0.15f);
            yield return Shot("racket_player_up.png");
            Run("player: up and back", 0.6f);
            Swing(Stroke.Overhead, 0.0, power: 0.6f, quick: true);   // straight down (a miss)
            Run("player: down", 0.6f);
            p.Preparing = true;                                       // a ball comes: the racket goes back by itself
            p.PrepareStroke = Stroke.Forehand;
            Run("player: racket back for the ball", 0.6f);
            yield return Shot("racket_player_ready_for_ball.png");
            p.Preparing = false;
            Swing(Stroke.Forehand, -0.05, power: 0.9f);              // the hit, reaching the game after the ball got there
            Run("player: the hit, the ball already there", 0.08f);
            yield return Shot("racket_player_hit.png");
            Run("player: follow-through and back", 0.8f);
            Object.Destroy(view.gameObject);
            Log($"the player's racket's biggest step between two frames: {worst:0.000} m ({worstAt}); by phase: " +
                string.Join(", ", byPhase.Select(kv => $"{kv.Key} {kv.Value:0.00}")));
            Assert.Less(worst, jump, $"the player's racket jumped {worst:0.00} m in one frame during: {worstAt}");
        }

        /// <summary>
        /// Two players, versus (fake_sender.py --script tennis_duo --alternate: two scripted people side by side, one per
        /// packet taking turns with track ids, like the box's NPU sharing its runs): both stand still and raise their hands,
        /// the match starts split-screen with all four toys in low detail; player 1's flick tosses and their swing serves,
        /// player 2's swings never do (it's player 1's service); the far team's toy runs to the serve by itself.
        /// Screenshots: tennis_two_waiting.png, tennis_versus.png.
        /// </summary>
        [UnityTest, Timeout(120000)]
        public IEnumerator TwoPlayers_Versus_SplitScreen_OnlyTheServerServes()
        {
            _game.TwoPlayerMode = TennisMode.Versus;
            var bySlot = new List<(int slot, SwingResult result)>();
            int wrongServer = 0;
            _game.PlayerSwingHandled += (slot, s, at, r) =>
            {
                bySlot.Add((slot, r));
                // a toss or a serve only from whoever's turn it is to serve (player 1 serves first; player 2 the next game)
                if (r is SwingResult.Serve or SwingResult.Toss && _game.Match != null && _game.Match.ServerTeam != slot) wrongServer++;
                Log($"player {slot + 1} swing {s.Speed:0.0} torso/s at {at:0.00}: {r}");
            };
            bool shotWaiting = false, shotPlaying = false, split = false, sawTwo = false;
            int splitFrames = 0, lowFrames = 0, farArrivals = 0;
            double planned = -1;
            yield return RunFakePlayer(() =>
            {
                if (_game.SplitCamera != null && _game.SplitCamera.enabled)
                {
                    splitFrames++;
                    if (_game.Views.Where(v => v != null).All(v => v.Low) && _game.Views.Count(v => v != null) == 2) lowFrames++;
                }
                if (_game.Current == TennisGame.Phase.Waiting && _game.Slots.Present == 2 && !shotWaiting && _game.Slots[0].Calibrated)
                {
                    shotWaiting = true;
                    return "tennis_two_waiting.png";
                }
                if (_game.Current != TennisGame.Phase.Playing) return null;
                var m = _game.Match;
                split |= _game.SplitCamera != null;
                sawTwo |= _game.Slots.Present == 2;
                var n = m.Next;
                // the far team's toy at the ball as it arrives: it ran there by itself
                if (m.Phase == MatchPhase.Rally && n.Active && n.Team == 1 && n.Reachable && System.Math.Abs(n.Time - planned) > 1e-6 && m.Time >= n.Time - 0.02)
                {
                    planned = n.Time;
                    if (Vector2.Distance(n.Hitter.Position, n.Spot) < m.ReachTolerance) farArrivals++;
                    if (!shotPlaying)
                    {
                        shotPlaying = true;
                        return "tennis_versus.png";
                    }
                }
                return null;
            }, "tennis_duo --alternate", 75f);
            Log($"versus: the far team's toys got to {farArrivals} balls by themselves; swings: " + string.Join(", ", bySlot.Select(s => $"P{s.slot + 1} {s.result}")));
            Assert.AreEqual(TennisMode.Versus, _game.Mode, "both raised their hands: versus started");
            Assert.IsTrue(sawTwo, "two players in view all along");
            Assert.IsTrue(split, "the screen was split");
            Assert.AreEqual(splitFrames, lowFrames, "split screen: all four toys drawn as low-detail chibis");
            Assert.Greater(farArrivals, 0, "player 2's toy ran to player 1's serve by itself");
            Assert.IsTrue(bySlot.Exists(s => s.slot == 0 && s.result is SwingResult.Serve or SwingResult.Toss), "player 1's swings serve");
            Assert.IsTrue(bySlot.Exists(s => s.slot == 0 && s.result == SwingResult.Serve), "a serve went in play");
            Assert.AreEqual(0, wrongServer, "only the player whose turn it is serves (player 2 only in their own service games)");
        }

        /// <summary>
        /// Two players, together (the same script): the computer serves first, so player 2 (who won't serve their team's
        /// first game) starts at the back and player 1 at the net, on their side of the room (the left); player 2's toy
        /// runs to the computer's serve by itself. Screenshot: tennis_together.png.
        /// </summary>
        [UnityTest, Timeout(120000)]
        public IEnumerator TwoPlayers_Together_TheToysRunByThemselves()
        {
            _game.TwoPlayerMode = TennisMode.Together;
            _game.FirstServer = 1;
            bool p1AtNet = false, p2AtBack = false, p1Left = true, shot = false;
            int p2Arrivals = 0;
            double planned = -1;
            yield return RunFakePlayer(() =>
            {
                if (_game.Current != TennisGame.Phase.Playing) return null;
                var m = _game.Match;
                CourtPlayer p1 = m.Players[0], p2 = m.Players[1];
                p1AtNet |= p1.AtNet;
                p2AtBack |= !p2.AtNet;
                // player 1's toy waits at the net on the left, their side of the room
                if (p1.AtNet && m.Phase is MatchPhase.Serve or MatchPhase.Toss && p1.Position.x > -0.5f) p1Left = false;
                var n = m.Next;
                if (m.Phase == MatchPhase.Rally && n.Active && n.Hitter == p2 && n.Reachable && System.Math.Abs(n.Time - planned) > 1e-6 && m.Time >= n.Time - 0.02)
                {
                    planned = n.Time;
                    if (Vector2.Distance(p2.Position, n.Spot) < m.ReachTolerance) p2Arrivals++;
                    if (!shot)
                    {
                        shot = true;
                        return "tennis_together.png";
                    }
                }
                return null;
            }, "tennis_duo --alternate", 60f);
            Log($"together: player 2's toy got to {p2Arrivals} balls by itself");
            Assert.AreEqual(TennisMode.Together, _game.Mode);
            Assert.IsTrue(p1AtNet && p2AtBack, "the computer served first: player 2 at the back, player 1 at the net");
            Assert.IsTrue(p1Left, "player 1's toy waits at the net on the left");
            Assert.Greater(p2Arrivals, 0, "player 2's toy ran to the ball by itself");
        }

        /// <summary>
        /// The menu and a whole tournament (2026-11-07): one player, a tournament on Medium, seen from above, chosen on the
        /// menu; every match won at once (<see cref="TennisMatch.Award"/>): the bracket after the quarter-final and the
        /// semi-final, three different pairs, each round stronger, the cup after the final (counted for Medium).
        /// Screenshots: tennis_menu.png, tennis_view_high.png, tennis_bracket.png, tennis_champion.png.
        /// </summary>
        [UnityTest, Timeout(90000)]
        public IEnumerator Menu_ATournament_TheBracket_TheCup()
        {
            const string cupKey = "tennis.cups.1";
            int cupsBefore = PlayerPrefs.GetInt(cupKey, 0);
            var menu = _game.Menu;
            var saved = (menu.Mode, menu.Format, menu.Difficulty, menu.View);
            try
            {
                menu.Mode = TennisMode.Solo;
                menu.Format = TennisFormat.Tournament;
                menu.Difficulty = TennisDifficulty.Medium;
                menu.View = 1;
                menu.Selected = TennisMenu.Row.Difficulty;
                yield return new WaitForSeconds(1f);   // the camera eases into the view from above
                yield return Screenshot("tennis_menu.png");
                _game.Continue();
                Assert.AreEqual(TennisGame.Phase.Intro, _game.Current, "the menu's game started");
                Assert.IsNotNull(_game.Tournament, "a tournament");
                var opponents = new List<int>();
                float strength = -1f;
                for (int round = 0; round < TennisTournament.Rounds; round++)
                {
                    yield return WaitFor(() => _game.Current == TennisGame.Phase.Playing, 10f);
                    Assert.AreEqual(round, _game.Tournament.Round);
                    Assert.AreEqual(TennisDifficulty.Medium, _game.Match.Level.Difficulty);
                    Assert.Greater(_game.Match.Level.Strength, strength, "each round a stronger pair");
                    strength = _game.Match.Level.Strength;
                    opponents.Add(_game.Tournament.Opponent);
                    if (round == 0)
                    {
                        yield return new WaitForSeconds(2.5f);
                        yield return Screenshot("tennis_view_high.png");
                    }
                    _game.Match.Award(0);
                    yield return WaitFor(() => _game.Current is TennisGame.Phase.Bracket or TennisGame.Phase.Results, 10f);
                    if (round == TennisTournament.Rounds - 1) break;
                    Assert.AreEqual(TennisGame.Phase.Bracket, _game.Current, "the bracket between the matches");
                    if (round == 0)
                    {
                        yield return new WaitForSeconds(0.5f);
                        yield return Screenshot("tennis_bracket.png");
                    }
                    _game.Continue();
                }
                Assert.AreEqual(TennisGame.Phase.Results, _game.Current);
                Assert.IsTrue(_game.Tournament.Won, "the tournament won");
                Assert.AreEqual(cupsBefore + 1, PlayerPrefs.GetInt(cupKey, 0), "the cup counted for Medium");
                Assert.AreEqual(3, opponents.Distinct().Count(), "three different pairs");
                yield return new WaitForSeconds(2f);
                yield return Screenshot("tennis_champion.png");
            }
            finally
            {
                PlayerPrefs.SetInt(cupKey, cupsBefore);
                (menu.Mode, menu.Format, menu.Difficulty, menu.View) = saved;
                menu.Save();
            }
        }

        static IEnumerator WaitFor(System.Func<bool> done, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!done() && Time.realtimeSinceStartup < end) yield return null;
            Assert.IsTrue(done(), "timed out waiting");
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
