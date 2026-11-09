using UnityEditor;
using UnityEngine;

namespace MoveIt.EditorTools
{
    /// <summary>
    /// Import settings for the chibi characters (art/chibi, exported into <c>Assets/Games/Common/Characters/Resources/Chibi</c>):
    /// no materials (Tundu/Athlete colours them per region), meshes readable and uncompressed (the game merges a
    /// character's pieces into one mesh; the second UV channel holds exact region numbers), bone transforms kept (games
    /// pose the bones). Makes the characters' material next to the model when it is missing.
    /// </summary>
    sealed class ChibiArtImport : AssetPostprocessor
    {
        public const string ArtDir = "Assets/Games/Common/Characters/Resources/Chibi/";
        const string MaterialPath = ArtDir + "ChibiMaterial.mat";

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
            importer.useFileScale = true;
            importer.globalScale = 1f;
            importer.isReadable = true;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.optimizeGameObjects = false;
            importer.animationType = ModelImporterAnimationType.Generic;
        }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (string path in imported)
            {
                if (!path.StartsWith(ArtDir) || !path.EndsWith(".fbx")) continue;
                if (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath) != null) return;
                var shader = Shader.Find("Tundu/Athlete");
                if (shader == null) return;
                var mat = new Material(shader) { name = "ChibiMaterial" };
                mat.SetFloat("_Cull", 0f);   // two-sided: hems and sleeves are open shells
                AssetDatabase.CreateAsset(mat, MaterialPath);
                return;
            }
        }
    }
}
