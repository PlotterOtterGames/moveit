using System;
using System.Collections.Generic;
using MoveIt.Motion;
using UnityEngine;

namespace MoveIt.Games.Runner
{
    /// <summary>
    /// Vrapo!'s HUD (the console polish of 2026-11-18): pictures from tools/runner/art.py's sheet and outlined words, all
    /// in one batch (<see cref="HudBatch"/>: one draw call). The title: the logo, a line under it, the best score, the
    /// start prompt on a pill. The run: the eggs counted top left on a pill (each egg picked up flies into it and it
    /// bumps), the distance under it, the score top right with a race-to-your-best bar (Dino's head running toward the
    /// trophy), banners (milestones, the new record, GO), the moves drawn over the obstacles while learning. The
    /// countdown's numbers on a turning sunburst. The results: a card with the distance, the eggs, the score and the best,
    /// the record's ribbon. The placement prompts and warnings stay PlayerGate's (IMGUI, after the batch).
    /// </summary>
    public sealed partial class RunnerGame
    {
        HudBatch _hud;
        Dictionary<string, Rect> _sheet;
        static readonly Color Night = new Color(0.08f, 0.1f, 0.22f, 0.74f);
        static readonly Color Gold = new Color(1f, 0.82f, 0.3f);
        static readonly Color Cream = new Color(1f, 0.97f, 0.9f);
        const float PanelCorner = 40f;

        struct Flyer
        {
            public Vector2 From;
            public float Born;
            public bool Golden;
        }

        readonly List<Flyer> _flyers = new List<Flyer>();
        float _counterBump;
        string _banner;
        bool _bannerGold;
        float _bannerBorn = -10f, _bannerLife = 2f;
        string _eggText, _scoreText, _distText;
        int _eggShown = -1, _scoreShown = -1, _distShown = -1;

        void SetupHud()
        {
            _sheet = Sheet(hudJson);
            if (hudSheet != null) _hud = new HudBatch(hudSheet, hudSheet);
        }

        void DisposeHud() => _hud?.Dispose();

        void ClearHud()
        {
            _flyers.Clear();
            _banner = null;
        }

        void ShowBanner(string text, bool gold, float life = 2.1f)
        {
            _banner = text;
            _bannerGold = gold;
            _bannerBorn = Time.unscaledTime;
            _bannerLife = life;
        }

        void FlyEgg(Vector3 world, bool golden)
        {
            if (cam == null) return;
            var s = cam.WorldToScreenPoint(world);
            if (s.z <= 0f) return;
            _flyers.Add(new Flyer { From = GameHud.FromScreen(s), Born = Time.unscaledTime, Golden = golden });
        }

        // ------------------------------------------------------------------------------------------------ drawing

        void Picture(Rect r, string name, Color tint, float degrees = 0f)
        {
            if (_hud == null || _sheet == null || !_sheet.TryGetValue(name, out var uv)) return;
            _hud.Image(r, uv, tint, degrees);
        }

        void Nine(Rect r, string name, float corner, Color tint)
        {
            if (_hud == null || _sheet == null || !_sheet.TryGetValue(name, out var uv)) return;
            float uvx = uv.width * (corner / Mathf.Max(1f, uv.width * hudSheet.width));
            float uvy = uv.height * (corner / Mathf.Max(1f, uv.height * hudSheet.height));
            _hud.Nine(r, Mathf.Min(corner, Mathf.Min(r.width, r.height) * 0.5f), uv, new Vector2(uvx, uvy), tint);
        }

        void Words(Rect r, string text, int size, Color colour, TextAnchor anchor = TextAnchor.MiddleCenter, float outline = 3f, float grow = 1f)
        {
            if (_hud != null) SportHud.Text(_hud, r, text, size, colour, anchor, outline, null, true, grow);
            else SportHud.Text(r, text, size, colour, anchor, outline);
        }

        /// <summary>A picture's aspect (width / height) in the sheet.</summary>
        float Aspect(string name) => _sheet != null && hudSheet != null && _sheet.TryGetValue(name, out var uv)
            ? uv.width * hudSheet.width / Mathf.Max(1f, uv.height * hudSheet.height) : 1f;

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            useGUILayout = false;
            float w = GameHud.Begin();
            _hud?.Clear();
            switch (_state)
            {
                case State.Waiting: DrawTitle(w); break;
                case State.Countdown: DrawCountdown(w); break;
                case State.Running:
                    if (!_paused) DrawRun(w);
                    break;
                case State.Crashed:
                    DrawRun(w);
                    break;
                case State.GameOver: DrawResults(w); break;
            }
            _hud?.Draw(GUI.matrix);
            // the gate's prompts and warnings (IMGUI, over the batch)
            if (_state == State.Running && _paused) PlayerGate.DrawPaused(w, _input);
            else if (_state == State.Running) PlayerGate.DrawEdgeWarning(w, _input);
        }

        // ------------------------------------------------------------------------------------------------ the title

        void DrawTitle(float w)
        {
            float t = Time.unscaledTime;
            // the logo, top left (Dino stands right of the middle), breathing a little; the line under it
            float lw = 820f, lh = lw / Aspect("logo");
            float k = 1f + 0.015f * Mathf.Sin(t * 2.2f);
            float lx = Mathf.Max(40f, w * 0.27f - lw * 0.5f);
            Picture(new Rect(lx - (k - 1f) * lw * 0.5f, 36f - (k - 1f) * lh * 0.5f, lw * k, lh * k), "logo", Color.white);
            Words(new Rect(lx, 36f + lh - 14f, lw, 60f), RunnerStrings.Tagline, 42, Cream, TextAnchor.MiddleCenter, 3f);
            // the best score, bottom left
            if (_best > 0)
            {
                var pill = new Rect(40f, 960f, 380f, 80f);
                Nine(pill, "capsule", 40f, Night);
                Picture(new Rect(pill.x + 12f, pill.y + 8f, 64f, 64f), "trophy", Color.white);
                Words(new Rect(pill.x + 84f, pill.y, pill.width - 96f, pill.height), $"{CommonStrings.Best}: {_best}", 38, Gold, TextAnchor.MiddleLeft);
            }
            // the start prompt on a pill, lower middle (the gate's text: step in, hold still, raise your hands...)
            var prompt = PlayerGate.Waiting(_input, RunnerStrings.KeyboardHelp);
            if (_input != null && _input.Gestures != null) _input.Gestures.PlacementShownFrame = Time.frameCount;
            if (prompt.Title != null)
            {
                float pulse = 1f + 0.03f * Mathf.Sin(t * 4f);
                float tw = Mathf.Min(w - 120f, SportHud.Width(prompt.Title, 50) + 120f);
                float cx = Mathf.Max(tw * 0.5f + 40f, w * 0.36f);
                var pill = new Rect(cx - tw * 0.5f * pulse, 812f, tw * pulse, 100f);
                Nine(pill, "capsule", 50f, Night);
                Words(pill, prompt.Title, 50, prompt.Colour == Color.white ? Color.white : prompt.Colour);
                if (!string.IsNullOrEmpty(prompt.Subtitle))
                    Words(new Rect(cx - 600f, 918f, 1200f, 50f), prompt.Subtitle, 32, Cream, TextAnchor.MiddleCenter, 2.5f);
                if (prompt.Chevrons != 0)
                    Words(new Rect(prompt.Chevrons < 0 ? 60f : w - 260f, 330f, 200f, 200f), prompt.Chevrons < 0 ? "<<" : ">>", 120,
                          prompt.Colour, TextAnchor.MiddleCenter, 5f);
            }
        }

        // ------------------------------------------------------------------------------------------------ the countdown

        void DrawCountdown(float w)
        {
            DrawRun(w, counters: false);
            int n = Mathf.CeilToInt(CountdownSeconds - _stateTime);
            if (n <= 0) return;
            float into = 1f - (CountdownSeconds - _stateTime - (n - 1));     // 0..1 through this number
            float pop = into < 0.18f ? Mathf.SmoothStep(0.4f, 1.15f, into / 0.18f) : Mathf.Lerp(1.15f, 1f, Mathf.Clamp01((into - 0.18f) / 0.2f));
            float fade = into > 0.8f ? 1f - (into - 0.8f) / 0.2f : 1f;
            var c = new Vector2(w * 0.5f, 430f);
            float s = 300f * pop;
            var tint = n == 3 ? new Color(1f, 0.42f, 0.36f, 0.9f * fade) : n == 2 ? new Color(1f, 0.62f, 0.22f, 0.9f * fade) : new Color(1f, 0.82f, 0.25f, 0.9f * fade);
            Picture(new Rect(c.x - s * 0.5f, c.y - s * 0.5f, s, s), "burst", tint, Time.unscaledTime * 40f);
            Words(new Rect(c.x - 200f, c.y - 120f, 400f, 240f), n.ToString(), 180, new Color(1f, 1f, 1f, fade), TextAnchor.MiddleCenter, 7f, pop);
            Words(new Rect(0f, c.y + 170f, w, 60f), RunnerStrings.Ready, 44, new Color(1f, 0.97f, 0.9f, fade), TextAnchor.MiddleCenter, 3f);
        }

        // ------------------------------------------------------------------------------------------------ the run

        void DrawRun(float w, bool counters = true)
        {
            float now = Time.unscaledTime;
            if (counters)
            {
                // the eggs, top left: a pill with the egg; it bumps as each egg flies in
                _counterBump = Mathf.MoveTowards(_counterBump, 0f, Time.unscaledDeltaTime * 4f);
                int eggs = _eggs + _golden;
                if (eggs != _eggShown) { _eggShown = eggs; _eggText = eggs.ToString(); }
                var pill = new Rect(34f, 28f, 270f, 92f);
                Nine(pill, "capsule", 46f, Night);
                float bump = 1f + 0.35f * _counterBump;
                Picture(new Rect(pill.x + 14f + 34f * (1f - bump), pill.y + 8f + 38f * (1f - bump), 76f * bump, 76f * bump), "egg", Color.white);
                Words(new Rect(pill.x + 100f, pill.y, pill.width - 116f, pill.height), _eggText, 56, Color.white, TextAnchor.MiddleLeft, 3.5f, 1f + 0.18f * _counterBump);
                // the distance under it
                int metres = Mathf.FloorToInt(_distance);
                if (metres != _distShown) { _distShown = metres; _distText = RunnerStrings.Metres(metres); }
                Picture(new Rect(44f, 128f, 46f, 46f), "footprint", Color.white);
                Words(new Rect(98f, 124f, 300f, 54f), _distText, 36, Cream, TextAnchor.MiddleLeft, 3f);
                // the score, top right, and the race to the best
                int score = Score;
                if (score != _scoreShown) { _scoreShown = score; _scoreText = score.ToString(); }
                var panel = new Rect(w - 34f - 380f, 28f, 380f, 92f);
                Nine(panel, "capsule", 46f, Night);
                Words(new Rect(panel.x + 24f, panel.y, panel.width - 48f, panel.height), _scoreText, 60, Color.white, TextAnchor.MiddleRight, 3.5f);
                Words(new Rect(panel.x + 26f, panel.y, 200f, panel.height), CommonStrings.Score.ToUpperInvariant(), 26, Gold, TextAnchor.MiddleLeft, 2f);
                if (_best > 0)
                {
                    var track = new Rect(panel.x + 30f, panel.yMax + 18f, panel.width - 92f, 14f);
                    float k = Mathf.Clamp01((float)score / _best);
                    Nine(track, "capsule", 7f, new Color(0.08f, 0.1f, 0.22f, 0.6f));
                    Nine(new Rect(track.x, track.y, Mathf.Max(14f, track.width * k), track.height), "capsule", 7f, _passedBest ? Gold : new Color(0.45f, 0.86f, 0.5f));
                    Picture(new Rect(track.xMax + 6f, track.y - 22f, 52f, 52f), "trophy", Color.white);
                    float hx = track.x + track.width * k;
                    Picture(new Rect(hx - 28f, track.y - 22f + 2f * Mathf.Sin(now * 12f), 56f, 56f), "dino_head", Color.white);
                }
            }
            // eggs flying into the counter
            for (int i = _flyers.Count - 1; i >= 0; i--)
            {
                var f = _flyers[i];
                float age = (now - f.Born) / 0.5f;
                if (age >= 1f)
                {
                    _flyers.RemoveAt(i);
                    _counterBump = 1f;
                    continue;
                }
                var to = new Vector2(34f + 52f, 28f + 46f);
                float e = age * age * (3f - 2f * age);
                var p = Vector2.Lerp(f.From, to, e) + new Vector2(0f, -140f * Mathf.Sin(age * Mathf.PI));
                float s = Mathf.Lerp(f.Golden ? 96f : 70f, 54f, e);
                Picture(new Rect(p.x - s * 0.5f, p.y - s * 0.6f, s, s * 1.15f), f.Golden ? "egg_gold" : "egg", Color.white, age * 360f);
                if (f.Golden && age < 0.6f)
                    Words(new Rect(f.From.x - 200f, f.From.y - 110f - 60f * age, 400f, 70f), $"+50  {RunnerStrings.Golden}", 40, Gold, TextAnchor.MiddleCenter, 3f);
            }
            DrawBanner(w);
            if (_state == State.Running && _runTime < hintSeconds) DrawHint();
        }

        void DrawBanner(float w)
        {
            if (_banner == null) return;
            float age = Time.unscaledTime - _bannerBorn;
            if (age > _bannerLife)
            {
                _banner = null;
                return;
            }
            float inK = Mathf.SmoothStep(0f, 1f, age / 0.25f);
            float outK = age > _bannerLife - 0.3f ? (_bannerLife - age) / 0.3f : 1f;
            float s = Mathf.Lerp(0.6f, 1f, inK) * (1f + 0.05f * Mathf.Sin(age * 10f) * (1f - inK));
            var a = new Color(1f, 1f, 1f, outK);
            float bw = 680f * s, bh = bw / Aspect("ribbon");
            float y = 196f;
            if (_bannerGold || _banner == CommonStrings.Go)
            {
                Picture(new Rect(w * 0.5f - bw * 0.5f, y - bh * 0.5f, bw, bh), "ribbon", a);
                Words(new Rect(w * 0.5f - bw * 0.5f, y - bh * 0.45f, bw, bh * 0.75f), _banner, Mathf.RoundToInt(58 * s), new Color(1f, 1f, 1f, outK), TextAnchor.MiddleCenter, 4f);
            }
            else
            {
                Words(new Rect(0f, y - 60f, w, 120f), _banner, Mathf.RoundToInt(76 * s), new Color(1f, 0.92f, 0.5f, outK), TextAnchor.MiddleCenter, 5f);
            }
        }

        /// <summary>While learning: the move the next obstacle in the runner's lane wants, drawn over it.</summary>
        void DrawHint()
        {
            if (cam == null) return;
            int lane = Mathf.RoundToInt(player.X / LaneWidth);
            Item nearest = null;
            float nz = 0f;
            foreach (var item in _items)
            {
                var rule = item.Spawn.Rule;
                if (item.Done || rule is Rule.Egg or Rule.GoldEgg or Rule.Sight) continue;
                if (!item.Spawn.Wide && item.Spawn.Lane != lane) continue;
                float z = item.Spawn.At - _distance - item.Depth / 2f;
                if (z < 0f || z / Mathf.Max(1f, _speed) > 1.5f) continue;
                if (nearest == null || z < nz)
                {
                    nearest = item;
                    nz = z;
                }
            }
            if (nearest == null) return;
            var r = nearest.Spawn.Rule;
            float top = r == Rule.Dodge ? 3.0f : r == Rule.Duck ? 2.4f : 1.5f;
            var world = new Vector3(nearest.Spawn.Wide ? player.X : nearest.Spawn.Lane * LaneWidth, top, nz);
            float d = Mathf.Max(0f, world.z - cam.transform.position.z);
            world.y -= RunnerWorld.Curve * d * d;
            var s = cam.WorldToScreenPoint(world);
            if (s.z <= 0f) return;
            var p = GameHud.FromScreen(s);
            string icon = r == Rule.Jump ? "hint_jump" : r == Rule.Duck ? "hint_duck" : "hint_side";
            string word = r == Rule.Jump ? RunnerStrings.Jump : r == Rule.Duck ? RunnerStrings.Duck : RunnerStrings.Switch;
            var tint = r == Rule.Jump ? new Color(1f, 0.85f, 0.3f) : r == Rule.Duck ? new Color(0.45f, 0.85f, 1f) : new Color(1f, 0.55f, 0.45f);
            float pulse = 1f + 0.08f * Mathf.Sin(Time.unscaledTime * 10f);
            float size = Mathf.Clamp(260f - nz * 3f, 130f, 230f) * pulse;
            Picture(new Rect(p.x - size * 0.5f, p.y - size, size, size), icon, tint);
            Words(new Rect(p.x - 250f, p.y - size - 64f, 500f, 64f), word, 50, tint, TextAnchor.MiddleCenter, 4f);
        }

        // ------------------------------------------------------------------------------------------------ the results

        void DrawResults(float w)
        {
            float age = _stateTime;
            float inK = Mathf.SmoothStep(0f, 1f, (age - 0.15f) / 0.45f);
            if (inK <= 0f) return;
            // the card, left of middle (Dino sits dizzy or dances on the right)
            float cw = 640f, ch = 560f;
            var card = new Rect(Mathf.Lerp(-cw, w * 0.5f - cw - 40f, inK), 250f, cw, ch);
            Nine(new Rect(card.x + 8f, card.y + 14f, card.width, card.height), "panel", PanelCorner, new Color(0f, 0f, 0.1f, 0.35f));
            Nine(card, "panel", PanelCorner, new Color(0.1f, 0.12f, 0.27f, 0.9f));
            // the ribbon over it: a new record, or well run
            float bw = cw + 120f, bh = bw / Aspect("ribbon");
            Picture(new Rect(card.center.x - bw * 0.5f, card.y - bh * 0.55f, bw, bh), "ribbon", _newBest ? Color.white : new Color(0.75f, 0.9f, 1f));
            Words(new Rect(card.center.x - bw * 0.5f, card.y - bh * 0.5f, bw, bh * 0.78f), _newBest ? CommonStrings.NewBest : RunnerStrings.Result, 54, Color.white, TextAnchor.MiddleCenter, 4f);
            float y = card.y + 92f;
            Row(card, ref y, "footprint", RunnerStrings.Distance, RunnerStrings.Metres(Mathf.FloorToInt(_distance)));
            Row(card, ref y, "egg", RunnerStrings.Eggs, (_eggs + _golden).ToString());
            if (_golden > 0) Row(card, ref y, "egg_gold", RunnerStrings.Golden.TrimEnd('!'), _golden.ToString());
            // the score, big
            Words(new Rect(card.x, y + 6f, card.width, 50f), CommonStrings.Score.ToUpperInvariant(), 34, Gold, TextAnchor.MiddleCenter, 2.5f);
            float pop = _newBest ? 1f + 0.06f * Mathf.Sin(age * 6f) : 1f;
            Words(new Rect(card.x, y + 50f, card.width, 120f), Score.ToString(), 110, Color.white, TextAnchor.MiddleCenter, 5f, pop);
            Picture(new Rect(card.x + 40f, card.yMax - 84f, 60f, 60f), "trophy", Color.white);
            Words(new Rect(card.x + 112f, card.yMax - 90f, card.width - 140f, 70f), $"{CommonStrings.Best}: {_best}", 40, Gold, TextAnchor.MiddleLeft, 3f);
            if (age > 1.4f)
            {
                string again = PlayerGate.PlayAgainText(_input);
                float tw = Mathf.Min(w - 120f, SportHud.Width(again, 44) + 100f);
                var pill = new Rect(w * 0.5f - tw * 0.5f, 900f, tw, 90f);
                Nine(pill, "capsule", 45f, Night);
                Words(pill, again, 44, Color.white, TextAnchor.MiddleCenter, 3f, 1f + 0.03f * Mathf.Sin(Time.unscaledTime * 4f));
            }
        }

        void Row(Rect card, ref float y, string icon, string label, string value)
        {
            Picture(new Rect(card.x + 40f, y + 4f, 58f, 58f), icon, Color.white);
            Words(new Rect(card.x + 112f, y, 300f, 66f), label, 38, Cream, TextAnchor.MiddleLeft, 2.5f);
            Words(new Rect(card.x + 300f, y, card.width - 340f, 66f), value, 46, Color.white, TextAnchor.MiddleRight, 3f);
            y += 72f;
        }

        // ------------------------------------------------------------------------------------------------ the sheet

        [Serializable]
        sealed class SheetJson
        {
            public int[] size;
            public SpriteJson[] sprites;
        }

        [Serializable]
        sealed class SpriteJson
        {
            public string name;
            public int[] rect;
        }

        /// <summary>The HUD sheet's pictures by name: uv rectangles (0..1, y up), like Zana!'s.</summary>
        static Dictionary<string, Rect> Sheet(TextAsset json)
        {
            var map = new Dictionary<string, Rect>();
            if (json == null) return map;
            var data = JsonUtility.FromJson<SheetJson>(json.text);
            if (data?.sprites == null || data.size == null || data.size.Length < 2) return map;
            float w = data.size[0], h = data.size[1];
            foreach (var s in data.sprites)
            {
                if (s.rect == null || s.rect.Length < 4) continue;
                float x = (s.rect[0] + 0.5f) / w, top = s.rect[1] + 0.5f, width = (s.rect[2] - 1f) / w, height = s.rect[3] - 1f;
                map[s.name] = new Rect(x, 1f - (top + height) / h, width, height / h);
            }
            return map;
        }
    }
}
