using UnityEngine;
using UnityEngine.Rendering;

namespace MoveIt.Games.Boxing
{
    /// <summary>
    /// A boxer on the screen: a chibi (art/chibi, the boxers' pieces) wearing boxing gloves (art/boxing's Glove, on its
    /// hands every frame) and, for a hero or a villain, a cloth cape; posed procedurally like the other games' chibis
    /// (two-bone arms with <see cref="Chibi.ReachArm"/>, the spine, the legs, the face), from what the rules say it's
    /// doing. The opponent follows <see cref="Bout"/>'s state every frame (<see cref="FollowFoe"/>: its guard, the tell of
    /// each punch (the arm drawn back the way it will come, a power punch's big trembling wind-up), the strike, the hit,
    /// covering up, dizzy, the fall); the hero follows the player's body (<see cref="FollowHero"/>: the guard, the lean and
    /// the duck the camera sees) and plays each punch the moment the camera tells it (<see cref="Punch"/>). In the fight
    /// the hero is see-through (<see cref="SeeThrough"/>: Wii Sports Boxing's look from behind). Both face +Z in their own
    /// frame.
    /// </summary>
    public sealed class BoxerView : MonoBehaviour
    {
        public Chibi Chibi { get; private set; }
        public bool IsHero { get; private set; }
        public float Size { get; private set; } = 1f;

        Transform _body;
        readonly Transform[] _gloves = new Transform[2];
        readonly MeshRenderer[] _gloveRenderers = new MeshRenderer[2];
        MaterialPropertyBlock _gloveBlock;
        Vector4[] _gloveTable;
        CapeCloth _cape;

        // the arms' actions: a punch thrown (the hero), an opponent's punch in its phases
        struct ArmAction
        {
            public int Kind;   // 0 none, 1 the hero's punch, 2 a wind-up, 3 a strike
            public Blow Blow;
            public Attack Attack;
            public bool Body;
            public float Start, Duration;
        }

        readonly ArmAction[] _arm = new ArmAction[2];
        /// <summary>Where the opponent's punch goes (world): the hero's head as it aimed; none = straight ahead.</summary>
        public Vector3? StrikeAt;
        float _hurt, _hurtBody, _hurtSide, _flinch, _cheer;
        Vector3 _lean;   // x = side shift, y = drop, z = forward step (all smoothed)
        float _roll, _pitch, _yaw;
        float _fall;     // 0 standing .. 1 lying
        float _time;
        bool _guardHigh = true;
        int _slipSide;
        Chibi.Face _face = Chibi.Face.Smile;

        // ------------------------------------------------------------------ making one

        /// <summary>A boxer in <paramref name="look"/> at <paramref name="size"/>, its gloves (<paramref name="gloveMesh"/>,
        /// coloured by <paramref name="gloveTable"/> with <paramref name="gloveMaterial"/>) and, with a colour, a cape.</summary>
        public static BoxerView Create(string name, Transform parent, ChibiLook look, float size, bool hero, Mesh gloveMesh, Material gloveMaterial,
                                       Vector4[] gloveTable, Color? cape, Material capeMaterial)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            var view = root.gameObject.AddComponent<BoxerView>();
            view.IsHero = hero;
            view.Size = size;
            view._body = new GameObject("Body").transform;
            view._body.SetParent(root, false);
            view.Chibi = Chibi.Create("Chibi", look, view._body);
            if (view.Chibi != null)
            {
                view.Chibi.transform.localScale = Vector3.one * size;
                view.Chibi.VertexLit = true;   // big on the screen: lit per pixel it cost the box 12-15 % of its frame
            }
            view._gloveBlock = new MaterialPropertyBlock();
            view._gloveTable = gloveTable;
            for (int s = 0; s < 2; s++)
            {
                var g = new GameObject(s == 0 ? "Glove L" : "Glove R");
                g.transform.SetParent(root, false);
                g.AddComponent<MeshFilter>().sharedMesh = gloveMesh;
                var r = g.AddComponent<MeshRenderer>();
                r.sharedMaterial = gloveMaterial;
                r.shadowCastingMode = ShadowCastingMode.Off;
                view._gloves[s] = g.transform;
                view._gloveRenderers[s] = r;
            }
            view.SetGloves(gloveTable);
            if (cape.HasValue && capeMaterial != null) view._cape = new CapeCloth(root, capeMaterial, cape.Value, size);
            return view;
        }

        public void SetGloves(Vector4[] table)
        {
            _gloveTable = table;
            _gloveBlock.SetVectorArray("_Regions", table);
            foreach (var r in _gloveRenderers) if (r != null) r.SetPropertyBlock(_gloveBlock);
        }

        /// <summary>The hero drawn see-through (two materials of Tundu/AthleteGhost on its low-detail copy) in a fight, the
        /// gloves solid; solid again for the shots from the front (null materials).</summary>
        public void SeeThrough(Material depth, Material colour, Material cape)
        {
            bool on = depth != null && colour != null;
            if (Chibi != null) Chibi.OverrideMaterials = on ? new[] { depth, colour } : null;
            _cape?.SeeThrough(on ? depth : null, on ? colour : cape);
        }

        public void Flash(Color colour, float amount = 1f)
        {
            Chibi?.Flash(colour, amount);
            _gloveBlock.SetColor("_FlashColor", new Color(colour.r, colour.g, colour.b, amount * 0.6f));
            foreach (var r in _gloveRenderers) if (r != null) r.SetPropertyBlock(_gloveBlock);
            _gloveFlash = amount * 0.6f;
        }

        float _gloveFlash;
        readonly Vector3[] _glove = new Vector3[2];
        bool _gloveStarted;

        /// <summary>Where a glove is (world): effects at the fist.</summary>
        public Vector3 GlovePosition(int side) => _gloves[side] != null ? _gloves[side].position + _gloves[side].forward * 0.13f * Size : transform.position;
        /// <summary>The head's middle (world).</summary>
        public Vector3 HeadPosition => Chibi != null ? Chibi.HeadPosition : transform.TransformPoint(_lean + new Vector3(0f, 1.15f * Size, 0f));
        public Vector3 ChestPosition => transform.TransformPoint(_lean + new Vector3(0f, 0.72f * Size - _lean.y, 0f));

        // ------------------------------------------------------------------ what happens (events)

        /// <summary>The hero throws a punch now (the camera saw it): straight, a hook or an uppercut, at the body or the head.</summary>
        public void Punch(bool right, Blow blow, bool body)
        {
            int s = right ? 1 : 0;
            _arm[s] = new ArmAction { Kind = 1, Blow = blow, Body = body, Start = _time, Duration = blow == Blow.Straight ? 0.32f : 0.4f };
        }

        /// <summary>Hit: the head (or the body) snaps back, from a side (-1 left, 1 right, 0 straight).</summary>
        public void Hurt(bool body, int fromSide, float strength = 1f)
        {
            _hurt = Mathf.Max(_hurt, strength);
            _hurtBody = body ? 1f : 0f;
            _hurtSide = fromSide;
            Flash(new Color(1f, 0.35f, 0.3f), 0.7f * strength);
        }

        /// <summary>A punch into the guard: a small push back.</summary>
        public void Blocked()
        {
            _flinch = 1f;
            Flash(new Color(0.75f, 0.9f, 1f), 0.4f);
        }

        /// <summary>A big happy moment (a win, a knockdown it caused): arms up for a while.</summary>
        public void Cheer(float seconds = 2f) => _cheer = Mathf.Max(_cheer, seconds);

        /// <summary>It has been posed at least once (before that it stands in the model's import pose).</summary>
        public bool Posed { get; private set; }

        // ------------------------------------------------------------------ following the rules

        /// <summary>The opponent's pose from what the rules say it's doing.</summary>
        public void FollowFoe(Bout b, float dt)
        {
            _time += dt;
            float t = b.StateTime, len = Mathf.Max(0.01f, b.StateLength);
            float u = Mathf.Clamp01(t / len);
            var a = b.Attack;
            int arm = ArmFor(a);
            // the boxer's weave while it waits: a little side to side, never still
            var targetLean = new Vector3(Mathf.Sin(_time * 1.3f) * 0.05f, 0f, 0f);
            float roll = Mathf.Sin(_time * 1.3f) * 4f, pitch = 0f, yaw = 0f, fall = 0f, crouch = 0.18f;
            _face = Chibi.Face.Smile;
            _guardHigh = b.Guard != Guard.Low;
            bool guardUp = b.Guard != Guard.Open, cover = false;
            for (int s = 0; s < 2; s++) if (_arm[s].Kind != 1) _arm[s].Kind = 0;
            switch (b.State)
            {
                case Foe.WindUp:
                    _arm[arm] = new ArmAction { Kind = 2, Attack = a.Attack, Start = _time - t, Duration = len };
                    if (a.Attack is Attack.Sweep or Attack.Fireball)
                        _arm[1 - arm] = new ArmAction { Kind = 2, Attack = a.Attack, Start = _time - t, Duration = len };
                    yaw = (a.Side == 0 ? (arm == 1 ? 18f : -18f) : 30f * a.Side) * Ease(u);
                    if (a.Attack == Attack.Uppercut) crouch = 0.18f + 0.4f * Ease(u);
                    if (a.Attack == Attack.Body) { crouch = 0.18f + 0.25f * Ease(u); pitch = 8f * Ease(u); }
                    if (a.Attack is Attack.Haymaker or Attack.Lightning) { yaw *= 1.4f; pitch = -10f * Ease(u); }
                    if (a.Attack == Attack.Sweep) { pitch = -8f * Ease(u); crouch = 0.3f; }
                    targetLean.x = 0f;
                    roll = 0f;
                    _face = a.Special ? Chibi.Face.Grin : Chibi.Face.Smile;
                    break;
                case Foe.Strike:
                case Foe.Impact:
                    _arm[arm] = new ArmAction { Kind = 3, Attack = a.Attack, Start = _time - (b.State == Foe.Strike ? t : len + t), Duration = Mathf.Max(0.05f, b.Level.StrikeTime) };
                    if (a.Attack is Attack.Sweep or Attack.Fireball) _arm[1 - arm] = _arm[arm];
                    targetLean = new Vector3(0f, 0f, 0.3f);
                    roll = 0f;
                    yaw = (a.Side == 0 ? (arm == 1 ? -16f : 16f) : -24f * a.Side);
                    if (a.Attack == Attack.Uppercut) crouch = 0.05f;
                    if (a.Attack == Attack.Body) crouch = 0.35f;
                    _face = Chibi.Face.Grin;
                    break;
                case Foe.Recover:
                    guardUp = false;
                    yaw = 6f * Mathf.Sin(u * Mathf.PI);
                    break;
                case Foe.Staggered:
                    // its punch met nothing: off balance, open (the counter's moment)
                    guardUp = false;
                    targetLean = new Vector3(0.14f * Mathf.Sin(t * 7f), 0f, -0.22f);
                    roll = 16f * Mathf.Sin(t * 5f);
                    pitch = -10f;
                    _face = Chibi.Face.Sad;
                    break;
                case Foe.Hurt:
                    guardUp = false;
                    break;
                case Foe.Dizzy:
                    guardUp = false;
                    roll = 14f * Mathf.Sin(t * 4.2f);
                    pitch = 8f + 5f * Mathf.Sin(t * 3.1f);
                    targetLean = new Vector3(0.1f * Mathf.Sin(t * 4.2f), 0.04f, -0.06f);
                    crouch = 0.35f;
                    _face = Chibi.Face.Happy;   // closed eyes: dizzy
                    break;
                case Foe.Covering:
                    // the shell: both gloves tight in front of the face, elbows in, bent forward
                    cover = true;
                    crouch = 0.42f;
                    pitch = 14f;
                    targetLean = new Vector3(0f, 0f, -0.06f);
                    roll = 0f;
                    _face = Chibi.Face.Sad;
                    break;
                case Foe.Blocking:
                    guardUp = true;
                    targetLean = new Vector3(0f, 0f, -0.06f);
                    break;
                case Foe.Slipping:
                {
                    int side = _slipSide = _slipSide == 0 ? (Random.value < 0.5f ? -1 : 1) : _slipSide;
                    float k = Mathf.Sin(u * Mathf.PI);
                    targetLean = new Vector3(0.34f * side * k, 0f, 0f);
                    roll = 24f * side * k;
                    crouch = 0.18f + 0.22f * k;
                    break;
                }
                case Foe.Taunt:
                    guardUp = false;
                    _face = Chibi.Face.Grin;
                    _cheer = Mathf.Max(_cheer, 0.6f);
                    break;
                case Foe.Throwing:
                    _arm[0] = _arm[1] = new ArmAction { Kind = 3, Attack = Attack.Fireball, Start = _time - t, Duration = 0.15f };
                    targetLean = new Vector3(0f, 0f, 0.12f);
                    break;
                case Foe.Frozen:
                    guardUp = true;
                    _guardHigh = true;
                    targetLean = new Vector3(0f, 0f, -0.1f);
                    _face = Chibi.Face.Sad;
                    break;
                case Foe.Down:
                case Foe.Out:
                    fall = 1f;
                    _face = Chibi.Face.Happy;
                    break;
                case Foe.GettingUp:
                    fall = 1f - Ease(u);
                    _face = Chibi.Face.Sad;
                    break;
                case Foe.Won:
                    _face = Chibi.Face.Happy;
                    _cheer = Mathf.Max(_cheer, 1f);
                    break;
                case Foe.Lost:
                    _face = Chibi.Face.Sad;
                    guardUp = false;
                    pitch = 14f;
                    break;
            }
            if (b.State != Foe.Slipping) _slipSide = 0;
            if (b.Phase == BoutPhase.Down && b.DownFighter == 0) { _face = Chibi.Face.Grin; _cheer = Mathf.Max(_cheer, 0.6f); }
            if (b.Phase == BoutPhase.Rest) { guardUp = false; _face = Chibi.Face.Smile; }
            Compose(targetLean, roll, pitch, yaw, fall, crouch, guardUp, cover, dt);
        }

        /// <summary>The hero's pose: the guard, the lean and the duck the camera sees, and the rules' state (down, the super,
        /// the end).</summary>
        public void FollowHero(Bout b, bool guard, float lean, float duck, float dt)
        {
            _time += dt;
            float fall = 0f, pitch, roll;
            var targetLean = new Vector3(0.34f * lean, 0f, 0f);
            roll = 24f * lean;
            float crouch = 0.15f + 0.65f * duck;
            pitch = 10f * duck;
            _face = Chibi.Face.Smile;
            _guardHigh = true;
            switch (b.Hero)
            {
                case HeroState.Super:
                {
                    float t = b.HeroStateTime;
                    float rise = Mathf.Clamp01(t / Bout.SuperRise), dive = Mathf.Clamp01((t - Bout.SuperRise) / Bout.SuperDive);
                    targetLean = new Vector3(0f, -0.9f * Mathf.Sin(rise * Mathf.PI * 0.5f) * (1f - dive), 0.7f * Ease(dive));
                    pitch = -10f * rise + 40f * dive;
                    // the right fist straight out all the way (held at the punch's full reach)
                    _arm[1] = new ArmAction { Kind = 1, Blow = Blow.Straight, Start = _time - 0.4f, Duration = 1f };
                    _face = Chibi.Face.Grin;
                    break;
                }
                case HeroState.Down:
                    fall = Mathf.Clamp01(b.PhaseTime / 0.5f);
                    _face = Chibi.Face.Happy;
                    break;
                case HeroState.GettingUp:
                    fall = 1f - Ease(Mathf.Clamp01(b.HeroStateTime / Bout.GetUpTime));
                    break;
                case HeroState.Out:
                    fall = 1f;
                    _face = Chibi.Face.Happy;
                    break;
                case HeroState.Won:
                    _cheer = Mathf.Max(_cheer, 1f);
                    _face = Chibi.Face.Happy;
                    break;
                case HeroState.Lost:
                    _face = Chibi.Face.Sad;
                    pitch = 14f;
                    guard = false;
                    break;
            }
            Compose(targetLean, roll, pitch, 0f, fall, crouch, guard, false, dt);
        }

        /// <summary>The arm an opponent's attack comes from (0 = its left): a hook from the screen's left is its right
        /// hand (it faces the camera).</summary>
        static int ArmFor(PlannedAttack a)
        {
            if (a.Side < 0) return 1;
            if (a.Side > 0) return 0;
            return a.Attack switch { Attack.Jab => 0, Attack.Body => 0, _ => 1 };
        }

        static float Ease(float u) => u * u * (3f - 2f * u);
        static float EaseOut(float u) => 1f - (1f - u) * (1f - u);

        // ------------------------------------------------------------------ the pose

        void Compose(Vector3 targetLean, float roll, float pitch, float yaw, float fall, float crouch, bool guardUp, bool cover, float dt)
        {
            if (Chibi == null) return;
            float k = 1f - Mathf.Exp(-14f * dt);
            _lean = Vector3.Lerp(_lean, targetLean, k);
            _roll = Mathf.Lerp(_roll, roll, k);
            _pitch = Mathf.Lerp(_pitch, pitch, k);
            _yaw = Mathf.Lerp(_yaw, yaw, k);
            _fall = Mathf.MoveTowards(_fall, fall, dt * (fall > _fall ? 2.2f : 1.4f));
            _hurt = Mathf.MoveTowards(_hurt, 0f, dt * 3.5f);
            _flinch = Mathf.MoveTowards(_flinch, 0f, dt * 6f);
            _cheer = Mathf.MoveTowards(_cheer, 0f, dt * 0.8f);
            if (_gloveFlash > 0f)
            {
                _gloveFlash = Mathf.MoveTowards(_gloveFlash, 0f, dt * 3f);
                _gloveBlock.SetColor("_FlashColor", new Color(1f, 1f, 1f, _gloveFlash));
                foreach (var r in _gloveRenderers) if (r != null) r.SetPropertyBlock(_gloveBlock);
            }

            Chibi.ResetPose();
            // the boxer's bounce on bent knees
            float bob = Mathf.Sin(_time * 7.5f) * 0.014f * (1f - _fall);
            float knee = Mathf.Lerp(18f, 70f, Mathf.Clamp01(crouch));
            for (int s = 0; s < 2; s++) Chibi.BendLeg(s, knee * 0.55f + (s == 0 ? 6f : -4f), knee);
            float drop = Chibi.LegDrop(knee * 0.55f, knee);
            // the hit: the head and the body snap back (a body blow folds it forward)
            float hurt = Ease(Mathf.Clamp01(_hurt));
            float hurtPitch = _hurtBody > 0.5f ? 18f * hurt : -16f * hurt;
            float hurtRoll = -10f * hurt * _hurtSide;
            float flinch = -6f * _flinch;
            float cheerBounce = _cheer > 0f ? Mathf.Abs(Mathf.Sin(_time * 6f)) * 0.12f * Mathf.Clamp01(_cheer) : 0f;
            var body = _lean + new Vector3(0f, -drop + bob + cheerBounce, -0.08f * hurt - 0.04f * _flinch);
            _body.localPosition = body;
            // falling flat on its back, round its heels
            _body.localRotation = Quaternion.AngleAxis(-82f * EaseOut(_fall), Vector3.right);
            Chibi.TurnSpine(_yaw, _pitch + hurtPitch + flinch, _roll + hurtRoll);
            Chibi.TurnHead(-_yaw * 0.4f + hurtRoll * 0.5f, -_pitch * 0.3f + (_hurtBody > 0.5f ? 0f : -14f * hurt));

            // the arms: the stance's glove places, then each arm's action on top
            for (int s = 0; s < 2; s++)
            {
                var target = cover ? CoverPlace(s) : GlovePlace(s, guardUp);
                var arm = _arm[s];
                float w = 0f;
                Vector3 action = target;
                if (arm.Kind != 0)
                {
                    float u = Mathf.Clamp01((_time - arm.Start) / Mathf.Max(0.01f, arm.Duration));
                    action = ActionPlace(s, arm, u, out w);
                    if (arm.Kind == 1 && u >= 1f) _arm[s].Kind = 0;
                }
                var local = Vector3.Lerp(target, action, w);
                // cheering: both fists pumped beside the big head, out and forward (a chibi's arms don't reach over its
                // head: straight up, or in a V, the fists hid behind it), pumping with the bounce
                if (_cheer > 0.01f && arm.Kind == 0)
                {
                    float pump = Mathf.Abs(Mathf.Sin(_time * 6f + s * 0.9f)) * 0.08f;
                    local = Vector3.Lerp(local, Shoulder(s) + new Vector3(s == 0 ? -0.22f : 0.22f, 0.26f + pump, 0.2f) * Size, Ease(Mathf.Clamp01(_cheer * 1.5f)));
                }
                if (_fall > 0.01f) local = Vector3.Lerp(local, Shoulder(s) + new Vector3(s == 0 ? -0.4f : 0.4f, 0.1f, 0f), _fall);
                // an action's curve moves the glove itself; between them it eases (a punch's end back to the guard, a guard
                // changing) instead of jumping
                bool driven = arm.Kind != 0 && w > 0.5f;
                _glove[s] = !_gloveStarted || driven ? local : Vector3.Lerp(_glove[s], local, 1f - Mathf.Exp(-16f * dt));
                local = _glove[s];
                var world = _body.TransformPoint(local);
                var pole = _body.TransformDirection(new Vector3(s == 0 ? -0.6f : 0.6f, -1f, -0.25f));
                Chibi.ReachArm(s, world, pole);
            }
            _gloveStarted = true;
            Posed = true;
            Chibi.SetFace(_hurt > 0.3f ? Chibi.Face.Sad : _face);
            PlaceGloves();
            _cape?.Step(_body, Chibi, dt);
        }

        /// <summary>A shoulder in the body's frame (at this boxer's size).</summary>
        Vector3 Shoulder(int s) => new Vector3(s == 0 ? -0.2f : 0.2f, 0.86f, 0.02f) * Size;

        /// <summary>Where a glove rests: in front of the chin (the high guard), the belly (low), or loose (open).</summary>
        Vector3 GlovePlace(int s, bool guardUp)
        {
            float inward = s == 0 ? 1f : -1f;
            var sh = Shoulder(s);
            if (!guardUp) return sh + new Vector3(-inward * 0.05f, -0.18f, 0.16f) * Size;
            if (!_guardHigh) return sh + new Vector3(inward * 0.06f, -0.14f, 0.24f) * Size;
            return sh + new Vector3(inward * 0.09f, 0.14f, 0.22f) * Size;
        }

        /// <summary>The shell: both gloves pressed together in front of the face, the forearms covering the body.</summary>
        Vector3 CoverPlace(int s)
        {
            float inward = s == 0 ? 1f : -1f;
            return Shoulder(s) + new Vector3(inward * 0.15f, 0.1f, 0.16f) * Size;
        }

        /// <summary>An arm's action at progress <paramref name="u"/>: where its glove goes and how much of it there is.</summary>
        Vector3 ActionPlace(int s, ArmAction a, float u, out float weight)
        {
            float inward = s == 0 ? 1f : -1f;
            var sh = Shoulder(s);
            weight = 1f;
            switch (a.Kind)
            {
                case 1:   // the hero's punch: out fast, back
                {
                    float outU = Mathf.Clamp01(u / 0.38f), back = Mathf.Clamp01((u - 0.55f) / 0.45f);
                    weight = 1f - Ease(back);
                    Vector3 from, to;
                    switch (a.Blow)
                    {
                        case Blow.Hook:
                            from = sh + new Vector3(-inward * 0.2f, 0.12f, 0.12f) * Size;
                            to = sh + new Vector3(inward * 0.32f, 0.14f, 0.5f) * Size;
                            return Arc(from, to, EaseOut(outU), new Vector3(0f, 0f, 0.14f) * Size);
                        case Blow.Uppercut:
                            from = sh + new Vector3(inward * 0.05f, -0.22f, 0.2f) * Size;
                            to = sh + new Vector3(inward * 0.12f, 0.34f, 0.46f) * Size;
                            return Vector3.Lerp(from, to, EaseOut(outU));
                        default:
                            from = GlovePlace(s, true);
                            to = sh + (a.Body ? new Vector3(inward * 0.1f, -0.16f, 0.66f) : new Vector3(inward * 0.12f, 0.12f, 0.7f)) * Size;
                            return Vector3.Lerp(from, to, EaseOut(outU));
                    }
                }
                case 2:   // a wind-up: the arm drawn back the way the punch will come
                {
                    float k = Ease(u);
                    Vector3 back = a.Attack switch
                    {
                        Attack.Jab => sh + new Vector3(inward * 0.06f, 0.1f, 0.02f),
                        Attack.Cross => sh + new Vector3(-inward * 0.08f, 0.1f, -0.2f),
                        Attack.Hook or Attack.Haymaker => sh + new Vector3(-inward * 0.42f, 0.16f, -0.08f) * (a.Attack == Attack.Haymaker ? 1.25f : 1f),
                        Attack.Uppercut => sh + new Vector3(inward * 0.02f, -0.4f, 0.04f),
                        Attack.Body => sh + new Vector3(-inward * 0.08f, -0.26f, -0.16f),
                        Attack.Sweep => sh + new Vector3(-0.55f, 0.1f, -0.1f),
                        Attack.Rocket => sh + new Vector3(-inward * 0.08f, 0.06f, -0.34f),
                        Attack.Lightning => sh + new Vector3(-inward * 0.12f, 0.58f, -0.05f),
                        Attack.Fireball => new Vector3(0f, 0.86f, 0.32f),
                        _ => sh + new Vector3(-inward * 0.05f, 0.08f, -0.18f),
                    } * 1f;
                    back = sh + (back - sh) * Size;
                    // a power punch's tell trembles at its end
                    if (Campaign.IsSpecial(a.Attack)) back += new Vector3(Mathf.Sin(_time * 60f), Mathf.Cos(_time * 53f), 0f) * 0.012f * k;
                    return Vector3.Lerp(GlovePlace(s, true), back, k);
                }
                default:  // the strike: from the wind-up to the hero (to where it aimed, when the game says where)
                {
                    float k = EaseOut(Mathf.Clamp01(u));
                    Vector3 from, to;
                    if (StrikeAt.HasValue && a.Attack is not (Attack.Sweep or Attack.Fireball))
                    {
                        var target = _body.InverseTransformPoint(StrikeAt.Value);
                        var reach = target - sh;
                        to = sh + Vector3.ClampMagnitude(reach, 0.8f * Size);
                        switch (a.Attack)
                        {
                            case Attack.Hook:
                            case Attack.Haymaker:
                            case Attack.Lightning:
                                from = sh + new Vector3(-inward * 0.42f, 0.16f, -0.05f) * Size;
                                return Arc(from, to, k, new Vector3(-inward * 0.12f, 0f, 0.1f) * Size);
                            case Attack.Uppercut:
                                from = sh + new Vector3(inward * 0.02f, -0.4f, 0.1f) * Size;
                                return Arc(from, to, k, new Vector3(0f, -0.12f, 0f) * Size);
                            case Attack.Body:
                                from = sh + new Vector3(-inward * 0.08f, -0.26f, -0.12f) * Size;
                                return Vector3.Lerp(from, to, k);
                            default:
                                from = sh + new Vector3(-inward * 0.05f, 0.08f, -0.18f) * Size;
                                return Vector3.Lerp(from, to, k);
                        }
                    }
                    switch (a.Attack)
                    {
                        case Attack.Hook:
                        case Attack.Haymaker:
                        case Attack.Lightning:
                            from = sh + new Vector3(-inward * 0.42f, 0.16f, -0.05f) * Size;
                            to = sh + new Vector3(inward * 0.3f, 0.14f, 0.58f) * Size;
                            return Arc(from, to, k, new Vector3(0f, 0f, 0.18f) * Size);
                        case Attack.Uppercut:
                            from = sh + new Vector3(inward * 0.02f, -0.4f, 0.1f) * Size;
                            to = sh + new Vector3(inward * 0.12f, 0.28f, 0.55f) * Size;
                            return Vector3.Lerp(from, to, k);
                        case Attack.Body:
                            from = sh + new Vector3(-inward * 0.08f, -0.26f, -0.12f) * Size;
                            to = sh + new Vector3(inward * 0.1f, -0.2f, 0.62f) * Size;
                            return Vector3.Lerp(from, to, k);
                        case Attack.Sweep:
                            from = sh + new Vector3(-0.55f, 0.1f, -0.1f) * Size;
                            to = sh + new Vector3(0.55f, 0.12f, 0.5f) * Size;
                            return Arc(from, to, k, new Vector3(0f, 0f, 0.25f) * Size);
                        case Attack.Fireball:
                            return Vector3.Lerp(new Vector3(0f, 0.86f, 0.32f) * Size, sh + new Vector3(inward * 0.15f, 0.06f, 0.55f) * Size, k);
                        default:
                            from = sh + new Vector3(-inward * 0.05f, 0.08f, -0.18f) * Size;
                            to = sh + new Vector3(inward * 0.12f, 0.12f, 0.66f) * Size;
                            return Vector3.Lerp(from, to, k);
                    }
                }
            }
        }

        /// <summary>From a to b bulging by <paramref name="bulge"/> in the middle (a swing round the body).</summary>
        static Vector3 Arc(Vector3 a, Vector3 b, float u, Vector3 bulge) => Vector3.Lerp(a, b, u) + bulge * (4f * u * (1f - u));

        /// <summary>The gloves on the hands: each on its mitten, along the forearm, the thumb up and inward.</summary>
        void PlaceGloves()
        {
            for (int s = 0; s < 2; s++)
            {
                if (_gloves[s] == null) continue;
                var dir = Chibi.ForearmDirection(s);
                var up = _body.up;
                if (Mathf.Abs(Vector3.Dot(dir, up)) > 0.95f) up = _body.forward;
                _gloves[s].position = Chibi.HandCenter(s) - dir * 0.02f * Size;
                _gloves[s].rotation = Quaternion.LookRotation(dir, up);
                _gloves[s].localScale = new Vector3(s == 0 ? -1f : 1f, 1f, 1f) * Size;
            }
        }

        // ------------------------------------------------------------------ the cape

        /// <summary>
        /// A cape of cloth: a grid of points hanging from the back of the shoulders (pinned to the spine's frame), pulled down
        /// and back, kept their distance apart, pushed out of the body; drawn both sides (outside the colour, inside darker).
        /// Cheap: 7 x 9 points, two passes a frame.
        /// </summary>
        sealed class CapeCloth
        {
            const int Cols = 7, Rows = 9;
            readonly Vector3[] _p = new Vector3[Cols * Rows], _old = new Vector3[Cols * Rows];
            readonly Mesh _mesh;
            readonly Vector3[] _verts = new Vector3[Cols * Rows * 2];
            readonly MeshRenderer _renderer;
            readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
            readonly Vector4[] _table = new Vector4[24];
            readonly float _size, _rowGap, _colGap;
            bool _started;

            public CapeCloth(Transform parent, Material material, Color colour, float size)
            {
                _size = size;
                _rowGap = 0.075f * size;
                _colGap = 0.07f * size;
                var go = new GameObject("Cape");
                go.transform.SetParent(parent, false);
                _mesh = new Mesh { name = "Cape" };
                _mesh.MarkDynamic();
                var uv1 = new Vector2[_verts.Length];
                var tris = new int[(Cols - 1) * (Rows - 1) * 12];
                int t = 0;
                for (int side = 0; side < 2; side++)
                {
                    int o = side * Cols * Rows;
                    for (int r = 0; r < Rows; r++)
                        for (int c = 0; c < Cols; c++)
                            uv1[o + r * Cols + c] = new Vector2(side == 0 ? 22f : 23f, 1f - 0.3f * r / (Rows - 1f));
                    for (int r = 0; r < Rows - 1; r++)
                        for (int c = 0; c < Cols - 1; c++)
                        {
                            int a = o + r * Cols + c, b = a + 1, d = a + Cols, e = d + 1;
                            if (side == 0) { tris[t++] = a; tris[t++] = d; tris[t++] = b; tris[t++] = b; tris[t++] = d; tris[t++] = e; }
                            else { tris[t++] = a; tris[t++] = b; tris[t++] = d; tris[t++] = b; tris[t++] = e; tris[t++] = d; }
                        }
                }
                _mesh.vertices = _verts;
                _mesh.uv2 = uv1;
                _mesh.triangles = tris;
                _mesh.RecalculateBounds();
                go.AddComponent<MeshFilter>().sharedMesh = _mesh;
                _renderer = go.AddComponent<MeshRenderer>();
                _renderer.sharedMaterial = material;
                _renderer.shadowCastingMode = ShadowCastingMode.Off;
                SetColour(colour);
            }

            public void SetColour(Color c)
            {
                var l = c.linear;
                var d = (c * 0.6f).linear;
                _table[22] = new Vector4(l.r, l.g, l.b, 0.2f);
                _table[23] = new Vector4(d.r, d.g, d.b, 0.1f);
                _block.SetVectorArray("_Regions", _table);
                _renderer.SetPropertyBlock(_block);
            }

            public void SeeThrough(Material depth, Material colour) =>
                _renderer.sharedMaterials = depth != null ? new[] { depth, colour } : new[] { colour };

            /// <summary>The top row on the shoulders' back (the spine's frame), the rest hanging and swinging.</summary>
            public void Step(Transform body, Chibi chibi, float dt)
            {
                var root = body;
                var up = root.up;
                var back = -root.forward;
                var right = root.right;
                var top = root.TransformPoint(new Vector3(0f, 0.86f * _size, -0.13f * _size));
                if (!_started)
                {
                    for (int r = 0; r < Rows; r++)
                        for (int c = 0; c < Cols; c++)
                        {
                            var p = top + right * ((c - (Cols - 1) * 0.5f) * _colGap) - up * (r * _rowGap) + back * (0.02f * r);
                            _p[r * Cols + c] = _old[r * Cols + c] = p;
                        }
                    _started = true;
                }
                dt = Mathf.Clamp(dt, 0.001f, 0.05f);
                var gravity = (-up * 5.5f + back * 2.2f) * _size;
                for (int r = 1; r < Rows; r++)
                    for (int c = 0; c < Cols; c++)
                    {
                        int i = r * Cols + c;
                        var v = (_p[i] - _old[i]) * 0.94f;
                        _old[i] = _p[i];
                        _p[i] += v + gravity * dt * dt + right * (Mathf.Sin(Time.time * 3.1f + r * 0.6f + c) * 0.0008f * r);
                    }
                for (int c = 0; c < Cols; c++)
                {
                    _p[c] = top + right * ((c - (Cols - 1) * 0.5f) * _colGap * 0.75f);
                    _old[c] = _p[c];
                }
                var chest = root.TransformPoint(new Vector3(0f, 0.6f * _size, 0.0f));
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int r = 1; r < Rows; r++)
                        for (int c = 0; c < Cols; c++)
                        {
                            int i = r * Cols + c;
                            Keep(i, i - Cols, _rowGap, true);
                            if (c > 0) Keep(i, i - 1, _colGap * (0.75f + 0.35f * r / (Rows - 1f)), false);
                            // out of the body (a capsule down the back)
                            var rel = _p[i] - chest;
                            float behind = Vector3.Dot(rel, back);
                            if (behind < 0.16f * _size) _p[i] += back * (0.16f * _size - behind);
                        }
                }
                // draw: both sides, a hair apart
                for (int i = 0; i < _p.Length; i++)
                {
                    var local = body.parent.InverseTransformPoint(_p[i]);
                    _verts[i] = local;
                    _verts[i + _p.Length] = local + body.parent.InverseTransformDirection(-back) * 0.004f;
                }
                _mesh.vertices = _verts;
                _mesh.RecalculateNormals();
                _mesh.RecalculateBounds();
            }

            void Keep(int i, int j, float rest, bool pinnedJ)
            {
                var d = _p[i] - _p[j];
                float len = d.magnitude;
                if (len < 1e-5f) return;
                float diff = (len - rest) / len;
                if (pinnedJ) _p[i] -= d * diff;
                else
                {
                    _p[i] -= d * diff * 0.5f;
                    _p[j] += d * diff * 0.5f;
                }
            }
        }
    }
}
