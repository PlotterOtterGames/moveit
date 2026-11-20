using UnityEditor;
using UnityEngine;

namespace MoveIt.EditorTools
{
    /// <summary>
    /// Import settings for Superboks!'s art in <c>Assets/Games/Boxing/Art</c>: the kit (Boxing.fbx, art/boxing/boxing.py:
    /// the gloves and the belt, coloured by Tundu/Athlete's regions: the meshes readable and uncompressed, the second UV
    /// channel holds exact region numbers and the baked occlusion), the arenas (Arena.fbx, art/boxing/arena.py: vertex
    /// colours with the AO in alpha, uv0 = glow, custom normals, imported exactly; corners that share everything welded
    /// back into one vertex: on the box every vertex runs the whole light), the HUD's sheet (boxing_ui.png,
    /// tools/boxing/art.py: Vrapo!'s settings). No materials from the models: the scene builder gives them theirs.
    /// </summary>
    sealed class BoxingArtImport : AssetPostprocessor
    {
        public const string ArtDir = "Assets/Games/Boxing/Art/";

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
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            if (assetPath.EndsWith("Arena.fbx"))
            {
                importer.isReadable = false;
                importer.optimizeMeshVertices = true;
                importer.weldVertices = true;
            }
            else importer.isReadable = true;
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ArtDir)) return;
            var importer = (TextureImporter)assetImporter;
            if (System.IO.Path.GetFileNameWithoutExtension(assetPath) != "boxing_ui") return;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaIsTransparency = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
        }
    }
}
