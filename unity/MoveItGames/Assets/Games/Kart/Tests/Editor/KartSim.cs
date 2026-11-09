using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Kart.Tests
{
    /// <summary>
    /// Simulated players for Gara!'s rules (KartRaceTests): a person turning an invisible wheel the way the camera would
    /// report it. They look at the road ahead and steer for a line on it (into the corners as much as the smart steering
    /// leaves them, back toward their line), late by their reaction plus the camera's lag, with noise; they jump at
    /// ramps (well or badly timed, or not at all), hold the wheel hard through the long corners to drift, and raise their
    /// hands for an item after a while.
    /// </summary>
    public sealed class KartSim
    {
        public sealed class Profile
        {
            public string Name;
            /// <summary>Reaction plus the camera's lag (s).</summary>
            public float Delay;
            /// <summary>Wheel noise (a share of a full turn): slow wandering and per-sample jitter.</summary>
            public float Wander, Jitter;
            /// <summary>How much of the curve they steer into by themselves (1 = right), how hard they correct.</summary>
            public float Anticipate = 1f, Gain = 2.4f;
            /// <summary>Jumps at ramps: how often, how far from the lip (s, a spread around a slightly early mean).</summary>
            public float Tricks, TrickSpread, TrickBias = -0.1f;
            /// <summary>Hold the wheel hard through long corners (a drift) this often.</summary>
            public float Drifts;
            /// <summary>Use an item this long after getting it (s; ∞ = never).</summary>
            public float ItemAfter = 1.5f;
            /// <summary>Sometimes yank the wheel the wrong way for a moment (a child): per second.</summary>
            public float Yanks;
            /// <summary>Doesn't hold a wheel at all (the hands down, standing still).</summary>
            public bool Idle;
            /// <summary>Goes for the boost pads, gift boxes and stars this often.</summary>
            public float Aims;

            public static Profile Perfect => new Profile { Name = "perfect", Delay = 0.12f, Aims = 0.9f, Tricks = 1f, TrickSpread = 0.05f, Drifts = 0.9f, ItemAfter = 0.8f };
            public static Profile Good => new Profile { Name = "good", Delay = 0.22f, Aims = 0.6f, Wander = 0.05f, Jitter = 0.04f, Anticipate = 0.85f, Tricks = 0.85f, TrickSpread = 0.15f, Drifts = 0.4f, ItemAfter = 1.2f };
            public static Profile Child => new Profile { Name = "child", Delay = 0.35f, Aims = 0.3f, Wander = 0.14f, Jitter = 0.08f, Anticipate = 0.5f, Gain = 2f, Tricks = 0.55f, TrickSpread = 0.35f, Drifts = 0.1f, ItemAfter = 2.5f, Yanks = 0.08f };
            public static Profile Struggling => new Profile { Name = "struggling", Delay = 0.5f, Aims = 0.1f, Wander = 0.22f, Jitter = 0.12f, Anticipate = 0.35f, Gain = 1.4f, Tricks = 0.15f, TrickSpread = 0.5f, ItemAfter = 6f, Yanks = 0.15f };
            public static Profile Nothing => new Profile { Name = "standing still", Idle = true, ItemAfter = float.PositiveInfinity };

            public static Profile[] All => new[] { Perfect, Good, Child, Struggling, Nothing };
        }

        readonly KartRace _race;
        readonly int _slot;
        readonly Profile _p;
        readonly System.Random _rng;
        readonly int _seed;
        readonly Queue<(double t, float u)> _wheel = new Queue<(double, float)>();
        float _wander, _yank;
        double _yankUntil = -1;
        int _jumpedRamp = -1;
        double _hopAt = double.NaN;
        double _itemAt = double.NaN;
        bool _drift;
        int _driftCorner = -999;
        double _driftFrom;
        double _lastSample;

        public KartSim(KartRace race, int slot, Profile profile, int seed)
        {
            _race = race;
            _slot = slot;
            _p = profile;
            _seed = seed;
            _rng = new System.Random(seed);
        }

        Kart K => _race.KartOf(_slot);

        /// <summary>A frame: what the player does now, as the race hears it (at the camera's rate, 30 a second).</summary>
        public void Step(float dt)
        {
            var k = K;
            double now = _race.Time;
            if (_p.Idle)
            {
                _race.Steer(_slot, 0f, false);
                return;
            }
            if (now - _lastSample < 1.0 / 30.0) return;
            _lastSample = now;

            // what they would turn the wheel to now (seen with their delay)
            float u = Want(k, now);
            _wheel.Enqueue((now, u));
            float late = 0f;
            while (_wheel.Count > 0 && now - _wheel.Peek().t >= _p.Delay) late = _wheel.Dequeue().u;
            _race.Steer(_slot, late, true);

            // a jump at a ramp: as it comes, they decide and time it
            if (_race.Phase == KartPhase.Racing && !k.Airborne)
            {
                float toLip = _race.TimeToLip(k, out int ramp);
                int lapRamp = ramp * 100 + k.Lap;
                if (ramp >= 0 && toLip < 1.5f && _jumpedRamp != lapRamp)
                {
                    _jumpedRamp = lapRamp;
                    if (_rng.NextDouble() < _p.Tricks) _hopAt = now + toLip + _p.TrickBias + Gauss() * _p.TrickSpread;
                }
            }
            // the start: the good ones jump at "go"
            if (_race.Phase == KartPhase.Countdown && double.IsNaN(_hopAt) && now > -1.0 && _rng.NextDouble() < _p.Tricks * 0.05)
                _hopAt = Gauss() * Math.Max(0.05, _p.TrickSpread);
            if (!double.IsNaN(_hopAt) && now >= _hopAt + _p.Delay)
            {
                // the camera reports it with their delay's camera part (the reaction is already in the timing)
                _race.Hop(_slot, _hopAt);
                _hopAt = double.NaN;
            }

            // items: raise the hands a while after getting one
            if (k.Item != KartItem.None && k.ItemRoll <= 0f)
            {
                if (double.IsNaN(_itemAt)) _itemAt = now + _p.ItemAfter * (0.6 + 0.8 * _rng.NextDouble());
                if (now >= _itemAt)
                {
                    _race.UseItem(_slot, now);
                    _itemAt = double.NaN;
                }
            }
            else _itemAt = double.NaN;
        }

        float Want(Kart k, double now)
        {
            var track = _race.Track;
            float v = Mathf.Max(8f, k.Speed);
            float look = 9f + 0.5f * v;
            float kap = track.CurvatureAhead(k.S, 2f, look);
            // a line: the middle, a little inside the corners, over a boost pad or through a gift box they go for; round a
            // kart just ahead on the side with more room
            float target = Mathf.Clamp(kap * 120f, -1f, 1f) * 2.5f;
            for (int i = 0; i < track.Features.Count; i++)
            {
                var f = track.Features[i];
                float ahead = track.Ahead(k.S, f.S);
                if (ahead < 2f || ahead > 45f) continue;
                bool wanted = f.Kind == FeatureKind.Boost || (f.Kind == FeatureKind.Items && k.Item == KartItem.None) ||
                              (f.Kind == FeatureKind.Stars && ahead < 25f);
                if (!wanted || Hash(i, k.Lap) >= _p.Aims) continue;
                target = f.Kind == FeatureKind.Items ? KartRace.BoxD(f, Mathf.Clamp(Mathf.RoundToInt((k.D - f.D + f.Width * 0.5f) / f.Width * f.Count - 0.5f), 0, f.Count - 1)) : f.D;
                break;
            }
            foreach (var o in _race.Karts)
            {
                if (o == k) continue;
                float gap = track.Ahead(k.S, o.S);
                if (gap < 1f || gap > 16f || Mathf.Abs(o.D - target) > 2.4f || o.Speed > k.Speed + 2f) continue;
                float left = o.D + 2.6f, right = o.D - 2.6f;
                target = Mathf.Abs(left) < Mathf.Abs(right) && left < track.HalfWidth - 1f ? left : right;
            }
            // into the corner as much as the smart steering leaves them (perfect: exactly), toward the line
            float radius = 1f / KartRace.Turn(v);
            float a = _race.Level.Assist;
            float psiTarget = Mathf.Atan2(target - k.D, look);
            float fb = (k.Psi - psiTarget) * _p.Gain;
            // a drift through a long corner: the wheel hard into it to start it, then their line with the wheel kept at
            // least a third of the way into it (the drift then follows half of the corner by itself)
            int corner = Mathf.RoundToInt(k.Distance / 40f);
            if (Mathf.Abs(kap) > 1f / 45f && corner != _driftCorner)
            {
                _driftCorner = corner;
                // from the outside half of the road (the drift's start turns the kart in a little)
                _drift = _rng.NextDouble() < _p.Drifts && k.D * Mathf.Sign(kap) < 1f;
                _driftFrom = now;
            }
            if (_drift && (Mathf.Abs(kap) < 1f / 70f || (!k.Drifting && now - _driftFrom > 0.6))) _drift = false;
            float u;
            if (_drift)
            {
                // into the corner hard (0.66) to start the drift, then the drift follows the road and the wheel steers
                // their line around where it began (further = tighter)
                float dir = kap > 0f ? -1f : 1f;
                if (!k.Drifting) u = dir * 0.66f;
                else u = dir * Mathf.Clamp(0.66f + fb * dir / KartRace.DriftSteer, 0.34f, 1f);
            }
            else u = -(1f - a) * kap * radius * _p.Anticipate + fb;
            // noise: a slow wander and jitter; a child's sudden yank
            _wander = Mathf.Clamp(_wander + Gauss() * _p.Wander * 0.35f, -_p.Wander * 2f, _p.Wander * 2f) * 0.97f;
            u += _wander + Gauss() * _p.Jitter;
            if (now < _yankUntil) u += _yank;
            else if (_rng.NextDouble() < _p.Yanks / 30.0)
            {
                _yank = (_rng.NextDouble() < 0.5 ? -1f : 1f) * 0.9f;
                _yankUntil = now + 0.5;
            }
            return Mathf.Clamp(u, -1f, 1f);
        }

        float Hash(int a, int b)
        {
            unchecked
            {
                uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(_seed * 83492791);
                h ^= h >> 13;
                h *= 0x5bd1e995;
                h ^= h >> 15;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        float Gauss()
        {
            double u1 = 1.0 - _rng.NextDouble(), u2 = _rng.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }

        /// <summary>A whole race with these players (one per slot); returns the race once it's over.</summary>
        public static KartRace Run(KartLevel level, int track, KartMode mode, Profile[] players, int seed, Action<KartRace> watch = null)
        {
            var drivers = KartCup.DrawDrivers(mode == KartMode.Versus ? 4 : 5, seed);
            var race = new KartRace(KartTracks.Get(track), level, mode, 3, drivers, seed);
            var sims = new List<KartSim>();
            for (int s = 0; s < players.Length; s++) sims.Add(new KartSim(race, s, players[s], seed * 31 + s));
            const float dt = 1f / 60f;
            int guard = 0;
            while (race.Phase != KartPhase.Over && guard++ < 60 * 400)
            {
                foreach (var sim in sims) sim.Step(dt);
                race.Step(dt);
                watch?.Invoke(race);
            }
            return race;
        }
    }
}
