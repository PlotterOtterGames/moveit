using System;
using UnityEditor;
using UnityEngine;

namespace MoveIt.EditorTools
{
    /// <summary>
    /// Import settings for the Blender art in <c>Assets/Games/Runner/Art</c> (made by <c>art/runner/*.py</c>):
    /// the palette texture keeps hard pixels (every face's UVs sit in one colour cell), models bring no materials
    /// (the scene builder gives them the shared palette material), and the dino's clips are named and looped.
    /// </summary>
    sealed class RunnerArtImport : AssetPostprocessor
    {
        public const string ArtDir = "Assets/Games/Runner/Art/";
        static readonly string[] LoopingClips = { "Idle", "Run", "Slide", "Cheer", "Fly" };

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
            importer.useFileScale = true;
            importer.globalScale = 1f;
        }

        void OnPreprocessAnimation()
        {
            if (!assetPath.StartsWith(ArtDir)) return;
            var importer = (ModelImporter)assetImporter;
            var clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0)
            {
                importer.animationType = ModelImporterAnimationType.None;
                return;
            }
            importer.animationType = ModelImporterAnimationType.Generic;
            foreach (var clip in clips)
            {
                // Blender names takes "Armature|Action"
                string name = clip.takeName;
                int bar = name.LastIndexOf('|');
                if (bar >= 0) name = name.Substring(bar + 1);
                if (clips.Length == 1 && assetPath.EndsWith("Ptero.fbx")) name = "Fly";   // single take: its only clip
                clip.name = name;
                clip.loopTime = Array.IndexOf(LoopingClips, name) >= 0;
            }
            importer.clipAnimations = clips;
        }
    }
}
