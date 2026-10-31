using UnityEditor;
using UnityEngine;

namespace MoveIt.EditorTools
{
    /// <summary>
    /// Vishu!'s art (<c>Assets/Games/Dress/Art</c>, made by <c>tools/dress/outfits.py</c> and <c>ui_art.py</c>): the
    /// outfit atlas and thumbnails with mipmaps (clothes are drawn smaller than the art), their tint masks as linear
    /// single-channel data, the interface and particle sheets (sRGB). The atlas and the thumbnails are imported as
    /// data, not sRGB: Tundu/Outfit does its colour maths on their sRGB values, and converting each texel back from
    /// linear per pixel was most of its cost on the box. On Android the colour sheets are ASTC (the boxes' Mali GPUs
    /// have it: the 4096 x 4096 atlas is ~7.5 MB in memory instead of 64), the masks EAC R11.
    /// </summary>
    sealed class DressArtImport : AssetPostprocessor
    {
        public const string Folder = "Assets/Games/Dress/Art/";

        // 2: the atlas and thumbnails as data (re-imports them)
        public override uint GetVersion() => 2;

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder) || !assetPath.EndsWith(".png")) return;
            var importer = (TextureImporter)assetImporter;
            bool mask = assetPath.EndsWith("_mask.png");
            bool outfitColours = assetPath.EndsWith("outfit_atlas.png") || assetPath.EndsWith("outfit_thumbs.png");
            importer.textureType = mask ? TextureImporterType.SingleChannel : TextureImporterType.Default;
            if (mask)
            {
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.singleChannelComponent = TextureImporterSingleChannelComponent.Red;
                importer.SetTextureSettings(settings);
            }
            importer.sRGBTexture = !mask && !outfitColours;
            importer.alphaSource = mask ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = !mask;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 4096;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Android", overridden = true, maxTextureSize = 4096,
                format = mask ? TextureImporterFormat.EAC_R : TextureImporterFormat.ASTC_6x6,
                compressionQuality = 60,
            });
        }
    }
}
