using UnityEngine;
using UnityEngine.Rendering;

namespace MoveIt.Games.Fairy
{
    /// <summary>
    /// Zana!'s particles (one system each, Gara!'s way): sparkles where a bubble pops, a burst of stars and petals in its
    /// own colours where a creature is purified, a grey puff where the gloom hits a fairy, glitter falling from the wings,
    /// a rainbow ring for the big magic, confetti for a saved land, motes drifting through the air.
    /// </summary>
    public sealed class FairyFx : MonoBehaviour
    {
        ParticleSystem _sparkles, _bursts, _petals, _puffs, _glitter, _confetti, _motes;

        public static readonly Color Gold = new Color(1f, 0.85f, 0.35f), Pink = new Color(1f, 0.55f, 0.8f), Sky = new Color(0.5f, 0.85f, 1f),
                                     Lilac = new Color(0.72f, 0.6f, 1f), Leaf = new Color(0.5f, 0.9f, 0.45f);

        public static FairyFx Create(Material spark, Material dot)
        {
            var go = new GameObject("Fairy fx");
            var fx = go.AddComponent<FairyFx>();
            fx._sparkles = fx.Particles("Sparkles", spark, 400, 0f, 2.2f);
            fx._bursts = fx.Particles("Bursts", spark, 400, 0f, 1.8f);
            fx._petals = fx.Particles("Petals", spark, 300, 1.5f, 1.2f);
            fx._puffs = fx.Particles("Puffs", dot, 160, 0f, 2.5f);
            fx._glitter = fx.Particles("Glitter", spark, 300, 0.6f, 0.6f);
            fx._confetti = fx.Particles("Confetti", spark, 500, 3f, 0.8f);
            fx._motes = fx.Particles("Motes", dot, 160, 0f, 0f);
            // the garden's pollen fades in and out (the others start bright)
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(1f, 0.65f), new GradientAlphaKey(0f, 1f) });
            var col = fx._motes.colorOverLifetime;
            col.color = fade;
            var grow = fx._motes.sizeOverLifetime;
            grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1f));
            return fx;
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
            main.startLifetime = 0.6f;
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
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.4f));
            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-4f, 4f);
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

        /// <summary>A bubble popping: a little ring of sparks in its shape's colour.</summary>
        public void Pop(Vector3 at, Color colour, int count = 10)
        {
            for (int i = 0; i < count; i++)
            {
                var d = Random.onUnitSphere;
                Emit(_sparkles, at + d * 0.1f, d * Random.Range(2.5f, 4.5f), Random.Range(0.12f, 0.24f), colour, Random.Range(0.3f, 0.5f));
            }
            Emit(_sparkles, at, Vector3.zero, 0.9f, Color.white, 0.15f);
        }

        /// <summary>A creature purified: stars and petals burst out in its own colours, a soft flash.</summary>
        public void Purify(Vector3 at, Color a, Color b, float size)
        {
            int n = Mathf.RoundToInt(16f + 12f * size);
            for (int i = 0; i < n; i++)
            {
                var d = Random.onUnitSphere;
                Emit(_bursts, at + d * size * 0.3f, d * Random.Range(3f, 6f) * Mathf.Sqrt(size), Random.Range(0.18f, 0.36f), i % 2 == 0 ? a : b, Random.Range(0.5f, 0.85f));
            }
            for (int i = 0; i < n / 2; i++)
                Emit(_petals, at + Random.insideUnitSphere * size * 0.4f, new Vector3(Random.Range(-2f, 2f), Random.Range(1.5f, 4f), Random.Range(-1f, 2f)), Random.Range(0.14f, 0.26f),
                     i % 3 == 0 ? Gold : i % 3 == 1 ? Pink : Color.white, Random.Range(1f, 1.6f));
            Emit(_puffs, at, Vector3.zero, size * 3f, new Color(1f, 1f, 1f, 0.6f), 0.35f);
        }

        /// <summary>The gloom hits a fairy: a dark violet puff.</summary>
        public void Hit(Vector3 at)
        {
            for (int i = 0; i < 18; i++)
                Emit(_puffs, at + Random.insideUnitSphere * 0.3f, Random.insideUnitSphere * 3f, Random.Range(0.4f, 0.9f), new Color(0.35f, 0.2f, 0.5f, 0.75f), Random.Range(0.5f, 0.9f));
            for (int i = 0; i < 10; i++)
                Emit(_sparkles, at, Random.onUnitSphere * Random.Range(3f, 5f), Random.Range(0.15f, 0.3f), Lilac, Random.Range(0.3f, 0.5f));
        }

        /// <summary>A sparkle caught, a ring flown through: a quick ring of gold.</summary>
        public void Catch(Vector3 at, Color colour, int count = 8)
        {
            for (int i = 0; i < count; i++)
            {
                float a = i / (float)count * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                Emit(_sparkles, at, d * Random.Range(2f, 3.2f), Random.Range(0.12f, 0.22f), colour, Random.Range(0.3f, 0.45f));
            }
        }

        /// <summary>Glitter falling from a fairy's wings (every frame).</summary>
        public void Glitter(Vector3 at, Vector3 back, Color colour, float dt, float rate = 18f)
        {
            int n = Mathf.FloorToInt(rate * dt + Random.value);
            for (int i = 0; i < n; i++)
                Emit(_glitter, at + Random.insideUnitSphere * 0.3f, back * 1.5f + Random.insideUnitSphere * 0.6f, Random.Range(0.07f, 0.15f),
                     Random.value < 0.5f ? colour : Color.white, Random.Range(0.5f, 0.9f));
        }

        /// <summary>The big magic: a rainbow ring bursting out from the fairy.</summary>
        public void Rainbow(Vector3 at, Vector3 right, Vector3 up)
        {
            for (int i = 0; i < 90; i++)
            {
                float a = i / 90f * Mathf.PI * 2f;
                var d = right * Mathf.Cos(a) + up * Mathf.Sin(a);
                var c = Color.HSVToRGB(i / 90f, 0.6f, 1f);
                Emit(_bursts, at + d * 0.5f, d * Random.Range(9f, 12f), Random.Range(0.3f, 0.5f), c, Random.Range(0.8f, 1.1f));
            }
        }

        public void Confetti(Vector3 at, int count)
        {
            Color[] cs = { Gold, Pink, Sky, Lilac, Leaf, Color.white };
            for (int i = 0; i < count; i++)
                Emit(_confetti, at + Random.insideUnitSphere * 2f, new Vector3(Random.Range(-6f, 6f), Random.Range(5f, 12f), Random.Range(-3f, 6f)),
                     Random.Range(0.18f, 0.32f), cs[i % cs.Length], Random.Range(1.8f, 3f));
        }

        /// <summary>The garden's air (every frame): pollen and little lights drifting up through the evening sun over the
        /// meadow, round the tree, in front of the forest.</summary>
        public void Pollen(float dt, float wind)
        {
            int n = Mathf.FloorToInt(11f * dt + Random.value);
            for (int i = 0; i < n; i++)
            {
                var at = new Vector3(Random.Range(-13f, 13f), Random.Range(0.3f, 6.5f), Random.Range(0.5f, 13f));
                var v = new Vector3(wind * Random.Range(0.1f, 0.35f), Random.Range(0.08f, 0.3f), Random.Range(-0.05f, 0.1f));
                float k = Random.value;
                var c = k < 0.6f ? new Color(1f, 0.9f, 0.6f, 0.75f) : k < 0.85f ? new Color(1f, 0.7f, 0.85f, 0.7f) : new Color(0.75f, 1f, 0.7f, 0.7f);
                Emit(_motes, at, v, Random.Range(0.06f, 0.14f), c, Random.Range(3.5f, 6f));
            }
        }

        /// <summary>Motes drifting past the camera (every frame): the air felt, more as the fairies fly faster.</summary>
        public void Motes(Vector3 at, Vector3 forward, Vector3 up, float speed, float dt, Color colour)
        {
            float rate = 6f + speed * 1.6f;
            int n = Mathf.FloorToInt(rate * dt + Random.value);
            var right = Vector3.Cross(up, forward).normalized;
            for (int i = 0; i < n; i++)
            {
                var off = right * Random.Range(-10f, 10f) + up * Random.Range(-4f, 6f) + forward * Random.Range(12f, 30f);
                Emit(_motes, at + off, -forward * (speed * 0.15f) + Random.insideUnitSphere * 0.4f, Random.Range(0.08f, 0.2f), colour, Random.Range(1.5f, 2.6f));
            }
        }
    }
}
