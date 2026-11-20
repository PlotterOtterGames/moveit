using UnityEditor;
using UnityEngine;

namespace MoveIt.EditorTools
{
    /// <summary>
    /// Import settings for Zana!'s art in <c>Assets/Games/Fairy/Art</c> (art/fairy/build.py's creature kit and garden kit,
    /// tools/fairy/art.py's HUD sheet, tools/fairy/ground.py's meadow detail): the models bring no materials (the scene
    /// builder gives them Tundu/Gloom and Tundu/Realm) and stay readable and uncompressed (the creatures' meshes are put
    /// together at runtime from the kit's pieces, and their second uv set holds exact region numbers and baked occlusion;
    /// the garden kit's vertex colours and uvs carry its colours, occlusion, sway and glow; its normals are its own); the
    /// HUD sheet keeps its full quality, with mips (its bubbles and shapes are also drawn over the creatures in the
    /// world); the meadow detail is tiling data.
    /// </summary>
    sealed class FairyArtImport : AssetPostprocessor
    {
        public const string ArtDir = "Assets/Games/Fairy/Art/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ArtDir)) return;
            var importer = (TextureImporter)assetImporter;
            string file = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaIsTransparency = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            if (file == "garden_detail")
            {
                // tools/fairy/ground.py: data (grass dabs, cobbles, the path's edge) read in world space, tiling. Compressed
                // (ETC2 on the box) and without anisotropic filtering: the meadow covers half the screen at a grazing angle,
                // and uncompressed with 2x anisotropy it was the box's biggest single cost (2026-11-17)
                importer.sRGBTexture = false;
                importer.alphaIsTransparency = false;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Bilinear;
                importer.anisoLevel = 0;
                importer.maxTextureSize = 512;
                importer.textureCompression = TextureImporterCompression.Compressed;
                return;
            }
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
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
            importer.optimizeMeshVertices = false;
            importer.weldVertices = false;
        }
    }
}
