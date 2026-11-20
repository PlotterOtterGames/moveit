using System;
using UnityEditor;
using UnityEngine;

namespace MoveIt.EditorTools
{
    /// <summary>
    /// Import settings for the art in <c>Assets/Games/Runner/Art</c>. The console polish of 2026-11-18 (art/runner,
    /// tools/runner): the valley's kit (Valley.fbx: vertex colours with the AO in alpha, uv0 = sway and glow, custom normals:
    /// imported exactly, no materials), Dino (Dino.fbx: his clips named and the loops looping), the HUD sheet
    /// (runner_ui.png: Zana!'s settings), the ground's detail (ground_detail.png: data, tiling, compressed like Zana!'s
    /// meadow), the particles' textures. The old props (Log, Boulders, Fern1... made by art/runner/world.py and sky.py) stay
    /// for Gara!'s dino valley: the palette keeps hard pixels, models bring no materials.
    /// </summary>
    sealed class RunnerArtImport : AssetPostprocessor
    {
        public const string ArtDir = "Assets/Games/Runner/Art/";
        static readonly string[] LoopingClips = { "Idle", "LookAround", "Wave", "Ready", "Run", "Slide", "Dizzy", "Cheer", "Dance", "Fly" };

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ArtDir)) return;
            var importer = (TextureImporter)assetImporter;
            string file = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            if (file == "tundu_palette")
            {
                importer.sRGBTexture = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Point;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                return;
            }
            if (file == "ground_detail")
            {
                // tools/runner/ground.py: data (grass dabs, the path's pebbles, cracks and footprints, the edge's wobble),
                // tiling with the floor's metres; compressed and no anisotropic filtering (Zana!'s meadow: on the box the
                // uncompressed, anisotropic one was its biggest single cost)
                importer.textureType = TextureImporterType.Default;
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
            if (file == "runner_ui")
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.alphaIsTransparency = true;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Bilinear;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                return;
            }
            if (file.StartsWith("fx_"))
            {
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
            }
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
            if (assetPath.EndsWith("Valley.fbx") || assetPath.EndsWith("Dino.fbx"))
            {
                // the kit's and Dino's vertex data exactly as made (vertex colours, uv0 = sway/gloss and glow, the normals)
                importer.meshCompression = ModelImporterMeshCompression.Off;
                importer.importNormals = ModelImporterNormals.Import;
                importer.importTangents = ModelImporterTangents.None;
                // corners that share everything (position, normal, colour, uv) become one vertex again (the FBX lists
                // them per corner), and the vertices in the order the GPU's cache likes: the box runs the whole light for
                // every vertex, so vertices are its cost
                importer.optimizeMeshVertices = true;
                importer.weldVertices = true;
            }
            if (assetPath.EndsWith("Valley.fbx"))
            {
                importer.importVisibility = false;
                importer.importAnimation = false;
                importer.animationType = ModelImporterAnimationType.None;
            }
        }

        void OnPreprocessAnimation()
        {
            if (!assetPath.StartsWith(ArtDir) || assetPath.EndsWith("Valley.fbx")) return;
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
