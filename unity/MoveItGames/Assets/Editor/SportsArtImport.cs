using System;
using MoveIt.Games.Sports;
using UnityEditor;
using UnityEngine;

namespace MoveIt.EditorTools
{
    /// <summary>
    /// Import settings for Sporte!'s Blender art in <c>Assets/Games/Sports/Art</c> (made by <c>art/sports/*.py</c>):
    /// models bring no materials (the athletes use Tundu/Athlete, coloured per region), meshes stay readable and
    /// uncompressed (the game merges an athlete's pieces into one mesh; the second UV channel holds exact region numbers),
    /// bone transforms stay (the game turns heads and bends arms on top of the clips), and the clips are named after
    /// their Blender actions, the looping ones looped (<see cref="AthleteData.Clips"/>).
    /// </summary>
    sealed class SportsArtImport : AssetPostprocessor
    {
        public const string ArtDir = "Assets/Games/Sports/Art/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ArtDir)) return;
            var importer = (TextureImporter)assetImporter;
            string file = System.IO.Path.GetFileName(assetPath);
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            // ground details are multiplied with the vertex colours: their greys are used as they are
            importer.sRGBTexture = !file.EndsWith("_detail.png");
            if (file == "crowd_atlas.png" || file == "net.png")
            {
                importer.alphaIsTransparency = true;
                importer.mipMapsPreserveCoverage = true;
                importer.alphaTestReferenceValue = 0.5f;
                importer.wrapMode = file == "net.png" ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            }
            if (file == "screen.png") importer.wrapMode = TextureWrapMode.Clamp;
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
            importer.useFileScale = true;
            importer.globalScale = 1f;
            importer.isReadable = true;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.optimizeGameObjects = false;
            importer.animationCompression = ModelImporterAnimationCompression.Optimal;
            importer.resampleCurves = true;
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
                clip.name = name;
                bool loop = Array.Exists(AthleteData.Clips, c => c.Name == name && c.Loop);
                clip.loopTime = loop;
                // a looping clip's last key repeats its first: leave it out so the loop doesn't stall a frame
                if (loop && clip.lastFrame - clip.firstFrame > 2) clip.lastFrame -= 1;
            }
            importer.clipAnimations = clips;
        }
    }
}
