using System.Collections.Generic;
using MoveIt.Games.Tennis;
using UnityEngine;
using UnityEngine.Rendering;

namespace MoveIt.Games.Volley
{
    /// <summary>
    /// Volejboll!'s shadows and effects, Tenis!'s (<see cref="TennisFx"/>: next to nothing for the TV box's GPU, no
    /// real-time shadows) on sand:
    /// <list type="bullet">
    /// <item><b>Sun shadows</b>: every toy's, stretched away from the sun as tall as it stands (a jump lifts it off its
    /// shadow), a darker one where its feet touch the sand, and <b>the ball's</b>, bigger and fainter the higher it flies:
    /// on the sand it shows where the ball will come down. One mesh rebuilt every frame (one draw call), the shadow atlas
    /// drawn in code (<see cref="TennisArt.ShadowAtlas"/>).</item>
    /// <item><b>Particles</b> (one system each): stars at a touch (more, bigger and orange for a spike), a splash of sand
    /// and a scuff ring where the ball comes down, sand kicked up by a toy setting off, landing from a jump or diving,
    /// confetti for a won match.</item>
    /// <item><b>The ball's glow</b>, a soft additive halo, warm after a spike.</item>
    /// </list>
    /// </summary>
    public sealed class VolleyFx : MonoBehaviour
    {
        Vector3 _sun = new Vector3(-0.321f, -0.766f, 0.557f);
        Vector2 _slant;
        Mesh _mesh;
        readonly List<Vector3> _v = new List<Vector3>();
        readonly List<Vector2> _uv = new List<Vector2>();
        readonly List<Color> _c = new List<Color>();
        readonly List<int> _t = new List<int>();
        ParticleSystem _stars, _sand, _rings, _confetti;
        Transform _glow;
        MaterialPropertyBlock _glowBlock;

        /// <summary>A shadow on warm sand: a deep warm brown.</summary>
        static readonly Color ShadowColour = new Color(0.28f, 0.17f, 0.07f, 1f);

        public static VolleyFx Create(Material shadow, Material spark, Material dot, Material additiveDot, Vector3 sunDirection)
        {
            var go = new GameObject("Volley fx");
            var fx = go.AddComponent<VolleyFx>();
            fx.Build(shadow, spark, dot, additiveDot, sunDirection);
            return fx;
        }

        void Build(Material shadow, Material spark, Material dot, Material additiveDot, Vector3 sunDirection)
        {
            _sun = sunDirection;
            float down = Mathf.Max(0.2f, -_sun.y);
            _slant = new Vector2(_sun.x, _sun.z) / down;

            _mesh = new Mesh { name = "Volley shadows" };
            _mesh.MarkDynamic();
            var shadows = new GameObject("Shadows");
            shadows.transform.SetParent(transform, false);
            shadows.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = shadows.AddComponent<MeshRenderer>();
            r.sharedMaterial = shadow;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;

            _stars = Particles("Touch stars", spark, 64, gravity: 0f, drag: 3.5f);
            _sand = Particles("Sand", dot, 140, gravity: 7f, drag: 0.5f);
            _rings = Particles("Scuff rings", dot, 12, gravity: 0f, drag: 0f, flat: true);
            _confetti = Particles("Confetti", spark, 180, gravity: 2.2f, drag: 1.2f);

            _glowBlock = new MaterialPropertyBlock();
            var glow = new GameObject("Ball glow");
            glow.transform.SetParent(transform, false);
            glow.AddComponent<MeshFilter>().sharedMesh = PartMesh.Primitive(PrimitiveType.Quad);
            var gr = glow.AddComponent<MeshRenderer>();
            gr.sharedMaterial = additiveDot;
            gr.shadowCastingMode = ShadowCastingMode.Off;
            gr.receiveShadows = false;
            _glow = glow.transform;
        }

        ParticleSystem Particles(string name, Material material, int max, float gravity, float drag, bool flat = false)
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
            var limit = ps.limitVelocityOverLifetime;
            if (drag > 0f)
            {
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
            size.size = new ParticleSystem.MinMaxCurve(1f, flat ? AnimationCurve.Linear(0f, 0.5f, 1f, 1.7f) : AnimationCurve.Linear(0f, 1f, 1f, 0.4f));
            var rotation = ps.rotationOverLifetime;
            rotation.enabled = !flat;
            rotation.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.renderMode = flat ? ParticleSystemRenderMode.HorizontalBillboard : ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortMode = ParticleSystemSortMode.None;
            ps.Play();
            return ps;
        }

        // ------------------------------------------------------------------------------------------------ shadows

        /// <summary>This frame's moving shadows: each toy's (feet, body) and the ball's.</summary>
        public void UpdateShadows(IReadOnlyList<VolleyPlayerView> views, Vector3? ball, float ballSize)
        {
            _v.Clear();
            _uv.Clear();
            _c.Clear();
            _t.Clear();
            foreach (var view in views)
            {
                if (view == null || !view.isActiveAndEnabled) continue;
                var feet = view.FeetWorld;
                var head = view.HeadWorld;
                float lift = Mathf.Clamp01(Mathf.Max(0f, feet.y) * 1.6f);
                var ground = new Vector2(feet.x, feet.z);
                var top = Fall(head);
                var along = top - Fall(new Vector3(feet.x, Mathf.Max(0f, feet.y), feet.z));
                float length = along.magnitude;
                var dir = length > 1e-3f ? along / length : _slant.normalized;
                var start = Fall(new Vector3(feet.x, Mathf.Max(0f, feet.y), feet.z));
                const float width = 0.62f;
                Quad(start - dir * (width * 0.35f), top + dir * (width * 0.45f), width, Atlas.Body, 0.5f * (1f - 0.45f * lift));
                Quad(ground - dir * 0.2f, ground + dir * 0.24f, 0.55f, Atlas.Blob, 0.42f * (1f - lift));
            }
            if (ball.HasValue)
            {
                var b = ball.Value;
                float h = Mathf.Max(0f, b.y);
                // straight below the ball (where it will come down), and the sun's slanted one, softer
                var below = new Vector2(b.x, b.z);
                float size = ballSize * 1.3f + h * 0.06f;
                Quad(below - Vector2.up * size * 0.5f, below + Vector2.up * size * 0.5f, size, Atlas.Blob, Mathf.Lerp(0.5f, 0.22f, h / 5f));
                var at = Fall(b);
                var d = _slant.normalized;
                Quad(at - d * size * 0.6f, at + d * size * 0.6f, size, Atlas.Blob, Mathf.Lerp(0.3f, 0.08f, h / 5f));
            }
            _mesh.Clear();
            _mesh.SetVertices(_v);
            _mesh.SetUVs(0, _uv);
            _mesh.SetColors(_c);
            _mesh.SetTriangles(_t, 0, false);
            _mesh.bounds = new Bounds(Vector3.zero, new Vector3(80f, 2f, 80f));
        }

        Vector2 Fall(Vector3 p)
        {
            float h = Mathf.Max(0f, p.y);
            return new Vector2(p.x + _slant.x * h, p.z + _slant.y * h);
        }

        enum Atlas { Solid, Blob, Body }

        static Vector2 Cell(Atlas cell) => cell switch
        {
            Atlas.Solid => new Vector2(0f, 1f / 3f),
            Atlas.Blob => new Vector2(1f / 3f, 2f / 3f),
            _ => new Vector2(2f / 3f, 1f),
        };

        void Quad(Vector2 a, Vector2 b, float width, Atlas cell, float alpha)
        {
            if (alpha <= 0.01f) return;
            var d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            var side = new Vector2(-d.y, d.x) / len * (width * 0.5f);
            int i = _v.Count;
            const float y = 0.025f;
            _v.Add(new Vector3(a.x - side.x, y, a.y - side.y));
            _v.Add(new Vector3(a.x + side.x, y, a.y + side.y));
            _v.Add(new Vector3(b.x + side.x, y, b.y + side.y));
            _v.Add(new Vector3(b.x - side.x, y, b.y - side.y));
            var u = Cell(cell);
            _uv.Add(new Vector2(u.x, 0f));
            _uv.Add(new Vector2(u.y, 0f));
            _uv.Add(new Vector2(u.y, 1f));
            _uv.Add(new Vector2(u.x, 1f));
            var c = ShadowColour;
            c.a = alpha;
            for (int k = 0; k < 4; k++) _c.Add(c);
            _t.Add(i);
            _t.Add(i + 2);
            _t.Add(i + 1);
            _t.Add(i);
            _t.Add(i + 3);
            _t.Add(i + 2);
        }

        // ------------------------------------------------------------------------------------------------ the glow

        public void UpdateGlow(Vector3? ball, Camera cam, float ballSize, Color colour, float strength)
        {
            bool show = ball.HasValue && cam != null && strength > 0.01f;
            if (_glow.gameObject.activeSelf != show) _glow.gameObject.SetActive(show);
            if (!show) return;
            _glow.SetPositionAndRotation(ball.Value, cam.transform.rotation);
            _glow.localScale = Vector3.one * (ballSize * (2.2f + 1.2f * strength));
            colour.a = 0.28f + 0.32f * strength;
            _glowBlock.SetColor("_BaseColor", colour);
            _glow.GetComponent<Renderer>().SetPropertyBlock(_glowBlock);
        }

        // ------------------------------------------------------------------------------------------------ bursts

        /// <summary>A touch: stars from the ball (a spike: more, bigger, warmer).</summary>
        public void Touch(Vector3 at, bool power, Color colour)
        {
            int n = power ? 16 : 7;
            for (int i = 0; i < n; i++)
            {
                var v = Random.onUnitSphere * Random.Range(2.5f, power ? 6.5f : 4f);
                Emit(_stars, at, v, Random.Range(0.12f, power ? 0.32f : 0.2f), Color.Lerp(colour, Color.white, Random.value * 0.4f), Random.Range(0.25f, 0.45f));
            }
        }

        /// <summary>The ball came down: a scuff ring and a splash of sand (red-tinged ring when it's out), as big as it was
        /// fast.</summary>
        public void Land(Vector3 at, bool inside, float speed)
        {
            var ground = new Vector3(at.x, 0.03f, at.z);
            Emit(_rings, ground, Vector3.zero, 0.7f + speed * 0.02f, inside ? new Color(1f, 0.95f, 0.85f, 0.6f) : new Color(1f, 0.45f, 0.38f, 0.85f), 0.5f);
            int n = Mathf.Clamp(Mathf.RoundToInt(6f + speed * 0.6f), 6, 16);
            for (int i = 0; i < n; i++)
            {
                var v = new Vector3(Random.Range(-1.6f, 1.6f), Random.Range(1.8f, 3.6f), Random.Range(-1.6f, 1.6f));
                Emit(_sand, ground, v, Random.Range(0.06f, 0.13f), Sand(), Random.Range(0.4f, 0.65f));
            }
        }

        /// <summary>Sand kicked up at a toy's feet: setting off, landing from a jump, a dive (<paramref name="amount"/>).</summary>
        public void Kick(Vector3 feet, Vector3 velocity, float amount = 1f)
        {
            var back = velocity.sqrMagnitude > 1e-4f ? -velocity.normalized : Vector3.zero;
            int n = Mathf.RoundToInt(3f + 5f * amount);
            for (int i = 0; i < n; i++)
            {
                var v = back * Random.Range(0.5f, 1.5f) + new Vector3(Random.Range(-0.7f, 0.7f), Random.Range(0.9f, 1.9f) * Mathf.Max(0.6f, amount), Random.Range(-0.7f, 0.7f));
                Emit(_sand, new Vector3(feet.x, 0.05f, feet.z), v, Random.Range(0.05f, 0.1f), Sand(), Random.Range(0.3f, 0.5f));
            }
        }

        /// <summary>A won match (or the tournament): confetti in the brand's colours over <paramref name="over"/>.</summary>
        public void Confetti(Vector3 over, int count = 90)
        {
            Color[] colours = { SportHud.Sun, SportHud.Coral, SportHud.Sky, SportHud.Grape, SportHud.Leaf, Color.white };
            for (int i = 0; i < count; i++)
            {
                var p = over + new Vector3(Random.Range(-5f, 5f), Random.Range(3f, 6f), Random.Range(-3f, 3f));
                var v = new Vector3(Random.Range(-1.5f, 1.5f), Random.Range(-0.5f, 2.5f), Random.Range(-1.5f, 1.5f));
                Emit(_confetti, p, v, Random.Range(0.12f, 0.22f), colours[Random.Range(0, colours.Length)], Random.Range(2.2f, 3.4f));
            }
        }

        static Color Sand() => Color.Lerp(new Color(0.86f, 0.74f, 0.52f), new Color(0.98f, 0.9f, 0.72f), Random.value);

        static void Emit(ParticleSystem ps, Vector3 at, Vector3 velocity, float size, Color colour, float life)
        {
            var e = new ParticleSystem.EmitParams
            {
                position = at, velocity = velocity, startSize = size, startColor = colour, startLifetime = life, applyShapeToPosition = false,
            };
            ps.Emit(e, 1);
        }

        void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }
    }
}
