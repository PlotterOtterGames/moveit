// Vishu!'s clothes on a real recording, with the box's timing: how much each part of the outfit shakes and how far it
// is from the body in the picture it's drawn on, for the old pipeline (the picture as soon as it comes, the joints
// interpolated or carried on to its moment, One Euro filters) and the new one (OutfitTracker: the picture a few frames
// late, the joints fitted from both sides). Engine-free; run with the quick runner:
//
//   D=unity/MoveItGames/Assets/Games/Dress/Scripts; S=unity/MoveItGames/Assets/MoveSDK/Runtime
//   DRESS_BENCH=motion-service-py/sessions/clips/routine-20261101.full.jsonl tools/logic-tests/quick/run.sh Bench -- \
//     $D/OutfitRig.cs $D/OutfitTracker.cs $D/PoseTimeline.cs $D/DressCatalog.cs $D/DressLook.cs $S/Joint.cs \
//     $S/OneEuroFilter.cs tools/motion/dress_bench/DressBench.cs
//   (DRESS_BENCH_RUNS=old,new:2 picks the pipelines; new:K = K camera frames of waiting)
//
// The reference ("where the body is") is the recording smoothed offline both ways in time (a median of five, then a
// local quadratic over +-0.1 s): it can't be known exactly, so read the error numbers as relative, and the shake
// numbers (frame-to-frame acceleration of what's drawn, against the reference's own) as the main measure. Joints are
// only counted on frames where the camera AI saw them (visibility >= 0.6). Units: % of a torso length.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MoveIt.Games.Dress;
using MoveIt.Motion;
using NUnit.Framework;
using UnityEngine;

public class DressBench
{
    sealed class Sample
    {
        public Vector3[] RawView, RawWorld;
        public float[] RawVis;
        public double T;
        public bool Person;
        public float Aspect;
        public readonly Vector3[] View = new Vector3[17];
        public readonly float[] Vis = new float[17];
        public readonly Vector3[] World = new Vector3[17];
        public bool HasWorld;
    }

    static List<Sample> Load(string path)
    {
        var list = new List<Sample>();
        var num = @"-?[0-9.eE+-]+";
        foreach (var line in File.ReadLines(path))
        {
            if (line.Length < 10) continue;
            var s = new Sample();
            s.T = double.Parse(Regex.Match(line, "\"t\":(" + num + ")").Groups[1].Value, CultureInfo.InvariantCulture) / 1000.0;
            float w = float.Parse(Regex.Match(line, "\"w\":(" + num + ")").Groups[1].Value, CultureInfo.InvariantCulture);
            float h = float.Parse(Regex.Match(line, "\"h\":(" + num + ")").Groups[1].Value, CultureInfo.InvariantCulture);
            s.Aspect = w / h;
            var k = Regex.Match(line, "\"k\":\\[([^\\]]*)\\]");
            if (k.Success)
            {
                var v = k.Groups[1].Value.Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                for (int i = 0; i < 17; i++)
                {
                    s.View[i] = new Vector3((1f - v[i * 4]) * s.Aspect, 1f - v[i * 4 + 1], v[i * 4 + 2] * s.Aspect);
                    s.Vis[i] = v[i * 4 + 3];
                }
                s.Person = true;
                var wk = Regex.Match(line, "\"wk\":\\[([^\\]]*)\\]");
                if (wk.Success)
                {
                    var u = wk.Groups[1].Value.Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                    for (int i = 0; i < 17; i++) s.World[i] = new Vector3(-u[i * 3], -u[i * 3 + 1], u[i * 3 + 2]);
                    s.HasWorld = true;
                }
            }
            s.RawView = (Vector3[])s.View.Clone();
            s.RawVis = (float[])s.Vis.Clone();
            s.RawWorld = (Vector3[])s.World.Clone();
            list.Add(s);
        }
        // the reference must not hold the pose model's side swaps either (OutfitTracker.FixSwaps: same rule)
        float turn = float.NaN;
        int swaps = 0;
        foreach (var s in list)
        {
            if (!s.Person || !s.HasWorld) continue;
            float yaw = OutfitPose.WorldYaw(s.World[5], s.World[6], s.World[11], s.World[12]);
            if (float.IsNaN(turn)) { turn = yaw; continue; }
            float d = WrapA(yaw - turn);
            bool swapped = Mathf.Abs(d) > 1.75f;
            if (swapped)
            {
                d = WrapA(d + Mathf.PI);
                swaps++;
                foreach (var (a, b) in new[] { (1, 2), (3, 4), (5, 6), (7, 8), (9, 10), (11, 12), (13, 14), (15, 16) })
                {
                    (s.View[a], s.View[b]) = (s.View[b], s.View[a]);
                    (s.Vis[a], s.Vis[b]) = (s.Vis[b], s.Vis[a]);
                    (s.World[a], s.World[b]) = (s.World[b], s.World[a]);
                }
            }
            turn = WrapA(turn + 0.6f * d);
        }
        Console.WriteLine($"side swaps put right: {swaps} samples");
        return list;
    }

    static float WrapA(float a)
    {
        while (a > Mathf.PI) a -= 2f * Mathf.PI;
        while (a < -Mathf.PI) a += 2f * Mathf.PI;
        return a;
    }

    sealed class Shown
    {
        public double T;
        public Vector2[] J = new Vector2[17];
        public Vector2 Head, Hat;
        public float HeadScale, Roll, Yaw, HeadYaw, Torso;
        public readonly float[] Seen = new float[17];
    }

    static readonly string[] PartNames = { "shoulders", "elbows", "wrists", "hips", "knees", "ankles", "head", "hat" };

    // the reference: each joint smoothed offline both ways in time
    static Vector2[][] Reference(List<Sample> samples)
    {
        int n = samples.Count;
        var refj = new Vector2[n][];
        for (int i = 0; i < n; i++) refj[i] = new Vector2[17];
        for (int j = 0; j < 17; j++)
        {
            var med = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                var xs = new List<float>();
                var ys = new List<float>();
                for (int d = -2; d <= 2; d++)
                {
                    int k = i + d;
                    if (k < 0 || k >= n || !samples[k].Person) continue;
                    xs.Add(samples[k].View[j].x);
                    ys.Add(samples[k].View[j].y);
                }
                if (xs.Count == 0) continue;
                xs.Sort();
                ys.Sort();
                med[i] = new Vector2(xs[xs.Count / 2], ys[ys.Count / 2]);
            }
            for (int i = 0; i < n; i++)
            {
                if (!samples[i].Person) continue;
                double s0 = 0, s1 = 0, s2 = 0, s3 = 0, s4 = 0;
                double px = 0, py = 0, qx = 0, qy = 0, rx = 0, ry = 0;
                for (int k = Math.Max(0, i - 6); k <= Math.Min(n - 1, i + 6); k++)
                {
                    if (!samples[k].Person) continue;
                    double d = samples[k].T - samples[i].T;
                    if (Math.Abs(d) > 0.1) continue;
                    double w = Math.Exp(-0.5 * (d / 0.06) * (d / 0.06)) * (0.05 + Math.Min(1.0, samples[k].Vis[j]));
                    s0 += w; s1 += w * d; s2 += w * d * d; s3 += w * d * d * d; s4 += w * d * d * d * d;
                    px += w * med[k].x; py += w * med[k].y;
                    qx += w * d * med[k].x; qy += w * d * med[k].y;
                    rx += w * d * d * med[k].x; ry += w * d * d * med[k].y;
                }
                refj[i][j] = new Vector2((float)Quad(s0, s1, s2, s3, s4, px, qx, rx), (float)Quad(s0, s1, s2, s3, s4, py, qy, ry));
            }
        }
        return refj;
    }

    static double Quad(double s0, double s1, double s2, double s3, double s4, double p, double q, double r)
    {
        double det = s0 * (s2 * s4 - s3 * s3) - s1 * (s1 * s4 - s3 * s2) + s2 * (s1 * s3 - s2 * s2);
        if (Math.Abs(det) < 1e-18) return s0 > 0 ? p / s0 : 0;
        double da = p * (s2 * s4 - s3 * s3) - s1 * (q * s4 - s3 * r) + s2 * (q * s3 - s2 * r);
        return da / det;
    }

    static int JointOf(int part, bool left) => part switch
    {
        0 => left ? 5 : 6, 1 => left ? 7 : 8, 2 => left ? 9 : 10, 3 => left ? 11 : 12, 4 => left ? 13 : 14,
        5 => left ? 15 : 16, _ => 1,
    };

    static Vector2 Pt(Vector2[] j, int part, bool left) => part == 6 ? (j[1] + j[2]) * 0.5f : j[JointOf(part, left)];

    sealed class Stats
    {
        public double ShakeSum, ErrSum, ShakeRefSum, MovShake, MovErr, MovShakeRef, LagSum;
        public int ShakeN, MovN;
    }

    // what the pipeline under test drew, per shown picture
    static List<Shown> Run(List<Sample> samples, string mode, int lookahead, double poseDelay, double pictureDelay, int seed,
                           Action<OutfitTracker, OutfitPose> setup = null)
    {
        var rng = new System.Random(seed);
        var shown = new List<Shown>();
        var arrivals = samples.Select(s => s.T + poseDelay + (rng.NextDouble() - 0.5) * 0.008).ToArray();
        for (int i = 1; i < arrivals.Length; i++) arrivals[i] = Math.Max(arrivals[i], arrivals[i - 1] + 0.001);
        var pictures = samples.Select(s => s.T + pictureDelay).ToArray();
        double t0 = samples[0].T, t1 = samples[samples.Count - 1].T;
        var timeline = new PoseTimeline();
        var tracker = new OutfitTracker { Lookahead = lookahead };
        var pose = new OutfitPose { Prefiltered = mode == "new" };
        setup?.Invoke(tracker, pose);
        var worldFilters = Enumerable.Range(0, 17).Select(_ => new OneEuroFilter3(1.5f, 1f)).ToArray();
        float worldScale = 0f;
        var world = new Vector3[17];
        bool hasWorld = false;
        double lastSampleT = 0, lastShown = 0;
        int next = 0, firstPic = 0;
        var view = new Vector3[17];
        var view2 = new Vector2[17];
        var vis = new float[17];
        var cam = new Vector2[17];
        double phase = rng.NextDouble() / 30.0;
        for (double d = t0 + 0.3 + phase; d < t1 + 0.2; d += 1.0 / 30.0 + (rng.NextDouble() - 0.5) * 0.002)
        {
            while (next < samples.Count && arrivals[next] <= d)
            {
                var s = samples[next++];
                if (!s.Person) continue;
                if (mode == "old")
                {
                    timeline.Add(s.T, s.RawView, s.RawVis);
                    if (s.HasWorld)
                    {
                        float dtw = lastSampleT > 0 ? (float)(s.T - lastSampleT) : 0f;
                        Vector3 sc = (s.World[5] + s.World[6]) * 0.5f, hc = (s.World[11] + s.World[12]) * 0.5f;
                        float wt = Mathf.Max(Vector3.Distance(sc, hc), 0.2f);
                        worldScale = worldScale <= 0f ? wt : Mathf.Lerp(worldScale, wt, 0.05f);
                        for (int j = 0; j < 17; j++) world[j] = worldFilters[j].Filter(s.RawWorld[j], dtw, worldScale);
                        hasWorld = true;
                    }
                }
                else tracker.Add(s.T, s.RawView, s.RawVis, s.HasWorld ? s.RawWorld : null);
                lastSampleT = s.T;
            }
            // the newest picture the game has (new: no newer than the moment it waits for)
            double limit = mode == "new" ? tracker.ShowTime + 0.004 : double.MaxValue;
            int pic = -1;
            for (int i = firstPic; i < samples.Count && pictures[i] <= d; i++)
                if (samples[i].T <= limit) pic = i;
            if (pic < 0) continue;
            firstPic = Math.Max(0, pic - 2);
            double t = samples[pic].T;
            if (t == lastShown) continue;
            float camDt = lastShown > 0 ? Mathf.Clamp((float)(t - lastShown), 0.001f, 0.2f) : 0f;
            float worldYaw = float.NaN;
            if (mode == "old")
            {
                if (timeline.Count == 0) continue;
                double tt = t;
                if (tt < timeline.Oldest - 0.5 || tt > timeline.Newest + 0.5) tt = timeline.Newest;
                timeline.At(tt, view, vis);
                for (int j = 0; j < 17; j++) cam[j] = new Vector2(view[j].x * 1000f, view[j].y * 1000f);
                if (hasWorld) worldYaw = OutfitPose.WorldYaw(world[5], world[6], world[11], world[12]);
            }
            else
            {
                if (tracker.Count == 0) continue;
                tracker.At(t, view2, vis);
                for (int j = 0; j < 17; j++) cam[j] = view2[j] * 1000f;
                if (tracker.WorldYawAt(t, out float wyaw)) worldYaw = wyaw;
            }
            pose.Update(cam, vis, camDt, worldYaw);
            lastShown = t;
            if (!pose.Valid) continue;
            var o = new Shown { T = t, HeadScale = pose.HeadScale, Roll = pose.HeadRoll, Yaw = pose.Yaw, HeadYaw = pose.HeadYaw, Torso = pose.T };
            for (int j = 0; j < 17; j++)
            {
                o.J[j] = pose.J[j] / 1000f;
                o.Seen[j] = pose.Seen[j];
            }
            o.Head = pose.HeadCenter / 1000f;
            // the crown's band: above the eyes along the head's up, by its size
            Vector2 up = new Vector2(-Mathf.Sin(pose.HeadRoll), Mathf.Cos(pose.HeadRoll));
            o.Hat = (pose.SkullCenter + up * (pose.HeadScale * 0.22f)) / 1000f;
            shown.Add(o);
        }
        return shown;
    }

    [Test]
    public void Bench()
    {
        string path = Environment.GetEnvironmentVariable("DRESS_BENCH");
        if (string.IsNullOrEmpty(path)) Assert.Ignore("set DRESS_BENCH to a recording");
        var samples = Load(path);
        var index = new Dictionary<double, int>();
        for (int i = 0; i < samples.Count; i++) index[samples[i].T] = i;
        var refj = Reference(samples);
        Console.WriteLine($"{Path.GetFileName(path)}: {samples.Count} frames, {samples.Count(s => s.Person)} with a person");

        // ears against eyes on frames facing the camera (how the head's size is measured)
        var ratios = new List<float>();
        foreach (var s in samples)
        {
            if (!s.Person || s.Vis[3] < 0.8f || s.Vis[4] < 0.8f || s.Vis[1] < 0.8f || s.Vis[2] < 0.8f) continue;
            float eyes = Vector2.Distance(s.View[1], s.View[2]), ears = Vector2.Distance(s.View[3], s.View[4]);
            if (eyes > 0.001f) ratios.Add(ears / eyes);
        }
        ratios.Sort();
        if (ratios.Count > 0)
            Console.WriteLine($"ears / eyes on {ratios.Count} frames: median {ratios[ratios.Count / 2]:F2}, 10 % {ratios[ratios.Count / 10]:F2}, 90 % {ratios[ratios.Count * 9 / 10]:F2} (template 0.30 / 0.128 = 2.34)");

        string configs = Environment.GetEnvironmentVariable("DRESS_BENCH_RUNS") ?? "old,new:0,new:1,new:2,new:3";
        bool brief = Environment.GetEnvironmentVariable("DRESS_BENCH_BRIEF") != null;
        foreach (var config in configs.Split(','))
        {
            string mode = config.Split(':')[0];
            var parts = config.Split(':');
            int k = parts.Length > 1 ? int.Parse(parts[1]) : 0;
            // further parts: key=value settings of the tracker (still, moving, slow, fast, glitch)
            Action<OutfitTracker, OutfitPose> setup = (tr, po) =>
            {
                foreach (var kv in parts.Skip(2))
                {
                    var pair = kv.Split('=');
                    float val = float.Parse(pair[1], CultureInfo.InvariantCulture);
                    switch (pair[0])
                    {
                        case "still": tr.StillSigma = val; break;
                        case "moving": tr.MovingSigma = val; break;
                        case "slow": tr.SlowSpeed = val; break;
                        case "fast": tr.FastSpeed = val; break;
                        case "glitch": tr.GlitchTorso = val; break;
                    }
                }
            };
            var all = new Stats[8];
            for (int p = 0; p < 8; p++) all[p] = new Stats();
            double sumYaw = 0, sumHeadYaw = 0, sumScale = 0, sumRoll = 0, yawShake = 0;
            int nYaw = 0, nScale = 0, turned = 0, headTurned = 0;
            foreach (int seed in new[] { 1, 2, 3 })
            {
                var shown = Run(samples, mode, k, 0.070, 0.045, seed, setup);
                for (int n = 1; n + 1 < shown.Count; n++)
                {
                    var a = shown[n - 1];
                    var b = shown[n];
                    var c = shown[n + 1];
                    double g1 = b.T - a.T, g2 = c.T - b.T;
                    if (g1 > 0.09 || g2 > 0.09 || g1 <= 0 || g2 <= 0) continue;
                    if (!index.TryGetValue(b.T, out int ib) || !index.TryGetValue(a.T, out int ia) || !index.TryGetValue(c.T, out int ic)) continue;
                    float torso = Vector2.Distance((refj[ib][5] + refj[ib][6]) * 0.5f, (refj[ib][11] + refj[ib][12]) * 0.5f);
                    if (torso < 0.02f) continue;
                    // acceleration over uneven gaps, as a second difference over one camera frame
                    float k1 = (float)(1.0 / g1), k2 = (float)(1.0 / g2), kk = (float)(2.0 / (g1 + g2) / (30.0 * 30.0));
                    Vector2 Acc(Vector2 pa, Vector2 pb, Vector2 pc) => ((pc - pb) * k2 - (pb - pa) * k1) * kk;
                    for (int part = 0; part < 8; part++)
                    {
                        for (int side = 0; side < (part < 6 ? 2 : 1); side++)
                        {
                            bool left = side == 0;
                            int jv = JointOf(part, left);
                            if (samples[ib].Vis[jv] < 0.6f || samples[ia].Vis[jv] < 0.6f || samples[ic].Vis[jv] < 0.6f) continue;
                            Vector2 oa, ob, oc, ra, rb, rc;
                            if (part == 7)
                            {
                                oa = a.Hat; ob = b.Hat; oc = c.Hat;
                                ra = Pt(refj[ia], 6, true) + (a.Hat - a.Head);
                                rb = Pt(refj[ib], 6, true) + (b.Hat - b.Head);
                                rc = Pt(refj[ic], 6, true) + (c.Hat - c.Head);
                            }
                            else if (part == 6)
                            {
                                oa = a.Head; ob = b.Head; oc = c.Head;
                                ra = Pt(refj[ia], 6, true); rb = Pt(refj[ib], 6, true); rc = Pt(refj[ic], 6, true);
                            }
                            else
                            {
                                oa = Pt(a.J, part, left); ob = Pt(b.J, part, left); oc = Pt(c.J, part, left);
                                ra = Pt(refj[ia], part, left); rb = Pt(refj[ib], part, left); rc = Pt(refj[ic], part, left);
                            }
                            float shake = Acc(oa, ob, oc).magnitude / torso;
                            float shakeRef = Acc(ra, rb, rc).magnitude / torso;
                            Vector2 e = ob - rb;
                            float err = e.magnitude / torso;
                            Vector2 vel = (rc - ra) / (float)(c.T - a.T);
                            float speed = vel.magnitude / torso;
                            var st = all[part];
                            st.ShakeSum += shake * shake; st.ShakeRefSum += shakeRef * shakeRef; st.ErrSum += err * err; st.ShakeN++;
                            if (speed > 0.5f)
                            {
                                st.MovShake += shake * shake; st.MovShakeRef += shakeRef * shakeRef; st.MovErr += err * err; st.MovN++;
                                // behind the body (+) or ahead (-) along its way, in seconds
                                st.LagSum += -Vector2.Dot(e, vel) / Mathf.Max(vel.sqrMagnitude, 1e-9f);
                            }
                        }
                    }
                    sumScale += Sq((c.HeadScale - 2 * b.HeadScale + a.HeadScale) / Mathf.Max(b.HeadScale, 1f));
                    sumRoll += Sq((c.Roll - 2 * b.Roll + a.Roll) * Mathf.Rad2Deg);
                    nScale++;
                    float yaw = Wrap(b.Yaw) * Mathf.Rad2Deg, headYaw = Wrap(b.HeadYaw) * Mathf.Rad2Deg;
                    sumYaw += Sq(yaw);
                    sumHeadYaw += Sq(headYaw);
                    if (Mathf.Abs(yaw) > 25f) turned++;
                    if (Mathf.Abs(headYaw) > 25f) headTurned++;
                    yawShake += Sq(Wrap(c.Yaw - 2 * b.Yaw + a.Yaw) * Mathf.Rad2Deg);
                    nYaw++;
                }
            }
            if (brief)
            {
                // one line: shake (shoulders, elbows, wrists, hips, hat) and error (same), all frames
                string Sh(int p) => (100 * Math.Sqrt(all[p].ShakeSum / Math.Max(1, all[p].ShakeN))).ToString("F2", CultureInfo.InvariantCulture);
                string Er(int p) => (100 * Math.Sqrt(all[p].ErrSum / Math.Max(1, all[p].ShakeN))).ToString("F2", CultureInfo.InvariantCulture);
                Console.WriteLine($"{config,-58} shake sh {Sh(0)} el {Sh(1)} wr {Sh(2)} hp {Sh(3)} hat {Sh(7)} | err sh {Er(0)} el {Er(1)} wr {Er(2)} hp {Er(3)} hat {Er(7)}");
                continue;
            }
            Console.WriteLine($"\n== {config}   shake = RMS frame-to-frame acceleration (ref = the reference path's own), err = RMS distance from the reference, % torso; moving = > 0.5 torso/s");
            for (int part = 0; part < 8; part++)
            {
                var st = all[part];
                double N = Math.Max(1, st.ShakeN), M = Math.Max(1, st.MovN);
                Console.WriteLine($"  {PartNames[part],-10} shake {100 * Math.Sqrt(st.ShakeSum / N),5:F2} (ref {100 * Math.Sqrt(st.ShakeRefSum / N),5:F2})  err {100 * Math.Sqrt(st.ErrSum / N),5:F2}" +
                                  $"  | moving: shake {100 * Math.Sqrt(st.MovShake / M),5:F2} (ref {100 * Math.Sqrt(st.MovShakeRef / M),5:F2})  err {100 * Math.Sqrt(st.MovErr / M),5:F2}  lag {1000 * st.LagSum / M,4:F0} ms  ({100.0 * st.MovN / N:F0} % of {st.ShakeN})");
            }
            Console.WriteLine($"  head size shake {100 * Math.Sqrt(sumScale / Math.Max(1, nScale)):F2} %, roll shake {Math.Sqrt(sumRoll / Math.Max(1, nScale)):F2} deg; body turn RMS {Math.Sqrt(sumYaw / Math.Max(1, nYaw)):F1} deg (> 25: {100.0 * turned / Math.Max(1, nYaw):F0} %, shake {Math.Sqrt(yawShake / Math.Max(1, nYaw)):F2} deg); head turn RMS {Math.Sqrt(sumHeadYaw / Math.Max(1, nYaw)):F1} deg (> 25: {100.0 * headTurned / Math.Max(1, nYaw):F0} %)");
        }
    }

    // Vishu!'s hand cursors (DressGame.UpdateHands) from the fitted pose, three ways (2026-11-17 night: the other games'
    // cursors moved from the wrist pushed along the forearm to the arm made longer from the shoulder, Body.HandPoint):
    // jumps = shown frames whose cursor step differs from the reference's step (the same point on the reference
    // joints) by more than 10 / 20 % of a torso, per minute of a raised hand (wrist above the hips, seen >= 0.6);
    // err = RMS distance from the reference's same point; hand = median distance from the reference's tip (the
    // hand's middle). % torso. DRESS_BENCH=<recording> ... run.sh Cursor -- <the same sources>
    [Test]
    public void Cursor()
    {
        string path = Environment.GetEnvironmentVariable("DRESS_BENCH");
        if (string.IsNullOrEmpty(path)) Assert.Ignore("set DRESS_BENCH to a recording");
        var samples = Load(path);
        var index = new Dictionary<double, int>();
        for (int i = 0; i < samples.Count; i++) index[samples[i].T] = i;
        var refj = Reference(samples);
        Vector2 Point(string kind, Vector2[] j, float[] seen, bool left)
        {
            Vector2 s = j[left ? 5 : 6], e = j[left ? 7 : 8], w = j[left ? 9 : 10];
            switch (kind)
            {
                case "tip": return w + (w - e) * (0.3f * (seen == null ? 1f : seen[left ? 7 : 8]));
                case "arm0.1": return s + (w - s) * 1.1f;
                default: return w;
            }
        }
        Console.WriteLine($"{Path.GetFileName(path)}: Vishu!'s cursor (OutfitTracker, 2 frames of waiting), % torso");
        foreach (var kind in new[] { "tip", "arm0.1", "wrist" })
        {
            double j10 = 0, j20 = 0, err = 0, frames = 0;
            var hand = new List<float>();
            foreach (int seed in new[] { 1, 2, 3 })
            {
                var shown = Run(samples, "new", 2, 0.070, 0.045, seed);
                for (int n = 1; n < shown.Count; n++)
                {
                    var a = shown[n - 1];
                    var b = shown[n];
                    if (b.T - a.T > 0.09 || b.T <= a.T) continue;
                    if (!index.TryGetValue(b.T, out int ib) || !index.TryGetValue(a.T, out int ia)) continue;
                    var rb = refj[ib];
                    var ra = refj[ia];
                    float torso = Vector2.Distance((rb[5] + rb[6]) * 0.5f, (rb[11] + rb[12]) * 0.5f);
                    if (torso < 0.02f) continue;
                    float hipsY = (rb[11].y + rb[12].y) * 0.5f;
                    for (int side = 0; side < 2; side++)
                    {
                        bool left = side == 0;
                        int wj = left ? 9 : 10;
                        if (samples[ib].Vis[wj] < 0.6f || samples[ia].Vis[wj] < 0.6f || rb[wj].y < hipsY) continue;
                        Vector2 cb = Point(kind, b.J, b.Seen, left), ca = Point(kind, a.J, a.Seen, left);
                        Vector2 qb = Point(kind, rb, null, left), qa = Point(kind, ra, null, left);
                        float step = ((cb - ca) - (qb - qa)).magnitude / torso;
                        if (step > 0.1f) j10++;
                        if (step > 0.2f) j20++;
                        err += Sq((cb - qb).magnitude / torso);
                        hand.Add((cb - Point("tip", rb, null, left)).magnitude / torso);
                        frames++;
                    }
                }
            }
            hand.Sort();
            double minutes = Math.Max(frames, 1) / 30.0 / 60.0;
            Console.WriteLine($"  {kind,-7} jumps/min > 10 % {j10 / minutes,6:F1}  > 20 % {j20 / minutes,5:F1} | err {100 * Math.Sqrt(err / Math.Max(1, frames)),5:F2}" +
                              $" | hand {100 * (hand.Count > 0 ? hand[hand.Count / 2] : float.NaN),5:F2}   ({minutes / 3:F2} hand-min a run)");
        }
    }

    static double Sq(double x) => x * x;

    static float Wrap(float a)
    {
        while (a > Mathf.PI) a -= 2f * Mathf.PI;
        while (a < -Mathf.PI) a += 2f * Mathf.PI;
        return a;
    }
}

public class DressBenchTurns
{
    // the body's and the head's turn over the recording, every half second (new pipeline, 2 frames of waiting)
    [Test]
    public void BenchTurns()
    {
        string path = Environment.GetEnvironmentVariable("DRESS_BENCH");
        if (string.IsNullOrEmpty(path) || Environment.GetEnvironmentVariable("DRESS_BENCH_TURNS") == null) Assert.Ignore("set DRESS_BENCH and DRESS_BENCH_TURNS");
        var lines = File.ReadAllLines(path);
        var tracker = new OutfitTracker { Lookahead = 0 };
        var pose = new OutfitPose { Prefiltered = true };
        var pose2d = new OutfitPose { Prefiltered = true };
        var view = new Vector3[17];
        var vis = new float[17];
        var world = new Vector3[17];
        var j2 = new Vector2[17];
        var cam = new Vector2[17];
        double last = 0, lastPrint = 0;
        foreach (var line in lines)
        {
            var m = Regex.Match(line, "\"t\":([0-9]+)");
            double t = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) / 1000.0;
            var k = Regex.Match(line, @"""k"":\[([^\]]*)\]");
            if (!k.Success) continue;
            var v = k.Groups[1].Value.Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            float aspect = 1024f / 768f;
            for (int i = 0; i < 17; i++)
            {
                view[i] = new Vector3((1f - v[i * 4]) * aspect, 1f - v[i * 4 + 1], 0f);
                vis[i] = v[i * 4 + 3];
            }
            var wk = Regex.Match(line, @"""wk"":\[([^\]]*)\]");
            bool hasWorld = wk.Success;
            if (hasWorld)
            {
                var u = wk.Groups[1].Value.Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                for (int i = 0; i < 17; i++) world[i] = new Vector3(-u[i * 3], -u[i * 3 + 1], u[i * 3 + 2]);
            }
            tracker.Add(t, view, vis, hasWorld ? world : null);
            tracker.At(t, j2, vis);
            for (int i = 0; i < 17; i++) cam[i] = j2[i] * 1000f;
            float dt = last > 0 ? (float)(t - last) : 0f;
            float wy = tracker.WorldYawAt(t, out float wyaw) ? wyaw : float.NaN;
            pose.Update(cam, vis, dt, wy);
            pose2d.Update(cam, vis, dt);
            last = t;
            if (t - lastPrint >= 0.5)
            {
                lastPrint = t;
                float sw = Vector2.Distance(cam[5], cam[6]) / Mathf.Max(pose.T, 1f);
                Console.WriteLine($"{t - 1.8e9,6:F1}s  world yaw {Mathf.Rad2Deg * Wrap(wy),6:F0}  body {Mathf.Rad2Deg * Wrap(pose.Yaw),6:F0}  2d {Mathf.Rad2Deg * Wrap(pose2d.Yaw),6:F0}  head {Mathf.Rad2Deg * Wrap(pose.HeadYaw),6:F0}  shoulders/T {sw:F2}  nose vis {vis[0]:F2} ears {vis[3]:F2}/{vis[4]:F2}");
            }
        }
    }

    static float Wrap(float a)
    {
        if (float.IsNaN(a)) return a;
        while (a > Mathf.PI) a -= 2f * Mathf.PI;
        while (a < -Mathf.PI) a += 2f * Mathf.PI;
        return a;
    }
}
