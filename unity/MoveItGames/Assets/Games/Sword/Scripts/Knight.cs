using UnityEngine;
using UnityEngine.Rendering;

namespace MoveIt.Games.Sword
{
    public enum KnightState
    {
        /// <summary>Walking up to the player.</summary>
        Approach,
        /// <summary>Shield up in front: swings bounce off it.</summary>
        Guard,
        /// <summary>Shield lowered for a moment: open.</summary>
        Opening,
        /// <summary>Sword raised, about to strike ("!"): the player must block.</summary>
        WindUp,
        /// <summary>The blow.</summary>
        Strike,
        /// <summary>After a blow that landed: off balance for a moment, open.</summary>
        Recover,
        /// <summary>After a blow the player blocked: dizzy and open (the counterattack).</summary>
        Staggered,
        Defeated,
        /// <summary>It won: jumps for joy.</summary>
        Cheer,
    }

    /// <summary>
    /// A toy knight in cardboard armour with a wooden sword and a round shield: a chunky, friendly figure built from
    /// simple shapes (Blender art can replace it later). <see cref="SwordGame"/> decides what it does; this only animates
    /// the state it is put in, plus two quick reactions on top: <see cref="Recoil"/> (hit) and <see cref="ShieldJolt"/>
    /// (a swing bounced off its shield). The knight is turned to face the player: its local +z points at the player,
    /// +x is its right (the player's left). Its root is at its feet.
    /// </summary>
    public sealed class Knight : MonoBehaviour
    {
        public KnightState State { get; private set; }
        public float StateTime { get; private set; }
        public int Hp { get; set; }
        public int MaxHp { get; set; }
        /// <summary>Winding up / striking: where the blow comes from.</summary>
        public AttackFrom Attack { get; private set; }
        public float WindUpSeconds { get; private set; } = 1.2f;
        /// <summary>Open to hits (shield away from its body).</summary>
        public bool Open => State is KnightState.Opening or KnightState.Recover or KnightState.Staggered;
        /// <summary>Where swings hit it (the middle of its body) and how big that is (metres).</summary>
        public Vector3 HitCenter => transform.TransformPoint(0f, 0.95f, 0f);
        /// <summary>Generous for children: about the knight's whole upper body and head.</summary>
        public float HitRadius => 0.6f * transform.lossyScale.x;
        public Vector3 HeadTop => transform.TransformPoint(0f, 1.62f, 0f);
        public Vector3 ShieldWorld => _shield.position;

        Vector3 _from, _home;
        float _walkSeconds, _recoil, _jolt;
        Transform _model, _shield, _swordArm, _head, _legL, _legR;
        Transform[] _stars;
        Renderer[] _renderers;
        Color[] _colors;
        MaterialPropertyBlock _block;
        float _flash;
        Color _flashColor = Color.white;

        /// <summary>Builds a knight that walks from <paramref name="from"/> to <paramref name="home"/> in <paramref name="walkSeconds"/>.</summary>
        public static Knight Create(Material toon, Material star, Color armour, Vector3 from, Vector3 home, float walkSeconds,
                                    float size = 1f)
        {
            var go = new GameObject("Knight");
            go.transform.localScale = Vector3.one * size;
            var k = go.AddComponent<Knight>();
            k._from = from;
            k._home = home;
            k._walkSeconds = walkSeconds;
            go.transform.SetPositionAndRotation(from, Quaternion.Euler(0f, 180f, 0f));
            k.Build(toon, star, armour);
            k.Enter(KnightState.Approach);
            return k;
        }

        public void Enter(KnightState state)
        {
            State = state;
            StateTime = 0f;
        }

        public void BeginWindUp(AttackFrom from, float seconds)
        {
            Attack = from;
            WindUpSeconds = seconds;
            Enter(KnightState.WindUp);
        }

        /// <summary>Hit: it rocks back (on top of whatever it is doing).</summary>
        public void Recoil()
        {
            _recoil = 1f;
            Flash(Color.white);
        }

        /// <summary>A swing bounced off its shield: the shield jolts.</summary>
        public void ShieldJolt()
        {
            _jolt = 1f;
            Flash(new Color(0.75f, 0.8f, 0.95f));
        }

        /// <summary>A colour flash: white (hit), gold (its blow was blocked).</summary>
        public void Flash(Color color)
        {
            _flashColor = color;
            _flash = 1f;
        }

        public bool Arrived => State != KnightState.Approach;

        void Update()
        {
            float dt = Time.deltaTime;
            StateTime += dt;
            _flash = Mathf.Max(0f, _flash - dt * 4f);
            _recoil = Mathf.Max(0f, _recoil - dt * 3f);
            _jolt = Mathf.Max(0f, _jolt - dt * 5f);
            Animate(dt);
            ApplyColors();
        }

        // ---------------------------------------------------------------- animation

        // Shield poses (local): in front of the body (guarding), at its side (attacking), low (open).
        static readonly Vector3 ShieldFront = new Vector3(-0.05f, 1.0f, 0.42f), ShieldSide = new Vector3(-0.46f, 0.95f, 0.18f),
                                ShieldLow = new Vector3(-0.46f, 0.55f, 0.12f);
        static readonly Vector3 RestArm = new Vector3(0.38f, 0.95f, 0.15f);
        static readonly Vector3 RestSword = new Vector3(0.3f, 0.75f, 0.6f);    // up and toward the player

        void Animate(float dt)
        {
            float t = StateTime;
            var bodyPos = Vector3.zero;
            var bodyRot = Quaternion.identity;
            float legs = 0f;
            Vector3 shieldPos = ShieldFront;
            var shieldRot = Quaternion.identity;
            Vector3 armPos = RestArm, swordDir = RestSword;
            bool snap = false;

            switch (State)
            {
                case KnightState.Approach:
                {
                    float k = Mathf.Clamp01(t / _walkSeconds);
                    transform.position = Vector3.Lerp(_from, _home, 1f - (1f - k) * (1f - k));
                    float step = Mathf.Sin(t * 11f);
                    bodyPos.y = Mathf.Abs(step) * 0.06f;
                    bodyRot = Quaternion.Euler(0f, 0f, step * 5f);
                    legs = step;
                    shieldPos = ShieldSide;
                    if (k >= 1f) Enter(KnightState.Guard);
                    break;
                }
                case KnightState.Guard:
                    transform.position = _home;
                    bodyPos.y = Mathf.Sin(t * 3f) * 0.02f;
                    bodyRot = Quaternion.Euler(0f, Mathf.Sin(t * 1.7f) * 5f, 0f);
                    break;
                case KnightState.Opening:
                    // the shield drops, it stretches and yawns: an opening
                    transform.position = _home;
                    shieldPos = ShieldLow;
                    shieldRot = Quaternion.Euler(0f, 0f, 25f);
                    bodyRot = Quaternion.Euler(-8f * Mathf.Sin(Mathf.Clamp01(t / 0.4f) * Mathf.PI * 0.5f), 0f, Mathf.Sin(t * 4f) * 4f);
                    swordDir = new Vector3(0.6f, -0.3f, 0.5f);
                    break;
                case KnightState.WindUp:
                {
                    transform.position = _home;
                    float shake = Mathf.Sin(t * 45f) * Mathf.Clamp01(t / WindUpSeconds) * 3f;
                    bodyRot = Quaternion.Euler(-6f, 0f, shake);
                    float k = Mathf.SmoothStep(0f, 1f, t / 0.3f);
                    armPos = Vector3.Lerp(RestArm, WindUpArm(Attack), k);
                    swordDir = Vector3.Slerp(RestSword, WindUpSword(Attack), k);
                    shieldPos = ShieldSide;
                    snap = true;
                    break;
                }
                case KnightState.Strike:
                {
                    float k = Mathf.SmoothStep(0f, 1f, t / 0.18f);
                    transform.position = Vector3.Lerp(_home, _home + Vector3.back * 0.35f, Mathf.Sin(Mathf.Clamp01(t / 0.4f) * Mathf.PI));
                    armPos = Vector3.Lerp(WindUpArm(Attack), new Vector3(0.1f, 1.0f, 0.4f), k);
                    swordDir = Vector3.Slerp(WindUpSword(Attack), StrikeSword(Attack), k);
                    bodyRot = Quaternion.Euler(12f * k, 0f, 0f);
                    shieldPos = ShieldSide;
                    snap = true;
                    break;
                }
                case KnightState.Recover:
                    // overreached after its blow: bent forward, sword down, shield out of the way
                    transform.position = _home + Vector3.back * 0.15f;
                    bodyRot = Quaternion.Euler(14f, 0f, Mathf.Sin(t * 6f) * 3f);
                    armPos = new Vector3(0.2f, 0.85f, 0.35f);
                    swordDir = StrikeSword(Attack) + Vector3.down * 0.6f;
                    shieldPos = ShieldLow;
                    break;
                case KnightState.Staggered:
                    transform.position = _home;
                    bodyRot = Quaternion.Euler(Mathf.Sin(t * 5f) * 8f, 0f, Mathf.Sin(t * 3.3f) * 14f);
                    swordDir = new Vector3(0.5f, -0.6f, 0.4f);
                    shieldPos = ShieldLow;
                    shieldRot = Quaternion.Euler(0f, 0f, 35f);
                    break;
                case KnightState.Defeated:
                {
                    float k = Mathf.Clamp01(t / 0.9f);
                    transform.position = _home + new Vector3(0f, Mathf.Sin(k * Mathf.PI) * 1.2f, 2.5f * k);
                    bodyRot = Quaternion.Euler(-540f * k, 0f, 200f * k);
                    _model.localScale = Vector3.one * (1f - k * k);
                    break;
                }
                case KnightState.Cheer:
                    transform.position = _home;
                    bodyPos.y = Mathf.Abs(Mathf.Sin(t * 7f)) * 0.25f;
                    armPos = new Vector3(0.32f, 1.45f, 0.05f);
                    swordDir = new Vector3(0.15f, 1f, 0.1f);
                    shieldPos = ShieldSide;
                    break;
            }

            // reactions on top: hit = rock back; a swing off the shield = the shield shoves toward the knight
            bodyRot = Quaternion.Euler(-22f * Mathf.Sin(_recoil * Mathf.PI), 0f, 0f) * bodyRot;
            bodyPos += new Vector3(0f, 0f, -0.18f * _recoil);
            shieldPos += new Vector3(0f, 0f, -0.12f * _jolt);
            shieldRot = Quaternion.Euler(-14f * _jolt, 0f, 0f) * shieldRot;

            _model.localPosition = bodyPos;
            _model.localRotation = bodyRot;
            _legL.localRotation = Quaternion.Euler(legs * 25f, 0f, 0f);
            _legR.localRotation = Quaternion.Euler(-legs * 25f, 0f, 0f);
            float ease = 1f - Mathf.Exp(-14f * dt);
            _shield.localPosition = Vector3.Lerp(_shield.localPosition, shieldPos, ease);
            _shield.localRotation = Quaternion.Slerp(_shield.localRotation, shieldRot, ease);
            var armRot = Quaternion.FromToRotation(Vector3.up, swordDir.normalized);
            _swordArm.localPosition = snap ? armPos : Vector3.Lerp(_swordArm.localPosition, armPos, ease);
            _swordArm.localRotation = snap ? armRot : Quaternion.Slerp(_swordArm.localRotation, armRot, ease);

            bool dizzy = State == KnightState.Staggered;
            for (int i = 0; i < _stars.Length; i++)
            {
                _stars[i].gameObject.SetActive(dizzy);
                if (!dizzy) continue;
                float a = t * 5f + i * Mathf.PI * 2f / _stars.Length;
                _stars[i].localPosition = new Vector3(Mathf.Cos(a) * 0.32f, 1.78f + Mathf.Sin(a * 2f) * 0.04f, Mathf.Sin(a) * 0.32f);
                if (Camera.main != null) _stars[i].rotation = Camera.main.transform.rotation;
            }
        }

        // Its right arm is on the player's left: a blow "from the left" is swung from its right side.
        static Vector3 WindUpArm(AttackFrom g) => g switch
        {
            AttackFrom.Overhead => new Vector3(0.2f, 1.6f, 0f),
            AttackFrom.Left => new Vector3(0.55f, 1.12f, 0f),
            _ => new Vector3(-0.15f, 1.15f, 0.15f),
        };

        /// <summary>Raised: straight up and cocked back, or pulled out to its right (the player's left), or across.</summary>
        static Vector3 WindUpSword(AttackFrom g) => g switch
        {
            AttackFrom.Overhead => new Vector3(0f, 0.85f, -0.5f),
            AttackFrom.Left => new Vector3(0.85f, 0.45f, -0.2f),
            _ => new Vector3(-0.75f, 0.55f, -0.2f),
        };

        /// <summary>The blow ends pointing at the player (chopping down, or swept across).</summary>
        static Vector3 StrikeSword(AttackFrom g) => g switch
        {
            AttackFrom.Overhead => new Vector3(0f, -0.2f, 1f),
            AttackFrom.Left => new Vector3(-0.4f, 0.15f, 0.9f),
            _ => new Vector3(0.4f, 0.15f, 0.9f),
        };

        // ---------------------------------------------------------------- building

        static readonly Color Skin = new Color(0.98f, 0.8f, 0.66f);
        static readonly Color Metal = new Color(0.78f, 0.82f, 0.92f);
        static readonly Color Wood = new Color(0.72f, 0.5f, 0.3f);
        static readonly Color Dark = new Color(0.12f, 0.12f, 0.2f);
        static readonly Color Sun = new Color(1f, 0.79f, 0.24f);
        static readonly Color Coral = new Color(1f, 0.35f, 0.31f);

        void Build(Material toon, Material starMaterial, Color armour)
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
                r.shadowCastingMode = ShadowCastingMode.On;
                renderers.Add(r);
                colors.Add(color);
                return go.transform;
            }

            // legs and boots (they swing while walking)
            _legL = new GameObject("Leg L").transform;
            _legL.SetParent(_model, false);
            _legL.localPosition = new Vector3(-0.13f, 0.38f, 0f);
            Part(PrimitiveType.Capsule, _legL, new Vector3(0f, -0.18f, 0f), new Vector3(0.16f, 0.2f, 0.16f), Dark);
            Part(PrimitiveType.Sphere, _legL, new Vector3(0f, -0.36f, 0.04f), new Vector3(0.2f, 0.12f, 0.26f), Wood);
            _legR = new GameObject("Leg R").transform;
            _legR.SetParent(_model, false);
            _legR.localPosition = new Vector3(0.13f, 0.38f, 0f);
            Part(PrimitiveType.Capsule, _legR, new Vector3(0f, -0.18f, 0f), new Vector3(0.16f, 0.2f, 0.16f), Dark);
            Part(PrimitiveType.Sphere, _legR, new Vector3(0f, -0.36f, 0.04f), new Vector3(0.2f, 0.12f, 0.26f), Wood);

            // a round cardboard-armour body, belt, big head with a helmet and a plume
            Part(PrimitiveType.Sphere, _model, new Vector3(0f, 0.72f, 0f), new Vector3(0.62f, 0.68f, 0.5f), armour);
            Part(PrimitiveType.Cylinder, _model, new Vector3(0f, 0.55f, 0f), new Vector3(0.6f, 0.04f, 0.5f), Dark);
            Part(PrimitiveType.Sphere, _model, new Vector3(0f, 0.86f, 0.24f), new Vector3(0.22f, 0.22f, 0.06f), Sun);   // badge
            _head = Part(PrimitiveType.Sphere, _model, new Vector3(0f, 1.27f, 0f), Vector3.one * 0.44f, Skin);
            Part(PrimitiveType.Sphere, _head, new Vector3(-0.17f, -0.02f, 0.44f), new Vector3(0.11f, 0.15f, 0.1f), Dark);   // eyes
            Part(PrimitiveType.Sphere, _head, new Vector3(0.17f, -0.02f, 0.44f), new Vector3(0.11f, 0.15f, 0.1f), Dark);
            Part(PrimitiveType.Sphere, _head, new Vector3(-0.29f, -0.15f, 0.36f), new Vector3(0.14f, 0.08f, 0.06f), new Color(1f, 0.6f, 0.6f));  // cheeks
            Part(PrimitiveType.Sphere, _head, new Vector3(0.29f, -0.15f, 0.36f), new Vector3(0.14f, 0.08f, 0.06f), new Color(1f, 0.6f, 0.6f));
            Part(PrimitiveType.Sphere, _head, new Vector3(0f, 0.3f, 0f), new Vector3(1.06f, 0.6f, 1.06f), Metal);           // helmet dome
            Part(PrimitiveType.Cylinder, _head, new Vector3(0f, 0.1f, 0f), new Vector3(1.12f, 0.06f, 1.12f), Metal);         // helmet rim
            Part(PrimitiveType.Capsule, _head, new Vector3(0f, 0.68f, -0.1f), new Vector3(0.18f, 0.32f, 0.5f), Coral, new Vector3(-20f, 0f, 0f));  // plume

            // a round shield on its left arm (the player's right), with a rim and a sun emblem, facing the player
            _shield = new GameObject("Shield").transform;
            _shield.SetParent(_model, false);
            Part(PrimitiveType.Cylinder, _shield, Vector3.zero, new Vector3(0.62f, 0.025f, 0.62f), Metal, new Vector3(-90f, 0f, 0f));
            Part(PrimitiveType.Cylinder, _shield, new Vector3(0f, 0f, 0.015f), new Vector3(0.55f, 0.02f, 0.55f), armour * 0.8f + Color.white * 0.2f, new Vector3(-90f, 0f, 0f));
            Part(PrimitiveType.Cylinder, _shield, new Vector3(0f, 0f, 0.03f), new Vector3(0.26f, 0.02f, 0.26f), Sun, new Vector3(-90f, 0f, 0f));
            Part(PrimitiveType.Sphere, _shield, new Vector3(0f, 0f, -0.04f), Vector3.one * 0.12f, Skin);                    // its hand
            _shield.localPosition = ShieldSide;

            // the sword arm pivots at its right shoulder (the player's left): hand, wooden blade, crossguard
            _swordArm = new GameObject("Sword Arm").transform;
            _swordArm.SetParent(_model, false);
            Part(PrimitiveType.Sphere, _swordArm, Vector3.zero, Vector3.one * 0.13f, Skin);
            Part(PrimitiveType.Cube, _swordArm, new Vector3(0f, 0.08f, 0f), new Vector3(0.2f, 0.04f, 0.06f), Dark);
            Part(PrimitiveType.Cube, _swordArm, new Vector3(0f, 0.43f, 0f), new Vector3(0.08f, 0.66f, 0.03f), Wood);
            _swordArm.localPosition = RestArm;
            _swordArm.localRotation = Quaternion.FromToRotation(Vector3.up, RestSword.normalized);

            // dizzy stars (shown while staggered)
            _stars = new Transform[3];
            for (int i = 0; i < _stars.Length; i++)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(quad.GetComponent<Collider>());
                quad.transform.SetParent(transform, false);
                quad.transform.localScale = Vector3.one * 0.2f;
                var r = quad.GetComponent<Renderer>();
                r.sharedMaterial = starMaterial;
                r.shadowCastingMode = ShadowCastingMode.Off;
                quad.SetActive(false);
                _stars[i] = quad.transform;
            }

            _renderers = renderers.ToArray();
            _colors = colors.ToArray();
            ApplyColors();
        }

        void ApplyColors()
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                _block.SetColor("_BaseColor", Color.Lerp(_colors[i], _flashColor, _flash * 0.85f));
                _renderers[i].SetPropertyBlock(_block);
            }
        }
    }
}
