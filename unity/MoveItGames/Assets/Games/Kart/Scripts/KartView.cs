using UnityEngine;
using UnityEngine.Rendering;

namespace MoveIt.Games.Kart
{
    /// <summary>The kart's models and materials (set by the scene builder from art/kart's Kart.fbx).</summary>
    [System.Serializable]
    public sealed class KartModels
    {
        [Tooltip("art/kart/kart.py: KartBody, KartWheel, KartSteering.")] public GameObject kart;
        [Tooltip("Tundu/Athlete (the chibis' material: colour regions per kart).")] public Material paint;
        [Tooltip("Tundu/Fx additive: the exhausts' flames, the shield's bubble.")] public Material glow;

        Mesh _body, _wheel, _steering;

        public Mesh Body => Find(ref _body, "KartBody");
        public Mesh Wheel => Find(ref _wheel, "KartWheel");
        public Mesh Steering => Find(ref _steering, "KartSteering");

        Mesh Find(ref Mesh cache, string name)
        {
            if (cache != null || kart == null) return cache;
            foreach (var mf in kart.GetComponentsInChildren<MeshFilter>(true))
                if (mf.name == name) cache = mf.sharedMesh;
            return cache;
        }
    }

    /// <summary>
    /// One kart on the screen (2026-11-08): art/kart's body in its racer's paint, four wheels (the front pair turned with
    /// the wheel, all rolling with the speed), the steering wheel turned as the kart steers, a chibi at the wheel (its
    /// hands on the grips, leaning into the turns, its face by what happens), and for two players together a passenger on
    /// the back. It follows <see cref="Kart"/>: on the road and its slope, leaning into turns, sliding round in a drift,
    /// flipping or spinning through a trick, spinning out when painted, squashing on landing, flames from the exhausts on a
    /// boost, a bubble with a shield. Positions in the kart's frame: +z forward, +y up, the kart's right +x.
    /// </summary>
    public sealed class KartView : MonoBehaviour
    {
        /// <summary>The driver's size against the chibis' 1.52 m (big head, short arms: a toy in a toy kart).</summary>
        public const float DriverScale = 0.74f;

        static readonly Vector3[] WheelAt =
        {
            new Vector3(-0.63f, 0.25f, 0.70f), new Vector3(0.63f, 0.25f, 0.70f),     // front left, front right
            new Vector3(-0.63f, 0.29f, -0.62f), new Vector3(0.63f, 0.29f, -0.62f),   // rear
        };
        static readonly Vector3 SteeringAt = new Vector3(0f, 0.73f, 0.06f);
        const float SteeringTilt = -35f, SteeringRadius = 0.15f;
        static readonly Vector3[] ExhaustAt = { new Vector3(-0.19f, 0.665f, -0.94f), new Vector3(0.19f, 0.665f, -0.94f) };

        public Chibi Driver { get; private set; }
        public Chibi Passenger { get; private set; }
        public float Lean { get; private set; }
        /// <summary>The model's yaw against the way the kart goes (degrees: a drift's slide, a spin).</summary>
        public float Slide { get; private set; }

        Transform _pivot, _body, _steering, _flamesRoot, _shield;
        readonly Transform[] _wheels = new Transform[4], _flames = new Transform[2];
        MaterialPropertyBlock _block;
        readonly Vector4[] _table = new Vector4[24];
        Renderer _bodyRenderer, _steeringRenderer, _shieldRenderer;
        readonly Renderer[] _wheelRenderers = new Renderer[4];
        float _roll, _spin, _squash, _wheelTurn, _slide, _flame, _flash;
        Color _flashColour;
        int _trickShown = -1;
        float _hopLift;

        public static KartView Create(string name, KartModels models, KartPaint paint, ChibiLook driver, ChibiLook passenger, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<KartView>();
            v.Build(models, paint, driver, passenger);
            return v;
        }

        void Build(KartModels m, KartPaint paint, ChibiLook driver, ChibiLook passenger)
        {
            _block = new MaterialPropertyBlock();
            KartLooks.Fill(paint, _table);
            _pivot = new GameObject("Pivot").transform;
            _pivot.SetParent(transform, false);
            _body = new GameObject("Body").transform;
            _body.SetParent(_pivot, false);
            _bodyRenderer = Part(_body, m.Body, m.paint);
            for (int i = 0; i < 4; i++)
            {
                _wheels[i] = new GameObject($"Wheel {i}").transform;
                _wheels[i].SetParent(_body, false);
                _wheels[i].localPosition = WheelAt[i];
                _wheels[i].localScale = Vector3.one * (i < 2 ? 0.86f : 1f);
                var turn = new GameObject("Spin").transform;
                turn.SetParent(_wheels[i], false);
                if (WheelAt[i].x > 0f) turn.localRotation = Quaternion.Euler(0f, 180f, 0f);   // the hub cap outward
                _wheelRenderers[i] = Part(turn, m.Wheel, m.paint);
            }
            _steering = new GameObject("Steering").transform;
            _steering.SetParent(_body, false);
            _steering.localPosition = SteeringAt;
            _steeringRenderer = Part(_steering, m.Steering, m.paint);
            // the flames (a stretched glow) and the shield's bubble
            _flamesRoot = new GameObject("Flames").transform;
            _flamesRoot.SetParent(_body, false);
            for (int i = 0; i < 2; i++)
            {
                _flames[i] = new GameObject($"Flame {i}").transform;
                _flames[i].SetParent(_flamesRoot, false);
                _flames[i].localPosition = ExhaustAt[i];
                var r = Part(_flames[i], FlameMesh(), m.glow);
                var b = new MaterialPropertyBlock();
                b.SetColor("_BaseColor", new Color(1f, 0.62f, 0.22f, 1f));
                r.SetPropertyBlock(b);
            }
            _flamesRoot.gameObject.SetActive(false);
            _shield = new GameObject("Shield").transform;
            _shield.SetParent(_pivot, false);
            _shield.localPosition = new Vector3(0f, 0.75f, 0f);
            _shieldRenderer = Part(_shield, PartMesh.Ball(16), m.glow);
            var sb = new MaterialPropertyBlock();
            sb.SetColor("_BaseColor", new Color(0.45f, 0.85f, 1f, 0.45f));
            _shieldRenderer.SetPropertyBlock(sb);
            _shield.gameObject.SetActive(false);
            Recolour();

            if (driver != null)
            {
                Driver = Chibi.Create("Driver", driver, _body);
                Driver.transform.localScale = Vector3.one * DriverScale;
                Driver.transform.localPosition = new Vector3(0f, 0.58f - 0.47f * DriverScale + 0.04f, -0.2f);
                foreach (var r in Driver.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
            }
            if (passenger != null)
            {
                Passenger = Chibi.Create("Passenger", passenger, _body);
                Passenger.transform.localScale = Vector3.one * (DriverScale * 0.92f);
                Passenger.transform.localPosition = new Vector3(0f, 0.66f - 0.47f * DriverScale, -0.66f);
                foreach (var r in Passenger.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        static Mesh _flameMesh;

        /// <summary>A flame: a soft cone pointing back (-z) from the exhaust, 1 m long (scaled with the boost).</summary>
        static Mesh FlameMesh()
        {
            if (_flameMesh != null) return _flameMesh;
            var verts = new System.Collections.Generic.List<Vector3>();
            var uvs = new System.Collections.Generic.List<Vector2>();
            var tris = new System.Collections.Generic.List<int>();
            const int segs = 10;
            // the soft dot's bright middle at the exhaust, its faded edge at the tip
            verts.Add(new Vector3(0f, 0f, -1f));
            uvs.Add(new Vector2(0.5f, 0.98f));
            for (int i = 0; i < segs; i++)
            {
                float a = 2f * Mathf.PI * i / segs;
                verts.Add(new Vector3(Mathf.Cos(a) * 0.09f, Mathf.Sin(a) * 0.09f, 0f));
                uvs.Add(new Vector2(0.5f + Mathf.Cos(a) * 0.08f, 0.5f + Mathf.Sin(a) * 0.08f));
            }
            for (int i = 0; i < segs; i++)
            {
                tris.Add(0);
                tris.Add(1 + i);
                tris.Add(1 + (i + 1) % segs);
                tris.Add(0);
                tris.Add(1 + (i + 1) % segs);
                tris.Add(1 + i);
            }
            _flameMesh = new Mesh { name = "Flame" };
            _flameMesh.SetVertices(verts);
            _flameMesh.SetUVs(0, uvs);
            _flameMesh.SetTriangles(tris, 0);
            _flameMesh.RecalculateBounds();
            return _flameMesh;
        }

        Renderer Part(Transform t, Mesh mesh, Material material)
        {
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = t.gameObject.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.BlendProbes;
            return r;
        }

        void Recolour()
        {
            _block.SetVectorArray("_Regions", _table);
            _block.SetColor("_FlashColor", new Color(_flashColour.r, _flashColour.g, _flashColour.b, _flash));
            _bodyRenderer.SetPropertyBlock(_block);
            _steeringRenderer.SetPropertyBlock(_block);
            foreach (var w in _wheelRenderers) w.SetPropertyBlock(_block);
        }

        /// <summary>Small on the screen: the chibis' low-detail copies.</summary>
        public bool Low
        {
            set
            {
                if (Driver != null) Driver.Low = value;
                if (Passenger != null) Passenger.Low = value;
            }
        }

        public void Flash(Color colour, float amount)
        {
            _flashColour = colour;
            _flash = Mathf.Max(_flash, amount);
            Driver?.Flash(colour, amount);
        }

        /// <summary>Where the kart's exhausts are (world): the flames' and smoke's place.</summary>
        public Vector3 Exhaust(int side) => _body.TransformPoint(ExhaustAt[side] + new Vector3(0f, 0f, -0.05f));
        public Vector3 RearWheel(int side) => _body.TransformPoint(new Vector3(WheelAt[2 + side].x, 0.05f, WheelAt[2 + side].z));
        public Vector3 Forward => transform.forward;
        public Vector3 HeadWorld => Driver != null ? Driver.transform.TransformPoint(new Vector3(0f, 1.18f, 0f)) : transform.position + Vector3.up * 1.4f;

        /// <summary>
        /// Puts the kart where <paramref name="k"/> is and poses it. <paramref name="wheel"/> = the wheel's turn the
        /// driver shows (degrees, + = right: a player's own hands, else the kart's steer).
        /// </summary>
        public void Show(Kart k, KartTrack track, float wheel, float dt, double raceTime)
        {
            // where: on the road, along its heading plus the kart's, up its slope
            float s = k.S;
            var pos = track.World(s, k.D, k.Ground + k.Height);
            float heading = track.Heading(s) + k.Psi;
            var forward = new Vector3(Mathf.Cos(heading), 0f, Mathf.Sin(heading));
            float slope = Mathf.Atan(track.Slope(s) + (k.Ground > 0.01f && !k.Airborne ? KartRace.LipHeight / 6f : 0f)) * Mathf.Rad2Deg;
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(forward) * Quaternion.Euler(-slope, 0f, 0f));

            // leaning into the turn, sliding in a drift, spinning out, flipping in a trick
            float targetRoll = -k.Steer * 7f * Mathf.Clamp01(k.Speed / 15f);
            _roll = Mathf.Lerp(_roll, targetRoll, 1f - Mathf.Exp(-8f * dt));
            float targetSlide = k.Drifting ? k.DriftDir * 24f : 0f;
            _slide = Mathf.Lerp(_slide, targetSlide, 1f - Mathf.Exp(-7f * dt));
            if (k.SpinTime > 0f) _spin += dt * 720f;
            else _spin = Mathf.MoveTowards(Mathf.Repeat(_spin + 180f, 360f) - 180f, 0f, dt * 900f);
            Slide = _slide + _spin;
            Lean = _roll;
            var trick = Quaternion.identity;
            if (k.Trick > 0 && k.Airborne)
            {
                float t = Mathf.Clamp01((float)(raceTime - k.TrickAt) / 0.7f);
                float turn = Mathf.SmoothStep(0f, 1f, t) * 360f * (k.Trick == 2 ? 2f : 1f);
                trick = k.TrickKind switch
                {
                    0 => Quaternion.Euler(-turn, 0f, 0f),     // a front flip
                    1 => Quaternion.Euler(0f, turn, 0f),      // a spin
                    _ => Quaternion.Euler(0f, 0f, turn),      // a barrel roll
                };
            }
            // squash on landing and on a hop's take-off
            _squash = Mathf.MoveTowards(_squash, 0f, dt * 4f);
            if (k.Airborne && k.VSpeed < -2f) _hopLift = 1f;
            else if (!k.Airborne && _hopLift > 0f)
            {
                _squash = 0.22f;
                _hopLift = 0f;
            }
            float sq = Mathf.Sin(_squash / 0.22f * Mathf.PI) * _squash;
            _pivot.localPosition = new Vector3(0f, 0f, 0f);
            _pivot.localRotation = Quaternion.Euler(0f, Slide, _roll) * trick;
            _pivot.localScale = new Vector3(1f + sq * 0.5f, 1f - sq, 1f + sq * 0.5f);
            // a little bounce from the road at speed
            _body.localPosition = new Vector3(0f, k.Airborne ? 0f : Mathf.Sin((float)raceTime * 21f + k.Index) * 0.012f * Mathf.Clamp01(k.Speed / 20f), 0f);

            // wheels: turned with the steer, rolling with the speed
            _wheelTurn += k.Speed * dt / 0.27f * Mathf.Rad2Deg;
            float steerAngle = k.Steer * 26f;
            for (int i = 0; i < 4; i++)
            {
                _wheels[i].localRotation = Quaternion.Euler(0f, i < 2 ? steerAngle : 0f, 0f);
                var spin = _wheels[i].GetChild(0);
                var face = WheelAt[i].x > 0f ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
                spin.localRotation = face * Quaternion.Euler(WheelAt[i].x > 0f ? -_wheelTurn : _wheelTurn, 0f, 0f);
            }
            // the steering wheel turns with the driver's hands (clockwise for right)
            float shown = Mathf.Clamp(wheel, -110f, 110f);
            _steering.localRotation = Quaternion.Euler(SteeringTilt, 0f, 0f) * Quaternion.Euler(0f, 0f, -shown);

            // flames on a boost, the shield's bubble
            _flame = Mathf.MoveTowards(_flame, k.BoostTime > 0f ? (k.RocketTime > 0f ? 1.6f : 1f) : 0f, dt * 6f);
            bool flames = _flame > 0.02f;
            if (_flamesRoot.gameObject.activeSelf != flames) _flamesRoot.gameObject.SetActive(flames);
            if (flames)
                for (int i = 0; i < 2; i++)
                {
                    float flicker = 0.75f + 0.25f * Mathf.PerlinNoise((float)raceTime * 18f, i * 3.1f);
                    _flames[i].localScale = new Vector3(1f + 0.4f * _flame, 1f + 0.4f * _flame, (0.35f + 0.55f * _flame) * flicker);
                }
            bool shield = k.ShieldTime > 0f;
            if (_shield.gameObject.activeSelf != shield) _shield.gameObject.SetActive(shield);
            if (shield)
            {
                float pulse = 1f + 0.04f * Mathf.Sin((float)raceTime * 6f);
                bool ending = k.ShieldTime < 2f && Mathf.Repeat((float)raceTime * 6f, 1f) < 0.4f;
                _shield.localScale = new Vector3(2.0f, 1.7f, 2.6f) * pulse * (ending ? 0.96f : 1f);
            }
            _flash = Mathf.MoveTowards(_flash, 0f, dt * 2.5f);
            Recolour();
            PoseDriver(k, shown, dt);
        }

        float _lookYaw;

        /// <summary>The driver sits (legs forward), its mittens on the wheel's grips, leaning with the turns, looking into
        /// them; faces by what happens. The passenger holds on behind.</summary>
        void PoseDriver(Kart k, float wheel, float dt)
        {
            if (Driver != null)
            {
                Driver.ResetPose();
                for (int side = 0; side < 2; side++) Driver.BendLeg(side, 78f, 18f);
                // the grips at ten to two, turned with the wheel
                for (int side = 0; side < 2; side++)
                {
                    float a = (side == 0 ? 145f : 35f) - wheel;
                    var gripLocal = Quaternion.Euler(SteeringTilt, 0f, 0f) * new Vector3(Mathf.Cos(a * Mathf.Deg2Rad) * SteeringRadius, Mathf.Sin(a * Mathf.Deg2Rad) * SteeringRadius, 0f);
                    var grip = _body.TransformPoint(SteeringAt + gripLocal);
                    var shoulder = Driver.Shoulder(side);
                    var dir = Driver.transform.InverseTransformDirection(grip - shoulder);
                    Driver.AimArm(side, dir);
                }
                _lookYaw = Mathf.Lerp(_lookYaw, wheel * 0.25f + _slide * -0.4f, 1f - Mathf.Exp(-6f * dt));
                Driver.TurnHead(Mathf.Clamp(_lookYaw, -30f, 30f), 4f);
                var face = k.SpinTime > 0f || k.SlowTime > 0f ? Chibi.Face.Sad
                    : k.Trick > 0 || k.BoostTime > 0.3f ? Chibi.Face.Grin
                    : k.Finished && k.Place <= 3 ? Chibi.Face.Happy : Chibi.Face.Smile;
                Driver.SetFace(face);
                Driver.transform.localRotation = Quaternion.Euler(0f, 0f, _roll * 0.8f);
            }
            if (Passenger != null)
            {
                Passenger.ResetPose();
                for (int side = 0; side < 2; side++) Passenger.BendLeg(side, 70f, 60f);
                bool cheering = k.Trick > 0 || k.BoostTime > 0.5f;
                for (int side = 0; side < 2; side++)
                    Passenger.AimArm(side, cheering ? new Vector3(side == 0 ? -0.5f : 0.5f, 1f, 0.2f) : new Vector3(side == 0 ? -0.25f : 0.25f, 0.15f, 1f));
                Passenger.TurnHead(-_lookYaw * 0.6f, 0f);
                Passenger.SetFace(k.SpinTime > 0f ? Chibi.Face.Sad : cheering ? Chibi.Face.Happy : Chibi.Face.Grin);
                Passenger.transform.localRotation = Quaternion.Euler(0f, 0f, _roll);
            }
        }
    }
}
