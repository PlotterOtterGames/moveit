using System;
using MoveIt.Games;
using UnityEngine;
using UnityEngine.InputSystem;
using static MoveIt.Games.CommonStrings;

namespace MoveIt.Shell
{
    /// <summary>
    /// Game Pass screen on the home screen: shows whether Game Pass is active and lets a parent type the gift card
    /// code with the remote (an on-screen grid of the 32 code characters; a USB keyboard works too). The check
    /// character catches a typo at once, before anything is sent. Opened from Settings → Game Pass, or by choosing a
    /// locked game. (IMGUI for now; the P3 UI Toolkit rewrite replaces the look, not the flow.)
    /// Later: read the QR code on the card with the box's camera instead of typing.
    /// </summary>
    public sealed class GamePassPanel
    {
        const int Columns = 8;
        const int Keys = 34; // 32 characters + Delete + OK
        const int DeleteKey = 32, OkKey = 33;

        public bool Open { get; private set; }
        public string Code { get; private set; } = "";
        public int Focus { get; private set; }

        string _forGame;
        string _message = "";
        bool _messageGood;
        bool _working;

        /// <param name="forGame">Title of the locked game the player chose, or null when opened from Settings.</param>
        public void Show(string forGame)
        {
            Open = true;
            _forGame = forGame;
            Code = "";
            Focus = 0;
            _message = "";
            _working = false;
            if (Keyboard.current != null) Keyboard.current.onTextInput += OnText;
        }

        public void Close()
        {
            Open = false;
            if (Keyboard.current != null) Keyboard.current.onTextInput -= OnText;
        }

        void OnText(char c)
        {
            if (!Open || _working) return;
            c = char.ToUpperInvariant(c);
            if (GiftCode.Alphabet.IndexOf(c) >= 0) Type(c);
        }

        public void Update()
        {
            if (_working)
            {
                var r = TunduCloud.PollRedeem();
                if (r.state == "done") Finish(r);
                return;
            }
            if (RemoteInput.BackNotBackspace)
            {
                GameAudio.Sfx("ui_back", vary: 0f);
                Close();
                return;
            }
            if (RemoteInput.Backspace) Delete();
            int row = Focus / Columns, col = Focus % Columns;
            int rows = (Keys + Columns - 1) / Columns;
            if (RemoteInput.Left) Move(Focus == 0 ? Keys - 1 : Focus - 1);
            if (RemoteInput.Right) Move(Focus == Keys - 1 ? 0 : Focus + 1);
            if (RemoteInput.Up) Move(Mathf.Min(Keys - 1, ((row + rows - 1) % rows) * Columns + col));
            if (RemoteInput.Down) Move(Mathf.Min(Keys - 1, ((row + 1) % rows) * Columns + col));
            if (RemoteInput.Confirm)
            {
                if (Focus == DeleteKey) Delete();
                else if (Focus == OkKey) Submit();
                else Type(GiftCode.Alphabet[Focus]);
            }
        }

        void Move(int to)
        {
            Focus = to;
            GameAudio.Sfx("ui_move");
        }

        void Type(char c)
        {
            if (Code.Length >= GiftCode.Length) return;
            Code += c;
            _message = "";
            GameAudio.Sfx("ui_move");
            if (Code.Length == GiftCode.Length) Submit();
        }

        void Delete()
        {
            if (Code.Length == 0) return;
            Code = Code.Substring(0, Code.Length - 1);
            _message = "";
            GameAudio.Sfx("ui_back", vary: 0f);
        }

        void Submit()
        {
            if (Code.Length < GiftCode.Length)
            {
                Say(T("Kodi ka 16 shkronja e shifra", "The code has 16 letters and digits"), false);
                return;
            }
            if (!GiftCode.IsValid(Code))
            {
                Say(T("Një shkronjë është gabim: kontrollo kodin në kartë", "One character is wrong: check the code on the card"), false);
                return;
            }
            GameAudio.Sfx("ui_select", vary: 0f);
            _working = true;
            _message = T("Po kontrollohet…", "Checking…");
            _messageGood = true;
            TunduCloud.Redeem(Code);
        }

        void Finish(TunduCloud.RedeemResult r)
        {
            _working = false;
            bool ok = r.result == "ok";
            if (ok) Code = "";
            Say(Message(r), ok || r.result == "already_yours");
            GameAudio.Sfx(ok ? "ui_select" : "ui_back", vary: 0f);
        }

        void Say(string text, bool good)
        {
            _message = text;
            _messageGood = good;
        }

        static string Message(TunduCloud.RedeemResult r) => r.result switch
        {
            "ok" => T($"U aktivizua! +{r.days} ditë Game Pass", $"Activated! +{r.days} days of Game Pass"),
            "already_yours" => T("Ky kod është aktivizuar tashmë në këtë kuti", "This code is already active on this box"),
            "used" => T("Ky kod është përdorur tashmë", "This code has already been used"),
            "not_active" => T("Ky kod nuk është aktivizuar ende: pyet dyqanin", "This code isn't activated yet: ask the shop"),
            "void" => T("Ky kod është bllokuar: pyet dyqanin", "This code was blocked: ask the shop"),
            "invalid" => T("Kod i panjohur: kontrollo shkronjat", "Unknown code: check the characters"),
            "check_code" => T("Një shkronjë është gabim: kontrollo kodin në kartë", "One character is wrong: check the code on the card"),
            "limit" => T("Game Pass është paguar tashmë për shumë vite", "Game Pass is already paid for years ahead"),
            "locked" => T("Shumë kode të gabuara: provo përsëri nesër", "Too many wrong codes: try again tomorrow"),
            "slow_down" => T("Prit një minutë dhe provo përsëri", "Wait a minute and try again"),
            "blocked" => T("Kjo kuti është bllokuar: kontakto Tundu", "This box is blocked: contact Tundu"),
            "network" => T("Nuk ka internet: kontrollo Wi-Fi dhe provo përsëri", "No internet: check Wi-Fi and try again"),
            _ => T("Nuk u arrit: provo përsëri", "That didn't work: try again"),
        };

        public static string StatusText()
        {
            var pass = TunduCloud.Pass;
            if (pass.blocked) return T("Kjo kuti është bllokuar", "This box is blocked");
            if (!pass.active) return T("Jo aktive", "Not active");
            var until = DateTimeOffset.FromUnixTimeSeconds(pass.until).ToLocalTime();
            return T($"Aktive deri më {until:dd.MM.yyyy}", $"Active until {until:dd.MM.yyyy}");
        }

        public void Draw(float w, Brand brand)
        {
            GameHud.Box(new Rect(0, 0, w, GameHud.Height), new Color(0f, 0f, 0f, 0.8f));
            const float panelW = 1300f, panelH = 900f;
            float x = (w - panelW) / 2f, y = (GameHud.Height - panelH) / 2f;
            GameHud.Box(new Rect(x, y, panelW, panelH), brand.BackgroundColor);
            GameHud.Box(new Rect(x, y, panelW, 6f), brand.PrimaryColor);

            string title = _forGame == null ? "Game Pass" : T($"{_forGame} është në Game Pass", $"{_forGame} is in Game Pass");
            GameHud.Text(new Rect(x + 50, y + 25, panelW - 100, 80), title, GameHud.Medium, TextAnchor.MiddleLeft);
            GameHud.Text(new Rect(x + 50, y + 100, panelW - 100, 50),
                StatusText() + "  ·  " + T("Fut kodin nga karta Tundu", "Enter the code from a Tundu card"),
                GameHud.Small, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.8f));

            // The code typed so far, in groups of four, with the remaining places shown as dots.
            string shown = GiftCode.Format(Code.PadRight(GiftCode.Length, '·'));
            var codeBox = new Rect(x + 150, y + 170, panelW - 300, 110);
            GameHud.Box(codeBox, new Color(1f, 1f, 1f, 0.08f));
            GameHud.Text(codeBox, shown, GameHud.Big, TextAnchor.MiddleCenter, brand.PrimaryColor);

            const float keyW = 130f, keyH = 78f, gap = 12f;
            float gridW = Columns * keyW + (Columns - 1) * gap;
            float gx = x + (panelW - gridW) / 2f, gy = y + 310;
            for (int i = 0; i < Keys; i++)
            {
                int row = i / Columns, col = i % Columns;
                var r = new Rect(gx + col * (keyW + gap), gy + row * (keyH + gap), keyW, keyH);
                if (i == DeleteKey) r.width = keyW * 2 + gap;
                if (i == OkKey) r = new Rect(gx + 2 * (keyW + gap), r.y, keyW * 2 + gap, keyH);
                bool focused = i == Focus && !_working;
                GameHud.Box(r, focused ? brand.PrimaryColor : new Color(1f, 1f, 1f, 0.1f));
                string label = i == DeleteKey ? T("Fshi", "Delete") : i == OkKey ? "OK" : GiftCode.Alphabet[i].ToString();
                GameHud.Text(r, label, GameHud.Medium, TextAnchor.MiddleCenter, focused ? brand.BackgroundColor : Color.white, shadow: !focused);
            }

            if (!string.IsNullOrEmpty(_message))
            {
                GameHud.Text(new Rect(x + 50, y + panelH - 175, panelW - 100, 60), _message, GameHud.Medium, TextAnchor.MiddleCenter,
                    _messageGood ? brand.LeafColor : brand.SecondaryColor);
            }
            GameHud.Text(new Rect(x + 50, y + panelH - 105, panelW - 100, 45),
                T("Shigjetat lëviz   OK shkruaj   MBRAPA mbyll", "Arrows move   OK types   BACK closes") + $"      ID: {TunduCloud.DeviceId}",
                GameHud.Small, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.6f));
        }
    }
}
