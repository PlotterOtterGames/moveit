using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Boxing
{
    /// <summary>
    /// Superboks!'s screens (2026-11-19 overhaul), on a 1080-high virtual screen, their pictures from tools/boxing's sheet
    /// in one draw (<see cref="HudBatch"/>), words in it too (IMGUI only for what the batch can't: the portraits, words with
    /// a ★):
    /// <list type="bullet">
    /// <item><b>the fight</b>: each boxer's face in a ring (the hero's blue, the opponent's red) beside its health in ten
    /// segments (Wii Sports Boxing's pie made a bar) and its knockdowns; the round and the clock in the middle; the super's
    /// power by a glove, bottom left (full: the little figure with both arms up); the calls in the lower third; the
    /// referee's count; a red flash when hit, speed lines on a dodge;</item>
    /// <item><b>the intro</b>: the stage's name, then the two faces and "VS";</item>
    /// <item><b>the map</b>: the title, the chosen fight (its stage, the opponent, what beats it, its stars), the rows, the
    /// climb's four places with their fights, the moves, what starts it;</item>
    /// <item><b>the results</b>: won or lost, how, the stars, the punches, what it opened.</item>
    /// </list>
    /// </summary>
    public sealed partial class BoxingGame
    {
        static readonly Color Night = SportHud.Night;
        static readonly Color Cream = new Color(1f, 0.95f, 0.86f);

        enum HudPass { All, Pictures, Words }
        HudPass _pass;
        HudBatch _hudBatch;
        Dictionary<string, Rect> _ui;
        Vector2 _uvPx;

        bool MakeHudBatch()
        {
            if (_hudBatch != null) return true;
            if (art.sheet == null || art.sheetJson == null) return false;
            _ui = BoxingFx.Sheet(art.sheetJson);
            _uvPx = new Vector2(1f / art.sheet.width, 1f / art.sheet.height);
            _hudBatch = new HudBatch(art.sheet, art.sheet);
            return true;
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;   // nothing here takes input: only the repaint draws
            float w = GameHud.Begin();
            if (!MakeHudBatch()) return;
            bool batch = PerfSwitches.Get("hudbatch") != "0";
            if (batch)
            {
                _hudBatch.Clear();
                _pass = HudPass.Pictures;
                Screens(w);
                _hudBatch.Draw(GUI.matrix);
                _pass = HudPass.Words;
            }
            else _pass = HudPass.All;
            Screens(w);
            _pass = HudPass.All;
        }

        void Screens(float w)
        {
            switch (Current)
            {
                case Phase.Waiting: DrawMap(w); break;
                case Phase.Intro: DrawIntro(w); break;
                case Phase.Fight: DrawFight(w); break;
                case Phase.Result: DrawResult(w); break;
            }
        }

        // ------------------------------------------------------------------ pieces

        /// <summary>A picture from the sheet (the batch pass), or IMGUI's when not batching.</summary>
        void Pic(string name, Rect r, Color tint, float degrees = 0f)
        {
            if (!_ui.TryGetValue(name, out var uv)) return;
            if (_pass == HudPass.Pictures) _hudBatch.Image(r, uv, tint, degrees);
            else if (_pass == HudPass.All)
            {
                var old = GUI.color;
                GUI.color = tint;
                GUI.DrawTextureWithTexCoords(r, art.sheet, uv);
                GUI.color = old;
            }
        }

        /// <summary>A rounded panel (the sheet's in nine pieces).</summary>
        void Panel(Rect r, Color tint, float corner = 24f)
        {
            if (!_ui.TryGetValue("panel", out var uv)) return;
            var border = new Vector2(40f * _uvPx.x, 40f * _uvPx.y);
            if (_pass == HudPass.Pictures) _hudBatch.Nine(r, Mathf.Min(corner, Mathf.Min(r.width, r.height) * 0.5f), uv, border, tint);
            else if (_pass == HudPass.All) SportHud.Rounded(r, tint, corner);
        }

        /// <summary>Words into the batch, or IMGUI's: when not batching, and words with a ★.</summary>
        void Words(Rect r, string text, int size, Color colour, TextAnchor anchor, float outline, bool imgui = false, float grow = 1f)
        {
            imgui |= text.IndexOf('★') >= 0;
            if (_pass == HudPass.Pictures) { if (!imgui) SportHud.Text(_hudBatch, r, text, size, colour, anchor, outline, grow: grow); }
            else if (_pass == HudPass.All || imgui) SportHud.Text(r, text, Mathf.RoundToInt(size * grow), colour, anchor, outline);
        }

        /// <summary>A boxer's face in its ring (IMGUI: a render texture can't join the batch).</summary>
        void Face(Vector2 centre, float radius, int index, Color ring)
        {
            Pic("disc", new Rect(centre.x - radius, centre.y - radius, radius * 2f, radius * 2f), new Color(Night.r, Night.g, Night.b, 0.92f));
            Pic("ring", new Rect(centre.x - radius * 1.12f, centre.y - radius * 1.12f, radius * 2.24f, radius * 2.24f), ring);
            if (_pass == HudPass.Pictures) return;
            var tex = _portraits?[index];
            if (tex == null || portraitMaterial == null) return;
            var r = new Rect(centre.x - radius * 0.94f, centre.y - radius * 0.94f, radius * 1.88f, radius * 1.88f);
            Graphics.DrawTexture(r, tex, portraitMaterial);
        }

        // ------------------------------------------------------------------ the fight

        void DrawFight(float w)
        {
            if (_bout == null) return;
            if (_hurtFlash > 0.01f) Pic("panel", new Rect(-60f, -60f, w + 120f, GameHud.Height + 120f), new Color(0.9f, 0.1f, 0.15f, _hurtFlash * 0.28f));
            if (_dodgeFlash > 0.01f) DodgeLines(w);
            DrawPlates(w);
            DrawClock(w);
            DrawPower();
            DrawPopups();
            if (_bout.Phase == BoutPhase.Down) DrawCount(w);
            DrawCalls(w);
            if (_tip != null && Time.time < _tipUntil)
            {
                float tw = Mathf.Min(w - 120f, 24f * _tip.Length * 0.62f + 80f);
                Panel(new Rect(w / 2f - tw / 2f, 990f, tw, 62f), new Color(Night.r, Night.g, Night.b, 0.88f), 31f);
                Words(new Rect(w / 2f - tw / 2f, 990f, tw, 62f), _tip, 28, Sun, TextAnchor.MiddleCenter, 0f);
            }
            if (_pass == HudPass.Pictures) return;
            if (_paused) PlayerGate.DrawPaused(w, _input);
            else if (!UsingKeys) PlayerGate.DrawEdgeWarning(w, _input, 880f);
        }

        /// <summary>The two boxers' plates: the face, the name, the health in ten segments, the knockdowns.</summary>
        void DrawPlates(float w)
        {
            var o = _bout.Opponent;
            Plate(new Vector2(110f, 104f), false, BoxingStrings.Hero, null, _bout.HeroHealth / Bout.HeroMax, _bout.HeroKnockdowns, Sky, 0);
            Plate(new Vector2(w - 110f, 104f), true, o.Name.ToUpperInvariant(), string.IsNullOrEmpty(o.Title) ? null : o.Title,
                  _bout.Health / _bout.MaxHealth, _bout.Knockdowns, Coral, 1);
        }

        void Plate(Vector2 face, bool right, string name, string title, float health, int downs, Color colour, int index)
        {
            float dir = right ? -1f : 1f;
            const float barW = 470f, segW = 38f, segGap = 47f;
            float x0 = face.x + dir * 98f;
            // the panel behind the bar
            var panel = right ? new Rect(x0 - barW - 26f, 60f, barW + 56f, 92f) : new Rect(x0 - 30f, 60f, barW + 56f, 92f);
            Panel(panel, new Color(Night.r, Night.g, Night.b, 0.82f), 30f);
            Face(face, 70f, index, colour);
            // the name (and the title, gold, under it for the opponent)
            var nameRect = right ? new Rect(x0 - barW, 8f, barW, 48f) : new Rect(x0, 8f, barW, 48f);
            Words(nameRect, name, 34, Color.white, right ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft, 3f);
            // ten segments: full ones in the colour (lighter at the top), the one being lost part-full, the rest dark
            health = Mathf.Clamp01(health);
            bool low = health < 0.3f;
            bool blink = low && Mathf.Repeat(Time.time, 0.6f) < 0.3f;
            for (int i = 0; i < 10; i++)
            {
                float sx = right ? x0 - segW - i * segGap : x0 + i * segGap;
                var r = new Rect(sx, 72f, segW, 66f);
                float fill = Mathf.Clamp01(health * 10f - i);
                Pic("seg", r, new Color(0.05f, 0.06f, 0.14f, 0.7f));
                if (fill <= 0.01f) continue;
                var c = low ? Color.Lerp(Coral, Sun, 0.25f) : colour;
                if (blink) c = Color.Lerp(c, Color.white, 0.45f);
                var inner = new Rect(r.x + 4f, r.yMax - 4f - (r.height - 8f) * fill, r.width - 8f, (r.height - 8f) * fill);
                Pic("seg", inner, c);
                Pic("seg", new Rect(inner.x + 4f, inner.y + 3f, inner.width - 8f, Mathf.Min(inner.height * 0.35f, 18f)), new Color(1f, 1f, 1f, 0.35f));
            }
            // knockdowns: stars under the bar
            for (int i = 0; i < downs && i < 6; i++)
            {
                float sx = right ? x0 - 30f - i * 34f : x0 + i * 34f;
                Pic("star", new Rect(sx, 150f, 30f, 30f), Sun);
            }
            if (title != null)
            {
                var t = right ? new Rect(x0 - barW, 148f, barW - 6f - downs * 34f, 36f) : new Rect(x0 + downs * 34f, 148f, barW, 36f);
                Words(t, title, 24, Sun, right ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft, 2f);
            }
        }

        int _clockSecs = -1;
        string _clockText = "";

        /// <summary>The round and its clock in the middle, the bell's gold.</summary>
        void DrawClock(float w)
        {
            var r = new Rect(w / 2f - 104f, 24f, 208f, 108f);
            Panel(r, new Color(Night.r, Night.g, Night.b, 0.9f), 34f);
            Panel(new Rect(r.x + 6f, r.y + 6f, r.width - 12f, 34f), new Color(Sun.r, Sun.g, Sun.b, 0.95f), 17f);
            Words(new Rect(r.x, r.y + 6f, r.width, 34f), BoxingStrings.Round(_bout.Round), 24, Night, TextAnchor.MiddleCenter, 0f);
            int secs = Mathf.CeilToInt(_bout.RoundClock);
            if (secs != _clockSecs)
            {
                _clockSecs = secs;
                _clockText = $"{secs / 60}:{secs % 60:00}";
            }
            bool hurry = secs <= 10 && _bout.Phase == BoutPhase.Fighting;
            Words(new Rect(r.x, r.y + 40f, r.width, 64f), _clockText, 52, hurry ? Coral : Color.white, TextAnchor.MiddleCenter, 0f);
        }

        /// <summary>The super punch's power, bottom left: a glove and a gauge; full, it glows and the little figure shows
        /// how (both arms up).</summary>
        void DrawPower()
        {
            bool full = _bout.Power >= 1f;
            float pulse = full ? 0.5f + 0.5f * Mathf.Sin(Time.time * 8f) : 0f;
            var gauge = new Rect(150f, 968f, 380f, 46f);
            Panel(new Rect(gauge.x - 8f, gauge.y - 8f, gauge.width + 16f, gauge.height + 16f), new Color(Night.r, Night.g, Night.b, 0.85f), 31f);
            float k = Mathf.Clamp01(_bout.Power);
            if (k > 0.02f)
            {
                var fill = new Rect(gauge.x, gauge.y, Mathf.Max(gauge.height, gauge.width * k), gauge.height);
                Panel(fill, full ? Color.Lerp(Sun, Color.white, pulse * 0.6f) : Color.Lerp(Coral, Sun, k), 23f);
                Panel(new Rect(fill.x + 10f, fill.y + 5f, fill.width - 20f, 12f), new Color(1f, 1f, 1f, 0.32f), 6f);
            }
            for (int i = 1; i < 5; i++) Pic("seg", new Rect(gauge.x + gauge.width * i / 5f - 2f, gauge.y + 10f, 4f, gauge.height - 20f), new Color(1f, 1f, 1f, 0.22f));
            Pic("glove", new Rect(28f, 922f, 130f, 130f), full ? Color.Lerp(Sun, Color.white, pulse * 0.5f) : new Color(1f, 0.6f, 0.55f), -18f);
            Words(new Rect(gauge.x + 8f, gauge.y - 46f, 300f, 40f), "SUPER", 30, full ? Sun : Color.white, TextAnchor.MiddleLeft, 2.5f);
            if (full && _bout.Hero == HeroState.Fighting)
            {
                Pic("arms_up", new Rect(gauge.xMax + 24f, 920f - pulse * 10f, 120f, 120f), Sun);
                Words(new Rect(gauge.xMax + 150f, 950f, 560f, 70f), BoxingStrings.PowerFull, 32, Sun, TextAnchor.MiddleLeft, 3f);
            }
        }

        /// <summary>Speed lines from the sides for a moment after a dodge (Wii's slow motion).</summary>
        void DodgeLines(float w)
        {
            float a = _dodgeFlash;
            for (int i = 0; i < 7; i++)
            {
                float y = 180f + i * 120f + (i % 2) * 40f;
                float len = 380f + (i % 3) * 120f;
                float slide = (1f - a) * 160f;
                Pic("streak", new Rect(-60f + slide, y, len, 30f), new Color(0.85f, 1f, 0.9f, a * 0.65f));
                Pic("streak", new Rect(w + 60f - slide, y + 60f, -len, 30f), new Color(0.85f, 1f, 0.9f, a * 0.65f));
            }
        }

        void DrawPopups()
        {
            foreach (var p in _popups)
            {
                var screen = cam.WorldToScreenPoint(p.World);
                if (screen.z <= 0f) continue;
                var g = GameHud.FromScreen(screen);
                var c = p.Color;
                c.a = Mathf.Clamp01(1.3f - p.Age);
                float grow = p.Age < 0.12f ? Mathf.Lerp(1.6f, 1f, p.Age / 0.12f) : 1f;
                g.y = Mathf.Clamp(g.y, 220f, 880f);   // under the plates, over the power
                Words(new Rect(g.x - 300f, g.y - 36f, 600f, 72f), p.Text, 50, c, TextAnchor.MiddleCenter, 4f, grow: grow);
            }
        }

        /// <summary>The referee's count, big, on a burst over whoever is down (the side shots show the opponent on the
        /// screen's left, the hero on its right); the hero down: get up by punching (a bar of the punches needed).</summary>
        void DrawCount(float w)
        {
            if (_bout.Count > 0)
            {
                float since = (_bout.PhaseTime - Bout.FallTime) % (_bout.DownFighter == 0 ? _bout.Level.HeroCountInterval : BoxingLevel.CountInterval);
                float pop = Mathf.Lerp(1.25f, 1f, Mathf.Clamp01(since / 0.15f));
                var c = _bout.DownFighter == 0 ? Coral : Sun;
                float cx = _bout.DownFighter == 0 ? w * 0.7f : w * 0.3f;
                Pic("burst", new Rect(cx - 140f * pop, 340f - 140f * pop, 280f * pop, 280f * pop), new Color(c.r, c.g, c.b, 0.85f), Time.time * 20f);
                Words(new Rect(cx - 300f, 230f, 600f, 220f), _bout.Count.ToString(), Mathf.RoundToInt(160 * pop), Color.white, TextAnchor.MiddleCenter, 7f);
            }
            // under the hero on the canvas (the side shot shows it lying across the screen's middle)
            if (_bout.DownFighter != 0) return;
            Words(new Rect(0f, 770f, w, 110f), BoxingStrings.GetUp, 100, Color.white, TextAnchor.MiddleCenter, 6f);
            Words(new Rect(0f, 872f, w, 54f), BoxingStrings.GetUpHow, 38, Sun, TextAnchor.MiddleCenter, 3f);
            var r = new Rect(w / 2f - 300f, 936f, 600f, 46f);
            Panel(r, new Color(Night.r, Night.g, Night.b, 0.88f), 24f);
            float k = _bout.GetUpNeeded > 0 ? Mathf.Clamp01(_bout.GetUpProgress / (float)_bout.GetUpNeeded) : 0f;
            if (k > 0.02f) Panel(new Rect(r.x + 6f, r.y + 6f, Mathf.Max(36f, (r.width - 12f) * k), r.height - 12f), Color.Lerp(Coral, Leaf, k), 18f);
        }

        void DrawCalls(float w)
        {
            // one call at a time, the newest that has started, in the lower third over the see-through hero: the opponent's
            // face (its wind-ups) stays clear
            int newest = -1;
            for (int i = 0; i < _calls.Count; i++)
                if (Time.time >= _calls[i].at && (newest < 0 || _calls[i].at >= _calls[newest].at)) newest = i;
            if (newest < 0) return;
            var c = _calls[newest];
            float age = Time.time - c.at;
            float pop = age < 0.12f ? Mathf.Lerp(0.5f, 1.12f, age / 0.12f) : age < 0.22f ? Mathf.Lerp(1.12f, 1f, (age - 0.12f) / 0.1f) : 1f;
            float fade = Mathf.Clamp01((c.life - age) / 0.25f);
            // before the bell's fight, in the middle; in the fight, low over the see-through hero (the opponent's arms, its
            // wind-ups, stay clear)
            float y = _bout != null && _bout.Phase == BoutPhase.Starting || Current == Phase.Intro ? 420f : 790f;
            if (c.size >= 120)
                Pic("burst", new Rect(w / 2f - 260f * pop, y - 170f * pop + 40f, 520f * pop, 340f * pop), new Color(c.colour.r, c.colour.g, c.colour.b, 0.5f * fade));
            var col = Color.white;
            col.a = fade;
            Words(new Rect(0f, y - 20f, w, c.size * 1.3f), c.text, Mathf.RoundToInt(c.size * pop), col, TextAnchor.MiddleCenter, 7f);
            if (!string.IsNullOrEmpty(c.sub))
            {
                var sub = c.colour;
                sub.a = fade;
                Words(new Rect(0f, y + c.size * 1.05f, w, 60f), c.sub, 44, sub, TextAnchor.MiddleCenter, 4f);
            }
        }

        // ------------------------------------------------------------------ the intro

        void DrawIntro(float w)
        {
            var o = Campaign.Opponent(SelectedFight);
            float t = _phaseTime;
            if (t < 1.5f)
            {
                // the place: its name and its line, its icon
                float a = Mathf.Clamp01(t / 0.25f) * Mathf.Clamp01((1.5f - t) / 0.25f);
                var c = new Color(1f, 1f, 1f, a);
                Pic(StageIcon(o.Stage), new Rect(w / 2f - 70f, 90f, 140f, 140f), c);
                Words(new Rect(0f, 240f, w, 110f), BoxingStrings.Stage(o.Stage).ToUpperInvariant(), 96, new Color(Sun.r, Sun.g, Sun.b, a), TextAnchor.MiddleCenter, 6f);
                Words(new Rect(0f, 344f, w, 60f), BoxingStrings.StageLine(o.Stage), 40, c, TextAnchor.MiddleCenter, 3f);
                return;
            }
            if (t > 3.2f) return;
            // the two faces, "VS", the names
            float u = Mathf.Clamp01((t - 1.5f) / 0.25f) * Mathf.Clamp01((3.2f - t) / 0.25f);
            float slide = (1f - Mathf.Clamp01((t - 1.5f) / 0.3f)) * 400f;
            var white = new Color(1f, 1f, 1f, u);
            Face(new Vector2(260f - slide, 760f), 120f, 0, new Color(Sky.r, Sky.g, Sky.b, u));
            Face(new Vector2(w - 260f + slide, 760f), 120f, 1, new Color(Coral.r, Coral.g, Coral.b, u));
            Pic("vs", new Rect(w / 2f - 130f, 660f, 260f, 225f), white, Mathf.Sin(t * 6f) * 4f);
            Words(new Rect(60f, 890f, 400f, 60f), BoxingStrings.Hero, 40, white, TextAnchor.MiddleCenter, 3f);
            Words(new Rect(w - 520f, 890f, 520f, 60f), o.Name.ToUpperInvariant(), 44, white, TextAnchor.MiddleCenter, 3f);
            if (!string.IsNullOrEmpty(o.Title))
                Words(new Rect(w - 520f, 944f, 520f, 44f), o.Title, 32, new Color(Sun.r, Sun.g, Sun.b, u), TextAnchor.MiddleCenter, 2.5f);
            Words(new Rect(0f, 40f, w, 50f), $"{BoxingStrings.Stage(o.Stage).ToUpperInvariant()} · {BoxingStrings.FightOf(SelectedFight + 1, Campaign.Fights)}", 32,
                  new Color(Cream.r, Cream.g, Cream.b, u), TextAnchor.MiddleCenter, 3f);
            if (o.Champion)
                Words(new Rect(0f, 88f, w, 60f), BoxingStrings.ChampionFight, 40, new Color(Sun.r, Sun.g, Sun.b, u), TextAnchor.MiddleCenter, 3f);
        }

        static string StageIcon(int stage) => stage switch { 0 => "stage_gym", 1 => "stage_roof", 2 => "stage_mountain", _ => "stage_stars" };

        // ------------------------------------------------------------------ the map

        void DrawMap(float w)
        {
            var o = Campaign.Opponent(SelectedFight);
            // the title
            Words(new Rect(60f, 30f, 900f, 120f), BoxingStrings.Title, 104, Sun, TextAnchor.MiddleLeft, 6f, imgui: true);
            Words(new Rect(66f, 140f, 900f, 50f), BoxingStrings.Climb, 38, Color.white, TextAnchor.MiddleLeft, 3f);
            DrawClimb(w);
            // the chosen fight
            var card = new Rect(50f, 210f, 680f, 222f);
            Panel(card, new Color(Night.r, Night.g, Night.b, 0.88f), 30f);
            Panel(new Rect(card.x, card.y, card.width, 10f), o.Champion ? Sun : Coral, 5f);
            Pic(StageIcon(o.Stage), new Rect(card.x + 22f, card.y + 26f, 92f, 92f), Color.white);
            Words(new Rect(card.x + 130f, card.y + 20f, card.width - 160f, 40f),
                  $"{BoxingStrings.Stage(o.Stage).ToUpperInvariant()} · {SelectedFight % Campaign.FightsPerStage + 1}/{Campaign.FightsPerStage}", 28, Sky,
                  TextAnchor.MiddleLeft, 0f);
            Words(new Rect(card.x + 130f, card.y + 56f, card.width - 160f, 76f), o.Name + (string.IsNullOrEmpty(o.Title) ? "" : " " + o.Title), 54,
                  o.Champion ? Sun : Color.white, TextAnchor.MiddleLeft, 3f);
            string line = !_progress.Unlocked(Difficulty, SelectedFight) ? BoxingStrings.Locked : BoxingStrings.Tip(o.Id);
            Words(new Rect(card.x + 30f, card.y + 136f, card.width - 60f, 76f), line, 28, Cream, TextAnchor.MiddleLeft, 0f);
            Stars(new Vector2(card.xMax - 150f, card.y + 26f), _progress.StarsOf(Difficulty, SelectedFight), 40f);
            // the rows
            var panel = new Rect(50f, 452f, 680f, 4 * 62f + 24f);
            Panel(panel, new Color(Night.r, Night.g, Night.b, 0.88f), 30f);
            RowLine(panel, 0, Row.Fight, BoxingStrings.FightRow, BoxingStrings.FightOf(SelectedFight + 1, Campaign.Fights));
            RowLine(panel, 1, Row.Difficulty, BoxingStrings.DifficultyRow, BoxingStrings.Difficulty(Difficulty));
            string outfit = BoxingLooks.Outfits[_progress.Outfit].name;
            int nextStars = _progress.OutfitsUnlocked < BoxingProgress.OutfitStars.Length ? BoxingProgress.OutfitStars[_progress.OutfitsUnlocked] : -1;
            RowLine(panel, 2, Row.Outfit, BoxingStrings.OutfitRow, outfit + (nextStars > 0 ? $"   ({_progress.TotalStars}/{nextStars} ★)" : ""));
            RowLine(panel, 3, Row.Gloves, BoxingStrings.GlovesRow, $"{_progress.Gloves + 1} / {BoxingProgress.GloveCount}");
            // the moves: little figures doing them
            float mx = 50f, my = panel.yMax + 18f;
            string[] figures = { "punch", "guard", "lean", "duck", "arms_up" };
            string[] captions = { BoxingStrings.MovePunch, BoxingStrings.MoveBlock, BoxingStrings.MoveDodge, BoxingStrings.MoveDuck, BoxingStrings.MoveSuper };
            for (int i = 0; i < figures.Length; i++)
            {
                var r = new Rect(mx + i * 136f, my, 128f, 150f);
                Panel(r, new Color(Night.r, Night.g, Night.b, 0.7f), 22f);
                Pic(figures[i], new Rect(r.x + 14f, r.y + 6f, 100f, 100f), Color.white);
                Words(new Rect(r.x, r.y + 102f, r.width, 44f), captions[i], 22, Cream, TextAnchor.MiddleCenter, 0f);
            }
            // what starts it
            var prompt = PlayerGate.Waiting(_input, BoxingStrings.KeyboardHelp);
            if (_input != null && _input.Gestures != null) _input.Gestures.PlacementShownFrame = Time.frameCount;
            if (prompt.Title != null)
            {
                Words(new Rect(0f, 960f, w, 64f), prompt.Title, 46, prompt.Colour, TextAnchor.MiddleCenter, 4f);
                if (!string.IsNullOrEmpty(prompt.Subtitle)) Words(new Rect(0f, 1018f, w, 44f), prompt.Subtitle, 26, Color.white, TextAnchor.MiddleCenter, 2f);
            }
            Words(new Rect(58f, my + 156f, 900f, 34f), BoxingStrings.MenuHint, 22, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleLeft, 2f);
        }

        void RowLine(Rect panel, int i, Row row, string label, string value)
        {
            float ry = panel.y + 12f + i * 62f;
            bool selected = _row == row;
            if (selected) Panel(new Rect(panel.x + 10f, ry, panel.width - 20f, 58f), new Color(1f, 1f, 1f, 0.13f), 22f);
            Words(new Rect(panel.x + 26f, ry, 240f, 58f), label, 30, selected ? Sun : new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleLeft, 0f);
            Words(new Rect(panel.x + 250f, ry, panel.width - 270f, 58f), (selected ? "‹  " : "") + value + (selected ? "  ›" : ""), 30,
                  Color.white, TextAnchor.MiddleLeft, 0f);
        }

        void Stars(Vector2 at, int n, float size)
        {
            for (int i = 0; i < 3; i++)
                Pic("star", new Rect(at.x + i * size * 1.05f, at.y, size, size), i < n ? Sun : new Color(1f, 1f, 1f, 0.22f));
        }

        /// <summary>The climb on the right: the four places from the gym at the bottom to the stars at the top, each its
        /// icon, its name, a dot a fight (won: gold, open: white, closed: dim), the champions a star, the chosen fight
        /// ringed, a belt for each champion beaten.</summary>
        void DrawClimb(float w)
        {
            float x0 = w - 640f, top = 60f, rowH = 196f;
            var panel = new Rect(x0, top - 14f, 600f, rowH * Campaign.Stages.Length + 20f);
            Panel(panel, new Color(Night.r, Night.g, Night.b, 0.78f), 34f);
            for (int s = 0; s < Campaign.Stages.Length; s++)
            {
                int row = Campaign.Stages.Length - 1 - s;
                float y = top + row * rowH;
                bool belt = _progress.Belt(Difficulty, s);
                bool open = _progress.Unlocked(Difficulty, s * Campaign.FightsPerStage);
                Pic(StageIcon(s), new Rect(x0 + 22f, y + 20f, 110f, 110f), open ? Color.white : new Color(1f, 1f, 1f, 0.35f));
                Words(new Rect(x0 + 146f, y + 16f, 260f, 50f), BoxingStrings.Stage(s), 36, belt ? Sun : Color.white, TextAnchor.MiddleLeft, 2f);
                Words(new Rect(x0 + 146f, y + 62f, 300f, 40f), BoxingStrings.StageLine(s), 21, new Color(1f, 1f, 1f, 0.65f), TextAnchor.MiddleLeft, 0f);
                if (belt) Pic("belt", new Rect(x0 + 146f, y + 104f, 104f, 65f), Color.white);
                for (int i = 0; i < Campaign.FightsPerStage; i++)
                {
                    int fight = s * Campaign.FightsPerStage + i;
                    bool champ = i == Campaign.FightsPerStage - 1;
                    float size = champ ? 66f : 50f;
                    var c = new Vector2(x0 + 400f + i * 76f, y + 128f - (champ ? 4f : 0f));
                    var r = new Rect(c.x - size / 2f, c.y - size / 2f, size, size);
                    bool won = _progress.Won(Difficulty, fight), unlocked = _progress.Unlocked(Difficulty, fight);
                    if (fight == SelectedFight)
                    {
                        float pulse = 7f + 3f * Mathf.Sin(Time.time * 5f);
                        Pic("disc", new Rect(r.x - pulse, r.y - pulse, r.width + pulse * 2f, r.height + pulse * 2f), Sky);
                    }
                    Pic("disc", r, won ? Sun : unlocked ? Color.white : new Color(1f, 1f, 1f, 0.18f));
                    var ink = won || unlocked ? Night : new Color(1f, 1f, 1f, 0.4f);
                    if (champ) Pic("star", new Rect(r.x + size * 0.2f, r.y + size * 0.18f, size * 0.6f, size * 0.6f), ink);
                    else Words(r, (fight + 1).ToString(), 26, ink, TextAnchor.MiddleCenter, 0f);
                }
                if (row < Campaign.Stages.Length - 1)
                    Pic("seg", new Rect(x0 + 74f, y + rowH - 14f, 6f, 30f), new Color(1f, 1f, 1f, open ? 0.5f : 0.15f));
            }
        }

        // ------------------------------------------------------------------ the results

        /// <summary>The results card on the screen's right half: the camera keeps the hero (cheering, or not) on the left
        /// (<see cref="WinnerShot"/>).</summary>
        void DrawResult(float w)
        {
            if (_bout == null) return;
            bool won = _bout.HeroWon;
            float cx = w * 0.75f;
            float pop = Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(_phaseTime / 0.25f));
            var tint = won ? Sun : Coral;
            Pic("burst", new Rect(cx - 300f * pop, 22f, 600f * pop, 270f * pop), new Color(tint.r, tint.g, tint.b, 0.35f), Time.time * 8f);
            Words(new Rect(cx - 500f, 48f, 1000f, 150f), won ? BoxingStrings.YouWon : BoxingStrings.YouLost, Mathf.RoundToInt(120 * pop), tint,
                  TextAnchor.MiddleCenter, 7f);
            string how = _bout.Ending switch
            {
                Ending.KO => BoxingStrings.KO,
                Ending.TKO => BoxingStrings.TKO,
                _ => won ? BoxingStrings.WonOnPoints : BoxingStrings.LostOnPoints,
            };
            Words(new Rect(cx - 500f, 196f, 1000f, 60f), how, 44, Color.white, TextAnchor.MiddleCenter, 3f);
            // what it opened: two to a row under the stats (the card as tall as what it holds)
            var r = LastReward;
            var pills = new List<(string text, string icon)>();
            if (r != null && r.Won)
            {
                if (r.Champion) pills.Add((BoxingStrings.Champion, "belt"));
                else if (r.Belt) pills.Add((BoxingStrings.NewBelt, "belt"));
                if (r.NewGloves >= 0) pills.Add((BoxingStrings.NewGloves, "glove"));
                if (r.NewOutfits.Count > 0) pills.Add((BoxingStrings.NewOutfit, "star"));
            }
            float statsY = won ? 206f : 60f;
            float height = pills.Count > 0 ? statsY + 116f + 72f * ((pills.Count + 1) / 2) + 10f : statsY + 116f;
            var panel = new Rect(cx - 400f, 280f, 800f, height);
            Panel(panel, new Color(Night.r, Night.g, Night.b, 0.88f), 40f);
            if (won)
            {
                string[] labels = { BoxingStrings.StarWin, BoxingStrings.StarNoDown, BoxingStrings.StarKO };
                bool[] got = { true, _bout.HeroKnockdowns == 0, _bout.Ending != Ending.Decision && _bout.Round < _bout.Level.Rounds };
                for (int i = 0; i < 3; i++)
                {
                    float x = panel.x + 134f + i * 266f;
                    float appear = Mathf.Clamp01((_phaseTime - 0.3f - i * 0.25f) / 0.2f);
                    float size = 110f * Mathf.Lerp(1.6f, 1f, appear);
                    if (appear > 0f) Pic("star", new Rect(x - size / 2f, panel.y + 78f - size / 2f, size, size), got[i] ? Sun : new Color(1f, 1f, 1f, 0.2f));
                    Words(new Rect(x - 135f, panel.y + 140f, 270f, 44f), labels[i], 26, got[i] ? Color.white : new Color(1f, 1f, 1f, 0.4f), TextAnchor.MiddleCenter, 0f);
                }
            }
            Words(new Rect(panel.x, panel.y + statsY, panel.width, 50f), BoxingStrings.Punches(_bout.Landed, _bout.Thrown), 32, Color.white, TextAnchor.MiddleCenter, 0f);
            Words(new Rect(panel.x, panel.y + statsY + 46f, panel.width, 50f), BoxingStrings.Defences(_bout.Dodges, _bout.Blocks), 32, Color.white, TextAnchor.MiddleCenter, 0f);
            for (int i = 0; i < pills.Count; i++)
            {
                int row = i / 2, inRow = Mathf.Min(2, pills.Count - row * 2);
                float px = cx + (i % 2 - (inRow - 1) * 0.5f) * 395f;
                var pr = new Rect(px - 185f, panel.y + statsY + 116f + row * 72f, 370f, 60f);
                Panel(pr, Sun, 30f);
                Pic(pills[i].icon, new Rect(pr.x + 8f, pr.y - 6f, 72f, 72f), Color.white);
                Words(new Rect(pr.x + 76f, pr.y, pr.width - 84f, pr.height), pills[i].text, 26, Night, TextAnchor.MiddleCenter, 0f);
            }
            if (_phaseTime > resultDelay)
            {
                float y = panel.yMax + 36f;
                Words(new Rect(cx - 520f, y, 1040f, 70f), won ? BoxingStrings.NextFight : BoxingStrings.TryAgain, 46, Sun, TextAnchor.MiddleCenter, 4f);
                Words(new Rect(cx - 520f, y + 70f, 1040f, 50f), BoxingStrings.BackToMap, 30, Color.white, TextAnchor.MiddleCenter, 2f);
            }
        }
    }
}
