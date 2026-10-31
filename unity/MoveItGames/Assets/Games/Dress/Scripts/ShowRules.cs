using System;
using MoveIt.Motion;
using UnityEngine;
using Joint = MoveIt.Motion.Joint;

namespace MoveIt.Games.Dress
{
    /// <summary>The runway poses (a card shows each one; the camera checks it).</summary>
    public enum ShowPose { Star, Hips, Heart, Wave, Super, Plane }

    /// <summary>
    /// How well the body matches a runway pose, 0..1, from the joints on the screen (pixels, y up, mirrored like a
    /// mirror). Forgiving on purpose (children, a camera that guesses hidden joints): about right is enough, and the
    /// one-sided poses count with either arm.
    /// </summary>
    public static class PoseCheck
    {
        /// <summary>A pose counts from this score.</summary>
        public const float Pass = 0.6f;

        public static float Score(ShowPose pose, Vector2[] j, float torso)
        {
            float t = Mathf.Max(torso, 1f);
            Vector2 ls = j[(int)Joint.LeftShoulder], rs = j[(int)Joint.RightShoulder];
            Vector2 le = j[(int)Joint.LeftElbow], re = j[(int)Joint.RightElbow];
            Vector2 lw = j[(int)Joint.LeftWrist], rw = j[(int)Joint.RightWrist];
            Vector2 lh = j[(int)Joint.LeftHip], rh = j[(int)Joint.RightHip];
            Vector2 nose = j[(int)Joint.Nose];
            float shoulderY = (ls.y + rs.y) * 0.5f;
            switch (pose)
            {
                case ShowPose.Star:     // both arms up and out, like a star
                    return Mathf.Min(Direction(lw - ls, new Vector2(-0.7f, 0.7f), 28f, 60f), Direction(rw - rs, new Vector2(0.7f, 0.7f), 28f, 60f));
                case ShowPose.Plane:    // arms straight out to the sides
                    return Mathf.Min(Direction(lw - ls, Vector2.left, 22f, 50f), Direction(rw - rs, Vector2.right, 22f, 50f));
                case ShowPose.Heart:    // hands together above the head, elbows out
                {
                    float above = Ramp(Mathf.Min(lw.y, rw.y) - nose.y, 0f, 0.25f * t);
                    float together = 1f - Ramp((lw - rw).magnitude, 0.45f * t, 0.9f * t);
                    float elbows = Ramp(re.x - le.x, rs.x - ls.x, rs.x - ls.x + 0.3f * t);   // wider than the shoulders
                    return Mathf.Min(above, together) * Mathf.Lerp(0.7f, 1f, elbows);
                }
                case ShowPose.Hips:     // hands on the hips, elbows well out (arms just hanging must not count)
                {
                    float left = (1f - Ramp((lw - lh).magnitude, 0.35f * t, 0.7f * t)) * Ramp(ls.x - le.x, 0.1f * t, 0.25f * t)
                                 * Ramp(lw.y - lh.y, -0.35f * t, -0.15f * t);
                    float right = (1f - Ramp((rw - rh).magnitude, 0.35f * t, 0.7f * t)) * Ramp(re.x - rs.x, 0.1f * t, 0.25f * t)
                                  * Ramp(rw.y - rh.y, -0.35f * t, -0.15f * t);
                    return Mathf.Min(left, right);
                }
                case ShowPose.Wave:     // one hand high above the head, the other low
                {
                    float left = Ramp(lw.y - nose.y, 0f, 0.2f * t) * Ramp(shoulderY - rw.y, -0.1f * t, 0.3f * t);
                    float right = Ramp(rw.y - nose.y, 0f, 0.2f * t) * Ramp(shoulderY - lw.y, -0.1f * t, 0.3f * t);
                    return Mathf.Max(left, right);
                }
                case ShowPose.Super:    // one fist straight up, the other hand down by the hip
                {
                    float left = Direction(lw - ls, Vector2.up, 22f, 50f) * Ramp(shoulderY - rw.y, 0f, 0.4f * t);
                    float right = Direction(rw - rs, Vector2.up, 22f, 50f) * Ramp(shoulderY - lw.y, 0f, 0.4f * t);
                    return Mathf.Max(left, right);
                }
            }
            return 0f;
        }

        /// <summary>1 within <paramref name="full"/> degrees of the direction, 0 beyond <paramref name="zero"/>.</summary>
        static float Direction(Vector2 v, Vector2 target, float full, float zero)
        {
            if (v.sqrMagnitude < 1e-4f) return 0f;
            float angle = Vector2.Angle(v, target);
            return 1f - Ramp(angle, full, zero);
        }

        /// <summary>0 at a, 1 at b (either order), clamped.</summary>
        static float Ramp(float x, float a, float b) => Mathf.Abs(b - a) < 1e-5f ? (x >= a ? 1f : 0f) : Mathf.Clamp01((x - a) / (b - a));
    }

    public enum Judge { Cat, Bunny, Dino }

    public enum Remark { Theme, ThemeMore, Colors, ColorsMore, Complete, AddMore, Poses, PosesMore, Magic }

    public struct Verdict
    {
        public Judge Judge;
        public int Stars;
        public float Score;
        public Remark Remark;
    }

    /// <summary>
    /// The fashion show's three judges (Love Nikki / Style Savvy: each looks at other things). Mimi the cat loves the
    /// theme, Lumi the bunny colours and a complete look, Dino the poses and the fun (magic, something in the hand,
    /// a special fabric). Every judge gives 2 to 5 stars: nobody leaves the stage sad.
    /// </summary>
    public static class ShowJudge
    {
        public const int MinStars = 2, MaxStars = 5;

        /// <summary>0..1: how many things fit the theme (four is plenty), and how much of the look does.</summary>
        public static float ThemeFit(DressLook look, Theme theme)
        {
            int worn = 0, fit = 0;
            for (int s = 0; s < DressCatalog.SlotCount; s++)
            {
                var item = look.Worn((Slot)s);
                if (item == null) continue;
                worn++;
                if (item.Fits(theme)) fit++;
            }
            if (look.Effect != Effect.None)
            {
                worn++;
                if (DressCatalog.EffectFits(look.Effect, theme)) fit++;
            }
            if (worn == 0) return 0f;
            return Mathf.Clamp01(0.55f * Mathf.Min(fit, 4) / 4f + 0.45f * fit / worn);
        }

        /// <summary>0..1: a top and a skirt, and three accessories.</summary>
        public static float Completeness(DressLook look)
        {
            float score = (look.IsWorn(Slot.Top) ? 0.25f : 0f) + (look.IsWorn(Slot.Bottom) ? 0.25f : 0f);
            int accessories = 0;
            for (int s = (int)Slot.Hair; s < DressCatalog.SlotCount; s++)
                if (look.IsWorn((Slot)s)) accessories++;
            return score + 0.5f * Mathf.Min(accessories, 3) / 3f;
        }

        /// <summary>0..1: colours the theme likes (special fabrics always shine), a bonus for matching pieces.</summary>
        public static float ColorFit(DressLook look, Theme theme)
        {
            var good = DressCatalog.ThemeColors[(int)theme];
            float sum = 0f;
            int n = 0;
            bool match = false;
            for (int s = 0; s < DressCatalog.SlotCount; s++)
            {
                var slot = (Slot)s;
                if (!look.IsWorn(slot)) continue;
                n++;
                int color = look.ColorOf(slot);
                sum += look.FinishOf(slot) != Finish.Plain ? 0.9f : Array.IndexOf(good, color) >= 0 ? 1f : 0.3f;
                for (int o = s + 1; o < DressCatalog.SlotCount && !match; o++)
                    if (look.IsWorn((Slot)o) && look.ColorOf((Slot)o) == color) match = true;
            }
            if (n == 0) return 0f;
            return Mathf.Clamp01(sum / n + (match ? 0.15f : 0f));
        }

        /// <summary>0..1: magic on, something in the hand, a special fabric.</summary>
        public static float Fun(DressLook look)
        {
            bool finish = false;
            for (int s = 0; s < DressCatalog.SlotCount; s++)
                if (look.FinishOf((Slot)s) != Finish.Plain) finish = true;
            return Mathf.Clamp01((look.Effect != Effect.None ? 0.5f : 0f) + (look.IsWorn(Slot.Hand) ? 0.3f : 0f) + (finish ? 0.2f : 0f));
        }

        /// <summary>The three verdicts. <paramref name="poses"/> = how well the runway poses went, 0..1.</summary>
        public static Verdict[] Judge(DressLook look, Theme theme, float poses)
        {
            float th = ThemeFit(look, theme), co = ColorFit(look, theme), cm = Completeness(look), fun = Fun(look);
            poses = Mathf.Clamp01(poses);
            return new[]
            {
                Make(Dress.Judge.Cat, (th, 0.55f, Remark.Theme, Remark.ThemeMore), (co, 0.25f, Remark.Colors, Remark.ColorsMore), (cm, 0.2f, Remark.Complete, Remark.AddMore)),
                Make(Dress.Judge.Bunny, (co, 0.45f, Remark.Colors, Remark.ColorsMore), (cm, 0.35f, Remark.Complete, Remark.AddMore), (th, 0.2f, Remark.Theme, Remark.ThemeMore)),
                Make(Dress.Judge.Dino, (poses, 0.5f, Remark.Poses, Remark.PosesMore), (cm, 0.25f, Remark.Complete, Remark.AddMore), (fun, 0.25f, Remark.Magic, Remark.AddMore)),
            };
        }

        static Verdict Make(Judge judge, params (float value, float weight, Remark good, Remark more)[] parts)
        {
            float score = 0f;
            foreach (var p in parts) score += p.value * p.weight;
            // the remark: what to add if something important is missing, else what was best
            var weakest = parts[0];
            var best = parts[0];
            foreach (var p in parts)
            {
                if (p.value * p.weight < weakest.value * weakest.weight - 1e-4f && p.value < 0.6f) weakest = p;
                if (p.value > best.value) best = p;
            }
            Remark remark = parts[0].value < 0.6f ? parts[0].more : weakest.value < 0.5f ? weakest.more : best.good;
            int stars = Mathf.Clamp(Mathf.RoundToInt(1f + 4f * score), MinStars, MaxStars);
            return new Verdict { Judge = judge, Stars = stars, Score = score, Remark = remark };
        }

        public static int Total(Verdict[] verdicts)
        {
            int n = 0;
            foreach (var v in verdicts) n += v.Stars;
            return n;
        }
    }
}
