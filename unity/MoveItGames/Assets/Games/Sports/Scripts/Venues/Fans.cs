using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MoveIt.Games.Sports
{
    /// <summary>
    /// A venue's crowd: one quad a taken seat (Tundu/Crowd draws a person from the atlas rendered from the athletes'
    /// model), a few big meshes. They bob along all the time; <see cref="Cheer"/> makes them stand and jump with their
    /// arms up (the keenest first) and settles back over a few seconds. One crowd at a time sets the global
    /// _CrowdCheer.
    /// </summary>
    public sealed class Fans : MonoBehaviour
    {
        const float Width = 1.05f, Height = 1.45f, Below = 0.27f;
        const int PerMesh = 15000;

        float _level, _target, _holdUntil;
        float _base = 0.04f;
        public int People { get; private set; }

        public static Fans Build(Transform parent, string seatsText, Material material, int seed, float occupancy)
        {
            var go = new GameObject("Fans");
            go.transform.SetParent(parent, false);
            var fans = go.AddComponent<Fans>();
            var seats = Venue.ParseSeats(seatsText);
            // Lower rows first: they're nearer every camera on the field, and an alpha-tested quad writes its depth only
            // after it's shaded, so drawing them first lets the depth test skip the parts of the rows behind they hide
            seats.Sort((a, b) => a.y.CompareTo(b.y));
            var rng = new System.Random(seed);
            var verts = new List<Vector3>();
            var uv0 = new List<Vector2>();
            var uv1 = new List<Vector2>();
            var tris = new List<int>();
            int chunk = 0;

            void Flush()
            {
                if (verts.Count == 0) return;
                var mesh = new Mesh { name = $"Fans {chunk}", indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(verts);
                mesh.SetUVs(0, uv0);
                mesh.SetUVs(1, uv1);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                var part = new GameObject($"Fans {chunk++}");
                part.transform.SetParent(go.transform, false);
                part.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = part.AddComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = true;
                verts.Clear();
                uv0.Clear();
                uv1.Clear();
                tris.Clear();
            }

            foreach (var s in seats)
            {
                if (rng.NextDouble() > occupancy) continue;
                float yaw = s.w * Mathf.Deg2Rad;
                var right = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw)) * (Width / 2f);
                var bottom = new Vector3(s.x, s.y - Below, s.z);
                var up = Vector3.up * Height;
                int b = verts.Count;
                verts.Add(bottom - right);
                verts.Add(bottom - right + up);
                verts.Add(bottom + right + up);
                verts.Add(bottom + right);
                uv0.Add(new Vector2(0f, 0f));
                uv0.Add(new Vector2(0f, 1f));
                uv0.Add(new Vector2(1f, 1f));
                uv0.Add(new Vector2(1f, 0f));
                var person = new Vector2(rng.Next(16), (float)rng.NextDouble());
                for (int k = 0; k < 4; k++) uv1.Add(person);
                tris.Add(b);
                tris.Add(b + 1);
                tris.Add(b + 2);
                tris.Add(b);
                tris.Add(b + 2);
                tris.Add(b + 3);
                fans.People++;
                if (verts.Count >= PerMesh * 4) Flush();
            }
            Flush();
            return fans;
        }

        /// <summary>The crowd cheers: <paramref name="amount"/> 0..1 of them on their feet, for a few seconds.</summary>
        public void Cheer(float amount = 1f, float seconds = 2.5f)
        {
            _target = Mathf.Max(_target, Mathf.Clamp01(amount));
            _holdUntil = Mathf.Max(_holdUntil, Time.time + seconds);
        }

        /// <summary>How excited they are when nothing happens (a tense moment: a little more).</summary>
        public void Buzz(float level) => _base = Mathf.Clamp01(level);

        void Update()
        {
            if (Time.time > _holdUntil) _target = _base;
            _level = Mathf.MoveTowards(_level, _target, Time.deltaTime * (_target > _level ? 3f : 0.6f));
            Shader.SetGlobalFloat("_CrowdCheer", _level);
        }
    }
}
