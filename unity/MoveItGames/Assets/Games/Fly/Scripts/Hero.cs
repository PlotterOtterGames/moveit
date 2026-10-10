using UnityEngine;
using UnityEngine.Rendering;

namespace MoveIt.Games.Fly
{
    /// <summary>
    /// The flying hero, seen from behind: a chunky kid in a suit and a big cape, lying forward in the air like the
    /// classic superhero, fists ready by the chest. Built from simple shapes (Blender art can replace it later).
    /// <see cref="FlyGame"/> moves it; this animates: banking into turns, the cape flapping, a fist shooting forward on
    /// <see cref="Punch"/>, a flash and a wobble on <see cref="Hurt"/>, a loop on <see cref="Cheer"/>. Its local +z points
    /// ahead (away from the camera), +x is its right (the screen's right). The root is the middle of its body.
    /// </summary>
    public sealed class Hero : MonoBehaviour
    {
        static readonly Color Suit = new Color(0.48f, 0.38f, 1f);       // grape
        static readonly Color Cape = new Color(1f, 0.35f, 0.31f);       // coral
        static readonly Color Sun = new Color(1f, 0.79f, 0.24f);
        static readonly Color Skin = new Color(0.98f, 0.8f, 0.66f);
        static readonly Color Hair = new Color(0.25f, 0.17f, 0.12f);
        static readonly Color Boot = new Color(0.12f, 0.12f, 0.22f);

        Transform _model, _cape, _armL, _armR, _fistL, _fistR, _legs;
        Renderer[] _renderers;
        Color[] _colors;
        MaterialPropertyBlock _block;
        float _punchL = -1f, _punchR = -1f, _hurt, _flash, _cheer = -1f, _bank, _pitch, _t;
        Color _flashColor = Color.white;
        Vector3 _lastPosition;

        /// <summary>Where the punching fist ends up (world), for sparks.</summary>
        public Vector3 FistWorld(bool right) => (right ? _fistR : _fistL).position;

        public static Hero Create(Material toon)
        {
            var go = new GameObject("Hero");
            var hero = go.AddComponent<Hero>();
            hero.Build(toon);
            return hero;
        }

        /// <summary>A fist shoots forward (and comes back).</summary>
        public void Punch(bool right)
        {
            if (right) _punchR = 0f;
            else _punchL = 0f;
        }

        public void Hurt()
        {
            _hurt = 1f;
            Flash(Cape, 0.6f);
        }

        public void Flash(Color color, float amount = 1f)
        {
            _flashColor = color;
            _flash = amount;
        }

        /// <summary>A happy loop in the air (level cleared, game won).</summary>
        public void Cheer() => _cheer = 0f;

        void Update()
        {
            float dt = Time.deltaTime;
            _t += dt;
            _flash = Mathf.Max(0f, _flash - dt * 3f);
            _hurt = Mathf.Max(0f, _hurt - dt * 2f);

            // bank and pitch with the movement (the root is moved by the game)
            var velocity = dt > 0f ? (transform.position - _lastPosition) / dt : Vector3.zero;
            _lastPosition = transform.position;
            float ease = 1f - Mathf.Exp(-8f * dt);
            _bank = Mathf.Lerp(_bank, Mathf.Clamp(-velocity.x * 9f, -40f, 40f), ease);
            _pitch = Mathf.Lerp(_pitch, Mathf.Clamp(-velocity.y * 8f, -25f, 25f), ease);
            float roll = _bank + Mathf.Sin(_hurt * 30f) * 25f * _hurt;
            float loop = 0f;
            if (_cheer >= 0f)
            {
                _cheer += dt;
                loop = Mathf.SmoothStep(0f, 360f, Mathf.Clamp01(_cheer / 1.1f));
                if (_cheer > 1.1f) _cheer = -1f;
            }
            // lying forward like the classic superhero (bobbing a little), banked, looping when cheering
            _model.localRotation = Quaternion.Euler(-loop, 0f, 0f) * Quaternion.Euler(_pitch, 0f, roll) * Quaternion.Euler(72f, 0f, 0f);
            _model.localPosition = new Vector3(0f, Mathf.Sin(_t * 2.2f) * 0.06f, 0f);

            // the cape ripples in the wind
            _cape.localRotation = Quaternion.Euler(8f + Mathf.Sin(_t * 9f) * 6f, Mathf.Sin(_t * 5.3f) * 4f, -roll * 0.3f);
            _cape.localScale = new Vector3(1f, 1f + Mathf.Sin(_t * 11f) * 0.04f, 1f);
            _legs.localRotation = Quaternion.Euler(Mathf.Sin(_t * 3f) * 5f, 0f, 0f);

            AnimateArm(_armL, ref _punchL, -1f, dt);
            AnimateArm(_armR, ref _punchR, 1f, dt);
            ApplyColors();
        }

        // An arm pivots at its shoulder: by the chest (ready), or straight ahead (punching). In the model's frame
        // (lying forward), "ahead" of the hero is the model's +y (its head's direction).
        void AnimateArm(Transform arm, ref float punch, float side, float dt)
        {
            const float Out = 0.08f, Hold = 0.12f, Back = 0.2f;
            float k = 0f;
            if (punch >= 0f)
            {
                punch += dt;
                k = punch < Out ? punch / Out : punch < Out + Hold ? 1f : 1f - (punch - Out - Hold) / Back;
                if (punch > Out + Hold + Back) punch = -1f;
            }
            k = Mathf.Clamp01(k);
            var ready = Quaternion.Euler(-130f, 0f, side * 30f);    // the fist held up by the chest
            var straight = Quaternion.Euler(-180f, 0f, side * -4f); // the arm pointing ahead
            arm.localRotation = Quaternion.Slerp(ready, straight, k);
            arm.localScale = new Vector3(1f, Mathf.Lerp(0.7f, 1.2f, k), 1f);
        }

        void Build(Material toon)
        {
            _block = new MaterialPropertyBlock();
            _model = new GameObject("Model").transform;
            _model.SetParent(transform, false);
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

            Transform Pivot(string name, Transform parent, Vector3 pos)
            {
                var t = new GameObject(name).transform;
                t.SetParent(parent, false);
                t.localPosition = pos;
                return t;
            }

            // Built standing up (head at +y, facing +z); the model is then tipped forward so it lies along the flight.
            Part(PrimitiveType.Capsule, _model, new Vector3(0f, 0.1f, 0f), new Vector3(0.5f, 0.42f, 0.38f), Suit);          // body
            Part(PrimitiveType.Cylinder, _model, new Vector3(0f, -0.12f, 0f), new Vector3(0.52f, 0.04f, 0.4f), Sun);         // belt
            Part(PrimitiveType.Sphere, _model, new Vector3(0f, 0.22f, 0.19f), new Vector3(0.2f, 0.2f, 0.05f), Sun);          // chest badge
            var head = Part(PrimitiveType.Sphere, _model, new Vector3(0f, 0.68f, 0f), Vector3.one * 0.44f, Skin);
            Part(PrimitiveType.Sphere, head, new Vector3(0f, 0.18f, -0.08f), new Vector3(1.04f, 0.75f, 1.0f), Hair);         // hair
            Part(PrimitiveType.Cube, head, new Vector3(0f, 0.05f, 0.38f), new Vector3(0.95f, 0.2f, 0.25f), Suit);            // mask
            Part(PrimitiveType.Sphere, head, new Vector3(-0.17f, 0.05f, 0.47f), new Vector3(0.12f, 0.12f, 0.06f), Color.white);
            Part(PrimitiveType.Sphere, head, new Vector3(0.17f, 0.05f, 0.47f), new Vector3(0.12f, 0.12f, 0.06f), Color.white);

            _legs = Pivot("Legs", _model, new Vector3(0f, -0.25f, 0f));
            Part(PrimitiveType.Capsule, _legs, new Vector3(-0.11f, -0.3f, 0f), new Vector3(0.17f, 0.28f, 0.17f), Suit);
            Part(PrimitiveType.Capsule, _legs, new Vector3(0.11f, -0.3f, 0f), new Vector3(0.17f, 0.28f, 0.17f), Suit);
            Part(PrimitiveType.Sphere, _legs, new Vector3(-0.11f, -0.6f, 0.03f), new Vector3(0.2f, 0.16f, 0.24f), Boot);
            Part(PrimitiveType.Sphere, _legs, new Vector3(0.11f, -0.6f, 0.03f), new Vector3(0.2f, 0.16f, 0.24f), Boot);

            // arms pivot at the shoulders; each ends in a big fist
            _armL = Pivot("Arm L", _model, new Vector3(-0.3f, 0.32f, 0f));
            Part(PrimitiveType.Capsule, _armL, new Vector3(0f, -0.2f, 0f), new Vector3(0.15f, 0.22f, 0.15f), Suit);
            _fistL = Part(PrimitiveType.Sphere, _armL, new Vector3(0f, -0.44f, 0f), Vector3.one * 0.19f, Skin);
            _armR = Pivot("Arm R", _model, new Vector3(0.3f, 0.32f, 0f));
            Part(PrimitiveType.Capsule, _armR, new Vector3(0f, -0.2f, 0f), new Vector3(0.15f, 0.22f, 0.15f), Suit);
            _fistR = Part(PrimitiveType.Sphere, _armR, new Vector3(0f, -0.44f, 0f), Vector3.one * 0.19f, Skin);

            // the cape hangs from the shoulders down the back (toward the camera once lying forward)
            _cape = Pivot("Cape", _model, new Vector3(0f, 0.38f, -0.2f));
            Part(PrimitiveType.Cube, _cape, new Vector3(0f, -0.48f, -0.02f), new Vector3(0.62f, 0.95f, 0.03f), Cape);
            Part(PrimitiveType.Cube, _cape, new Vector3(0f, -0.95f, -0.02f), new Vector3(0.74f, 0.08f, 0.03f), Cape * 0.85f + Color.black * 0.15f);
            Part(PrimitiveType.Sphere, _cape, new Vector3(0f, -0.4f, -0.05f), new Vector3(0.22f, 0.22f, 0.02f), Sun);    // emblem

            _renderers = renderers.ToArray();
            _colors = colors.ToArray();
            _lastPosition = transform.position;
            ApplyColors();
        }

        void ApplyColors()
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                _block.SetColor("_BaseColor", Color.Lerp(_colors[i], _flashColor, _flash * 0.8f));
                _renderers[i].SetPropertyBlock(_block);
            }
        }
    }
}
