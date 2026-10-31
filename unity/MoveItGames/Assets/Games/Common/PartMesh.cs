using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MoveIt.Games
{
    /// <summary>
    /// One mesh made of Unity's primitive shapes, each moved, turned, scaled and coloured (vertex colour rgb, alpha =
    /// how glossy), so a character part built from a dozen shapes is one draw call (Tundu/Toon with vertex colours). Used by
    /// Fluturo! (the hero, robots, the city's props) and Tenis! (the players, the stadium).
    /// </summary>
    public sealed class PartMesh
    {
        static readonly Dictionary<PrimitiveType, Mesh> Builtin = new Dictionary<PrimitiveType, Mesh>();
        static readonly Dictionary<Mesh, (Vector3[] v, Vector3[] n, int[] t)> Sources = new Dictionary<Mesh, (Vector3[], Vector3[], int[])>();
        readonly List<Vector3> _v = new List<Vector3>();
        readonly List<Vector3> _n = new List<Vector3>();
        readonly List<Color> _c = new List<Color>();
        readonly List<int> _t = new List<int>();

        /// <summary>The colours given are the palette's (sRGB) and are stored as linear values, so they show as picked in
        /// this linear-colour project (vertex colours aren't converted; without this a colour comes out paler).</summary>
        public bool Srgb { get; set; }

        /// <summary>Unity's mesh for a primitive (taken once from a throwaway primitive).</summary>
        public static Mesh Primitive(PrimitiveType type)
        {
            if (Builtin.TryGetValue(type, out var mesh) && mesh != null) return mesh;
            var go = GameObject.CreatePrimitive(type);
            mesh = go.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(go);
            return Builtin[type] = mesh;
        }

        public PartMesh Add(PrimitiveType type, Vector3 position, Vector3 scale, Color color, float gloss = 0.3f, Vector3 euler = default) =>
            Add(type, Matrix4x4.TRS(position, Quaternion.Euler(euler), scale), color, gloss);

        public PartMesh Add(PrimitiveType type, Matrix4x4 transform, Color color, float gloss = 0.3f) =>
            Add(type == PrimitiveType.Sphere ? Sphere(transform.lossyScale) : Primitive(type), transform, color, gloss);

        static readonly Dictionary<int, Mesh> Spheres = new Dictionary<int, Mesh>();

        /// <summary>A ball for a part this big: Unity's sphere has 768 triangles, which a robot's bolt doesn't need (with
        /// a wave of robots on screen the triangles add up on the box's GPU).</summary>
        static Mesh Sphere(Vector3 scale)
        {
            float size = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            int segments = size >= 0.5f ? 22 : size >= 0.15f ? 14 : 8;
            if (Spheres.TryGetValue(segments, out var mesh) && mesh != null) return mesh;
            int rings = segments * 2 / 3;
            var v = new List<Vector3>();
            var t = new List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float polar = Mathf.PI * r / rings;
                for (int s = 0; s <= segments; s++)
                {
                    float around = 2f * Mathf.PI * s / segments;
                    v.Add(new Vector3(Mathf.Sin(polar) * Mathf.Cos(around), Mathf.Cos(polar), Mathf.Sin(polar) * Mathf.Sin(around)) * 0.5f);
                }
            }
            for (int r = 0; r < rings; r++)
            for (int s = 0; s < segments; s++)
            {
                int a = r * (segments + 1) + s, b = a + segments + 1;
                if (r > 0) t.AddRange(new[] { a, a + 1, b });                 // clockwise seen from outside (Unity's front)
                if (r < rings - 1) t.AddRange(new[] { a + 1, b + 1, b });
            }
            mesh = new Mesh { name = $"Sphere {segments}" };
            mesh.SetVertices(v);
            mesh.SetNormals(v.ConvertAll(p => p.normalized));
            mesh.SetTriangles(t, 0);
            return Spheres[segments] = mesh;
        }

        public PartMesh Add(Mesh mesh, Matrix4x4 transform, Color color, float gloss)
        {
            int start = _v.Count;
            var normalMatrix = transform.inverse.transpose;
            if (Srgb) color = color.linear;
            // reading mesh.vertices copies the array: keep one copy per source mesh
            if (!Sources.TryGetValue(mesh, out var src)) Sources[mesh] = src = (mesh.vertices, mesh.normals, mesh.triangles);
            for (int i = 0; i < src.v.Length; i++)
            {
                _v.Add(transform.MultiplyPoint3x4(src.v[i]));
                _n.Add(normalMatrix.MultiplyVector(src.n[i]).normalized);
                _c.Add(new Color(color.r, color.g, color.b, gloss));
            }
            foreach (int index in src.t) _t.Add(start + index);
            return this;
        }

        public Mesh Build(string name)
        {
            var mesh = new Mesh { name = name };
            if (_v.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;   // a whole stadium in one mesh
            mesh.SetVertices(_v);
            mesh.SetNormals(_n);
            mesh.SetColors(_c);
            mesh.SetTriangles(_t, 0, true);
            return mesh;
        }

        /// <summary>A child object drawing <paramref name="mesh"/> with <paramref name="material"/> (no shadows).</summary>
        public static MeshRenderer Show(string name, Transform parent, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return r;
        }
    }
}
