using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Arcade.Tests
{
    /// <summary>
    /// Simulated players for the booths' tests: a body in stage units (torso lengths, hips at 0) whose hands, hips and
    /// arms move toward what a "brain" wants at a limited speed, seen by the rules through a camera that is
    /// <see cref="Profile.Lag"/> late and a little noisy, like the box's (~0.1 s from the picture to the game). Profiles:
    /// perfect, good, child (slow, late, lets some go), idle (stands still).
    /// </summary>
    public sealed class Profile
    {
        public string Name;
        public float Reaction, HandSpeed, StepSpeed, Lag, Noise, Skip;
        public static readonly Profile Perfect = new Profile { Name = "perfect", Reaction = 0.2f, HandSpeed = 5.5f, StepSpeed = 3.2f, Lag = 0.12f, Noise = 0.01f };
        public static readonly Profile Good = new Profile { Name = "good", Reaction = 0.3f, HandSpeed = 4.5f, StepSpeed = 2.6f, Lag = 0.12f, Noise = 0.015f, Skip = 0.05f };
        public static readonly Profile Child = new Profile { Name = "child", Reaction = 0.45f, HandSpeed = 3.2f, StepSpeed = 1.9f, Lag = 0.14f, Noise = 0.025f, Skip = 0.15f };
        public static readonly Profile Idle = new Profile { Name = "idle", Reaction = 9f, HandSpeed = 0f, StepSpeed = 0f, Lag = 0.12f, Noise = 0.015f, Skip = 1f };
        public static readonly Profile[] All = { Perfect, Good, Child, Idle };
    }

    public sealed class SimBody
    {
        public const float Upper = 0.585f, Fore = 0.66f, Reach = Upper + Fore - 0.02f;
        public Vector2 Hips;
        public float Squat;
        /// <summary>Where the hands are (when no arm angles are set).</summary>
        public Vector2 LeftHand = new Vector2(-0.52f, -0.24f), RightHand = new Vector2(0.52f, -0.24f);
        /// <summary>Arm angles (degrees from hanging, + outward) instead of hand positions: walls.</summary>
        public bool UseAngles;
        public float LU, LF, RU, RF;
        public float LeftThigh = 12f, RightThigh = 12f;
        public Vector2 HeadOffset;

        public Vector2 LeftShoulder => Hips + new Vector2(-0.42f, 1f - 0.45f * Squat);
        public Vector2 RightShoulder => Hips + new Vector2(0.42f, 1f - 0.45f * Squat);

        public StageBody ToStage(System.Random noise, float amount)
        {
            var b = new StageBody { Present = true };
            var hips = Hips + new Vector2(0f, -0.45f * Squat);
            b.SetStanding(hips);
            b[Part.Head] = hips + new Vector2(0f, 1.45f) + HeadOffset;
            var ls = LeftShoulder;
            var rs = RightShoulder;
            if (UseAngles)
            {
                b[Part.LeftElbow] = ls + Geo.Limb(true, LU) * Upper;
                b[Part.LeftHand] = b[Part.LeftElbow] + Geo.Limb(true, LF) * Fore;
                b[Part.RightElbow] = rs + Geo.Limb(false, RU) * Upper;
                b[Part.RightHand] = b[Part.RightElbow] + Geo.Limb(false, RF) * Fore;
            }
            else
            {
                Arm(b, ls, LeftHand, Part.LeftElbow, Part.LeftHand, true);
                Arm(b, rs, RightHand, Part.RightElbow, Part.RightHand, false);
            }
            b[Part.LeftKnee] = b[Part.LeftHip] + Geo.Limb(true, LeftThigh) * 0.92f;
            b[Part.RightKnee] = b[Part.RightHip] + Geo.Limb(false, RightThigh) * 0.92f;
            b[Part.LeftFoot] = b[Part.LeftKnee] + new Vector2(0f, -0.92f);
            b[Part.RightFoot] = b[Part.RightKnee] + new Vector2(0f, -0.92f);
            if (amount > 0f)
                for (int i = 0; i < StageBody.PartCount; i++)
                    b.At[i] += new Vector2(Gauss(noise) * amount, Gauss(noise) * amount);
            return b;
        }

        /// <summary>An arm reaching for <paramref name="hand"/> (as far as it goes), the elbow bent outward.</summary>
        static void Arm(StageBody b, Vector2 shoulder, Vector2 hand, Part elbowPart, Part handPart, bool left)
        {
            var d = hand - shoulder;
            float len = Mathf.Min(d.magnitude, Reach);
            var dir = d.sqrMagnitude > 1e-6f ? d.normalized : Vector2.down;
            var h = shoulder + dir * len;
            // the elbow: off the line between shoulder and hand by the bend (law of cosines), outward
            float a = Upper, c = Fore;
            float x = (a * a - c * c + len * len) / (2f * len);
            float y = Mathf.Sqrt(Mathf.Max(0f, a * a - x * x));
            var side = new Vector2(-dir.y, dir.x);
            if (left ? side.x > 0f : side.x < 0f) side = -side;
            b[elbowPart] = shoulder + dir * x + side * y;
            b[handPart] = h;
        }

        public static float Gauss(System.Random r)
        {
            double u1 = 1.0 - r.NextDouble(), u2 = r.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2));
        }

        public static Vector2 Toward(Vector2 from, Vector2 to, float maxStep) => Vector2.MoveTowards(from, to, maxStep);
    }

    /// <summary>The camera between the simulated body and the rules: what the rules see is the body
    /// <see cref="Profile.Lag"/> seconds ago, with noise; a glitch can be injected for a frame.</summary>
    public sealed class SimCamera
    {
        readonly Queue<(float time, StageBody body)> _line = new Queue<(float, StageBody)>();
        readonly System.Random _noise;
        readonly float _lag, _amount;
        StageBody _last;

        public SimCamera(Profile profile, int seed)
        {
            _lag = profile.Lag;
            _amount = profile.Noise;
            _noise = new System.Random(seed);
        }

        public StageBody See(float now, SimBody body)
        {
            _line.Enqueue((now, body.ToStage(_noise, _amount)));
            while (_line.Count > 1 && _line.Peek().time < now - _lag) _last = _line.Dequeue().body;
            return _last ?? _line.Peek().body;
        }
    }

    public static class Sim
    {
        public const float Dt = 1f / 30f;

        /// <summary>Plays <paramref name="rules"/> to the end with one body per player; <paramref name="brain"/> moves
        /// the bodies each frame (time, player, body).</summary>
        public static void Run(MachineRules rules, Profile[] profiles, Action<float, int, SimBody> brain, int seed = 3,
                               Action<float> each = null)
        {
            int n = rules.Players;
            var bodies = new SimBody[n];
            var cams = new SimCamera[n];
            var seen = new StageBody[n];
            for (int p = 0; p < n; p++)
            {
                bodies[p] = new SimBody();
                cams[p] = new SimCamera(profiles[Mathf.Min(p, profiles.Length - 1)], seed + 17 * p);
            }
            float t = 0f;
            while (!rules.Finished)
            {
                for (int p = 0; p < n; p++)
                {
                    brain(t, p, bodies[p]);
                    seen[p] = cams[p].See(t, bodies[p]);
                }
                rules.Events.Clear();
                rules.Step(Dt, seen);
                each?.Invoke(t);
                t += Dt;
            }
        }
    }
}
