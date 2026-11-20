using UnityEngine;

namespace MoveIt.Games.Runner
{
    /// <summary>
    /// Vrapo!'s juice (the console polish of 2026-11-18): dust kicked up by every footstep, a ring of dust on landing,
    /// puffs as the runner steps aside, earth spraying from a belly slide, sparkles where an egg is picked up (a gold burst
    /// for a golden one), stars and a dust cloud in a crash and stars circling the dizzy head, confetti for a new record,
    /// leaves and petals drifting across the valley by day and fireflies by night, speed lines when the run gets fast,
    /// smoke rising from the volcano. Particles in one system each (Tundu/ValleyFx: on the curved world), built in code.
    /// </summary>
    public sealed class RunnerFx : MonoBehaviour
    {
        [Header("Materials (Tundu/ValleyFx)")]
        [SerializeField, Tooltip("Alpha, the soft dot: dust, smoke")] Material dustMaterial;
        [SerializeField, Tooltip("Added light, a star: sparkles, dizzy stars")] Material starMaterial;
        [SerializeField, Tooltip("Added light, a streak: speed lines")] Material streakMaterial;
        [SerializeField, Tooltip("Alpha, a leaf: drifting leaves, petals, confetti")] Material leafMaterial;
        [SerializeField, Tooltip("Added light, the soft dot: fireflies, glows")] Material glowMaterial;

        static readonly Color Earth = new Color(0.94f, 0.8f, 0.6f, 0.8f);
        static readonly Color[] SparkleColors =
        {
            new Color(1f, 0.79f, 0.24f), Color.white, new Color(0.19f, 0.78f, 0.96f), new Color(1f, 0.55f, 0.5f),
            new Color(0.55f, 1f, 0.6f),
        };
        static readonly Color[] ConfettiColors =
        {
            new Color(1f, 0.79f, 0.24f), new Color(1f, 0.35f, 0.31f), new Color(0.19f, 0.78f, 0.96f), new Color(0.48f, 0.38f, 1f),
            new Color(0.44f, 0.84f, 0.42f), Color.white,
        };
        static readonly Color[] LeafColors =
        {
            new Color(0.5f, 0.78f, 0.36f), new Color(0.8f, 0.86f, 0.4f), new Color(1f, 0.62f, 0.72f), new Color(1f, 0.92f, 0.7f),
        };

        Transform _player;
        Camera _cam;
        ParticleSystem _dust, _sparks, _confetti, _leaves, _flies, _speed, _smoke;
        readonly Transform[] _dizzy = new Transform[4];
        bool _dizzyOn;
        Vector3 _dizzyAt;
        float _speedAccumulator, _leafAccumulator, _flyAccumulator, _smokeAccumulator;
        Vector3 _crater;

        public void Setup(Transform player, Camera cam, Vector3 crater)
        {
            _player = player;
            _cam = cam;
            _crater = crater;
            _dust = System("Dust", transform, dustMaterial, 260);
            _sparks = System("Sparkles", transform, starMaterial, 220);
            var sm = _sparks.main;
            sm.gravityModifier = 1.2f;
            _confetti = System("Confetti", transform, leafMaterial, 240);
            var cm = _confetti.main;
            cm.gravityModifier = 0.55f;
            _leaves = System("Leaves", transform, leafMaterial, 90);
            _flies = System("Fireflies", transform, glowMaterial, 70);
            _smoke = System("Volcano Smoke", transform, dustMaterial, 80, curve: 0.25f);
            if (cam != null)
            {
                _speed = System("Speed Lines", cam.transform, streakMaterial, 120, worldSpace: false);
                var r = _speed.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.03f;
                r.lengthScale = 1.6f;
            }
            // the dizzy stars: cartoon yellow, big enough to read from the crash shot
            var dizzy = starMaterial != null ? new Material(starMaterial) { name = "Dizzy stars" } : null;
            if (dizzy != null) dizzy.SetColor("_BaseColor", new Color(1f, 0.86f, 0.32f, 1f));
            for (int i = 0; i < _dizzy.Length; i++)
            {
                var star = GameObject.CreatePrimitive(PrimitiveType.Quad).transform;
                Destroy(star.GetComponent<Collider>());
                star.name = "Dizzy Star";
                var renderer = star.GetComponent<Renderer>();
                renderer.sharedMaterial = dizzy;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                star.SetParent(transform, false);
                star.localScale = Vector3.one * 0.46f;
                star.gameObject.SetActive(false);
                _dizzy[i] = star;
            }
        }

        static ParticleSystem System(string name, Transform parent, Material material, int max, bool worldSpace = true, float curve = 1f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.maxParticles = max;
            main.simulationSpace = worldSpace ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            main.playOnAwake = false;
            main.startSpeed = 0f;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            color.color = fade;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.7f, 1f, 1.15f));
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            if (material != null)
            {
                var m = new Material(material) { name = name };
                m.SetFloat("_Curve", curve);
                renderer.sharedMaterial = m;
            }
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ps.Play();
            return ps;
        }

        static void Emit(ParticleSystem ps, Vector3 position, Vector3 velocity, float size, float life, Color color, float spin = 0f)
        {
            if (ps == null) return;
            var p = new ParticleSystem.EmitParams
            {
                position = position, velocity = velocity, startSize = size, startLifetime = life, startColor = color,
                rotation = Random.Range(0f, 360f), angularVelocity = spin,
            };
            ps.Emit(p, 1);
        }

        // ---------------------------------------------------------------- per frame

        /// <summary>speed: the world's speed (m/s), 0 when it's still; speedFraction 0..1 of the run's range; night 0..1;
        /// light: the sun's colour now (dust takes it).</summary>
        public void Tick(float dt, float speed, float speedFraction, float night, Color light)
        {
            if (_player == null) return;
            // speed lines once the run gets fast
            if (_speed != null)
            {
                _speedAccumulator += dt * 46f * Mathf.Clamp01((speedFraction - 0.4f) / 0.6f);
                while (_speedAccumulator >= 1f)
                {
                    _speedAccumulator -= 1f;
                    float a = Random.Range(0f, Mathf.PI * 2f);
                    var local = new Vector3(Mathf.Cos(a) * Random.Range(2.8f, 4.2f), Mathf.Sin(a) * Random.Range(1.6f, 2.5f) + 0.4f, 16f);
                    Emit(_speed, local, new Vector3(0f, 0f, -58f), Random.Range(0.05f, 0.09f), 0.33f, new Color(1f, 1f, 1f, 0.42f));
                }
            }
            // leaves and petals drifting across the path by day, coming with the world
            _leafAccumulator += dt * 4.5f * (1f - night);
            while (_leafAccumulator >= 1f)
            {
                _leafAccumulator -= 1f;
                var at = new Vector3(Random.Range(-9f, 9f), Random.Range(3.5f, 7f), Random.Range(8f, 45f));
                var c = LeafColors[Random.Range(0, LeafColors.Length)];
                c.a = 0.95f;
                Emit(_leaves, at, new Vector3(Random.Range(-1.2f, 1.2f), Random.Range(-1.1f, -0.5f), -speed + Random.Range(-0.5f, 0.5f)),
                     Random.Range(0.14f, 0.24f), Random.Range(3.5f, 5f), c, Random.Range(-160f, 160f));
            }
            // fireflies by night: slow, glowing, beside the path
            _flyAccumulator += dt * 14f * night;
            while (_flyAccumulator >= 1f)
            {
                _flyAccumulator -= 1f;
                float side = Random.value < 0.5f ? -1f : 1f;
                var at = new Vector3(side * Random.Range(3.5f, 11f), Random.Range(0.4f, 2.6f), Random.Range(4f, 60f));
                Emit(_flies, at, new Vector3(Random.Range(-0.4f, 0.4f), Random.Range(-0.15f, 0.3f), -speed + Random.Range(-0.4f, 0.4f)),
                     Random.Range(0.18f, 0.32f), Random.Range(2.5f, 4.5f), new Color(0.85f, 1f, 0.45f, 1f));
            }
            // the volcano's smoke, rising and drifting
            _smokeAccumulator += dt * 1.6f;
            while (_smokeAccumulator >= 1f)
            {
                _smokeAccumulator -= 1f;
                var tint = Color.Lerp(new Color(0.96f, 0.95f, 0.98f, 0.75f), new Color(0.5f, 0.52f, 0.7f, 0.75f), night);
                Emit(_smoke, _crater + new Vector3(Random.Range(-5f, 5f), 0f, Random.Range(-5f, 5f)),
                     new Vector3(Random.Range(0.8f, 2.2f), Random.Range(3.2f, 5f), 0f), Random.Range(12f, 18f), 9f, tint);
            }
            if (_dizzyOn && _cam != null)
                for (int i = 0; i < _dizzy.Length; i++)
                {
                    float a = Time.time * 4f + i * Mathf.PI * 2f / _dizzy.Length;
                    _dizzy[i].position = _dizzyAt + new Vector3(Mathf.Cos(a) * 0.5f, 0.09f * Mathf.Sin(a * 2f), Mathf.Sin(a) * 0.34f);
                    _dizzy[i].rotation = _cam.transform.rotation * Quaternion.Euler(0f, 0f, Time.time * 200f);
                }
        }

        // ---------------------------------------------------------------- events

        public void Footstep(Vector3 at, float speed)
        {
            for (int i = 0; i < 2; i++)
                Emit(_dust, at + new Vector3(Random.Range(-0.12f, 0.12f), 0.06f, -0.1f),
                     new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(0.4f, 1.0f), -speed * 0.9f),
                     Random.Range(0.32f, 0.5f), Random.Range(0.35f, 0.5f), Earth);
        }

        public void Land(Vector3 at, float speed)
        {
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI * 2f / 12f;
                Emit(_dust, at + new Vector3(0f, 0.08f, 0f), new Vector3(Mathf.Cos(a) * 2.4f, 0.45f, Mathf.Sin(a) * 1.4f - speed * 0.85f),
                     Random.Range(0.38f, 0.55f), 0.5f, Earth);
            }
        }

        public void TakeOff(Vector3 at, float speed)
        {
            for (int i = 0; i < 6; i++)
                Emit(_dust, at + new Vector3(Random.Range(-0.3f, 0.3f), 0.05f, 0f),
                     new Vector3(Random.Range(-1f, 1f), Random.Range(0.2f, 0.8f), -speed * 0.85f), Random.Range(0.3f, 0.45f), 0.4f, Earth);
        }

        public void LanePuff(Vector3 at, float direction, float speed)
        {
            for (int i = 0; i < 6; i++)
                Emit(_dust, at + new Vector3(0f, 0.1f, Random.Range(-0.2f, 0.2f)),
                     new Vector3(-direction * Random.Range(1.6f, 2.8f), Random.Range(0.3f, 0.9f), -speed * 0.8f),
                     Random.Range(0.3f, 0.48f), 0.42f, Earth);
        }

        /// <summary>The belly slide's spray (each frame of it).</summary>
        public void Slide(Vector3 at, float speed, float dt)
        {
            if (Random.value > dt * 30f) return;
            for (int i = 0; i < 2; i++)
                Emit(_dust, at + new Vector3(Random.Range(-0.3f, 0.3f), 0.08f, 0.5f),
                     new Vector3(Random.Range(-1.8f, 1.8f), Random.Range(0.6f, 1.6f), -speed * 0.7f), Random.Range(0.3f, 0.5f), 0.45f, Earth);
        }

        public void Egg(Vector3 at, bool golden)
        {
            int n = golden ? 34 : 12;
            for (int i = 0; i < n; i++)
            {
                var dir = Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y) + 0.3f;
                var c = golden ? Color.Lerp(new Color(1f, 0.82f, 0.3f), Color.white, Random.value * 0.5f) : SparkleColors[i % SparkleColors.Length];
                Emit(_sparks, at, dir * Random.Range(2.5f, golden ? 6.5f : 4.5f), Random.Range(0.18f, golden ? 0.42f : 0.32f),
                     Random.Range(0.4f, golden ? 0.9f : 0.6f), c, Random.Range(-360f, 360f));
            }
        }

        public void Crash(Vector3 at)
        {
            for (int i = 0; i < 18; i++)
            {
                var dir = Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y);
                Emit(_sparks, at, dir * Random.Range(3f, 6.5f), Random.Range(0.25f, 0.48f), 0.75f, SparkleColors[i % 2], Random.Range(-500f, 500f));
            }
            for (int i = 0; i < 14; i++)
                Emit(_dust, at - new Vector3(0f, 0.8f, 0f), Random.onUnitSphere * 2.2f + Vector3.up, Random.Range(0.45f, 0.8f), 0.7f, Earth);
        }

        public void Confetti(Vector3 at, int count = 90)
        {
            for (int i = 0; i < count; i++)
            {
                var dir = new Vector3(Random.Range(-1f, 1f), Random.Range(0.9f, 1.8f), Random.Range(-1f, 1f));
                Emit(_confetti, at + Random.insideUnitSphere * 0.4f, dir * Random.Range(3.5f, 6.5f), Random.Range(0.13f, 0.22f),
                     Random.Range(1.8f, 2.8f), ConfettiColors[i % ConfettiColors.Length], Random.Range(-600f, 600f));
            }
        }

        public void Dizzy(bool on, Vector3 headPosition)
        {
            _dizzyOn = on;
            _dizzyAt = headPosition;
            foreach (var s in _dizzy)
                if (s != null) s.gameObject.SetActive(on);
        }
    }
}
