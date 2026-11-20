using System;
using System.Collections.Generic;
using System.Linq;
using MoveIt.Motion;
using NUnit.Framework;
using UnityEngine;
using Joint = MoveIt.Motion.Joint;
using Random = System.Random;

namespace MoveIt.Games.Boxing.Tests
{
    /// <summary>
    /// <see cref="BoutMoves"/> on simulated boxers: a 3D body (two-bone arms with fixed bone lengths) seen by a camera on
    /// the TV at 2.5 m, camera-like noise, 30 samples a second, world joints like the box's (depth squeezed), and the
    /// box's glitches (a wrist pulled onto its elbow or flipped away for a frame or two).
    /// </summary>
    public sealed class BoutMovesTests
    {
        // ---------------------------------------------------------------- the tests

        [Test]
        public void TheGuard_IsSeen_AndIsNoPunch()
        {
            var r = Watch(Guard(), 3f, new Random(1));
            Assert.Greater(r.GuardShare(1f, 3f), 0.95, "gloves up: the guard");
            Assert.AreEqual(0, r.Punches.Count, "holding the guard throws nothing");
            var hanging = Watch(t => Body.Stand(), 3f, new Random(2));
            Assert.AreEqual(0.0, hanging.GuardShare(0.5f, 3f), "arms hanging: no guard");
        }

        [Test]
        public void PunchesFromTheGuard_AreSeenOnce_EachItsKind()
        {
            int stray = 0;
            foreach (var (name, punch, blow, body) in new (string, Func<float, Body>, Blow, bool)[]
                     {
                         ("jab at the TV", Jab(1f, right: true), Blow.Straight, false),
                         ("left jab", Jab(1f, right: false), Blow.Straight, false),
                         ("left hook", Hook(1f, right: false), Blow.Hook, false),
                         ("right hook", Hook(1f, right: true), Blow.Hook, false),
                         ("uppercut", Uppercut(1f, right: true), Blow.Uppercut, false),
                         ("body punch", BodyPunch(1f, right: false), Blow.Straight, true),
                     })
            {
                for (int seed = 0; seed < 6; seed++)
                {
                    var r = Watch(punch, 2.2f, new Random(seed + 10));
                    // the punch (it starts at 1.0); a slow move getting a hook or an uppercut ready may now and then be taken
                    // for one too (forgiving: any quick move of a fist), counted apart
                    var during = r.Punches.Where(p => p.Time > 0.95 && p.Time < 1.4).ToList();
                    stray += r.Punches.Count - during.Count;
                    string all = string.Join(", ", r.Punches.Select(p => $"{p.Blow} {(p.Right ? "R" : "L")} {p.Time:0.00} {p.Rule}"));
                    Assert.AreEqual(1, during.Count, $"{name} (seed {seed}): {all}");
                    if (r.Punches.Count > during.Count) continue;   // a stray just before can make it read as the way back
                    var p = during[0];
                    Assert.AreEqual(blow, p.Blow, $"{name} (seed {seed}): {all}");
                    Assert.AreEqual(body, p.Body, $"{name} at the body (seed {seed})");
                    Assert.AreEqual(name.Contains("right") || name == "jab at the TV" || name == "uppercut", p.Right, $"{name}: the hand");
                }
            }
            Assert.LessOrEqual(stray, 2, "punches seen while a fist slowly got ready (36 runs)");
        }

        [Test]
        public void LeaningAndDucking_AreSeen_AndNoPunch()
        {
            foreach (int side in new[] { -1, 1 })
            {
                var r = Watch(t => Lean(Guard()(t), side * 18f * Ease(t, 1f, 0.25f) * (1f - Ease(t, 2.2f, 0.3f))), 3.5f, new Random(5 + side));
                Assert.Greater(r.LeanShare(1.4f, 2.1f, side), 0.95, $"leaning {side}");
                Assert.AreEqual(0.0, r.LeanShare(0.2f, 0.9f, side), "standing: no lean");
                Assert.Less(r.LeanShare(2.9f, 3.5f, side), 0.05, "back up: no lean");
                Assert.AreEqual(0, r.Punches.Count, $"leaning {side}: " + string.Join(", ", r.Punches.Select(p => $"{p.Blow} {(p.Right ? "R" : "L")} {p.Time:0.00} {p.Speed:0.0} {p.Rule} dir ({p.Direction.x:0.0},{p.Direction.y:0.0}) ext {p.Extension:0.0}")));
                Assert.Greater(r.GuardShare(1.4f, 2.1f), 0.9, "guarding while leaning");
                // where the head is, in the fight's metres: aside while leaning (the hips hardly), back in the middle after
                float head = r.Mean(1.4f, 2.1f, p => p.HeadX), hips = r.Mean(1.4f, 2.1f, p => p.BodyX);
                Assert.Greater(head * side, 0.1f, $"the head aside ({head:0.000} m)");
                Assert.Less(Mathf.Abs(hips), Mathf.Abs(head) * 0.6f, $"the hips stay ({hips:0.000} m)");
                Assert.Less(Mathf.Abs(r.Mean(3.2f, 3.5f, p => p.HeadX)), 0.05f, "back in the middle");
            }
            var d = Watch(t => Duck(Guard()(t), 0.32f * Ease(t, 1f, 0.3f) * (1f - Ease(t, 2.2f, 0.3f))), 3.5f, new Random(9));
            Assert.Greater(d.DuckShare(1.5f, 2.1f), 0.95, "ducking");
            Assert.Greater(d.Mean(1.5f, 2.1f, p => p.HeadDrop), 0.1f, "the head down");
            Assert.Less(d.Mean(0.2f, 0.9f, p => p.HeadDrop), 0.02f);
            Assert.AreEqual(0.0, d.DuckShare(0.2f, 0.9f));
            Assert.Less(d.DuckShare(3f, 3.5f), 0.05);
            Assert.AreEqual(0, d.Punches.Count, "ducking: " + string.Join(", ", d.Punches.Select(p => $"{p.Blow} {(p.Right ? "R" : "L")} {p.Time:0.00} {p.Speed:0.0} {p.Rule} dir ({p.Direction.x:0.0},{p.Direction.y:0.0}) ext {p.Extension:0.0}")));
        }

        [Test]
        public void StepingToANewPlace_IsALeanOnlyForAWhile()
        {
            var r = Watch(t => Step(Guard()(t), 0.5f * Ease(t, 1f, 0.4f)), 8f, new Random(4));
            Assert.Greater(r.LeanShare(1.5f, 2f, 1), 0.9, "a step aside reads as a lean (a dodge)...");
            Assert.Greater(r.Mean(1.5f, 2f, p => p.BodyX), 0.1f, "a step takes the hips along");
            Assert.Less(r.LeanShare(6f, 8f, 1), 0.05, "...until where the player stands has caught up");
        }

        [Test]
        public void BothArmsUp_IsTheSuper_NotPunches()
        {
            var r = Watch(t => ArmsUp(Body.Stand(), Ease(t, 1f, 0.35f) * (1f - Ease(t, 2.5f, 0.4f))), 3.5f, new Random(3));
            Assert.Greater(r.UpShare(1.6f, 2.4f), 0.9);
            Assert.AreEqual(0.0, r.UpShare(0.2f, 0.9f));
            Assert.LessOrEqual(r.Punches.Count, 0, "raising both arms together is no punch: " + string.Join(", ", r.Punches.Select(p => $"{p.Blow} {(p.Right ? "R" : "L")} {p.Time:0.00} {p.Speed:0.0} {p.Rule} dir ({p.Direction.x:0.0},{p.Direction.y:0.0}) ext {p.Extension:0.0}")));
        }

        /// <summary>The box's pose model pulls a wrist held in front of the body onto its elbow, or flips it, for a frame or
        /// two (docs/hardware.md: up to ~0.5 a second per hand circling in front of the chest, far fewer held still): at two
        /// a second, a still guard throws at most one phantom punch in twenty glitches, and stays a guard.</summary>
        [Test]
        public void TheModelsGlitches_ThrowNoPunches()
        {
            int punches = 0;
            double guard = 0;
            for (int seed = 0; seed < 10; seed++)
            {
                var r = Watch(Guard(), 10f, new Random(seed + 40), glitchesPerSecond: 2f);
                punches += r.Punches.Count;
                guard += r.GuardShare(1f, 10f) / 10;
            }
            Assert.LessOrEqual(punches, 10, "glitches of a still guard thrown as punches (100 s, 200 glitches)");
            Assert.Greater(guard, 0.9, "the guard held through them");
        }

        // ---------------------------------------------------------------- the simulated boxer

        const float CameraHeight = 0.95f, Distance = 2.5f, FovY = 50f, Aspect = 16f / 9f;
        const float UpperArm = 0.30f, Forearm = 0.28f, TorsoLength = 0.47f;

        /// <summary>A body at one moment (metres, mirrored: x = the screen's right = the player's right, y up, z negative =
        /// toward the camera): where the shoulders, hips and nose are, and each wrist relative to its shoulder (x outward
        /// for each arm).</summary>
        struct Body
        {
            public Vector3 Offset;       // the whole body moved
            public float Tilt;           // the upper body leaned (degrees, + = to the right)
            public Vector3 LeftWrist, RightWrist;

            public static Body Stand() => new Body { LeftWrist = new Vector3(0.05f, -0.55f, 0f), RightWrist = new Vector3(0.05f, -0.55f, 0f) };
        }

        static readonly Vector3 HipL = new Vector3(-0.11f, 0.95f, 0f), HipR = new Vector3(0.11f, 0.95f, 0f);

        static Func<float, Body> Guard() => t => WithGuard(Body.Stand());

        static Body WithGuard(Body b)
        {
            // fists in front of the chin, a little inward
            b.LeftWrist = b.RightWrist = new Vector3(-0.1f, 0.2f, -0.22f);
            return b;
        }

        static float MinJerk(float u)
        {
            u = Mathf.Clamp01(u);
            return u * u * u * (10f + u * (-15f + 6f * u));
        }

        static float Ease(float t, float start, float length) => MinJerk((t - start) / length);

        /// <summary>A punch from the guard starting at <paramref name="start"/>: out to <paramref name="target"/> in
        /// <paramref name="outTime"/>, back in 0.25 s. <paramref name="from"/> = where it starts (default the guard).</summary>
        static Func<float, Body> Punch(float start, bool right, Vector3 target, float outTime, Vector3? from = null) => t =>
        {
            var b = WithGuard(Body.Stand());
            var g = from ?? b.RightWrist;
            float outU = Ease(t, start, outTime), back = Ease(t, start + outTime + 0.05f, 0.25f);
            var w = Vector3.Lerp(Vector3.Lerp(g, target, outU), b.RightWrist, back);
            if (right) b.RightWrist = w;
            else b.LeftWrist = w;
            return b;
        };

        static Func<float, Body> Jab(float start, bool right) => Punch(start, right, new Vector3(-0.04f, 0.16f, -0.56f), 0.17f);
        static Func<float, Body> BodyPunch(float start, bool right) => Punch(start, right, new Vector3(-0.05f, -0.32f, -0.5f), 0.2f, new Vector3(-0.05f, -0.12f, -0.25f));

        /// <summary>A hook: the fist, cocked a little out (slowly, beforehand), swings across to the other side at chin
        /// height.</summary>
        static Func<float, Body> Hook(float start, bool right) => Swing(start, right, new Vector3(0.22f, 0.15f, -0.24f), new Vector3(-0.3f, 0.14f, -0.25f), 0.2f);

        /// <summary>An uppercut: the fist, lowered to the belly (slowly, beforehand), drives up to the chin and above.</summary>
        static Func<float, Body> Uppercut(float start, bool right) => Swing(start, right, new Vector3(-0.06f, -0.3f, -0.2f), new Vector3(-0.08f, 0.32f, -0.3f), 0.2f);

        /// <summary>The fist goes slowly (0.9 s, too slow to be a punch) from the guard to <paramref name="from"/>, then
        /// quickly to <paramref name="to"/> from <paramref name="start"/>, then back to the guard.</summary>
        static Func<float, Body> Swing(float start, bool right, Vector3 from, Vector3 to, float time) => t =>
        {
            var b = WithGuard(Body.Stand());
            var g = b.RightWrist;
            Vector3 w;
            if (t < start) w = Vector3.Lerp(g, from, Ease(t, start - 0.95f, 0.9f));
            else w = Vector3.Lerp(Vector3.Lerp(from, to, Ease(t, start, time)), g, Ease(t, start + time + 0.08f, 0.35f));
            if (right) b.RightWrist = w;
            else b.LeftWrist = w;
            return b;
        };

        static Body Lean(Body b, float degrees)
        {
            b.Tilt = degrees;
            return b;
        }

        static Body Duck(Body b, float metres)
        {
            b.Offset.y -= metres;
            return b;
        }

        static Body Step(Body b, float metres)
        {
            b.Offset.x += metres;
            return b;
        }

        static Body ArmsUp(Body b, float amount)
        {
            var up = new Vector3(0.06f, 0.56f, -0.02f);
            b.LeftWrist = Vector3.Lerp(b.LeftWrist, up, amount);
            b.RightWrist = Vector3.Lerp(b.RightWrist, up, amount);
            return b;
        }

        sealed class Seen
        {
            public readonly List<BoutPunch> Punches = new List<BoutPunch>();
            public BoutMoves Moves;
            public readonly List<(double t, HeroPose pose)> Poses = new List<(double, HeroPose)>();

            double Share(float from, float to, Func<HeroPose, bool> f)
            {
                var span = Poses.Where(p => p.t >= from && p.t <= to).ToList();
                return span.Count == 0 ? 0 : span.Count(p => f(p.pose)) / (double)span.Count;
            }

            public double GuardShare(float from, float to) => Share(from, to, p => p.Guard);
            public double LeanShare(float from, float to, int side) => Share(from, to, p => p.Lean == side);
            public double DuckShare(float from, float to) => Share(from, to, p => p.Duck);
            /// <summary>The mean of a pose's number over a stretch (the head's place, the hips').</summary>
            public float Mean(float from, float to, Func<HeroPose, float> of)
            {
                var span = Poses.Where(p => p.t >= from && p.t <= to).ToList();
                return span.Count == 0 ? 0f : span.Average(p => of(p.pose));
            }
            public double UpShare(float from, float to) => Share(from, to, p => p.ArmsUp);
        }

        /// <summary>Watches a body move for <paramref name="seconds"/> through the camera and <see cref="BoutMoves"/>.</summary>
        static Seen Watch(Func<float, Body> motion, float seconds, Random rng, float glitchesPerSecond = 0f)
        {
            var moves = new BoutMoves();
            var seen = new Seen { Moves = moves };
            var raw = new Vector3[Skeleton.JointCount];
            var world = new Vector3[Skeleton.JointCount];
            var vis = new float[Skeleton.JointCount];
            float tan = Mathf.Tan(FovY * 0.5f * Mathf.Deg2Rad);
            int glitchLeft = 0;
            bool glitchRight = false, glitchFlip = false;
            Vector3 flip = Vector3.zero;
            for (int frame = 0; ; frame++)
            {
                double t = frame / 30.0 + (rng.NextDouble() - 0.5) * 0.008;
                if (t > seconds) break;
                var b = motion((float)t);
                var joints = Joints(b);
                if (glitchLeft <= 0 && glitchesPerSecond > 0f && rng.NextDouble() < glitchesPerSecond / 30.0)
                {
                    glitchLeft = 1 + rng.Next(2);
                    glitchRight = rng.NextDouble() < 0.5;
                    glitchFlip = rng.NextDouble() < 0.4;
                    flip = new Vector3((float)(rng.NextDouble() - 0.5) * 0.8f, (float)(rng.NextDouble() - 0.5) * 0.8f, 0f);
                }
                if (glitchLeft > 0)
                {
                    glitchLeft--;
                    var w = glitchRight ? Joint.RightWrist : Joint.LeftWrist;
                    var e = glitchRight ? Joint.RightElbow : Joint.LeftElbow;
                    joints[(int)w] = glitchFlip ? joints[(int)w] + flip : Vector3.Lerp(joints[(int)w], joints[(int)e], 0.85f);
                }
                var hips = (joints[(int)Joint.LeftHip] + joints[(int)Joint.RightHip]) * 0.5f;
                for (int i = 0; i < Skeleton.JointCount; i++)
                {
                    var p = joints[i];
                    float depth = Distance + p.z;
                    raw[i] = new Vector3(Aspect * 0.5f + p.x / (2f * depth * tan) + Gauss(rng, 0.003f), 0.5f + (p.y - CameraHeight) / (2f * depth * tan) + Gauss(rng, 0.003f), 0f);
                    var rel = p - hips;
                    rel.z *= 0.6f;   // the camera service squeezes depth
                    world[i] = rel + new Vector3(Gauss(rng, 0.015f), Gauss(rng, 0.015f), Gauss(rng, 0.03f));
                    vis[i] = 0.95f;
                }
                var punches = new List<BoutPunch>();
                moves.Update(t, raw, vis, world, true, punches);
                seen.Punches.AddRange(punches);
                if (!double.IsNegativeInfinity(moves.Settled)) seen.Poses.Add((moves.Settled, moves.Pose));
            }
            return seen;
        }

        /// <summary>All joints (metres) of a body: the upper body leaned around the hips, the elbows by two-bone IK.</summary>
        static Vector3[] Joints(Body b)
        {
            var j = new Vector3[Skeleton.JointCount];
            var hipMid = (HipL + HipR) * 0.5f + b.Offset;
            j[(int)Joint.LeftHip] = HipL + b.Offset;
            j[(int)Joint.RightHip] = HipR + b.Offset;
            float a = b.Tilt * Mathf.Deg2Rad;
            Vector3 Up(Vector3 rel) => hipMid + new Vector3(rel.x * Mathf.Cos(a) + rel.y * Mathf.Sin(a), -rel.x * Mathf.Sin(a) + rel.y * Mathf.Cos(a), rel.z);
            var sl = Up(new Vector3(-0.18f, TorsoLength, 0f));
            var sr = Up(new Vector3(0.18f, TorsoLength, 0f));
            j[(int)Joint.LeftShoulder] = sl;
            j[(int)Joint.RightShoulder] = sr;
            j[(int)Joint.Nose] = Up(new Vector3(0f, TorsoLength + 0.2f, -0.08f));
            j[(int)Joint.LeftEye] = j[(int)Joint.RightEye] = j[(int)Joint.Nose];
            j[(int)Joint.LeftEar] = j[(int)Joint.RightEar] = j[(int)Joint.Nose];
            foreach (var (side, s, rel) in new[] { (-1f, sl, b.LeftWrist), (1f, sr, b.RightWrist) })
            {
                var r = rel;
                r.x *= side;
                var wrist = s + r;
                var elbow = Elbow(s, wrist, UpperArm, Forearm, new Vector3(0.45f * side, -1f, 0.35f));
                j[(int)(side < 0 ? Joint.LeftWrist : Joint.RightWrist)] = wrist;
                j[(int)(side < 0 ? Joint.LeftElbow : Joint.RightElbow)] = elbow;
            }
            for (int i = (int)Joint.LeftKnee; i <= (int)Joint.RightAnkle; i++) j[i] = hipMid + new Vector3(0f, -0.5f, 0f);
            return j;
        }

        static Vector3 Elbow(Vector3 s, Vector3 w, float a, float b, Vector3 pole)
        {
            var d = w - s;
            float len = Mathf.Clamp(d.magnitude, 1e-4f, (a + b) * 0.998f);
            var u = d.normalized;
            float x = (a * a - b * b + len * len) / (2f * len);
            float h = Mathf.Sqrt(Mathf.Max(0f, a * a - x * x));
            var p = (pole - u * Vector3.Dot(pole, u)).normalized;
            return s + u * x + p * h;
        }

        static float Gauss(Random rng, float sigma)
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2)) * sigma;
        }
    }
}
