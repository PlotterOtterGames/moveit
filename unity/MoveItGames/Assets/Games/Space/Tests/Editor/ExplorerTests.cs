using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MoveIt.Games.Space.Tests
{
    /// <summary>Simulated pilots for Yjet!'s exploration: from a child who lets the autopilot fly to one who steers
    /// straight at every planet with the arms up. Each returns (steer, boost, slow) for the explorer as it is.</summary>
    public sealed class SimPilot
    {
        public string Name;
        /// <summary>Steers toward the target (0 = never: the autopilot flies), with this many degrees of error.</summary>
        public float Steers, AimError;
        /// <summary>Share of the time spent steering somewhere else (looking round), in spells of a few seconds.</summary>
        public float Wander;
        /// <summary>Boosts when the target is farther than this (m; 0 = never), and only while heading at it within
        /// <see cref="BoostCone"/> degrees; <see cref="BoostShare"/> = how often the arms are up when they would be.</summary>
        public float BoostFrom, BoostCone = 25f, BoostShare = 1f;
        /// <summary>Seconds the camera and the body lag behind.</summary>
        public float Lag = 0.2f;

        float _until, _wanderSteer, _error;
        bool _wandering, _armsUp;
        readonly Queue<(float t, float steer, bool boost)> _queue = new Queue<(float, float, bool)>();

        public (float steer, bool boost, bool slow) Fly(Explorer e, System.Random rng)
        {
            float t = e.Time;
            if (t >= _until)
            {
                _until = t + 2f + (float)rng.NextDouble() * 4f;
                _wandering = rng.NextDouble() < Wander;
                _wanderSteer = (float)(rng.NextDouble() * 2 - 1);
                _error = (float)(rng.NextDouble() * 2 - 1) * AimError;
                _armsUp = rng.NextDouble() < BoostShare;
            }
            float steer = 0f;
            bool boost = false;
            if (_wandering) steer = _wanderSteer;
            else if (e.Scanning != null) steer = 0f;
            else if (Steers > 0f && e.Target != null)
            {
                var to = e.Target.Position - e.Position;
                float want = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg + _error;
                float off = Mathf.DeltaAngle(e.Heading * Mathf.Rad2Deg, want);
                steer = Mathf.Clamp(off / 30f, -1f, 1f) * Steers;
                if (Mathf.Abs(steer) < 0.15f) steer = 0f;
                boost = BoostFrom > 0f && _armsUp && to.magnitude > BoostFrom && Mathf.Abs(off) < BoostCone;
            }
            else if (BoostFrom > 0f && e.Target != null)
                boost = _armsUp && Vector2.Distance(e.Target.Position, e.Position) > BoostFrom;
            // what the game sees: the body as it was Lag seconds ago
            _queue.Enqueue((t, steer, boost));
            var seen = _queue.Peek();
            while (_queue.Count > 1 && _queue.Peek().t < t - Lag) seen = _queue.Dequeue();
            return (seen.steer, seen.boost, false);
        }

        public static SimPilot Autopilot() => new SimPilot { Name = "autopilot only" };
        public static SimPilot Child() => new SimPilot { Name = "child", Steers = 0.8f, AimError = 30f, Wander = 0.3f, BoostFrom = 250f, BoostShare = 0.35f, Lag = 0.35f };
        public static SimPilot Good() => new SimPilot { Name = "good", Steers = 1f, AimError = 8f, Wander = 0.1f, BoostFrom = 250f, BoostShare = 0.6f };
        public static SimPilot Expert() => new SimPilot { Name = "expert", Steers = 1f, AimError = 2f, BoostFrom = 160f, BoostCone = 35f, Lag = 0.15f };
    }

    public class ExplorerTests
    {
        const float Dt = 1f / 30f;
        /// <summary>The galaxy map between systems: choosing and the warp (s).</summary>
        const float MapSeconds = 12f;

        static Explorer Fresh(out Galaxy g)
        {
            g = new Galaxy();
            return new Explorer(g, GalaxyProgress.Start(g));
        }

        [Test]
        public void FlyingNear_FindsAPlanet_OnceAndForAll()
        {
            var e = Fresh(out var g);
            var p = e.System.Planets.First(x => !e.Found(x));
            var from = p.Position + new Vector2(0f, -Explorer.FindReach(p) - 60f);
            e.Position = from;
            e.Heading = 0f;
            e.Assist = 0f;
            for (int i = 0; i < 30 && !e.Found(p); i++) e.Step(Dt, 0f, false, false);
            Assert.IsFalse(e.Found(p), "60 m short of its reach: not yet");
            Assert.IsNull(e.Scanning);
            e.Assist = 1f;
            for (int i = 0; i < 300 && e.Scanning == null; i++) e.Step(Dt, 0f, false, false);
            Assert.AreEqual(p, e.Scanning, "in its zone: scanning");
            float start = e.Time, nearest = float.MaxValue, farthest = 0f;
            for (int i = 0; i < 600 && !e.Found(p); i++)
            {
                e.Step(Dt, 0f, false, false);
                nearest = Mathf.Min(nearest, e.Distance(p));
                farthest = Mathf.Max(farthest, e.Distance(p));
            }
            Assert.IsTrue(e.Found(p));
            Assert.AreEqual(Explorer.ScanSeconds, e.Time - start, 0.1f, "the scan takes its seconds, the swing keeps it in the zone");
            Assert.LessOrEqual(farthest, Explorer.FindReach(p) + 0.1f);
            Debug.Log($"swinging round {p.Name} (radius {p.Radius:0}): {nearest:0}-{farthest:0} m from its middle");
            Assert.AreEqual(1, e.Events.Count(x => x.Kind == ExploreEventKind.Found && x.Planet == p));
            Assert.Less(e.Speed, Explorer.Cruise, "it eased down to look");
            for (int i = 0; i < 300; i++) e.Step(Dt, 0f, false, false);
            Assert.AreEqual(1, e.Events.Count(x => x.Kind == ExploreEventKind.Found && x.Planet == p), "found once");
        }

        [Test]
        public void TheAutopilotAlone_FindsEverySystem_TheEventsSaySo()
        {
            var e = Fresh(out var g);
            int done = 0;
            foreach (var s in g.All.Take(12))
            {
                e.Enter(s.Id);
                e.Events.Clear();
                float start = e.Time;
                while (!e.SystemDone && e.Time - start < 900f) e.Step(Dt, 0f, false, false);
                Assert.IsTrue(e.SystemDone, $"{s.Name}: {e.Progress.FoundIn(s)} of {s.Planets.Count} in 15 minutes");
                Assert.AreEqual(1, e.Events.Count(x => x.Kind == ExploreEventKind.SystemComplete), s.Name);
                done++;
            }
            Assert.AreEqual(12, done);
        }

        [Test]
        public void NothingCanBeFlownInto_PastTheEdgeItTurnsBack()
        {
            var e = Fresh(out var g);
            foreach (var s in g.All.Take(10))
            {
                e.Enter(s.Id);
                e.Assist = 0f;
                // straight at the star
                e.Position = new Vector2(0f, -s.Edge * 0.6f);
                e.Heading = 0f;
                float nearest = float.MaxValue;
                for (int i = 0; i < 1800; i++)
                {
                    e.Step(Dt, 0f, true, false);
                    nearest = Mathf.Min(nearest, e.Position.magnitude);
                }
                Assert.GreaterOrEqual(nearest, s.StarRadius * 1.5f + 19f, $"{s.Name}'s star");
                // straight at each planet, boosting
                foreach (var p in s.Planets)
                {
                    var d = p.Position.normalized;
                    var side = new Vector2(-d.y, d.x);
                    e.Position = p.Position + side * (Explorer.FindReach(p) + 300f);
                    var to = p.Position - e.Position;
                    e.Heading = Mathf.Atan2(to.x, to.y);
                    float closest = float.MaxValue;
                    for (int i = 0; i < 600; i++)
                    {
                        e.Step(Dt, 0f, true, false);
                        closest = Mathf.Min(closest, Vector2.Distance(e.Position, p.Position));
                    }
                    Assert.GreaterOrEqual(closest, p.Radius * 1.22f + 9f, $"{p.Name}");
                }
                // straight out: it turns back at the edge
                e.Position = new Vector2(s.Edge - 300f, 0f);
                e.Heading = Mathf.PI / 2f;
                float farthest = 0f;
                for (int i = 0; i < 3000; i++)
                {
                    e.Step(Dt, 0f, true, false);
                    farthest = Mathf.Max(farthest, e.Position.magnitude);
                }
                Assert.Less(farthest, s.Edge + 200f, $"{s.Name}'s edge");
            }
        }

        [Test]
        public void ArmsUp_Fast_Ducking_Slow_TheLeanTurns()
        {
            var e = Fresh(out var g);
            e.Assist = 0f;
            e.Position = new Vector2(0f, -e.System.Edge + 200f);
            for (int i = 0; i < 150; i++) e.Step(Dt, 0f, true, false);
            Assert.Greater(e.Speed, Explorer.Fast * 0.9f, "5 s of boost");
            for (int i = 0; i < 150; i++) e.Step(Dt, 0f, false, true);
            Assert.AreEqual(Explorer.Slow, e.Speed, 0.5f);
            // outside every orbit, going round the sun (nothing to slide round, the edge not near): a full lean right
            e.Position = new Vector2(0f, -(e.System.Edge - 300f));
            e.Heading = Mathf.PI / 2f;
            float h = e.Heading;
            for (int i = 0; i < 30; i++) e.Step(Dt, 1f, false, true);
            Assert.AreEqual(Explorer.TurnRate, (e.Heading - h) * Mathf.Rad2Deg, 2f, "a full lean: TurnRate a second, right");
        }

        [Test]
        public void TheMap_SuggestsTheNearestSystemStillToFind()
        {
            var g = new Galaxy();
            var p = GalaxyProgress.Start(g);
            int next = p.Suggest(g);
            Assert.AreNotEqual(g.Home, next);
            Assert.IsTrue(g.All[g.Home].Links.Contains(next), "a neighbour of home");
            foreach (var planet in g.Planets) p.Found.Add(planet.Id);
            Assert.AreEqual(-1, p.Suggest(g));
        }

        /// <summary>The whole galaxy, system after system as the map suggests, for each simulated pilot: everything is
        /// found by everyone, and how long it takes (hours) is in the range the game is made for.</summary>
        [Test]
        public void TheWholeGalaxy_TakesHours_EveryPilotFindsEverything()
        {
            var results = new List<string>();
            foreach (var make in new System.Func<SimPilot>[] { SimPilot.Autopilot, SimPilot.Child, SimPilot.Good, SimPilot.Expert })
            {
                var pilot = make();
                var rng = new System.Random(5);
                var e = Fresh(out var g);
                int jumps = 0;
                float worstSystem = 0f;
                var perPlanet = new List<float>();
                float lastFound = 0f;
                while (!e.Progress.AllFound(g))
                {
                    float start = e.Time;
                    while (!e.SystemDone && e.Time - start < 1200f)
                    {
                        var (steer, boost, slow) = pilot.Fly(e, rng);
                        int before = e.Progress.Found.Count;
                        e.Step(Dt, steer, boost, slow);
                        if (e.Progress.Found.Count > before)
                        {
                            perPlanet.Add(e.Time - lastFound);
                            lastFound = e.Time;
                        }
                    }
                    Assert.IsTrue(e.SystemDone, $"{pilot.Name} stuck in {e.System.Name}");
                    worstSystem = Mathf.Max(worstSystem, e.Time - start);
                    int next = e.Progress.Suggest(g);
                    if (next < 0) break;
                    e.Enter(next);
                    lastFound = e.Time;
                    jumps++;
                }
                Assert.IsTrue(e.Progress.AllFound(g), pilot.Name);
                float hours = (e.Time + jumps * MapSeconds) / 3600f;
                perPlanet.Sort();
                results.Add($"{pilot.Name}: {hours:0.00} h ({jumps} jumps), a planet every {perPlanet.Average():0} s " +
                            $"(median {perPlanet[perPlanet.Count / 2]:0}, 90 % {perPlanet[perPlanet.Count * 9 / 10]:0}), slowest system {worstSystem / 60f:0.0} min");
                Debug.Log(results[results.Count - 1]);
            }
            foreach (var r in results)
            {
                float hours = float.Parse(r.Substring(r.IndexOf(':') + 2, 4), System.Globalization.CultureInfo.InvariantCulture);
                Assert.That(hours, Is.InRange(1.2f, 6f), r);
            }
        }
    }
}
