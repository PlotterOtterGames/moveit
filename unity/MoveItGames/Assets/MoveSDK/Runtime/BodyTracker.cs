using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Motion
{
    /// <summary>
    /// Turns per-frame detections into stable, filtered bodies: matches people between frames by torso position,
    /// filters every joint (a smooth track for drawing and a low-lag track for gestures), and picks the active player.
    ///
    /// Player continuity: when the camera briefly loses someone (motion blur, occlusion, stepping to the edge), the
    /// body is kept for <see cref="LostTimeout"/> seconds and re-attached when they reappear nearby, so the game
    /// doesn't pause and the player doesn't have to recalibrate.
    /// </summary>
    public sealed class BodyTracker
    {
        public float MatchDistance = 0.35f;
        /// <summary>How far a briefly-lost player may have moved when re-attached, per second missing (view units).</summary>
        public float LostMatchGrowth = 0.3f;
        public float LostTimeout = 1.5f;

        // One Euro settings. Beta is body-relative: it acts on speed in torso lengths per second (see OneEuroFilter),
        // so the same values fit a child far away and an adult close up. Tuned offline against realistic sidesteps,
        // jumps and swipes with camera-like noise: the smooth track keeps the old rest jitter with about a third of
        // the old lag during fast moves, the fast track has less jitter and about 15 ms less lag than before.
        public float SmoothMinCutoff = 0.5f, SmoothBeta = 1.5f, SmoothDerivativeCutoff = 1f;
        public float FastMinCutoff = 1f, FastBeta = 4f, FastDerivativeCutoff = 1f;
        public float WorldMinCutoff = 1.5f, WorldBeta = 1f;

        readonly List<Body> _bodies = new List<Body>();
        readonly List<Candidate> _candidates = new List<Candidate>();
        readonly Stack<Candidate> _candidatePool = new Stack<Candidate>();
        int _nextId = 1;

        public IReadOnlyList<Body> Bodies => _bodies;
        public Body Active { get; private set; }

        public event Action<Body> ActiveChanged;

        sealed class Candidate
        {
            public readonly Vector3[] Joints = new Vector3[Skeleton.JointCount];
            public readonly float[] Visibility = new float[Skeleton.JointCount];
            public readonly Vector3[] World = new Vector3[Skeleton.JointCount];
            public bool HasWorld;
            public float Score;
            public Vector3 Center;
            public float Torso, WorldTorso;
            public bool Matched;
        }

        public void Update(MotionPacket packet, float now)
        {
            float aspect = packet.h > 0 ? (float)packet.w / packet.h : 16f / 9f;
            double sampleTime = packet.t / 1000.0;
            BuildCandidates(packet, aspect);

            // Tracked bodies first, then briefly-lost ones (with a radius that grows the longer they were missing).
            foreach (bool lostPass in new[] { false, true })
            {
                foreach (var body in _bodies)
                {
                    bool lost = !body.IsTracking;
                    if (lost != lostPass) continue;
                    float missing = now - body.LastSeen;
                    float radius = lost ? Mathf.Min(MatchDistance + LostMatchGrowth * missing, 0.8f) : MatchDistance;
                    var best = Nearest(body.TorsoCenter, radius);
                    if (best == null) continue;
                    best.Matched = true;
                    Apply(body, best, aspect, sampleTime, now);
                }
            }

            foreach (var c in _candidates)
            {
                if (c.Matched) continue;
                var body = new Body { Id = _nextId++ };
                for (int i = 0; i < Skeleton.JointCount; i++)
                {
                    body.SmoothFilters[i] = new OneEuroFilter3(SmoothMinCutoff, SmoothBeta, SmoothDerivativeCutoff);
                    body.FastFilters[i] = new OneEuroFilter3(FastMinCutoff, FastBeta, FastDerivativeCutoff);
                    body.WorldFilters[i] = new OneEuroFilter3(WorldMinCutoff, WorldBeta);
                }
                Apply(body, c, aspect, sampleTime, now);
                _bodies.Add(body);
            }

            _bodies.RemoveAll(b => now - b.LastSeen > LostTimeout);
            SelectActive();
        }

        public void Clear()
        {
            _bodies.Clear();
            SelectActive();
        }

        /// <summary>Force a different player to become active (e.g. "next player" in turns mode).</summary>
        public void SetActive(Body body)
        {
            if (Active == body) return;
            Active = body;
            ActiveChanged?.Invoke(Active);
        }

        Candidate Nearest(Vector3 center, float radius)
        {
            Candidate best = null;
            float bestDist = radius;
            foreach (var c in _candidates)
            {
                if (c.Matched) continue;
                float d = Vector2.Distance(c.Center, center);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = c;
                }
            }
            return best;
        }

        void BuildCandidates(MotionPacket packet, float aspect)
        {
            foreach (var c in _candidates) _candidatePool.Push(c);
            _candidates.Clear();
            if (packet.people == null) return;

            foreach (var person in packet.people)
            {
                if (person.k == null || person.k.Length < Skeleton.JointCount * 4) continue;
                var c = _candidatePool.Count > 0 ? _candidatePool.Pop() : new Candidate();
                for (int i = 0; i < Skeleton.JointCount; i++)
                {
                    int o = i * 4;
                    // Raw camera space -> mirrored view space (x right = player's right, y up, units = image height).
                    c.Joints[i] = new Vector3((1f - person.k[o]) * aspect, 1f - person.k[o + 1], person.k[o + 2] * aspect);
                    c.Visibility[i] = person.k[o + 3];
                }
                c.HasWorld = person.wk != null && person.wk.Length >= Skeleton.JointCount * 3;
                if (c.HasWorld)
                {
                    // MediaPipe world (x image-right, y down) -> mirrored, y up. Depth (z) keeps its sign.
                    for (int i = 0; i < Skeleton.JointCount; i++)
                        c.World[i] = new Vector3(-person.wk[i * 3], -person.wk[i * 3 + 1], person.wk[i * 3 + 2]);
                    c.WorldTorso = Vector3.Distance(Mid(c.World, Joint.LeftShoulder, Joint.RightShoulder),
                                                    Mid(c.World, Joint.LeftHip, Joint.RightHip));
                }
                var shoulders = Mid(c.Joints, Joint.LeftShoulder, Joint.RightShoulder);
                var hips = Mid(c.Joints, Joint.LeftHip, Joint.RightHip);
                c.Center = (shoulders + hips) * 0.5f;
                c.Torso = Vector2.Distance(shoulders, hips);
                c.Score = person.score;
                c.Matched = false;
                _candidates.Add(c);
            }
        }

        static void Apply(Body body, Candidate c, float aspect, double sampleTime, float now)
        {
            float dt = body.SampleTime > 0 ? (float)(sampleTime - body.SampleTime) : 0f;
            // Body size for the filters: follows the torso slowly, so a jump or a crouch doesn't change it much.
            body.FilterScale = SmoothScale(body.FilterScale, c.Torso, 0.02f);
            if (c.HasWorld) body.WorldFilterScale = SmoothScale(body.WorldFilterScale, c.WorldTorso, 0.2f);
            for (int i = 0; i < Skeleton.JointCount; i++)
            {
                body.Joints[i] = body.SmoothFilters[i].Filter(c.Joints[i], dt, body.FilterScale);
                body.Fast[i] = body.FastFilters[i].Filter(c.Joints[i], dt, body.FilterScale);
                body.FastVelocity[i] = body.FastFilters[i].Velocity;
                body.Raw[i] = c.Joints[i];
                body.Visibility[i] = c.Visibility[i];
                if (c.HasWorld) body.World[i] = body.WorldFilters[i].Filter(c.World[i], dt, body.WorldFilterScale);
            }
            body.HasWorld = c.HasWorld;
            body.Score = c.Score;
            body.Aspect = aspect;
            body.SampleTime = sampleTime;
            body.LastSeen = now;
        }

        static float SmoothScale(float current, float sample, float min)
        {
            sample = Mathf.Max(sample, min);
            return current <= 0f ? sample : Mathf.Lerp(current, sample, 0.05f);
        }

        static Vector3 Mid(Vector3[] joints, Joint a, Joint b) => (joints[(int)a] + joints[(int)b]) * 0.5f;

        void SelectActive()
        {
            if (Active != null && _bodies.Contains(Active)) return;

            Body best = null;
            float bestRank = float.MinValue;
            foreach (var b in _bodies)
            {
                // Prefer the biggest (closest) and most central person.
                float rank = b.TorsoLength * 2f - Mathf.Abs(b.TorsoCenter.x - b.Aspect * 0.5f);
                if (rank > bestRank)
                {
                    bestRank = rank;
                    best = b;
                }
            }
            if (best == Active) return;
            Active = best;
            ActiveChanged?.Invoke(Active);
        }
    }
}
