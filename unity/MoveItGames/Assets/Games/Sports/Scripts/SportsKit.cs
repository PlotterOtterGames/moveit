using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MoveIt.Games.Sports
{
    /// <summary>The balls, drawn in code: a classic football (white with black pentagons) and a beach volleyball (yellow,
    /// blue and white panels), vertex coloured (linear) for Tundu/Toon with gloss.</summary>
    public static class SportsBalls
    {
        static Mesh _football, _volleyball;

        public static Mesh Football => _football != null ? _football : _football = Make("Football", 0.11f, (p, ico) =>
        {
            // pentagons round the twelve corners of an icosahedron, hexagon seams between
            float best = 0f;
            foreach (var c in ico) best = Mathf.Max(best, Vector3.Dot(p, c));
            return best > 0.93f ? new Color(0.06f, 0.06f, 0.09f, 0.5f) : best > 0.915f ? new Color(0.55f, 0.56f, 0.6f, 0.5f) : new Color(0.92f, 0.92f, 0.94f, 0.7f);
        });

        public static Mesh Volleyball => _volleyball != null ? _volleyball : _volleyball = Make("Volleyball", 0.105f, (p, ico) =>
        {
            // three bands of panels round the three axes, in the brand's sun, sky and white
            float ax = Mathf.Abs(p.x), ay = Mathf.Abs(p.y), az = Mathf.Abs(p.z);
            Color sun = new Color(1f, 0.58f, 0.04f, 0.6f), sky = new Color(0.03f, 0.53f, 0.9f, 0.6f), white = new Color(0.92f, 0.92f, 0.94f, 0.6f);
            Color c = ax > ay && ax > az ? (p.y > 0 == p.z > 0 ? sun : sky) : ay > az ? white : (p.x > 0 == p.y > 0 ? sky : sun);
            // the seams
            float m = Mathf.Max(ax, Mathf.Max(ay, az));
            float second = ax + ay + az - m - Mathf.Min(ax, Mathf.Min(ay, az));
            if (m - second < 0.035f) c = new Color(0.25f, 0.25f, 0.3f, 0.4f);
            return c;
        });

        static Mesh Make(string name, float radius, System.Func<Vector3, Vector3[], Color> paint)
        {
            // an icosphere, subdivided three times
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var ico = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            for (int i = 0; i < ico.Count; i++) ico[i] = ico[i].normalized;
            var faces = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            var corners = ico.ToArray();
            var verts = new List<Vector3>(ico);
            var cache = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) + b : ((long)b << 32) + a;
                if (cache.TryGetValue(key, out int m)) return m;
                verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
                cache[key] = verts.Count - 1;
                return verts.Count - 1;
            }
            for (int s = 0; s < 3; s++)
            {
                var next = new List<int>();
                for (int i = 0; i < faces.Count; i += 3)
                {
                    int a = faces[i], b = faces[i + 1], c = faces[i + 2];
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                faces = next;
            }
            var colours = new Color[verts.Count];
            var positions = new Vector3[verts.Count];
            for (int i = 0; i < verts.Count; i++)
            {
                var c = paint(verts[i], corners);
                colours[i] = new Color(Mathf.GammaToLinearSpace(c.r), Mathf.GammaToLinearSpace(c.g), Mathf.GammaToLinearSpace(c.b), c.a);
                positions[i] = verts[i] * radius;
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(positions);
            mesh.SetColors(colours);
            // the icosahedron's faces wind clockwise seen from outside in Unity's left-handed space: as they are
            mesh.SetTriangles(faces, 0);
            mesh.RecalculateNormals();
            // make sure they face out
            var n = mesh.normals;
            if (Vector3.Dot(n[0], positions[0]) < 0f)
            {
                faces.Reverse();
                mesh.SetTriangles(faces, 0);
                mesh.RecalculateNormals();
            }
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A ball object: the mesh, real-time shadow, a trail behind it when it flies.</summary>
        public static Transform Create(string name, Mesh mesh, Material material, Material trail, Transform parent, Color trailColour)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.On;
            if (trail != null)
            {
                var tr = go.AddComponent<TrailRenderer>();
                tr.sharedMaterial = trail;
                tr.time = 0.22f;
                tr.minVertexDistance = 0.1f;
                tr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.18f), new Keyframe(1f, 0f));
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(trailColour, 1f) },
                          new[] { new GradientAlphaKey(0.75f, 0f), new GradientAlphaKey(0f, 1f) });
                tr.colorGradient = g;
                tr.shadowCastingMode = ShadowCastingMode.Off;
                tr.emitting = false;
            }
            return go.transform;
        }
    }

    /// <summary>A camera that eases toward where a shot wants it (position, the point it looks at, the field of view),
    /// or cuts there at once; the effects' shake on top.</summary>
    public sealed class ShotCamera
    {
        readonly Camera _cam;
        Vector3 _pos, _look;
        float _fov;
        bool _placed;

        public ShotCamera(Camera cam)
        {
            _cam = cam;
            _fov = cam != null ? cam.fieldOfView : 50f;
        }

        public Vector3 Position => _pos;

        public void Cut(Vector3 position, Vector3 look, float fov)
        {
            _pos = position;
            _look = look;
            _fov = fov;
            _placed = true;
            Apply(Vector3.zero);
        }

        /// <summary>Eases toward the wanted shot (rate: how quick, per second; unscaled time so slow motion keeps the
        /// camera moving).</summary>
        public void Follow(Vector3 position, Vector3 look, float fov, float rate, Vector3 shake)
        {
            if (!_placed)
            {
                Cut(position, look, fov);
                return;
            }
            float k = 1f - Mathf.Exp(-rate * Time.unscaledDeltaTime);
            _pos = Vector3.Lerp(_pos, position, k);
            _look = Vector3.Lerp(_look, look, Mathf.Min(1f, k * 1.4f));
            _fov = Mathf.Lerp(_fov, fov, k);
            Apply(shake);
        }

        void Apply(Vector3 shake)
        {
            if (_cam == null) return;
            _cam.transform.position = _pos + shake;
            _cam.transform.rotation = Quaternion.LookRotation(_look - _pos);
            _cam.fieldOfView = _fov;
        }
    }

    /// <summary>Daylight for a venue: the sky's gradient and clouds, the sun, three-colour ambient light, the haze.</summary>
    public struct Daylight
    {
        public Color SkyTop, SkyHorizon, Ground, Fog, AmbientSky, AmbientEquator, AmbientGround, Sun;
        public Vector3 SunAngles;
        public float SunIntensity, FogStart, FogEnd, Clouds;

        public static Daylight Stadium => new Daylight
        {
            SkyTop = Hex(0x2C7FE0), SkyHorizon = Hex(0xBFE3F7), Ground = Hex(0x8FB6C8), Fog = Hex(0xC9E6F5),
            AmbientSky = Hex(0x9CC4F0), AmbientEquator = Hex(0xB9C4CF), AmbientGround = Hex(0x6E8A5C),
            Sun = Hex(0xFFF1D9), SunAngles = new Vector3(42f, 28f, 0f), SunIntensity = 1.35f, FogStart = 90f, FogEnd = 320f, Clouds = 0.55f,
        };

        public static Daylight Afternoon => new Daylight
        {
            SkyTop = Hex(0x3A86E3), SkyHorizon = Hex(0xD6ECF8), Ground = Hex(0xA3C0CC), Fog = Hex(0xD3EAF6),
            AmbientSky = Hex(0xA3C6EE), AmbientEquator = Hex(0xC8C8CC), AmbientGround = Hex(0x7E8A64),
            Sun = Hex(0xFFE8C8), SunAngles = new Vector3(36f, -62f, 0f), SunIntensity = 1.35f, FogStart = 90f, FogEnd = 340f, Clouds = 0.5f,
        };

        public static Daylight Beach => new Daylight
        {
            SkyTop = Hex(0x1F7FE6), SkyHorizon = Hex(0xCBEFFA), Ground = Hex(0x7FC9E0), Fog = Hex(0xD7F1FA),
            AmbientSky = Hex(0x9ED2F2), AmbientEquator = Hex(0xE0D3B8), AmbientGround = Hex(0xD8BE8C),
            Sun = Hex(0xFFEBC2), SunAngles = new Vector3(40f, 205f, 0f), SunIntensity = 1.4f, FogStart = 150f, FogEnd = 1300f, Clouds = 0.42f,
        };

        static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

        public void Apply(ActivityKit kit)
        {
            if (kit.Sky != null)
            {
                kit.Sky.SetColor("_TopColor", SkyTop);
                kit.Sky.SetColor("_HorizonColor", SkyHorizon);
                kit.Sky.SetColor("_BottomColor", Ground);
                kit.Sky.SetFloat("_Clouds", Clouds);
                if (Clouds > 0f) SkyNoise.Ensure();
                kit.Sky.SetFloat("_Exponent", 0.6f);
                var sunDir = Quaternion.Euler(SunAngles) * Vector3.back;
                kit.Sky.SetVector("_SunDirection", new Vector4(sunDir.x, sunDir.y, sunDir.z, 0f));
            }
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Fog;
            RenderSettings.fogStartDistance = FogStart;
            RenderSettings.fogEndDistance = FogEnd;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = AmbientSky;
            RenderSettings.ambientEquatorColor = AmbientEquator;
            RenderSettings.ambientGroundColor = AmbientGround;
            DynamicGI.UpdateEnvironment();   // the ambient probe the shaders sample follows the new colours
            if (kit.Cam != null)
            {
                kit.Cam.backgroundColor = SkyHorizon;
                kit.Cam.farClipPlane = Mathf.Max(600f, FogEnd + 200f);
            }
            if (kit.Sun != null)
            {
                kit.Sun.transform.rotation = Quaternion.Euler(SunAngles);
                kit.Sun.color = Sun;
                kit.Sun.intensity = SunIntensity;
                kit.Sun.shadows = LightShadows.Soft;
                kit.Sun.shadowStrength = 0.85f;
            }
        }
    }
}
