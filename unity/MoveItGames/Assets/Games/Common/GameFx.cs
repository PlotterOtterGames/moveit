using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MoveIt.Games
{
    /// <summary>
    /// Little effects shared by the activity games (Aventura!, Sporte!): sparks and puffs (Tundu/Fx quads facing the
    /// camera), confetti and debris cubes (Tundu/Toon), words that pop up over the world and float away, and a camera
    /// shake. Pieces are pooled (no garbage while playing). Call <see cref="DrawPopups"/> from OnGUI after
    /// <see cref="GameHud.Begin"/>.
    /// </summary>
    public sealed class GameFx : MonoBehaviour
    {
        sealed class Bit
        {
            public Transform T;
            public Renderer R;
            public Vector3 Velocity, Scale, Spin;
            public float Age, Life, Grow, Drag;
            public bool Cube, Gravity, Flat;
        }

        sealed class Word
        {
            public Vector3 World;
            public string Text;
            public Color Color;
            public float Age, Life;
            public bool Big;
        }

        Camera _cam;
        Material _spark, _dot, _toon;
        readonly List<Bit> _live = new List<Bit>();
        readonly Stack<Bit> _quads = new Stack<Bit>(), _cubes = new Stack<Bit>();
        readonly List<Word> _popups = new List<Word>();
        MaterialPropertyBlock _block;
        float _shake;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        /// <summary>How far the camera should be pushed this frame by the shake (add it to the camera's position).</summary>
        public Vector3 ShakeOffset { get; private set; }

        public static GameFx Create(Camera cam, Material spark, Material dot, Material toon)
        {
            var fx = new GameObject("Effects").AddComponent<GameFx>();
            fx._cam = cam;
            fx._spark = spark;
            fx._dot = dot;
            fx._toon = toon;
            fx._block = new MaterialPropertyBlock();
            return fx;
        }

        public Camera Camera
        {
            get => _cam;
            set => _cam = value;
        }

        /// <summary>A burst of star sparks flying out and slowing down.</summary>
        public void Sparks(Vector3 at, int count, Color color, float speed = 3.5f, float size = 0.16f)
        {
            for (int i = 0; i < count; i++)
            {
                var b = Take(false, _spark);
                b.T.position = at;
                b.Scale = Vector3.one * (size * Random.Range(0.7f, 1.3f));
                b.Life = Random.Range(0.25f, 0.5f);
                b.Velocity = Random.onUnitSphere * (speed * Random.Range(0.6f, 1.3f));
                b.Spin = new Vector3(0f, 0f, Random.Range(-720f, 720f));
                b.Drag = 4f;
                Tint(b, color);
            }
        }

        /// <summary>A soft puff that grows and fades (splashes, dust, smoke). Flat: lying on the ground.</summary>
        public void Puff(Vector3 at, Color color, float size = 0.6f, bool flat = false, float life = 0.5f, Vector3 drift = default)
        {
            var b = Take(false, _dot);
            b.T.position = at;
            if (flat) b.T.rotation = Quaternion.Euler(90f, 0f, 0f);
            b.Flat = flat;
            b.Scale = Vector3.one * size;
            b.Life = life;
            b.Grow = 1.8f;
            b.Velocity = drift;
            b.Drag = 1.5f;
            Tint(b, color);
        }

        /// <summary>Paper confetti (or debris cubes: <paramref name="chunky"/>) thrown up and falling.</summary>
        public void Confetti(Vector3 at, int count, Color[] colors, bool chunky = false, float spread = 2.5f)
        {
            for (int i = 0; i < count; i++)
            {
                var b = Take(true, _toon);
                b.T.position = at + Random.insideUnitSphere * 0.25f;
                b.T.rotation = Random.rotation;
                b.Scale = chunky ? Vector3.one * Random.Range(0.08f, 0.18f)
                                 : new Vector3(Random.Range(0.06f, 0.12f), Random.Range(0.06f, 0.12f), 0.015f);
                b.Life = Random.Range(0.9f, 1.5f);
                b.Gravity = true;
                b.Velocity = new Vector3(Random.Range(-spread, spread), Random.Range(2f, 5f), Random.Range(-spread, spread) * 0.6f);
                b.Spin = Random.insideUnitSphere * 600f;
                Tint(b, colors[Random.Range(0, colors.Length)]);
            }
        }

        /// <summary>Debris from something smashed: cubes in its colour flying out from <paramref name="at"/>.</summary>
        public void Debris(Vector3 at, int count, Color color, float speed = 4f, float size = 0.14f)
        {
            for (int i = 0; i < count; i++)
            {
                var b = Take(true, _toon);
                b.T.position = at + Random.insideUnitSphere * 0.15f;
                b.T.rotation = Random.rotation;
                b.Scale = Vector3.one * (size * Random.Range(0.6f, 1.3f));
                b.Life = Random.Range(0.6f, 1.1f);
                b.Gravity = true;
                b.Velocity = Random.onUnitSphere * speed + Vector3.up * 2f;
                b.Spin = Random.insideUnitSphere * 720f;
                Tint(b, Color.Lerp(color, Color.white, Random.Range(0f, 0.25f)));
            }
        }

        /// <summary>A word over the world that rises and fades (drawn by <see cref="DrawPopups"/>).</summary>
        public void Popup(Vector3 world, string text, Color color, bool big = false, float life = 1.1f) =>
            _popups.Add(new Word { World = world, Text = text, Color = color, Big = big, Life = life });

        public void Shake(float amount) => _shake = Mathf.Max(_shake, amount);

        /// <summary>Removes everything (a new round).</summary>
        public void Clear()
        {
            foreach (var b in _live) Return(b);
            _live.Clear();
            _popups.Clear();
            _shake = 0f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            var camRot = _cam != null ? _cam.transform.rotation : Quaternion.identity;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var b = _live[i];
                b.Age += dt;
                if (b.Age >= b.Life)
                {
                    Return(b);
                    _live.RemoveAt(i);
                    continue;
                }
                float fade = Mathf.Clamp01((b.Life - b.Age) / (b.Life * 0.45f));
                if (b.Gravity) b.Velocity += Vector3.down * (9f * dt);
                if (b.Drag > 0f) b.Velocity *= Mathf.Exp(-b.Drag * dt);
                b.T.position += b.Velocity * dt;
                if (b.Cube) b.T.Rotate(b.Spin * dt, Space.World);
                else if (!b.Flat) b.T.rotation = camRot * Quaternion.Euler(0f, 0f, b.Spin.z * b.Age);
                float grow = 1f + b.Grow * b.Age / b.Life;
                b.T.localScale = b.Scale * grow * (b.Cube ? fade : Mathf.Lerp(0.5f, 1f, fade));
                if (!b.Cube && b.Grow > 0f)
                {
                    var c = b.R.sharedMaterial == _dot ? _colors[b] : Color.white;
                    c.a *= fade;
                    _block.SetColor(BaseColor, c);
                    b.R.SetPropertyBlock(_block);
                }
            }
            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                var p = _popups[i];
                p.Age += dt;
                p.World += Vector3.up * (0.7f * dt);
                if (p.Age > p.Life) _popups.RemoveAt(i);
            }
            _shake = Mathf.Max(0f, _shake - dt);
            ShakeOffset = (Vector3)(Random.insideUnitCircle * (0.25f * _shake));
        }

        readonly Dictionary<Bit, Color> _colors = new Dictionary<Bit, Color>();

        /// <summary>Call from OnGUI (after GameHud.Begin): the pop-up words.</summary>
        public void DrawPopups()
        {
            if (_cam == null) return;
            foreach (var p in _popups)
            {
                var screen = _cam.WorldToScreenPoint(p.World);
                if (screen.z <= 0f) continue;
                var g = GameHud.FromScreen(screen);
                var c = p.Color;
                c.a = Mathf.Clamp01((p.Life - p.Age) / 0.4f);
                float pop = 1f + 0.35f * Mathf.Exp(-p.Age * 12f);
                var style = p.Big ? GameHud.Big : GameHud.Medium;
                var old = GUI.matrix;
                GUIUtility.ScaleAroundPivot(Vector2.one * pop, g * (Screen.height / GameHud.Height));
                GameHud.Text(new Rect(g.x - 400, g.y - 60, 800, 120), p.Text, style, TextAnchor.MiddleCenter, c);
                GUI.matrix = old;
            }
        }

        Bit Take(bool cube, Material material)
        {
            var pool = cube ? _cubes : _quads;
            Bit b;
            if (pool.Count > 0)
            {
                b = pool.Pop();
                b.T.gameObject.SetActive(true);
            }
            else
            {
                var go = new GameObject(cube ? "Bit" : "Spark");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = PartMesh.Primitive(cube ? PrimitiveType.Cube : PrimitiveType.Quad);
                var r = go.AddComponent<MeshRenderer>();
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                b = new Bit { T = go.transform, R = r, Cube = cube };
            }
            b.R.sharedMaterial = material;
            b.Age = 0f;
            b.Grow = b.Drag = 0f;
            b.Gravity = b.Flat = false;
            b.Spin = Vector3.zero;
            b.T.rotation = Quaternion.identity;
            _live.Add(b);
            return b;
        }

        void Tint(Bit b, Color c)
        {
            _colors[b] = c;
            _block.SetColor(BaseColor, c);
            b.R.SetPropertyBlock(_block);
        }

        void Return(Bit b)
        {
            b.T.gameObject.SetActive(false);
            (b.Cube ? _cubes : _quads).Push(b);
        }
    }
}
