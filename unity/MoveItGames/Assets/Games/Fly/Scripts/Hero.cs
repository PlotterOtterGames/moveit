using UnityEngine;

namespace MoveIt.Games.Fly
{
    /// <summary>
    /// The flying hero, seen from behind: a chunky kid in a suit and a big cape, lying forward in the air like the
    /// classic superhero, fists ready by the chest. Built from simple shapes merged into five meshes (body, legs, two arms,
    /// the cape: five draw calls) with vertex colours (Tundu/Toon). <see cref="FlyGame"/> moves it; this animates:
    /// banking into side steps and turns, the cape billowing in the wind, a fist shooting forward on <see cref="Punch"/>,
    /// a flash and a wobble on <see cref="Hurt"/>, a loop on <see cref="Cheer"/>. Its local +z points ahead (away from
    /// the camera), +x is its right (the screen's right). The root is the middle of its body.
    /// </summary>
    public sealed class Hero : MonoBehaviour
    {
        /// <summary>The outfits a player unlocks with stars (<see cref="FlyProgress.OutfitStars"/>): suit and cape colours.
        /// The first is the hero's own (grape suit, a deeper coral cape that reads against pastel walls); the last has a
        /// rainbow cape.</summary>
        public static readonly (Color suit, Color cape)[] Outfits =
        {
            (new Color(0.48f, 0.38f, 1f), new Color(0.96f, 0.22f, 0.24f)),     // classic
            (new Color(0.19f, 0.62f, 0.94f), new Color(1f, 0.74f, 0.18f)),     // sky
            (new Color(0.36f, 0.74f, 0.36f), new Color(0.56f, 0.34f, 0.92f)),  // leaf
            (new Color(1f, 0.74f, 0.16f), new Color(0.18f, 0.56f, 0.96f)),     // sun
            (new Color(0.96f, 0.36f, 0.3f), new Color(1f, 0.83f, 0.28f)),      // fire
            (new Color(0.17f, 0.19f, 0.44f), new Color(0.98f, 0.3f, 0.36f)),   // night
            (new Color(0.98f, 0.56f, 0.76f), new Color(0.97f, 0.97f, 1f)),     // pink
            (new Color(0.95f, 0.95f, 1f), Color.white),                        // rainbow (the cape's rows)
        };
        static readonly Color[] Rainbow =
        {
            new Color(1f, 0.3f, 0.3f), new Color(1f, 0.6f, 0.2f), new Color(1f, 0.86f, 0.24f), new Color(0.4f, 0.84f, 0.4f),
            new Color(0.25f, 0.65f, 1f), new Color(0.5f, 0.38f, 1f),
        };
        static readonly Color HurtFlash = new Color(0.96f, 0.22f, 0.24f);
        static readonly Color Sun = new Color(1f, 0.79f, 0.24f);
        static readonly Color Skin = new Color(0.98f, 0.8f, 0.66f);
        static readonly Color Hair = new Color(0.25f, 0.17f, 0.12f);
        static readonly Color Boot = new Color(0.12f, 0.12f, 0.22f);
        /// <summary>The model is drawn this far (m) below the hero's place in the rules, so a robot hovering right in
        /// front shows above the hero's head from the camera behind (the reach and pick-up radii are 1-1.35 m: unseen).</summary>
        const float ModelDrop = 0.25f;
        const int CapeColumns = 6, CapeRows = 9;
        const float CapeTop = 0.46f, CapeBottom = 0.74f, CapeLength = 1f;

        Transform _model, _cape, _armL, _armR, _fistL, _fistR, _legs;
        Renderer[] _renderers;
        MaterialPropertyBlock _block;
        float _punchL = -1f, _punchR = -1f, _hurt, _flash, _cheer = -1f, _bank, _pitch, _t;
        bool _flashShown;
        Color _flashColor = Color.white;
        Vector3 _lastPosition;
        Mesh _capeMesh;
        Vector3[] _capeVertices;
        Color Suit, SuitDark, Cape, CapeInside;
        bool _rainbow;

        /// <summary>Which of <see cref="Outfits"/> it wears.</summary>
        public int Outfit { get; private set; }

        /// <summary>The turn the route is making (+1 right, -1 left, 0 none): the hero banks into it.</summary>
        public float Turn { get; set; }

        /// <summary>Where the punching fist ends up (world), for sparks.</summary>
        public Vector3 FistWorld(bool right) => (right ? _fistR : _fistL).position;

        public static Hero Create(Material actor, int outfit = 0)
        {
            var go = new GameObject("Hero");
            var hero = go.AddComponent<Hero>();
            hero.Outfit = Mathf.Clamp(outfit, 0, Outfits.Length - 1);
            var (suit, cape) = Outfits[hero.Outfit];
            hero.Suit = suit;
            hero.SuitDark = suit * 0.78f;
            hero.Cape = cape;
            hero.CapeInside = cape * 0.66f;
            hero._rainbow = hero.Outfit == Outfits.Length - 1;
            hero.Build(actor);
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
            Flash(HurtFlash, 0.6f);
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

            // bank and pitch with the movement (the root is moved by the game) and into the route's turns
            var velocity = dt > 0f ? (transform.position - _lastPosition) / dt : Vector3.zero;
            _lastPosition = transform.position;
            float ease = 1f - Mathf.Exp(-8f * dt);
            _bank = Mathf.Lerp(_bank, Mathf.Clamp(-velocity.x * 9f - Turn * 38f, -50f, 50f), ease);
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
            _model.localPosition = new Vector3(0f, Mathf.Sin(_t * 2.2f) * 0.06f - ModelDrop, 0f);
            _legs.localRotation = Quaternion.Euler(Mathf.Sin(_t * 3f) * 5f, 0f, 0f);

            AnimateArm(_armL, ref _punchL, -1f, dt);
            AnimateArm(_armR, ref _punchR, 1f, dt);
            AnimateCape(roll);
            ApplyFlash();
        }

        // An arm pivots at its shoulder: by the chest (ready), or straight ahead (punching). In the model's frame
        // (lying forward), "ahead" of the hero is the model's +y (its head's direction).
        static void AnimateArm(Transform arm, ref float punch, float side, float dt)
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

        /// <summary>The cape trails back from the shoulders and ripples, more toward its hem; it swings out of a bank.</summary>
        void AnimateCape(float roll)
        {
            int n = CapeColumns * CapeRows;
            for (int r = 0; r < CapeRows; r++)
            {
                float s = r / (CapeRows - 1f);
                float width = Mathf.Lerp(CapeTop, CapeBottom, s);
                for (int c = 0; c < CapeColumns; c++)
                {
                    float u = c / (CapeColumns - 1f) - 0.5f;
                    // waves run down the cape; its edges curl forward a little, like cloth caught by the wind
                    float wave = Mathf.Sin(_t * 9f - s * 5f + u * 2.2f) * 0.11f * s + Mathf.Sin(_t * 14f - s * 8f) * 0.035f * s;
                    var p = new Vector3(u * width + Mathf.Sin(_t * 5f + s * 3f) * 0.04f * s - roll * 0.004f * s,
                                        -s * CapeLength,
                                        -(0.1f + 0.3f * s) * s + wave + u * u * 0.5f * s);
                    _capeVertices[r * CapeColumns + c] = p;
                    _capeVertices[n + r * CapeColumns + c] = p;
                }
            }
            _capeMesh.vertices = _capeVertices;
            _capeMesh.RecalculateNormals();
            _capeMesh.RecalculateBounds();
        }

        void ApplyFlash()
        {
            if (_flash <= 0f && !_flashShown) return;
            _flashShown = _flash > 0f;
            _block.SetColor("_FlashColor", new Color(_flashColor.r, _flashColor.g, _flashColor.b, _flash * 0.8f));
            foreach (var r in _renderers) r.SetPropertyBlock(_block);
        }

        void Build(Material actor)
        {
            _block = new MaterialPropertyBlock();
            _model = new GameObject("Model").transform;
            _model.SetParent(transform, false);

            Transform Pivot(string name, Transform parent, Vector3 pos)
            {
                var t = new GameObject(name).transform;
                t.SetParent(parent, false);
                t.localPosition = pos;
                return t;
            }

            // Built standing up (head at +y, facing +z); the model is then tipped forward so it lies along the flight.
            var head = Matrix4x4.TRS(new Vector3(0f, 0.68f, 0f), Quaternion.identity, Vector3.one * 0.44f);
            var body = new PartMesh()
                .Add(PrimitiveType.Capsule, new Vector3(0f, 0.1f, 0f), new Vector3(0.5f, 0.42f, 0.38f), Suit, 0.35f)
                .Add(PrimitiveType.Cylinder, new Vector3(0f, -0.12f, 0f), new Vector3(0.52f, 0.04f, 0.4f), Sun, 0.9f)
                .Add(PrimitiveType.Sphere, new Vector3(0f, 0.22f, 0.19f), new Vector3(0.2f, 0.2f, 0.05f), Sun, 0.9f)
                .Add(PrimitiveType.Sphere, head, Skin, 0.15f)
                .Add(PrimitiveType.Sphere, head * Matrix4x4.TRS(new Vector3(0f, 0.18f, -0.08f), Quaternion.identity, new Vector3(1.04f, 0.75f, 1f)), Hair, 0.25f)
                .Add(PrimitiveType.Cube, head * Matrix4x4.TRS(new Vector3(0f, 0.05f, 0.38f), Quaternion.identity, new Vector3(0.95f, 0.2f, 0.25f)), Suit, 0.4f)
                .Add(PrimitiveType.Sphere, head * Matrix4x4.TRS(new Vector3(-0.17f, 0.05f, 0.47f), Quaternion.identity, new Vector3(0.12f, 0.12f, 0.06f)), Color.white, 0.6f)
                .Add(PrimitiveType.Sphere, head * Matrix4x4.TRS(new Vector3(0.17f, 0.05f, 0.47f), Quaternion.identity, new Vector3(0.12f, 0.12f, 0.06f)), Color.white, 0.6f)
                // shoulder pads so the cape has something to hang from
                .Add(PrimitiveType.Sphere, new Vector3(-0.24f, 0.33f, -0.04f), new Vector3(0.2f, 0.16f, 0.2f), SuitDark, 0.35f)
                .Add(PrimitiveType.Sphere, new Vector3(0.24f, 0.33f, -0.04f), new Vector3(0.2f, 0.16f, 0.2f), SuitDark, 0.35f)
                .Build("Hero body");

            _legs = Pivot("Legs", _model, new Vector3(0f, -0.25f, 0f));
            var legs = new PartMesh()
                .Add(PrimitiveType.Capsule, new Vector3(-0.11f, -0.3f, 0f), new Vector3(0.17f, 0.28f, 0.17f), Suit, 0.35f)
                .Add(PrimitiveType.Capsule, new Vector3(0.11f, -0.3f, 0f), new Vector3(0.17f, 0.28f, 0.17f), Suit, 0.35f)
                .Add(PrimitiveType.Sphere, new Vector3(-0.11f, -0.6f, 0.03f), new Vector3(0.2f, 0.16f, 0.24f), Boot, 0.8f)
                .Add(PrimitiveType.Sphere, new Vector3(0.11f, -0.6f, 0.03f), new Vector3(0.2f, 0.16f, 0.24f), Boot, 0.8f)
                .Build("Hero legs");

            // arms pivot at the shoulders; each ends in a big fist
            Mesh Arm(string name) => new PartMesh()
                .Add(PrimitiveType.Capsule, new Vector3(0f, -0.2f, 0f), new Vector3(0.15f, 0.22f, 0.15f), Suit, 0.35f)
                .Add(PrimitiveType.Cylinder, new Vector3(0f, -0.34f, 0f), new Vector3(0.17f, 0.03f, 0.17f), Sun, 0.9f)   // glove cuff
                .Add(PrimitiveType.Sphere, new Vector3(0f, -0.44f, 0f), Vector3.one * 0.19f, Skin, 0.15f)
                .Build(name);
            _armL = Pivot("Arm L", _model, new Vector3(-0.3f, 0.32f, 0f));
            _armR = Pivot("Arm R", _model, new Vector3(0.3f, 0.32f, 0f));
            _fistL = Pivot("Fist L", _armL, new Vector3(0f, -0.44f, 0f));
            _fistR = Pivot("Fist R", _armR, new Vector3(0f, -0.44f, 0f));

            // the cape hangs from the shoulders down the back (toward the camera once lying forward)
            _cape = Pivot("Cape", _model, new Vector3(0f, 0.36f, -0.2f));
            _capeMesh = CapeMesh();

            _renderers = new Renderer[]
            {
                PartMesh.Show("Body", _model, body, actor),
                PartMesh.Show("Legs", _legs, legs, actor),
                PartMesh.Show("Arm", _armL, Arm("Hero arm L"), actor),
                PartMesh.Show("Arm", _armR, Arm("Hero arm R"), actor),
                PartMesh.Show("Cape", _cape, _capeMesh, actor),
            };
            _lastPosition = transform.position;
            AnimateCape(0f);
        }

        /// <summary>A grid of vertices for each side of the cape: outside coral fading darker to a gold hem, inside darker.</summary>
        Mesh CapeMesh()
        {
            int n = CapeColumns * CapeRows;
            _capeVertices = new Vector3[n * 2];
            var colors = new Color[n * 2];
            var tris = new int[(CapeColumns - 1) * (CapeRows - 1) * 12];
            for (int r = 0; r < CapeRows; r++)
            for (int c = 0; c < CapeColumns; c++)
            {
                float s = r / (CapeRows - 1f);
                bool hem = r == CapeRows - 1;
                var cape = _rainbow ? Rainbow[Mathf.Min(r * Rainbow.Length / (CapeRows - 1), Rainbow.Length - 1)] : Cape;
                var outside = hem ? Sun : Color.Lerp(cape, cape * 0.82f, s);
                colors[r * CapeColumns + c] = new Color(outside.r, outside.g, outside.b, hem ? 0.9f : 0.25f);
                colors[n + r * CapeColumns + c] = new Color(CapeInside.r, CapeInside.g, CapeInside.b, 0.2f);
            }
            int t = 0;
            for (int r = 0; r < CapeRows - 1; r++)
            for (int c = 0; c < CapeColumns - 1; c++)
            {
                int a = r * CapeColumns + c, b = a + 1, d = a + CapeColumns, e = d + 1;
                // outside faces -z of the cape's frame (the hero's back), inside the other way
                tris[t++] = a; tris[t++] = b; tris[t++] = e;
                tris[t++] = a; tris[t++] = e; tris[t++] = d;
                tris[t++] = n + a; tris[t++] = n + e; tris[t++] = n + b;
                tris[t++] = n + a; tris[t++] = n + d; tris[t++] = n + e;
            }
            var mesh = new Mesh { name = "Hero cape" };
            mesh.MarkDynamic();
            mesh.vertices = _capeVertices;
            mesh.colors = colors;
            mesh.triangles = tris;
            return mesh;
        }

        void OnDestroy()
        {
            if (_renderers == null) return;
            foreach (var r in _renderers)
                if (r != null) Destroy(r.GetComponent<MeshFilter>().sharedMesh);
        }
    }
}
