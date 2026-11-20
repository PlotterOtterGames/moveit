using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MoveIt.Games.Space.Tests
{
    /// <summary>Yjet!'s galaxy: the same on every box, ~330 planets in 80 systems all linked, unique names, every kind of
    /// world, planets far enough apart to be found one by one, a home to start from; and what a family has found, saved
    /// and read back.</summary>
    public class GalaxyTests
    {
        static readonly Galaxy G = new Galaxy();

        [Test]
        public void TheSameSeed_TheSameGalaxy()
        {
            var a = new Galaxy();
            Assert.AreEqual(G.Planets.Count, a.Planets.Count);
            for (int i = 0; i < a.Planets.Count; i++)
            {
                Assert.AreEqual(G.Planets[i].Name, a.Planets[i].Name);
                Assert.AreEqual(G.Planets[i].Type, a.Planets[i].Type);
                Assert.AreEqual(G.Planets[i].Orbit, a.Planets[i].Orbit);
            }
            Assert.AreNotEqual(G.Planets[5].Name, new Galaxy(7).Planets[5].Name, "another seed, another galaxy");
        }

        [Test]
        public void ManyPlanets_InSystemsAllLinked_NamesUnique()
        {
            Assert.AreEqual(Galaxy.Systems, G.All.Count);
            Debug.Log($"{G.Planets.Count} planets in {G.All.Count} systems");
            Assert.That(G.Planets.Count, Is.InRange(300, 380));
            Assert.AreEqual(G.All.Count, G.Reach(G.Home).Count, "every system can be reached from home");
            var names = new HashSet<string>();
            foreach (var p in G.Planets) Assert.IsTrue(names.Add(p.Name), $"two planets called {p.Name}");
            foreach (var s in G.All) Assert.IsTrue(names.Add(s.Name), $"a star called like a planet: {s.Name}");
            foreach (var s in G.All)
            {
                Assert.That(s.Planets.Count, Is.InRange(3, 6), s.Name);
                Assert.That(s.Links.Count, Is.GreaterThanOrEqualTo(1), s.Name);
                Assert.That(s.Map.magnitude, Is.LessThan(1.02f), s.Name);
            }
        }

        [Test]
        public void EveryKindOfWorld_TheRainbowRare_OneHome()
        {
            var counts = new Dictionary<PlanetType, int>();
            foreach (var p in G.Planets) counts[p.Type] = counts.TryGetValue(p.Type, out int n) ? n + 1 : 1;
            Debug.Log(string.Join(", ", counts.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}")));
            foreach (PlanetType t in System.Enum.GetValues(typeof(PlanetType)))
                Assert.IsTrue(counts.ContainsKey(t), $"no {t} world");
            Assert.AreEqual(1, counts[PlanetType.Home]);
            Assert.Less(counts[PlanetType.Rainbow], G.Planets.Count * 0.08f);
            Assert.AreEqual(PlanetType.Home, G.All[G.Home].Planets.Single(p => p.Type == PlanetType.Home).Type);
        }

        [Test]
        public void PlanetsAreFoundOneByOne_TheirOrbitsApart()
        {
            foreach (var s in G.All)
            {
                for (int i = 0; i < s.Planets.Count; i++)
                {
                    var a = s.Planets[i];
                    Assert.Greater(a.Orbit - a.Radius, s.StarRadius * 2.2f, $"{a.Name} too near its star");
                    Assert.Less(a.Orbit + a.Radius, s.Edge - 150f, $"{a.Name} outside its system");
                    for (int j = i + 1; j < s.Planets.Count; j++)
                    {
                        var b = s.Planets[j];
                        Assert.Greater(Vector2.Distance(a.Position, b.Position), Explorer.FindReach(a) + Explorer.FindReach(b),
                                       $"{a.Name} and {b.Name} would be found together");
                    }
                }
            }
        }

        [Test]
        public void Looks_HaveARamp_GasGiantsBands_SomeRingsAndMoons()
        {
            int rings = 0, moons = 0;
            foreach (var p in G.Planets)
            {
                var l = p.Look;
                Assert.GreaterOrEqual(l.Ramp.Count, 3, p.Name);
                for (int i = 1; i < l.Ramp.Count; i++) Assert.GreaterOrEqual(l.Ramp[i].at, l.Ramp[i - 1].at, $"{p.Name}'s ramp in order");
                Assert.That(l.Height, Is.InRange(0, 7));
                if (p.Type == PlanetType.Gas) Assert.Greater(l.Bands, 0.5f);
                if (l.Rings) rings++;
                moons += l.Moons.Count;
            }
            Debug.Log($"{rings} ringed planets, {moons} moons");
            Assert.Greater(rings, 25);
            Assert.Greater(moons, 50);
        }

        [Test]
        public void Progress_SavedAndReadBack_TheFog()
        {
            var p = GalaxyProgress.Start(G);
            Assert.AreEqual(1, p.Found.Count, "the home planet");
            Assert.IsTrue(p.Visited.Contains(G.Home));
            int linked = G.All[G.Home].Links[0];
            Assert.IsTrue(p.Reachable(G, linked), "a neighbour of home can be flown to");
            var far = G.All.First(s => !s.Links.Contains(G.Home) && s.Id != G.Home);
            Assert.IsFalse(p.Reachable(G, far.Id), "the fog: not yet");
            p.Found.Add(17);
            p.Found.Add(G.Planets.Count - 1);
            p.Visited.Add(linked);
            p.Current = linked;
            p.Seconds = 1234f;
            var q = GalaxyProgress.Load(G, p.Save(G));
            CollectionAssert.AreEquivalent(p.Found, q.Found);
            CollectionAssert.AreEquivalent(p.Visited, q.Visited);
            Assert.AreEqual(linked, q.Current);
            Assert.AreEqual(1234f, q.Seconds, 1f);
            Assert.AreEqual(1, GalaxyProgress.Load(G, "garbage").Found.Count);
            Assert.AreEqual(1, GalaxyProgress.Load(G, null).Found.Count);
            Assert.AreEqual(1, GalaxyProgress.Load(G, "1|0|0|0|zz|zz").Found.Count, "unreadable bits: a fresh start");
        }
    }
}
