using UnityEngine;
using UnityEngine.Rendering;

namespace MoveIt.Games.Space
{
    /// <summary>
    /// An astronaut on the screen: the chibi in its space suit and jetpack (art/chibi), a glass helmet, two flames under
    /// the jetpack. Seen from behind, it is the player: its arms reach where the player's do (each hand's place round the
    /// body, from the camera, eased), it steps aside and ducks with them; in flight it leans a little forward, its legs
    /// trailing; on a pad it stands, cheers after a landing. The face follows what happens (<see cref="Face"/>).
    /// </summary>
    public sealed class AstronautView : MonoBehaviour
    {
        Chibi _chibi;
        Transform _body;
        readonly Transform[] _flames = new Transform[2];
        Transform _glass;
        MeshRenderer _glassRenderer;
        MaterialPropertyBlock _helmet;
        readonly Vector3[] _arm = { new Vector3(-0.28f, -1f, 0.05f), new Vector3(0.28f, -1f, 0.05f) };
        float _tilt, _roll, _tuck, _flame = 0.4f, _cheer, _lean, _tumble;
        Chibi.Face _face = Chibi.Face.Smile;
        float _faceUntil;

        /// <summary>The chibi's hips are this far above its feet: the view's origin is at the hips (the path's line), the
        /// body turns round them.</summary>
        const float Hips = 0.7f;

        public Chibi Chibi => _chibi;
        public bool Low
        {
            get => _chibi != null && _chibi.Low;
            set { if (_chibi != null) _chibi.Low = value; }
        }

        /// <summary>Where the mittens are now (world), for their glow.</summary>
        public Vector3 Hand(int side) => _chibi != null ? _chibi.HandCenter(side) : transform.position;

        /// <summary>How far the body leans forward while flying (degrees): more as it goes faster.</summary>
        public float FlyTilt = 14f;

        public static AstronautView Create(string name, ChibiLook look, Material glass, Material flame, Mesh dome, Mesh flameMesh, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<AstronautView>();
            v.Build(look, glass, flame, dome, flameMesh);
            return v;
        }

        void Build(ChibiLook look, Material glass, Material flame, Mesh dome, Mesh flameMesh)
        {
            _body = new GameObject("Body").transform;
            _body.SetParent(transform, false);
            _chibi = Chibi.Create("Chibi", look, _body);
            _chibi.transform.localPosition = new Vector3(0f, -Hips, 0f);
            _chibi.transform.localRotation = Quaternion.identity;
            var head = Bone("Head");
            var spine = Bone("Spine");
            // a cool rim of light all round: the astronaut stands out against the sky
            _chibi.Rim = new Color(0.62f, 0.74f, 1f, 0.6f);
            // the glass helmet round the head (big enough for the hair), on the head's bone
            _glass = new GameObject("Helmet glass").transform;
            _glass.SetParent(_chibi.transform, false);
            _glass.localPosition = new Vector3(0f, 1.21f, 0.02f);
            _glass.localScale = new Vector3(0.46f, 0.45f, 0.44f);
            _glass.gameObject.AddComponent<MeshFilter>().sharedMesh = dome;
            var gr = _glass.gameObject.AddComponent<MeshRenderer>();
            gr.sharedMaterial = glass;
            gr.shadowCastingMode = ShadowCastingMode.Off;
            gr.receiveShadows = false;
            _glassRenderer = gr;
            PaintHelmet(look);
            if (head != null) _glass.SetParent(head, true);
            // the flames under the jetpack's nozzles (art/chibi: x 0.092 either side, 0.255 behind, 0.47 up)
            for (int s = 0; s < 2; s++)
            {
                var f = new GameObject(s == 0 ? "Flame L" : "Flame R").transform;
                f.SetParent(_chibi.transform, false);
                f.localPosition = new Vector3(s == 0 ? -0.092f : 0.092f, 0.46f, -0.255f);
                f.localRotation = Quaternion.identity;
                f.gameObject.AddComponent<MeshFilter>().sharedMesh = flameMesh;
                var r = f.gameObject.AddComponent<MeshRenderer>();
                r.sharedMaterial = flame;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                if (spine != null) f.SetParent(spine, true);
                _flames[s] = f;
            }
        }

        /// <summary>A bone of the chibi by name (not a piece's mesh object of the same name: those are gone after the merge).</summary>
        Transform Bone(string name)
        {
            foreach (var t in _chibi.GetComponentsInChildren<Transform>(true))
                if (t.name == name && t.GetComponent<Renderer>() == null && t.childCount > 0) return t;
            return null;
        }

        public void Recolour(ChibiLook look)
        {
            if (_chibi != null) _chibi.Recolour(look);
            PaintHelmet(look);
        }

        void PaintHelmet(ChibiLook look)
        {
            if (_glassRenderer == null) return;
            _helmet ??= new MaterialPropertyBlock();
            _helmet.SetColor("_ShellColor", look.Color("top"));
            _helmet.SetColor("_RimColor", look.Color("top_accent"));
            _glassRenderer.SetPropertyBlock(_helmet);
        }

        /// <summary>A face for a while (then back to a smile).</summary>
        public void Face(Chibi.Face face, float seconds = 1.2f)
        {
            _face = face;
            _faceUntil = Time.time + seconds;
            _chibi?.SetFace(face);
        }

        public void Flash(Color colour, float amount = 0.8f) => _chibi?.Flash(colour, amount);

        /// <summary>Cheering for a moment (both arms up, a hop of joy): after a landing.</summary>
        public void Cheer(float seconds = 2f)
        {
            _cheer = seconds;
            Face(Chibi.Face.Happy, seconds);
        }

        /// <summary>A meteor or comet hit: a quick tumble.</summary>
        public void Tumble()
        {
            _tumble = 1f;
            Face(Chibi.Face.Sad, 1f);
        }

        /// <summary>
        /// Every frame: the astronaut at <paramref name="position"/> in the path's <paramref name="frame"/> (forward along
        /// it). <paramref name="left"/> and <paramref name="right"/> are the player's palms round their shoulders' middle
        /// (torso lengths, x to the right, y up; null = not seen: the arm hangs); <paramref name="low"/> (0..1) ducks;
        /// <paramref name="sideways"/> (m/s across) leans into a step; <paramref name="thrust"/> sizes the flames.
        /// </summary>
        public void Pose(Vector3 position, Quaternion frame, bool flying, Vector2? left, Vector2? right, float low, float sideways, float thrust, float dt)
        {
            if (_chibi == null) return;
            if (Time.time > _faceUntil && _face != Chibi.Face.Smile)
            {
                _face = Chibi.Face.Smile;
                _chibi.SetFace(Chibi.Face.Smile);
            }
            _cheer = Mathf.Max(0f, _cheer - dt);
            _tumble = Mathf.Max(0f, _tumble - dt * 1.6f);
            bool cheering = _cheer > 0f && !flying;
            float k = 1f - Mathf.Exp(-10f * dt);
            _tilt = Mathf.Lerp(_tilt, flying ? FlyTilt + 26f * low : 0f, 1f - Mathf.Exp(-6f * dt));
            _lean = Mathf.Lerp(_lean, flying ? Mathf.Clamp(sideways * 5f, -22f, 22f) : 0f, 1f - Mathf.Exp(-8f * dt));
            _tuck = Mathf.Lerp(_tuck, low, k);
            transform.SetPositionAndRotation(position, frame);
            float hop = cheering ? Mathf.Abs(Mathf.Sin(_cheer * 7f)) * 0.25f : 0f;
            _body.localPosition = new Vector3(0f, hop - 0.42f * _tuck, 0f);
            float tumble = _tumble > 0f ? Mathf.Sin(_tumble * Mathf.PI) * 360f * _tumble : 0f;
            _body.localRotation = Quaternion.Euler(0f, 0f, -_lean - tumble * 0.15f) * Quaternion.Euler(_tilt + tumble * 0.2f, 0f, 0f);
            _chibi.ResetPose();
            // the arms: where the player's are (from their shoulder on that side), a little ahead of the body
            Vector3 l, r;
            if (cheering)
            {
                float wave = Mathf.Sin(Time.time * 10f) * 0.25f;
                l = new Vector3(-0.45f + wave, 1f, 0.1f);
                r = new Vector3(0.45f - wave, 1f, 0.1f);
            }
            else
            {
                l = Arm(left, -1f);
                r = Arm(right, 1f);
            }
            float ka = 1f - Mathf.Exp(-22f * dt);
            _arm[0] = Vector3.Slerp(_arm[0].normalized, l.normalized, ka);
            _arm[1] = Vector3.Slerp(_arm[1].normalized, r.normalized, ka);
            // the body leans forward: aim in the body's own frame
            var counter = Quaternion.Inverse(Quaternion.Euler(_tilt, 0f, 0f));
            _chibi.AimArm(0, counter * _arm[0]);
            _chibi.AimArm(1, counter * _arm[1]);
            // the legs: trailing and kicking a little in flight, tucked when ducking, standing on a pad
            if (flying)
            {
                float kick = Mathf.Sin(Time.time * 6.5f) * 8f;
                for (int s = 0; s < 2; s++)
                {
                    if (_tuck > 0.05f) _chibi.BendLeg(s, 70f * _tuck, 100f * _tuck);
                    else _chibi.SwingLeg(s, -10f + (s == 0 ? kick : -kick));
                }
            }
            _chibi.TurnHead(0f, -_tilt * 0.6f);
            // the flames
            _flame = Mathf.Lerp(_flame, flying ? Mathf.Max(0.25f, thrust) : 0.08f, 1f - Mathf.Exp(-8f * dt));
            float flicker = 1f + 0.12f * Mathf.Sin(Time.time * 43f) + 0.08f * Mathf.Sin(Time.time * 71f);
            for (int s = 0; s < 2; s++)
            {
                if (_flames[s] == null) continue;
                float len = 0.55f * _flame * flicker;
                float wide = 0.8f + 0.5f * Mathf.Clamp01(_flame - 0.5f);
                _flames[s].localScale = new Vector3(wide, Mathf.Max(0.01f, len), wide);
            }
        }

        /// <summary>The arm's direction from the player's palm (round their shoulders' middle): from their shoulder on that
        /// side (0.4 torso lengths out), a little ahead of the body (seen from behind, the hands stay in sight).</summary>
        static Vector3 Arm(Vector2? palm, float side)
        {
            if (palm == null) return new Vector3(0.3f * side, -1f, 0.05f);
            var d = palm.Value - new Vector2(0.4f * side, 0f);
            if (d.sqrMagnitude < 1e-4f) d = new Vector2(0f, -1f);
            var dir = new Vector3(d.x, d.y, 0f).normalized;
            return (dir + new Vector3(0f, 0f, 0.35f)).normalized;
        }
    }
}
