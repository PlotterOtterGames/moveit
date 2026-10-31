using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MoveIt.Games.Dress.Tests
{
    /// <summary>The art from tools/dress (outfits.py, ui_art.py) matches the catalog and the game's drawing code.</summary>
    public class DressArtTests
    {
        const string Art = "Assets/Games/Dress/Art/";

        static OutfitArt Load() => OutfitArt.Load(AssetDatabase.LoadAssetAtPath<TextAsset>(Art + "outfit_atlas.json"),
            AssetDatabase.LoadAssetAtPath<TextAsset>(Art + "dress_ui.json"), null, null, null, null, null, null);

        [Test]
        public void EveryItem_HasPiecesAndAThumbnail()
        {
            var art = Load();
            foreach (var item in DressCatalog.All)
            {
                var pieces = art.Pieces(item.Id);
                Assert.Greater(pieces.Count, 0, $"{item.Id} has no art (run tools/dress/outfits.py)");
                Assert.Greater(art.ThumbRect(item.Id).width, 0f, $"{item.Id} has no thumbnail (run tools/dress/ui_art.py)");
                foreach (var p in pieces)
                {
                    Assert.IsTrue(p.X >= 0 && p.Y >= 0 && p.X + p.W <= art.AtlasWidth && p.Y + p.H <= art.AtlasHeight, $"{p.Name} outside the atlas");
                    Assert.Greater(p.Ppt, 0f, p.Name);
                    Assert.AreEqual(0, p.Sparkles.Length % 3, p.Name);
                    bool handSlot = item.Slot == Slot.Hand;
                    Assert.AreEqual(handSlot, p.Rig == Rig.Hand, $"{p.Name}: hand items and only they use the hand rig");
                    // wings and capes go behind the body; their clasps and fur collars (torso rig) in front
                    if (item.Slot == Slot.Back && p.Rig != Rig.Torso) Assert.AreEqual(Layer.Back, p.Layer, $"{p.Name}: behind the body");
                }
            }
            Assert.Greater(art.Pieces("doll").Count, 3, "the paper doll");
            Assert.AreNotEqual(Vector2.zero, art.WhiteUv, "white texels for the balloon string");
            var balloon = art.Pieces("hand_balloon")[0];
            Assert.AreEqual(HandMode.Float, balloon.Mode);
            Assert.Greater(balloon.Knot.y, 0f);
            Assert.AreNotEqual(Vector2.zero, art.Pieces("hand_wand")[0].Tip, "the wand's star leaves a trail");
            Assert.IsTrue(art.Pieces("hair_long").Any(p => p.Layer == Layer.Back) && art.Pieces("hair_long").Any(p => p.Layer == Layer.HairFront));
        }

        [Test]
        public void EveryImageTheGameDraws_IsInTheSheet()
        {
            var art = Load();
            var names = new[]
            {
                "cat_top", "cat_bottom", "cat_hair", "cat_head", "cat_face", "cat_back", "cat_hand", "act_magic", "act_dice",
                "act_show", "act_photo", "act_remove", "act_ready", "none", "glow", "bulb", "bubble_button", "panel", "curtain",
                "star_full", "star_empty", "judge_cat", "judge_bunny", "judge_dino",
            };
            foreach (var n in names) Assert.IsTrue(art.HasUi(n), n);
            foreach (Theme t in System.Enum.GetValues(typeof(Theme))) Assert.IsTrue(art.HasUi("theme_" + t.ToString().ToLowerInvariant()), t.ToString());
            foreach (var p in new[] { "star", "hips", "heart", "wave", "super", "plane" }) Assert.IsTrue(art.HasUi("pose_" + p), p);
        }

        [Test]
        public void Textures_AreImportedForTheShader()
        {
            foreach (var name in new[] { "outfit_mask", "outfit_thumbs_mask" })
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(Art + name + ".png");
                Assert.IsNotNull(importer, name);
                Assert.IsFalse(importer.sRGBTexture, $"{name}: data, not colour");
                Assert.AreEqual(TextureImporterType.SingleChannel, importer.textureType, name);
            }
            var atlas = (TextureImporter)AssetImporter.GetAtPath(Art + "outfit_atlas.png");
            // as data since 2026-10-31: Tundu/Outfit works in sRGB and converts once at the end (cheaper on the box)
            Assert.IsFalse(atlas.sRGBTexture);
            Assert.IsTrue(atlas.mipmapEnabled);
            Assert.AreEqual(4096, atlas.maxTextureSize);
        }
    }
}
