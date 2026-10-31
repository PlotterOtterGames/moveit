using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Dress
{
    /// <summary>
    /// Vishu!'s art, as made by tools/dress/outfits.py and ui_art.py: the outfit atlas and its manifest (pieces and
    /// how they follow the body), the item thumbnails, the interface sheet (icons, poses, judges, curtains) and the
    /// particle shapes.
    /// </summary>
    public sealed class OutfitArt
    {
        [Serializable] sealed class PieceJson
        {
            public string name, item, rig, layer, side, mode;
            public float[] rect, origin, pivot, tip, knot, sparkles, profile, profileY;
            public float ppt, flap, hang;
        }

        [Serializable] sealed class ItemJson
        {
            public string id;
            public string[] pieces;
        }

        [Serializable] sealed class AtlasJson
        {
            public float[] atlas, white;
            public PieceJson[] pieces;
            public ItemJson[] items;
        }

        [Serializable] sealed class RectJson
        {
            public string name;
            public float[] rect;
        }

        [Serializable] sealed class UiJson
        {
            public float[] size, thumbSize;
            public RectJson[] rects, thumbs;
        }

        public Texture2D Atlas, Mask, Thumbs, ThumbsMask, Ui, Fx;
        public float AtlasWidth, AtlasHeight;
        /// <summary>UV of a block of opaque white texels (lines).</summary>
        public Vector2 WhiteUv;

        readonly Dictionary<string, List<PieceDef>> _pieces = new Dictionary<string, List<PieceDef>>();
        readonly Dictionary<string, Rect> _ui = new Dictionary<string, Rect>(), _thumbs = new Dictionary<string, Rect>();
        static readonly List<PieceDef> None = new List<PieceDef>();

        public int PieceCount { get; private set; }

        /// <summary>The pieces of an item (empty if the atlas has none).</summary>
        public List<PieceDef> Pieces(string item) => item != null && _pieces.TryGetValue(item, out var list) ? list : None;

        public bool HasItem(string item) => _pieces.ContainsKey(item);

        /// <summary>A named image of the interface sheet as texture coordinates (for GUI.DrawTextureWithTexCoords).</summary>
        public Rect UiRect(string name) => _ui.TryGetValue(name, out var r) ? r : new Rect(0, 0, 0, 0);

        public bool HasUi(string name) => _ui.ContainsKey(name);

        /// <summary>An item's thumbnail as texture coordinates in <see cref="Thumbs"/>.</summary>
        public Rect ThumbRect(string item) => _thumbs.TryGetValue(item, out var r) ? r : new Rect(0, 0, 0, 0);

        public static OutfitArt Load(TextAsset atlasJson, TextAsset uiJson, Texture2D atlas, Texture2D mask, Texture2D thumbs,
                                     Texture2D thumbsMask, Texture2D ui, Texture2D fx)
        {
            var art = new OutfitArt { Atlas = atlas, Mask = mask, Thumbs = thumbs, ThumbsMask = thumbsMask, Ui = ui, Fx = fx };
            if (atlasJson != null) art.ReadAtlas(JsonUtility.FromJson<AtlasJson>(atlasJson.text));
            if (uiJson != null) art.ReadUi(JsonUtility.FromJson<UiJson>(uiJson.text));
            return art;
        }

        void ReadAtlas(AtlasJson json)
        {
            AtlasWidth = json.atlas[0];
            AtlasHeight = json.atlas[1];
            if (json.white != null && json.white.Length == 2) WhiteUv = new Vector2(json.white[0] / AtlasWidth, 1f - json.white[1] / AtlasHeight);
            foreach (var p in json.pieces)
            {
                var def = new PieceDef
                {
                    Name = p.name, Item = p.item, Rig = ParseRig(p.rig), Layer = ParseLayer(p.layer),
                    X = p.rect[0], Y = p.rect[1], W = p.rect[2], H = p.rect[3],
                    OriginX = p.origin[0], OriginY = p.origin[1], Ppt = p.ppt,
                    BothSides = p.side == "both",
                    Pivot = V(p.pivot), Flap = p.flap, Mode = ParseMode(p.mode), Tip = V(p.tip), Knot = V(p.knot),
                    Hang = p.hang >= 90f ? float.PositiveInfinity : p.hang,
                    Sparkles = p.sparkles ?? Array.Empty<float>(),
                    Profile = p.profile ?? Array.Empty<float>(),
                    ProfileY0 = p.profileY != null && p.profileY.Length == 2 ? p.profileY[0] : 0f,
                    ProfileY1 = p.profileY != null && p.profileY.Length == 2 ? p.profileY[1] : 1f,
                };
                if (!_pieces.TryGetValue(def.Item, out var list)) _pieces[def.Item] = list = new List<PieceDef>();
                list.Add(def);
                PieceCount++;
            }
            foreach (var list in _pieces.Values) list.Sort((a, b) => a.Layer.CompareTo(b.Layer));
        }

        void ReadUi(UiJson json)
        {
            float w = json.size[0], h = json.size[1];
            foreach (var r in json.rects) _ui[r.name] = Uv(r.rect, w, h);
            float tw = json.thumbSize[0], th = json.thumbSize[1];
            foreach (var r in json.thumbs) _thumbs[r.name] = Uv(r.rect, tw, th);
        }

        /// <summary>Pixel rect (y down) -> texture coordinates (y up).</summary>
        static Rect Uv(float[] r, float w, float h) => new Rect(r[0] / w, 1f - (r[1] + r[3]) / h, r[2] / w, r[3] / h);

        static Vector2 V(float[] a) => a != null && a.Length >= 2 ? new Vector2(a[0], a[1]) : Vector2.zero;

        static Rig ParseRig(string s) => s switch
        {
            "torso" => Rig.Torso, "skirt" => Rig.Skirt, "tail" => Rig.Tail, "arm" => Rig.Arm, "leg" => Rig.Leg,
            "head" => Rig.Head, "hair" => Rig.Hair, "face" => Rig.Face, "back" => Rig.Back, "cape" => Rig.Cape,
            _ => Rig.Hand,
        };

        static Layer ParseLayer(string s) => s switch
        {
            "back" => Layer.Back, "doll" => Layer.Doll, "bottom" => Layer.Bottom, "top" => Layer.Top, "sleeve" => Layer.Sleeve,
            "hairfront" => Layer.HairFront, "head" => Layer.Head, "face" => Layer.Face, _ => Layer.Hand,
        };

        static HandMode ParseMode(string s) => s switch
        {
            "upright" => HandMode.Upright, "hang" => HandMode.Hang, "float" => HandMode.Float, _ => HandMode.Along,
        };
    }
}
