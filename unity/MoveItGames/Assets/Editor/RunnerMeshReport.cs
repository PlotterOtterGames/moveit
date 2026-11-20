using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MoveIt.EditorTools
{
    /// <summary>
    /// Vrapo!'s art as Unity imported it: each kit mesh's vertices and triangles (the box's GPU runs the whole light for
    /// every vertex, so vertices are its cost). Batch: -executeMethod MoveIt.EditorTools.RunnerMeshReport.Print.
    /// </summary>
    static class RunnerMeshReport
    {
        /// <summary>Imports Vrapo!'s two models again (after a change to RunnerArtImport: Unity doesn't reimport for a
        /// postprocessor's code), then prints the report.</summary>
        public static void ReimportAndPrint()
        {
            foreach (var path in new[] { "Assets/Games/Runner/Art/Valley.fbx", "Assets/Games/Runner/Art/Dino.fbx" })
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            Print();
        }

        [MenuItem("MoveIt/Report/Vrapo! meshes")]
        public static void Print()
        {
            foreach (var path in new[] { "Assets/Games/Runner/Art/Valley.fbx", "Assets/Games/Runner/Art/Dino.fbx" })
            {
                var meshes = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().OrderBy(m => m.name).ToList();
                foreach (var m in meshes)
                    Debug.Log($"[RunnerMeshes] {System.IO.Path.GetFileNameWithoutExtension(path)}/{m.name}: {m.vertexCount} vertices, {m.triangles.Length / 3} triangles");
            }
        }
    }
}
