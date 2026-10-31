using UnityEngine;

namespace MoveIt.Games.Fly
{
    /// <summary>
    /// A flying robot (or, much bigger, the boss): a round painted body with a shiny metal band, one big eye, stubby
    /// arms, a propeller on top and a glowing thruster below. Built from simple shapes merged into a few meshes (body,
    /// eye, propeller, arms, flame: about six draw calls) with vertex colours (Tundu/Toon, glossy). <see cref="FlyGame"/>
    /// places it; this animates what it is doing: bobbing while it hovers, its eye glowing red and its body shaking just
    /// before it charges, spinning away when smashed, a flinch when the boss is hit, an open mouth when the boss throws.
    /// The clash's champion wears big red boxing gloves and a gold belt and throws jabs (<see cref="Jab"/>). It faces -z
    /// (toward the hero and the camera).
    /// </summary>
    public sealed class RobotView : MonoBehaviour
    {
        static readonly Color Metal = new Color(0.66f, 0.7f, 0.82f);
        static readonly Color Dark = new Color(0.08f, 0.09f, 0.2f);
        static readonly Color Sun = new Color(1f, 0.79f, 0.24f);
        static readonly Color Coral = new Color(1f, 0.35f, 0.31f);
        static readonly Color Flame = new Color(1f, 0.62f, 0.24f);
        static readonly Color Glove = new Color(0.92f, 0.16f, 0.18f);

        public bool IsBoss { get; private set; }

        Transform _model, _propeller, _mouth, _armL, _armR, _flame;
        Renderer _eye;
        Renderer[] _renderers;
        MaterialPropertyBlock _block, _eyeBlock;
        float _t, _flash, _warn = -1f, _mouthOpen, _jabL = -1f, _jabR = -1f;
        Vector3 _spin;
        bool _smashed, _flashShown;
        Color _flashColor = Color.white;

        /// <summary>A robot in <paramref name="paint"/> (its body colour). The boss is the same robot, much bigger, with
        /// a crown of spikes, eyebrows and a mouth. An armoured robot (two punches) wears a metal helmet with a ridge.</summary>
        public static RobotView Create(Material actor, Color paint, bool boss = false, bool armoured = false, bool champion = false)
        {
            var go = new GameObject(boss ? "Boss" : champion ? "Champion" : armoured ? "Armoured robot" : "Robot");
            var r = go.AddComponent<RobotView>();
            r.IsBoss = boss;
            r._armoured = armoured;
            r._champion = champion;
            r.Build(actor, paint);
            return r;
        }

        bool _armoured, _champion;

        /// <summary>Builds a kind of robot's meshes now (at load), not when the first one flies in mid-fight.</summary>
        public static void Prewarm(Color paint, bool boss = false, bool armoured = false, bool champion = false) =>
            MeshesFor(paint, boss, armoured, champion);

        /// <summary>A jab with one arm, straight at the hero (the clash).</summary>
        public void Jab(bool right)
        {
            if (right) _jabR = 0f;
            else _jabL = 0f;
        }

        /// <summary>The arm's swing out to the front: 0 (at its side) .. 1 (straight at the hero), and back.</summary>
        static float JabAmount(ref float jab, float dt)
        {
            if (jab < 0f) return 0f;
            jab += dt;
            const float Out = 0.07f, Hold = 0.06f, Back = 0.16f;
            float k = jab < Out ? jab / Out : jab < Out + Hold ? 1f : 1f - (jab - Out - Hold) / Back;
            if (jab > Out + Hold + Back) jab = -1f;
            return Mathf.Clamp01(k);
        }

        /// <summary>0..1 while it gets ready to charge (the eye reddens and glows, it shakes).</summary>
        public void Warn(float amount)
        {
            amount = Mathf.Clamp01(amount);
            if (Mathf.Approximately(amount, _warn)) return;
            _warn = amount;
            float w = Mathf.Max(amount, IsBoss ? 0.6f : 0f);
            var eye = Color.Lerp(Sun, Coral, w);
            _eyeBlock.SetColor("_BaseColor", eye);
            _eyeBlock.SetColor("_EmissionColor", eye * Mathf.Lerp(0.5f, 2.2f, w));   // HDR: the bloom makes it glow
            _eye.SetPropertyBlock(_eyeBlock);
        }

        public void Flash(Color color)
        {
            _flashColor = color;
            _flash = 1f;
        }

        /// <summary>Knocked away: it tumbles (the game moves it).</summary>
        public void Smash(Vector3 spinDegreesPerSecond)
        {
            _smashed = true;
            _spin = spinDegreesPerSecond;
            Flash(Color.white);
            _flame.gameObject.SetActive(false);
        }

        /// <summary>The boss opens its mouth to throw.</summary>
        public void Throw() => _mouthOpen = 1f;

        void Update()
        {
            float dt = Time.deltaTime;
            _t += dt;
            _flash = Mathf.Max(0f, _flash - dt * 4f);
            _mouthOpen = Mathf.Max(0f, _mouthOpen - dt * 2.5f);
            _propeller.localRotation = Quaternion.Euler(0f, _t * 1100f, 0f);

            float warn = Mathf.Max(0f, _warn);
            if (_smashed) _model.localRotation *= Quaternion.Euler(_spin * dt);
            else
            {
                float shake = warn * warn * 0.06f;
                _model.localPosition = new Vector3(Mathf.Sin(_t * 47f) * shake, Mathf.Sin(_t * 3.1f) * 0.08f, 0f);
                _model.localRotation = Quaternion.Euler(0f, 180f + Mathf.Sin(_t * 1.7f) * 8f, Mathf.Sin(_t * 2.3f) * 6f + _flash * 12f);
                float wave = Mathf.Sin(_t * 6f) * 20f;
                // a jab swings the arm round to point at the hero (+z in the model) and stretches it
                float jl = JabAmount(ref _jabL, dt), jr = JabAmount(ref _jabR, dt);
                _armL.localRotation = Quaternion.Slerp(Quaternion.Euler(0f, 0f, -40f - wave - warn * 50f), Quaternion.Euler(0f, 90f, 0f), jl);
                _armR.localRotation = Quaternion.Slerp(Quaternion.Euler(0f, 0f, 40f + wave + warn * 50f), Quaternion.Euler(0f, -90f, 0f), jr);
                _armL.localScale = new Vector3(1f + 0.7f * jl, 1f, 1f);
                _armR.localScale = new Vector3(1f + 0.7f * jr, 1f, 1f);
                float flicker = 1f + Mathf.Sin(_t * 40f) * 0.12f + warn * 0.4f;
                _flame.localScale = new Vector3(0.2f, 0.26f * flicker, 0.2f);
            }
            if (_mouth != null) _mouth.localScale = new Vector3(1f, Mathf.Lerp(1f, 5f, _mouthOpen), 1f);
            ApplyFlash();
        }

        void ApplyFlash()
        {
            if (_flash <= 0f && !_flashShown) return;
            _flashShown = _flash > 0f;
            var f = new Color(_flashColor.r, _flashColor.g, _flashColor.b, _flash * 0.85f);
            _block.SetColor("_FlashColor", f);
            foreach (var r in _renderers) r.SetPropertyBlock(_block);
            _eyeBlock.SetColor("_FlashColor", f);
            _eye.SetPropertyBlock(_eyeBlock);
        }

        void Build(Material actor, Color paint)
        {
            _block = new MaterialPropertyBlock();
            _eyeBlock = new MaterialPropertyBlock();
            _model = new GameObject("Model").transform;
            _model.SetParent(transform, false);
            _model.localRotation = Quaternion.Euler(0f, 180f, 0f);   // built facing +z, turned to face the hero

            Transform Pivot(string name, Vector3 pos)
            {
                var t = new GameObject(name).transform;
                t.SetParent(_model, false);
                t.localPosition = pos;
                return t;
            }

            var meshes = MeshesFor(paint, IsBoss, _armoured, _champion);
            var eye = PartMesh.Show("Eye", _model, meshes.Eye, actor);
            eye.transform.localPosition = new Vector3(0f, 0.08f, 0.42f);
            eye.transform.localScale = new Vector3(0.2f, 0.2f, 0.08f);
            _eye = eye;
            _propeller = Pivot("Propeller", new Vector3(0f, 0.55f, 0f));
            _armL = Pivot("Arm L", new Vector3(-0.4f, -0.02f, 0f));
            _armR = Pivot("Arm R", new Vector3(0.4f, -0.02f, 0f));
            // the thruster's flame: emissive, so the bloom gives it a glow
            _flame = Pivot("Flame", new Vector3(0f, -0.5f, 0f));
            var flame = PartMesh.Show("Flame", _flame, meshes.Flame, actor);
            var flameBlock = new MaterialPropertyBlock();
            flameBlock.SetColor("_EmissionColor", Flame * 1.6f);
            flame.SetPropertyBlock(flameBlock);

            var renderers = new System.Collections.Generic.List<Renderer>
            {
                PartMesh.Show("Body", _model, meshes.Body, actor),
                PartMesh.Show("Propeller", _propeller, meshes.Propeller, actor),
                PartMesh.Show("Arm", _armL, meshes.ArmL, actor),
                PartMesh.Show("Arm", _armR, meshes.ArmR, actor),
            };
            if (IsBoss)
            {
                _mouth = Pivot("Mouth", new Vector3(0f, -0.2f, 0.36f));
                renderers.Add(PartMesh.Show("Mouth", _mouth, meshes.Mouth, actor));
            }
            _renderers = renderers.ToArray();
            Warn(0f);
        }

        sealed class Meshes
        {
            public Mesh Body, Eye, Propeller, ArmL, ArmR, Flame, Mouth;
        }

        /// <summary>Robots of one paint share their meshes (built once: a robot spawns every few seconds).</summary>
        static readonly System.Collections.Generic.Dictionary<(Color, bool, bool, bool), Meshes> Cache =
            new System.Collections.Generic.Dictionary<(Color, bool, bool, bool), Meshes>();

        static Meshes MeshesFor(Color paint, bool boss, bool armoured, bool champion = false)
        {
            // (meshes made in Play mode are gone after it: the editor keeps statics, so check)
            if (Cache.TryGetValue((paint, boss, armoured, champion), out var cached) && cached.Body != null) return cached;
            var body = new PartMesh()
                .Add(PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.8f, paint, 0.7f)
                .Add(PrimitiveType.Cylinder, new Vector3(0f, -0.02f, 0f), new Vector3(0.84f, 0.05f, 0.84f), Metal, 1.2f)
                .Add(PrimitiveType.Sphere, new Vector3(0f, 0.08f, 0.3f), new Vector3(0.44f, 0.44f, 0.22f), Metal, 1.2f)     // eye socket
                .Add(PrimitiveType.Sphere, new Vector3(0f, 0.08f, 0.33f), new Vector3(0.38f, 0.38f, 0.2f), Color.white, 0.8f)
                .Add(PrimitiveType.Sphere, new Vector3(0f, -0.38f, 0f), new Vector3(0.3f, 0.12f, 0.3f), Metal, 1.2f)       // thruster
                .Add(PrimitiveType.Cylinder, new Vector3(0f, 0.45f, 0f), new Vector3(0.05f, 0.08f, 0.05f), Metal, 1.2f)    // mast
                // rivets along the band
                .Add(PrimitiveType.Sphere, new Vector3(0.3f, -0.02f, 0.29f), Vector3.one * 0.06f, Metal, 1.2f)
                .Add(PrimitiveType.Sphere, new Vector3(-0.3f, -0.02f, 0.29f), Vector3.one * 0.06f, Metal, 1.2f);
            if (boss)
            {
                // angry brows and a crown of spikes
                body.Add(PrimitiveType.Cube, new Vector3(-0.13f, 0.33f, 0.33f), new Vector3(0.22f, 0.05f, 0.05f), Dark, 0.3f, new Vector3(0f, 0f, -20f))
                    .Add(PrimitiveType.Cube, new Vector3(0.13f, 0.33f, 0.33f), new Vector3(0.22f, 0.05f, 0.05f), Dark, 0.3f, new Vector3(0f, 0f, 20f));
                for (int i = 0; i < 5; i++)
                {
                    float a = (i - 2) * 24f;
                    body.Add(PrimitiveType.Capsule, Quaternion.Euler(0f, 0f, a) * new Vector3(0f, 0.42f, 0f), new Vector3(0.07f, 0.12f, 0.07f), Sun, 1f, new Vector3(0f, 0f, a));
                }
            }
            if (champion)
            {
                // a champion's gold belt over the band, a star on it, and frowning brows
                body.Add(PrimitiveType.Cylinder, new Vector3(0f, -0.12f, 0f), new Vector3(0.86f, 0.07f, 0.86f), Sun, 1.4f)
                    .Add(PrimitiveType.Sphere, new Vector3(0f, -0.12f, 0.42f), new Vector3(0.16f, 0.16f, 0.06f), Coral, 1f)
                    .Add(PrimitiveType.Cube, new Vector3(-0.13f, 0.33f, 0.33f), new Vector3(0.22f, 0.05f, 0.05f), Dark, 0.3f, new Vector3(0f, 0f, -18f))
                    .Add(PrimitiveType.Cube, new Vector3(0.13f, 0.33f, 0.33f), new Vector3(0.22f, 0.05f, 0.05f), Dark, 0.3f, new Vector3(0f, 0f, 18f));
            }
            if (armoured)
            {
                // a steel helmet over the top half, a ridge along it, and a visor rim above the eye
                var steel = new Color(0.78f, 0.8f, 0.86f);
                body.Add(PrimitiveType.Sphere, new Vector3(0f, 0.26f, -0.03f), new Vector3(0.86f, 0.46f, 0.86f), steel, 1.5f)
                    .Add(PrimitiveType.Cube, new Vector3(0f, 0.48f, -0.03f), new Vector3(0.08f, 0.1f, 0.6f), Sun, 1.2f)
                    .Add(PrimitiveType.Cube, new Vector3(0f, 0.3f, 0.37f), new Vector3(0.52f, 0.06f, 0.1f), Dark, 0.6f);
            }
            Mesh Arm(float side) => champion
                ? new PartMesh()   // big red boxing gloves
                    .Add(PrimitiveType.Capsule, new Vector3(side * 0.15f, 0f, 0f), new Vector3(0.1f, 0.16f, 0.1f), Metal, 1.2f, new Vector3(0f, 0f, 90f))
                    .Add(PrimitiveType.Cylinder, new Vector3(side * 0.27f, 0f, 0f), new Vector3(0.2f, 0.03f, 0.2f), Color.white, 0.6f, new Vector3(0f, 0f, 90f))
                    .Add(PrimitiveType.Sphere, new Vector3(side * 0.4f, 0f, 0f), new Vector3(0.3f, 0.27f, 0.27f), Glove, 1.4f)
                    .Build("Champion arm")
                : new PartMesh()
                    .Add(PrimitiveType.Capsule, new Vector3(side * 0.15f, 0f, 0f), new Vector3(0.1f, 0.16f, 0.1f), Metal, 1.2f, new Vector3(0f, 0f, 90f))
                    .Add(PrimitiveType.Sphere, new Vector3(side * 0.32f, 0f, 0f), Vector3.one * 0.14f, Dark, 0.6f)
                    .Build("Robot arm");
            var meshes = new Meshes
            {
                Body = body.Build("Robot body"),
                Eye = new PartMesh().Add(PrimitiveType.Sphere, Vector3.zero, Vector3.one, Color.white, 1.5f).Build("Robot eye"),
                Propeller = new PartMesh()
                    .Add(PrimitiveType.Cube, Vector3.zero, new Vector3(0.7f, 0.02f, 0.08f), Dark, 0.5f)
                    .Add(PrimitiveType.Cube, Vector3.zero, new Vector3(0.08f, 0.02f, 0.7f), Dark, 0.5f)
                    .Add(PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.08f, Metal, 1.2f)
                    .Build("Robot propeller"),
                ArmL = Arm(-1f),
                ArmR = Arm(1f),
                Flame = new PartMesh().Add(PrimitiveType.Sphere, Vector3.zero, Vector3.one, Flame, 0f).Build("Robot flame"),
                Mouth = boss ? new PartMesh().Add(PrimitiveType.Cube, Vector3.zero, new Vector3(0.36f, 0.04f, 0.06f), Dark, 0.3f).Build("Boss mouth") : null,
            };
            Cache[(paint, boss, armoured, champion)] = meshes;
            return meshes;
        }
    }
}
