using NUnit.Framework;
using UnityEngine;

namespace MoveIt.Games.Kart.Tests
{
    /// <summary>Gara!'s three tracks: closed loops of a lap's length, corners a kart can take, a road that never touches
    /// another part of itself, jumps on straight road, everything on the road.</summary>
    public class KartTrackTests
    {
        [Test]
        public void EveryTrack_IsAClosedLoop_OfALapsLength_WithCornersAKartCanTake()
        {
            for (int i = 0; i < KartTracks.Count; i++)
            {
                var t = KartTracks.Get(i);
                Assert.That(t.Length, Is.InRange(780f, 980f), $"{t.Id}: a lap of ~35 s");
                Assert.GreaterOrEqual(t.TightestRadius, 16f, $"{t.Id}: the tightest corner");
                // closed: the end meets the start, the heading carries on
                Assert.Less(Vector2.Distance(t.Point(0f), t.Point(t.Length)), 0.01f, t.Id);
                Assert.Less(Mathf.Abs(KartTrack.DeltaAngle(t.Heading(t.Length - 0.5f), t.Heading(0.5f))), 0.1f, t.Id);
                // the road and its grass (and a margin for the barriers and scenery) never meet another part of it
                Assert.Greater(t.ClosestApproach(), t.Wall * 2f + 6f, $"{t.Id}: parts of the track too close");
                // the samples are about a metre apart and evenly so
                Assert.That(t.SampleStep, Is.InRange(0.95f, 1.05f), t.Id);
            }
        }

        [Test]
        public void TheGrid_IsOnStraightRoad_BeforeTheLine()
        {
            for (int i = 0; i < KartTracks.Count; i++)
            {
                var t = KartTracks.Get(i);
                for (int g = 0; g < 6; g++)
                {
                    KartRace.GridSlot(g, out float s, out float d);
                    Assert.Less(s, 0f);
                    Assert.Less(Mathf.Abs(d), t.HalfWidth - 1f);
                    Assert.Less(Mathf.Abs(t.Curvature(s)), 1f / 60f, $"{t.Id}: grid slot {g} on a corner");
                }
            }
        }

        [Test]
        public void Ramps_AreOnStraightRoad_AndEverythingIsOnTheRoad()
        {
            for (int i = 0; i < KartTracks.Count; i++)
            {
                var t = KartTracks.Get(i);
                int ramps = 0, boxes = 0, pads = 0;
                foreach (var f in t.Features)
                {
                    Assert.That(f.S, Is.InRange(0f, t.Length), $"{t.Id}: {f.Kind} at {f.S}");
                    Assert.LessOrEqual(Mathf.Abs(f.D) + f.Width * 0.5f, t.HalfWidth + 0.01f, $"{t.Id}: {f.Kind} at {f.S} off the road");
                    switch (f.Kind)
                    {
                        case FeatureKind.Ramp:
                            ramps++;
                            // the run-up and the flight (~24 m) on fairly straight road: the kart lands on the road
                            for (float x = -f.Length; x <= 26f; x += 2f)
                                Assert.Less(Mathf.Abs(t.Curvature(f.S + x)), 1f / 60f, $"{t.Id}: ramp at {f.S}, curve at +{x}");
                            break;
                        case FeatureKind.Items:
                            boxes++;
                            break;
                        case FeatureKind.Boost:
                            pads++;
                            break;
                        case FeatureKind.Stars:
                            Assert.LessOrEqual(Mathf.Abs(f.D), t.HalfWidth - 1f);
                            break;
                    }
                }
                Assert.That(ramps, Is.InRange(1, 3), t.Id);
                Assert.AreEqual(2, boxes, $"{t.Id}: two rows of gift boxes a lap");
                Assert.That(pads, Is.InRange(3, 6), t.Id);
            }
        }

        [Test]
        public void WorldPoints_FollowTheRoad_AndItsHills()
        {
            var t = KartTracks.Get(KartTheme.Village);
            for (float s = 0f; s < t.Length; s += 37f)
            {
                var p = t.World(s, 0f);
                var left = t.World(s, 3f);
                Assert.AreEqual(3f, Vector3.Distance(p, left), 1e-3f);
                Assert.AreEqual(t.Height(s), p.y, 1e-4f);
                Assert.Less(Mathf.Abs(t.Slope(s)), 0.12f, $"gentle hills ({s})");
                // the left vector really is to the left of the way the road goes
                var tan = t.Tangent(s);
                var l = t.Left(s);
                Assert.Greater(tan.x * l.y - tan.y * l.x, 0.99f);
            }
            Assert.Greater(t.Height(t.Length * 0.5f), 8f, "the village climbs its hill");
        }
    }
}
