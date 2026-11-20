using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MoveIt.Games.Fairy
{
    /// <summary>
    /// Soft camera-facing glows and streaks, added on, one mesh a frame (one draw call): the spells drawn in the air, the
    /// bolts flying to the creatures, sparkles on the way, the casting hand's light, gloom balls' auras. The box has no
    /// bloom: this is it.
    /// </summary>
    public sealed class FairyGlows
    {
        readonly Material _material;
        readonly Mesh _mesh;
        readonly List<Vector3> _v = new List<Vector3>(2048);
        readonly List<Color32> _c = new List<Color32>(2048);
        readonly List<Vector2> _u = new List<Vector2>(2048);
        readonly List<int> _t = new List<int>(3072);
        Vector3 _camPos, _right = Vector3.right, _up = Vector3.up;

        public FairyGlows(Material material)
        {
            _material = material;
            _mesh = new Mesh { name = "Glows", indexFormat = IndexFormat.UInt32 };
            _mesh.MarkDynamic();
        }

        public void Destroy()
        {
            if (_mesh != null) Object.Destroy(_mesh);
        }

        public void Begin(Camera cam)
        {
            _v.Clear();
            _c.Clear();
            _u.Clear();
            _t.Clear();
            if (cam == null) return;
            _camPos = cam.transform.position;
            _right = cam.transform.right;
            _up = cam.transform.up;
        }

        /// <summary>A soft round glow <paramref name="size"/> m across.</summary>
        public void Glow(Vector3 at, float size, Color colour, float alpha)
        {
            if (alpha <= 0.002f || _v.Count > 60000) return;
            var r = _right * size * 0.5f;
            var u = _up * size * 0.5f;
            Quad(at - r - u, at - r + u, at + r + u, at + r - u, colour, alpha);
        }

        /// <summary>A soft streak from <paramref name="a"/> to <paramref name="b"/>, <paramref name="width"/> m wide.</summary>
        public void Streak(Vector3 a, Vector3 b, float width, Color colour, float alpha)
        {
            if (alpha <= 0.002f || _v.Count > 60000) return;
            var dir = b - a;
            if (dir.sqrMagnitude < 1e-8f)
            {
                Glow(a, width, colour, alpha);
                return;
            }
            dir.Normalize();
            var side = Vector3.Cross(dir, (_camPos - a).normalized);
            if (side.sqrMagnitude < 1e-6f) return;
            side = side.normalized * width * 0.5f;
            var a0 = a - dir * width * 0.5f;
            var b0 = b + dir * width * 0.5f;
            Quad(a0 - side, b0 - side, b0 + side, a0 + side, colour, alpha);
        }

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color colour, float alpha)
        {
            int i = _v.Count;
            _v.Add(a);
            _v.Add(b);
            _v.Add(c);
            _v.Add(d);
            _u.Add(new Vector2(0f, 0f));
            _u.Add(new Vector2(0f, 1f));
            _u.Add(new Vector2(1f, 1f));
            _u.Add(new Vector2(1f, 0f));
            var c32 = (Color32)new Color(colour.r, colour.g, colour.b, Mathf.Clamp01(alpha));
            for (int k = 0; k < 4; k++) _c.Add(c32);
            _t.Add(i);
            _t.Add(i + 1);
            _t.Add(i + 2);
            _t.Add(i);
            _t.Add(i + 2);
            _t.Add(i + 3);
        }

        public void Draw()
        {
            if (_material == null) return;
            _mesh.Clear();
            if (_v.Count == 0 || PerfSwitches.Get("glows") == "0") return;
            _mesh.SetVertices(_v);
            _mesh.SetColors(_c);
            _mesh.SetUVs(0, _u);
            _mesh.SetTriangles(_t, 0, false);
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            var rp = new RenderParams(_material) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, worldBounds = new Bounds(Vector3.zero, Vector3.one * 1e6f) };
            Graphics.RenderMesh(rp, _mesh, 0, Matrix4x4.identity);
        }
    }
}
