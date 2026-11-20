using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Fairy
{
    /// <summary>
    /// Zana!'s drawn magic on the HUD (2026-11-17, the user: "I want the countdown and the spell trail to be nicely designed
    /// and visible"):
    /// <list type="bullet">
    /// <item><b>The trail</b>: the stroke being drawn as a ribbon of light in the wand's colour (a soft halo, a glow, a white
    /// core; thin at its tail, thick at the wand; its colour drifting along it; stars twinkling on it). While the wand still
    /// charges it is a thin grey thread: no magic in it yet.</item>
    /// <item><b>What it became</b>: read as a spell, the trail turns into the spell's clean shape in the spell's colours (the
    /// rainbow's in all of them), flashes, swells and fades with a burst of stars, the spell's picture and name popping up
    /// over it. Read as nothing, it goes grey and sinks away ("Provo sërish!"); drawn while the wand charged or for a spell
    /// not back yet, grey with what to wait for.</item>
    /// <item><b>The countdown</b> round the wand while it charges: twelve stars round its star filling clockwise, the
    /// wand dimmed, the seconds left on a bubble beside it; charged, a ring of light bursts out with stars and a chime.</item>
    /// <item><b>The spell book</b> along the bottom left: each spell the fairy has, its picture on a medallion and the shape to
    /// draw for it under it; dim and filling while one comes back (the big magic with the crystal); the one that would help
    /// most now glows and grows.</item>
    /// <item><b>A hint</b>: a player who hasn't drawn for a while (<see cref="HintAfter"/>) sees the helpful spell's shape
    /// faintly where it goes, with a light running along it the way to draw it.</item>
    /// </list>
    /// All in the HUD's batch (one draw call): the ribbon is stretched, turned glow pictures.
    /// </summary>
    public sealed partial class FairyGame
    {
        static readonly Spell[] Book = { Spell.Lightning, Spell.Rainbow, Spell.Shield, Spell.Seeds, Spell.Burst, Spell.Freeze, Spell.Heal };
        static readonly Dictionary<Spell, Vector2[]> Glyphs = new Dictionary<Spell, Vector2[]>();
        readonly List<Vector2> _ribbon = new List<Vector2>(256);
        readonly Vector2[][] _morphFrom = new Vector2[2][], _morphTo = new Vector2[2][];
        readonly int[] _shownSeconds = { -1, -1 };
        readonly float[] _secondsPop = new float[2], _lastDrawn = new float[2];
        readonly string[] _secondsText = new string[2];

        /// <summary>Seconds without drawing before the hint shows (a child on Easy sooner).</summary>
        float HintAfter => _run != null && _run.Difficulty == FairyDifficulty.Easy ? 4f : 6f;

        // ------------------------------------------------ helpers

        /// <summary>A picture turned <paramref name="degrees"/> about its centre (the batch only; plain IMGUI draws it
        /// straight).</summary>
        void PictureTurned(Rect r, string name, Color tint, float degrees)
        {
            if (_pass == HudPass.Pictures)
            {
                if (_sheet != null && _sheet.TryGetValue(name, out var uv)) _hud.Image(r, uv, tint, degrees);
            }
            else if (_pass == HudPass.All) Picture(r, name, tint);
        }

        static Color Shift(Color c, float hue)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            var o = Color.HSVToRGB(Mathf.Repeat(h + hue, 1f), s, v);
            o.a = c.a;
            return o;
        }

        static Color A(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);

        /// <summary><paramref name="pts"/> as points <paramref name="spacing"/> apart into <paramref name="into"/>.</summary>
        static void Even(IReadOnlyList<Vector2> pts, float spacing, List<Vector2> into, int max = 240)
        {
            into.Clear();
            if (pts == null || pts.Count == 0) return;
            float length = 0f;
            for (int i = 1; i < pts.Count; i++) length += Vector2.Distance(pts[i - 1], pts[i]);
            spacing = Mathf.Max(spacing, length / max);
            into.Add(pts[0]);
            float carry = 0f;
            for (int i = 1; i < pts.Count; i++)
            {
                var a = pts[i - 1];
                var b = pts[i];
                float seg = Vector2.Distance(a, b);
                float at = spacing - carry;
                while (at <= seg)
                {
                    into.Add(Vector2.Lerp(a, b, at / seg));
                    at += spacing;
                }
                carry = seg - (at - spacing);
            }
            if (Vector2.Distance(into[into.Count - 1], pts[pts.Count - 1]) > 1f) into.Add(pts[pts.Count - 1]);
        }

        static Vector2[] EvenArray(IReadOnlyList<Vector2> pts, int n)
        {
            var output = new Vector2[n];
            SpellReader.Resample(pts, 0, pts.Count, output);
            return output;
        }

        /// <summary>
        /// A ribbon of light through <paramref name="pts"/>: per piece a soft halo, a glow and a white core, stretched
        /// glow pictures turned along the line; <paramref name="width"/> at its head (the last point), 45 % at its tail;
        /// the colour of each piece from <paramref name="colourAt"/> (0 tail .. 1 head), all times <paramref name="alpha"/>.
        /// </summary>
        void Ribbon(IReadOnlyList<Vector2> pts, float width, System.Func<float, Color> colourAt, float alpha, float core = 1f, float taper = 0.45f)
        {
            Even(pts, 11f, _ribbon);
            int n = _ribbon.Count;
            if (n < 2) return;
            for (int layer = 0; layer < 4; layer++)
                for (int i = 1; i < n; i++)
                {
                    var a = _ribbon[i - 1];
                    var b = _ribbon[i];
                    float t = i / (float)(n - 1);
                    float w = width * Mathf.Lerp(taper, 1f, t);
                    float len = Vector2.Distance(a, b);
                    float deg = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
                    var mid = (a + b) * 0.5f;
                    var c = colourAt(t);
                    Rect R(float along, float across) => new Rect(mid.x - along * 0.5f, mid.y - across * 0.5f, along, across);
                    switch (layer)
                    {
                        case 0:
                            // a dark violet glow under it: the light shows on the bright meadow and sky too
                            PictureTurned(R(len + w * 2.2f, w * 2.4f), "glow", new Color(0.16f, 0.06f, 0.3f, 0.32f * alpha), deg);
                            break;
                        case 1:
                            PictureTurned(R(len + w * 3.4f, w * 3.6f), "glow", A(c, 0.42f * alpha), deg);
                            break;
                        case 2:
                            // a solid band of the colour (stretched dots: round ends, no gaps)
                            PictureTurned(R(len + w * 0.75f, w * 0.75f), "dot", A(Color.Lerp(c, Color.white, 0.15f), 0.92f * alpha), deg);
                            break;
                        default:
                            if (core > 0f) PictureTurned(R(len + w * 0.32f, w * 0.3f), "dot", A(Color.white, Mathf.Min(1f, alpha * core)), deg);
                            break;
                    }
                }
        }

        /// <summary>Stars twinkling along the ribbon last laid out (every fourth piece, a little off it).</summary>
        void Sparkles(Color colour, float alpha, float seed)
        {
            int n = _ribbon.Count;
            for (int i = 2; i < n; i += 4)
            {
                float k = i * 1.618f + seed;
                float tw = 0.5f + 0.5f * Mathf.Sin(Time.time * 9f + k * 3.1f);
                var off = new Vector2(Mathf.Sin(k * 2.3f), Mathf.Cos(k * 1.7f)) * 16f;
                float size = (10f + 16f * tw) * Mathf.Lerp(0.6f, 1f, i / (float)n);
                var p = _ribbon[i] + off;
                Picture(new Rect(p.x - size, p.y - size, size * 2f, size * 2f), "star", A(Color.Lerp(colour, Color.white, 0.5f), alpha * (0.4f + 0.6f * tw)));
            }
        }

        // ------------------------------------------------ the stroke being drawn

        void DrawStroke(int k)
        {
            var c = _humans[k].Caster;
            if (!c.Drawing || c.Stroke.Count < 2) return;
            if (_pass == HudPass.Words) return;
            if (_run.Charged(k))
            {
                var col = WandColour(k, Element);
                float seed = k * 7.3f;
                Ribbon(c.Stroke, 30f, t => Shift(col, (t - 0.5f) * 0.14f + 0.03f * Mathf.Sin(Time.time * 3f + t * 6f + seed)), 1f);
                Sparkles(col, 0.9f, seed);
                // the head: a bright star where the wand is drawing
                var head = c.Stroke[c.Stroke.Count - 1];
                float s = 34f + 7f * Mathf.Sin(Time.time * 14f);
                Picture(new Rect(head.x - s * 1.8f, head.y - s * 1.8f, s * 3.6f, s * 3.6f), "glow", A(col, 0.8f));
                Picture(new Rect(head.x - s * 0.7f, head.y - s * 0.7f, s * 1.4f, s * 1.4f), "glow", Color.white);
            }
            else
            {
                // no magic in it yet: a thin grey thread
                Ribbon(c.Stroke, 14f, t => new Color(0.66f, 0.64f, 0.8f), 0.6f, 0.6f);
            }
        }

        // ------------------------------------------------ what the last stroke became

        /// <summary>Called when a stroke is read (<see cref="Feedback"/>): the morph's two ends, evenly along.</summary>
        void PrepareMorph(int k)
        {
            var path = _readPath[k];
            _morphFrom[k] = path != null && path.Length >= 2 ? EvenArray(path, 72) : null;
            _morphTo[k] = _readGuide[k] != null && _readGuide[k].Length >= 2 ? EvenArray(_readGuide[k], 72) : null;
        }

        static readonly Vector2[] Burst8 =
        {
            new Vector2(1f, 0f), new Vector2(0.71f, 0.71f), new Vector2(0f, 1f), new Vector2(-0.71f, 0.71f),
            new Vector2(-1f, 0f), new Vector2(-0.71f, -0.71f), new Vector2(0f, -1f), new Vector2(0.71f, -0.71f),
        };
        readonly List<Vector2> _morph = new List<Vector2>(80);

        void DrawRead(float w, int k)
        {
            var from = _morphFrom[k];
            if (from == null) return;
            float a = _readAge[k];
            var kind = _readKind[k];
            float duration = kind == ReadKind.Cast ? 1.25f : kind == ReadKind.NotReady ? 1.4f : 1f;
            if (a >= duration) return;
            Vector2 min = from[0], max = from[0];
            foreach (var p in from)
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            var centre = (min + max) * 0.5f;
            var spell = _readOffer[k].Spell;
            if (kind == ReadKind.Cast && _morphTo[k] != null)
            {
                var to = _morphTo[k];
                // the trail turns into the spell's clean shape, flashes, swells a little and fades
                float m = Mathf.SmoothStep(0f, 1f, a / 0.22f);
                float swell = 1f + 0.12f * Mathf.Max(0f, a - 0.3f);
                float fade = a < 0.5f ? 1f : 1f - (a - 0.5f) / (duration - 0.5f);
                _morph.Clear();
                for (int i = 0; i < from.Length; i++)
                {
                    var p = Vector2.Lerp(from[i], to[i], m);
                    _morph.Add(centre + (p - centre) * swell);
                }
                if (_pass != HudPass.Words)
                {
                    var sc = FairyLooks.SpellColour(spell);
                    bool rainbow = spell == Spell.Rainbow;
                    float flash = Mathf.Clamp01(1f - Mathf.Abs(a - 0.26f) / 0.12f);
                    Ribbon(_morph, 32f + 14f * flash, t => rainbow ? Color.HSVToRGB(Mathf.Repeat(t * 0.85f, 1f), 0.6f, 1f) : Shift(sc, (t - 0.5f) * 0.08f), fade, 1f + flash, 0.8f);
                    Sparkles(sc, fade, k * 3.1f);
                    // a burst of stars from its middle
                    if (a > 0.2f)
                    {
                        float b = (a - 0.2f) / (duration - 0.2f);
                        float r = 60f + 340f * Mathf.Sqrt(b);
                        for (int i = 0; i < 8; i++)
                        {
                            var p = centre + Burst8[i] * r * (0.8f + 0.25f * ((i * 37) % 5) / 5f);
                            float s = 26f * (1f - b);
                            Picture(new Rect(p.x - s, p.y - s, s * 2f, s * 2f), "star", A(rainbow ? Color.HSVToRGB(i / 8f, 0.5f, 1f) : Color.Lerp(sc, Color.white, 0.4f), 1f - b));
                        }
                    }
                }
                // its picture and name pop up over it
                float pop = a < 0.06f ? 0f : 1f + 0.35f * Mathf.Exp(-(a - 0.06f) * 9f) * Mathf.Cos((a - 0.06f) * 20f);
                if (pop > 0f) Label(w, k, spell, centre, min.y, max.y, FairyStrings.SpellName(spell), FairyLooks.SpellColour(spell), fade, pop, true);
                return;
            }
            // read as nothing, too soon, or a spell not back yet: the stroke goes grey and sinks away
            float f = a / duration;
            if (_pass != HudPass.Words)
            {
                _morph.Clear();
                float sink = 110f * f * f;
                foreach (var p in from) _morph.Add(p + new Vector2(0f, sink));
                Ribbon(_morph, 16f * (1f - 0.5f * f), t => new Color(0.6f, 0.58f, 0.68f), 0.8f * (1f - f), 0.5f, 0.8f);
                // a few puffs
                for (int i = 0; i < 5; i++)
                {
                    var p = _morph[Mathf.Min(_morph.Count - 1, i * _morph.Count / 5)] + new Vector2(0f, -40f * f);
                    float s = 18f + 40f * f;
                    Picture(new Rect(p.x - s, p.y - s, s * 2f, s * 2f), "glow", new Color(0.7f, 0.68f, 0.78f, 0.35f * (1f - f)));
                }
            }
            string text;
            Color colour;
            switch (kind)
            {
                case ReadKind.Wait:
                    text = FairyStrings.Wait;
                    colour = new Color(0.8f, 0.85f, 1f);
                    break;
                case ReadKind.NotReady:
                    text = spell == Spell.Burst ? FairyStrings.CrystalNotFull : FairyStrings.ComesBack(spell, Mathf.CeilToInt(_run.Cooldown(spell)));
                    colour = new Color(0.85f, 0.85f, 0.95f);
                    break;
                default:
                    text = FairyStrings.TryAgain;
                    colour = Color.white;
                    break;
            }
            float alpha = a < 0.8f * duration ? 1f : (duration - a) / (0.2f * duration);
            float grow = 1f + 0.25f * Mathf.Exp(-a * 10f);
            Label(w, k, kind == ReadKind.Fizzle ? (Spell?)null : spell, centre, min.y, max.y, text, colour, alpha, grow, false);
        }

        /// <summary>A label over a stroke (under it near the top of the screen): the spell's picture (dim when it isn't
        /// cast) and a word.</summary>
        void Label(float w, int k, Spell? spell, Vector2 centre, float top, float bottom, string text, Color colour, float alpha, float grow, bool cast)
        {
            float y = top > 230f ? top - 70f : Mathf.Min(GameHud.Height - 160f, bottom + 64f);
            float half = 70f + 12f * text.Length;
            float x = Mathf.Clamp(centre.x, half + 20f, w - half - 20f);
            float icon = 76f * grow;
            if (spell.HasValue)
            {
                var r = new Rect(x - half - icon * 0.1f, y - icon * 0.5f, icon, icon);
                if (cast) Picture(new Rect(r.x - icon * 0.4f, r.y - icon * 0.4f, icon * 1.8f, icon * 1.8f), "glow", A(colour, 0.6f * alpha));
                Picture(r, FairyArt.SpellSprite(spell.Value), new Color(1f, 1f, 1f, (cast ? 1f : 0.45f) * alpha));
            }
            float left = spell.HasValue ? x - half + icon * 0.95f : x - half;
            Words(new Rect(left, y - 40f, 2f * half, 80f), text, 44, A(colour, alpha), spell.HasValue ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, 3.5f, grow);
        }

        // ------------------------------------------------ the countdown round the wand

        /// <summary>Under the wand: while it charges, twelve stars round its star filling clockwise; charged, a ring of
        /// light bursting out. False: the wand is charged (draw it bright).</summary>
        bool DrawCharge(int k, Vector2 px)
        {
            float left = _run.RechargeOf(k), total = Mathf.Max(0.1f, _run.RechargeTime);
            var col = WandColour(k, Element);
            if (left > 0f)
            {
                // a stroke drawn too soon makes it pulse
                bool nudged = _readKind[k] == ReadKind.Wait && _readAge[k] < 0.6f;
                float scale = nudged ? 1f + 0.2f * Mathf.Sin(_readAge[k] * 30f) * (1f - _readAge[k] / 0.6f) : 1f;
                float f = 1f - left / total;
                const int pips = 12;
                const float radius = 92f;
                if (_pass != HudPass.Words)
                {
                    Picture(new Rect(px.x - radius * 1.45f * scale, px.y - radius * 1.45f * scale, radius * 2.9f * scale, radius * 2.9f * scale), "glow", new Color(0.12f, 0.08f, 0.25f, 0.35f));
                    for (int i = 0; i < pips; i++)
                    {
                        float ang = (-90f + i * 360f / pips) * Mathf.Deg2Rad;
                        var p = px + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * radius * scale;
                        float lit = Mathf.Clamp01(f * pips - i);
                        // twelve little stars, dark until they fill gold one by one
                        if (lit >= 1f)
                        {
                            Picture(new Rect(p.x - 26f, p.y - 26f, 52f, 52f), "glow", A(col, 0.6f));
                            Picture(new Rect(p.x - 16f, p.y - 16f, 32f, 32f), "star", Color.white);
                        }
                        else
                        {
                            Picture(new Rect(p.x - 14f, p.y - 14f, 28f, 28f), "star_empty", new Color(1f, 1f, 1f, 0.9f));
                            if (lit > 0f) Picture(new Rect(p.x - 16f * lit, p.y - 16f * lit, 32f * lit, 32f * lit), "star", A(Color.white, lit));
                        }
                    }
                }
                // the seconds left on a bubble up and right of the star
                int sec = Mathf.CeilToInt(left);
                if (sec != _shownSeconds[k] || _secondsText[k] == null)
                {
                    _shownSeconds[k] = sec;
                    _secondsText[k] = sec.ToString();
                    _secondsPop[k] = Time.time;
                }
                float popped = 1f + 0.35f * Mathf.Exp(-(Time.time - _secondsPop[k]) * 9f);
                var bubble = px + new Vector2(radius * 0.95f, -radius * 0.95f) * scale;
                float bs = 58f * popped;
                Picture(new Rect(bubble.x - bs * 0.5f, bubble.y - bs * 0.5f, bs, bs), "medallion", Color.white);
                Words(new Rect(bubble.x - 40f, bubble.y - 36f, 80f, 72f), _secondsText[k], 40, Gold, TextAnchor.MiddleCenter, 3f, popped);
                return true;
            }
            _shownSeconds[k] = -1;
            float c = _chargedAge[k];
            if (c < 0.6f && _pass != HudPass.Words)
            {
                // charged: a ring of light bursts out, stars fly
                float b = c / 0.6f;
                float s = 70f + 240f * b;
                Picture(new Rect(px.x - s, px.y - s, s * 2f, s * 2f), "ring", A(Color.Lerp(col, Color.white, 0.4f), 1f - b));
                Picture(new Rect(px.x - s * 0.7f, px.y - s * 0.7f, s * 1.4f, s * 1.4f), "glow", A(col, 0.6f * (1f - b)));
                for (int i = 0; i < 8; i++)
                {
                    var p = px + Burst8[i] * (40f + 200f * b);
                    float z = 22f * (1f - b);
                    Picture(new Rect(p.x - z, p.y - z, z * 2f, z * 2f), "star", A(Color.Lerp(col, Color.white, 0.5f), 1f - b));
                }
            }
            return false;
        }

        // ------------------------------------------------ the spell book

        static Vector2[] GlyphOf(Spell s)
        {
            if (Glyphs.TryGetValue(s, out var g)) return g;
            var path = SpellShapes.PathOf(new SpellOffer(s, SpellOffer.Sided(s) ? 1 : 0), 1920f);
            Vector2 min = path[0], max = path[0];
            foreach (var p in path)
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            var size = Vector2.Max(max - min, Vector2.one);
            float scale = 1f / Mathf.Max(size.x, size.y);
            var even = EvenArray(path, s == Spell.Burst || s == Spell.Freeze || s == Spell.Heal ? 34 : 24);
            g = new Vector2[even.Length];
            // in a unit box round its middle, as wide as it is
            for (int i = 0; i < even.Length; i++) g[i] = (even[i] - (min + max) * 0.5f) * scale;
            Glyphs[s] = g;
            return g;
        }

        /// <summary>The spell book along the bottom left (the camera's corner mirror is bottom right).</summary>
        void DrawSpellBook(float w)
        {
            const float icon = 80f, gap = 18f;
            float x = 36f, y = GameHud.Height - icon - 96f;
            var tint = FairyLooks.ElementColour(Element);
            var s0 = _run.Suggest(0);
            var s1 = Players > 1 ? _run.Suggest(1) : null;
            foreach (var s in Book)
            {
                if (!_run.Has(s)) continue;
                bool burst = s == Spell.Burst;
                float total = GardenLevel.CooldownOf(s, _run.Level);
                float fill = burst ? Mathf.Clamp01(_run.Magic) : total > 0f ? 1f - Mathf.Clamp01(_run.Cooldown(s) / total) : 1f;
                bool ready = fill >= 1f;
                bool suggested = ready && (s0.HasValue && s0.Value.Spell == s || s1.HasValue && s1.Value.Spell == s);
                var sc = FairyLooks.SpellColour(s);
                float pulse = suggested ? 1.1f + 0.06f * Mathf.Sin(Time.time * 6f) : 1f;
                var c = new Vector2(x + icon * 0.5f, y + icon * 0.5f);
                float size = icon * pulse;
                var r = new Rect(c.x - size * 0.5f, c.y - size * 0.5f, size, size);
                if (suggested) Picture(new Rect(r.x - 26f, r.y - 26f, r.width + 52f, r.height + 52f), "glow", A(sc, 0.8f));
                else if (ready) Picture(new Rect(r.x - 12f, r.y - 12f, r.width + 24f, r.height + 24f), "glow", A(tint, 0.35f));
                Picture(new Rect(r.x - 4f, r.y - 4f, r.width + 8f, r.height + 8f), "medallion", new Color(1f, 1f, 1f, ready ? 1f : 0.8f));
                Picture(new Rect(r.x + 11f * pulse, r.y + 11f * pulse, r.width - 22f * pulse, r.height - 22f * pulse), FairyArt.SpellSprite(s), ready ? Color.white : new Color(1f, 1f, 1f, 0.4f));
                if (!ready)
                {
                    // filling from below as it comes back (the big magic: the crystal), the seconds over it
                    Picture(new Rect(r.x + 9f, r.yMax - 9f - (r.height - 18f) * fill, r.width - 18f, 5f), "white", A(burst ? new Color(1f, 0.6f, 0.95f) : tint, 0.95f));
                    if (!burst) Words(new Rect(r.x, r.y, r.width, r.height), Mathf.CeilToInt(_run.Cooldown(s)).ToString(), 34, Color.white, TextAnchor.MiddleCenter, 3f);
                }
                // the shape to draw for it, on a dark pill under it
                var pill = new Rect(c.x - icon * 0.5f - 4f, y + icon + 8f, icon + 8f, 58f);
                Picture(pill, "capsule", new Color(0.12f, 0.08f, 0.24f, suggested ? 0.85f : 0.65f));
                if (_pass != HudPass.Words)
                {
                    var g = GlyphOf(s);
                    var gc = new Vector2(pill.center.x, pill.center.y);
                    float gs = 64f;
                    var col = suggested ? Color.Lerp(sc, Color.white, 0.3f) : new Color(1f, 1f, 1f, ready ? 0.9f : 0.45f);
                    // a light running along it the way to draw it, on the suggested one
                    int head = suggested ? Mathf.FloorToInt(Mathf.Repeat(Time.time * 0.8f, 1f) * g.Length) : -1;
                    for (int i = 0; i < g.Length; i++)
                    {
                        var p = gc + new Vector2(g[i].x, g[i].y) * gs * new Vector2(1f, 0.72f);
                        float d = 3.4f;
                        if (head >= 0 && Mathf.Abs(i - head) < 3) d = 5.5f;
                        Picture(new Rect(p.x - d, p.y - d, d * 2f, d * 2f), "dot", col);
                    }
                }
                x += icon + gap;
            }
        }

        // ------------------------------------------------ the hint

        /// <summary>A player who hasn't drawn for a while: the helpful spell's shape faintly where it goes, a light running
        /// along it the way to draw it, its picture at its start.</summary>
        void DrawHint(float w, int k)
        {
            if (Autoplay || Current != Phase.Playing || _run.Current != GardenRun.Stage.Wave) return;
            var c = _humans[k].Caster;
            if (c.Drawing || !_run.Charged(k)) return;
            float idle = _runSeconds - _lastDrawn[k];
            if (idle < HintAfter) return;
            var s = _run.Suggest(k);
            if (!s.HasValue || !_run.Ready(s.Value.Spell)) return;
            float appear = Mathf.Clamp01((idle - HintAfter) / 0.6f);
            var path = HintPath(k, s.Value, w);
            var col = FairyLooks.SpellColour(s.Value.Spell);
            int n = path.Length;
            float run = Mathf.Repeat(Time.time * 0.55f, 1.25f);
            if (_pass != HudPass.Words)
            {
                // the shape as a faint ribbon, dots along it, a light running along it the way to draw it
                Ribbon(path, 20f, t => col, 0.6f * appear, 0.7f, 1f);
                for (int i = 0; i < n; i += 4)
                {
                    float near = Mathf.Clamp01(1f - Mathf.Abs(i / (float)(n - 1) - run) * 8f);
                    float d = 6f + 9f * near;
                    if (near > 0f) Picture(new Rect(path[i].x - 34f, path[i].y - 34f, 68f, 68f), "glow", A(col, 0.8f * near * appear));
                    Picture(new Rect(path[i].x - d, path[i].y - d, d * 2f, d * 2f), near > 0.3f ? "star" : "dot", new Color(1f, 1f, 1f, (0.6f + 0.4f * near) * appear));
                }
                Picture(new Rect(path[0].x - 34f, path[0].y - 34f, 68f, 68f), FairyArt.SpellSprite(s.Value.Spell), new Color(1f, 1f, 1f, appear));
            }
            var top = path[0];
            foreach (var p in path) if (p.y < top.y) top = p;
            Words(new Rect(top.x - 240f, Mathf.Max(150f, top.y - 76f), 480f, 56f), FairyStrings.DrawThis(s.Value.Spell), 34, A(col, appear), TextAnchor.MiddleCenter, 3f);
        }

        readonly SpellOffer?[] _hintOffer = new SpellOffer?[2];
        readonly Vector2[][] _hintPath = new Vector2[2][];
        float _hintWidth;

        Vector2[] HintPath(int k, SpellOffer o, float w)
        {
            if (!_hintOffer[k].HasValue || !_hintOffer[k].Value.Equals(o) || Mathf.Abs(_hintWidth - w) > 1f || _hintPath[k] == null)
            {
                _hintOffer[k] = o;
                _hintWidth = w;
                _hintPath[k] = SpellShapes.PathOf(o, w);
            }
            return _hintPath[k];
        }
    }
}
