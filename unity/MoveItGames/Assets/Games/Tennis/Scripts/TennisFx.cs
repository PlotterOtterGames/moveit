using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MoveIt.Games.Tennis
{
    /// <summary>
    /// Tenis!'s shadows and effects, built to cost the TV box's GPU next to nothing (2026-11-07, user: "more styling,
    /// shadows, effects, shaders, but without breaking the performance"; real-time shadows were measured too costly there):
    /// <list type="bullet">
    /// <item><b>Sun shadows</b>: every toy casts a soft shadow stretched away from the sun (its length from how tall it
    /// stands, so a hop or a lunge moves it), a darker one where its feet touch the grass, and its big racket's head; the
    /// ball one that grows and fades with its height. All of them are quads of one mesh rebuilt every frame: one draw
    /// call, a few dozen vertices (<see cref="TennisArt.ShadowAtlas"/>).</item>
    /// <item><b>Particles</b> (one particle system each, so one draw call however many): a burst of stars at a hit (more
    /// and orange for a power shot), a puff of grass and a scuff ring where the ball bounces, a little grass kicked up by
    /// a toy setting off or stopping, confetti over the court for a won match.</item>
    /// <item><b>The ball's glow</b>: a soft additive halo, so the small ball reads on a TV across the room.</item>
    /// </list>
    /// </summary>
    public sealed class TennisFx : MonoBehaviour
    {
        /// <summary>The way the sunlight travels (the scene's directional light), flattened for shadows on the ground.</summary>
        Vector3 _sun = new Vector3(-0.321f, -0.766f, 0.557f);
        /// <summary>Metres a shadow moves along the ground per metre of height (the sun's slant).</summary>
        Vector2 _slant;

        Mesh _mesh;
        readonly List<Vector3> _v = new List<Vector3>();
        readonly List<Vector2> _uv = new List<Vector2>();
        readonly List<Color> _c = new List<Color>();
        readonly List<int> _t = new List<int>();
        ParticleSystem _stars, _grass, _rings, _confetti;
        Transform _glow;
        Material _glowMaterial;
        MaterialPropertyBlock _glowBlock;

        /// <summary>The shadows' colour: a deep, slightly blue green (a shadow on grass is coloured, not black).</summary>
        static readonly Color ShadowColour = new Color(0.04f, 0.1f, 0.06f, 1f);

        public static TennisFx Create(Material shadow, Material spark, Material dot, Material additiveDot, Light sun)
        {
            var go = new GameObject("Tennis fx");
            var fx = go.AddComponent<TennisFx>();
            fx.Build(shadow, spark, dot, additiveDot, sun);
            return fx;
        }

        void Build(Material shadow, Material spark, Material dot, Material additiveDot, Light sun)
        {
            if (sun != null) _sun = sun.transform.forward;
            float down = Mathf.Max(0.2f, -_sun.y);
            _slant = new Vector2(_sun.x, _sun.z) / down;

            _mesh = new Mesh { name = "Tennis shadows" };
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

            _stars = Particles("Hit stars", spark, 64, gravity: 0f, drag: 3.5f);
            _grass = Particles("Grass bits", dot, 96, gravity: 6f, drag: 0.6f);
            _rings = Particles("Bounce rings", dot, 12, gravity: 0f, drag: 0f, flat: true);
            _confetti = Particles("Confetti", spark, 160, gravity: 2.2f, drag: 1.2f);

            _glowMaterial = additiveDot;
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

        /// <summary>A particle system that only emits when told (<see cref="ParticleSystem.Emit(ParticleSystem.EmitParams, int)"/>),
        /// its particles in world space; <paramref name="flat"/> ones lie on the ground.</summary>
        ParticleSystem Particles(string name, Material material, int max, float gravity, float drag, bool flat = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;   // (emits only when told; looping keeps it simulating what was emitted)
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
            size.size = new ParticleSystem.MinMaxCurve(1f, flat ? AnimationCurve.Linear(0f, 0.5f, 1f, 1.6f) : AnimationCurve.Linear(0f, 1f, 1f, 0.35f));
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

        /// <summary>
        /// Rebuilds the moving shadows for this frame: each toy's (feet, body stretched away from the sun as tall as it
        /// stands, the racket's head) and the ball's (bigger and fainter the higher it is).
        /// </summary>
        public void UpdateShadows(IReadOnlyList<TennisPlayerView> views, Vector3? ball, float ballSize)
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
                float lift = Mathf.Clamp01(Mathf.Max(0f, feet.y) * 2.5f);   // a hop lifts the feet off their shadow
                var ground = new Vector2(feet.x, feet.z);
                // the body: from the feet to where the top of the head falls, a toy's width across
                var top = Fall(head);
                var along = top - ground;
                float length = along.magnitude;
                var dir = length > 1e-3f ? along / length : new Vector2(_slant.x, _slant.y).normalized;
                float width = 0.5f * TennisPlayerView.Scale / 0.78f;
                Quad(ground - dir * (width * 0.35f), top + dir * (width * 0.45f), width, Atlas.Body, 0.58f * (1f - 0.4f * lift));
                // where the feet touch the grass: small and darker, fading as the toy hops
                Quad(ground - dir * 0.18f, ground + dir * 0.22f, 0.46f, Atlas.Blob, 0.45f * (1f - lift));
                // the racket's head (big and readable, like the toy's own: as big as the drawn head)
                var racket = view.RacketWorld;
                if (racket.y > -1f)
                {
                    var r = Fall(racket);
                    var racketHead = TennisPlayerView.RacketHead;
                    Quad(r - dir * (0.3f * racketHead.y), r + dir * (0.3f * racketHead.y), 0.7f * racketHead.x, Atlas.Blob, 0.36f);
                }
            }
            if (ball.HasValue)
            {
                var b = ball.Value;
                float h = Mathf.Max(0f, b.y);
                var at = Fall(b);
                float size = ballSize * 1.25f + h * 0.08f;
                var d = new Vector2(_slant.x, _slant.y).normalized;
                Quad(at - d * size * 0.55f, at + d * size * 0.55f, size, Atlas.Blob, Mathf.Lerp(0.55f, 0.18f, h / 4f));
            }
            _mesh.Clear();
            _mesh.SetVertices(_v);
            _mesh.SetUVs(0, _uv);
            _mesh.SetColors(_c);
            _mesh.SetTriangles(_t, 0, false);
            _mesh.bounds = new Bounds(Vector3.zero, new Vector3(80f, 2f, 80f));
        }

        /// <summary>Where a point's shadow falls on the ground (x, z).</summary>
        Vector2 Fall(Vector3 p)
        {
            float h = Mathf.Max(0f, p.y);
            return new Vector2(p.x + _slant.x * h, p.z + _slant.y * h);
        }

        /// <summary>The shadow atlas's cells (<see cref="TennisArt.ShadowAtlas"/>): u from, u to.</summary>
        enum Atlas { Solid, Blob, Body }

        static Vector2 Cell(Atlas cell) => cell switch
        {
            Atlas.Solid => new Vector2(0f, 1f / 3f),
            Atlas.Blob => new Vector2(1f / 3f, 2f / 3f),
            _ => new Vector2(2f / 3f, 1f),
        };

        /// <summary>A quad on the ground from <paramref name="a"/> to <paramref name="b"/> (its length, the texture's v),
        /// <paramref name="width"/> across, of strength <paramref name="alpha"/>.</summary>
        void Quad(Vector2 a, Vector2 b, float width, Atlas cell, float alpha)
        {
            if (alpha <= 0.01f) return;
            var d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            var side = new Vector2(-d.y, d.x) / len * (width * 0.5f);
            int i = _v.Count;
            const float y = 0.014f;
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

        // ------------------------------------------------------------------------------------------------ the ball's glow

        /// <summary>The halo round the ball (facing <paramref name="cam"/>), warmer and bigger for a fast or perfect shot.</summary>
        public void UpdateGlow(Vector3? ball, Camera cam, float ballSize, Color colour, float strength)
        {
            bool show = ball.HasValue && cam != null && strength > 0.01f;
            if (_glow.gameObject.activeSelf != show) _glow.gameObject.SetActive(show);
            if (!show) return;
            _glow.SetPositionAndRotation(ball.Value, cam.transform.rotation);
            _glow.localScale = Vector3.one * (ballSize * (2.4f + 1.2f * strength));
            colour.a = 0.22f + 0.3f * strength;
            _glowBlock.SetColor("_BaseColor", colour);
            _glow.GetComponent<Renderer>().SetPropertyBlock(_glowBlock);
        }

        // ------------------------------------------------------------------------------------------------ bursts

        /// <summary>A hit: stars bursting from where the racket met the ball (more, bigger and orange for a power shot).</summary>
        public void Hit(Vector3 at, bool power, Color colour)
        {
            int n = power ? 16 : 8;
            for (int i = 0; i < n; i++)
            {
                var v = Random.onUnitSphere * Random.Range(2.5f, power ? 6.5f : 4.5f);
                Emit(_stars, at, v, Random.Range(0.12f, power ? 0.3f : 0.22f), Color.Lerp(colour, Color.white, Random.value * 0.4f),
                     Random.Range(0.25f, 0.45f));
            }
        }

        /// <summary>The ball bounced: a scuff ring spreading on the grass and a few blades kicked up (red-tinged when it's
        /// out), as many as the ball was quick.</summary>
        public void Bounce(Vector3 at, bool inside, float speed)
        {
            var ground = new Vector3(at.x, 0.02f, at.z);
            Emit(_rings, ground, Vector3.zero, 0.6f + speed * 0.012f, inside ? new Color(1f, 1f, 0.92f, 0.55f) : new Color(1f, 0.45f, 0.38f, 0.8f), 0.45f);
            int n = Mathf.Clamp(Mathf.RoundToInt(speed * 0.35f), 3, 9);
            for (int i = 0; i < n; i++)
            {
                var v = new Vector3(Random.Range(-1f, 1f), Random.Range(1.6f, 3f), Random.Range(-1f, 1f));
                Emit(_grass, ground, v, Random.Range(0.06f, 0.11f), Grass(), Random.Range(0.35f, 0.55f));
            }
        }

        /// <summary>A toy set off or pulled up: a little grass kicked up behind its feet (against its run).</summary>
        public void Kick(Vector3 feet, Vector3 velocity)
        {
            var back = -velocity.normalized;
            for (int i = 0; i < 3; i++)
            {
                var v = back * Random.Range(0.6f, 1.6f) + new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(0.8f, 1.6f), Random.Range(-0.5f, 0.5f));
                Emit(_grass, new Vector3(feet.x, 0.05f, feet.z), v, Random.Range(0.05f, 0.09f), Grass(), Random.Range(0.3f, 0.45f));
            }
        }

        /// <summary>A won match (or the tournament): confetti in the brand's colours raining over <paramref name="over"/>.</summary>
        public void Confetti(Vector3 over, int count = 90)
        {
            Color[] colours = { TennisStadium.Sun, TennisStadium.Coral, TennisStadium.Sky, TennisStadium.Grape, TennisStadium.Leaf, Color.white };
            for (int i = 0; i < count; i++)
            {
                var p = over + new Vector3(Random.Range(-5f, 5f), Random.Range(3f, 6f), Random.Range(-3f, 3f));
                var v = new Vector3(Random.Range(-1.5f, 1.5f), Random.Range(-0.5f, 2.5f), Random.Range(-1.5f, 1.5f));
                Emit(_confetti, p, v, Random.Range(0.12f, 0.22f), colours[Random.Range(0, colours.Length)], Random.Range(2.2f, 3.4f));
            }
        }

        static Color Grass() => Color.Lerp(new Color(0.42f, 0.62f, 0.22f), new Color(0.7f, 0.82f, 0.4f), Random.value);

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
