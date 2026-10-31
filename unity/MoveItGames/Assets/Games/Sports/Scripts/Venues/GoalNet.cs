using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MoveIt.Games.Sports
{
    /// <summary>
    /// A football goal's net (the frame is in the venue): the roof from the crossbar down to the back bar, the back down
    /// to the grass and the two sides, a grid of cords (Tundu/Net). A ball in the net bulges it where it hit, then it
    /// swings back. Local space: x across the goal, y up, z into the net; the goal line at z = 0.
    /// </summary>
    public sealed class GoalNet : MonoBehaviour
    {
        const float HalfWidth = 3.72f, Height = 2.44f, Depth = 2.0f, BackHeight = 2.0f, Cell = 0.12f;

        Mesh _mesh;
        Vector3[] _base, _moved;
        Vector3 _hitAt, _push;
        float _hitTime = -10f;

        /// <summary>A net behind a goal line at <paramref name="lineZ"/>; <paramref name="into"/> = +1 if it goes to +z.</summary>
        public static GoalNet Build(Transform parent, float lineZ, float into, Material material)
        {
            var go = new GameObject("Goal net");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0f, lineZ);
            go.transform.localRotation = into > 0 ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
            var net = go.AddComponent<GoalNet>();
            net.Make(material);
            return net;
        }

        void Make(Material material)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            // a sheet between four corners, nu x nv cells, uv in net cells
            void Sheet(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int nu, int nv)
            {
                int start = verts.Count;
                float lu = (b - a).magnitude, lv = (d - a).magnitude;
                for (int j = 0; j <= nv; j++)
                    for (int i = 0; i <= nu; i++)
                    {
                        float u = i / (float)nu, v = j / (float)nv;
                        var p = Vector3.Lerp(Vector3.Lerp(a, b, u), Vector3.Lerp(d, c, u), v);
                        // a little slack: the middle sags back
                        float sag = Mathf.Sin(Mathf.PI * u) * Mathf.Sin(Mathf.PI * v) * 0.08f;
                        p += new Vector3(0f, -sag * 0.5f, sag);
                        verts.Add(p);
                        uvs.Add(new Vector2(u * lu / Cell, v * lv / Cell));
                    }
                for (int j = 0; j < nv; j++)
                    for (int i = 0; i < nu; i++)
                    {
                        int k = start + j * (nu + 1) + i;
                        tris.Add(k);
                        tris.Add(k + nu + 1);
                        tris.Add(k + 1);
                        tris.Add(k + 1);
                        tris.Add(k + nu + 1);
                        tris.Add(k + nu + 2);
                    }
            }

            float w = HalfWidth;
            // the roof: from the crossbar back to the back bar
            Sheet(new Vector3(-w, Height, 0f), new Vector3(w, Height, 0f), new Vector3(w, BackHeight, Depth), new Vector3(-w, BackHeight, Depth), 24, 6);
            // the back
            Sheet(new Vector3(-w, BackHeight, Depth), new Vector3(w, BackHeight, Depth), new Vector3(w, 0.02f, Depth), new Vector3(-w, 0.02f, Depth), 24, 7);
            // the sides
            foreach (float s in new[] { -1f, 1f })
                Sheet(new Vector3(s * w, Height, 0f), new Vector3(s * w, BackHeight, Depth), new Vector3(s * w, 0.02f, Depth), new Vector3(s * w, 0.02f, 0f), 7, 8);
            _mesh = new Mesh { name = "Goal net" };
            _mesh.SetVertices(verts);
            _mesh.SetUVs(0, uvs);
            _mesh.SetTriangles(tris, 0);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
            _mesh.MarkDynamic();
            _base = verts.ToArray();
            _moved = verts.ToArray();
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = gameObject.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.On;
            r.receiveShadows = true;
        }

        /// <summary>The ball hit the net at a world point, going with a world velocity.</summary>
        public void Hit(Vector3 worldPoint, Vector3 worldVelocity)
        {
            _hitAt = transform.InverseTransformPoint(worldPoint);
            var v = transform.InverseTransformDirection(worldVelocity);
            _push = v.normalized * Mathf.Clamp(v.magnitude * 0.05f, 0.3f, 1.0f);
            _hitTime = Time.time;
        }

        void Update()
        {
            float t = Time.time - _hitTime;
            if (t > 2.2f)
            {
                if (t < 2.4f)
                {
                    _mesh.SetVertices(_base);
                    _mesh.RecalculateBounds();
                }
                return;
            }
            // a damped swing of the bulge
            float swing = Mathf.Exp(-t * 2.6f) * Mathf.Cos(t * 9f);
            for (int i = 0; i < _base.Length; i++)
            {
                float d = (_base[i] - _hitAt).sqrMagnitude;
                float k = Mathf.Exp(-d / 0.8f);
                _moved[i] = _base[i] + _push * (k * swing);
            }
            _mesh.SetVertices(_moved);
            _mesh.RecalculateBounds();
        }

        void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }
    }
}
