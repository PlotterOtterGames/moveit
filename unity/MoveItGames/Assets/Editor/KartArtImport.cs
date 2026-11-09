using UnityEditor;
using UnityEngine;

namespace MoveIt.EditorTools
{
    /// <summary>
    /// Import settings for Gara!'s art in <c>Assets/Games/Kart/Art</c> (made by <c>art/kart/build.py</c>): the kart and the
    /// scenery bring no materials (the scene builder gives them theirs: Tundu/Athlete's colour regions for the kart, the
    /// palette's Tundu/Toon for the scenery), their meshes stay readable (the scenery is static-batched when a track is
    /// built) and uncompressed (the kart's second UV channel holds exact region numbers); the palette keeps hard pixels.
    /// </summary>
    sealed class KartArtImport : AssetPostprocessor
    {
        public const string ArtDir = "Assets/Games/Kart/Art/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ArtDir) || !assetPath.EndsWith("tundu_palette.png")) return;
            var importer = (TextureImporter)assetImporter;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
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
        }
    }
}
