using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MoveIt.Games.Tennis.Tests
{
    public class TennisBallTests
    {
        static (PathPoint bounce, bool net) FirstBounce(BallState b)
        {
            var path = new List<PathPoint>();
            BallPhysics.Predict(b, 0, 6f, path, 1);
            foreach (var p in path)
            {
                if (p.Event == BallEvent.Net) return (p, true);
                if (p.Event == BallEvent.Bounce) return (p, false);
            }
            return (path[path.Count - 1], false);
        }

        [Test]
        public void Aim_LandsOnTheTarget_AndClearsTheNet_ForEveryKindOfShot()
        {
            var froms = new[] { new Vector3(0f, 1f, -12f), new Vector3(3f, 0.6f, -10f), new Vector3(-1.5f, 2.6f, -3.6f), new Vector3(1f, 3f, 12.4f) };
            var shots = new[] { (13f, 0.15f, 0.45f), (20f, 0.75f, 0.9f), (11f, -0.7f, 0.25f), (9.5f, 0.3f, 3f), (24f, 0.1f, 0.1f) };
            int checks = 0;
            foreach (var from in froms)
            foreach (var (speed, spin, clearance) in shots)
            foreach (float tx in new[] { -4.5f, 0f, 4.5f })
            {
                float side = from.z < 0f ? 1f : -1f;
                var target = new Vector3(tx, 0f, side * 8.5f);
                var v = BallPhysics.Aim(from, target, speed, spin, clearance, out float t);
                var (bounce, net) = FirstBounce(new BallState(from, v, spin));
                Assert.IsFalse(net, $"from {from} at {speed} m/s, spin {spin}: into the net");
                float miss = Vector2.Distance(new Vector2(bounce.Ball.Position.x, bounce.Ball.Position.z), new Vector2(target.x, target.z));
                Assert.Less(miss, 0.12f, $"from {from} at {speed} m/s, spin {spin}: lands {miss:0.00} m off");
                Assert.AreEqual(t, (float)bounce.Time, 0.03f, "the flight time it reports");
                checks++;
            }
            Assert.AreEqual(60, checks);
        }

        [Test]
        public void IntoTheNet_HitsTheNet()
        {
            var from = new Vector3(0f, 1f, 11f);
            var (_, net) = FirstBounce(new BallState(from, BallPhysics.IntoTheNet(from, new Vector3(1f, 0f, -9f), 15f)));
            Assert.IsTrue(net);
        }

        [Test]
        public void Slice_StaysLow_Topspin_KicksUpAndOn()
        {
            float ApexAfterBounce(float spin)
            {
                var from = new Vector3(0f, 1f, -11f);
                var v = BallPhysics.Aim(from, new Vector3(0f, 0f, 8f), 14f, spin, 0.4f, out _);
                var path = new List<PathPoint>();
                BallPhysics.Predict(new BallState(from, v, spin), 0, 6f, path, 2);
                float apex = 0f;
                bool bounced = false;
                foreach (var p in path)
                {
                    if (p.Event == BallEvent.Bounce) bounced = true;
                    else if (bounced) apex = Mathf.Max(apex, p.Ball.Position.y);
                }
                return apex;
            }
            float slice = ApexAfterBounce(-0.7f), flat = ApexAfterBounce(0.15f);
            Assert.Less(slice, flat, $"slice bounces lower ({slice:0.00} m vs {flat:0.00} m)");
        }

        [Test]
        public void Lines_CountAsIn_ServiceBoxes_AreDiagonal()
        {
            Assert.IsTrue(Court.InCourt(new Vector3(Court.SinglesHalfWidth, 0f, -Court.HalfLength), 0), "on the corner lines");
            Assert.IsFalse(Court.InCourt(new Vector3(Court.SinglesHalfWidth + 0.5f, 0f, -5f), 0), "singles: the doubles alley is out");
            Assert.IsFalse(Court.InCourt(new Vector3(0f, 0f, -Court.HalfLength - 0.2f), 0), "long");
            Assert.IsFalse(Court.InCourt(new Vector3(0f, 0f, 3f), 0), "the other half");
            // team 0's deuce box is on its right (+x), the advantage box on its left
            Assert.IsTrue(Court.InServiceBox(new Vector3(2f, 0f, -5f), 0, deuceCourt: true));
            Assert.IsFalse(Court.InServiceBox(new Vector3(-2f, 0f, -5f), 0, deuceCourt: true));
            Assert.IsTrue(Court.InServiceBox(new Vector3(-2f, 0f, -5f), 0, deuceCourt: false));
            Assert.IsFalse(Court.InServiceBox(new Vector3(2f, 0f, -7f), 0, deuceCourt: true), "past the service line");
            // team 1 faces the other way: its right is -x
            Assert.IsTrue(Court.InServiceBox(new Vector3(-2f, 0f, 5f), 1, deuceCourt: true));
        }
    }
}
