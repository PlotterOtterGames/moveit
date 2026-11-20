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
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace MoveIt.Games.Space.Tests
{
    /// <summary>
    /// Yjet!, the exploration (2026-11-14): a scripted player (fake_sender.py --script space_fly: steps to turn, both arms
    /// up to boost, a duck to slow down) flies the astronaut and a planet is found; the game flying itself finds a whole
    /// system, the galaxy map opens, the warp takes it to the next system; two players fly one trip (--script
    /// space_fly_duo --alternate); the map of a galaxy well explored. Nothing is saved (<see cref="SpaceGame.Saving"/>).
    /// Screenshots: unity/captures/space_*.png.
    /// </summary>
    public class SpaceGameTests
    {
        const int MotionPort = 5028;

        Process _sender;
        SpaceGame _game;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("Space");
            yield return null;
            yield return null;
            _game = Object.FindAnyObjectByType<SpaceGame>();
            Assert.IsNotNull(_game, "Space scene has no SpaceGame");
            _game.Saving = false;
            _game.Choose(SpaceMode.Solo);
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
            if (_game != null) _game.Autoplay = false;
            if (MotionSystem.Instance != null) Object.Destroy(MotionSystem.Instance.gameObject);
            yield return null;
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator Solo_TheBodySteersAndBoosts_APlanetIsFound()
        {
            yield return new WaitForSeconds(1.5f);
            yield return Screenshot("space_menu.png");
            StartFakePlayer("space_fly");
            float deadline = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < deadline && (MotionSystem.Instance.ActiveBody == null || !MotionSystem.Instance.ActiveBody.IsTracking)) yield return null;
            Assert.IsNotNull(MotionSystem.Instance.ActiveBody, "the scripted player is seen");
            // the scripted player raises its hands (the start), then flies
            deadline = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < deadline && _game.Current == SpaceGame.Phase.Waiting) yield return null;
            if (_game.Current == SpaceGame.Phase.Waiting) _game.Continue();
            Assert.AreEqual(SpaceGame.Phase.Flying, _game.Current);
            float minSteer = 0f, maxSteer = 0f;
            bool boosted = false, ducked = false, fast = false;
            var found = new List<Planet>();
            _game.Explored += e =>
            {
                if (e.Kind == ExploreEventKind.Found) found.Add(e.Planet);
            };
            // 16 s of the body's flying: a turn each way, a boost, a duck
            deadline = Time.realtimeSinceStartup + 16f;
            bool shotFlight = false, shotBoost = false;
            while (Time.realtimeSinceStartup < deadline)
            {
                minSteer = Mathf.Min(minSteer, _game.Steer);
                maxSteer = Mathf.Max(maxSteer, _game.Steer);
                boosted |= _game.Boosting;
                ducked |= _game.MovesOf(0).Ducking;
                fast |= _game.Explorer.Speed > Explorer.Cruise * 1.8f;
                if (!shotFlight && Time.realtimeSinceStartup > deadline - 13f)
                {
                    shotFlight = true;
                    yield return Screenshot("space_flight.png");
                }
                if (!shotBoost && _game.Explorer.Boost > 0.8f)
                {
                    shotBoost = true;
                    yield return Screenshot("space_boost.png");
                }
                yield return null;
            }
            Log($"steer {minSteer:0.00}..{maxSteer:0.00}, boosted {boosted} (fast {fast}), ducked {ducked}");
            Assert.Less(minSteer, -0.5f, "a step to the left turns left");
            Assert.Greater(maxSteer, 0.5f, "a step to the right turns right");
            Assert.IsTrue(boosted && fast, "both arms up: a boost");
            Assert.IsTrue(ducked, "a duck is seen");
            // to the nearest planet not found yet (put near it), the body flying on: the scan, then it's found
            var target = _game.Explorer.Target;
            Assert.IsNotNull(target);
            _game.PlaceNear(target, 30f);
            bool shotScan = false;
            deadline = Time.realtimeSinceStartup + 40f;
            while (Time.realtimeSinceStartup < deadline && !found.Contains(target))
            {
                if (!shotScan && _game.Explorer.Scanning == target && _game.Explorer.ScanOf(target) > 0.45f)
                {
                    shotScan = true;
                    yield return Screenshot("space_scan.png");
                }
                yield return null;
            }
            Assert.Contains(target, found, $"{target.Name} found");
            yield return new WaitForSeconds(0.6f);
            yield return Screenshot("space_found.png");
            Log($"found {target.Name} ({target.Type}), {found.Count} in all");
        }

        [UnityTest, Timeout(420000)]
        public IEnumerator Autoplay_ASystemFound_TheMapOpens_TheWarpToTheNext()
        {
            string before = PlayerPrefs.GetString("space.galaxy", "");
            _game.Autoplay = true;
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(SpaceGame.Phase.Flying, _game.Current);
            var system = _game.Explorer.System;
            Log($"system {system.Name}: {system.Planets.Count} planets, {_game.Progress.FoundIn(system)} found");
            float start = Time.realtimeSinceStartup, deadline = start + 330f;
            int shots = 0;
            float nextShot = start + 6f;
            while (Time.realtimeSinceStartup < deadline && _game.Current != SpaceGame.Phase.Map)
            {
                if (Time.realtimeSinceStartup > nextShot && shots < 4)
                {
                    nextShot = Time.realtimeSinceStartup + 18f;
                    yield return Screenshot($"space_auto_{shots++}.png");
                }
                yield return null;
            }
            Log($"system done in {Time.realtimeSinceStartup - start:0} s; {_game.Progress.Found.Count} planets found");
            Assert.IsTrue(_game.Progress.Complete(system), "every planet of the system found");
            Assert.AreEqual(SpaceGame.Phase.Map, _game.Current, "the map opened by itself");
            yield return new WaitForSeconds(0.8f);
            yield return Screenshot("space_map.png");
            int to = _game.MapCursor;
            Assert.AreNotEqual(system.Id, to, "the map suggests another system");
            deadline = Time.realtimeSinceStartup + 10f;
            bool warped = false;
            while (Time.realtimeSinceStartup < deadline && _game.Current != SpaceGame.Phase.Flying)
            {
                if (!warped && _game.Current == SpaceGame.Phase.Warp)
                {
                    warped = true;
                    yield return new WaitForSeconds(0.6f);
                    yield return Screenshot("space_warp.png");
                }
                yield return null;
            }
            Assert.IsTrue(warped, "a warp");
            Assert.AreEqual(to, _game.Explorer.System.Id, "in the system chosen");
            yield return new WaitForSeconds(2.5f);
            yield return Screenshot("space_arrival.png");
            Assert.AreEqual(before, PlayerPrefs.GetString("space.galaxy", ""), "the game flying itself saves nothing");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Together_TwoAstronautsFlyOneTrip()
        {
            _game.Choose(SpaceMode.Together);
            StartFakePlayer("space_fly_duo", alternate: true);
            float deadline = Time.realtimeSinceStartup + 14f;
            while (Time.realtimeSinceStartup < deadline && _game.Current == SpaceGame.Phase.Waiting) yield return null;
            if (_game.Current == SpaceGame.Phase.Waiting) _game.Continue();
            Assert.AreEqual(SpaceGame.Phase.Flying, _game.Current);
            Assert.AreEqual(SpaceMode.Together, _game.Mode);
            Assert.AreEqual(2, _game.Astronauts.Count(a => a != null && a.gameObject.activeSelf), "two astronauts");
            float max0 = 0f, max1 = 0f;
            deadline = Time.realtimeSinceStartup + 14f;
            bool shot = false;
            while (Time.realtimeSinceStartup < deadline)
            {
                max0 = Mathf.Max(max0, Mathf.Abs(_game.MovesOf(0).Steer));
                max1 = Mathf.Max(max1, Mathf.Abs(_game.MovesOf(1).Steer));
                if (!shot && Time.realtimeSinceStartup > deadline - 9f)
                {
                    shot = true;
                    yield return Screenshot("space_together.png");
                }
                yield return null;
            }
            Log($"player 1 steered up to {max0:0.00}, player 2 up to {max1:0.00}; together {_game.Steer:0.00} now");
            Assert.Greater(max0, 0.5f);
            Assert.Greater(max1, 0.5f);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator TheMap_AGalaxyWellExplored()
        {
            // a copy of the progress the game plays on (nothing saved): 14 systems visited and found
            _game.Autoplay = true;
            yield return null;
            var g = _game.Galaxy;
            var p = _game.Progress;
            var order = g.All.OrderBy(s => Vector2.Distance(s.Map, g.All[g.Home].Map)).Take(14).ToList();
            foreach (var s in order)
            {
                p.Visited.Add(s.Id);
                foreach (var planet in s.Planets.Take(s.Id % 3 == 0 ? s.Planets.Count - 1 : s.Planets.Count)) p.Found.Add(planet.Id);
            }
            yield return new WaitForSeconds(0.5f);
            _game.OpenMap();
            yield return new WaitForSeconds(0.9f);
            yield return Screenshot("space_map_explored.png");
            Assert.AreEqual(SpaceGame.Phase.Map, _game.Current);
        }

        /// <summary>One planet of every kind, flown up to (the game flying itself, nothing saved): what each kind looks like
        /// up close, in its own system's light and sky (space_planet_&lt;kind&gt;.png).</summary>
        [UnityTest, Timeout(180000)]
        public IEnumerator Gallery_EveryKindOfPlanet()
        {
            _game.Autoplay = true;
            yield return new WaitForSeconds(0.5f);
            var g = _game.Galaxy;
            foreach (PlanetType kind in System.Enum.GetValues(typeof(PlanetType)))
            {
                var p = g.Planets.FirstOrDefault(x => x.Type == kind && (x.Look.Rings || kind is PlanetType.Home or PlanetType.Ocean or PlanetType.Continents or PlanetType.Jungle))
                        ?? g.Planets.First(x => x.Type == kind);
                _game.Visit(p.System);
                if (kind == PlanetType.Home) _game.PlaceNear(p, 120f);
                else _game.PlaceNear(p, 20f);
                yield return new WaitForSeconds(kind == PlanetType.Home ? 1.2f : 2.2f);
                yield return Screenshot($"space_planet_{kind.ToString().ToLowerInvariant()}.png");
                Log($"{kind}: {p.Name} in {g.All[p.System].Name} ({g.All[p.System].Star}), radius {p.Radius:0}, rings {p.Look.Rings}, moons {p.Look.Moons.Count}");
            }
        }

        static void Log(string text) => Debug.Log($"[SpaceTest] {text}");

        void StartFakePlayer(string script, bool alternate = false)
        {
            string serviceDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../motion-service-py"));
            string python = Path.Combine(serviceDir, ".venv/Scripts/python.exe");
            Assume.That(File.Exists(python), $"Python venv not found at {python}");
            _sender = Process.Start(new ProcessStartInfo(python, $"fake_sender.py --script {script} --port {MotionPort}{(alternate ? " --alternate" : "")}")
            {
                WorkingDirectory = serviceDir,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
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
