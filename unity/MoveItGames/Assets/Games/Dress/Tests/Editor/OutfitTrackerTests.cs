using System;
using MoveIt.Motion;
using NUnit.Framework;
using UnityEngine;
using Joint = MoveIt.Motion.Joint;

namespace MoveIt.Games.Dress.Tests
{
    /// <summary>
    /// <see cref="OutfitTracker"/>: the joints Vishu!'s clothes are fitted to, for a camera picture shown a couple of
    /// frames late. Measured on a box recording (tools/motion/dress_bench): the clothes shook ~10x more than the body moved
    /// and trailed it by ~25 ms with the old pipeline; these tests keep the properties that fixed it.
    /// </summary>
    public class OutfitTrackerTests
    {
        const float Torso = 0.25f;               // view units (picture heights)
        const double Frame = 1.0 / 30.0;

        /// <summary>A person facing the camera at (x, y) = shoulder centre, arms hanging; the right wrist can be moved.</summary>
        static void Body(Vector3[] j, float[] v, Vector3[] w, float x, float y, Vector2 rightWrist = default, float yawDeg = 0f)
        {
            float s = 0.4f * Torso;
            j[(int)Joint.Nose] = new Vector3(x, y + 0.38f * Torso);
            j[(int)Joint.LeftEye] = new Vector3(x - 0.064f * Torso, y + 0.47f * Torso);
            j[(int)Joint.RightEye] = new Vector3(x + 0.064f * Torso, y + 0.47f * Torso);
            j[(int)Joint.LeftEar] = new Vector3(x - 0.15f * Torso, y + 0.43f * Torso);
            j[(int)Joint.RightEar] = new Vector3(x + 0.15f * Torso, y + 0.43f * Torso);
            j[(int)Joint.LeftShoulder] = new Vector3(x - s, y);
            j[(int)Joint.RightShoulder] = new Vector3(x + s, y);
            j[(int)Joint.LeftElbow] = new Vector3(x - 0.5f * Torso, y - 0.63f * Torso);
            j[(int)Joint.RightElbow] = new Vector3(x + 0.5f * Torso, y - 0.63f * Torso);
            j[(int)Joint.LeftWrist] = new Vector3(x - 0.55f * Torso, y - 1.19f * Torso);
            j[(int)Joint.RightWrist] = rightWrist != default ? (Vector3)rightWrist : new Vector3(x + 0.55f * Torso, y - 1.19f * Torso);
            j[(int)Joint.LeftHip] = new Vector3(x - 0.24f * Torso, y - Torso);
            j[(int)Joint.RightHip] = new Vector3(x + 0.24f * Torso, y - Torso);
            j[(int)Joint.LeftKnee] = new Vector3(x - 0.25f * Torso, y - 1.92f * Torso);
            j[(int)Joint.RightKnee] = new Vector3(x + 0.25f * Torso, y - 1.92f * Torso);
            j[(int)Joint.LeftAnkle] = new Vector3(x - 0.25f * Torso, y - 2.85f * Torso);
            j[(int)Joint.RightAnkle] = new Vector3(x + 0.25f * Torso, y - 2.85f * Torso);
            for (int i = 0; i < Skeleton.JointCount; i++) v[i] = 0.99f;
            // world joints (metres, x right, z depth): shoulders and hips turned by yaw
            float c = Mathf.Cos(yawDeg * Mathf.Deg2Rad), sn = Mathf.Sin(yawDeg * Mathf.Deg2Rad);
            Vector3 Turn(float wx, float wy) => new Vector3(wx * c, wy, wx * sn);
            for (int i = 0; i < Skeleton.JointCount; i++) w[i] = Turn((j[i].x - x) / Torso * 0.5f, (j[i].y - y) / Torso * 0.5f);
        }

        [Test]
        public void ASteadyMove_IsFollowedWithoutLag_AndShakesLessThanTheSamples()
        {
            var tracker = new OutfitTracker { Lookahead = 2 };
            var rng = new System.Random(3);
            var j = new Vector3[17];
            var v = new float[17];
            var w = new Vector3[17];
            var outJ = new Vector2[17];
            var outV = new float[17];
            double rawShake = 0, fitShake = 0, along = 0;
            int n = 0;
            Vector2 a1 = default, a2 = default, r1 = default, r2 = default;
            float speed = 1.5f * Torso;   // a torso and a half a second, sideways
            for (int f = 0; f < 120; f++)
            {
                double t = f * Frame;
                float x = 0.6f + speed * (float)t;
                Body(j, v, w, x, 0.7f);
                Vector3 noise = new Vector3((float)(rng.NextDouble() - 0.5), (float)(rng.NextDouble() - 0.5)) * (0.03f * Torso);
                j[(int)Joint.LeftShoulder] += noise;
                tracker.Add(t, j, v, w);
                if (f < 10) continue;
                double show = tracker.ShowTime;
                Assert.AreEqual(t - 2 * Frame, show, 0.004, "the picture waits two frames");
                tracker.At(show, outJ, outV);
                Vector2 truth = new Vector2(0.6f + speed * (float)show - 0.4f * Torso, 0.7f);
                Vector2 got = outJ[(int)Joint.LeftShoulder];
                along += (got.x - truth.x) / speed;   // seconds ahead (+) or behind (-)
                Vector2 raw = (Vector2)j[(int)Joint.LeftShoulder];
                if (n >= 2)
                {
                    fitShake += (got - 2 * a1 + a2).sqrMagnitude;
                    rawShake += (raw - 2 * r1 + r2).sqrMagnitude;
                }
                a2 = a1; a1 = got; r2 = r1; r1 = raw;
                n++;
            }
            Assert.AreEqual(0.0, along / n, 0.004, "no lag (seconds)");
            Assert.Less(Math.Sqrt(fitShake), 0.4 * Math.Sqrt(rawShake), "shakes far less than the samples");
        }

        [Test]
        public void AGlitchOfAFrameOrTwo_IsLeftOut()
        {
            foreach (int length in new[] { 1, 2 })
            {
                var tracker = new OutfitTracker { Lookahead = 2 };
                var j = new Vector3[17];
                var v = new float[17];
                var w = new Vector3[17];
                var outJ = new Vector2[17];
                var outV = new float[17];
                Vector2 still = new Vector2(0.6f + 0.55f * Torso, 0.7f - 1.19f * Torso);
                float worst = 0f;
                for (int f = 0; f < 40; f++)
                {
                    bool glitch = f >= 20 && f < 20 + length;
                    // the pose model pulls the wrist onto the elbow, or flings it away (0.6 torso)
                    Body(j, v, w, 0.6f, 0.7f, glitch ? still + new Vector2(-0.5f * Torso, 0.35f * Torso) : default);
                    tracker.Add(f * Frame, j, v, w);
                    if (f < 6) continue;
                    tracker.At(tracker.ShowTime, outJ, outV);
                    worst = Mathf.Max(worst, Vector2.Distance(outJ[(int)Joint.RightWrist], still) / Torso);
                }
                Assert.Less(worst, 0.03f, $"a {length}-frame glitch moves the wrist (torso lengths)");
            }
        }

        [Test]
        public void SwappedSides_ArePutBack_ARealTurnIsNot()
        {
            // the pose model swaps left and right for 8 frames while the person faces the camera (it does when someone
            // walks up close): the joints come back on their sides, the turn stays facing the camera
            var tracker = new OutfitTracker { Lookahead = 2 };
            var j = new Vector3[17];
            var v = new float[17];
            var w = new Vector3[17];
            var outJ = new Vector2[17];
            var outV = new float[17];
            for (int f = 0; f < 40; f++)
            {
                Body(j, v, w, 0.6f, 0.7f);
                if (f >= 15 && f < 23)
                    foreach (var (a, b) in new[] { (1, 2), (3, 4), (5, 6), (7, 8), (9, 10), (11, 12), (13, 14), (15, 16) })
                    {
                        (j[a], j[b]) = (j[b], j[a]);
                        (w[a], w[b]) = (w[b], w[a]);
                    }
                tracker.Add(f * Frame, j, v, w);
                if (f < 6) continue;
                double t = tracker.ShowTime;
                tracker.At(t, outJ, outV);
                Assert.Less(outJ[(int)Joint.LeftShoulder].x, outJ[(int)Joint.RightShoulder].x, $"frame {f}: the left shoulder stays left");
                Assert.IsTrue(tracker.WorldYawAt(t, out float yaw));
                Assert.AreEqual(0f, yaw * Mathf.Rad2Deg, 5f, $"frame {f}: still facing the camera");
            }
            Assert.AreEqual(2, tracker.Swaps > 0 ? 2 : 0, "swaps were seen");

            // a real half turn in a second (the shoulders come together side on, then cross): no swap, the turn follows
            var turning = new OutfitTracker { Lookahead = 2 };
            float yawEnd = 0f;
            for (int f = 0; f <= 45; f++)
            {
                float deg = Mathf.Clamp01((f - 5) / 30f) * 180f;
                Body(j, v, w, 0.6f, 0.7f, default, deg);
                // the picture: the shoulders' width shrinks with the turn and crosses past side on
                float c = Mathf.Cos(deg * Mathf.Deg2Rad);
                j[(int)Joint.LeftShoulder].x = 0.6f - 0.4f * Torso * c;
                j[(int)Joint.RightShoulder].x = 0.6f + 0.4f * Torso * c;
                j[(int)Joint.LeftHip].x = 0.6f - 0.24f * Torso * c;
                j[(int)Joint.RightHip].x = 0.6f + 0.24f * Torso * c;
                turning.Add(f * Frame, j, v, w);
                if (turning.WorldYawAt(turning.ShowTime, out float y)) yawEnd = y;
            }
            Assert.AreEqual(0, turning.Swaps, "a real turn is not a swap");
            Assert.AreEqual(180f, Mathf.Abs(yawEnd * Mathf.Rad2Deg), 8f, "the back faces the camera");
        }

        [Test]
        public void FacingTheCamera_SmallTurnsReadAsNone_RealOnesKeepTheirSize()
        {
            float dead = 15f * Mathf.Deg2Rad;
            Assert.AreEqual(0f, OutfitPose.Facing(5f * Mathf.Deg2Rad, dead), 1e-4f, "5 degrees: facing");
            Assert.AreEqual(0f, OutfitPose.Facing(-12f * Mathf.Deg2Rad, dead), 1e-4f, "-12 degrees: facing");
            Assert.AreEqual(40f, OutfitPose.Facing(40f * Mathf.Deg2Rad, dead) * Mathf.Rad2Deg, 0.5f, "40 degrees: a turn");
            Assert.AreEqual(-90f, OutfitPose.Facing(-90f * Mathf.Deg2Rad, dead) * Mathf.Rad2Deg, 0.5f, "side on");
            Assert.AreEqual(180f, Mathf.Abs(OutfitPose.Facing(Mathf.PI, dead) * Mathf.Rad2Deg), 0.5f, "the back");
            // never jumps: small steps in, small steps out
            float prev = 0f;
            for (float deg = 0f; deg <= 60f; deg += 0.5f)
            {
                float y = OutfitPose.Facing(deg * Mathf.Deg2Rad, dead) * Mathf.Rad2Deg;
                Assert.Less(y - prev, 1.6f, $"smooth at {deg}");
                Assert.GreaterOrEqual(y, prev - 1e-3f, $"never backwards at {deg}");
                prev = y;
            }
        }
    }
}
