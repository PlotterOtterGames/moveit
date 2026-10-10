using UnityEngine;

namespace MoveIt.Games.Runner
{
    /// <summary>
    /// Vrapo!'s juice: running dust, landing and lane-change puffs, egg sparkles, crash stars and dizzy stars, speed
    /// lines, a blob shadow, volcano smoke, drifting clouds and pterodactyls gliding past. Everything is built in code
    /// from four small materials (Tundu/Fx) so there are no prefabs to keep in sync.
    /// </summary>
    public sealed class RunnerFx : MonoBehaviour
    {
        [Header("Materials (Tundu/Fx)")]
        [SerializeField] Material dustMaterial;
        [SerializeField] Material starMaterial;
        [SerializeField] Material streakMaterial;
        [SerializeField] Material shadowMaterial;

        [Header("Sky")]
        [SerializeField] Transform[] smokeAnchors;
        [SerializeField] GameObject cloudPrefab;
        [SerializeField] GameObject pteroPrefab;
        [SerializeField] RuntimeAnimatorController pteroController;
        [SerializeField] Material skyMaterial;
        [SerializeField, Range(0, 16)] int clouds = 9;
        [SerializeField, Range(0, 4)] int pteros = 2;

        static readonly Color Dirt = new Color(0.93f, 0.8f, 0.6f, 0.75f);
        static readonly Color[] SparkleColors =
        {
            new Color(1f, 0.79f, 0.24f), Color.white, new Color(0.19f, 0.78f, 0.96f), new Color(1f, 0.55f, 0.5f),
        };

        Transform _player;
        Camera _cam;
        ParticleSystem _dust, _puffs, _stars, _speed, _smoke;
        Transform _shadow;
        readonly Transform[] _dizzy = new Transform[3];
        bool _dizzyOn;
        Vector3 _dizzyAt;
        float _speedAccumulator;
        Transform[] _clouds;
        Transform[] _pteros;
        Vector3[] _pteroVelocity;
        float[] _pteroWait;

        public void Setup(Transform player, Camera cam)
        {
            _player = player;
            _cam = cam;
            _dust = System("Dust", transform, dustMaterial, 200);
            _puffs = System("Puffs", transform, dustMaterial, 200);
            _stars = System("Stars", transform, starMaterial, 150);
            var starsMain = _stars.main;
            starsMain.gravityModifier = 1.4f;
            if (cam != null)
            {
                _speed = System("Speed Lines", cam.transform, streakMaterial, 120, worldSpace: false);
                var r = _speed.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.03f;
                r.lengthScale = 1.5f;
            }
            if (smokeAnchors != null && smokeAnchors.Length > 0) _smoke = System("Volcano Smoke", transform, dustMaterial, 120);

            _shadow = GameObject.CreatePrimitive(PrimitiveType.Quad).transform;
            Destroy(_shadow.GetComponent<Collider>());
            _shadow.name = "Blob Shadow";
            _shadow.GetComponent<Renderer>().sharedMaterial = shadowMaterial;
            _shadow.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _shadow.SetParent(transform, false);
            _shadow.rotation = Quaternion.Euler(90f, 0f, 0f);

            for (int i = 0; i < _dizzy.Length; i++)
            {
                var star = GameObject.CreatePrimitive(PrimitiveType.Quad).transform;
                Destroy(star.GetComponent<Collider>());
                star.name = "Dizzy Star";
                star.GetComponent<Renderer>().sharedMaterial = starMaterial;
                star.SetParent(transform, false);
                star.localScale = Vector3.one * 0.32f;
                star.gameObject.SetActive(false);
                _dizzy[i] = star;
            }
            BuildSky();
        }

        static ParticleSystem System(string name, Transform parent, Material material, int max, bool worldSpace = true)
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
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ps.Play();
            return ps;
        }

        static void Emit(ParticleSystem ps, Vector3 position, Vector3 velocity, float size, float life, Color color, float spin = 0f)
        {
            var p = new ParticleSystem.EmitParams
            {
                position = position, velocity = velocity, startSize = size, startLifetime = life, startColor = color,
                rotation = Random.Range(0f, 360f), angularVelocity = spin,
            };
            ps.Emit(p, 1);
        }

        // ---------------------------------------------------------------- per frame

        /// <summary>running: on the ground and running. speed: world speed (m/s), 0 when stopped.</summary>
        public void Tick(float dt, bool running, float speed, float speedFraction, float feetY)
        {
            if (_player == null) return;
            var feet = _player.position;
            // dust kicked up behind the feet, flying back with the ground
            if (running && Random.value < dt * 22f)
                Emit(_dust, feet + new Vector3(Random.Range(-0.25f, 0.25f), 0.08f, -0.25f),
                     new Vector3(Random.Range(-0.6f, 0.6f), Random.Range(0.4f, 1.1f), -speed * 0.95f),
                     Random.Range(0.3f, 0.55f), Random.Range(0.35f, 0.55f), Dirt);

            // speed lines once the run gets fast
            if (_speed != null)
            {
                _speedAccumulator += dt * 40f * Mathf.Clamp01((speedFraction - 0.35f) / 0.65f);
                while (_speedAccumulator >= 1f)
                {
                    _speedAccumulator -= 1f;
                    float a = Random.Range(0f, Mathf.PI * 2f);
                    var local = new Vector3(Mathf.Cos(a) * Random.Range(2.6f, 4f), Mathf.Sin(a) * Random.Range(1.6f, 2.4f) + 0.4f, 16f);
                    Emit(_speed, local, new Vector3(0f, 0f, -55f), Random.Range(0.05f, 0.09f), 0.35f, new Color(1f, 1f, 1f, 0.45f));
                }
            }

            // soft round shadow that shrinks while jumping
            float lift = Mathf.Clamp01(feetY / 1.6f);
            _shadow.position = new Vector3(feet.x, 0.03f, feet.z + 0.05f);
            _shadow.localScale = new Vector3(1.15f, 1.5f, 1f) * (1f - 0.4f * lift);

            if (_dizzyOn && _cam != null)
            {
                for (int i = 0; i < _dizzy.Length; i++)
                {
                    float a = Time.time * 4f + i * Mathf.PI * 2f / _dizzy.Length;
                    _dizzy[i].position = _dizzyAt + new Vector3(Mathf.Cos(a) * 0.45f, 0.08f * Mathf.Sin(a * 2f), Mathf.Sin(a) * 0.3f);
                    _dizzy[i].rotation = _cam.transform.rotation * Quaternion.Euler(0f, 0f, Time.time * 180f);
                }
            }

            UpdateSky(dt);
        }

        // ---------------------------------------------------------------- events

        public void Land(Vector3 at)
        {
            for (int i = 0; i < 10; i++)
            {
                float a = i * Mathf.PI * 2f / 10f;
                Emit(_puffs, at + new Vector3(0f, 0.08f, 0f), new Vector3(Mathf.Cos(a) * 2.2f, 0.5f, Mathf.Sin(a) * 1.2f),
                     Random.Range(0.35f, 0.5f), 0.45f, Dirt);
            }
        }

        public void LanePuff(Vector3 at, float direction)
        {
            for (int i = 0; i < 5; i++)
                Emit(_puffs, at + new Vector3(0f, 0.1f, Random.Range(-0.2f, 0.2f)),
                     new Vector3(-direction * Random.Range(1.5f, 2.5f), Random.Range(0.3f, 0.9f), -1f), Random.Range(0.3f, 0.45f), 0.4f, Dirt);
        }

        public void Egg(Vector3 at)
        {
            for (int i = 0; i < 12; i++)
            {
                var dir = Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y) + 0.3f;
                Emit(_stars, at, dir * Random.Range(2.5f, 4.5f), Random.Range(0.18f, 0.32f), Random.Range(0.4f, 0.6f),
                     SparkleColors[i % SparkleColors.Length], Random.Range(-360f, 360f));
            }
        }

        public void Crash(Vector3 at)
        {
            for (int i = 0; i < 16; i++)
            {
                var dir = Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y);
                Emit(_stars, at, dir * Random.Range(3f, 6f), Random.Range(0.25f, 0.45f), 0.7f, SparkleColors[i % 2], Random.Range(-500f, 500f));
            }
            for (int i = 0; i < 12; i++)
                Emit(_puffs, at - new Vector3(0f, 0.8f, 0f), Random.onUnitSphere * 2f + Vector3.up, Random.Range(0.4f, 0.7f), 0.6f, Dirt);
        }

        public void Dizzy(bool on, Vector3 headPosition)
        {
            _dizzyOn = on;
            _dizzyAt = headPosition;
            foreach (var s in _dizzy) s.gameObject.SetActive(on);
        }

        // ---------------------------------------------------------------- sky

        void BuildSky()
        {
            if (cloudPrefab != null)
            {
                _clouds = new Transform[clouds];
                for (int i = 0; i < clouds; i++)
                {
                    _clouds[i] = SkyThing(cloudPrefab, $"Cloud {i}");
                    _clouds[i].position = new Vector3(Random.Range(-70f, 70f), Random.Range(20f, 36f), Random.Range(70f, 130f));
                    _clouds[i].rotation = Quaternion.Euler(0f, Random.Range(-25f, 25f), 0f);
                    _clouds[i].localScale = Vector3.one * Random.Range(1.2f, 2.4f);
                }
            }
            if (pteroPrefab != null)
            {
                _pteros = new Transform[pteros];
                _pteroVelocity = new Vector3[pteros];
                _pteroWait = new float[pteros];
                for (int i = 0; i < pteros; i++)
                {
                    _pteros[i] = SkyThing(pteroPrefab, $"Pterodactyl {i}");
                    var animator = _pteros[i].GetComponentInChildren<Animator>();
                    if (animator == null) animator = _pteros[i].gameObject.AddComponent<Animator>();
                    animator.runtimeAnimatorController = pteroController;
                    animator.speed = Random.Range(0.85f, 1.15f);
                    _pteroWait[i] = Random.Range(0f, 6f) + i * 5f;
                    _pteros[i].gameObject.SetActive(false);
                }
            }
        }

        Transform SkyThing(GameObject prefab, string name)
        {
            var t = Instantiate(prefab, transform).transform;
            t.name = name;
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            {
                r.sharedMaterial = skyMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return t;
        }

        void UpdateSky(float dt)
        {
            if (_clouds != null)
                foreach (var c in _clouds)
                {
                    var p = c.position;
                    p.x += dt * 0.6f;
                    if (p.x > 80f) p.x = -80f;
                    c.position = p;
                }
            if (_pteros != null)
                for (int i = 0; i < _pteros.Length; i++)
                {
                    var t = _pteros[i];
                    if (!t.gameObject.activeSelf)
                    {
                        _pteroWait[i] -= dt;
                        if (_pteroWait[i] > 0f) continue;
                        float side = Random.value < 0.5f ? -1f : 1f;
                        t.position = new Vector3(-side * 40f, Random.Range(8f, 13f), Random.Range(28f, 50f));
                        _pteroVelocity[i] = new Vector3(side * Random.Range(4.5f, 6.5f), 0f, Random.Range(-1f, 1f));
                        t.rotation = Quaternion.LookRotation(_pteroVelocity[i]);
                        t.localScale = Vector3.one * Random.Range(2.2f, 2.8f);
                        t.gameObject.SetActive(true);
                    }
                    t.position += _pteroVelocity[i] * dt + Vector3.up * (Mathf.Sin(Time.time * 1.3f + i) * 0.4f * dt);
                    if (Mathf.Abs(t.position.x) > 45f)
                    {
                        t.gameObject.SetActive(false);
                        _pteroWait[i] = Random.Range(6f, 14f);
                    }
                }
            if (_smoke != null && Random.value < dt * 1.6f)
            {
                var anchor = smokeAnchors[Random.Range(0, smokeAnchors.Length)];
                float s = anchor.lossyScale.x;
                Emit(_smoke, anchor.position + new Vector3(Random.Range(-1f, 1f), 0f, 0f) * s,
                     new Vector3(Random.Range(0.3f, 0.8f), Random.Range(1.4f, 2.4f), 0f) * s, Random.Range(4f, 7f) * s, 7f,
                     new Color(0.93f, 0.93f, 0.97f, 0.8f));
            }
        }
    }
}
