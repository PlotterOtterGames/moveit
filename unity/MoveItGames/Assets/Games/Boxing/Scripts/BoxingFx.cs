using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

namespace MoveIt.Games.Boxing
{
    /// <summary>
    /// Superboks!'s effects (2026-11-19 overhaul): a comic burst where a punch lands ("POW!", "BAM!", "WHAM!", "BOOM!",
    /// "K.O.!" from tools/boxing's sheet, popping in and turning a little), stars and sparks with it, sweat flying off a
    /// head that's hit, a blue flash on a block, a ring of air for a dodge, dust where a boxer falls, flashes popping in
    /// the crowd, confetti for a win, the stage's snow or sparkles; and what's drawn in code each frame: dizzy stars round
    /// a head, the glow of a power punch's tell, the fireball and its trail, lightning, the hero's power round the gloves.
    /// One particle system each, one dynamic mesh of glows (additive), one of bursts (the sheet, alpha).
    /// </summary>
    public sealed class BoxingFx : MonoBehaviour
    {
        ParticleSystem _stars, _sparks, _confetti, _weather, _rings, _sweat;
        Mesh _glowMesh, _burstMesh;
        readonly List<Vector3> _v = new List<Vector3>(), _bv = new List<Vector3>();
        readonly List<Vector2> _uv = new List<Vector2>(), _buv = new List<Vector2>();
        readonly List<Color> _c = new List<Color>(), _bc = new List<Color>();
        readonly List<int> _t = new List<int>(), _bt = new List<int>();
        Camera _cam;
        Dictionary<string, Rect> _sheet = new Dictionary<string, Rect>();

        sealed class Burst
        {
            public Vector3 At;
            public Rect Uv;
            public float Size, Age, Life, Turn, Aspect;
        }

        readonly List<Burst> _bursts = new List<Burst>();
        readonly List<(Vector3 at, float age, float size)> _flashes = new List<(Vector3, float, float)>();

        public static BoxingFx Create(Material spark, Material dot, Material glow, Material burst, TextAsset sheetJson, Camera cam)
        {
            var go = new GameObject("Boxing fx");
            var fx = go.AddComponent<BoxingFx>();
            fx._cam = cam;
            fx._stars = fx.Particles("Hit stars", spark, 160, 0f, 4f);
            fx._sparks = fx.Particles("Sparks", dot, 200, 3f, 2f);
            fx._confetti = fx.Particles("Confetti", spark, 300, 2.2f, 1.2f);
            fx._weather = fx.Particles("Weather", dot, 400, 0.6f, 0.2f);
            fx._rings = fx.Particles("Rings", dot, 24, 0f, 0f);
            fx._sweat = fx.Particles("Sweat", dot, 80, 7f, 0.5f);
            fx._glowMesh = fx.MeshOf("Glows", glow);
            fx._burstMesh = fx.MeshOf("Bursts", burst);
            fx._sheet = Sheet(sheetJson);
            return fx;
        }

        Mesh MeshOf(string name, Material material)
        {
            var mesh = new Mesh { name = name };
            mesh.MarkDynamic();
            var g = new GameObject(name);
            g.transform.SetParent(transform, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return mesh;
        }

        ParticleSystem Particles(string name, Material material, int max, float gravity, float drag)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = gravity / 9.81f;
            main.startLifetime = 0.5f;
            main.startSpeed = 0f;
            main.startSize = 0.2f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            if (drag > 0f)
            {
                var limit = ps.limitVelocityOverLifetime;
                limit.enabled = true;
                limit.drag = drag;
                limit.limit = 1000f;
            }
            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.55f), new GradientAlphaKey(0f, 1f) });
            colour.color = fade;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.35f));
            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        static void Emit(ParticleSystem ps, Vector3 at, Vector3 velocity, float size, float life, Color colour)
        {
            var p = new ParticleSystem.EmitParams
            {
                position = at, velocity = velocity, startSize = size, startLifetime = life, startColor = colour,
                rotation = Random.Range(0f, 360f),
            };
            ps.Emit(p, 1);
        }

        // ------------------------------------------------------------------ what happens

        static readonly string[] Words = { "pow", "bam", "wham", "boom" };
        int _word;

        /// <summary>A punch landed: stars, sparks, sweat off the head, and (a counter, an uppercut, a big hit) a comic burst
        /// over it. <paramref name="word"/> picks the burst (null = the next in turn, "" = none).</summary>
        public void Hit(Vector3 at, bool big, Color colour, string word = null, float size = 1f)
        {
            int n = big ? 16 : 8;
            for (int i = 0; i < n; i++)
            {
                var d = Random.onUnitSphere;
                Emit(_stars, at, d * Random.Range(1.5f, big ? 4.5f : 3f), Random.Range(0.08f, big ? 0.24f : 0.16f), Random.Range(0.3f, 0.6f),
                     Color.Lerp(colour, Color.white, Random.value * 0.5f));
            }
            for (int i = 0; i < n; i++)
                Emit(_sparks, at, Random.onUnitSphere * Random.Range(2f, 5f), Random.Range(0.04f, 0.09f), Random.Range(0.15f, 0.35f), Color.white);
            Emit(_rings, at, Vector3.zero, big ? 1.1f : 0.7f, 0.22f, new Color(1f, 1f, 1f, 0.8f));
            for (int i = 0; i < (big ? 6 : 3); i++)
            {
                var d = (Random.onUnitSphere + Vector3.up * 0.8f).normalized;
                Emit(_sweat, at + d * 0.15f, d * Random.Range(1.4f, 2.6f), Random.Range(0.04f, 0.08f), Random.Range(0.4f, 0.6f), new Color(0.8f, 0.95f, 1f, 0.9f));
            }
            if (word == "") return;
            if (word == null)
            {
                word = Words[_word % Words.Length];
                _word++;
            }
            Pop(at, word, (big ? 0.62f : 0.48f) * size);
        }

        /// <summary>A comic burst <paramref name="name"/> (tools/boxing's sheet) popping in at <paramref name="at"/>,
        /// <paramref name="size"/> metres high.</summary>
        public void Pop(Vector3 at, string name, float size, float life = 0.62f)
        {
            if (!_sheet.TryGetValue(name, out var uv)) return;
            _bursts.Add(new Burst { At = at, Uv = uv, Size = size, Life = life, Turn = Random.Range(-14f, 14f), Aspect = AspectOf(name) });
        }

        /// <summary>The comic bursts gone at once (the camera cut to another shot).</summary>
        public void ClearBursts() => _bursts.Clear();

        float AspectOf(string name) => name switch
        {
            "pow" or "bam" => 420f / 280f,
            "wham" or "boom" => 480f / 280f,
            "ko" => 560f / 360f,
            "vs" => 300f / 260f,
            _ => 1f,
        };

        /// <summary>A punch into a guard: a small blue-white flash.</summary>
        public void Block(Vector3 at)
        {
            for (int i = 0; i < 8; i++)
                Emit(_sparks, at, Random.onUnitSphere * Random.Range(1f, 2.5f), Random.Range(0.04f, 0.07f), Random.Range(0.15f, 0.3f),
                     new Color(0.7f, 0.9f, 1f));
            Emit(_rings, at, Vector3.zero, 0.55f, 0.18f, new Color(0.6f, 0.85f, 1f, 0.7f));
        }

        /// <summary>A dodge: a whoosh of air where the punch went by.</summary>
        public void Whoosh(Vector3 at)
        {
            for (int i = 0; i < 6; i++)
                Emit(_sparks, at + Random.insideUnitSphere * 0.15f, Random.onUnitSphere * 0.8f, Random.Range(0.08f, 0.14f), 0.3f, new Color(1f, 1f, 1f, 0.5f));
            Emit(_rings, at, Vector3.zero, 0.9f, 0.25f, new Color(0.75f, 1f, 0.8f, 0.6f));
        }

        /// <summary>A boxer hits the canvas: a puff of dust round it.</summary>
        public void Dust(Vector3 at)
        {
            for (int i = 0; i < 18; i++)
            {
                var d = Random.insideUnitCircle.normalized;
                Emit(_sparks, at + new Vector3(d.x, 0.05f, d.y) * 0.3f, new Vector3(d.x, 0.35f, d.y) * Random.Range(0.8f, 1.8f),
                     Random.Range(0.14f, 0.26f), Random.Range(0.5f, 0.9f), new Color(1f, 0.96f, 0.9f, 0.55f));
            }
        }

        /// <summary>A camera's flash in the crowd (a bright dot for a moment).</summary>
        public void Flash(Vector3 at, float size = 0.5f) => _flashes.Add((at, 0f, size));

        public void Confetti(Vector3 at, int count = 140)
        {
            Color[] colours = { new Color(1f, 0.79f, 0.24f), new Color(1f, 0.35f, 0.31f), new Color(0.19f, 0.78f, 0.96f), new Color(0.48f, 0.38f, 1f), Color.white };
            for (int i = 0; i < count; i++)
                Emit(_confetti, at + new Vector3(Random.Range(-2.5f, 2.5f), Random.Range(0f, 1f), Random.Range(-1f, 2f)),
                     new Vector3(Random.Range(-1.2f, 1.2f), Random.Range(2.5f, 5.5f), Random.Range(-1f, 1f)), Random.Range(0.08f, 0.16f),
                     Random.Range(1.6f, 2.6f), colours[Random.Range(0, colours.Length)]);
        }

        /// <summary>The stage's weather this frame: dust motes in the gym's light, snow on the mountain, sparkles among the
        /// stars.</summary>
        public void Weather(int stage, float dt)
        {
            float rate = stage switch { 0 => 9f, 1 => 4f, 2 => 60f, 3 => 14f, _ => 0f };
            int n = Mathf.FloorToInt(rate * dt + Random.value);
            for (int i = 0; i < n; i++)
            {
                switch (stage)
                {
                    case 2:
                        Emit(_weather, new Vector3(Random.Range(-9f, 9f), 7f, Random.Range(-4f, 12f)), new Vector3(Random.Range(-0.4f, 0.4f), -1.2f, 0f),
                             Random.Range(0.04f, 0.09f), 6f, Color.white);
                        break;
                    case 0:
                        Emit(_weather, new Vector3(Random.Range(-3f, 3f), Random.Range(0.8f, 4.5f), Random.Range(-3f, 3f)), new Vector3(0f, 0.12f, 0f),
                             Random.Range(0.02f, 0.045f), 4f, new Color(1f, 0.9f, 0.7f, 0.5f));
                        break;
                    case 1:
                        Emit(_weather, new Vector3(Random.Range(-6f, 6f), Random.Range(0.5f, 3.5f), Random.Range(-2f, 8f)), new Vector3(0.3f, 0.05f, 0f),
                             Random.Range(0.02f, 0.04f), 5f, new Color(1f, 0.85f, 0.6f, 0.45f));
                        break;
                    default:
                        Emit(_weather, new Vector3(Random.Range(-8f, 8f), Random.Range(0f, 5f), Random.Range(-2f, 12f)), Vector3.up * 0.2f,
                             Random.Range(0.04f, 0.08f), 2.5f, new Color(0.6f, 0.95f, 1f, 0.7f));
                        break;
                }
            }
        }

        // ------------------------------------------------------------------ drawn each frame

        public void BeginFrame()
        {
            _v.Clear();
            _uv.Clear();
            _c.Clear();
            _t.Clear();
        }

        /// <summary>The glows (and the crowd's flashes) and the bursts this frame.</summary>
        public void EndFrame(float dt)
        {
            for (int i = _flashes.Count - 1; i >= 0; i--)
            {
                var f = _flashes[i];
                f.age += dt;
                if (f.age > 0.12f) { _flashes.RemoveAt(i); continue; }
                _flashes[i] = f;
                Glow(f.at, f.size * (1f - f.age / 0.12f * 0.4f), new Color(1f, 1f, 1f, 1f - f.age / 0.12f));
            }
            _glowMesh.Clear();
            if (_v.Count > 0)
            {
                _glowMesh.SetVertices(_v);
                _glowMesh.SetUVs(0, _uv);
                _glowMesh.SetColors(_c);
                _glowMesh.SetTriangles(_t, 0, true);
                _glowMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 400f);
            }
            DrawBursts(dt);
        }

        void DrawBursts(float dt)
        {
            _bv.Clear();
            _buv.Clear();
            _bc.Clear();
            _bt.Clear();
            var right = _cam != null ? _cam.transform.right : Vector3.right;
            var up = _cam != null ? _cam.transform.up : Vector3.up;
            var toward = _cam != null ? -_cam.transform.forward : Vector3.back;
            for (int i = _bursts.Count - 1; i >= 0; i--)
            {
                var b = _bursts[i];
                b.Age += Time.unscaledDeltaTime;
                if (b.Age > b.Life) { _bursts.RemoveAt(i); continue; }
                // pop in past full size, settle, then fade and shrink a little
                float u = b.Age / b.Life;
                float scale = b.Age < 0.08f ? Mathf.Lerp(0.2f, 1.18f, b.Age / 0.08f) : b.Age < 0.16f ? Mathf.Lerp(1.18f, 1f, (b.Age - 0.08f) / 0.08f) : 1f - 0.12f * Mathf.Clamp01((u - 0.7f) / 0.3f);
                float alpha = 1f - Mathf.Clamp01((u - 0.72f) / 0.28f);
                float h = b.Size * scale * 0.5f, w = h * b.Aspect;
                var rot = Quaternion.AngleAxis(b.Turn, toward);
                var r = rot * right * w;
                var uvec = rot * up * h;
                var at = b.At + toward * 0.25f + up * (0.05f * u);
                int k = _bv.Count;
                _bv.Add(at - r - uvec);
                _bv.Add(at - r + uvec);
                _bv.Add(at + r + uvec);
                _bv.Add(at + r - uvec);
                _buv.Add(new Vector2(b.Uv.xMin, b.Uv.yMin));
                _buv.Add(new Vector2(b.Uv.xMin, b.Uv.yMax));
                _buv.Add(new Vector2(b.Uv.xMax, b.Uv.yMax));
                _buv.Add(new Vector2(b.Uv.xMax, b.Uv.yMin));
                var c = new Color(1f, 1f, 1f, alpha);
                for (int q = 0; q < 4; q++) _bc.Add(c);
                _bt.Add(k); _bt.Add(k + 1); _bt.Add(k + 2); _bt.Add(k); _bt.Add(k + 2); _bt.Add(k + 3);
            }
            _burstMesh.Clear();
            if (_bv.Count == 0) return;
            _burstMesh.SetVertices(_bv);
            _burstMesh.SetUVs(0, _buv);
            _burstMesh.SetColors(_bc);
            _burstMesh.SetTriangles(_bt, 0, true);
            _burstMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 400f);
        }

        /// <summary>A glow facing the camera this frame.</summary>
        public void Glow(Vector3 at, float size, Color colour)
        {
            var right = _cam != null ? _cam.transform.right : Vector3.right;
            var up = _cam != null ? _cam.transform.up : Vector3.up;
            Quad(at, right * size * 0.5f, up * size * 0.5f, colour);
        }

        void Quad(Vector3 at, Vector3 right, Vector3 up, Color colour)
        {
            int b = _v.Count;
            _v.Add(at - right - up);
            _v.Add(at - right + up);
            _v.Add(at + right + up);
            _v.Add(at + right - up);
            _uv.Add(new Vector2(0f, 0f));
            _uv.Add(new Vector2(0f, 1f));
            _uv.Add(new Vector2(1f, 1f));
            _uv.Add(new Vector2(1f, 0f));
            for (int k = 0; k < 4; k++) _c.Add(colour);
            _t.Add(b); _t.Add(b + 1); _t.Add(b + 2); _t.Add(b); _t.Add(b + 2); _t.Add(b + 3);
        }

        /// <summary>Stars circling a dizzy head.</summary>
        public void DizzyStars(Vector3 head, float time)
        {
            for (int i = 0; i < 5; i++)
            {
                float a = time * 3.2f + i * Mathf.PI * 2f / 5f;
                Glow(head + new Vector3(Mathf.Cos(a) * 0.42f, 0.34f + Mathf.Sin(a * 2f) * 0.04f, Mathf.Sin(a) * 0.3f), 0.22f, new Color(1f, 0.9f, 0.3f, 0.95f));
            }
        }

        /// <summary>A line of glow from a to b (a lightning bolt in zigzags, a trail).</summary>
        public void Bolt(Vector3 a, Vector3 b, float width, Color colour, int zig = 6, float jitter = 0.18f)
        {
            var prev = a;
            var dir = (b - a);
            var side = _cam != null ? Vector3.Cross(dir.normalized, _cam.transform.forward).normalized : Vector3.up;
            for (int i = 1; i <= zig; i++)
            {
                var next = Vector3.Lerp(a, b, i / (float)zig) + (i < zig ? side * Random.Range(-jitter, jitter) : Vector3.zero);
                var seg = next - prev;
                var w = Vector3.Cross(seg.normalized, _cam != null ? _cam.transform.forward : Vector3.forward).normalized * width;
                int bi = _v.Count;
                _v.Add(prev - w); _v.Add(prev + w); _v.Add(next + w); _v.Add(next - w);
                _uv.Add(new Vector2(0f, 0.5f)); _uv.Add(new Vector2(1f, 0.5f)); _uv.Add(new Vector2(1f, 0.5f)); _uv.Add(new Vector2(0f, 0.5f));
                for (int k = 0; k < 4; k++) _c.Add(colour);
                _t.Add(bi); _t.Add(bi + 1); _t.Add(bi + 2); _t.Add(bi); _t.Add(bi + 2); _t.Add(bi + 3);
                prev = next;
            }
        }

        // ------------------------------------------------------------------ the sheet

        [Serializable]
        sealed class SheetJson
        {
            public int[] size;
            public SpriteJson[] sprites;
        }

        [Serializable]
        sealed class SpriteJson
        {
            public string name;
            public int[] rect;
        }

        /// <summary>tools/boxing's sheet by name: uv rectangles (0..1, y up).</summary>
        public static Dictionary<string, Rect> Sheet(TextAsset json)
        {
            var map = new Dictionary<string, Rect>();
            if (json == null) return map;
            var data = JsonUtility.FromJson<SheetJson>(json.text);
            if (data?.sprites == null || data.size == null || data.size.Length < 2) return map;
            float w = data.size[0], h = data.size[1];
            foreach (var s in data.sprites)
            {
                if (s.rect == null || s.rect.Length < 4) continue;
                float x = (s.rect[0] + 0.5f) / w, top = s.rect[1] + 0.5f, width = (s.rect[2] - 1f) / w, height = s.rect[3] - 1f;
                map[s.name] = new Rect(x, 1f - (top + height) / h, width, height / h);
            }
            return map;
        }
    }
}
