using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Tennis
{
    /// <summary>
    /// A real court (metres; doubles is played: the alleys are in after the serve): the net along x at z = 0, the player's
    /// team (0) at z &lt; 0 facing +z, the other team (1) at z &gt; 0 facing -z. x grows to the right as the player sees it
    /// (the camera is behind team 0).
    /// </summary>
    public static class Court
    {
        public const float HalfLength = 11.885f;
        /// <summary>The doubles sidelines (the net posts stand outside them).</summary>
        public const float HalfWidth = 5.485f;
        /// <summary>The singles sidelines: the service boxes' width.</summary>
        public const float SinglesHalfWidth = 4.115f;
        /// <summary>The service line's distance from the net.</summary>
        public const float ServiceLine = 6.40f;
        public const float NetCenter = 0.914f, NetPost = 1.07f;
        /// <summary>The posts stand this far outside the doubles sidelines.</summary>
        public const float PostX = HalfWidth + 0.914f;
        /// <summary>A real ball (the rules; the game draws it bigger so it reads on a TV).</summary>
        public const float BallRadius = 0.033f;

        /// <summary>-1 for team 0's half (z &lt; 0), +1 for team 1's.</summary>
        public static float Side(int team) => team == 0 ? -1f : 1f;

        /// <summary>A team's own right, in x: team 0 faces +z (right = +x), team 1 faces -z (right = -x).</summary>
        public static float RightSign(int team) => team == 0 ? 1f : -1f;

        /// <summary>The net's height at x: it sags to the middle.</summary>
        public static float NetHeight(float x)
        {
            float k = Mathf.Clamp01(Mathf.Abs(x) / PostX);
            return NetCenter + (NetPost - NetCenter) * k * k;
        }

        /// <summary>The team whose half <paramref name="z"/> is in.</summary>
        public static int TeamAt(float z) => z < 0f ? 0 : 1;

        /// <summary>A bounce at <paramref name="p"/> is in <paramref name="team"/>'s half of the court (a ball touching a
        /// line is in): the doubles court with its alleys, or the singles court.</summary>
        public static bool InCourt(Vector3 p, int team, bool doubles = true)
        {
            float depth = p.z * Side(team);
            float half = doubles ? HalfWidth : SinglesHalfWidth;
            return depth >= 0f && depth <= HalfLength + BallRadius && Mathf.Abs(p.x) <= half + BallRadius;
        }

        /// <summary>A serve bouncing at <paramref name="p"/> is in the box of <paramref name="receiver"/>: the deuce
        /// box is on the receiver's right, the advantage box on its left.</summary>
        public static bool InServiceBox(Vector3 p, int receiver, bool deuceCourt)
        {
            float depth = p.z * Side(receiver);
            float across = p.x * RightSign(receiver) * (deuceCourt ? 1f : -1f);
            return depth >= 0f && depth <= ServiceLine + BallRadius && across >= -BallRadius && across <= SinglesHalfWidth + BallRadius;
        }
    }

    /// <summary>The ball: where, how fast, its spin (+1 = heavy topspin, -1 = slice) and its pace.</summary>
    public struct BallState
    {
        public Vector3 Position, Velocity;
        public float Spin;
        /// <summary>
        /// How fast time runs for this ball (0 counts as 1): its gravity is <see cref="BallPhysics.GravityFor"/> times
        /// the pace squared. A shot aimed with <see cref="BallPhysics.Aim"/> and launched with its velocity times the pace
        /// flies exactly the same arc, bounces as high and lands on the same spot, only slower (below 1) or quicker: the
        /// rally's pace (<see cref="TennisMatch.PaceFor"/>) changes the ball's speed without changing where a racket can
        /// meet it.
        /// </summary>
        public float Pace;

        public BallState(Vector3 position, Vector3 velocity, float spin = 0f, float pace = 1f)
        {
            Position = position;
            Velocity = velocity;
            Spin = spin;
            Pace = pace;
        }

        /// <summary><see cref="Pace"/>, a default (zero) one counting as 1.</summary>
        public float TimeScale => Pace > 0f ? Pace : 1f;
    }

    /// <summary>What happened to the ball during one step.</summary>
    public enum BallEvent { None, Bounce, Net }

    /// <summary>One point of a predicted path.</summary>
    public struct PathPoint
    {
        public double Time;
        public BallState Ball;
        /// <summary>Bounces so far (counted on the whole court).</summary>
        public int Bounces;
        public BallEvent Event;
    }

    /// <summary>
    /// The ball's flight: gravity (spin changes how fast it drops: topspin dips, slice floats), bounces that keep less
    /// speed for a slice and kick on for topspin, the net. There's no air drag, so slow balls come down steeper than real
    /// ones: bounces keep half the fall (a real ball keeps ~0.74), so a ball reaches the player at a height a racket can
    /// meet (with 0.74 a slow ball came to the baseline at up to 3.7 m). Fixed steps integrated exactly for constant gravity, so a path
    /// predicted ahead is the path the ball will fly (players run to where it will really be). No air drag: shots are
    /// aimed with <see cref="Aim"/>, which knows the same physics.
    /// </summary>
    public static class BallPhysics
    {
        public const float Gravity = 9.81f;
        public const float Step = 1f / 120f;

        /// <summary>Gravity with spin: topspin pulls the ball down faster, slice holds it up a little.</summary>
        public static float GravityFor(float spin) => Gravity * (1f + (spin >= 0f ? 0.45f : 0.22f) * spin);

        /// <summary>The gravity this ball flies under: its spin's, times its pace squared (<see cref="BallState.Pace"/>).</summary>
        public static float GravityOf(in BallState b)
        {
            float pace = b.TimeScale;
            return GravityFor(b.Spin) * pace * pace;
        }

        /// <summary>The ball is rolling (or lying) on the ground: no more bounces, gravity no longer pulls it.</summary>
        public static bool Resting(in BallState b) => b.Position.y <= Court.BallRadius + 1e-4f && Mathf.Abs(b.Velocity.y) < 1e-3f;

        /// <summary>Advances the ball by <paramref name="dt"/> (at most one <see cref="Step"/> for exact bounces).</summary>
        public static BallEvent Advance(ref BallState b, float dt)
        {
            var before = b.Position;
            var velocityBefore = b.Velocity;
            float g = Resting(b) ? 0f : GravityOf(b);
            b.Position += b.Velocity * dt + Vector3.down * (0.5f * g * dt * dt);
            b.Velocity.y -= g * dt;

            // the net: crossing z = 0 below the tape (inside the posts)
            if ((before.z < 0f) != (b.Position.z < 0f) && Mathf.Abs(b.Velocity.z) > 1e-4f)
            {
                float k = -before.z / (b.Position.z - before.z);
                var at = Vector3.Lerp(before, b.Position, k);
                if (Mathf.Abs(at.x) <= Court.PostX && at.y - Court.BallRadius < Court.NetHeight(at.x))
                {
                    float back = before.z < 0f ? -1f : 1f;
                    b.Position = new Vector3(at.x, at.y, back * (Court.BallRadius + 0.02f));
                    b.Velocity = new Vector3(b.Velocity.x * 0.2f, Mathf.Max(0f, b.Velocity.y) * 0.2f, -b.Velocity.z * 0.1f);
                    b.Spin = 0f;
                    return BallEvent.Net;
                }
            }

            if (b.Position.y < Court.BallRadius && b.Velocity.y < 0f)
            {
                // back to the moment it touched (else a fast ball bounces up to a step, 20 cm, past where it landed:
                // a ball on the line could be called out)
                float a = 0.5f * g, c = Mathf.Max(0f, before.y - Court.BallRadius), vy = velocityBefore.y;
                float s = a > 1e-6f ? (vy + Mathf.Sqrt(Mathf.Max(0f, vy * vy + 4f * a * c))) / (2f * a) : dt;
                s = Mathf.Clamp(s, 0f, dt);
                b.Position = before + velocityBefore * s + Vector3.down * (a * s * s);
                b.Velocity = velocityBefore + Vector3.down * (g * s);
                b.Position.y = Court.BallRadius;
                float spin = b.Spin;
                float bounce = spin < 0f ? 0.44f + 0.08f * spin : 0.5f + 0.04f * spin;   // slice stays low
                float keep = spin < 0f ? 0.76f + 0.06f * spin : 0.8f + 0.08f * spin;      // topspin kicks on
                b.Velocity = new Vector3(b.Velocity.x * keep, -b.Velocity.y * bounce, b.Velocity.z * keep);
                b.Spin *= 0.4f;
                if (b.Velocity.y < 0.6f * b.TimeScale)   // (a slow ball's bounces are as high as a quick one's)
                {
                    b.Velocity.y = 0f;     // rolling
                    b.Velocity *= 0.9f;
                }
                return BallEvent.Bounce;
            }
            return BallEvent.None;
        }

        /// <summary>
        /// The path from <paramref name="start"/> for <paramref name="seconds"/>, one point per step (the first is the
        /// start itself). Stops early after <paramref name="maxBounces"/> bounces or at the net.
        /// </summary>
        public static void Predict(BallState start, double startTime, float seconds, List<PathPoint> into, int maxBounces = 2)
        {
            into.Clear();
            var b = start;
            int bounces = 0;
            into.Add(new PathPoint { Time = startTime, Ball = b });
            int steps = Mathf.CeilToInt(seconds / Step);
            for (int i = 1; i <= steps; i++)
            {
                var e = Advance(ref b, Step);
                if (e == BallEvent.Bounce) bounces++;
                into.Add(new PathPoint { Time = startTime + i * Step, Ball = b, Bounces = bounces, Event = e });
                if (e == BallEvent.Net || bounces >= maxBounces) break;
            }
        }

        /// <summary>The ball on a predicted path at <paramref name="time"/> (between points: interpolated).</summary>
        public static BallState At(List<PathPoint> path, double time)
        {
            if (path.Count == 0) return default;
            if (time <= path[0].Time) return path[0].Ball;
            for (int i = 1; i < path.Count; i++)
            {
                if (path[i].Time < time) continue;
                var a = path[i - 1];
                var b = path[i];
                float k = (float)((time - a.Time) / System.Math.Max(1e-6, b.Time - a.Time));
                if (b.Event != BallEvent.None) return k < 0.5f ? a.Ball : b.Ball;   // don't blend across a bounce
                return new BallState(Vector3.Lerp(a.Ball.Position, b.Ball.Position, k), Vector3.Lerp(a.Ball.Velocity, b.Ball.Velocity, k), b.Ball.Spin);
            }
            return path[path.Count - 1].Ball;
        }

        /// <summary>
        /// The launch velocity that lands a ball from <paramref name="from"/> on <paramref name="to"/> (on the ground) at
        /// about <paramref name="speed"/> m/s across the court, clearing the net by <paramref name="clearance"/> (the arc
        /// is raised, by flying slower, until it does). <paramref name="flightTime"/> is the time to the bounce.
        /// </summary>
        public static Vector3 Aim(Vector3 from, Vector3 to, float speed, float spin, float clearance, out float flightTime)
        {
            float g = GravityFor(spin);
            float dx = to.x - from.x, dz = to.z - from.z;
            float dist = Mathf.Max(0.5f, Mathf.Sqrt(dx * dx + dz * dz));
            bool crosses = (from.z < 0f) != (to.z < 0f);
            speed = Mathf.Max(2f, speed);
            Vector3 v = default;
            flightTime = dist / speed;
            for (int i = 0; i < 60; i++)
            {
                float t = dist / speed;
                float vy = (Court.BallRadius - from.y + 0.5f * g * t * t) / t;
                v = new Vector3(dx / t, vy, dz / t);
                flightTime = t;
                if (!crosses || clearance <= -10f) break;
                float tn = -from.z / v.z;
                float yn = from.y + vy * tn - 0.5f * g * tn * tn;
                float xn = from.x + v.x * tn;
                if (yn - Court.BallRadius >= Court.NetHeight(xn) + clearance) break;
                speed *= 0.96f;
            }
            return v;
        }

        /// <summary>A launch that drops into the net (a mistake): aimed at <paramref name="to"/> but too low to clear it.</summary>
        public static Vector3 IntoTheNet(Vector3 from, Vector3 to, float speed)
        {
            float dz = to.z - from.z, dx = to.x - from.x;
            float tn = Mathf.Abs(from.z) / Mathf.Max(2f, speed);
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            float vz = dz / dist * speed, vx = dx / dist * speed;
            // reach the net at about half its height
            float vy = (Court.NetCenter * 0.5f - from.y + 0.5f * Gravity * tn * tn) / tn;
            return new Vector3(vx, vy, vz);
        }
    }
}
