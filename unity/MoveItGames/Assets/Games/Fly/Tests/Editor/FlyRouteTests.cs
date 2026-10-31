using System;
using NUnit.Framework;
using UnityEngine;

namespace MoveIt.Games.Fly.Tests
{
    /// <summary>
    /// The way through the city (<see cref="FlyRoute"/>): straight on in the middle, 90° turns when steering to a side,
    /// forced turns at T-junctions toward the hero's side, never into a missing street, always a way on.
    /// </summary>
    public class FlyRouteTests
    {
        const float Step = 0.25f;   // metres per Advance: about one frame at full speed

        /// <summary>A crossroads reached flying north whose streets match, searched outward from the start.</summary>
        static (int i, int j) Find(FlyRoute route, Func<Junction, bool> match)
        {
            for (int r = 1; r < 40; r++)
            for (int i = -r; i <= r; i++)
            for (int j = -r; j <= r; j++)
            {
                if (!route.Street(i, j, 2)) continue;   // the street we arrive on (from the south)
                route.StartAt(i, j, 0, 30f);
                if (match(route.Next)) return (i, j);
            }
            Assert.Fail("no such crossroads near the start");
            return default;
        }

        static void Fly(FlyRoute route, float metres, float steer)
        {
            for (float done = 0f; done < metres; done += Step) route.Advance(Step, steer);
        }

        [Test]
        public void Middle_FliesStraightOn()
        {
            var route = new FlyRoute();
            var (i, j) = Find(route, n => n.Straight && n.Left && n.Right);
            Fly(route, 60f, 0f);
            Assert.AreEqual(0f, route.Heading, 1e-3f);
            Assert.AreEqual(0, route.TurnsTaken);
            Assert.AreEqual(i * route.Pitch, route.Position.x, 1e-3f, "still on the north-south centre line");
            Assert.AreEqual(j * route.Pitch + 30f, route.Position.y, 0.3f);
        }

        [TestCase(1f, 90f)]
        [TestCase(-1f, 270f)]
        public void SteeringToASide_Turns90DegreesOntoTheCrossStreet(float steer, float heading)
        {
            var route = new FlyRoute();
            var (i, j) = Find(route, n => n.Straight && n.Left && n.Right);
            Fly(route, 60f, steer);
            Assert.AreEqual(heading, route.Heading, 1e-3f);
            Assert.AreEqual(1, route.TurnsTaken);
            Assert.IsFalse(route.Turning);
            Assert.AreEqual(j * route.Pitch, route.Position.y, 1e-3f, "on the east-west centre line");
            // 30 m to the crossroads: (30 - R) straight, a quarter circle, then straight along the new street
            float along = 60f - (30f - route.TurnRadius) - Mathf.PI * 0.5f * route.TurnRadius + route.TurnRadius;
            Assert.AreEqual(i * route.Pitch + Mathf.Sign(steer) * along, route.Position.x, 0.3f);
        }

        [Test]
        public void Turn_IsOneSmoothQuarterCircle()
        {
            var route = new FlyRoute();
            Find(route, n => n.Right);
            float arc = 0f;
            while (route.TurnsTaken == 0) route.Advance(Step, 1f);
            Assert.IsTrue(route.Turning);
            float last = route.Heading;
            while (route.Turning)
            {
                route.Advance(Step, 1f);
                float h = route.Turning ? route.Heading : 90f;
                Assert.GreaterOrEqual(h, last - 1e-3f, "the heading only grows");
                Assert.LessOrEqual(h - last, Step / route.TurnRadius * Mathf.Rad2Deg + 1e-2f, "no jumps");
                last = h;
                arc += Step;
            }
            Assert.AreEqual(Mathf.PI * 0.5f * route.TurnRadius, arc, 2f * Step);
        }

        [Test]
        public void TJunction_TurnsAnyway_TowardTheHerosSide()
        {
            var route = new FlyRoute();
            var (i, j) = Find(route, n => !n.Straight && n.Left && n.Right);
            Fly(route, 60f, 0.2f);   // leaning right, but not enough to choose
            Assert.AreEqual(90f, route.Heading, 1e-3f, "turned right, the side the hero is on");

            route.StartAt(i, j, 0, 30f);
            Fly(route, 60f, -0.2f);
            Assert.AreEqual(270f, route.Heading, 1e-3f);

            route.StartAt(i, j, 0, 30f);
            Fly(route, 60f, 0f);
            Assert.That(route.Heading, Is.EqualTo(90f).Within(1e-3f).Or.EqualTo(270f).Within(1e-3f), "in the middle: either way");
        }

        [Test]
        public void IntoAMissingStreet_FliesStraightOn()
        {
            var route = new FlyRoute();
            Find(route, n => n.Straight && !n.Right);
            Fly(route, 60f, 1f);
            Assert.AreEqual(0f, route.Heading, 1e-3f);
            Assert.AreEqual(0, route.TurnsTaken);
        }

        [Test]
        public void EveryCrossroads_KeepsAtLeastTwoStreets_AndTJunctionsExist()
        {
            var route = new FlyRoute();
            int tees = 0;
            for (int i = -30; i <= 30; i++)
            for (int j = -30; j <= 30; j++)
            {
                int streets = 0;
                for (int d = 0; d < 4; d++) if (route.Street(i, j, d)) streets++;
                Assert.GreaterOrEqual(streets, 2, $"crossroads {i},{j}");
                if (streets == 3) tees++;
            }
            Assert.Greater(tees, 61 * 61 / 10, "T-junctions are common enough to matter");
            Assert.Less(tees, 61 * 61 * 3 / 5, "...but plenty are real crossroads");
        }

        [Test]
        public void LongRandomFlight_StaysOnStreetsThatExist()
        {
            var route = new FlyRoute(seed: 3);
            var rng = new System.Random(11);
            float steer = 0f;
            int forced = 0;
            for (int k = 0; k < 80000; k++)   // 20 km
            {
                if (k % 160 == 0) steer = (float)rng.NextDouble() * 2f - 1f;   // a new wish every 40 m
                var next = route.Next;
                var turn = route.Advance(Step, steer);
                if (turn != TurnChoice.Straight && !next.Straight) forced++;
                if (route.Turning) continue;
                var p = route.Position;
                Assert.AreEqual(0f, Mathf.Repeat(route.Heading, 90f), 1e-3f, "straight flight is along a street");
                bool northSouth = route.Direction % 2 == 0;
                float line = northSouth ? p.x : p.y, along = northSouth ? p.y : p.x;
                int street = Mathf.RoundToInt(line / route.Pitch);
                Assert.AreEqual(street * route.Pitch, line, 1e-2f, "on a centre line");
                int segment = Mathf.FloorToInt(along / route.Pitch);
                bool exists = northSouth ? route.NorthSouth(street, segment) : route.EastWest(segment, street);
                Assert.IsTrue(exists, $"flying a street that exists at {p}");
            }
            Assert.Greater(route.TurnsTaken, 50);
            Assert.Greater(forced, 5, "some turns were T-junctions");
        }
    }
}
