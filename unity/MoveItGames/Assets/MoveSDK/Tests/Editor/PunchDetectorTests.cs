using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Random = System.Random;

namespace MoveIt.Motion.Tests
{
    /// <summary>
    /// <see cref="PunchDetector"/> against simulated players: a 3D body (two-bone arms with fixed bone lengths, an elbow
    /// that bends down and out), seen by a perspective camera at 2.2-2.5 m, with camera-like noise (0.003 of the picture,
    /// world joints 1.5 cm, depth 3 cm), uneven 30 fps timing, and the same One Euro filters as <see cref="BodyTracker"/>.
    /// Punches (jabs straight at the TV, slow child jabs, hooks, uppercuts, punches from a hanging arm, straight-arm swings,
    /// combos, punching while steering) must each be seen once, as the fist arrives; everything else a flying player does
    /// (steering, ducking, jumping, swaying, arms out like wings, dropping or raising the arms, scratching the head) must
    /// give none. Adult and child, 12 noise seeds each. <see cref="Report"/> prints the numbers; <see cref="Sweep"/> is how
    /// the speed settings were chosen (a 0.32 s lazy jab must count, a 0.9 s reach toward the TV mostly not: physically
    /// they differ only in speed). Without world joints (an old camera service) only sideways punches can be seen.
    /// </summary>
    public sealed class PunchDetectorTests
    {
        const int Seeds = 12;

        // ---------------------------------------------------------------- the tests

        [Test]
        public void Punches_WithWorldJoints_AreEachSeenOnce_AsTheFistArrives()
        {
            foreach (var clip in Clips.Where(c => c.Punches.Length > 0))
            {
                var r = Run(clip, world: true);
                Assert.AreEqual(1.0, r.HitRate, 1e-9, $"{clip.Name}: {r}");
                Assert.AreEqual(0, r.Extra, $"{clip.Name}: {r}");
                Assert.LessOrEqual(r.WorstLag, 0.15, $"{clip.Name}: {r}");
            }
        }

        [Test]
        public void EverythingElseAFlyingPlayerDoes_IsNotAPunch()
        {
            foreach (var clip in Clips.Where(c => c.Punches.Length == 0))
                Assert.LessOrEqual(Run(clip, world: true).Extra, clip.AllowedFalse, clip.Name);
        }

        [Test]
        public void WithoutWorldJoints_SidewaysPunchesCount_AndMovingTheBodyNeverDoes()
        {
            foreach (var clip in Clips.Where(c => c.Sideways))
            {
                var r = Run(clip, world: false);
                Assert.AreEqual(1.0, r.HitRate, 1e-9, $"{clip.Name} (picture only): {r}");
                Assert.AreEqual(0, r.Extra, $"{clip.Name} (picture only): {r}");
            }
            foreach (var clip in Clips.Where(c => c.Punches.Length == 0 && c.Body != null))
                Assert.AreEqual(0, Run(clip, world: false).Extra, clip.Name + " (picture only)");
        }

        [Test]
        public void TheHandIsReported()
        {
            var clip = Clips.First(c => c.Name.StartsWith("combo"));
            var hands = new List<bool>();
            Simulate(clip, Adult, new Random(1), world: true, (t, info) => hands.Add(info.RightHand));
            CollectionAssert.AreEqual(new[] { true, false, true }, hands, "right, left, right");
        }

        [Test, Explicit("diagnostics: the speed settings")]
        public void Sweep()
        {
            var lazy = Clips.First(c => c.Name.StartsWith("lazy"));
            var point = Clips.First(c => c.Name.StartsWith("reaching"));
            var hang = Clips.First(c => c.Name.StartsWith("punch from a hanging"));
            foreach (float window in new[] { 0.08f, 0.1f, 0.12f, 0.15f })
                foreach (float min in new[] { 2.2f, 2.4f, 2.6f, 2.8f, 3f })
                {
                    void Setup(PunchDetector d) { d.SpeedWindow = window; d.MinOutSpeed = min; }
                    TestContext.WriteLine($"window {window:0.00} min {min:0.0}: lazy {Run(lazy, true, Setup)} | point {Run(point, true, Setup)} | hang {Run(hang, true, Setup)}");
                }
        }

        [Test, Explicit("diagnostics: when false punches fire")]
        public void Diagnose()
        {
            foreach (var clip in Clips.Where(c => c.Punches.Length == 0 || c.Name.StartsWith("punch from")))
                foreach (var player in new[] { Adult, Child })
                    for (int seed = 0; seed < Seeds; seed++)
                        Simulate(clip, player, new Random(seed * 7919 + 13), true, (t, info) =>
                            TestContext.WriteLine($"{clip.Name} {(player == Adult ? "adult" : "child")} seed {seed}: t={t:0.000} swing={info.Swing} speed={info.Speed:0.0}"));
        }

        [Test]
        public void Report()
        {
            TestContext.WriteLine($"{"clip",-44} {"world joints",-46} picture only");
            foreach (var clip in Clips)
                TestContext.WriteLine($"{clip.Name,-44} {Run(clip, true),-46} {Run(clip, false)}");
        }

        // ---------------------------------------------------------------- results

        sealed class Result
        {
            public int Expected, Found, Extra, Swings;
            /// <summary>Seconds from the fist arriving (the end of the punch motion) to the punch being seen.</summary>
            public readonly List<double> Lags = new List<double>();
            public double HitRate => Expected == 0 ? 1.0 : (double)Found / Expected;
            public double WorstLag => Lags.Count > 0 ? Lags.Max() : 0.0;

            public override string ToString() => Expected == 0
                ? $"false punches {Extra}" + (Swings > 0 ? $" ({Swings} swings)" : "")
                : $"{Found}/{Expected} seen, +{Extra} extra, after arrival {Mean(Lags) * 1000:0}/{WorstLag * 1000:0} ms" +
                  (Swings > 0 ? $" ({Swings} swings)" : "");

            static double Mean(List<double> v) => v.Count > 0 ? v.Average() : 0.0;
        }

        /// <summary>Every seed for the adult and the child. A punch counts if seen between its start and 0.3 s after its end.</summary>
        static Result Run(Clip clip, bool world, Action<PunchDetector> setup = null)
        {
            var result = new Result();
            foreach (var player in new[] { Adult, Child })
            {
                for (int seed = 0; seed < Seeds; seed++)
                {
                    var found = new List<double>();
                    Simulate(clip, player, new Random(seed * 7919 + 13), world, (t, info) =>
                    {
                        found.Add(t);
                        if (info.Swing) result.Swings++;
                    }, setup);
                    result.Expected += clip.Punches.Length;
                    foreach (var p in clip.Punches)
                    {
                        int i = found.FindIndex(t => t >= p.Start - 0.02 && t <= p.End + 0.3);
                        if (i < 0) continue;
                        result.Found++;
                        result.Lags.Add(found[i] - p.End);
                        found.RemoveAt(i);
                    }
                    result.Extra += found.Count;
                }
            }
            return result;
        }

        // ---------------------------------------------------------------- the simulated player and camera

        sealed class Player
        {
            public float Scale;          // 1 = a 1.70 m adult
            public float Distance;       // metres from the camera
        }

        static readonly Player Adult = new Player { Scale = 1f, Distance = 2.5f };
        static readonly Player Child = new Player { Scale = 0.72f, Distance = 2.2f };

        const float CameraHeight = 0.9f, FovY = 50f, Aspect = 16f / 9f;
        const float UpperArm = 0.30f, Forearm = 0.28f;

        // Standing joints, metres, as the game sees them (mirrored): x = the screen's right = the player's right,
        // y up, z negative = toward the camera.
        static readonly Vector3 ShoulderR = new Vector3(0.18f, 1.42f, 0f), ShoulderL = new Vector3(-0.18f, 1.42f, 0f);
        static readonly Vector3 HipR = new Vector3(0.11f, 0.95f, 0f), HipL = new Vector3(-0.11f, 0.95f, 0f);

        /// <summary>Runs one clip through the camera, the filters and the detector; calls found(time, info) per punch.</summary>
        static void Simulate(Clip clip, Player player, Random rng, bool world, Action<double, PunchInfo> found,
                             Action<PunchDetector> setup = null)
        {
            var detector = new PunchDetector();
            setup?.Invoke(detector);
            var joints = new[] { "sl", "el", "wl", "sr", "er", "wr", "hl", "hr" };
            var view = joints.ToDictionary(j => j, j => new OneEuroFilter3(1f, 4f, 1f));          // BodyTracker's fast track
            var worldF = joints.ToDictionary(j => j, j => new OneEuroFilter3(1.5f, 1f));           // and its world track
            double last = -1;
            float tan = Mathf.Tan(FovY * 0.5f * Mathf.Deg2Rad);
            for (int frame = 0; ; frame++)
            {
                double t = frame / 30.0 + (rng.NextDouble() - 0.5) * 0.008;
                if (t > clip.Duration) break;
                float dt = last < 0 ? 0f : (float)(t - last);
                last = t;
                var pose = Pose(clip, (float)t, player.Scale);
                var hips = (pose["hl"] + pose["hr"]) * 0.5f;
                float torsoView = 0f, torsoWorld = 0.47f * player.Scale;
                var v = new Dictionary<string, Vector3>();
                var w = new Dictionary<string, Vector3>();
                foreach (var j in joints)
                {
                    var p = pose[j];
                    float depth = player.Distance + p.z;
                    var picture = new Vector3(Aspect * 0.5f + p.x / (2f * depth * tan), 0.5f + (p.y - CameraHeight) / (2f * depth * tan), 0f);
                    picture.x += Gauss(rng, 0.003f * Aspect);
                    picture.y += Gauss(rng, 0.003f);
                    var rel = p - hips + new Vector3(Gauss(rng, 0.015f), Gauss(rng, 0.015f), Gauss(rng, 0.03f));
                    float scaleView = 0.47f * player.Scale / (2f * player.Distance * tan);
                    v[j] = view[j].Filter(picture, dt, scaleView);
                    w[j] = worldF[j].Filter(rel, dt, torsoWorld);
                    torsoView = scaleView;
                }
                ArmSample Arm(string side) => new ArmSample
                {
                    Shoulder = v["s" + side], Elbow = v["e" + side], Wrist = v["w" + side],
                    WorldShoulder = w["s" + side], WorldElbow = w["e" + side], WorldWrist = w["w" + side],
                    Visibility = 0.95f,
                };
                if (detector.Update(t, Arm("l"), Arm("r"), world ? torsoWorld : torsoView, world, out var info)) found(t, info);
            }
        }

        /// <summary>All joints at time t (metres): the body offset plus both arms, the elbows found by two-bone IK.</summary>
        static Dictionary<string, Vector3> Pose(Clip clip, float t, float scale)
        {
            var body = clip.Body != null ? clip.Body(t) : Vector3.zero;
            var pose = new Dictionary<string, Vector3> { ["hl"] = (HipL + body) * scale, ["hr"] = (HipR + body) * scale };
            foreach (var (side, shoulder, arm) in new[] { ("l", ShoulderL, clip.Left), ("r", ShoulderR, clip.Right) })
            {
                var s = (shoulder + body) * scale;
                float sign = side == "r" ? 1f : -1f;
                // the arm's wrist target, relative to the shoulder (metres for an adult), mirrored for the left arm
                var rel = (arm ?? Arms.Hang)(t);
                rel.x *= sign;
                var wrist = s + rel * scale;
                pose["s" + side] = s;
                pose["w" + side] = wrist;
                pose["e" + side] = Elbow(s, wrist, UpperArm * scale, Forearm * scale, new Vector3(0.45f * sign, -1f, 0.35f));
            }
            return pose;
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

        // ---------------------------------------------------------------- motions

        struct Window
        {
            public double Start, End;
            public Window(double start, double end) { Start = start; End = end; }
        }

        sealed class Clip
        {
            public string Name;
            public double Duration;
            /// <summary>Wrist targets relative to each shoulder over time (null = hanging); the left one is mirrored.</summary>
            public Func<float, Vector3> Left, Right;
            /// <summary>Whole-body offset over time (x sideways, y up, metres for an adult).</summary>
            public Func<float, Vector3> Body;
            public Window[] Punches = new Window[0];
            /// <summary>Punches that move sideways in the picture (seen even without world joints).</summary>
            public bool Sideways;
            /// <summary>False punches tolerated over all seeds (only for the borderline slow reach).</summary>
            public int AllowedFalse;
        }

        static float MinJerk(float u)
        {
            u = Mathf.Clamp01(u);
            return u * u * u * (10f + u * (-15f + 6f * u));
        }

        /// <summary>Keyframes (time, wrist relative to the shoulder), min-jerk in between.</summary>
        static Func<float, Vector3> Keys(params (float t, Vector3 p)[] keys) => t =>
        {
            if (t <= keys[0].t) return keys[0].p;
            for (int i = 1; i < keys.Length; i++)
                if (t <= keys[i].t)
                    return Vector3.Lerp(keys[i - 1].p, keys[i].p, MinJerk((t - keys[i - 1].t) / (keys[i].t - keys[i - 1].t)));
            return keys[keys.Length - 1].p;
        };

        /// <summary>Keyframes like <see cref="Keys"/>, but the wrist swings around the shoulder (a straight arm moving).</summary>
        static Func<float, Vector3> Arc(params (float t, Vector3 p)[] keys) => t =>
        {
            if (t <= keys[0].t) return keys[0].p;
            for (int i = 1; i < keys.Length; i++)
                if (t <= keys[i].t)
                    return Slerp(keys[i - 1].p, keys[i].p, MinJerk((t - keys[i - 1].t) / (keys[i].t - keys[i - 1].t)));
            return keys[keys.Length - 1].p;
        };

        static Vector3 Slerp(Vector3 a, Vector3 b, float t)
        {
            float la = a.magnitude, lb = b.magnitude;
            Vector3 ua = a / la, ub = b / lb;
            float dot = Mathf.Clamp(Vector3.Dot(ua, ub), -1f, 1f);
            var across = ub - ua * dot;
            if (across.sqrMagnitude < 1e-8f) return Vector3.Lerp(a, b, t);
            float angle = Mathf.Acos(dot) * t;
            return (ua * Mathf.Cos(angle) + across.normalized * Mathf.Sin(angle)) * Mathf.Lerp(la, lb, t);
        }

        static class Arms
        {
            const float Long = (UpperArm + Forearm) * 0.97f;
            // right-arm poses relative to the right shoulder (x = outward for that arm after mirroring)
            public static readonly Vector3 Guard = new Vector3(-0.05f, -0.06f, -0.25f);       // fist near the chin
            public static readonly Vector3 LowGuard = new Vector3(-0.04f, -0.3f, -0.12f);     // fist at the belly, elbow bent
            public static readonly Vector3 Chamber = new Vector3(0.02f, -0.22f, -0.12f);      // elbow bent at the side
            public static readonly Vector3 HangPose = new Vector3(0.04f, -0.56f, 0.02f);
            public static Vector3 Out(float x, float y, float z) => new Vector3(x, y, z).normalized * Long;
            public static readonly Vector3 Forward = Out(-0.08f, 0.05f, -1f);
            public static Func<float, Vector3> Hang => _ => HangPose;
            public static Func<float, Vector3> Hold(Vector3 p) => _ => p;

            /// <summary>From a pose, a punch to `to` taking `ext` seconds at `at`, held, pulled back in 0.3 s.</summary>
            public static Func<float, Vector3> Punch(Vector3 from, Vector3 to, float at, float ext) =>
                Keys((0f, from), (at, from), (at + ext, to), (at + ext + 0.12f, to), (at + ext + 0.42f, from));
        }

        static readonly Func<float, Vector3> Sidesteps = t =>
            new Vector3(0.4f * Mathf.Sin(t * Mathf.PI * 2f / 1.6f), 0f, 0f);   // 0.4 m each way, about 0.4 s per step

        static readonly Clip[] Clips =
        {
            new Clip { Name = "jab at the TV, right (0.15 s)", Duration = 2.5, Left = Arms.Hold(Arms.Guard),
                       Right = Arms.Punch(Arms.Guard, Arms.Forward, 1.2f, 0.15f), Punches = new[] { new Window(1.2, 1.35) } },
            new Clip { Name = "jab at the TV, left (child speed 0.22 s)", Duration = 2.5, Right = Arms.Hold(Arms.Guard),
                       Left = Arms.Punch(Arms.Guard, Arms.Forward, 1.2f, 0.22f), Punches = new[] { new Window(1.2, 1.42) } },
            new Clip { Name = "lazy jab (0.32 s)", Duration = 2.5, Left = Arms.Hold(Arms.Guard),
                       Right = Arms.Punch(Arms.Guard, Arms.Forward, 1.2f, 0.32f), Punches = new[] { new Window(1.2, 1.52) } },
            new Clip { Name = "jab up and across", Duration = 2.5, Left = Arms.Hold(Arms.Guard),
                       Right = Arms.Punch(Arms.Guard, Arms.Out(-0.35f, 0.35f, -1f), 1.2f, 0.17f), Punches = new[] { new Window(1.2, 1.37) } },
            new Clip { Name = "side punch / hook", Duration = 2.5, Left = Arms.Hold(Arms.Guard), Sideways = true,
                       Right = Arms.Punch(Arms.Guard, Arms.Out(1f, 0.1f, -0.45f), 1.2f, 0.18f), Punches = new[] { new Window(1.2, 1.38) } },
            new Clip { Name = "uppercut", Duration = 2.5, Left = Arms.Hold(Arms.Guard), Sideways = true,
                       Right = Arms.Punch(Arms.LowGuard, Arms.Out(-0.05f, 1f, -0.45f), 1.2f, 0.2f), Punches = new[] { new Window(1.2, 1.4) } },
            new Clip { Name = "punch from a hanging arm", Duration = 2.8, Left = Arms.Hang,
                       Right = Keys((0f, Arms.HangPose), (1.2f, Arms.HangPose), (1.38f, Arms.Chamber), (1.58f, Arms.Forward),
                                    (1.7f, Arms.Forward), (2.1f, Arms.HangPose)),
                       Punches = new[] { new Window(1.2, 1.58) } },
            new Clip { Name = "straight arm swung at the TV", Duration = 2.6, Left = Arms.Hang,
                       Right = Arc((0f, Arms.HangPose), (1.2f, Arms.HangPose), (1.45f, Arms.Out(-0.05f, 0.1f, -1f)),
                                   (1.6f, Arms.Out(-0.05f, 0.1f, -1f)), (2.1f, Arms.HangPose)),
                       Punches = new[] { new Window(1.2, 1.45) } },
            new Clip { Name = "combo right-left-right", Duration = 3.2,
                       Right = Keys((0f, Arms.Guard), (1.2f, Arms.Guard), (1.35f, Arms.Forward), (1.45f, Arms.Forward), (1.7f, Arms.Guard),
                                    (2.1f, Arms.Guard), (2.25f, Arms.Forward), (2.35f, Arms.Forward), (2.6f, Arms.Guard)),
                       Left = Keys((0f, Arms.Guard), (1.65f, Arms.Guard), (1.8f, Arms.Forward), (1.9f, Arms.Forward), (2.15f, Arms.Guard)),
                       Punches = new[] { new Window(1.2, 1.35), new Window(1.65, 1.8), new Window(2.1, 2.25) } },
            new Clip { Name = "jab while stepping sideways", Duration = 2.6, Left = Arms.Hold(Arms.Guard), Body = Sidesteps,
                       Right = Arms.Punch(Arms.Guard, Arms.Forward, 1.25f, 0.17f), Punches = new[] { new Window(1.25, 1.42) } },

            new Clip { Name = "idle, swaying", Duration = 6, Body = t => new Vector3(0.05f * Mathf.Sin(t * 3f), 0.01f * Mathf.Sin(t * 5f), 0f) },
            new Clip { Name = "stepping side to side, arms down", Duration = 6, Body = Sidesteps },
            new Clip { Name = "stepping side to side, fists up", Duration = 6, Body = Sidesteps,
                       Left = Arms.Hold(Arms.Guard), Right = Arms.Hold(Arms.Guard) },
            new Clip { Name = "arms out like wings, steering and ducking", Duration = 6,
                       Body = t => new Vector3(0.4f * Mathf.Sin(t * 2.5f), -0.25f * Mathf.Max(0f, Mathf.Sin(t * 1.7f)), 0f),
                       Left = Arms.Hold(Arms.Out(1f, 0.05f, -0.1f)), Right = Arms.Hold(Arms.Out(1f, 0.05f, -0.1f)) },
            new Clip { Name = "quick ducks, fists up", Duration = 5, Left = Arms.Hold(Arms.Guard), Right = Arms.Hold(Arms.Guard),
                       Body = t => new Vector3(0f, -0.35f * Mathf.Max(0f, Mathf.Sin(t * 4f)), 0f) },
            new Clip { Name = "jumping, fists up", Duration = 5, Left = Arms.Hold(Arms.Guard), Right = Arms.Hold(Arms.Guard),
                       Body = t => new Vector3(0f, 0.3f * Mathf.Max(0f, Mathf.Sin(t * 5f)), 0f) },
            new Clip { Name = "dropping the fists to the sides (fast, slow)", Duration = 5,
                       Right = Keys((0f, Arms.Guard), (1.2f, Arms.Guard), (1.5f, Arms.HangPose), (2.5f, Arms.HangPose), (2.8f, Arms.Guard),
                                    (3.4f, Arms.Guard), (4.0f, Arms.HangPose)),
                       Left = Keys((0f, Arms.Guard), (1.2f, Arms.Guard), (1.5f, Arms.HangPose), (2.5f, Arms.HangPose), (2.8f, Arms.Guard),
                                   (3.4f, Arms.Guard), (4.0f, Arms.HangPose)) },
            new Clip { Name = "reaching toward the TV (0.9 s)", Duration = 4, Left = Arms.Hang, AllowedFalse = 3,
                       Right = Keys((0f, Arms.HangPose), (1f, Arms.HangPose), (1.4f, Arms.Chamber), (2.3f, Arms.Forward), (3.2f, Arms.HangPose)) },
            new Clip { Name = "raising both arms slowly and back", Duration = 5,
                       Left = Arc((0f, Arms.HangPose), (1f, Arms.HangPose), (2.1f, Arms.Out(0.15f, 1f, -0.1f)), (3f, Arms.Out(0.15f, 1f, -0.1f)), (4.2f, Arms.HangPose)),
                       Right = Arc((0f, Arms.HangPose), (1f, Arms.HangPose), (2.1f, Arms.Out(0.15f, 1f, -0.1f)), (3f, Arms.Out(0.15f, 1f, -0.1f)), (4.2f, Arms.HangPose)) },
            new Clip { Name = "raising both arms in front, quickly", Duration = 4,
                       Left = Arc((0f, Arms.HangPose), (1f, Arms.HangPose), (1.6f, Arms.Out(0.05f, 1f, -0.25f)), (2.2f, Arms.Out(0.05f, 1f, -0.25f)), (3f, Arms.HangPose)),
                       Right = Arc((0f, Arms.HangPose), (1f, Arms.HangPose), (1.6f, Arms.Out(0.05f, 1f, -0.25f)), (2.2f, Arms.Out(0.05f, 1f, -0.25f)), (3f, Arms.HangPose)) },
            new Clip { Name = "bringing the fists up, quickly", Duration = 4,
                       Left = Keys((0f, Arms.HangPose), (1.2f, Arms.HangPose), (1.45f, Arms.Guard), (2.5f, Arms.Guard), (2.75f, Arms.HangPose)),
                       Right = Keys((0f, Arms.HangPose), (1.2f, Arms.HangPose), (1.45f, Arms.Guard), (2.5f, Arms.Guard), (2.75f, Arms.HangPose)) },
            new Clip { Name = "scratching the head", Duration = 4, Left = Arms.Hang,
                       Right = Keys((0f, Arms.HangPose), (1f, Arms.HangPose), (1.8f, new Vector3(-0.12f, 0.3f, -0.05f)),
                                    (2.6f, new Vector3(-0.12f, 0.3f, -0.05f)), (3.4f, Arms.HangPose)) },
        };
    }
}
