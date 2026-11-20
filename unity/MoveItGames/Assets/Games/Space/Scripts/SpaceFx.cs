using UnityEngine;
using UnityEngine.Rendering;

namespace MoveIt.Games.Space
{
    /// <summary>
    /// Yjet!'s particles (one system each, Gara!'s way): golden sparkles where stardust is caught, a burst through a ring,
    /// grey dust and sparks where a rock bumps, a puff of stars for a perfect gate, confetti and fireworks for a won
    /// galaxy, and the space dust streaming past the camera (it sells the speed: more and longer at a rocket's speed).
    /// </summary>
    public sealed class SpaceFx : MonoBehaviour
    {
        ParticleSystem _sparkles, _bursts, _dust, _confetti, _streaks, _shooting, _trail;
        float _nextShooting = 3f;
        Transform _follow;

        public static readonly Color Gold = new Color(1f, 0.82f, 0.3f), Sky = new Color(0.45f, 0.85f, 1f), Pink = new Color(1f, 0.55f, 0.8f);

        public static SpaceFx Create(Material spark, Material dot)
        {
            var go = new GameObject("Space fx");
            var fx = go.AddComponent<SpaceFx>();
            fx._sparkles = fx.Particles("Sparkles", spark, 260, 0f, 2.5f);
            fx._bursts = fx.Particles("Bursts", spark, 200, 0f, 1.6f);
            fx._dust = fx.Particles("Dust", dot, 160, 0f, 2f);
            fx._confetti = fx.Particles("Confetti", spark, 400, 1.2f, 0.8f);
            fx._streaks = fx.Particles("Space dust", dot, 220, 0f, 0f);
            var r = fx._streaks.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.05f;
            r.lengthScale = 2f;
            fx._shooting = fx.Particles("Shooting stars", dot, 8, 0f, 0f);
            r = fx._shooting.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.12f;
            r.lengthScale = 1f;
            fx._trail = fx.Particles("Jetpack sparkles", spark, 160, 0f, 1.2f);
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

        /// <summary>Golden sparkles flying out of a caught piece of stardust (moving along with the astronaut).</summary>
        public void Sparkle(Vector3 at, Vector3 carry, Color colour, int count = 6)
        {
            for (int i = 0; i < count; i++)
                Emit(_sparkles, at, carry + Random.insideUnitSphere * 3.5f, Random.Range(0.18f, 0.38f), colour, Random.Range(0.35f, 0.6f));
        }

        /// <summary>A ring of stars bursting out (a ring flown through, a perfect gate, a big star).</summary>
        public void Burst(Vector3 at, Vector3 carry, Quaternion facing, Color colour, float radius, int count = 24)
        {
            for (int i = 0; i < count; i++)
            {
                float a = i / (float)count * Mathf.PI * 2f;
                var d = facing * new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                Emit(_bursts, at + d * radius, carry + d * Random.Range(4f, 7f), Random.Range(0.3f, 0.55f), colour, Random.Range(0.5f, 0.8f));
            }
        }

        /// <summary>Grey dust and a few sparks where a rock was bumped.</summary>
        /// <summary>A caught star: a small ring of sparks round it in its colour, quick and close (not across the screen).</summary>
        public void Pop(Vector3 at, Color colour, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var d = Random.onUnitSphere;
                Emit(_bursts, at + d * 0.1f, d * Random.Range(1.6f, 2.6f), Random.Range(0.12f, 0.22f), colour, Random.Range(0.25f, 0.4f));
            }
            Emit(_sparkles, at, Vector3.zero, 0.9f, Color.white, 0.18f);
        }

        public void Bump(Vector3 at, Vector3 carry)
        {
            for (int i = 0; i < 14; i++)
                Emit(_dust, at, carry * 0.4f + Random.insideUnitSphere * 4f, Random.Range(0.4f, 0.9f), new Color(0.7f, 0.66f, 0.8f, 0.8f), Random.Range(0.5f, 0.9f));
            for (int i = 0; i < 8; i++)
                Emit(_sparkles, at, carry * 0.5f + Random.insideUnitSphere * 6f, Random.Range(0.2f, 0.35f), Gold, Random.Range(0.3f, 0.5f));
        }

        public void Confetti(Vector3 at, int count)
        {
            Color[] cs = { Gold, SpaceArtColours.Coral, Sky, Pink, new Color(0.45f, 0.85f, 0.42f), new Color(0.6f, 0.5f, 1f) };
            for (int i = 0; i < count; i++)
                Emit(_confetti, at + Random.insideUnitSphere * 1.5f, new Vector3(Random.Range(-5f, 5f), Random.Range(4f, 11f), Random.Range(-5f, 5f)),
                     Random.Range(0.18f, 0.32f), cs[i % cs.Length], Random.Range(1.6f, 2.8f));
        }

        /// <summary>A firework: a burst of one colour high up.</summary>
        public void Firework(Vector3 at)
        {
            Color[] cs = { Gold, SpaceArtColours.Coral, Sky, Pink };
            var c = cs[Random.Range(0, cs.Length)];
            for (int i = 0; i < 36; i++)
                Emit(_confetti, at, Random.onUnitSphere * Random.Range(7f, 10f), Random.Range(0.3f, 0.5f), c, Random.Range(0.9f, 1.4f));
        }

        /// <summary>Now and then a shooting star far away across the sky round <paramref name="camera"/> (every frame).</summary>
        public void ShootingStars(Vector3 camera, Vector3 forward, float dt)
        {
            _nextShooting -= dt;
            if (_nextShooting > 0f) return;
            _nextShooting = Random.Range(2.5f, 6f);
            // ahead and above, crossing the view
            var dir = (forward + new Vector3(Random.Range(-1.2f, 1.2f), Random.Range(0.25f, 0.9f), 0f)).normalized;
            var across = Vector3.Cross(dir, Vector3.up).normalized * (Random.value < 0.5f ? -1f : 1f) + Vector3.down * Random.Range(0.2f, 0.6f);
            Emit(_shooting, camera + dir * 700f, across.normalized * Random.Range(260f, 380f), Random.Range(2.5f, 4f), new Color(1f, 0.95f, 0.85f, 0.9f), Random.Range(0.6f, 0.9f));
        }

        /// <summary>Sparkles left behind by a jetpack at <paramref name="at"/> (every frame while flying).</summary>
        public void Jetpack(Vector3 at, Vector3 back, float dt)
        {
            int n = Mathf.FloorToInt(14f * dt + Random.value);
            Color[] cs = { Gold, Pink, new Color(1f, 0.95f, 0.8f) };
            for (int i = 0; i < n; i++)
                Emit(_trail, at + Random.insideUnitSphere * 0.25f, back * 2f + Random.insideUnitSphere * 1.2f, Random.Range(0.14f, 0.3f), cs[Random.Range(0, cs.Length)], Random.Range(0.45f, 0.8f));
        }

        /// <summary>The space dust streaming past (every frame while flying): <paramref name="speed"/> m/s along
        /// <paramref name="forward"/>, round <paramref name="at"/> (the camera).</summary>
        public void Stream(Vector3 at, Vector3 forward, Vector3 up, float speed, float dt)
        {
            if (speed < 2f) return;
            float rate = Mathf.Clamp(speed * 2.2f, 0f, 90f);
            int n = Mathf.FloorToInt(rate * dt + Random.value);
            var right = Vector3.Cross(up, forward).normalized;
            for (int i = 0; i < n; i++)
            {
                var off = right * Random.Range(-9f, 9f) + up * Random.Range(-5f, 6f) + forward * Random.Range(18f, 34f);
                Emit(_streaks, at + off, -forward * (speed * 0.9f), Random.Range(0.05f, 0.12f), new Color(0.8f, 0.88f, 1f, 0.7f), Random.Range(0.8f, 1.2f));
            }
        }
    }

    /// <summary>The brand's colours as Unity colours (the HUD and the world agree).</summary>
    public static class SpaceArtColours
    {
        public static readonly Color Sun = new Color(1f, 0.79f, 0.24f), Coral = new Color(1f, 0.35f, 0.31f),
                                     Sky = new Color(0.19f, 0.78f, 0.96f), Grape = new Color(0.48f, 0.38f, 1f),
                                     Leaf = new Color(0.44f, 0.84f, 0.42f), Night = new Color(0.078f, 0.098f, 0.22f);
    }
}
