using System.Collections.Generic;
using MoveIt.Games.Tennis;
using UnityEngine;
using UnityEngine.Rendering;

namespace MoveIt.Games.Kart
{
    /// <summary>
    /// Gara!'s shadows and effects, Tenis!'s way (<see cref="TennisFx"/>: next to nothing for the TV box's GPU, no real-time
    /// shadows):
    /// <list type="bullet">
    /// <item><b>Sun shadows</b>: every kart's, a soft rounded shape on the road under it, stretched a little away from the
    /// sun, fainter and further off when it flies; the paint bombs' too. One mesh rebuilt every frame (one draw call), the
    /// shadow atlas drawn in code (<see cref="TennisArt.ShadowAtlas"/>).</item>
    /// <item><b>Particles</b> (one system each): drift sparks at the rear wheels (yellow, orange, then pink as they heat),
    /// a burst of stars on a boost, dust or sand thrown up off the road and on landing, exhaust puffs, sparkles at a star
    /// or a gift box, paint splashes, confetti at the finish.</item>
    /// <item><b>Paint bombs</b> in flight (purple balls with a shine) and <b>honey</b> puddles on the road.</item>
    /// </list>
    /// </summary>
    public sealed class KartFx : MonoBehaviour
    {
        Vector2 _slant;
        Mesh _mesh;
        readonly List<Vector3> _v = new List<Vector3>();
        readonly List<Vector2> _uv = new List<Vector2>();
        readonly List<Color> _c = new List<Color>();
        readonly List<int> _t = new List<int>();
        ParticleSystem _sparks, _dust, _puffs, _stars, _splash, _confetti;
        readonly List<Transform> _bombs = new List<Transform>(), _puddles = new List<Transform>();
        Material _bombMaterial, _puddleMaterial;
        Mesh _ball, _disc;

        static readonly Color ShadowColour = new Color(0.12f, 0.12f, 0.2f, 1f);
        public static readonly Color[] SparkColours = { new Color(1f, 0.85f, 0.35f), new Color(1f, 0.55f, 0.2f), new Color(1f, 0.4f, 0.8f) };

        public static KartFx Create(Material shadow, Material spark, Material dot, Material actor, Vector3 sunDirection)
        {
            var go = new GameObject("Kart fx");
            var fx = go.AddComponent<KartFx>();
            fx.Build(shadow, spark, dot, actor, sunDirection);
            return fx;
        }

        void Build(Material shadow, Material spark, Material dot, Material actor, Vector3 sun)
        {
            float down = Mathf.Max(0.3f, -sun.y);
            _slant = new Vector2(sun.x, sun.z) / down * 0.6f;
            _mesh = new Mesh { name = "Kart shadows" };
            _mesh.MarkDynamic();
            var shadows = new GameObject("Shadows");
            shadows.transform.SetParent(transform, false);
            shadows.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = shadows.AddComponent<MeshRenderer>();
            r.sharedMaterial = shadow;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;

            _sparks = Particles("Drift sparks", spark, 160, gravity: 6f, drag: 1.5f);
            _dust = Particles("Dust", dot, 160, gravity: 2f, drag: 2.5f);
            _puffs = Particles("Puffs", dot, 80, gravity: -0.6f, drag: 2f);
            _stars = Particles("Stars", spark, 120, gravity: 0f, drag: 3f);
            _splash = Particles("Splash", dot, 120, gravity: 8f, drag: 0.8f);
            _confetti = Particles("Confetti", spark, 240, gravity: 2.2f, drag: 1.2f);

            _ball = PartMesh.Ball(12);
            _disc = Disc();
            _bombMaterial = new Material(actor) { name = "Paint bomb" };
            _bombMaterial.SetFloat("_UseVertexColor", 0f);
            _bombMaterial.SetColor("_BaseColor", new Color(0.48f, 0.38f, 1f));
            _bombMaterial.SetColor("_EmissionColor", new Color(0.25f, 0.12f, 0.6f));
            _puddleMaterial = new Material(dot) { name = "Honey" };
            _puddleMaterial.SetColor("_BaseColor", new Color(1f, 0.68f, 0.12f, 0.9f));
        }

        static Mesh Disc()
        {
            var m = new Mesh { name = "Puddle" };
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            v.Add(Vector3.zero);
            uv.Add(new Vector2(0.5f, 0.5f));
            const int n = 16;
            for (int i = 0; i < n; i++)
            {
                float a = 2f * Mathf.PI * i / n, r = 1f + 0.18f * Mathf.Sin(a * 3f + 1f);
                v.Add(new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
                uv.Add(new Vector2(0.5f + Mathf.Cos(a) * 0.3f, 0.5f + Mathf.Sin(a) * 0.3f));
            }
            for (int i = 0; i < n; i++)
            {
                t.Add(0);
                t.Add(1 + (i + 1) % n);
                t.Add(1 + i);
            }
            m.SetVertices(v);
            m.SetUVs(0, uv);
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
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
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.5f), new GradientAlphaKey(0f, 1f) });
            colour.color = fade;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.45f));
            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-5f, 5f);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortMode = ParticleSystemSortMode.None;
            ps.Play();
            return ps;
        }

        static void Emit(ParticleSystem ps, Vector3 at, Vector3 velocity, float size, Color colour, float life)
        {
            ps.Emit(new ParticleSystem.EmitParams
            {
                position = at, velocity = velocity, startSize = size, startColor = colour, startLifetime = life, applyShapeToPosition = false,
            }, 1);
        }

        // ------------------------------------------------------------------------------------------------ every frame

        /// <summary>The karts' shadows (and the paint bombs'), the bombs and the honey where the race has them.</summary>
        public void UpdateFrame(KartRace race, IReadOnlyList<KartView> views)
        {
            _v.Clear();
            _uv.Clear();
            _c.Clear();
            _t.Clear();
            var track = race.Track;
            for (int i = 0; i < race.Karts.Length && i < views.Count; i++)
            {
                var k = race.Karts[i];
                var view = views[i];
                if (view == null) continue;
                float lift = k.Height;
                var ground = track.World(k.S, k.D, k.Ground + 0.06f);
                var fwd = view.transform.forward;
                var f2 = new Vector2(fwd.x, fwd.z).normalized;
                float alpha = Mathf.Lerp(0.55f, 0.18f, Mathf.Clamp01(lift / 3f));
                var centre = new Vector2(ground.x, ground.z) + _slant * lift;
                Quad(centre, f2, 1.25f + lift * 0.1f, 0.95f + lift * 0.08f, ground.y, alpha);
            }
            foreach (var p in race.Paints)
            {
                var g = track.World(p.S, p.D, 0.06f);
                Quad(new Vector2(g.x, g.z), Vector2.up, 0.45f, 0.45f, g.y, 0.4f);
            }
            _mesh.Clear();
            _mesh.SetVertices(_v);
            _mesh.SetUVs(0, _uv);
            _mesh.SetColors(_c);
            _mesh.SetTriangles(_t, 0, false);
            _mesh.bounds = new Bounds(Vector3.zero, new Vector3(2000f, 200f, 2000f));

            // paint bombs and honey: a pool each
            Pool(_bombs, race.Paints.Count, "Paint bomb", _ball, _bombMaterial);
            for (int i = 0; i < race.Paints.Count; i++)
            {
                var p = race.Paints[i];
                _bombs[i].position = track.World(p.S, p.D, 0.9f + 0.25f * Mathf.Abs(Mathf.Sin(Time.time * 9f + i)));
                _bombs[i].localScale = Vector3.one * 0.62f;
                _bombs[i].rotation = Quaternion.Euler(Time.time * 400f, Time.time * 150f, 0f);
            }
            Pool(_puddles, race.Puddles.Count, "Honey", _disc, _puddleMaterial);
            for (int i = 0; i < race.Puddles.Count; i++)
            {
                var p = race.Puddles[i];
                _puddles[i].position = track.World(p.S, p.D, 0.05f);
                var tan = track.Tangent(p.S);
                _puddles[i].rotation = Quaternion.LookRotation(new Vector3(tan.x, 0f, tan.y));
                _puddles[i].localScale = new Vector3(1.6f, 1f, 1.9f);
            }
        }

        void Pool(List<Transform> pool, int count, string name, Mesh mesh, Material material)
        {
            while (pool.Count < count)
            {
                var go = new GameObject(name);
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                pool.Add(go.transform);
            }
            for (int i = 0; i < pool.Count; i++)
                if (pool[i].gameObject.activeSelf != i < count) pool[i].gameObject.SetActive(i < count);
        }

        void Quad(Vector2 centre, Vector2 forward, float halfLength, float halfWidth, float y, float alpha)
        {
            var side = new Vector2(forward.y, -forward.x);
            int i = _v.Count;
            Vector2 a = centre - forward * halfLength - side * halfWidth, b = centre - forward * halfLength + side * halfWidth;
            Vector2 c = centre + forward * halfLength + side * halfWidth, d = centre + forward * halfLength - side * halfWidth;
            _v.Add(new Vector3(a.x, y, a.y));
            _v.Add(new Vector3(b.x, y, b.y));
            _v.Add(new Vector3(c.x, y, c.y));
            _v.Add(new Vector3(d.x, y, d.y));
            // the atlas's blob cell (TennisArt.ShadowAtlas: solid, blob, body)
            _uv.Add(new Vector2(1f / 3f, 0f));
            _uv.Add(new Vector2(2f / 3f, 0f));
            _uv.Add(new Vector2(2f / 3f, 1f));
            _uv.Add(new Vector2(1f / 3f, 1f));
            var col = ShadowColour;
            col.a = alpha;
            for (int k = 0; k < 4; k++) _c.Add(col);
            _t.Add(i);
            _t.Add(i + 3);
            _t.Add(i + 2);
            _t.Add(i);
            _t.Add(i + 2);
            _t.Add(i + 1);
        }

        // ------------------------------------------------------------------------------------------------ bursts

        /// <summary>Drift sparks from the rear wheels (tier 0 = cool yellow, a few; 3 = hot pink, many).</summary>
        public void Sparks(Vector3 left, Vector3 right, int tier, Vector3 back)
        {
            int n = 1 + tier;
            var colour = SparkColours[Mathf.Clamp(tier - 1, 0, 2)];
            if (tier == 0) colour = new Color(1f, 0.95f, 0.7f, 0.8f);
            for (int i = 0; i < n; i++)
                foreach (var at in new[] { left, right })
                {
                    var v = back * Random.Range(1.5f, 4f) + new Vector3(Random.Range(-1.2f, 1.2f), Random.Range(1.2f, 3.2f), Random.Range(-1.2f, 1.2f));
                    Emit(_sparks, at, v, Random.Range(0.08f, 0.15f) * (1f + 0.25f * tier), Color.Lerp(colour, Color.white, Random.value * 0.3f), Random.Range(0.2f, 0.4f));
                }
        }

        /// <summary>A boost: a ring of stars round the kart's exhausts.</summary>
        public void Boost(Vector3 at, Color colour, int count = 14)
        {
            for (int i = 0; i < count; i++)
            {
                var v = Random.onUnitSphere * Random.Range(2f, 5f);
                v.y = Mathf.Abs(v.y) * 0.6f;
                Emit(_stars, at, v, Random.Range(0.14f, 0.3f), Color.Lerp(colour, Color.white, Random.value * 0.35f), Random.Range(0.3f, 0.5f));
            }
        }

        /// <summary>Dust (or sand) thrown up behind a wheel off the road, or on landing.</summary>
        public void Dust(Vector3 at, Vector3 velocity, Color colour, int count = 2)
        {
            for (int i = 0; i < count; i++)
            {
                var v = velocity * Random.Range(0.1f, 0.3f) + new Vector3(Random.Range(-1f, 1f), Random.Range(0.8f, 2f), Random.Range(-1f, 1f));
                Emit(_dust, at + Random.insideUnitSphere * 0.2f, v, Random.Range(0.25f, 0.5f), colour, Random.Range(0.4f, 0.7f));
            }
        }

        public void Puff(Vector3 at, Vector3 back, bool boosting)
        {
            var c = boosting ? new Color(1f, 0.8f, 0.55f, 0.6f) : new Color(1f, 1f, 1f, 0.35f);
            Emit(_puffs, at, back * Random.Range(1f, 2.5f) + Vector3.up * 0.4f, Random.Range(0.18f, 0.3f), c, Random.Range(0.35f, 0.6f));
        }

        public void Sparkle(Vector3 at, Color colour, int count = 10)
        {
            for (int i = 0; i < count; i++)
                Emit(_stars, at, Random.onUnitSphere * Random.Range(1.5f, 3.5f), Random.Range(0.1f, 0.22f), Color.Lerp(colour, Color.white, Random.value * 0.4f), Random.Range(0.3f, 0.55f));
        }

        public void Splash(Vector3 at, Color colour, int count = 18)
        {
            for (int i = 0; i < count; i++)
            {
                var v = Random.onUnitSphere * Random.Range(2f, 5f);
                v.y = Mathf.Abs(v.y) + 1f;
                Emit(_splash, at, v, Random.Range(0.2f, 0.45f), Color.Lerp(colour, Color.white, Random.value * 0.15f), Random.Range(0.4f, 0.7f));
            }
        }

        public void Confetti(Vector3 over, int count = 120)
        {
            Color[] colours = { SportHud.Sun, SportHud.Coral, SportHud.Sky, SportHud.Grape, SportHud.Leaf, Color.white };
            for (int i = 0; i < count; i++)
            {
                var p = over + new Vector3(Random.Range(-7f, 7f), Random.Range(3f, 7f), Random.Range(-4f, 4f));
                var v = new Vector3(Random.Range(-1.5f, 1.5f), Random.Range(-0.5f, 2.5f), Random.Range(-1.5f, 1.5f));
                Emit(_confetti, p, v, Random.Range(0.14f, 0.26f), colours[Random.Range(0, colours.Length)], Random.Range(2.2f, 3.4f));
            }
        }

        void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_bombMaterial != null) Destroy(_bombMaterial);
            if (_puddleMaterial != null) Destroy(_puddleMaterial);
            if (_disc != null) Destroy(_disc);
        }
    }
}
