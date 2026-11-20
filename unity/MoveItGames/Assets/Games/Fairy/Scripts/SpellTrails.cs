using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Fairy
{
    /// <summary>
    /// The spells' light in the garden (2026-11-16): the lightning's bolts striking down from the sky onto each creature
    /// it hits (<see cref="Strike"/>: jagged, flickering, a flash where it lands), darts of light arcing from the fairy's
    /// hand to each creature the rainbow hits (<see cref="Dart"/>: they burst there, <see cref="Arrived"/>), and the
    /// beams the fairy throws (<see cref="Beam"/>: up into the sky for the lightning). Drawn with <see cref="FairyGlows"/>.
    /// </summary>
    public sealed class SpellTrails
    {
        const float DartSeconds = 0.3f, StrikeSeconds = 0.42f;

        sealed class Dart_
        {
            public Vector3 From;
            public int Target;
            public Color Colour;
            public float Age, Delay;
            public bool Arrived;
        }

        sealed class Strike_
        {
            public Vector3 Top, At;
            public Color Colour;
            public float Age, Delay;
            public int Seed;
            public bool Landed;
        }

        sealed class Beam_
        {
            public Vector3 From, To;
            public Color Colour;
            public float Age, Seconds, Width;
        }

        readonly List<Dart_> _darts = new List<Dart_>();
        readonly List<Strike_> _strikes = new List<Strike_>();
        readonly List<Beam_> _beams = new List<Beam_>();
        public static readonly Color Gold = new Color(1f, 0.92f, 0.62f);

        /// <summary>Darts that arrived this frame: the creature's id and the colour (the game bursts sparkles there).</summary>
        public readonly List<(int creature, Color colour)> Arrived = new List<(int, Color)>();
        /// <summary>Bolts that landed this frame: where and the colour.</summary>
        public readonly List<(Vector3 at, Color colour)> Landed = new List<(Vector3, Color)>();

        /// <summary>A dart from <paramref name="from"/> (the fairy's hand) to the creature <paramref name="target"/>,
        /// leaving after <paramref name="delay"/> seconds.</summary>
        public void Dart(Vector3 from, int target, Color colour, float delay = 0f)
        {
            if (_darts.Count < 48) _darts.Add(new Dart_ { From = from, Target = target, Colour = colour, Delay = delay });
        }

        /// <summary>A lightning bolt striking down at <paramref name="at"/> from high above it, after <paramref name="delay"/>
        /// seconds.</summary>
        public void Strike(Vector3 at, Color colour, float delay = 0f)
        {
            if (_strikes.Count >= 40) return;
            var top = at + new Vector3(Random.Range(-1.2f, 1.2f), 9f, Random.Range(-0.5f, 0.5f));
            _strikes.Add(new Strike_ { Top = top, At = at, Colour = colour, Delay = delay, Seed = Random.Range(0, 1000) });
        }

        /// <summary>A straight beam of light from <paramref name="from"/> to <paramref name="to"/>, fading over
        /// <paramref name="seconds"/>.</summary>
        public void Beam(Vector3 from, Vector3 to, Color colour, float seconds = 0.3f, float width = 0.35f)
        {
            if (_beams.Count < 8) _beams.Add(new Beam_ { From = from, To = to, Colour = colour, Seconds = seconds, Width = width });
        }

        public void Clear()
        {
            _darts.Clear();
            _strikes.Clear();
            _beams.Clear();
        }

        /// <summary>Every frame: the darts (where each target creature is now: <paramref name="where"/>), the bolts, the
        /// beams.</summary>
        public void Draw(FairyGlows glows, float dt, System.Func<int, Vector3?> where)
        {
            Arrived.Clear();
            Landed.Clear();
            for (int i = _darts.Count - 1; i >= 0; i--)
            {
                var b = _darts[i];
                b.Age += dt;
                if (b.Age < b.Delay) continue;
                var to = where(b.Target);
                if (to == null)
                {
                    _darts.RemoveAt(i);
                    continue;
                }
                float k = Mathf.Clamp01((b.Age - b.Delay) / DartSeconds);
                // an arc up and over toward the creature
                var mid = Vector3.Lerp(b.From, to.Value, 0.5f) + Vector3.up * 1.4f;
                var head = Bezier(b.From, mid, to.Value, k);
                var tail = Bezier(b.From, mid, to.Value, Mathf.Max(0f, k - 0.4f));
                glows.Streak(tail, head, 0.24f, b.Colour, 0.9f);
                glows.Streak(tail, head, 0.07f, Color.white, 1f);
                glows.Glow(head, 0.7f, b.Colour, 1f);
                if (k < 1f) continue;
                if (!b.Arrived) Arrived.Add((b.Target, b.Colour));
                b.Arrived = true;
                if (b.Age > b.Delay + DartSeconds + 0.06f) _darts.RemoveAt(i);
            }
            for (int i = _strikes.Count - 1; i >= 0; i--)
            {
                var s = _strikes[i];
                s.Age += dt;
                if (s.Age < s.Delay) continue;
                float age = s.Age - s.Delay;
                if (!s.Landed) Landed.Add((s.At, s.Colour));
                s.Landed = true;
                if (age > StrikeSeconds)
                {
                    _strikes.RemoveAt(i);
                    continue;
                }
                // jagged, a new zigzag every few frames (it flickers), bright at first and fading
                float a = 1f - age / StrikeSeconds;
                a *= 0.75f + 0.25f * Mathf.Sin(age * 70f);
                var rng = new System.Random(s.Seed + (int)(age * 24f));
                const int n = 7;
                var last = s.Top;
                for (int k = 1; k <= n; k++)
                {
                    float u = k / (float)n;
                    var p = Vector3.Lerp(s.Top, s.At, u);
                    if (k < n) p += new Vector3((float)(rng.NextDouble() - 0.5) * 0.9f, 0f, (float)(rng.NextDouble() - 0.5) * 0.3f);
                    glows.Streak(last, p, 0.9f, s.Colour, 0.8f * a);
                    glows.Streak(last, p, 0.24f, Color.white, a);
                    last = p;
                }
                glows.Glow(s.At + Vector3.up * 0.3f, 3.2f * (0.6f + 0.4f * a), s.Colour, a);
                glows.Glow(s.At + Vector3.up * 0.3f, 1.2f, Color.white, a);
            }
            for (int i = _beams.Count - 1; i >= 0; i--)
            {
                var b = _beams[i];
                b.Age += dt;
                if (b.Age > b.Seconds)
                {
                    _beams.RemoveAt(i);
                    continue;
                }
                float a = 1f - b.Age / b.Seconds;
                glows.Streak(b.From, b.To, b.Width, b.Colour, 0.85f * a);
                glows.Streak(b.From, b.To, b.Width * 0.3f, Color.white, a);
            }
        }

        static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * b + t * t * c;
        }
    }
}
