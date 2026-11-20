using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Fairy
{
    /// <summary>
    /// The spells' shapes (2026-11-16; since 2026-11-17 drawn freely in the air and read by <see cref="SpellReader"/>: these
    /// are the guides the spell book shows, the game draws when it plays itself and the simulated players draw), in HUD
    /// pixels (1080 high, y down, the screen's middle at <c>width / 2</c>), each where its magic goes: the lightning a bolt drawn down onto its
    /// side's path (out toward the screen's edge, where the creatures come in), the rainbow an arc over its side of
    /// the sky (where the wisps fly), the shield a circle round the tree, the seeds a wave along the meadow, the big
    /// magic a star over the garden, the freeze a spiral in the sky, the healing rain a heart over the crown. Evenly
    /// spaced points (<see cref="Spacing"/>), the first where the start star is drawn. Big and simple: a child follows
    /// them with the whole arm. motion-service-py/fake_sender.py draws the lightning the same way (keep them in step).
    /// </summary>
    public static class SpellShapes
    {
        public const float Spacing = 12f;

        public static bool Closed(Spell s) => s is Spell.Shield or Spell.Burst or Spell.Heal;

        public static Vector2[] PathOf(Spell s, float width) => PathOf(new SpellOffer(s, 1), width);

        public static Vector2[] PathOf(SpellOffer offer, float width)
        {
            var s = offer.Spell;
            float mid = width * 0.5f;
            var pts = new List<Vector2>();
            var c = s switch
            {
                Spell.Shield => new Vector2(mid, 600f),
                Spell.Seeds => new Vector2(mid, 800f),
                Spell.Burst => new Vector2(mid, 480f),
                Spell.Freeze => new Vector2(mid, 290f),
                _ => new Vector2(mid, 330f),
            };
            switch (s)
            {
                case Spell.Lightning:
                    // a lightning bolt drawn down onto that side's path (out toward the screen's edge, where the creatures
                    // come in): from its top, down, a jog back, down to its point on the path
                    foreach (var (x, y) in new[] { (530f, 430f), (630f, 620f), (500f, 620f), (620f, 840f) })
                        pts.Add(new Vector2(mid + offer.Side * x, y));
                    break;
                case Spell.Rainbow:
                    // an arc over that side of the sky, from above the crown's side out toward the edge
                    for (int i = 0; i <= 48; i++)
                    {
                        float a = i / 48f * Mathf.PI;
                        pts.Add(new Vector2(mid + offer.Side * (530f - 290f * Mathf.Cos(a)), 360f - 170f * Mathf.Sin(a)));
                    }
                    break;
                case Spell.Shield:
                    // a circle round the tree's trunk, from the top, clockwise on the screen
                    for (int i = 0; i <= 96; i++)
                    {
                        float a = -Mathf.PI / 2f + i / 96f * Mathf.PI * 2f;
                        pts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 210f);
                    }
                    break;
                case Spell.Seeds:
                    // a wave along the meadow, left to right: one and a half swells
                    for (int i = 0; i <= 96; i++)
                    {
                        float t = i / 96f;
                        pts.Add(c + new Vector2(-520f + 1040f * t, -105f * Mathf.Sin(t * Mathf.PI * 3f)));
                    }
                    break;
                case Spell.Burst:
                    // a five-pointed star over the garden, from its top point, clockwise (no bigger: a child follows a
                    // star of 300 px in ~10 s)
                    for (int i = 0; i <= 10; i++)
                    {
                        float a = -Mathf.PI / 2f + i / 10f * Mathf.PI * 2f;
                        float r = i % 2 == 0 ? 165f : 70f;
                        pts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
                    }
                    break;
                case Spell.Freeze:
                    // a snow spiral in the sky: from its top, two turns clockwise inward (2026-11-17: drawn freely, a turn
                    // and a half was too near a circle drawn a little past its start)
                    for (int i = 0; i <= 144; i++)
                    {
                        float a = i / 144f * Mathf.PI * 4f;
                        float r = 190f - 155f * i / 144f;
                        pts.Add(c + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * r);
                    }
                    break;
                default:
                    // a heart over the crown, from the dip at its top, round to the right, down to the point, back up
                    for (int i = 0; i <= 120; i++)
                    {
                        float t = i / 120f * Mathf.PI * 2f;
                        float x = 16f * Mathf.Pow(Mathf.Sin(t), 3f);
                        float y = 13f * Mathf.Cos(t) - 5f * Mathf.Cos(2f * t) - 2f * Mathf.Cos(3f * t) - Mathf.Cos(4f * t);
                        pts.Add(c + new Vector2(x, -y - 3f) * 10.5f);
                    }
                    break;
            }
            return Resample(pts, Spacing);
        }

        /// <summary>The polyline through <paramref name="pts"/> as points <paramref name="spacing"/> apart.</summary>
        public static Vector2[] Resample(IReadOnlyList<Vector2> pts, float spacing)
        {
            var output = new List<Vector2> { pts[0] };
            float carry = 0f;
            for (int i = 1; i < pts.Count; i++)
            {
                var a = pts[i - 1];
                var b = pts[i];
                float len = Vector2.Distance(a, b);
                float at = spacing - carry;
                while (at <= len)
                {
                    output.Add(Vector2.Lerp(a, b, at / len));
                    at += spacing;
                }
                carry = len - (at - spacing);
            }
            if (Vector2.Distance(output[output.Count - 1], pts[pts.Count - 1]) > spacing * 0.3f) output.Add(pts[pts.Count - 1]);
            return output.ToArray();
        }

        public static float Length(Vector2[] path)
        {
            float l = 0f;
            for (int i = 1; i < path.Length; i++) l += Vector2.Distance(path[i - 1], path[i]);
            return l;
        }
    }
}
