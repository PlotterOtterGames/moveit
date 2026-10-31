using System;
using System.Collections.Generic;
using System.Linq;
using MoveIt.Motion;
using NUnit.Framework;
using UnityEngine;
using Joint = MoveIt.Motion.Joint;

namespace MoveIt.Games.Dress.Tests
{
    /// <summary>Vishu!'s rules without a scene: the wardrobe, what is worn, the judges, progress (also tools/logic-tests).</summary>
    public class DressRulesTests
    {
        [Test]
        public void Catalog_SixItemsPerSlot_UniqueIds_ValidColors()
        {
            Assert.AreEqual(DressCatalog.SlotCount, DressCatalog.Items.Length);
            var ids = new HashSet<string>();
            for (int s = 0; s < DressCatalog.SlotCount; s++)
            {
                Assert.AreEqual(6, DressCatalog.Items[s].Length, $"slot {(Slot)s}");
                foreach (var item in DressCatalog.Items[s])
                {
                    Assert.IsTrue(ids.Add(item.Id), $"{item.Id} twice");
                    Assert.AreEqual((Slot)s, item.Slot, item.Id);
                    Assert.Greater(item.Colors.Length, 2, item.Id);
                    Assert.IsTrue(item.Colors.All(c => c >= 0 && c < DressCatalog.Palette.Length), item.Id);
                    Assert.AreEqual(item.Colors.Length, item.Colors.Distinct().Count(), $"{item.Id}: a colour twice");
                    Assert.Greater(item.Themes.Length, 0, item.Id);
                    Assert.IsFalse(string.IsNullOrEmpty(item.NameSq) || string.IsNullOrEmpty(item.NameEn), item.Id);
                }
            }
        }

        [Test]
        public void EveryTheme_HasATopOrSkirt_AndEnoughToWear()
        {
            for (int t = 0; t < DressCatalog.ThemeCount; t++)
            {
                var theme = (Theme)t;
                int fitting = DressCatalog.All.Count(i => i.Fits(theme));
                bool clothes = DressCatalog.Items[(int)Slot.Top].Concat(DressCatalog.Items[(int)Slot.Bottom]).Any(i => i.Fits(theme));
                bool effect = Enumerable.Range(1, (int)Effect.Stars).Any(e => DressCatalog.EffectFits((Effect)e, theme));
                Assert.IsTrue(clothes, $"{theme}: no top or skirt");
                Assert.GreaterOrEqual(fitting, 3, $"{theme}: only {fitting} items");
                Assert.IsTrue(effect, $"{theme}: no magic effect");
                Assert.Greater(DressCatalog.ThemeColors[t].Length, 2);
            }
        }

        [Test]
        public void Wear_SameItemAgain_CyclesColoursThenFinishes()
        {
            var look = new DressLook();
            Assert.AreEqual(DressLook.Change.Wore, look.Wear(Slot.Top, 0, 2));
            var item = look.Worn(Slot.Top);
            Assert.AreEqual("top_princess", item.Id);
            Assert.AreEqual(item.Colors[0], look.ColorOf(Slot.Top));
            for (int k = 1; k < item.Colors.Length; k++)
            {
                Assert.AreEqual(DressLook.Change.Recolored, look.Wear(Slot.Top, 0, 2));
                Assert.AreEqual(item.Colors[k], look.ColorOf(Slot.Top));
                Assert.AreEqual(Finish.Plain, look.FinishOf(Slot.Top));
            }
            look.Wear(Slot.Top, 0, 2);
            Assert.AreEqual(Finish.Rainbow, look.FinishOf(Slot.Top), "after the colourways: the first unlocked finish");
            look.Wear(Slot.Top, 0, 2);
            Assert.AreEqual(Finish.Glitter, look.FinishOf(Slot.Top));
            look.Wear(Slot.Top, 0, 2);
            Assert.AreEqual(Finish.Plain, look.FinishOf(Slot.Top), "and round again");
            Assert.AreEqual(item.Colors[0], look.ColorOf(Slot.Top));

            Assert.AreEqual(DressLook.Change.Wore, look.Wear(Slot.Top, 3, 2), "another item: worn in its first colour");
            Assert.AreEqual(0, look.Shade[(int)Slot.Top]);
            Assert.AreEqual(DressLook.Change.Removed, look.Wear(Slot.Top, -1, 2));
            Assert.AreEqual(DressLook.Change.Nothing, look.Wear(Slot.Top, -1, 2));
            Assert.IsFalse(look.IsWorn(Slot.Top));
        }

        [Test]
        public void Look_SavesAndLoads_AndRejectsGarbage()
        {
            var look = new DressLook();
            look.Wear(Slot.Bottom, 2, 0);
            look.Wear(Slot.Hair, 5, 0);
            look.Wear(Slot.Hair, 5, 0);
            look.Effect = Effect.Bubbles;
            var back = DressLook.Load(look.Save(), 0);
            CollectionAssert.AreEqual(look.Item, back.Item);
            CollectionAssert.AreEqual(look.Shade, back.Shade);
            Assert.AreEqual(Effect.Bubbles, back.Effect);
            Assert.AreEqual(0, DressLook.Load("nonsense|x", 0).Count);
            Assert.AreEqual(0, DressLook.Load("1|0|9:9|a:b|0:0|0:0|0:0|0:0|0:0", 0).Count, "bad numbers: a fresh look");
            var finish = new DressLook();
            finish.Wear(Slot.Top, 0, 4);
            for (int k = 0; k < finish.Worn(Slot.Top).Colors.Length + 3; k++) finish.Wear(Slot.Top, 0, 4);
            Assert.AreEqual(0, DressLook.Load(finish.Save(), 0).Shade[(int)Slot.Top], "a finish not unlocked (anymore) falls back");
        }

        [Test]
        public void Surprise_IsAWholeLookForTheTheme()
        {
            var rng = new System.Random(7);
            for (int t = 0; t < DressCatalog.ThemeCount; t++)
            {
                var look = new DressLook();
                var theme = look.Surprise(rng, (Theme)t);
                Assert.AreEqual((Theme)t, theme);
                Assert.IsTrue(look.IsWorn(Slot.Top) && look.IsWorn(Slot.Bottom), $"{theme}: always a top and a skirt");
                Assert.GreaterOrEqual(ShowJudge.ThemeFit(look, theme), 0.6f, $"{theme}: {look.Save()}");
            }
        }

        [Test]
        public void Judges_LikeAFittingCompleteLook_AndNeverGiveLessThanTwo()
        {
            var princess = new DressLook();
            princess.Wear(Slot.Top, 0, 0);            // princess bodice, pink
            princess.Wear(Slot.Bottom, 0, 0);         // ball gown, pink
            princess.Wear(Slot.Hair, 0, 0);
            princess.Wear(Slot.Head, 0, 0);           // crown, gold
            princess.Wear(Slot.Back, 4, 0);           // royal cloak
            princess.Wear(Slot.Hand, 0, 0);           // wand
            princess.Effect = Effect.Sparkles;
            var verdicts = ShowJudge.Judge(princess, Theme.Princess, 1f);
            Assert.AreEqual(3, verdicts.Length);
            Assert.GreaterOrEqual(verdicts[0].Stars, 4, "Mimi loves a real princess");
            Assert.AreEqual(Remark.Theme, verdicts[0].Remark);
            Assert.GreaterOrEqual(ShowJudge.Total(verdicts), 12);

            var wrong = new DressLook();
            wrong.Wear(Slot.Top, 3, 0);               // hero suit for a princess show
            var poor = ShowJudge.Judge(wrong, Theme.Princess, 0f);
            Assert.IsTrue(poor.All(v => v.Stars >= ShowJudge.MinStars && v.Stars <= ShowJudge.MaxStars));
            Assert.Less(ShowJudge.Total(poor), ShowJudge.Total(verdicts));
            Assert.AreEqual(Remark.ThemeMore, poor[0].Remark);
            Assert.AreEqual(Remark.PosesMore, poor[2].Remark);
            Assert.AreEqual(6, ShowJudge.Total(ShowJudge.Judge(new DressLook(), Theme.Party, 0f)), "nothing worn: the minimum");
        }

        [Test]
        public void Progress_UnlocksFinishesWithStars_AndSaves()
        {
            var p = new DressProgress();
            Assert.AreEqual(0, p.Finishes);
            Assert.AreEqual(DressCatalog.FinishStars[0], p.StarsToNext);
            CollectionAssert.IsEmpty(p.AddShow(6));
            CollectionAssert.AreEqual(new[] { Finish.Rainbow }, p.AddShow(6));
            Assert.AreEqual(12, p.Stars);
            Assert.AreEqual(2, p.Shows);
            CollectionAssert.AreEqual(new[] { Finish.Glitter, Finish.Gold, Finish.Galaxy }, p.AddShow(50));
            Assert.AreEqual(-1, p.StarsToNext);
            var back = DressProgress.Load(p.Save());
            Assert.AreEqual(p.Stars, back.Stars);
            Assert.AreEqual(p.Shows, back.Shows);
            Assert.AreEqual(50, back.BestShow);
            Assert.AreEqual(0, DressProgress.Load("2|x").Stars);
        }

        [Test]
        public void Timeline_InterpolatesOnTheCameraClock()
        {
            var tl = new PoseTimeline();
            var j = new Vector3[Skeleton.JointCount];
            var v = new float[Skeleton.JointCount];
            for (int k = 0; k < 4; k++)
            {
                for (int i = 0; i < j.Length; i++) j[i] = new Vector3(k * 10f, i, 0f);
                for (int i = 0; i < v.Length; i++) v[i] = 0.9f;
                tl.Add(1.0 + k * 0.033, j, v);
            }
            tl.Add(1.0, j, v);   // an old sample is ignored
            Assert.AreEqual(4, tl.Count);
            var o = new Vector3[Skeleton.JointCount];
            var ov = new float[Skeleton.JointCount];
            Assert.IsTrue(tl.At(1.0 + 0.0165, o, ov));
            Assert.AreEqual(5f, o[0].x, 0.01f, "half way between the first two samples");
            tl.At(0.5, o, ov);
            Assert.AreEqual(0f, o[0].x, 1e-4f, "before the oldest: the oldest");
            tl.At(1.099 + 1.0, o, ov);
            Assert.AreEqual(30f + 10f * 0.05f / 0.033f, o[0].x, 0.05f, "after the newest: carried on at most 50 ms");
        }

        // ---------------------------------------------------------------- the rig

        static OutfitPose Pose(float t = 200f, Action<Vector2[]> change = null)
        {
            var joints = new Vector2[Skeleton.JointCount];
            var vis = new float[Skeleton.JointCount];
            OutfitPose.DollJoints(new Vector2(800f, 700f), t, 0f, joints, vis);
            change?.Invoke(joints);
            var pose = new OutfitPose();
            pose.Update(joints, vis, 0f);
            for (int k = 0; k < 4; k++) pose.Update(joints, vis, 1f / 30f);
            return pose;
        }

        static PieceDef Piece(Rig rig, Layer layer = Layer.Top) => new PieceDef
        {
            Name = "test", Item = "test", Rig = rig, Layer = layer, W = 300f, H = 300f, OriginX = 150f, OriginY = 150f, Ppt = 100f,
        };

        [Test]
        public void Rig_Torso_PutsTheTemplateShouldersAndHipsOnTheJoints()
        {
            var pose = Pose();
            var f = new RigFrame();
            Assert.IsTrue(OutfitRig.Build(Piece(Rig.Torso), pose, new OutfitMotion(), false, f));
            AssertNear(pose.J[(int)Joint.LeftShoulder], f.Map(new Vector2(-0.40f, 0f)), "left shoulder");
            AssertNear(pose.J[(int)Joint.RightShoulder], f.Map(new Vector2(0.40f, 0f)), "right shoulder");
            AssertNear(pose.J[(int)Joint.LeftHip], f.Map(new Vector2(-0.24f, 1f)), "left hip");
            AssertNear(pose.J[(int)Joint.RightHip], f.Map(new Vector2(0.24f, 1f)), "right hip");
            Assert.Greater(f.Map(new Vector2(0f, -0.1f)).y, f.Map(new Vector2(0f, 0.5f)).y, "template y down = screen down");
        }

        [Test]
        public void Rig_SkirtStartsWhereTheTopsWaistIs_AndKneesTogetherDontPinchIt()
        {
            var pose = Pose();
            var torso = new RigFrame();
            var skirt = new RigFrame();
            var motion = new OutfitMotion();
            OutfitRig.Build(Piece(Rig.Torso), pose, motion, false, torso);
            OutfitRig.Build(Piece(Rig.Skirt, Layer.Bottom), pose, motion, false, skirt);
            foreach (float x in new[] { -0.272f, 0f, 0.272f })
                AssertNear(torso.Map(new Vector2(x, 0.8f)), skirt.Map(new Vector2(x, 0.8f)), $"waist x {x}");
            float hem = (skirt.Map(new Vector2(0.9f, 2.9f)) - skirt.Map(new Vector2(-0.9f, 2.9f))).magnitude;

            var together = Pose(change: j =>
            {
                j[(int)Joint.LeftKnee].x += 40f;
                j[(int)Joint.RightKnee].x -= 40f;
                j[(int)Joint.LeftAnkle].x += 40f;
                j[(int)Joint.RightAnkle].x -= 40f;
            });
            OutfitRig.Build(Piece(Rig.Skirt, Layer.Bottom), together, new OutfitMotion(), false, skirt);
            float hem2 = (skirt.Map(new Vector2(0.9f, 2.9f)) - skirt.Map(new Vector2(-0.9f, 2.9f))).magnitude;
            Assert.AreEqual(hem, hem2, hem * 0.02f, "a ball gown keeps its width when the knees meet");
        }

        [Test]
        public void Rig_Sleeve_FollowsTheArm_AndTheLeftOneIsMirrored()
        {
            var pose = Pose(change: j =>
            {
                j[(int)Joint.RightElbow] = j[(int)Joint.RightShoulder] + new Vector2(120f, 30f);   // arm out to the side
                j[(int)Joint.RightWrist] = j[(int)Joint.RightElbow] + new Vector2(110f, 60f);
            });
            var f = new RigFrame();
            OutfitRig.Build(Piece(Rig.Arm, Layer.Sleeve), pose, new OutfitMotion(), false, f);
            AssertNear(pose.J[(int)Joint.RightShoulder], f.Map(new Vector2(0f, 0f)), "shoulder");
            AssertNear(pose.J[(int)Joint.RightElbow], f.Map(new Vector2(0f, 0.64f)), "elbow");
            AssertNear(pose.J[(int)Joint.RightWrist], f.Map(new Vector2(0f, 1.21f)), "wrist");
            float width = (f.Map(new Vector2(0.1f, 0.3f)) - f.Map(new Vector2(-0.1f, 0.3f))).magnitude;
            Assert.AreEqual(0.2f * pose.T, width, 1f, "sleeve width scales with the torso length");

            var left = new RigFrame();
            OutfitRig.Build(Piece(Rig.Arm, Layer.Sleeve), pose, new OutfitMotion(), true, left);
            var outside = left.Map(new Vector2(0.1f, 0.3f)) - left.Map(new Vector2(0f, 0.3f));
            Assert.Less(outside.x, 0f, "the left sleeve's outer side (template +x) faces the screen's left");
        }

        [Test]
        public void Rig_HeadItems_SitOnTheEyes_AndTiltWithTheHead()
        {
            var pose = Pose();
            var f = new RigFrame();
            OutfitRig.Build(Piece(Rig.Face, Layer.Face), pose, new OutfitMotion(), false, f);
            AssertNear(pose.J[(int)Joint.LeftEye], f.Map(new Vector2(-0.064f, -0.47f)), "left eye", 2f);
            AssertNear(pose.J[(int)Joint.RightEye], f.Map(new Vector2(0.064f, -0.47f)), "right eye", 2f);

            var tilted = Pose(change: j =>
            {
                Vector2 c = (j[(int)Joint.LeftEye] + j[(int)Joint.RightEye]) * 0.5f;
                foreach (var e in new[] { Joint.Nose, Joint.LeftEye, Joint.RightEye, Joint.LeftEar, Joint.RightEar })
                {
                    Vector2 d = j[(int)e] - c;
                    float a = 20f * Mathf.Deg2Rad;
                    j[(int)e] = c + new Vector2(d.x * Mathf.Cos(a) - d.y * Mathf.Sin(a), d.x * Mathf.Sin(a) + d.y * Mathf.Cos(a));
                }
            });
            Assert.AreEqual(20f, tilted.HeadRoll * Mathf.Rad2Deg, 1.5f, "the head's roll");
            OutfitRig.Build(Piece(Rig.Head, Layer.Head), tilted, new OutfitMotion(), false, f);
            Vector2 across = f.Map(new Vector2(0.15f, -0.8f)) - f.Map(new Vector2(-0.15f, -0.8f));
            Assert.AreEqual(20f, Mathf.Atan2(across.y, across.x) * Mathf.Rad2Deg, 2f, "a crown tilts with the head");
        }

        [Test]
        public void Rig_Balloon_FloatsAboveTheHand_ABagHangsBelowIt()
        {
            var pose = Pose();
            var motion = new OutfitMotion();
            for (int k = 0; k < 120; k++) motion.Update(pose, 1f / 60f);
            Vector2 grip = OutfitMotion.Grip(pose, true);
            var balloon = Piece(Rig.Hand, Layer.Hand);
            balloon.Mode = HandMode.Float;
            balloon.Knot = new Vector2(0f, 0.24f);
            var f = new RigFrame();
            OutfitRig.Build(balloon, pose, motion, false, f);
            Vector2 knot = f.Map(balloon.Knot);
            Assert.AreEqual(pose.T, (knot - grip).magnitude, 1f, "the string is one torso length");
            Assert.Greater(knot.y, grip.y + 0.8f * pose.T, "the balloon floats up");
            var bag = Piece(Rig.Hand, Layer.Hand);
            bag.Mode = HandMode.Hang;
            OutfitRig.Build(bag, pose, motion, false, f);
            Assert.Less(f.Map(new Vector2(0f, 0.3f)).y, grip.y - 0.25f * pose.T, "the bag hangs below the hand");
        }

        // ---------------------------------------------------------------- turning

        [Test]
        public void Turn_SheetsNeverFold_CoverTheWholeSilhouette_AndMoveSmoothly()
        {
            foreach (float k in new[] { OutfitRig.TorsoDepth, OutfitRig.SkirtDepth, 1f })
                for (int deg = -180; deg <= 180; deg += 3)
                {
                    float yaw = deg * Mathf.Deg2Rad;
                    float prevF = float.NegativeInfinity, prevB = float.PositiveInfinity;
                    float minF = 9f, maxF = -9f, minB = 9f, maxB = -9f;
                    for (int i = 0; i <= 60; i++)
                    {
                        float s = -1.2f + i * 0.04f;
                        float f = Turn.Wrap(s, yaw, k), b = Turn.WrapBack(s, yaw, k, out _);
                        Assert.GreaterOrEqual(f, prevF - 1e-4f, $"the front never folds (k {k}, {deg} deg, s {s})");
                        Assert.LessOrEqual(b, prevB + 1e-4f, $"the back is mirrored and never folds (k {k}, {deg} deg, s {s})");
                        prevF = f;
                        prevB = b;
                        if (Mathf.Abs(s) <= 1f)
                        {
                            minF = Mathf.Min(minF, f); maxF = Mathf.Max(maxF, f);
                            minB = Mathf.Min(minB, b); maxB = Mathf.Max(maxB, b);
                        }
                    }
                    float sil = Turn.Silhouette(yaw, k);
                    Assert.AreEqual(-sil, Mathf.Min(minF, minB), 0.01f, $"covers the silhouette's left (k {k}, {deg} deg)");
                    Assert.AreEqual(sil, Mathf.Max(maxF, maxB), 0.01f, $"and its right (k {k}, {deg} deg)");
                    Assert.LessOrEqual(Mathf.Max(minF, minB), Mathf.Min(maxF, maxB) + 0.01f, $"no gap between front and back (k {k}, {deg} deg)");
                    // the horizon (where the renderer puts a column) is where a sheet starts to collapse
                    foreach (bool back in new[] { false, true })
                    {
                        float h = Turn.Horizon(yaw, k, back);
                        bool Hidden(float x)
                        {
                            bool b;
                            if (back) Turn.WrapBack(x, yaw, k, out b);
                            else Turn.Wrap(x, yaw, k, out b);
                            return b;
                        }
                        if (Mathf.Abs(h) < 0.97f)
                            Assert.AreNotEqual(Hidden(h - 0.02f), Hidden(h + 0.02f), $"{(back ? "back" : "front")} horizon (k {k}, {deg} deg)");
                    }
                }
            // facing: the front as drawn, the back folded away; from behind: the front folded away, the back mirrored
            for (float s = -1f; s <= 1f; s += 0.25f)
            {
                Assert.AreEqual(s, Turn.Wrap(s, 0f, 0.62f), 1e-4f);
                Assert.AreEqual(-s, Turn.WrapBack(s, Mathf.PI, 0.62f, out _), 1e-4f);
            }
            // a degree more turn moves every point a little (no jumps), except where a whole sheet is folded to nothing
            for (int deg = -179; deg < 179; deg++)
                for (float s = -1.2f; s <= 1.2f; s += 0.1f)
                {
                    float a = deg * Mathf.Deg2Rad, b = (deg + 1) * Mathf.Deg2Rad;
                    if (Mathf.Abs(deg) < 176)
                        Assert.Less(Mathf.Abs(Turn.Wrap(s, b, 0.62f) - Turn.Wrap(s, a, 0.62f)), 0.06f, $"front, {deg} deg, s {s}");
                    if (Mathf.Abs(deg) > 3)
                        Assert.Less(Mathf.Abs(Turn.WrapBack(s, b, 0.62f, out _) - Turn.WrapBack(s, a, 0.62f, out _)), 0.06f, $"back, {deg} deg, s {s}");
                }
        }

        /// <summary>
        /// The doll seen by the camera while it turns in 3D: a second facing the camera, then half a second turning by
        /// <paramref name="yawDeg"/> (the head <paramref name="headDeg"/> further; <paramref name="change"/> moves joints
        /// meanwhile), then a second still. Depths: the face in
        /// front of the head's axis, the rest on the body's axis plane. <paramref name="world"/>: the camera AI's world
        /// joints give the turn; else only the picture.
        /// </summary>
        static OutfitPose Turned(float yawDeg, float headDeg = 0f, bool world = true, Action<Vector2[]> change = null)
        {
            const float t = 200f, cx = 800f;
            var flat = new Vector2[Skeleton.JointCount];
            var vis = new float[Skeleton.JointCount];
            OutfitPose.DollJoints(new Vector2(cx, 700f), t, 0f, flat, vis);
            var changed = (Vector2[])flat.Clone();
            change?.Invoke(changed);
            var depth = new float[Skeleton.JointCount];
            depth[(int)Joint.Nose] = -0.19f;
            depth[(int)Joint.LeftEye] = depth[(int)Joint.RightEye] = -0.15f;
            var head = new[] { Joint.Nose, Joint.LeftEye, Joint.RightEye, Joint.LeftEar, Joint.RightEar };
            var joints = new Vector2[Skeleton.JointCount];
            var w = new Vector3[Skeleton.JointCount];
            var pose = new OutfitPose();
            for (int frame = 0; frame < 75; frame++)
            {
                float f = Mathf.Clamp01((frame - 30) / 15f);
                for (int i = 0; i < Skeleton.JointCount; i++)
                {
                    float a = (yawDeg + (Array.IndexOf(head, (Joint)i) >= 0 ? headDeg : 0f)) * f * Mathf.Deg2Rad;
                    Vector2 q = Vector2.Lerp(flat[i], changed[i], f);
                    float x = (q.x - cx) / t, z = depth[i];
                    float xr = x * Mathf.Cos(a) - z * Mathf.Sin(a), zr = x * Mathf.Sin(a) + z * Mathf.Cos(a);
                    joints[i] = new Vector2(cx + xr * t, q.y);
                    w[i] = new Vector3(xr * 0.4f, q.y / t * 0.4f, zr * 0.4f);
                }
                float wy = world ? OutfitPose.WorldYaw(w[(int)Joint.LeftShoulder], w[(int)Joint.RightShoulder], w[(int)Joint.LeftHip], w[(int)Joint.RightHip]) : float.NaN;
                pose.Update(joints, vis, frame == 0 ? 0f : 1f / 30f, wy);
            }
            return pose;
        }

        [Test]
        public void Pose_Turn_FromTheWorldJoints_OrFromThePicture()
        {
            Assert.AreEqual(0f, Pose().Yaw, 0.01f, "facing the camera");
            foreach (float deg in new[] { 40f, -60f, 120f, -165f })
                Assert.AreEqual(deg, Turned(deg).Yaw * Mathf.Rad2Deg, 3f, $"world joints, {deg} deg");
            // from the picture only: the shoulders' width says how much, the nose which way (a little less than true)
            float right = Turned(60f, world: false).Yaw * Mathf.Rad2Deg, left = Turned(-60f, world: false).Yaw * Mathf.Rad2Deg;
            Assert.That(right, Is.InRange(42f, 66f), "picture only, 60 deg to the right");
            Assert.That(left, Is.InRange(-66f, -42f), "picture only, 60 deg to the left");
            Assert.Greater(Turned(85f, world: false).Yaw * Mathf.Rad2Deg, 70f, "a profile");
            // narrower shoulders with the nose in the middle (arms up, a hunch) is not a turn
            var hunch = Turned(0f, world: false, change: j =>
            {
                j[(int)Joint.LeftShoulder].x += 0.08f * 200f;
                j[(int)Joint.RightShoulder].x -= 0.08f * 200f;
            });
            Assert.AreEqual(0f, hunch.Yaw * Mathf.Rad2Deg, 3f, "narrow shoulders, nose in the middle");
            // the head on its own
            var look = Turned(0f, 40f);
            Assert.AreEqual(0f, look.Yaw * Mathf.Rad2Deg, 3f, "only the head turns");
            Assert.AreEqual(40f, look.HeadYaw * Mathf.Rad2Deg, 8f, "the head's turn from the nose and the ears");
        }

        /// <summary>How wide a piece is on the screen at template height <paramref name="y"/> (both sheets together), where
        /// its front middle is (from <paramref name="from"/>), and whether either sheet folds over itself.</summary>
        static float Span(PieceDef def, OutfitPose pose, float y, float half, Vector2 from, out float front, out bool folds)
        {
            var f = new RigFrame();
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            folds = false;
            front = 0f;
            for (int sheet = 0; sheet < 2; sheet++)
            {
                OutfitRig.Build(def, pose, new OutfitMotion(), false, f);
                f.BackSheet = sheet == 1;
                float prev = sheet == 0 ? float.NegativeInfinity : float.PositiveInfinity;
                for (float x = -half; x <= half + 1e-4f; x += half / 20f)
                {
                    float px = f.Map(new Vector2(x, y)).x;
                    if (sheet == 0 ? px < prev - 0.01f : px > prev + 0.01f) folds = true;
                    prev = px;
                    min = Mathf.Min(min, px);
                    max = Mathf.Max(max, px);
                }
                if (sheet == 0) front = f.Map(new Vector2(0f, y)).x - from.x;
            }
            return max - min;
        }

        [Test]
        public void Rig_TurnedDress_KeepsItsWidth_ItsFrontSlides_AndItNeverFolds()
        {
            var skirt = Piece(Rig.Skirt, Layer.Bottom);
            skirt.Profile = new[] { 0.3f, 0.9f };
            skirt.ProfileY0 = 0.8f;
            skirt.ProfileY1 = 2.9f;
            var facing = Pose();
            float width = Span(skirt, facing, 2.9f, 0.9f, facing.HipC, out float front0, out _);
            Assert.AreEqual(0f, front0, 1f, "facing: the front in the middle");
            foreach (float deg in new[] { 30f, 60f, 90f, -45f, 135f, 180f })
            {
                var turned = Turned(deg);
                float span = Span(skirt, turned, 2.9f, 0.9f, turned.HipC, out float front, out bool folds);
                Assert.IsFalse(folds, $"{deg} deg: no folds");
                Assert.AreEqual(width * Turn.Silhouette(deg * Mathf.Deg2Rad, OutfitRig.SkirtDepth), span, width * 0.06f,
                                $"{deg} deg: as wide as a turned round skirt");
                if (Mathf.Abs(deg) < 90f)
                    Assert.AreEqual(Mathf.Sign(deg), Mathf.Sign(front), $"{deg} deg: the front slides the way the chest turns");
            }
            // a top doesn't shrink with the shoulders in the picture: turned 60 degrees (seen only in the picture) it is
            // as wide as a turned chest, much wider than the picture's shoulders
            var top = Piece(Rig.Torso);
            top.Profile = new[] { 0.45f, 0.3f };
            top.ProfileY0 = -0.1f;
            top.ProfileY1 = 1.1f;
            float topWidth = Span(top, facing, 0.1f, 0.42f, facing.ShoulderC, out _, out _);
            var side = Turned(60f, world: false);
            float topSpan = Span(top, side, 0.1f, 0.42f, side.ShoulderC, out _, out bool topFolds);
            Assert.IsFalse(topFolds, "a turned top doesn't fold");
            Assert.AreEqual(topWidth * Turn.Silhouette(60f * Mathf.Deg2Rad, OutfitRig.TorsoDepth), topSpan, topWidth * 0.08f,
                            "a top turned 60 deg is as wide as a turned chest");
            Assert.Greater(topSpan, 1.3f * topWidth * Mathf.Cos(60f * Mathf.Deg2Rad), "not squashed like the picture's shoulders");
        }

        [Test]
        public void Rig_TurnedHead_HatsAndGlassesStayOnTheFace()
        {
            var look = Turned(0f, 40f);
            var f = new RigFrame();
            Vector2 eyes = (look.J[(int)Joint.LeftEye] + look.J[(int)Joint.RightEye]) * 0.5f;
            Vector2 ears = (look.J[(int)Joint.LeftEar] + look.J[(int)Joint.RightEar]) * 0.5f;
            Assert.Greater(eyes.x, ears.x + 5f, "the test head looks to the right");
            OutfitRig.Build(Piece(Rig.Face, Layer.Face), look, new OutfitMotion(), false, f);
            Assert.AreEqual(eyes.x, f.Map(new Vector2(0f, OutfitPose.TemplateEyeY)).x, 0.05f * look.HeadScale, "glasses between the eyes");
            OutfitRig.Build(Piece(Rig.Head, Layer.Head), look, new OutfitMotion(), false, f);
            float crown = f.Map(new Vector2(0f, -0.8f)).x;
            Assert.That(crown, Is.InRange(ears.x, eyes.x + 0.03f * look.HeadScale), "a crown's front turns with the face");
            Assert.IsTrue(Turn.HasBack(f.Wrap, f.WrapYaw), "and shows its back on the far side");

            // from behind: the head is the right way up and turned away (no glasses, the crown's back)
            var back = Turned(160f);
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, back.HeadRoll * Mathf.Rad2Deg), 5f, "the head upright seen from behind");
            Assert.AreEqual(160f, Mathf.DeltaAngle(0f, back.HeadYaw * Mathf.Rad2Deg), 12f, "the head faces away with the body");
            OutfitRig.Build(Piece(Rig.Head, Layer.Head), back, new OutfitMotion(), false, f);
            Assert.Greater(f.Map(new Vector2(0f, -0.8f)).y, f.Map(new Vector2(0f, -0.5f)).y, "a crown's top is up");
        }

        // ---------------------------------------------------------------- poses

        [Test]
        public void Poses_EachIsRecognised_AndNotConfusedWithStanding()
        {
            foreach (ShowPose pose in Enum.GetValues(typeof(ShowPose)))
            {
                var p = Pose(change: j => Strike(j, pose, 200f));
                Assert.GreaterOrEqual(PoseCheck.Score(pose, p.J, p.T), PoseCheck.Pass, $"{pose} itself");
                var standing = Pose();
                Assert.Less(PoseCheck.Score(pose, standing.J, standing.T), PoseCheck.Pass, $"{pose} while just standing");
            }
            var star = Pose(change: j => Strike(j, ShowPose.Star, 200f));
            Assert.Less(PoseCheck.Score(ShowPose.Plane, star.J, star.T), PoseCheck.Pass, "a star is not an airplane");
            Assert.Less(PoseCheck.Score(ShowPose.Hips, star.J, star.T), PoseCheck.Pass);
        }

        /// <summary>The doll's arms put into a pose (pixels, y up, mirrored: the player's left is the screen's left).</summary>
        static void Strike(Vector2[] j, ShowPose pose, float t)
        {
            Vector2 ls = j[(int)Joint.LeftShoulder], rs = j[(int)Joint.RightShoulder], lh = j[(int)Joint.LeftHip], rh = j[(int)Joint.RightHip];
            Vector2 nose = j[(int)Joint.Nose];
            void Arm(bool right, Vector2 elbow, Vector2 wrist)
            {
                j[(int)(right ? Joint.RightElbow : Joint.LeftElbow)] = elbow;
                j[(int)(right ? Joint.RightWrist : Joint.LeftWrist)] = wrist;
            }
            switch (pose)
            {
                case ShowPose.Star:
                    Arm(false, ls + new Vector2(-0.45f, 0.45f) * t, ls + new Vector2(-0.85f, 0.85f) * t);
                    Arm(true, rs + new Vector2(0.45f, 0.45f) * t, rs + new Vector2(0.85f, 0.85f) * t);
                    break;
                case ShowPose.Plane:
                    Arm(false, ls + new Vector2(-0.6f, 0f) * t, ls + new Vector2(-1.2f, 0.02f) * t);
                    Arm(true, rs + new Vector2(0.6f, 0f) * t, rs + new Vector2(1.2f, 0.02f) * t);
                    break;
                case ShowPose.Heart:
                    Arm(false, ls + new Vector2(-0.3f, 0.5f) * t, nose + new Vector2(-0.05f, 0.45f) * t);
                    Arm(true, rs + new Vector2(0.3f, 0.5f) * t, nose + new Vector2(0.05f, 0.45f) * t);
                    break;
                case ShowPose.Hips:
                    Arm(false, ls + new Vector2(-0.3f, -0.45f) * t, lh + new Vector2(-0.05f, 0.05f) * t);
                    Arm(true, rs + new Vector2(0.3f, -0.45f) * t, rh + new Vector2(0.05f, 0.05f) * t);
                    break;
                case ShowPose.Wave:
                    Arm(true, rs + new Vector2(0.35f, 0.4f) * t, rs + new Vector2(0.45f, 1.05f) * t);
                    break;
                case ShowPose.Super:
                    Arm(true, rs + new Vector2(0.05f, 0.6f) * t, rs + new Vector2(0.08f, 1.2f) * t);
                    Arm(false, ls + new Vector2(-0.3f, -0.45f) * t, lh + new Vector2(-0.05f, 0.05f) * t);
                    break;
            }
        }

        static void AssertNear(Vector2 expected, Vector2 actual, string what, float tolerance = 0.5f)
        {
            Assert.Less((expected - actual).magnitude, tolerance, $"{what}: expected {expected}, got {actual}");
        }
    }
}
