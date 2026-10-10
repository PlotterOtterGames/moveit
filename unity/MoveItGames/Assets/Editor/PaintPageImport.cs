using UnityEditor;

namespace MoveIt.EditorTools
{
    /// <summary>
    /// Import settings for Pikturo's colouring pages (Assets/Resources/Paint/Pages, made by tools/paint/pages.py or
    /// dropped in by hand: any PNG with dark lines on a transparent background). Only the alpha channel is kept
    /// (2 MB per 1920×1080 page instead of 8), uncompressed so the lines stay crisp, and readable, because the paint
    /// bucket uses the lines as walls.
    /// </summary>
    sealed class PaintPageImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.Contains("/Resources/Paint/Pages/")) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.SingleChannel;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.singleChannelComponent = TextureImporterSingleChannelComponent.Alpha;
            importer.SetTextureSettings(settings);
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.isReadable = true;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            importer.maxTextureSize = 2048;
        }
    }
}
