using UnityEngine;
using UnityEngine.Rendering;

namespace MoveIt.Games.Fairy
{
    /// <summary>How the fairy casts a spell (<see cref="FairyView.Cast"/>): both arms thrown up (the lightning, the freeze,
    /// the healing rain), one arm flung toward a side (the rainbow), both spread wide (the shield), both pushed down to the
    /// ground (the seeds), a spin in light (the big magic).</summary>
    public enum CastMove { Up, Out, Wide, Down, Twirl }

    /// <summary>
    /// A fairy on the screen (2026-11-15): the tall fairy figure (art/fairy/heroine.py, drawn and posed by <see cref="Chibi"/>)
    /// in her dress (<see cref="FairyLooks"/>) with four big glowing wings on her back (<see cref="FairyArt.WingPanel"/>,
    /// Tundu/Wing: a gradient, a bright rim, veins and glitter) that flap all the time, faster when she flies or casts.
    /// Since 2026-11-16 (the user: "dont make the wand tied up to the character ... the character should do some
    /// animation when the spell is cast, and should move left or right, no mechanic, just for more immersion") she is
    /// her own: she hovers facing the camera, flies from spot to spot in front of the tree (the game chooses them),
    /// leaning into it, her eyes on the player's wand; while the player follows a shape she gathers its light between her
    /// hands (<see cref="Hover"/>'s charge), and when it's cast she throws it (<see cref="Cast"/>). She cheers when a
    /// wave is saved, spins for the transformation, is sad when the tree is hit.
    /// </summary>
    public sealed class FairyView : MonoBehaviour
    {
        Chibi _chibi;
        Transform _body;
        readonly Transform[] _wings = new Transform[4];
        readonly MeshRenderer[] _wingRenderers = new MeshRenderer[4];
        MaterialPropertyBlock _block;
        readonly Vector3[] _arm = { new Vector3(-0.3f, -1f, 0.1f), new Vector3(0.3f, -1f, 0.1f) };
        float _tilt, _lean, _flap, _flapSpeed = 1f, _cheer, _tumble, _spin, _faceUntil, _turn;
        Vector2 _look;
        Chibi.Face _face = Chibi.Face.Smile;
        int _wingStyle = -1;
        // the cast: how far through (0..1, -1 none), which move, toward which side of the screen
        float _castT = -1f;
        CastMove _castMove;
        int _castSide;

        /// <summary>Her middle is this far above her feet: the view's origin, round which she leans and tumbles.</summary>
        const float Hips = 0.95f;
        public const float Scale = 1.1f;
        /// <summary>The wings drawn this much bigger than art's panel: big, like the cartoon fairies' (each ~0.65 m).</summary>
        public const float WingScale = 1.45f;
        /// <summary>Seconds a cast takes: the gathering, the throw held, back to hovering.</summary>
        public const float CastSeconds = 0.85f;

        public Chibi Chibi => _chibi;
        /// <summary>She is casting (the game holds her still meanwhile).</summary>
        public bool Casting => _castT >= 0f;

        /// <summary>Where a hand is now (world): spells start there.</summary>
        public Vector3 Hand(int side) => _chibi != null ? _chibi.HandCenter(side) : transform.position;
        /// <summary>Between her hands (the light she gathers while a shape is followed).</summary>
        public Vector3 Palms => (Hand(0) + Hand(1)) * 0.5f;

        public static FairyView Create(string name, ChibiLook look, int wings, Material wingMaterial, Mesh[] wingMeshes, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<FairyView>();
            v.Build(look, wings, wingMaterial, wingMeshes);
            return v;
        }

        void Build(ChibiLook look, int wings, Material wingMaterial, Mesh[] wingMeshes)
        {
            _block = new MaterialPropertyBlock();
            _body = new GameObject("Body").transform;
            _body.SetParent(transform, false);
            _body.localScale = Vector3.one * Scale;
            _chibi = Chibi.Create("Chibi", look, _body);
            _chibi.transform.localPosition = new Vector3(0f, -Hips, 0f);
            _chibi.Rim = new Color(1f, 0.85f, 0.95f, 0.55f);
            var spine = Bone("Spine");
            // the wings: hinged between her shoulder blades, two each side, drawn two-sided; long hair falls down her
            // back, so with it they grow from behind the hair
            float behind = look.Hair == "Wavy" ? -0.17f : -0.10f;
            for (int i = 0; i < 4; i++)
            {
                var w = new GameObject(i < 2 ? "Wing upper" : "Wing lower").transform;
                w.SetParent(_chibi.transform, false);
                int side = i % 2 == 0 ? 1 : -1;
                w.localPosition = new Vector3(side * 0.035f, i < 2 ? 1.29f : 1.22f, behind);
                w.localScale = new Vector3(side, 1f, 1f);
                w.gameObject.AddComponent<MeshFilter>();
                var r = w.gameObject.AddComponent<MeshRenderer>();
                r.sharedMaterial = wingMaterial;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                _wingRenderers[i] = r;
                if (spine != null) w.SetParent(spine, true);
                _wings[i] = w;
            }
            SetWings(wings, wingMeshes);
        }

        Transform Bone(string name)
        {
            foreach (var t in _chibi.GetComponentsInChildren<Transform>(true))
                if (t.name == name && t.GetComponent<Renderer>() == null && t.childCount > 0) return t;
            return null;
        }

        /// <summary>Wears wings <paramref name="style"/> (<see cref="FairyLooks.AllWings"/>); <paramref name="meshes"/> are
        /// every style's upper and lower panels (16).</summary>
        public void SetWings(int style, Mesh[] meshes)
        {
            style = Mathf.Clamp(style, 0, FairyLooks.AllWings.Length - 1);
            if (style == _wingStyle || meshes == null) return;
            _wingStyle = style;
            var w = FairyLooks.AllWings[style];
            for (int i = 0; i < 4; i++)
            {
                if (_wings[i] == null) continue;
                _wings[i].GetComponent<MeshFilter>().sharedMesh = meshes[style * 2 + (i < 2 ? 0 : 1)];
                _block.Clear();
                _block.SetColor("_RootColor", w.Root);
                _block.SetColor("_EdgeColor", w.Edge);
                _block.SetColor("_RimColor", w.Rim);
                _block.SetFloat("_Glitter", w.Glitter);
                _block.SetFloat("_Rainbow", style == 7 ? 1f : 0f);
                _wingRenderers[i].SetPropertyBlock(_block);
                float size = w.Size;
                int side = i % 2 == 0 ? 1 : -1;
                _wings[i].localScale = new Vector3(side * size, size, size) * WingScale;
            }
        }

        public void Recolour(ChibiLook look) => _chibi?.Recolour(look);

        public void Face(Chibi.Face face, float seconds = 1.2f)
        {
            _face = face;
            _faceUntil = Time.time + seconds;
            _chibi?.SetFace(face);
        }

        public void Flash(Color colour, float amount = 0.8f) => _chibi?.Flash(colour, amount);
        public void Cheer(float seconds = 2.5f)
        {
            _cheer = seconds;
            Face(Chibi.Face.Happy, seconds);
        }

        public void Tumble()
        {
            _tumble = 1f;
            Face(Chibi.Face.Sad, 1.2f);
            Flash(new Color(0.55f, 0.35f, 0.75f), 0.7f);
        }

        /// <summary>The transformation: a spin in light.</summary>
        public void Spin()
        {
            _spin = 1f;
            Face(Chibi.Face.Grin, 2f);
        }

        /// <summary>She casts a spell: <paramref name="move"/>, toward <paramref name="screenSide"/> (-1 the screen's left,
        /// +1 its right) for a spell that strikes a side.</summary>
        public void Cast(CastMove move, int screenSide = 0)
        {
            _castMove = move;
            _castSide = screenSide;
            _castT = 0f;
            _cheer = 0f;
            if (move == CastMove.Twirl) _spin = 1f;
            Face(Chibi.Face.Grin, 1.4f);
        }

        /// <summary>
        /// Every frame: her hips at <paramref name="position"/>, facing the camera, turned a little the way she flies and
        /// leaning into it (<paramref name="velocity"/>, m/s), beating her wings faster; <paramref name="charge"/> (0..1, 0 =
        /// none): her hands together in front of her, gathering the light of the shape the player follows, rising as it
        /// fills; <paramref name="look"/>: where her head turns (degrees: yaw toward her right, pitch down), the player's
        /// wand. <paramref name="flying"/> false: upright, at ease (the waiting screen).
        /// </summary>
        public void Hover(Vector3 position, Vector3 velocity, float charge, Vector2 look, float dt, bool flying = true)
        {
            if (_chibi == null) return;
            if (Time.time > _faceUntil && _face != Chibi.Face.Smile && charge <= 0f)
            {
                _face = Chibi.Face.Smile;
                _chibi.SetFace(Chibi.Face.Smile);
            }
            else if (charge > 0f && _face == Chibi.Face.Smile) Face(Chibi.Face.Grin, 0.3f);
            _cheer = Mathf.Max(0f, _cheer - dt);
            _tumble = Mathf.Max(0f, _tumble - dt * 1.4f);
            _spin = Mathf.Max(0f, _spin - dt * 0.8f);
            if (_castT >= 0f)
            {
                _castT += dt / CastSeconds;
                if (_castT >= 1f) _castT = -1f;
            }
            float casting = CastWeight();
            // a side's spell turns her toward that side as she throws it (facing the camera, the screen's left is her right)
            float turn = _castSide * casting * (_castMove == CastMove.Out ? 0.75f : _castMove == CastMove.Up ? 0.25f : 0f);
            _turn = Mathf.Lerp(_turn, turn, 1f - Mathf.Exp(-10f * dt));
            float across = Mathf.Clamp(velocity.x / 3f, -1f, 1f);
            var frame = Quaternion.LookRotation(new Vector3(0.45f * across + _turn, 0f, -1f).normalized, Vector3.up);
            float want = flying ? 6f + Mathf.Clamp(-velocity.y, -3f, 3f) : 3f;
            _tilt = Mathf.Lerp(_tilt, want, 1f - Mathf.Exp(-5f * dt));
            // facing the camera her right is the screen's left: the lean follows her flight across the screen
            _lean = Mathf.Lerp(_lean, Mathf.Clamp(-velocity.x * 4.5f, -26f, 26f), 1f - Mathf.Exp(-7f * dt));
            _look = Vector2.Lerp(_look, new Vector2(Mathf.Clamp(look.x, -40f, 40f), Mathf.Clamp(look.y, -25f, 25f)), 1f - Mathf.Exp(-6f * dt));
            transform.SetPositionAndRotation(position, frame);
            // a little hop as she throws a spell up
            float hop = _castMove == CastMove.Up && _castT >= 0f ? 0.16f * Mathf.Sin(Mathf.Clamp01((_castT - 0.12f) / 0.75f) * Mathf.PI) : 0f;
            float bob = Mathf.Sin(Time.time * 2.2f) * 0.06f;
            _body.localPosition = new Vector3(0f, bob + hop, 0f);
            float tumble = _tumble > 0f ? Mathf.Sin(_tumble * Mathf.PI) * 300f * _tumble : 0f;
            float spin = _spin > 0f ? (1f - _spin) * 720f : 0f;
            _body.localRotation = Quaternion.Euler(0f, spin, -_lean - tumble * 0.12f) * Quaternion.Euler(_tilt + tumble * 0.1f, 0f, 0f);
            _chibi.ResetPose();
            Arms(charge, out var l, out var r);
            float ka = 1f - Mathf.Exp(-(_castT >= 0f ? 26f : 14f) * dt);
            _arm[0] = Vector3.Slerp(_arm[0].normalized, l.normalized, ka);
            _arm[1] = Vector3.Slerp(_arm[1].normalized, r.normalized, ka);
            var counter = Quaternion.Inverse(Quaternion.Euler(_tilt, 0f, 0f));
            _chibi.AimArm(0, counter * _arm[0]);
            _chibi.AimArm(1, counter * _arm[1]);
            // flying: legs long and together, toes pointed, one knee bent with its foot tucked in behind the other leg (a
            // cartoon fairy's flight); at ease on the waiting screen, toes still pointed down
            float sway = Mathf.Sin(Time.time * 1.7f) * 5f;
            if (flying)
            {
                _chibi.FlyLeg(0, -12f + sway, 8f, 1f, 55f);
                _chibi.FlyLeg(1, -2f - sway, 72f, -8f, 50f);
            }
            else
            {
                _chibi.FlyLeg(0, 0f, 4f, 0f, 35f);
                _chibi.FlyLeg(1, 6f, 24f + sway, -4f, 42f);
            }
            // her eyes on the wand (on the light she gathers while charging; ahead while she throws)
            var head = charge > 0f ? new Vector2(_look.x * 0.4f, 12f) : casting > 0f ? Vector2.zero : _look;
            _chibi.TurnHead(-_lean * 0.3f + head.x, -_tilt * 0.5f + head.y);
            // the wings: always beating, faster when she flies, gathers or casts
            float effort = Mathf.Clamp01(Mathf.Abs(velocity.x) * 0.15f + Mathf.Abs(velocity.y) * 0.15f + charge * 0.6f + casting + (_spin > 0f ? 1f : 0f));
            _flapSpeed = Mathf.Lerp(_flapSpeed, 1f + 1.6f * effort, 1f - Mathf.Exp(-4f * dt));
            _flap += dt * _flapSpeed * 7.5f;
            float beat = Mathf.Sin(_flap);
            for (int i = 0; i < 4; i++)
            {
                if (_wings[i] == null) continue;
                bool upper = i < 2;
                int side = i % 2 == 0 ? 1 : -1;
                float sweep = (upper ? 28f : 38f) + beat * (upper ? 26f : 20f);
                float lift = upper ? 18f + beat * 6f : -20f - beat * 5f;
                _wings[i].localRotation = Quaternion.Euler(0f, -side * sweep, side * lift);
            }
        }

        /// <summary>0..1: how much of the cast's throw is on (the gathering before it and the way back after it fade).</summary>
        float CastWeight()
        {
            if (_castT < 0f) return 0f;
            if (_castT < 0.18f) return _castT / 0.18f * 0.5f;
            return _castT < 0.7f ? 1f : 1f - (_castT - 0.7f) / 0.3f;
        }

        /// <summary>Where her arms point (her frame: +X her right, +Y up, +Z ahead, toward the camera; 0 = her left arm).</summary>
        void Arms(float charge, out Vector3 l, out Vector3 r)
        {
            float t = Time.time;
            if (_castT >= 0f && _castT < 0.7f)
            {
                if (_castT < 0.18f)
                {
                    // gathering: the hands come together at her chest
                    l = new Vector3(0.38f, 0.15f, 1f);
                    r = new Vector3(-0.38f, 0.15f, 1f);
                    return;
                }
                switch (_castMove)
                {
                    case CastMove.Up:
                    case CastMove.Twirl:
                        l = new Vector3(-0.25f, 1f, 0.25f);
                        r = new Vector3(0.25f, 1f, 0.25f);
                        return;
                    case CastMove.Out:
                        // the arm on that side flung out (the screen's left is her right), the other back for balance
                        if (_castSide < 0)
                        {
                            r = new Vector3(1f, 0.35f, 0.45f);
                            l = new Vector3(-0.45f, -0.7f, -0.1f);
                        }
                        else
                        {
                            l = new Vector3(-1f, 0.35f, 0.45f);
                            r = new Vector3(0.45f, -0.7f, -0.1f);
                        }
                        return;
                    case CastMove.Wide:
                        // spreading the bubble: wide, up and reaching forward (level, they looked like a T-pose)
                        l = new Vector3(-0.85f, 0.5f, 0.6f);
                        r = new Vector3(0.85f, 0.5f, 0.6f);
                        return;
                    default:
                        l = new Vector3(-0.5f, -0.75f, 0.7f);
                        r = new Vector3(0.5f, -0.75f, 0.7f);
                        return;
                }
            }
            if (_spin > 0f)
            {
                l = new Vector3(-0.3f, 1f, 0f);
                r = new Vector3(0.3f, 1f, 0f);
                return;
            }
            if (_cheer > 0f)
            {
                float wave = Mathf.Sin(t * 9f) * 0.25f;
                l = new Vector3(-0.45f + wave, 1f, 0.1f);
                r = new Vector3(0.45f - wave, 1f, 0.1f);
                return;
            }
            if (charge > 0f)
            {
                // the hands together in front of her, gathering the light, rising as it fills
                float up = -0.2f + 0.45f * Mathf.Clamp01(charge) + 0.05f * Mathf.Sin(t * 11f);
                l = new Vector3(0.42f, up, 1f);
                r = new Vector3(-0.42f, up, 1f);
                return;
            }
            // hovering: the arms loose at her sides, drifting with the wing beats
            l = new Vector3(-0.5f, -0.82f + 0.08f * Mathf.Sin(t * 2.1f), 0.3f);
            r = new Vector3(0.5f, -0.82f + 0.08f * Mathf.Sin(t * 2.1f + 1f), 0.3f);
        }
    }
}
