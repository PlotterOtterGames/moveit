using MoveIt.Motion;
using UnityEngine;

namespace MoveIt.Games.Fairy
{
    /// <summary>Zana!'s HUD (the garden, 2026-11-16; framed like the garden's storybook since 2026-11-17): the tree's
    /// health top left (the tree on a gold-rimmed medallion, a bar of its hearts with a leafy frame) and the stardust under
    /// it, the wave on a pink ribbon top middle, the crystal on a medallion top right, the spell book along the bottom
    /// left, the drawn magic (the trail, what it became, the countdown round the wand, the hint: FairyMagicHud.cs), the
    /// wands (drawn as magic wands, like mouse pointers), calls, the wave's title, the transformation, the run's end, and
    /// the waiting screen with the moves and the menu. No panels over play: outlined words and pictures on the scene. Pictures from tools/fairy/art.py's sheet in one batch
    /// (<see cref="HudBatch"/>), words with IMGUI or the batch.</summary>
    public sealed partial class FairyGame
    {
        enum HudPass { All, Pictures, Words }

        HudPass _pass;
        HudBatch _hud;
        bool _batchWords;
        static readonly Color Night = new Color(SportHud.Night.r, SportHud.Night.g, SportHud.Night.b, 0.78f);
        static readonly Color Gold = new Color(1f, 0.85f, 0.35f);

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || PerfSwitches.Get("hud") == "0") return;
            float w = GameHud.Begin();
            bool batch = _hud != null && !_paused && PerfSwitches.Get("hudbatch") != "0";
            _batchWords = batch && PerfSwitches.Get("hudtext") != "0";
            if (batch)
            {
                _hud.Clear();
                _pass = HudPass.Pictures;
                DrawHud(w);
                _hud.Draw(GUI.matrix);
                _pass = HudPass.Words;
            }
            DrawHud(w);
            _pass = HudPass.All;
        }

        void DrawHud(float w)
        {
            switch (Current)
            {
                case Phase.Waiting:
                    DrawWaiting(w);
                    break;
                case Phase.Intro:
                    DrawPlay(w);
                    DrawIntro(w);
                    break;
                case Phase.Playing:
                    if (_paused)
                    {
                        if (_pass != HudPass.All) break;
                        if (!_two) PlayerGate.DrawPaused(w, _input);
                        else DrawTwoPaused(w);
                        break;
                    }
                    DrawPlay(w);
                    if (!_two && !UsingKeys && !Autoplay && _pass != HudPass.Pictures) PlayerGate.DrawEdgeWarning(w, _input);
                    break;
                case Phase.Transform:
                    DrawPlay(w);
                    DrawTransform(w);
                    break;
                case Phase.Over:
                    DrawOver(w);
                    break;
            }
            if (_pass == HudPass.Pictures && _batchWords == false) return;
            if (_call != null && Time.time < _callUntil && Current == Phase.Playing && !_paused) DrawCall(w);
        }

        // ------------------------------------------------ playing

        string _dustText, _healthText;
        int _dustShown = -1, _healthShown = -1;

        void DrawPlay(float w)
        {
            if (_run == null) return;
            // the tree's health, top left: the tree on its medallion, a bar of its hearts (rose, red and pulsing when low,
            // a white flash when it's hit), the hearts left on it
            float health = _run.TreeMax > 0 ? Mathf.Clamp01((float)_run.TreeHealth / _run.TreeMax) : 0f;
            var bar = new Rect(112f, 36f, 380f, 70f);
            Picture(bar, "bar_frame", Color.white);
            if (health > 0f && _sheet != null && _sheet.TryGetValue("bar_fill", out var fillUv))
            {
                // the fill's capsule spans 2.8 .. 97.2 % of its picture: cut it at the health left
                float f = 0.028f + 0.944f * health;
                bool low = health < 0.34f;
                var fill = low ? Color.Lerp(new Color(1f, 0.32f, 0.36f), new Color(1f, 0.55f, 0.6f), 0.5f + 0.5f * Mathf.Sin(Time.time * 7f))
                               : new Color(1f, 0.42f, 0.62f);
                fill = Color.Lerp(fill, Color.white, Mathf.Clamp01(_treeFlash) * 0.8f);
                PictureUv(new Rect(bar.x, bar.y, bar.width * f, bar.height), new Rect(fillUv.x, fillUv.y, fillUv.width * f, fillUv.height), fill);
            }
            float beat = 1f + 0.1f * Mathf.Sin(Time.time * (health < 0.34f ? 9f : 4f));
            Picture(new Rect(bar.x + 30f - 17f * beat, bar.center.y - 17f * beat, 34f * beat, 34f * beat), "heart", Color.white);
            if (_healthShown != _run.TreeHealth || _healthText == null)
            {
                _healthShown = _run.TreeHealth;
                _healthText = _run.TreeHealth.ToString();
            }
            Words(new Rect(bar.x, bar.y + 4f, bar.width - 34f, bar.height - 8f), _healthText, 34, Color.white, TextAnchor.MiddleRight, 3f);
            Picture(new Rect(20f, 18f, 112f, 112f), "tree_badge", _treeFlash > 0f ? Color.Lerp(Color.white, new Color(1f, 0.5f, 0.5f), _treeFlash) : Color.white);
            int dust = _progress.Stardust + _run.Stardust;
            if (dust != _dustShown || _dustText == null)
            {
                _dustShown = dust;
                _dustText = dust.ToString();
            }
            // the stardust under it, on a dark pill
            var pill = new Rect(132f, 110f, 150f, 46f);
            Picture(pill, "capsule", new Color(0.18f, 0.12f, 0.3f, 0.72f));
            Picture(new Rect(pill.x + 6f, pill.y + 3f, 40f, 40f), "star", Color.white);
            Words(new Rect(pill.x + 50f, pill.y, pill.width - 60f, pill.height), _dustText, 30, Gold, TextAnchor.MiddleLeft, 2f);
            // the wave, top middle, with a star for each transformation (the wave's title says it big at its start)
            bool titled = Current == Phase.Intro || _waveCall && _call != null && Time.time < _callUntil;
            if (!titled)
            {
                Picture(new Rect(w * 0.5f - 210f, 10f, 420f, 102f), "ribbon", Color.white);
                Words(new Rect(w * 0.5f - 160f, 16f, 320f, 72f), FairyStrings.Wave(_run.Wave), 46, Color.white, TextAnchor.MiddleCenter, 3.5f);
            }
            for (int i = 1; i < _run.Level; i++)
                Picture(new Rect(w * 0.5f - (_run.Level - 1) * 17f + (i - 1) * 34f, 104f, 30f, 30f), "star", Color.white);
            DrawMagic(w);
            DrawSpellBook(w);
            if (Current == Phase.Playing)
            {
                for (int k = 0; k < Players; k++)
                {
                    DrawHint(w, k);
                    DrawRead(w, k);
                    DrawStroke(k);
                }
                DrawWands();
            }
        }

        /// <summary>Where the wand's star is in its picture (0..1 from the top left): the point it points at.</summary>
        static readonly Vector2 WandStar = new Vector2(0.27f, 0.27f);

        /// <summary>Each wand (2026-11-16, the user: "it should look more like a wand, like the mouse pointer behaves, not
        /// tied to the hand but freeform"): a magic wand with its star on the point, leaning like a mouse pointer, a soft
        /// glow round the star (brighter on its shape), sparkles trailing behind it.</summary>
        void DrawWands()
        {
            for (int k = 0; k < Players; k++)
            {
                var trail = _trail[k];
                if (!CursorOf(k, out var px))
                {
                    if (_pass != HudPass.Words) trail.Clear();
                    // the wand down: the countdown round the fairy's hands instead
                    if (k < _fairies.Count && _fairies[k] != null && ScreenOf(_fairies[k].Palms, out var palms)) DrawCharge(k, palms);
                    continue;
                }
                var colour = WandColour(k, Element);
                // while it charges: the countdown round it, the wand dimmed, no sparkles
                bool charging = DrawCharge(k, px);
                bool drawing = !charging && _humans[k].Caster.Drawing;
                if (_pass != HudPass.Words)
                {
                    trail.Add(px);
                    if (trail.Count > 12) trail.RemoveAt(0);
                }
                if (!charging)
                    for (int i = 0; i < trail.Count - 1; i++)
                    {
                        float a = (i + 1f) / trail.Count;
                        float size = 5f + 9f * a;
                        var at = trail[i] + new Vector2(Mathf.Sin(i * 2.1f + Time.time * 3f), Mathf.Cos(i * 1.7f + Time.time * 2.6f)) * 6f;
                        Picture(new Rect(at.x - size, at.y - size, size * 2f, size * 2f), i % 3 == 0 ? "star" : "dot", new Color(colour.r, colour.g, colour.b, 0.75f * a));
                    }
                float g = WandGlow * (drawing ? 1.4f : 1f);
                if (!charging) Picture(new Rect(px.x - g, px.y - g, g * 2f, g * 2f), "glow", new Color(colour.r, colour.g, colour.b, drawing ? 0.65f : 0.4f));
                float s = 150f * (drawing ? 1f + 0.05f * Mathf.Sin(Time.time * 14f) : 1f);
                Picture(new Rect(px.x - s * WandStar.x, px.y - s * WandStar.y, s, s), k == 0 ? "wand" : "wand_blue", charging ? new Color(0.62f, 0.6f, 0.72f, 0.85f) : Color.white);
            }
        }

        void DrawMagic(float w)
        {
            float k = Mathf.Clamp01(_run.Magic);
            bool full = k >= 1f;
            float pulse = full ? 1f + 0.12f * Mathf.Sin(Time.time * 8f) : 1f;
            var c = new Rect(w - 150f - 8f * (pulse - 1f) * 10f, 20f, 118f * pulse, 118f * pulse);
            if (full)
            {
                Picture(new Rect(c.x - 34f, c.y - 34f, c.width + 68f, c.height + 68f), "glow", new Color(1f, 0.55f, 0.95f, 0.85f));
                // the big magic's star beside it: its shape comes when the garden is calm enough (the arms-up figure from
                // the gestures' time said the wrong move)
                float bob = 6f * Mathf.Sin(Time.time * 6f);
                Picture(new Rect(c.x - 96f, 38f + bob, 80f, 80f), FairyArt.SpellSprite(Spell.Burst), Color.white);
            }
            Picture(new Rect(c.center.x - 72f, c.center.y - 72f, 144f, 144f), "medallion", Color.white);
            Picture(c, "crystal", new Color(1f, 1f, 1f, 0.4f));
            if (k > 0.01f && _sheet != null && _sheet.TryGetValue("crystal", out var uv))
            {
                var part = new Rect(c.x, c.y + c.height * (1f - k), c.width, c.height * k);
                var puv = new Rect(uv.x, uv.y, uv.width, uv.height * k);
                PictureUv(part, puv, Color.HSVToRGB(Mathf.Repeat(Time.time * 0.15f, 1f), full ? 0.55f : 0.35f, 1f));
            }
        }

        void DrawCall(float w)
        {
            float age = Time.time - _callFrom;
            float pop = 1f + 0.25f * Mathf.Exp(-age * 10f);
            int size = _call.Length > 26 ? 56 : 74;
            // above the fairy's head (she flies in the middle of the screen)
            Words(new Rect(0f, 116f, w, 130f), _call, size, Gold, TextAnchor.MiddleCenter, 4f, pop);
            if (_callSub != null) Words(new Rect(0f, 232f, w, 56f), _callSub, 36, Color.white, TextAnchor.MiddleCenter, 3f);
        }

        // ------------------------------------------------ cards

        void DrawIntro(float w)
        {
            float a = Mathf.Clamp01(_phaseTime / 0.4f);
            float rise = 30f * (1f - a);
            Words(new Rect(0f, 60f + rise, w, 130f), FairyStrings.Wave(_run.Wave), 104, new Color(Gold.r, Gold.g, Gold.b, a), TextAnchor.MiddleCenter, 6f);
            Words(new Rect(0f, 182f + rise, w, 70f), FairyStrings.DefendTree, 50, new Color(1f, 1f, 1f, a), TextAnchor.MiddleCenter, 4f);
        }

        void DrawTransform(float w)
        {
            float pulse = 1f + 0.08f * Mathf.Sin(Time.time * 6f);
            // above her head, and the move on both sides of her (she is in the middle)
            Words(new Rect(0f, 70f, w, 140f), FairyStrings.Transform, 96, Gold, TextAnchor.MiddleCenter, 6f, pulse);
            Words(new Rect(0f, 196f, w, 64f), FairyStrings.TransformHow, 48, Color.white, TextAnchor.MiddleCenter, 4f);
            float bob = 10f * Mathf.Sin(Time.time * 5f);
            Picture(new Rect(w * 0.5f - 470f, 380f + bob, 190f, 190f), "pose_arms_up", new Color(1f, 0.6f, 0.95f));
            Picture(new Rect(w * 0.5f + 280f, 380f - bob, 190f, 190f), "pose_arms_up", new Color(1f, 0.6f, 0.95f));
            Words(new Rect(w * 0.5f - 560f, 580f, 370f, 56f), FairyStrings.Form(_run.Level + 1), 36, Gold, TextAnchor.MiddleCenter, 3f);
        }

        void DrawOver(float w)
        {
            float pop = 1f + 0.2f * Mathf.Exp(-_phaseTime * 6f);
            // a soft plum card behind the words (the garden is busy behind them)
            int opened = (_opened.Wings ? 1 : 0) + (_opened.Dress ? 1 : 0) + (_opened.Freeze ? 1 : 0) + (_opened.Heal ? 1 : 0);
            float bottom = 430f + (_opened.Best ? 66f : 54f) + 58f * opened + 22f;
            float k = Mathf.Clamp01(_phaseTime / 0.4f);
            Picture(new Rect(w * 0.5f - 470f, 100f, 940f, bottom - 100f), "panel", new Color(0.17f, 0.11f, 0.3f, 0.68f * k));
            Words(new Rect(0f, 120f, w, 120f), FairyStrings.GardenFell, 70, FairyFx.Pink, TextAnchor.MiddleCenter, 5f, pop);
            Words(new Rect(0f, 250f, w, 110f), FairyStrings.WavesSaved(_run.WavesCleared), 88, Gold, TextAnchor.MiddleCenter, 6f);
            Words(new Rect(0f, 360f, w, 60f), FairyStrings.Dust(_run.Stardust), 44, Color.white, TextAnchor.MiddleCenter, 3f);
            float y = 430f;
            if (_opened.Best)
            {
                Words(new Rect(0f, y, w, 64f), FairyStrings.NewBest, 54, FairyFx.Pink, TextAnchor.MiddleCenter, 4f, 1f + 0.06f * Mathf.Sin(Time.time * 5f));
                y += 66f;
            }
            else
            {
                Words(new Rect(0f, y, w, 50f), FairyStrings.Best(_progress.Best[(int)_run.Difficulty]), 34, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleCenter, 3f);
                y += 54f;
            }
            if (_opened.Wings) y = Opened(w, y, FairyStrings.NewWings);
            if (_opened.Dress) y = Opened(w, y, FairyStrings.NewDress);
            if (_opened.Freeze) y = Opened(w, y, FairyStrings.NewSpell(Spell.Freeze));
            if (_opened.Heal) y = Opened(w, y, FairyStrings.NewSpell(Spell.Heal));
            if (_phaseTime > 2.5f) Words(new Rect(0f, GameHud.Height - 140f, w, 60f), FairyStrings.PlayAgain, 38, Gold, TextAnchor.MiddleCenter, 3f);
        }

        float Opened(float w, float y, string text)
        {
            Words(new Rect(0f, y, w, 58f), text, 42, FairyFx.Pink, TextAnchor.MiddleCenter, 4f, 1f + 0.06f * Mathf.Sin(Time.time * 5f));
            return y + 58f;
        }

        void DrawTwoPaused(float w)
        {
            for (int k = 0; k < 2; k++)
            {
                var s = _slots[k];
                if (s.SeenWithin(0.6f) && s.Calibrated) continue;
                GameHud.Message(w, FairyStrings.ComeBackPlayer(k), null, PlayerColors[k]);
                return;
            }
        }

        // ------------------------------------------------ the waiting screen

        void DrawWaiting(float w)
        {
            bool camera = PlayerGate.State(_input) != GateState.NoService;
            float h = GameHud.Height;
            // top left: the game, the goal, the best and the garden
            Words(new Rect(50f, 20f, 800f, 120f), FairyStrings.Title, 108, FairyFx.Pink, TextAnchor.MiddleLeft, 5f);
            Words(new Rect(54f, 136f, 1100f, 56f), FairyStrings.Goal, 36, Color.white, TextAnchor.MiddleLeft, 3f);
            int best = _progress.Best[(int)_menu.Difficulty];
            Words(new Rect(54f, 188f, 1100f, 46f), $"✦ {_progress.Stardust} · {FairyStrings.Best(best)} · {FairyStrings.GardenGrew(_progress.Decorations)}",
                  28, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleLeft, 2f);
            // the moves, a little figure with its words on each row of a soft card (side by side they ran into each other)
            Picture(new Rect(40f, 238f, 800f, 206f), "panel", new Color(0.17f, 0.11f, 0.3f, 0.55f));
            float fy = 246f;
            foreach (var (pic, tint, text) in new[]
                     {
                         ("pose_point", new Color(1f, 0.85f, 0.4f), FairyStrings.HowWand), ("pose_trace", new Color(0.45f, 0.8f, 1f), FairyStrings.HowDraw),
                         ("pose_arms_up", new Color(1f, 0.6f, 0.95f), FairyStrings.HowArms),
                     })
            {
                Picture(new Rect(56f, fy, 62f, 62f), pic, tint);
                Words(new Rect(128f, fy, 700f, 62f), text, 27, Color.white, TextAnchor.MiddleLeft, 2f);
                fy += 64f;
            }
            DrawMenu(h - FairyMenu.Height - 30f, camera);
        }

        const float GateScale = 0.58f;   // PlayerGate's big line on one line in the left column

        /// <summary>
        /// The menu, its hint, and over the menu the gate's prompt (PlayerGate's, scaled into the menu's column) or the two
        /// players' cards. Into the HUD's batch with its pictures and words since 2026-11-17 night: drawn with IMGUI (~40
        /// draws a frame: boxes, rounded pills, labels) the waiting screen ran 25.9-27.6 fps on a hot box against 29.4
        /// without its HUD, and the user stood in front of it at 24-25 (docs/hardware.md "Zana!'s waiting screen").
        /// PerfLab menubatch=0 draws it with IMGUI as before.
        /// </summary>
        void DrawMenu(float menuY, bool camera)
        {
            if (!_batchWords || PerfSwitches.Get("menubatch") == "0")
            {
                if (_pass == HudPass.Pictures) return;
                _menu.Draw(60f, menuY, camera, _progress, FairyMenu.Imgui);
                SportHud.Text(new Rect(60f, menuY - 40f, FairyMenu.Width, 36f), FairyStrings.MenuHint, 24, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleLeft, 0f);
                if (_menu.Two)
                {
                    DrawPlayerCards(menuY - 160f, FairyMenu.Imgui);
                    return;
                }
                var old = GUI.matrix;
                GUI.matrix = old * Matrix4x4.TRS(new Vector3(60f, menuY - 46f - 530f * GateScale, 0f), Quaternion.identity, new Vector3(GateScale, GateScale, 1f));
                PlayerGate.DrawWaiting(FairyMenu.Width / GateScale, _input, "", FairyStrings.KeyboardHelp);
                GUI.matrix = old;
                return;
            }
            // batched: it all goes in during the pictures pass; the words pass draws only what stays IMGUI (now and then)
            if (_pass == HudPass.Pictures)
            {
                _menu.Draw(60f, menuY, camera, _progress, MenuPainter);
                Words(new Rect(60f, menuY - 40f, FairyMenu.Width, 36f), FairyStrings.MenuHint, 24, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleLeft, 0f);
            }
            if (_menu.Two)
            {
                DrawPlayerCards(menuY - 160f, MenuPainter);
                return;
            }
            var prompt = PlayerGate.Waiting(_input, FairyStrings.KeyboardHelp);
            if (_input != null && _input.Gestures != null) _input.Gestures.PlacementShownFrame = Time.frameCount;
            if (prompt.Title == null) return;
            var origin = new Vector2(60f, menuY - 46f - 530f * GateScale);
            float width = FairyMenu.Width / GateScale;
            // without the camera service (a long line that wraps) or with arrows to a side: IMGUI, as PlayerGate draws it
            if (prompt.State == GateState.NoService || prompt.Chevrons != 0)
            {
                if (_pass == HudPass.Pictures) return;
                var old = GUI.matrix;
                GUI.matrix = old * Matrix4x4.TRS(origin, Quaternion.identity, new Vector3(GateScale, GateScale, 1f));
                PlayerGate.DrawPrompt(width, prompt);
                GUI.matrix = old;
                return;
            }
            if (_pass != HudPass.Pictures) return;
            // GameHud.Message's lines: the big one at y 300 (Medium past 26 characters), the small one at 450
            GateLine(origin, new Rect(0f, 300f, width, 140f), prompt.Title, prompt.Title.Length > 26 ? GameHud.Medium : GameHud.Big, prompt.Colour);
            if (!string.IsNullOrEmpty(prompt.Subtitle)) GateLine(origin, new Rect(0f, 450f, width, 80f), prompt.Subtitle, GameHud.Small, Color.white);
        }

        /// <summary>One of the gate's lines into the batch, as <see cref="GameHud.Text"/> lays it out (a drop shadow 3
        /// units down and right, then the line; one line: the gate's lines fit the column) under the column's scale.</summary>
        void GateLine(Vector2 origin, Rect r, string text, GUIStyle style, Color colour)
        {
            const float k = GateScale;
            var at = new Rect(origin.x + r.x * k, origin.y + r.y * k, r.width * k, r.height * k);
            var font = style.font != null ? style.font : SportHud.Font;
            int size = Mathf.RoundToInt(style.fontSize * k);
            _hud.Text(font, new Rect(at.x + 3f * k, at.y + 3f * k, at.width, at.height), text, size, style.fontStyle, new Color(0f, 0f, 0f, 0.7f),
                      TextAnchor.MiddleCenter);
            _hud.Text(font, at, text, size, style.fontStyle, colour, TextAnchor.MiddleCenter);
        }

        MenuPaint _menuPaint;
        FairyMenu.IPaint MenuPainter => _menuPaint ??= new MenuPaint(this);

        /// <summary>The menu into the HUD's batch: boxes from the sheet's white, pills from its capsule, rounded boxes and
        /// cards from its panel, words as glyphs (the pass decides where each goes, like every picture of the HUD).</summary>
        sealed class MenuPaint : FairyMenu.IPaint
        {
            readonly FairyGame _game;

            public MenuPaint(FairyGame game) => _game = game;

            public void Box(Rect r, Color colour) => _game.Solid(r, colour);

            public void Round(Rect r, Color colour, float radius) =>
                _game.Picture(r, r.width >= r.height && radius >= r.height * 0.5f - 0.5f ? "capsule" : "panel", colour);

            public void Card(Rect r, Color colour, float radius) => _game.Picture(r, "panel", colour);

            public void Text(Rect r, string text, int size, Color colour, TextAnchor anchor) => _game.Words(r, text, size, colour, anchor, 0f);
        }

        /// <summary>A plain box: the middle of the sheet's white cell (at its edge the sheet's gap would blend in).</summary>
        void Solid(Rect r, Color colour)
        {
            if (_sheet == null || !_sheet.TryGetValue("white", out var uv))
            {
                if (_pass != HudPass.Words) GameHud.Box(r, colour);
                return;
            }
            PictureUv(r, new Rect(uv.x + uv.width * 0.25f, uv.y + uv.height * 0.25f, uv.width * 0.5f, uv.height * 0.5f), colour);
        }

        bool DrawPlayerCards(float y, FairyMenu.IPaint paint)
        {
            if (_input != null && _input.Gestures != null) _input.Gestures.PlacementShownFrame = Time.frameCount;
            for (int k = 0; k < 2; k++)
            {
                var s = _slots[k];
                var r = new Rect(60f + k * (FairyMenu.Width / 2f + 10f), y, FairyMenu.Width / 2f - 10f, 100f);
                paint.Card(r, new Color(SportHud.Night.r, SportHud.Night.g, SportHud.Night.b, 0.84f), 22f);
                paint.Round(new Rect(r.x + 14f, r.y + 14f, 10f, r.height - 28f), PlayerColors[k], 5f);
                paint.Text(new Rect(r.x + 40f, r.y + 4f, r.width - 50f, 48f), FairyStrings.Player(k), 34, PlayerColors[k], TextAnchor.MiddleLeft);
                string status;
                var color = Color.white;
                if (!s.Present) status = CommonStrings.StepIn;
                else if (s.Placement.Blocks)
                {
                    status = s.Placement.Issue switch
                    {
                        PlacementIssue.MoveLeft => CommonStrings.MoveLeft,
                        PlacementIssue.MoveRight => CommonStrings.MoveRight,
                        _ => CommonStrings.StepBack,
                    };
                    color = Brand.Current.SecondaryColor;
                }
                else if (!s.Calibrated) status = $"{FairyStrings.StandStill} {s.CalibrationProgress * 100f:0}%";
                else if (!s.Ready) status = FairyStrings.RaiseYourHands;
                else
                {
                    status = FairyStrings.Ready;
                    color = Gold;
                }
                paint.Text(new Rect(r.x + 40f, r.y + 50f, r.width - 50f, 44f), status, 26, color, TextAnchor.MiddleLeft);
            }
            if (_slots.TooClose)
            {
                if (_pass != HudPass.Pictures) GameHud.Banner(new Rect(60f, y - 70f, FairyMenu.Width, 60f), FairyStrings.StepApart, Brand.Current.SecondaryColor);
                return true;
            }
            if (_readySince < 0f) return false;
            int waiting = _slots[0].Ready ? 1 : 0;
            int left = Mathf.Max(1, Mathf.CeilToInt(JoinWait - (Time.time - _readySince)));
            paint.Text(new Rect(60f, y - 50f, FairyMenu.Width, 44f), $"{FairyStrings.WaitingFor(waiting)} {FairyStrings.PlayingAloneIn(left)}", 28, PlayerColors[waiting], TextAnchor.MiddleLeft);
            return true;
        }

        // ------------------------------------------------ drawing helpers

        void Picture(Rect r, string name, Color tint)
        {
            if (_sheet == null || !_sheet.TryGetValue(name, out var uv))
            {
                // no sheet yet: plain shapes stand in
                if (_pass != HudPass.Words && (name == "panel" || name == "capsule" || name == "white")) GameHud.Box(r, tint);
                return;
            }
            // the rounded panel and the capsule stretch in nine pieces (their corners stay round: tools/fairy/art.py)
            if (name == "panel") Nine(r, Mathf.Min(30f, Mathf.Min(r.width, r.height) * 0.5f), uv, new Vector2(uv.width * 40f / 128f, uv.height * 40f / 128f), tint);
            else if (name == "capsule") Nine(r, r.height * 0.5f, uv, new Vector2(uv.width * 0.125f, uv.height * 0.5f), tint);
            else PictureUv(r, uv, tint);
        }

        void Nine(Rect r, float border, Rect uv, Vector2 uvBorder, Color tint)
        {
            if (_pass == HudPass.Pictures)
            {
                _hud.Nine(r, border, uv, uvBorder, tint);
                return;
            }
            if (_pass == HudPass.Words || ui == null) return;
            float[] xs = { r.xMin, r.xMin + border, r.xMax - border, r.xMax };
            float[] ys = { r.yMin, r.yMin + border, r.yMax - border, r.yMax };
            float[] us = { uv.xMin, uv.xMin + uvBorder.x, uv.xMax - uvBorder.x, uv.xMax };
            float[] vs = { uv.yMax, uv.yMax - uvBorder.y, uv.yMin + uvBorder.y, uv.yMin };
            var old = GUI.color;
            GUI.color = tint;
            for (int j = 0; j < 3; j++)
                for (int i = 0; i < 3; i++)
                {
                    if (xs[i + 1] <= xs[i] || ys[j + 1] <= ys[j]) continue;
                    GUI.DrawTextureWithTexCoords(new Rect(xs[i], ys[j], xs[i + 1] - xs[i], ys[j + 1] - ys[j]), ui,
                                                 Rect.MinMaxRect(us[i], vs[j + 1], us[i + 1], vs[j]));
                }
            GUI.color = old;
        }

        void PictureUv(Rect r, Rect uv, Color tint)
        {
            if (_pass == HudPass.Pictures)
            {
                _hud.Image(r, uv, tint);
                return;
            }
            if (_pass == HudPass.Words || ui == null) return;
            var old = GUI.color;
            GUI.color = tint;
            GUI.DrawTextureWithTexCoords(r, ui, uv);
            GUI.color = old;
        }

        void Words(Rect r, string text, int size, Color colour, TextAnchor anchor, float outline, float grow = 1f)
        {
            if (_pass == HudPass.Pictures)
            {
                if (_batchWords) SportHud.Text(_hud, r, text, size, colour, anchor, outline, grow: grow);
            }
            else if (_pass == HudPass.All || !_batchWords) SportHud.Text(r, text, (int)(size * grow), colour, anchor, outline);
        }
    }
}
