using UnityEngine;

namespace MoveIt.Games.Paint
{
    /// <summary>The tools in the left toolbar, top to bottom.</summary>
    public enum PaintTool { Marker, Rainbow, Spray, Neon, Glitter, Stamp, Eraser, Fill }

    /// <summary>What the brush does right now (shared by both hands).</summary>
    public struct BrushSettings
    {
        public PaintTool Tool;
        public Color Color;
        /// <summary>Brush diameter in canvas pixels.</summary>
        public float Size;
        public BrushShape StampShape;
        /// <summary>Symmetry: every stamp is repeated mirrored left-right.</summary>
        public bool Mirror;
    }

    /// <summary>
    /// One hand's stroke. Hand positions arrive ~30 times a second, so the path between them is smoothed with
    /// quadratic curves through the midpoints (no corners on circles), and stamps are placed at an even spacing along
    /// it. Each tool turns a point on the path into stamps ("dabs"): a soft round tip, a rainbow that changes hue with
    /// distance, spray dots, a glowing neon line, glitter, stamps (stars, hearts, flowers, dino footprints that walk
    /// along the path), the eraser. Spray and glitter also build up while the hand stays in one place.
    /// </summary>
    public sealed class BrushStroke
    {
        public bool Active { get; private set; }
        /// <summary>Distance painted in this stroke, canvas pixels.</summary>
        public float Distance { get; private set; }
        /// <summary>Stamps placed by the Stamp tool in this stroke (for sounds).</summary>
        public int StampsPlaced { get; private set; }

        readonly System.Random _rng;
        readonly int _owner;
        Vector2 _p0, _p1;
        float _carry;
        float _timeCarry;
        int _step;

        /// <param name="owner">Which hand (0, 1): its stamps start wet (see <see cref="PaintCanvas.Wipe"/>); -1 = dry at once.</param>
        public BrushStroke(int seed, int owner = -1)
        {
            _rng = new System.Random(seed);
            _owner = owner;
        }

        public void Begin(PaintCanvas canvas, in BrushSettings s, Vector2 at)
        {
            Active = true;
            Distance = 0f;
            StampsPlaced = 0;
            _p0 = _p1 = at;
            _carry = 0f;
            _timeCarry = 0f;
            _step = 0;
            Dab(canvas, s, at, Vector2.up);
        }

        /// <summary>The hand is now at <paramref name="at"/>; <paramref name="dt"/> = seconds since the last call.</summary>
        public void MoveTo(PaintCanvas canvas, in BrushSettings s, Vector2 at, float dt)
        {
            if (!Active) return;
            if ((at - _p1).sqrMagnitude > 0.25f)
            {
                // curve from the midpoint of the last segment to the midpoint of this one, bending through _p1
                Curve(canvas, s, (_p0 + _p1) * 0.5f, _p1, (_p1 + at) * 0.5f);
                _p0 = _p1;
                _p1 = at;
            }
            // spray and glitter keep coming out while the hand holds still (like a real spray can)
            float rate = s.Tool == PaintTool.Spray ? 60f : s.Tool == PaintTool.Glitter ? 12f : 0f;
            _timeCarry += dt * rate;
            for (; _timeCarry >= 1f; _timeCarry -= 1f) Dab(canvas, s, _p1, Vector2.up);
        }

        public void End(PaintCanvas canvas, in BrushSettings s)
        {
            if (!Active) return;
            Curve(canvas, s, (_p0 + _p1) * 0.5f, (_p0 + _p1) * 0.5f, _p1);   // the last half segment, straight
            Active = false;
        }

        /// <summary>Stops without painting the last half segment (the hand was leaving, not painting).</summary>
        public void Abort() => Active = false;

        void Curve(PaintCanvas canvas, in BrushSettings s, Vector2 a, Vector2 control, Vector2 b)
        {
            float spacing = Spacing(s);
            float approx = Vector2.Distance(a, control) + Vector2.Distance(control, b);
            int steps = Mathf.Clamp(Mathf.CeilToInt(approx / 2f), 1, 400);
            var previous = a;
            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps;
                var p = (1 - t) * (1 - t) * a + 2 * (1 - t) * t * control + t * t * b;
                var d = p - previous;
                float len = d.magnitude;
                if (len <= 0f) continue;
                var dir = d / len;
                float walked = 0f;
                while (_carry + (len - walked) >= spacing)
                {
                    walked += spacing - _carry;
                    _carry = 0f;
                    Distance += spacing;
                    Dab(canvas, s, previous + dir * walked, dir);
                }
                _carry += len - walked;
                previous = p;
            }
        }

        static float Spacing(in BrushSettings s) => Mathf.Max(1f, s.Size * s.Tool switch
        {
            PaintTool.Spray => 0.3f,
            PaintTool.Neon => 0.15f,
            PaintTool.Glitter => 0.45f,
            PaintTool.Stamp => s.StampShape == BrushShape.Footprint ? 1.4f : 2.2f,
            PaintTool.Eraser => 0.2f,
            _ => 0.12f,
        });

        float Rand(float min, float max) => min + (float)_rng.NextDouble() * (max - min);

        Vector2 InDisc(float radius)
        {
            float a = Rand(0f, Mathf.PI * 2f), r = Mathf.Sqrt(Rand(0f, 1f)) * radius;
            return new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
        }

        static readonly Color[] GlitterColors =
        {
            new Color(1f, 0.85f, 0.25f), new Color(1f, 1f, 1f), new Color(1f, 0.5f, 0.8f),
            new Color(0.45f, 0.85f, 1f), new Color(0.75f, 0.6f, 1f),
        };

        void Dab(PaintCanvas canvas, in BrushSettings s, Vector2 p, Vector2 dir)
        {
            float size = s.Size;
            switch (s.Tool)
            {
                case PaintTool.Marker:
                    Put(canvas, s, p, size, 0f, s.Color, BrushShape.Round);
                    break;
                case PaintTool.Rainbow:
                    Put(canvas, s, p, size, 0f, Color.HSVToRGB(Mathf.Repeat(Distance / 900f, 1f), 0.75f, 1f), BrushShape.Round);
                    break;
                case PaintTool.Spray:
                    for (int i = 0; i < 4; i++)
                        Put(canvas, s, p + InDisc(size * 1.3f), size * Rand(0.07f, 0.14f), 0f,
                            Color.Lerp(s.Color, Color.white, Rand(0f, 0.15f)), BrushShape.Round);
                    break;
                case PaintTool.Neon:
                    Put(canvas, s, p, size * 3f, 0f, s.Color.WithAlpha(0.05f), BrushShape.Soft);
                    Put(canvas, s, p, size * 1.4f, 0f, s.Color.WithAlpha(0.25f), BrushShape.Soft);
                    Put(canvas, s, p, size * 0.5f, 0f, Color.Lerp(s.Color, Color.white, 0.7f), BrushShape.Round);
                    break;
                case PaintTool.Glitter:
                {
                    var c = _rng.Next(3) == 0 ? Color.Lerp(s.Color, Color.white, 0.4f) : GlitterColors[_rng.Next(GlitterColors.Length)];
                    Put(canvas, s, p + InDisc(size * 1.1f), size * Rand(0.4f, 1.1f), Rand(-20f, 20f), c, BrushShape.Sparkle);
                    if (_rng.Next(2) == 0) Put(canvas, s, p + InDisc(size * 1.4f), size * 0.18f, 0f, Color.white, BrushShape.Round);
                    break;
                }
                case PaintTool.Stamp:
                {
                    StampsPlaced++;
                    if (s.StampShape == BrushShape.Footprint)
                    {
                        // walking dino: prints point along the path, alternating left and right foot
                        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
                        var side = new Vector2(dir.y, -dir.x) * (size * 0.45f * ((_step++ & 1) == 0 ? 1f : -1f));
                        Put(canvas, s, p + side, size * 1.3f, angle, s.Color, BrushShape.Footprint);
                    }
                    else Put(canvas, s, p, size * Rand(1.7f, 2.3f), Rand(-25f, 25f), s.Color, s.StampShape);
                    break;
                }
                case PaintTool.Eraser:
                    Put(canvas, s, p, size * 2f, 0f, Color.white, BrushShape.Round, erase: true);
                    break;
            }
        }

        void Put(PaintCanvas canvas, in BrushSettings s, Vector2 p, float size, float degrees, Color c, BrushShape shape, bool erase = false)
        {
            canvas.Stamp(p, size, degrees, c, shape, erase, _owner);
            if (s.Mirror) canvas.Stamp(new Vector2(canvas.Width - p.x, p.y), size, -degrees, c, shape, erase, _owner);
        }
    }
}
