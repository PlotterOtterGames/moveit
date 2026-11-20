using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MoveIt.Games.Fairy.Tests
{
    /// <summary>Spells drawn in the air (2026-11-17): the reader and the stroke cutter on simulated wands, every shape drawn
    /// as an adult and a child would (sizes, places, leaning, wobbling, the camera's noise and glitches, the way up from the
    /// hip and the arm dropping after), and moves that are no spell.</summary>
    public class SpellReaderTests
    {
        static readonly Spell[] All = { Spell.Lightning, Spell.Rainbow, Spell.Shield, Spell.Seeds, Spell.Burst, Spell.Freeze, Spell.Heal };

        /// <summary>Each shape <paramref name="tries"/> times by <paramref name="drawer"/>, through the stroke cutter: how
        /// many were read right, wrong (and as what), or not at all.</summary>
        static (float right, float wrong, float none, string table) Score(SpellDrawer drawer, int tries, int seed)
        {
            var rng = new System.Random(seed);
            int right = 0, wrong = 0, none = 0;
            var lines = new List<string>();
            foreach (var spell in All)
            {
                int r = 0, w = 0, n = 0;
                var mistaken = new Dictionary<Spell, int>();
                for (int i = 0; i < tries; i++)
                {
                    var reads = drawer.Play(spell, rng.Next(2) == 0 ? -1 : 1, rng);
                    var got = reads.FirstOrDefault(x => x.Found);
                    if (reads.Count(x => x.Found) == 0) n++;
                    else if (got.Spell == spell) r++;
                    else
                    {
                        w++;
                        mistaken[got.Spell] = mistaken.TryGetValue(got.Spell, out int c) ? c + 1 : 1;
                    }
                }
                right += r;
                wrong += w;
                none += n;
                lines.Add($"{spell,-10} right {r,3} wrong {w,3} none {n,3}  {string.Join(", ", mistaken.Select(kv => $"{kv.Key} {kv.Value}"))}");
            }
            float total = tries * All.Length;
            return (right / total, wrong / total, none / total, string.Join("\n", lines));
        }

        [Test]
        public void EveryShape_DrawnByAnAdult_IsRead()
        {
            var (right, wrong, none, table) = Score(SpellDrawer.Adult(), 60, 11);
            Debug.Log($"adult: right {right:P0}, wrong {wrong:P0}, none {none:P0}\n{table}");
            // 2026-11-17: 90 % right, 1 % wrong (a few strokes of a careful adult read as nothing)
            Assert.That(right, Is.GreaterThanOrEqualTo(0.87f), table);
            Assert.That(wrong, Is.LessThanOrEqualTo(0.03f), table);
        }

        [Test]
        public void EveryShape_DrawnByASloppyChild_IsMostlyRead()
        {
            var (right, wrong, none, table) = Score(SpellDrawer.Child(), 60, 12);
            Debug.Log($"child: right {right:P0}, wrong {wrong:P0}, none {none:P0}\n{table}");
            // 2026-11-17: 81 % right, 10 % wrong: a small wobbly spiral reads as a circle, a wobbly circle as a heart
            Assert.That(right, Is.GreaterThanOrEqualTo(0.77f), table);
            Assert.That(wrong, Is.LessThanOrEqualTo(0.13f), table);
        }

        [Test]
        public void WithTheBoxsGlitches_TheShapesAreStillRead()
        {
            var drawer = SpellDrawer.Child();
            drawer.Glitches = 0.5f;   // the full pose model's wrists: ~4 big errors a minute on the box (lite ~13)
            var (right, wrong, none, table) = Score(drawer, 40, 13);
            Debug.Log($"child with glitches: right {right:P0}, wrong {wrong:P0}, none {none:P0}\n{table}");
            // 2026-11-17: 66 % right, 14 % wrong at this rate (7x the full model's)
            Assert.That(right, Is.GreaterThanOrEqualTo(0.6f), table);
            Assert.That(wrong, Is.LessThanOrEqualTo(0.18f), table);
        }

        [Test]
        public void MovesThatAreNoShape_CastNothing()
        {
            var rng = new System.Random(5);
            int cast = 0, tries = 0;
            var what = new Dictionary<string, int>();
            void Count(string name, List<SpellRead> reads)
            {
                tries++;
                foreach (var r in reads)
                    if (r.Found)
                    {
                        cast++;
                        what[$"{name}: {r.Spell}"] = what.TryGetValue($"{name}: {r.Spell}", out int c) ? c + 1 : 1;
                        break;
                    }
            }
            var drawer = SpellDrawer.Child();
            for (int i = 0; i < 40; i++)
            {
                // pointing from one place to another, and back
                var a = new Vector2(R(rng, 300f, 1600f), R(rng, 200f, 900f));
                var b = new Vector2(R(rng, 300f, 1600f), R(rng, 200f, 900f));
                Count("line", drawer.Wand(new List<Vector2> { a, b }, rng, runUp: false, drop: rng.Next(2) == 0));
                Count("there and back", drawer.Wand(new List<Vector2> { a, b, a + (b - a) * 0.1f }, rng, runUp: false, drop: true));
                // the hand raised and lowered
                Count("raise", drawer.Wand(new List<Vector2> { new Vector2(a.x, 1060f), a }, rng, runUp: false, drop: true));
                // an L, a corner (pointing at one thing, then another)
                Count("corner", drawer.Wand(new List<Vector2> { a, new Vector2(a.x, b.y), b }, rng, runUp: false, drop: false));
                // wandering slowly about (aiming)
                var wander = new List<Vector2> { a };
                for (int k = 0; k < 6; k++) wander.Add(wander[wander.Count - 1] + new Vector2(R(rng, -90f, 90f), R(rng, -90f, 90f)));
                var slow = SpellDrawer.Child();
                slow.Speed = 110f;
                Count("wander", slow.Wand(wander, rng, runUp: false, drop: false));
            }
            float share = cast / (float)tries;
            Debug.Log($"no shape: {cast} of {tries} cast something ({share:P0}): {string.Join(", ", what.Select(kv => $"{kv.Key} {kv.Value}"))}");
            Assert.That(share, Is.LessThanOrEqualTo(0.06f));
        }

        [Test]
        public void TheLightningAndTheRainbow_LandOnTheSideTheyreDrawnOn()
        {
            var rng = new System.Random(3);
            var drawer = SpellDrawer.Adult();
            foreach (var spell in new[] { Spell.Lightning, Spell.Rainbow })
            foreach (int side in new[] { -1, 1 })
            {
                var read = drawer.Play(spell, side, rng).First(r => r.Found);
                Assert.That(read.Spell, Is.EqualTo(spell));
                Assert.That(Mathf.Sign(read.Centre.x - 960f), Is.EqualTo((float)side), $"{spell} on {side}: {read}");
            }
        }

        [Test]
        public void AStrokeStoppedHalfway_JoinsTheRest()
        {
            // a child stops in the middle of the circle (longer than a rest), then goes on: one shield, not a fizzle
            var caster = new SpellCaster();
            var circle = SpellShapes.PathOf(Spell.Shield, 1920f);
            double t = 0;
            Vector2 at = circle[0];
            SpellCaster.Outcome Step(Vector2 p, bool on = true)
            {
                t += 1 / 60.0;
                return caster.Update(t, on, p);
            }
            var outcomes = new List<SpellCaster.Outcome>();
            for (int i = 0; i < 20; i++) outcomes.Add(Step(at));
            int half = circle.Length / 2;
            float speed = 600f;
            foreach (var (from, to, stop) in new[] { (0, half, 0.33f), (half, circle.Length - 1, 0.5f) })
            {
                for (int i = from; i < to; i++)
                {
                    float seconds = Vector2.Distance(circle[i], circle[i + 1]) / speed;
                    for (float s = 0f; s < seconds; s += 1 / 60f) outcomes.Add(Step(Vector2.Lerp(circle[i], circle[i + 1], s / seconds)));
                }
                for (float s = 0f; s < stop; s += 1 / 60f) outcomes.Add(Step(circle[to]));
            }
            Assert.That(outcomes.Count(o => o == SpellCaster.Outcome.Fizzled), Is.EqualTo(0));
            Assert.That(outcomes.Count(o => o == SpellCaster.Outcome.Read), Is.EqualTo(1));
            Assert.That(caster.LastRead.Spell, Is.EqualTo(Spell.Shield), caster.LastRead.ToString());
        }

        [Test]
        public void OnlyTheAllowedSpells_AreRead()
        {
            var rng = new System.Random(8);
            var drawer = SpellDrawer.Adult();
            drawer.Allowed = SpellReader.All & ~SpellReader.Bit(Spell.Freeze) & ~SpellReader.Bit(Spell.Heal);
            for (int i = 0; i < 10; i++)
            {
                Assert.IsFalse(drawer.Play(Spell.Heal, 1, rng).Any(r => r.Found && r.Spell == Spell.Heal));
                Assert.IsFalse(drawer.Play(Spell.Freeze, 1, rng).Any(r => r.Found && r.Spell == Spell.Freeze));
            }
        }

        static float R(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);
    }

    /// <summary>
    /// A simulated hand drawing spells with the wand (HUD pixels, 1920 x 1080, y down): a shape (the game's own guide,
    /// or another way people draw it: a star in five lines, a Z for the bolt, a circle from anywhere either way) at a size
    /// and place, leaned and stretched, drawn at <see cref="Speed"/> (pixels a second, slowing in corners) with a slow
    /// wobble (<see cref="Wobble"/> px) and the camera's noise (<see cref="Noise"/> px a sample, 30 samples a second, then
    /// the wand's own smoothing), now and then the box's glitches (<see cref="Glitches"/> a second: the hand thrown
    /// 100-200 px for one or two samples); the wand comes up from the bottom of the screen to the start (the way up from
    /// the hip; a stop there or not), and after the shape it rests or the arm drops straight away.
    /// </summary>
    public sealed class SpellDrawer
    {
        public float Speed, Wobble, Noise, Lean, Stretch, SizeMin, SizeMax, Glitches, StopFirst, DropAtOnce;
        public int Allowed = SpellReader.All;

        public static SpellDrawer Adult() => new SpellDrawer
        {
            Speed = 750f, Wobble = 10f, Noise = 4f, Lean = 10f, Stretch = 0.12f, SizeMin = 0.8f, SizeMax = 1.25f, StopFirst = 0.6f, DropAtOnce = 0.3f,
        };

        public static SpellDrawer Child() => new SpellDrawer
        {
            Speed = 420f, Wobble = 22f, Noise = 7f, Lean = 18f, Stretch = 0.25f, SizeMin = 0.6f, SizeMax = 1.4f, StopFirst = 0.3f, DropAtOnce = 0.6f,
        };

        static float R(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

        /// <summary>The shape as this hand draws it (HUD pixels).</summary>
        public List<Vector2> Shape(Spell spell, int side, System.Random rng)
        {
            List<Vector2> path;
            int variant = rng.Next(3);
            switch (spell)
            {
                case Spell.Burst when variant > 0:
                {
                    // five lines, from any point
                    int s = rng.Next(5);
                    var pts = new List<Vector2>();
                    for (int i = 0; i <= 5; i++)
                    {
                        float a = (90f + (s + i * 2) * 72f) * Mathf.Deg2Rad;
                        pts.Add(new Vector2(960f + Mathf.Cos(a) * 190f, 480f - Mathf.Sin(a) * 190f));
                    }
                    path = new List<Vector2>(pts);
                    break;
                }
                case Spell.Lightning when variant == 2:
                    // a Z
                    path = new List<Vector2> { new Vector2(-150f, -170f), new Vector2(150f, -170f), new Vector2(-150f, 170f), new Vector2(150f, 170f) }
                        .Select(p => p + new Vector2(960f + side * 520f, 620f)).ToList();
                    break;
                case Spell.Shield:
                {
                    // round from anywhere, either way, a little short or past
                    float start = R(rng, 0f, 360f) * Mathf.Deg2Rad, dir = rng.Next(2) == 0 ? 1f : -1f, turns = R(rng, 0.9f, 1.1f);
                    path = new List<Vector2>();
                    for (int i = 0; i <= 64; i++)
                    {
                        float a = start + dir * i / 64f * turns * Mathf.PI * 2f;
                        path.Add(new Vector2(960f, 600f) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 200f);
                    }
                    break;
                }
                default:
                    path = new List<Vector2>(SpellShapes.PathOf(new SpellOffer(spell, SpellOffer.Sided(spell) ? side : 0), 1920f));
                    break;
            }
            if (rng.Next(2) == 0 && !SpellOffer.Sided(spell)) path.Reverse();
            // a size, a lean, a stretch, a place
            var c = Vector2.zero;
            foreach (var p in path) c += p;
            c /= path.Count;
            float size = R(rng, SizeMin, SizeMax), lean = R(rng, -Lean, Lean) * Mathf.Deg2Rad, sx = 1f + R(rng, -Stretch, Stretch);
            float cos = Mathf.Cos(lean), sin = Mathf.Sin(lean);
            var shift = new Vector2(R(rng, -120f, 120f), R(rng, -80f, 80f));
            if (SpellOffer.Sided(spell)) shift.x = side * Mathf.Abs(shift.x) * 0.5f;
            var output = new List<Vector2>();
            foreach (var p in path)
            {
                var d = (p - c) * size;
                d.x *= sx;
                d = new Vector2(d.x * cos - d.y * sin, d.x * sin + d.y * cos);
                var q = c + d + shift;
                output.Add(new Vector2(Mathf.Clamp(q.x, 30f, 1890f), Mathf.Clamp(q.y, 30f, 1050f)));
            }
            return output;
        }

        /// <summary><paramref name="spell"/> drawn with the wand into a fresh <see cref="SpellCaster"/>: what it read.</summary>
        public List<SpellRead> Play(Spell spell, int side, System.Random rng) =>
            Wand(Shape(spell, side, rng), rng, runUp: true, drop: rng.NextDouble() < DropAtOnce);

        /// <summary>The wand along <paramref name="shape"/>: up from the bottom first (<paramref name="runUp"/>), then the
        /// shape, then a rest or the arm dropping at once (<paramref name="drop"/>), into a fresh caster.</summary>
        public List<SpellRead> Wand(List<Vector2> shape, System.Random rng, bool runUp, bool drop, SpellCaster caster = null)
        {
            // the hand's path on the camera's clock: (time, point, on) at 30 samples a second
            var keys = new List<(Vector2 p, float speed, float hold)>();
            if (runUp)
            {
                keys.Add((new Vector2(shape[0].x + R(rng, -200f, 200f), 1080f), 1000f, 0f));
                keys.Add((shape[0], Speed, rng.NextDouble() < StopFirst ? R(rng, 0.3f, 0.6f) : 0f));
            }
            else keys.Add((shape[0], Speed, 0.4f));
            for (int i = 1; i < shape.Count; i++) keys.Add((shape[i], Speed, 0f));
            if (drop) keys.Add((new Vector2(shape[shape.Count - 1].x + R(rng, -60f, 60f), 1110f), 1500f, 0f));
            else keys[keys.Count - 1] = (keys[keys.Count - 1].p, keys[keys.Count - 1].speed, R(rng, 0.4f, 0.7f));
            var samples = new List<(double t, Vector2 p, bool on)>();
            double t = 0;
            float wobblePhase = R(rng, 0f, 10f), wobbleRate = R(rng, 0.6f, 1.4f);
            void Sample(Vector2 p)
            {
                var wob = new Vector2(Mathf.Sin(wobblePhase + (float)t * wobbleRate * 6.28f), Mathf.Cos(wobblePhase * 1.3f + (float)t * wobbleRate * 4.1f)) * Wobble;
                var noise = new Vector2(Gauss(rng), Gauss(rng)) * Noise;
                var q = p + wob + noise;
                samples.Add((t, q, q.y < 1085f));
                t += 1 / 30.0;
            }
            // before the run-up the hand is down (the wand isn't up yet); else it starts where it rests
            if (runUp)
                for (int i = 0; i < 8; i++)
                {
                    samples.Add((t, new Vector2(keys[0].p.x, 1200f), false));
                    t += 1 / 30.0;
                }
            else
                for (int i = 0; i < 8; i++) Sample(keys[0].p);
            for (int k = 1; k < keys.Count; k++)
            {
                var (a, b) = (keys[k - 1].p, keys[k].p);
                // slower into a corner of the shape
                float speed = keys[k].speed;
                if (k + 1 < keys.Count)
                {
                    var d0 = b - a;
                    var d1 = keys[k + 1].p - b;
                    if (d0.sqrMagnitude > 1f && d1.sqrMagnitude > 1f && Vector2.Angle(d0, d1) > 60f) speed *= 0.75f;
                }
                float seconds = Vector2.Distance(a, b) / Mathf.Max(1f, speed);
                double end = t + seconds;
                double begin = t;
                while (t < end) Sample(Vector2.Lerp(a, b, (float)((t - begin) / Math.Max(1e-6, seconds))));
                for (float h = 0f; h < keys[k].hold; h += 1 / 30f) Sample(b);
            }
            for (int i = 0; i < 30; i++) samples.Add((t += 1 / 30.0, new Vector2(960f, 1200f), false));
            // the box's glitches: a sample thrown 100-200 px, sometimes two in a row
            if (Glitches > 0f)
                for (int i = 0; i < samples.Count; i++)
                    if (samples[i].on && rng.NextDouble() < Glitches / 30.0)
                    {
                        var jump = new Vector2(Gauss(rng), Gauss(rng)).normalized * R(rng, 100f, 200f);
                        int len = rng.Next(3) == 0 ? 2 : 1;
                        for (int j = i; j < Mathf.Min(samples.Count, i + len); j++) samples[j] = (samples[j].t, samples[j].p + jump, samples[j].on);
                    }
            // the game: 60 frames a second, the wand between samples and smoothed a little
            caster ??= new SpellCaster();
            caster.Allowed = Allowed;
            var reads = new List<SpellRead>();
            Vector2 shown = samples[0].p;
            int si = 0;
            for (double now = 0; now < t; now += 1 / 60.0)
            {
                while (si + 1 < samples.Count && samples[si + 1].t <= now) si++;
                var s = samples[si];
                var next = si + 1 < samples.Count ? samples[si + 1] : s;
                float k = next.t > s.t ? (float)((now - s.t) / (next.t - s.t)) : 0f;
                var target = Vector2.Lerp(s.p, next.p, Mathf.Clamp01(k));
                shown = Vector2.Lerp(shown, target, 0.55f);
                bool on = s.on;
                var outcome = caster.Update(now, on, new Vector2(Mathf.Clamp(shown.x, -60f, 1980f), Mathf.Clamp(shown.y, -30f, 1110f)));
                if (outcome == SpellCaster.Outcome.Read) reads.Add(caster.LastRead);
            }
            return reads;
        }

        static float Gauss(System.Random rng)
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2));
        }
    }
}
