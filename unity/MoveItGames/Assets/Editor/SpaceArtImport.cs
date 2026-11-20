using UnityEditor;
using UnityEngine;

namespace MoveIt.EditorTools
{
    /// <summary>
    /// Import settings for Yjet!'s art in <c>Assets/Games/Space/Art</c> (art/space/build.py's props, tools/space/art.py's
    /// pictures): the props bring no materials (the scene builder gives them the palette's Tundu/Toon) and stay readable
    /// and uncompressed (their uvs point at exact palette cells); the palette keeps hard pixels; the planets' maps wrap
    /// round (and keep their glow in alpha); the HUD sheet keeps its size and full quality (its words and figures are
    /// drawn about its own size).
    /// </summary>
    sealed class SpaceArtImport : AssetPostprocessor
    {
        public const string ArtDir = "Assets/Games/Space/Art/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ArtDir)) return;
            var importer = (TextureImporter)assetImporter;
            string file = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaIsTransparency = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            if (file == "tundu_palette")
            {
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Point;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                return;
            }
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Bilinear;
            if (file == "planet_heights")
            {
                // tools/space/heights.py's eight height maps stacked: one 8-slice array, data (not colour), wrapping round
                // the planet; one channel (EAC R11 on the box: 11 bits, 4 bits a pixel)
                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.alphaIsTransparency = false;
                importer.wrapModeU = TextureWrapMode.Repeat;
                importer.wrapModeV = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 2;
                importer.maxTextureSize = 8192;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.textureShape = TextureImporterShape.Texture2DArray;
                settings.flipbookColumns = 1;
                settings.flipbookRows = 8;
                importer.SetTextureSettings(settings);
                importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
                    { name = "Standalone", overridden = true, maxTextureSize = 8192, format = TextureImporterFormat.R8 });
                importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
                    { name = "Android", overridden = true, maxTextureSize = 8192, format = TextureImporterFormat.EAC_R });
                return;
            }
            if (file == "space_sky")
            {
                // tools/space/sky.py's panorama: sampled at mip 0 by Tundu/SkyPanorama (a mip chain would draw a seam
                // where the longitude wraps), magnified on the screen anyway
                importer.mipmapEnabled = false;
                importer.wrapModeU = TextureWrapMode.Repeat;
                importer.wrapModeV = TextureWrapMode.Clamp;
                importer.alphaIsTransparency = false;
                importer.maxTextureSize = 4096;
                importer.textureCompression = TextureImporterCompression.Compressed;
                return;
            }
            if (file.StartsWith("planet_") || file == "clouds")
            {
                // round the planet: the map wraps across, not up and down (the clouds, tools/space/heights.py's, are seen
                // close up: their full 2048)
                importer.wrapModeU = TextureWrapMode.Repeat;
                importer.wrapModeV = TextureWrapMode.Clamp;
                importer.alphaIsTransparency = false;
                importer.maxTextureSize = file == "clouds" ? 2048 : 1024;
                importer.textureCompression = TextureImporterCompression.Compressed;
                return;
            }
            importer.wrapMode = TextureWrapMode.Clamp;
            if (file == "space_ui")
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 1024;
                return;
            }
            importer.maxTextureSize = 512;
            importer.textureCompression = TextureImporterCompression.Compressed;
        }

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ArtDir)) return;
            var importer = (ModelImporter)assetImporter;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.useFileScale = true;
            importer.globalScale = 1f;
            importer.isReadable = true;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
        }
    }
}
