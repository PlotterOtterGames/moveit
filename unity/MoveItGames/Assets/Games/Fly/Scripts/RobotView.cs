using UnityEngine;
using UnityEngine.Rendering;

namespace MoveIt.Games.Fly
{
    /// <summary>
    /// A flying robot (or, much bigger, the boss): a round metal body with one big eye, stubby arms and a propeller on
    /// top. Built from simple shapes. <see cref="FlyGame"/> places it; this animates what it is doing: bobbing while it
    /// hovers, its eye turning red and its body shaking just before it charges, spinning away when smashed, a flinch
    /// when the boss is hit, an open mouth when the boss throws. It faces -z (toward the hero and the camera).
    /// </summary>
    public sealed class RobotView : MonoBehaviour
    {
        static readonly Color Metal = new Color(0.62f, 0.66f, 0.78f);
        static readonly Color Dark = new Color(0.08f, 0.09f, 0.2f);
        static readonly Color Sun = new Color(1f, 0.79f, 0.24f);
        static readonly Color Coral = new Color(1f, 0.35f, 0.31f);

        public bool IsBoss { get; private set; }

        Transform _model, _propeller, _eye, _mouth, _armL, _armR;
        Renderer _eyeRenderer;
        Renderer[] _renderers;
        Color[] _colors;
        MaterialPropertyBlock _block;
        float _t, _flash, _warn, _mouthOpen;
        Vector3 _spin;
        bool _smashed;
        Color _flashColor = Color.white;

        /// <summary>A robot in <paramref name="paint"/> (its body colour). The boss is the same robot, much bigger, with
        /// a crown of spikes, eyebrows and a mouth.</summary>
        public static RobotView Create(Material toon, Color paint, bool boss = false)
        {
            var go = new GameObject(boss ? "Boss" : "Robot");
            var r = go.AddComponent<RobotView>();
            r.IsBoss = boss;
            r.Build(toon, paint);
            return r;
        }

        /// <summary>0..1 while it gets ready to charge (the eye reddens, it shakes).</summary>
        public void Warn(float amount) => _warn = Mathf.Clamp01(amount);

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

            if (_smashed) _model.localRotation *= Quaternion.Euler(_spin * dt);
            else
            {
                float shake = _warn * _warn * 0.06f;
                _model.localPosition = new Vector3(Mathf.Sin(_t * 47f) * shake, Mathf.Sin(_t * 3.1f) * 0.08f, 0f);
                _model.localRotation = Quaternion.Euler(0f, 180f + Mathf.Sin(_t * 1.7f) * 8f, Mathf.Sin(_t * 2.3f) * 6f + _flash * 12f);
                float wave = Mathf.Sin(_t * 6f) * 20f;
                _armL.localRotation = Quaternion.Euler(0f, 0f, -40f - wave - _warn * 50f);
                _armR.localRotation = Quaternion.Euler(0f, 0f, 40f + wave + _warn * 50f);
            }
            if (_mouth != null) _mouth.localScale = new Vector3(0.36f, Mathf.Lerp(0.04f, 0.2f, _mouthOpen), 0.06f);
            ApplyColors();
        }

        void Build(Material toon, Color paint)
        {
            _block = new MaterialPropertyBlock();
            _model = new GameObject("Model").transform;
            _model.SetParent(transform, false);
            _model.localRotation = Quaternion.Euler(0f, 180f, 0f);   // built facing +z, turned to face the hero
            var renderers = new System.Collections.Generic.List<Renderer>();
            var colors = new System.Collections.Generic.List<Color>();

            Transform Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Color color, Vector3 euler = default)
            {
                var go = GameObject.CreatePrimitive(type);
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(parent, false);
                go.transform.localPosition = pos;
                go.transform.localRotation = Quaternion.Euler(euler);
                go.transform.localScale = scale;
                var r = go.GetComponent<Renderer>();
                r.sharedMaterial = toon;
                r.shadowCastingMode = ShadowCastingMode.Off;
                renderers.Add(r);
                colors.Add(color);
                return go.transform;
            }

            Part(PrimitiveType.Sphere, _model, Vector3.zero, Vector3.one * 0.8f, paint);                                  // body
            Part(PrimitiveType.Cylinder, _model, new Vector3(0f, -0.02f, 0f), new Vector3(0.84f, 0.05f, 0.84f), Metal);    // band
            Part(PrimitiveType.Sphere, _model, new Vector3(0f, 0.08f, 0.3f), new Vector3(0.42f, 0.42f, 0.22f), Color.white);  // eye
            _eye = Part(PrimitiveType.Sphere, _model, new Vector3(0f, 0.08f, 0.4f), new Vector3(0.2f, 0.2f, 0.08f), Sun);
            _eyeRenderer = _eye.GetComponent<Renderer>();
            Part(PrimitiveType.Sphere, _model, new Vector3(0f, -0.38f, 0f), new Vector3(0.3f, 0.12f, 0.3f), Metal);       // thruster
            Part(PrimitiveType.Cylinder, _model, new Vector3(0f, 0.45f, 0f), new Vector3(0.05f, 0.08f, 0.05f), Metal);    // mast
            _propeller = new GameObject("Propeller").transform;
            _propeller.SetParent(_model, false);
            _propeller.localPosition = new Vector3(0f, 0.55f, 0f);
            Part(PrimitiveType.Cube, _propeller, Vector3.zero, new Vector3(0.7f, 0.02f, 0.08f), Dark);
            Part(PrimitiveType.Cube, _propeller, Vector3.zero, new Vector3(0.08f, 0.02f, 0.7f), Dark);

            _armL = new GameObject("Arm L").transform;
            _armL.SetParent(_model, false);
            _armL.localPosition = new Vector3(-0.4f, -0.02f, 0f);
            Part(PrimitiveType.Capsule, _armL, new Vector3(-0.15f, 0f, 0f), new Vector3(0.1f, 0.16f, 0.1f), Metal, new Vector3(0f, 0f, 90f));
            Part(PrimitiveType.Sphere, _armL, new Vector3(-0.32f, 0f, 0f), Vector3.one * 0.14f, Dark);
            _armR = new GameObject("Arm R").transform;
            _armR.SetParent(_model, false);
            _armR.localPosition = new Vector3(0.4f, -0.02f, 0f);
            Part(PrimitiveType.Capsule, _armR, new Vector3(0.15f, 0f, 0f), new Vector3(0.1f, 0.16f, 0.1f), Metal, new Vector3(0f, 0f, 90f));
            Part(PrimitiveType.Sphere, _armR, new Vector3(0.32f, 0f, 0f), Vector3.one * 0.14f, Dark);

            if (IsBoss)
            {
                // angry brows, a mouth, a crown of spikes
                Part(PrimitiveType.Cube, _model, new Vector3(-0.13f, 0.33f, 0.33f), new Vector3(0.22f, 0.05f, 0.05f), Dark, new Vector3(0f, 0f, -20f));
                Part(PrimitiveType.Cube, _model, new Vector3(0.13f, 0.33f, 0.33f), new Vector3(0.22f, 0.05f, 0.05f), Dark, new Vector3(0f, 0f, 20f));
                _mouth = Part(PrimitiveType.Cube, _model, new Vector3(0f, -0.2f, 0.36f), new Vector3(0.36f, 0.04f, 0.06f), Dark);
                for (int i = 0; i < 5; i++)
                {
                    float a = (i - 2) * 24f;
                    var q = Quaternion.Euler(0f, 0f, a);
                    Part(PrimitiveType.Capsule, _model, q * new Vector3(0f, 0.42f, 0f), new Vector3(0.07f, 0.12f, 0.07f), Sun, new Vector3(0f, 0f, a));
                }
            }

            _renderers = renderers.ToArray();
            _colors = colors.ToArray();
            ApplyColors();
        }

        void ApplyColors()
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                var c = _colors[i];
                if (_renderers[i] == _eyeRenderer) c = Color.Lerp(Sun, Coral, Mathf.Max(_warn, IsBoss ? 0.6f : 0f));
                _block.SetColor("_BaseColor", Color.Lerp(c, _flashColor, _flash * 0.85f));
                _renderers[i].SetPropertyBlock(_block);
            }
        }
    }
}
