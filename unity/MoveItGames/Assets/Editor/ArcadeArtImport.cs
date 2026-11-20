using UnityEditor;
using UnityEngine;

namespace MoveIt.EditorTools
{
    /// <summary>
    /// Lunapark!'s art (<c>Assets/Games/Arcade/Art</c>, made by <c>tools/arcade/art.py</c>): the sprite sheet (2048 x 2544,
    /// not a power of two: kept at its size, OpenGL ES 3 has no trouble with it) and the wall board, sRGB with mipmaps
    /// (sprites are drawn at many sizes), clamped; ASTC 6x6 on Android (~4.6 MB for the sheet instead of 20).
    /// </summary>
    sealed class ArcadeArtImport : AssetPostprocessor
    {
        public const string Folder = "Assets/Games/Arcade/Art/";

        public override uint GetVersion() => 1;

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder) || !assetPath.EndsWith(".png")) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 4096;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Android", overridden = true, maxTextureSize = 4096, format = TextureImporterFormat.ASTC_6x6, compressionQuality = 60,
            });
        }
    }
}
