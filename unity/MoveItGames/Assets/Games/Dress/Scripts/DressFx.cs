using MoveIt.Motion;
using UnityEngine;
using UnityEngine.Rendering;
using Joint = MoveIt.Motion.Joint;

namespace MoveIt.Games.Dress
{
    /// <summary>
    /// Vishu!'s particles (the shapes in dress_fx.png, 4 x 2: sparkle, heart, butterfly, bubble, snowflake, star,
    /// confetti, glow): the "poof" when something is put on, glints twinkling on gems and sequins, the wand's trail,
    /// the six magic effects around the player, and confetti for the show's results. World units = HUD pixels.
    /// </summary>
    public sealed class DressFx
    {
        enum Shape { Sparkle, Heart, Butterfly, Bubble, Snow, Star, Confetti, Glow }

        readonly ParticleSystem[] _systems = new ParticleSystem[8];
        readonly Material _material, _additive;
        float _ambient, _glint, _trail;
        Vector2 _lastTip;
        bool _hadTip;

        static readonly Color[] Rainbow =
        {
            new Color(1f, 0.49f, 0.78f), new Color(1f, 0.82f, 0.25f), new Color(0.43f, 0.76f, 1f), new Color(0.69f, 0.55f, 1f),
            new Color(0.39f, 0.84f, 0.65f), new Color(1f, 0.42f, 0.35f),
        };

        public DressFx(Transform parent, Material fx, Texture2D sheet)
        {
            _material = new Material(fx);
            _material.SetTexture("_BaseMap", sheet);
            _material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            _material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            _material.SetFloat("_ZTest", (float)CompareFunction.Always);
            _material.renderQueue = 3100;
            _additive = new Material(_material);
            _additive.SetFloat("_DstBlend", (float)BlendMode.One);
            _additive.renderQueue = 3110;
            for (int i = 0; i < _systems.Length; i++)
            {
                var shape = (Shape)i;
                bool glow = shape == Shape.Sparkle || shape == Shape.Glow || shape == Shape.Star;
                _systems[i] = Make(shape.ToString(), parent, glow ? _additive : _material, i, shape == Shape.Butterfly);
            }
        }

        public void Dispose()
        {
            Object.Destroy(_material);
            Object.Destroy(_additive);
        }

        public void Clear()
        {
            foreach (var ps in _systems) ps.Clear();
        }

        static ParticleSystem Make(string name, Transform parent, Material material, int tile, bool flutter)
        {
            var go = new GameObject("Fx " + name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0f, -1f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.maxParticles = 600;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;
            main.startSpeed = 0f;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startSize3D = flutter;   // a butterfly's width and height are animated apart (its wings beat)
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;
            var sheet = ps.textureSheetAnimation;
            sheet.enabled = true;
            sheet.numTilesX = 4;
            sheet.numTilesY = 2;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            // one tile for the whole life (frames count from the top-left; row 0 = sparkle, heart, butterfly, bubble)
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve((tile + 0.5f) / 8f);
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0f);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            if (flutter)
            {
                // a butterfly beats its wings: its width goes in and out
                size.separateAxes = true;
                var flap = new AnimationCurve();
                for (int k = 0; k <= 24; k++) flap.AddKey(k / 24f, k % 2 == 0 ? 1f : 0.35f);
                size.x = new ParticleSystem.MinMaxCurve(1f, flap);
                size.y = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 1f));
                size.z = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 1f));
            }
            else size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(0.15f, 1f), new Keyframe(0.7f, 0.9f), new Keyframe(1f, 0f)));
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            color.color = fade;
            var noise = ps.noise;
            noise.enabled = flutter || name == "Bubble" || name == "Snow";
            noise.strength = flutter ? 90f : 40f;
            noise.frequency = flutter ? 0.004f : 0.003f;
            noise.scrollSpeed = 0.3f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.alignment = ParticleSystemRenderSpace.View;
            ps.Play();
            return ps;
        }

        /// <summary>Body positions (the outfit's camera space) to the screen; set every frame by the game.</summary>
        public Matrix4x4 ToWorld = Matrix4x4.identity;

        /// <summary>A particle at a body position (screen = true: already on the screen).</summary>
        void Emit(Shape shape, Vector2 at, Vector2 velocity, float size, float life, Color color, float spin = 0f, bool screen = false)
        {
            Vector3 p = screen ? (Vector3)at : ToWorld.MultiplyPoint3x4(at);
            p.z = -1f;
            var emit = new ParticleSystem.EmitParams
            {
                position = p, velocity = velocity, startLifetime = life,
                startColor = color, rotation = shape == Shape.Butterfly || shape == Shape.Heart ? Random.Range(-15f, 15f) : Random.Range(0f, 360f),
                angularVelocity = spin,
            };
            if (shape == Shape.Butterfly) emit.startSize3D = new Vector3(size, size, size);
            else emit.startSize = size;
            _systems[(int)shape].Emit(emit, 1);
        }

        /// <summary>Something was just put on: a burst of sparkles and stars, a soft flash.</summary>
        public void Poof(Vector2 at, Color color, float scale)
        {
            Emit(Shape.Glow, at, Vector2.zero, 260f * scale, 0.45f, new Color(1f, 1f, 1f, 0.8f));
            for (int i = 0; i < 16; i++)
            {
                Vector2 dir = Random.insideUnitCircle.normalized;
                var c = i % 3 == 0 ? Color.white : Color.Lerp(color, Color.white, 0.35f);
                Emit(i % 4 == 0 ? Shape.Star : Shape.Sparkle, at + dir * (20f * scale), dir * Random.Range(160f, 420f) * scale,
                     Random.Range(26f, 52f) * scale, Random.Range(0.45f, 0.8f), c, Random.Range(-240f, 240f));
            }
        }

        /// <summary>A big celebratory burst (a whole new look, a pose that counted).</summary>
        public void Burst(Vector2 at, float scale)
        {
            for (int i = 0; i < 26; i++)
            {
                Vector2 dir = Random.insideUnitCircle.normalized;
                Emit(i % 3 == 0 ? Shape.Star : i % 3 == 1 ? Shape.Heart : Shape.Sparkle, at, dir * Random.Range(250f, 650f) * scale,
                     Random.Range(30f, 64f) * scale, Random.Range(0.6f, 1.1f), Rainbow[i % Rainbow.Length], Random.Range(-200f, 200f));
            }
        }

        /// <summary>Confetti raining from the top (the show's results).</summary>
        public void Confetti(float width, int count)
        {
            for (int i = 0; i < count; i++)
                Emit(Shape.Confetti, new Vector2(Random.Range(0f, width), 1100f + Random.Range(0f, 300f)),
                     new Vector2(Random.Range(-60f, 60f), Random.Range(-420f, -260f)), Random.Range(14f, 26f), Random.Range(2.5f, 3.8f),
                     Rainbow[i % Rainbow.Length], Random.Range(-500f, 500f), true);
        }

        /// <summary>Glints on the worn gems and sequins, the wand's trail, the magic effect (positions in body space).</summary>
        public void Tick(OutfitRenderer outfit, OutfitPose pose, Effect effect, float dt, float width)
        {
            // glints: a few twinkles a second on random sparkle points of what is worn
            _glint += dt * 7f;
            while (_glint >= 1f)
            {
                _glint -= 1f;
                if (outfit.Glints.Count == 0) break;
                var g = outfit.Glints[Random.Range(0, outfit.Glints.Count)];
                if (g.z > 1f) Emit(Shape.Sparkle, g, Vector2.zero, Mathf.Clamp(g.z * 1.8f, 22f, 48f), Random.Range(0.35f, 0.55f), Color.white, Random.Range(-90f, 90f));
            }
            // the wand leaves sparkles where it moves
            if (outfit.WandTip.HasValue)
            {
                Vector2 tip = outfit.WandTip.Value;
                float speed = _hadTip && dt > 0f ? (tip - _lastTip).magnitude / dt : 0f;
                _trail += dt * Mathf.Clamp(speed / 30f, 2f, 40f);
                while (_trail >= 1f)
                {
                    _trail -= 1f;
                    Vector2 at = _hadTip ? Vector2.Lerp(_lastTip, tip, Random.value) : tip;
                    Emit(Random.value < 0.3f ? Shape.Star : Shape.Sparkle, at + Random.insideUnitCircle * 8f, Random.insideUnitCircle * 40f + Vector2.down * 30f,
                         Random.Range(16f, 34f), Random.Range(0.4f, 0.8f), Rainbow[Random.Range(0, Rainbow.Length)], Random.Range(-180f, 180f));
                }
                _lastTip = tip;
                _hadTip = true;
            }
            else _hadTip = false;

            if (effect == Effect.None || !pose.Valid) return;
            float t = pose.T;
            Vector2 sc = pose.ShoulderC, hc = pose.HipC;
            float rate = effect switch { Effect.Snow => 26f, Effect.Butterflies => 2.4f, Effect.Bubbles => 5f, Effect.Hearts => 4f, Effect.Stars => 5f, _ => 14f };
            _ambient += dt * rate;
            while (_ambient >= 1f)
            {
                _ambient -= 1f;
                Vector2 around = Vector2.Lerp(sc, hc, Random.value) + new Vector2(Random.Range(-0.9f, 0.9f) * t, Random.Range(-0.5f, 0.9f) * t);
                switch (effect)
                {
                    case Effect.Sparkles:
                        Emit(Shape.Sparkle, around, Random.insideUnitCircle * 20f, Random.Range(16f, 38f), Random.Range(0.5f, 1.0f),
                             Color.Lerp(Color.white, Rainbow[Random.Range(0, Rainbow.Length)], 0.3f), Random.Range(-120f, 120f));
                        break;
                    case Effect.Hearts:
                    {
                        Vector2 hand = pose.J[(int)(Random.value < 0.5f ? Joint.LeftWrist : Joint.RightWrist)];
                        Vector2 from = Random.value < 0.5f ? hand : sc + new Vector2(Random.Range(-0.5f, 0.5f) * t, 0f);
                        Emit(Shape.Heart, from, new Vector2(Random.Range(-30f, 30f), Random.Range(80f, 150f)), Random.Range(26f, 48f),
                             Random.Range(1.4f, 2.2f), new Color(1f, Random.Range(0.3f, 0.6f), Random.Range(0.55f, 0.8f)));
                        break;
                    }
                    case Effect.Butterflies:
                        Emit(Shape.Butterfly, around + new Vector2(Random.Range(-1, 2) * 0.6f * t, 0f),
                             new Vector2(Random.Range(-40f, 40f), Random.Range(10f, 50f)), Random.Range(64f, 96f), Random.Range(3.5f, 5f),
                             Rainbow[Random.Range(0, Rainbow.Length)]);
                        break;
                    case Effect.Bubbles:
                        Emit(Shape.Bubble, new Vector2(around.x, hc.y - Random.Range(0.2f, 1.2f) * t), new Vector2(Random.Range(-15f, 15f), Random.Range(60f, 120f)),
                             Random.Range(20f, 56f), Random.Range(2.5f, 4f), new Color(0.85f, 0.97f, 1f, 0.9f));
                        break;
                    case Effect.Snow:
                        Emit(Shape.Snow, new Vector2(Random.Range(0f, width), 1100f), new Vector2(Random.Range(-25f, 25f), Random.Range(-110f, -60f)),
                             Random.Range(12f, 30f), Random.Range(9f, 13f), new Color(1f, 1f, 1f, 0.95f), Random.Range(-60f, 60f), true);
                        break;
                    case Effect.Stars:
                        Emit(Shape.Star, around, Random.insideUnitCircle * 30f, Random.Range(18f, 40f), Random.Range(0.6f, 1.2f),
                             new Color(1f, Random.Range(0.75f, 0.92f), Random.Range(0.25f, 0.5f)), Random.Range(-90f, 90f));
                        break;
                }
            }
        }
    }
}
