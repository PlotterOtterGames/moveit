using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Arcade
{
    /// <summary>
    /// How a booth looks: its rules' state drawn every frame into the sprite batches (world = HUD pixels, y up), over the
    /// camera picture (or its own sky, Zogu!), and its events turned into effects, popups and sounds. Sizes are in the
    /// players' torso lengths (<see cref="ArcadeStage.UnitPx"/>), so a child's moles are a child's size.
    /// </summary>
    public abstract class MachineView
    {
        protected readonly ArcadeArt Art;
        protected readonly ArcadeStage Stage;
        protected readonly ArcadeFx Fx;
        public abstract MachineRules Rules { get; }
        /// <summary>The booth plays over the camera picture (all but Zogu!, which has its own sky).</summary>
        public virtual bool UsesPicture => true;

        protected MachineView(ArcadeArt art, ArcadeStage stage, ArcadeFx fx)
        {
            Art = art;
            Stage = stage;
            Fx = fx;
        }

        public abstract void Draw(SpriteBatch back, SpriteBatch glow, float time, float dt);
        public virtual void OnEvent(ArcadeEvent e) { }
        public virtual void Dispose() { }

        protected Vector2 W(int p, Vector2 stage) => Stage.StageToWorld(p, stage);
        protected float U(int p) => Stage.UnitPx(p);

        public static readonly Color Sun = new Color(1f, 0.79f, 0.24f), Coral = new Color(1f, 0.35f, 0.31f), Sky = new Color(0.19f, 0.78f, 0.96f),
                                     Grape = new Color(0.48f, 0.38f, 1f), Leaf = new Color(0.44f, 0.84f, 0.42f), Orange = new Color(1f, 0.54f, 0.24f);
        public static Color PlayerColour(int p) => p == 0 ? Coral : Sky;

        protected void PointsPopup(Vector2 at, int points, float size = 56f)
        {
            if (points == 0) return;
            Fx.Points(at, points > 0 ? "+" + points : "-" + (-points), points > 0 ? Sun : Coral, size);
        }
    }

    // ------------------------------------------------------------------------------------------------ Godit!

    public sealed class WhackView : MachineView
    {
        readonly WhackRules _rules;
        readonly float[] _swing = new float[4];
        public override MachineRules Rules => _rules;

        public WhackView(WhackRules rules, ArcadeArt art, ArcadeStage stage, ArcadeFx fx) : base(art, stage, fx) => _rules = rules;

        public override void Draw(SpriteBatch back, SpriteBatch glow, float time, float dt)
        {
            for (int p = 0; p < _rules.Players; p++)
            {
                float u = U(p);
                var board = _rules.Boards[p];
                for (int h = 0; h < WhackRules.Holes.Length; h++)
                {
                    var at = W(p, _rules.HoleAt(h));
                    float bob = Mathf.Sin(time * 1.6f + h * 1.3f) * 0.025f * u;   // the islands float
                    var pos = at + new Vector2(0f, bob);
                    if (_rules.Players == 2) glow.Draw(Art["glow"], pos, u * 0.55f, 0f, PlayerColour(p) * new Color(1f, 1f, 1f, 0.45f));
                    back.Draw(Art["island_back"], pos, u * 0.447f);
                    WhackRules.Critter critter = null;
                    foreach (var c in board.Critters)
                        if (c.Hole == h) critter = c;
                    if (critter != null)
                    {
                        float k = critter.Out;
                        string name = critter.State == WhackRules.State.Hit
                            ? critter.Kind == WhackRules.Kind.Hedgehog ? "hedgehog" : "mole_hit"
                            : critter.Kind switch { WhackRules.Kind.Gold => "mole_gold", WhackRules.Kind.Hedgehog => "hedgehog", _ => "mole" };
                        float squash = critter.State == WhackRules.State.Hit ? 1f - 0.15f * Mathf.Sin(Mathf.Clamp01(critter.StateTime / 0.2f) * Mathf.PI) : 1f;
                        float wiggle = critter.State == WhackRules.State.Up ? Mathf.Sin(critter.StateTime * 9f) * 4f : 0f;
                        if (critter.Kind == WhackRules.Kind.Gold && critter.State != WhackRules.State.Hit)
                            glow.Draw(Art["glow"], pos + new Vector2(0f, 0.3f * u * k), u * 0.5f, 0f, new Color(1f, 0.85f, 0.4f, 0.6f));
                        back.Draw(Art[name], pos + new Vector2(0f, 0.03f * u), u * 0.3f, wiggle, null, 0.85f + 0.15f * k, k * squash);
                    }
                    back.Draw(Art["island_front"], pos, u * 0.447f);
                }
                if (Stage.Bodies[p].Present)
                    for (int hand = 0; hand < 2; hand++)
                    {
                        var part = hand == 0 ? Part.LeftHand : Part.RightHand;
                        if (!Stage.Bodies[p].Sees(part)) continue;
                        int i = p * 2 + hand;
                        _swing[i] = Mathf.Max(0f, _swing[i] - dt);
                        float swing = _swing[i] > 0f ? Mathf.Sin(_swing[i] / 0.25f * Mathf.PI) * 55f : 0f;
                        bool left = hand == 0;
                        var at = W(p, Stage.Bodies[p][part]);
                        var tint = _rules.Players == 2 && p == 1 ? new Color(0.75f, 0.9f, 1f) : Color.white;
                        // the hammer held up, its head outward (the sprite's head is up and to the left: mirrored for the
                        // right hand); a bonk swings it down and out
                        back.Draw(Art["hammer"], at, u * 0.36f, left ? swing : -swing, tint, left ? 1f : -1f, 1f);
                    }
            }
        }

        public override void OnEvent(ArcadeEvent e)
        {
            var at = W(e.Player, e.At) + new Vector2(0f, 0.3f * U(e.Player));
            float s = U(e.Player) / 260f;
            switch (e.Kind)
            {
                case EventKind.Hit:
                case EventKind.Gold:
                    Fx.Pow(at, s, Color.white);
                    Fx.Burst(at, s, e.Kind == EventKind.Gold ? Sun : Color.white, e.Kind == EventKind.Gold ? 16 : 9);
                    if (e.Kind == EventKind.Gold) Fx.Twinkle(at, 0.5f * U(e.Player), Sun, 6);
                    PointsPopup(at + new Vector2(0f, 0.35f * U(e.Player)), e.Points, e.Kind == EventKind.Gold ? 72f : 56f);
                    if (e.Combo >= 4 && e.Combo % 4 == 0) Fx.Points(at + new Vector2(0f, 0.7f * U(e.Player)), "x" + (1 + Mathf.Min(3, e.Combo / 4)), Sky, 64f);
                    Swing(e);
                    GameAudio.Sfx("arcade_bonk", 0.9f);
                    if (e.Kind == EventKind.Gold) GameAudio.Sfx("kart_star", 0.8f);
                    break;
                case EventKind.Bad:
                    Fx.Pow(at, s, Coral);
                    Fx.Dizzy(W(e.Player, Stage.Bodies[e.Player][Part.Head]) + new Vector2(0f, 0.25f * U(e.Player)), 0.3f * U(e.Player));
                    PointsPopup(at + new Vector2(0f, 0.35f * U(e.Player)), e.Points);
                    Swing(e);
                    GameAudio.Sfx("arcade_ouch");
                    break;
                case EventKind.Spawn:
                    Fx.Puff(W(e.Player, e.At), s * 0.6f, new Color(0.75f, 0.6f, 0.45f, 0.7f), 3);
                    GameAudio.Sfx("arcade_pop", 0.35f, 1f + 0.1f * (e.Variant == (int)WhackRules.Kind.Hedgehog ? -2 : 0), 0.12f);
                    break;
            }
        }

        void Swing(ArcadeEvent e)
        {
            // the hand nearest the hole swings
            var body = Stage.Bodies[e.Player];
            float l = body.Sees(Part.LeftHand) ? Vector2.Distance(body[Part.LeftHand], e.At) : 99f;
            float r = body.Sees(Part.RightHand) ? Vector2.Distance(body[Part.RightHand], e.At) : 99f;
            _swing[e.Player * 2 + (l < r ? 0 : 1)] = 0.25f;
        }
    }

    // ------------------------------------------------------------------------------------------------ Fruta!

    public sealed class SliceView : MachineView
    {
        readonly SliceRules _rules;
        readonly List<(Vector2 at, float time)>[] _trails = new List<(Vector2, float)>[4];
        public override MachineRules Rules => _rules;

        static readonly string[] Names = { "apple", "orange", "melon", "banana", "berry", "pear", "star", "bomb" };
        static readonly Color[] JuiceColours =
        {
            new Color(1f, 0.89f, 0.54f), new Color(1f, 0.65f, 0.23f), new Color(1f, 0.3f, 0.37f), new Color(1f, 0.9f, 0.5f),
            new Color(1f, 0.25f, 0.38f), new Color(0.9f, 0.94f, 0.54f), new Color(1f, 0.89f, 0.36f), new Color(0.6f, 0.63f, 0.71f),
        };

        public SliceView(SliceRules rules, ArcadeArt art, ArcadeStage stage, ArcadeFx fx) : base(art, stage, fx)
        {
            _rules = rules;
            for (int i = 0; i < 4; i++) _trails[i] = new List<(Vector2, float)>();
        }

        public override void Draw(SpriteBatch back, SpriteBatch glow, float time, float dt)
        {
            for (int p = 0; p < _rules.Players; p++)
            {
                float u = U(p);
                foreach (var f in _rules.Boards[p].Fruit)
                {
                    var at = W(p, f.Pos);
                    float unit = u * f.Radius / (f.Kind == SliceRules.FruitKind.Bomb ? 0.72f : 0.8f);
                    string name = Names[(int)f.Kind];
                    if (!f.Sliced)
                    {
                        if (f.Kind == SliceRules.FruitKind.Star) glow.Draw(Art["glow"], at, unit * 1.2f, 0f, new Color(1f, 0.85f, 0.4f, 0.55f));
                        if (f.Kind == SliceRules.FruitKind.Bomb)
                        {
                            var fuse = at + (Vector2)(Quaternion.Euler(0f, 0f, f.Angle * 0.2f) * new Vector3(0.55f * unit, 0.74f * unit, 0f));
                            glow.Draw(Art["spark"], fuse, unit * (0.35f + 0.12f * Mathf.Sin(time * 40f)), time * 400f, new Color(1f, 0.85f, 0.4f));
                            back.Draw(Art[name], at, unit, f.Angle * 0.2f);
                        }
                        else back.Draw(Art[name], at, unit, f.Angle);
                        continue;
                    }
                    if (f.Kind == SliceRules.FruitKind.Bomb) continue;   // gone in a puff
                    var n = new Vector2(-f.Cut.y, f.Cut.x);
                    float apart = (0.06f + f.SlicedAge * 0.9f) * u;
                    float cut = Mathf.Atan2(f.Cut.y, f.Cut.x) * Mathf.Rad2Deg - 90f;
                    var c = new Color(1f, 1f, 1f, Mathf.Clamp01((1.4f - f.SlicedAge) / 0.4f));
                    back.Draw(Art[name + "_left"], at - n * apart, unit, cut - f.SlicedAge * 140f, c);
                    back.Draw(Art[name + "_right"], at + n * apart, unit, cut + f.SlicedAge * 140f, c);
                }
                // the blade: each hand's last tenth of a second while it moves fast
                var body = Stage.Bodies[p];
                for (int hand = 0; hand < 2; hand++)
                {
                    var trail = _trails[p * 2 + hand];
                    var part = hand == 0 ? Part.LeftHand : Part.RightHand;
                    if (body.Present && body.Sees(part)) trail.Add((W(p, body[part]), time));
                    while (trail.Count > 0 && time - trail[0].time > 0.12f) trail.RemoveAt(0);
                    if (trail.Count < 2) continue;
                    float length = 0f;
                    for (int i = 1; i < trail.Count; i++) length += Vector2.Distance(trail[i - 1].at, trail[i].at);
                    float speed = length / Mathf.Max(0.02f, trail[trail.Count - 1].time - trail[0].time) / u;
                    if (speed < SliceRules.SliceSpeed * 0.8f) continue;
                    var colour = _rules.Players == 2 ? PlayerColour(p) : new Color(0.75f, 0.95f, 1f);
                    for (int i = 1; i < trail.Count; i++)
                    {
                        float k0 = (float)(i - 1) / (trail.Count - 1), k1 = (float)i / (trail.Count - 1);
                        glow.Stretch(Art["dot"], trail[i - 1].at, trail[i].at, u * 0.16f * k1, colour * new Color(1f, 1f, 1f, k0 * 0.8f), colour * new Color(1f, 1f, 1f, k1));
                        back.Stretch(Art["white"], trail[i - 1].at, trail[i].at, u * 0.04f * k1, new Color(1f, 1f, 1f, k0), Color.white);
                    }
                }
            }
        }

        public override void OnEvent(ArcadeEvent e)
        {
            var at = W(e.Player, e.At);
            float s = U(e.Player) / 260f;
            switch (e.Kind)
            {
                case EventKind.Slice:
                    Fx.Juice(at, s, JuiceColours[Mathf.Clamp(e.Variant, 0, JuiceColours.Length - 1)]);
                    if (e.Variant == (int)SliceRules.FruitKind.Star) Fx.Burst(at, s, Sun, 14);
                    PointsPopup(at + new Vector2(0f, 0.3f * U(e.Player)), e.Points);
                    GameAudio.Sfx("arcade_slice", 0.9f, 1f + 0.05f * Mathf.Min(e.Combo, 5));
                    break;
                case EventKind.Combo:
                    Fx.Points(at + new Vector2(0f, 0.6f * U(e.Player)), "x" + e.Combo, Sky, 84f);
                    Fx.Burst(at, s * 1.4f, Sky, 14);
                    GameAudio.Sfx("kart_trick", 0.8f);
                    break;
                case EventKind.Boom:
                    Fx.Pow(at, s * 1.5f, new Color(1f, 0.6f, 0.2f));
                    Fx.Burst(at, s * 1.5f, new Color(1f, 0.7f, 0.3f), 14);
                    Fx.Puff(at, s * 1.4f, new Color(0.35f, 0.35f, 0.42f, 0.85f), 7);
                    PointsPopup(at + new Vector2(0f, 0.4f * U(e.Player)), e.Points, 64f);
                    GameAudio.Sfx("crash", 0.8f);
                    break;
                case EventKind.Spawn:
                    GameAudio.Sfx("whoosh", 0.18f, 1.2f, 0.15f);
                    break;
            }
        }
    }

    // ------------------------------------------------------------------------------------------------ Muri!

    public sealed class WallView : MachineView
    {
        readonly WallRules _rules;
        readonly WallMask _mask = new WallMask();
        readonly Material _material;
        readonly Mesh _mesh;
        readonly GameObject _quad;
        readonly Vector3[] _corners = new Vector3[4];
        readonly List<WallMask.Capsule> _shape = new List<WallMask.Capsule>();
        int _maskFor = -1;
        Rect _final;
        public override MachineRules Rules => _rules;

        public WallView(WallRules rules, ArcadeArt art, ArcadeStage stage, ArcadeFx fx, Material wallMaterial, Transform parent) : base(art, stage, fx)
        {
            _rules = rules;
            _material = new Material(wallMaterial) { renderQueue = 2995 };
            if (art.Wall != null) _material.SetTexture("_BaseMap", art.Wall);
            _mesh = new Mesh { name = "Wall" };
            _mesh.MarkDynamic();
            _mesh.vertices = _corners;
            _mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            _mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            _mesh.bounds = new Bounds(Vector3.zero, new Vector3(1e5f, 1e5f, 10f));
            _quad = new GameObject("Wall");
            _quad.transform.SetParent(parent, false);
            _quad.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = _quad.AddComponent<MeshRenderer>();
            r.sharedMaterial = _material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        public override void Dispose()
        {
            _mask.Dispose();
            Object.Destroy(_material);
            Object.Destroy(_mesh);
            Object.Destroy(_quad);
        }

        /// <summary>The wall to show: the coming one, or the last one for a moment after it arrived.</summary>
        WallRules.Wall Shown()
        {
            WallRules.Wall shown = null;
            foreach (var w in _rules.Walls)
                if (shown == null || w.Spawn > shown.Spawn) shown = w;
            if (shown != null && shown.Checked && _rules.Time > shown.Arrive + 1.05f) return null;
            return shown;
        }

        public override void Draw(SpriteBatch back, SpriteBatch glow, float time, float dt)
        {
            var w = Shown();
            _quad.SetActive(w != null);
            if (w == null) return;
            if (w.Id != _maskFor) Build(w);
            float t = _rules.Time;
            float a = Mathf.Clamp01(w.Approach(t));
            float s = 1f / (1f + 3.2f * (1f - a));
            float alpha = Mathf.Clamp01((t - w.Spawn) / 0.25f);
            var vanish = new Vector2(Stage.GuiWidth * 0.5f, 640f);
            var centre = vanish + (_final.center - vanish) * s;
            var size = _final.size * s;
            bool passed = false;
            for (int p = 0; p < _rules.Players; p++) passed |= w.Checked && w.Passed[p];
            if (w.Checked)
            {
                float after = t - w.Arrive;
                if (passed)
                {
                    float grow = 1f + 3.5f * after;   // it goes past the players, toward the camera
                    size = _final.size * grow;
                    centre = vanish + (_final.center - vanish) * grow;
                    alpha = Mathf.Clamp01(1f - after / 0.4f);
                }
                else
                {
                    float shake = after < 0.35f ? Mathf.Sin(after * 60f) * 14f * (1f - after / 0.35f) : 0f;
                    float drop = after > 0.35f ? 2400f * (after - 0.35f) * (after - 0.35f) : 0f;
                    centre = _final.center + new Vector2(shake, -drop);
                    size = _final.size;
                    alpha = Mathf.Clamp01(1f - (after - 0.5f) / 0.5f);
                }
            }
            var r = new Rect(centre - size * 0.5f, size);
            _corners[0] = new Vector3(r.xMin, r.yMin, 0.5f);
            _corners[1] = new Vector3(r.xMax, r.yMin, 0.5f);
            _corners[2] = new Vector3(r.xMax, r.yMax, 0.5f);
            _corners[3] = new Vector3(r.xMin, r.yMax, 0.5f);
            _mesh.vertices = _corners;
            // the rim lights up green while the player holds the shape, gold when it's exact
            float match = 0f;
            for (int p = 0; p < _rules.Players; p++) match = Mathf.Max(match, _rules.Boards[p].MatchNow);
            var rim = w.Checked ? (passed ? Sun : Coral) : match >= 0.999f ? Sun : match >= WallRules.PassAt ? Leaf : Color.white;
            rim.a = 0.95f;
            _material.SetColor("_RimColor", rim);
            _material.SetColor("_BaseColor", new Color(1f, 1f, 1f, alpha));
        }

        void Build(WallRules.Wall w)
        {
            _maskFor = w.Id;
            _shape.Clear();
            float xMin = float.MaxValue, xMax = float.MinValue, yMin = float.MaxValue, yMax = float.MinValue, unit = 0f;
            for (int p = 0; p < _rules.Players; p++)
            {
                int pp = p;
                WallMask.PoseShape(w.Pose, s => W(pp, s), U(p), _shape);
                var lo = W(p, new Vector2(w.Pose.Step - 2.3f, -2.1f));
                var hi = W(p, new Vector2(w.Pose.Step + 2.3f, 3.0f));
                xMin = Mathf.Min(xMin, lo.x);
                yMin = Mathf.Min(yMin, lo.y);
                xMax = Mathf.Max(xMax, hi.x);
                yMax = Mathf.Max(yMax, hi.y);
                unit += U(p) / _rules.Players;
            }
            _final = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            bool wide = _final.width > _final.height * 1.4f;
            _mask.Draw(_final, _shape, unit, wide ? 256 : 128, 128);
            _material.SetTexture("_HoleTex", _mask.Texture);
            _material.SetTextureScale("_BaseMap", new Vector2(Mathf.Max(1f, Mathf.Round(_final.width / _final.height)), 1f));
        }

        public override void OnEvent(ArcadeEvent e)
        {
            float u = U(e.Player), s = u / 260f;
            var chest = W(e.Player, new Vector2(Stage.Bodies[e.Player].Present ? Stage.Bodies[e.Player].Hips.x : 0f, 1.1f));
            switch (e.Kind)
            {
                case EventKind.Spawn:
                    GameAudio.Sfx("arcade_wall", 0.8f);
                    break;
                case EventKind.Pass:
                case EventKind.Perfect:
                    Fx.ConfettiBurst(chest, s, e.Kind == EventKind.Perfect ? 40 : 22);
                    PointsPopup(chest + new Vector2(0f, 1.2f * u), e.Points, e.Kind == EventKind.Perfect ? 80f : 64f);
                    GameAudio.Sfx(e.Kind == EventKind.Perfect ? "kart_trick" : "kart_star", 0.9f);
                    break;
                case EventKind.Fail:
                    var head = W(e.Player, Stage.Bodies[e.Player].Present ? Stage.Bodies[e.Player][Part.Head] : new Vector2(0f, 1.45f));
                    Fx.Pow(head, s * 1.5f, Color.white);
                    Fx.Dizzy(head + new Vector2(0f, 0.3f * u), 0.35f * u);
                    GameAudio.Sfx("arcade_bonk", 1f, 0.8f);
                    break;
            }
        }
    }

    // ------------------------------------------------------------------------------------------------ Kap!

    public sealed class CatchView : MachineView
    {
        readonly CatchRules _rules;
        public override MachineRules Rules => _rules;
        static readonly Color[] Balloons = { Coral, Sky, Sun, Grape };

        public CatchView(CatchRules rules, ArcadeArt art, ArcadeStage stage, ArcadeFx fx) : base(art, stage, fx) => _rules = rules;

        public override void Draw(SpriteBatch back, SpriteBatch glow, float time, float dt)
        {
            for (int p = 0; p < _rules.Players; p++)
            {
                float u = U(p);
                var b = _rules.Boards[p];
                float shake = b.Shaking > 0f ? Mathf.Sin(time * 55f) * 0.06f * u : 0f;
                var body = Stage.Bodies[p];
                bool show = b.HaveBasket || body.Present && body.Sees(Part.LeftHip) && body.Sees(Part.RightHip);
                float basketX = b.HaveBasket ? b.BasketX : show ? Mathf.Clamp(body.Hips.x, -_rules.Strip - 0.3f, _rules.Strip + 0.3f) : 0f;
                var basket = W(p, new Vector2(basketX, CatchRules.RimY)) + new Vector2(shake, 0f);
                if (show)
                {
                    back.Draw(Art["basket_back"], basket, u * 0.43f);
                    int pile = Mathf.Min(7, b.Caught);
                    for (int i = 0; i < pile; i++)
                    {
                        float x = (i % 4 - 1.5f) * 0.2f + (i / 4) * 0.1f, y = 0.05f + (i / 4) * 0.08f;
                        back.Draw(Art["candy" + (i % 4)], basket + new Vector2(x * u, y * u), u * 0.12f, (i * 47) % 40 - 20f);
                    }
                }
                foreach (var it in b.Items)
                {
                    var at = W(p, it.Pos);
                    float k = it.Done ? Mathf.Clamp01(1f - it.DoneAge / 0.25f) : 1f;
                    if (it.Done && it.Caught && it.Kind != CatchRules.ItemKind.Balloon && it.Kind != CatchRules.ItemKind.Bomb)
                        at = Vector2.Lerp(at, basket, Mathf.Clamp01(it.DoneAge / 0.15f));
                    if (k <= 0f) continue;
                    switch (it.Kind)
                    {
                        case CatchRules.ItemKind.Candy:
                            back.Draw(Art["candy" + it.Variant], at, u * 0.16f * k, Mathf.Sin(it.Phase * 3f) * 25f);
                            break;
                        case CatchRules.ItemKind.Lollipop:
                            back.Draw(Art["lollipop"], at, u * 0.19f * k, Mathf.Sin(it.Phase * 2f) * 15f);
                            break;
                        case CatchRules.ItemKind.Floss:
                            back.Draw(Art["floss"], at, u * 0.2f * k, Mathf.Sin(it.Phase * 2.5f) * 12f);
                            break;
                        case CatchRules.ItemKind.Star:
                            glow.Draw(Art["glow"], at, u * 0.45f * k, 0f, new Color(1f, 0.85f, 0.4f, 0.6f));
                            back.Draw(Art["star"], at, u * 0.25f * k, it.Phase * 90f);
                            break;
                        case CatchRules.ItemKind.Bomb:
                            back.Draw(Art["bomb"], at, u * 0.26f * k, Mathf.Sin(it.Phase * 4f) * 10f);
                            break;
                        case CatchRules.ItemKind.Balloon:
                            if (!it.Done) back.Draw(Art["balloon"], at, u * 0.32f, Mathf.Sin(it.Phase * 1.7f) * 8f, Balloons[it.Variant % Balloons.Length]);
                            break;
                    }
                }
                if (show) back.Draw(Art["basket_front"], basket, u * 0.43f);
            }
        }

        public override void OnEvent(ArcadeEvent e)
        {
            var at = W(e.Player, e.At);
            float u = U(e.Player), s = u / 260f;
            switch (e.Kind)
            {
                case EventKind.Catch:
                    Fx.Twinkle(at + new Vector2(0f, 0.15f * u), 0.4f * u, Sun, 3);
                    PointsPopup(at + new Vector2(0f, 0.55f * u), e.Points);
                    GameAudio.Sfx("egg_" + Mathf.Clamp(e.Combo - 1, 0, 49) % 8, 0.8f, 1f, 0f);
                    if (e.Combo > 0 && e.Combo % 6 == 0) Fx.Points(at + new Vector2(0f, 0.95f * u), "x" + (1 + Mathf.Min(2, e.Combo / 6)), Sky, 64f);
                    break;
                case EventKind.Boom:
                    Fx.Pow(at, s * 1.4f, new Color(1f, 0.6f, 0.2f));
                    Fx.Puff(at, s * 1.2f, new Color(0.35f, 0.35f, 0.42f, 0.85f), 6);
                    PointsPopup(at + new Vector2(0f, 0.55f * u), e.Points, 64f);
                    GameAudio.Sfx("crash", 0.8f);
                    break;
                case EventKind.Pop:
                    Fx.ConfettiBurst(at, s, 24);
                    PointsPopup(at + new Vector2(0f, 0.3f * u), e.Points, 72f);
                    GameAudio.Sfx("arcade_pop");
                    break;
            }
        }
    }

    // ------------------------------------------------------------------------------------------------ Zogu!

    public sealed class FlapView : MachineView
    {
        readonly FlapRules _rules;
        readonly Vector2[] _stars = new Vector2[70];
        public override MachineRules Rules => _rules;
        public override bool UsesPicture => false;
        const float Bottom = 110f, Span = 880f;

        public FlapView(FlapRules rules, ArcadeArt art, ArcadeStage stage, ArcadeFx fx) : base(art, stage, fx)
        {
            _rules = rules;
            var rng = new System.Random(3);
            for (int i = 0; i < _stars.Length; i++) _stars[i] = new Vector2((float)rng.NextDouble(), 0.35f + 0.65f * (float)rng.NextDouble());
        }

        /// <summary>A point of the sky (distance, height 0..1) on the screen.</summary>
        Vector2 SkyAt(float x, float y) => new Vector2((x - _rules.Distance + FlapRules.BirdX) * Span, Bottom + y * Span);

        public override void Draw(SpriteBatch back, SpriteBatch glow, float time, float dt)
        {
            float w = Stage.GuiWidth;
            var white = Art["white"];
            back.Gradient(white, new Rect(0f, 0f, w, ArcadeStage.HudHeight), new Color(0.24f, 0.15f, 0.42f), new Color(0.04f, 0.06f, 0.17f));
            foreach (var st in _stars)
            {
                float tw = 0.5f + 0.5f * Mathf.Sin(time * 2.3f + st.x * 40f);
                glow.Draw(Art["spark"], new Vector2(st.x * w, st.y * 1080f), 7f + 6f * tw, 0f, new Color(1f, 1f, 1f, 0.35f + 0.5f * tw));
            }
            back.Draw(Art["moon"], new Vector2(w - 230f, 900f), 150f);
            float d = _rules.Distance;
            // far away: the ferris wheel and the tents drifting slowly by (parallax), the string lights closer
            float wheelX = Repeat(900f - d * 70f, w + 900f) - 450f;
            back.Draw(Art["wheel_rim"], new Vector2(wheelX, 560f), 300f, time * 8f, new Color(0.55f, 0.5f, 0.85f, 0.55f));
            back.Draw(Art["wheel_legs"], new Vector2(wheelX, 560f), 300f * 228f / 312f * 1.2f, 0f, new Color(0.45f, 0.4f, 0.75f, 0.55f));
            for (int i = 0; i < 4; i++)
            {
                float x = Repeat(i * 620f - d * 150f, w + 700f) - 350f;
                back.Draw(Art["tent"], new Vector2(x, Bottom + 20f), 75f + 10f * (i % 2), 0f, new Color(0.45f, 0.35f, 0.6f));
            }
            back.Gradient(white, new Rect(0f, 0f, w, Bottom + 40f), new Color(0.08f, 0.07f, 0.18f), new Color(0.16f, 0.12f, 0.3f));
            for (int i = 0; i < 18; i++)
            {
                float x = Repeat(i * 140f - d * 330f, w + 140f) - 70f;
                bool on = ((int)(time * 4f) + i) % 3 != 0;
                back.Draw(Art[on ? "bulb_on" : "bulb_off"], new Vector2(x, 1040f - 22f * Mathf.Sin((x / 280f) * Mathf.PI) * Mathf.Sin((x / 280f) * Mathf.PI)), 18f);
            }
            foreach (var t in _rules.Things)
            {
                var at = SkyAt(t.X, t.Y);
                if (at.x < -150f || at.x > w + 150f) continue;
                float fade = t.Done ? Mathf.Clamp01(1f - t.DoneAge / 0.25f) : 1f;
                switch (t.Kind)
                {
                    case FlapRules.ThingKind.Star:
                        if (t.Done) break;
                        glow.Draw(Art["glow"], at, 70f, 0f, new Color(1f, 0.85f, 0.4f, 0.45f));
                        back.Draw(Art["star"], at, 65f, Mathf.Sin(time * 3f + t.X * 9f) * 15f);
                        break;
                    case FlapRules.ThingKind.Balloon:
                        if (!t.Done) back.Draw(Art["balloon"], at + new Vector2(0f, Mathf.Sin(t.Phase * 2f) * 6f), 85f, Mathf.Sin(t.Phase) * 8f, CatchViewBalloon(t.Variant));
                        break;
                    case FlapRules.ThingKind.Ring:
                        glow.Draw(Art["glow"], at, 95f, 0f, new Color(1f, 0.8f, 0.3f, t.Done ? 0.6f * fade : 0.25f));
                        back.Draw(Art["hoop"], at, 92f * (t.Done ? 1f + (1f - fade) * 0.4f : 1f), 0f, new Color(1f, 1f, 1f, fade));
                        break;
                    default:
                        back.Draw(Art["storm"], at, 100f, Mathf.Sin(t.Phase * 1.3f) * 3f);
                        break;
                }
            }
            for (int p = 0; p < _rules.Players; p++)
            {
                var bird = _rules.Birds[p];
                var at = SkyAt(_rules.BirdAt(p), bird.Y);
                string tag = p == 0 ? "c" : "s";
                float fa = bird.FlapAge;
                string wing = fa < 0.1f ? "down" : fa < 0.2f ? "mid" : fa < 0.32f ? "up" : bird.Vy > 0.05f ? "up" : "mid";
                float angle = bird.Stunned > 0f ? bird.Stunned * 900f : bird.Tilt;
                back.Draw(Art[$"bird_{tag}_{wing}"], at, 79f, angle);
                if (_rules.Players == 2)
                    ArcadeFx.Number(back, Art, (p + 1).ToString(), at + new Vector2(0f, 95f), 42f, PlayerColour(p));
            }
        }

        static Color CatchViewBalloon(int v) => v switch { 0 => Coral, 1 => Sky, 2 => Sun, _ => Grape };

        static float Repeat(float x, float length) => Mathf.Repeat(x, length);

        public override void OnEvent(ArcadeEvent e)
        {
            var at = SkyAt(e.At.x, e.At.y);
            switch (e.Kind)
            {
                case EventKind.Flap:
                    Fx.Puff(at + new Vector2(-40f, -20f), 0.45f, new Color(1f, 1f, 1f, 0.5f), 2);
                    GameAudio.Sfx("arcade_flap", 0.7f, 1f, 0.1f);
                    break;
                case EventKind.Star:
                    Fx.Twinkle(at, 40f, Sun, 4);
                    PointsPopup(at + new Vector2(0f, 60f), e.Points, 48f);
                    GameAudio.Sfx("kart_star", 0.7f, 1f + 0.03f * Mathf.Min(e.Combo % 10, 8));
                    break;
                case EventKind.Pop:
                    Fx.ConfettiBurst(at, 0.6f, 20);
                    PointsPopup(at + new Vector2(0f, 60f), e.Points, 56f);
                    GameAudio.Sfx("arcade_pop");
                    break;
                case EventKind.Ring:
                    Fx.Burst(at, 0.8f, Sun, 16);
                    PointsPopup(at + new Vector2(0f, 70f), e.Points, 64f);
                    GameAudio.Sfx("kart_trick", 0.8f);
                    break;
                case EventKind.Bump:
                    Fx.Dizzy(at + new Vector2(0f, 50f), 40f);
                    Fx.Puff(at, 0.6f, new Color(0.6f, 0.55f, 0.8f, 0.8f), 4);
                    GameAudio.Sfx("kart_bump");
                    break;
            }
        }
    }

    // ------------------------------------------------------------------------------------------------ Topi!

    public sealed class KeepUpView : MachineView
    {
        readonly KeepUpRules _rules;
        public override MachineRules Rules => _rules;

        public KeepUpView(KeepUpRules rules, ArcadeArt art, ArcadeStage stage, ArcadeFx fx) : base(art, stage, fx) => _rules = rules;

        public override void Draw(SpriteBatch back, SpriteBatch glow, float time, float dt)
        {
            for (int p = 0; p < _rules.Players; p++)
            {
                float u = U(p);
                var ball = _rules.Balls[p];
                if (!ball.Live)
                {
                    // where the next one drops: a twinkle at the top
                    var top = W(p, new Vector2(Stage.Bodies[p].Present ? Mathf.Clamp(Stage.Bodies[p][Part.Head].x, -1.4f, 1.4f) : 0f, KeepUpRules.Top - 0.3f));
                    glow.Draw(Art["spark"], top, u * (0.25f + 0.08f * Mathf.Sin(time * 10f)), time * 120f, Sun);
                    continue;
                }
                // a shadow on the floor under the ball (smaller and fainter the higher it is)
                float height = Mathf.Clamp01((ball.Pos.y + 1.85f) / 5f);
                var floor = W(p, new Vector2(ball.Pos.x, -1.85f));
                back.Draw(Art["dot"], floor, u * 0.5f * (1f - 0.5f * height), 0f, new Color(0f, 0f, 0f, 0.35f * (1f - height)), 1.6f, 0.5f);
                var at = W(p, ball.Pos);
                switch (ball.Kind)
                {
                    case KeepUpRules.BallKind.Balloon:
                        back.Draw(Art["balloon"], at, u * ball.Radius / 0.78f, ball.Angle * 0.15f, p == 0 ? Coral : Sky);
                        break;
                    case KeepUpRules.BallKind.Beach:
                        back.Draw(Art["beachball"], at, u * ball.Radius / 0.88f, ball.Angle);
                        break;
                    default:
                        back.Draw(Art["football"], at, u * ball.Radius / 0.88f, ball.Angle);
                        break;
                }
            }
        }

        public override void OnEvent(ArcadeEvent e)
        {
            float u = U(e.Player), s = u / 260f;
            var at = W(e.Player, e.At);
            switch (e.Kind)
            {
                case EventKind.Touch:
                    Fx.Puff(at, s * 0.7f, new Color(1f, 1f, 1f, 0.7f), 3);
                    Fx.Burst(at, s * 0.7f, e.Variant == (int)Part.Head ? Sun : Color.white, e.Variant == (int)Part.Head ? 10 : 6);
                    PointsPopup(at + new Vector2(0f, 0.5f * u), e.Points);
                    var ball = _rules.Balls[e.Player];
                    GameAudio.Sfx(ball.Kind == KeepUpRules.BallKind.Balloon ? "arcade_boing" : "sport_bump", 0.85f, ball.Kind == KeepUpRules.BallKind.Football ? 1.1f : 1f);
                    if (e.Combo >= 5 && e.Combo % 5 == 0) Fx.Points(at + new Vector2(0f, 0.95f * u), "x" + e.Combo, Sky, 72f);
                    break;
                case EventKind.Drop:
                    Fx.Puff(at, s, new Color(0.9f, 0.85f, 0.8f, 0.7f), 5);
                    GameAudio.Sfx("sport_thud", 0.7f);
                    break;
                case EventKind.NewBall:
                    GameAudio.Sfx("ready", 0.6f);
                    break;
            }
        }
    }
}
