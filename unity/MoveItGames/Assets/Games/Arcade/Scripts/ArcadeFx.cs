using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Arcade
{
    /// <summary>
    /// Lunapark!'s effects, simulated here and drawn into the sprite batches (no particle systems: everything stays in
    /// the two draw calls): bursts of stars and sparks, juice, puffs of dust, confetti, and the points popping up as
    /// numbers ("+30", "x3") in the sheet's rounded digits. World units = HUD pixels, y up.
    /// </summary>
    public sealed class ArcadeFx
    {
        struct Particle
        {
            public ArcadeSprite Sprite;
            public Vector2 Pos, Vel;
            public float Age, Life, Size0, Size1, Angle, Spin, Gravity, Drag;
            public Color Colour;
            public bool Glow, Fade;
        }

        struct Popup
        {
            public string Text;
            public Vector2 Pos;
            public Color Colour;
            public float Age, Height;
        }

        readonly ArcadeArt _art;
        readonly List<Particle> _particles = new List<Particle>(512);
        readonly List<Popup> _popups = new List<Popup>(32);
        readonly System.Random _rng = new System.Random(7);
        public const int MaxParticles = 600;

        public static readonly Color[] Confetti =
        {
            new Color(1f, 0.79f, 0.24f), new Color(1f, 0.35f, 0.31f), new Color(0.19f, 0.78f, 0.96f), new Color(0.48f, 0.38f, 1f),
            new Color(0.44f, 0.84f, 0.42f), Color.white,
        };

        public ArcadeFx(ArcadeArt art) => _art = art;

        public int Count => _particles.Count;

        public void Clear()
        {
            _particles.Clear();
            _popups.Clear();
        }

        float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        void Add(Particle p)
        {
            if (_particles.Count >= MaxParticles) _particles.RemoveAt(0);
            _particles.Add(p);
        }

        /// <summary>Sparks and little stars flying out (a hit, a catch).</summary>
        public void Burst(Vector2 at, float scale, Color colour, int count = 10)
        {
            for (int i = 0; i < count; i++)
            {
                float a = R(0f, Mathf.PI * 2f), sp = R(260f, 620f) * scale;
                Add(new Particle
                {
                    Sprite = _art[i % 3 == 0 ? "star_white" : "spark"], Pos = at, Vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * sp,
                    Life = R(0.35f, 0.6f), Size0 = R(30f, 46f) * scale, Size1 = 4f * scale, Angle = R(0f, 360f), Spin = R(-400f, 400f),
                    Drag = 3.5f, Colour = Color.Lerp(colour, Color.white, R(0f, 0.5f)), Glow = true,
                });
            }
            Add(new Particle { Sprite = _art["ring"], Pos = at, Life = 0.3f, Size0 = 40f * scale, Size1 = 150f * scale, Colour = colour, Glow = true, Fade = true });
        }

        /// <summary>Juice: a splat that stays a moment and drops flying out.</summary>
        public void Juice(Vector2 at, float scale, Color colour)
        {
            Add(new Particle { Sprite = _art["splat"], Pos = at, Life = 1.2f, Size0 = 70f * scale, Size1 = 95f * scale, Angle = R(0f, 360f), Colour = colour, Fade = true });
            for (int i = 0; i < 9; i++)
            {
                float a = R(0f, Mathf.PI * 2f), sp = R(200f, 520f) * scale;
                var vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * sp;
                Add(new Particle
                {
                    Sprite = _art["drop"], Pos = at, Vel = vel, Life = R(0.4f, 0.7f), Size0 = R(16f, 26f) * scale, Size1 = 8f * scale,
                    Angle = Mathf.Atan2(vel.y, vel.x) * Mathf.Rad2Deg + 90f, Gravity = 1400f * scale, Colour = colour,
                });
            }
        }

        /// <summary>A puff of dust (a step, a landing, a pop).</summary>
        public void Puff(Vector2 at, float scale, Color? colour = null, int count = 5)
        {
            for (int i = 0; i < count; i++)
            {
                float a = R(0f, Mathf.PI * 2f), sp = R(40f, 160f) * scale;
                Add(new Particle
                {
                    Sprite = _art["puff"], Pos = at + new Vector2(R(-10f, 10f), R(-6f, 6f)) * scale, Vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.5f + 0.3f) * sp,
                    Life = R(0.4f, 0.7f), Size0 = R(30f, 50f) * scale, Size1 = R(70f, 100f) * scale, Angle = R(0f, 360f), Spin = R(-60f, 60f),
                    Drag = 2f, Colour = colour ?? new Color(1f, 1f, 1f, 0.8f), Fade = true,
                });
            }
        }

        /// <summary>A comic "pow" behind a bonk.</summary>
        public void Pow(Vector2 at, float scale, Color colour)
        {
            Add(new Particle { Sprite = _art["burst"], Pos = at, Life = 0.28f, Size0 = 70f * scale, Size1 = 150f * scale, Angle = R(0f, 30f), Colour = colour, Fade = true });
        }

        /// <summary>Confetti raining from the top of the screen (a win, a prize).</summary>
        public void ConfettiRain(float width, int count = 80)
        {
            for (int i = 0; i < count; i++)
            {
                Add(new Particle
                {
                    Sprite = _art["confetti"], Pos = new Vector2(R(0f, width), R(1100f, 1500f)), Vel = new Vector2(R(-80f, 80f), R(-260f, -120f)),
                    Life = R(2.5f, 4f), Size0 = R(16f, 26f), Size1 = R(14f, 22f), Angle = R(0f, 360f), Spin = R(-360f, 360f), Gravity = 60f,
                    Colour = Confetti[i % Confetti.Length],
                });
            }
        }

        /// <summary>Confetti bursting up from a point.</summary>
        public void ConfettiBurst(Vector2 at, float scale, int count = 30)
        {
            for (int i = 0; i < count; i++)
            {
                float a = R(0.3f, Mathf.PI - 0.3f), sp = R(380f, 900f) * scale;
                Add(new Particle
                {
                    Sprite = _art["confetti"], Pos = at, Vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * sp, Life = R(1.2f, 2f),
                    Size0 = R(14f, 22f) * scale, Size1 = R(12f, 18f) * scale, Angle = R(0f, 360f), Spin = R(-500f, 500f), Gravity = 900f * scale, Drag = 1.2f,
                    Colour = Confetti[i % Confetti.Length],
                });
            }
        }

        /// <summary>Twinkles around a point for a while (a golden mole, a star).</summary>
        public void Twinkle(Vector2 at, float radius, Color colour, int count = 4)
        {
            for (int i = 0; i < count; i++)
            {
                float a = R(0f, Mathf.PI * 2f), d = R(0.3f, 1f) * radius;
                Add(new Particle
                {
                    Sprite = _art["spark"], Pos = at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d, Vel = new Vector2(0f, R(10f, 40f)), Life = R(0.4f, 0.8f),
                    Size0 = R(8f, 16f), Size1 = R(28f, 40f), Angle = R(0f, 90f), Colour = colour, Glow = true, Fade = true,
                });
            }
        }

        /// <summary>Dizzy stars circling (a hedgehog's bonk, a bird bumping a cloud, a wall): a few sparks orbiting.</summary>
        public void Dizzy(Vector2 at, float radius)
        {
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI * 2f / 3f;
                Add(new Particle
                {
                    Sprite = _art["star"], Pos = at, Vel = new Vector2(a, radius), Life = 0.9f, Size0 = 34f, Size1 = 26f, Angle = 0f, Spin = 0f,
                    Colour = Color.white, Drag = -999f,   // Drag -999: orbits at (Vel.y) around Pos, starting at angle Vel.x
                });
            }
        }

        /// <summary>A number (or "x3") popping up and floating away.</summary>
        public void Points(Vector2 at, string text, Color colour, float height = 56f)
        {
            if (_popups.Count > 24) _popups.RemoveAt(0);
            _popups.Add(new Popup { Text = text, Pos = at, Colour = colour, Height = height });
        }

        public void Step(float dt)
        {
            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                var p = _particles[i];
                p.Age += dt;
                if (p.Age >= p.Life)
                {
                    _particles.RemoveAt(i);
                    continue;
                }
                if (p.Drag > -900f)
                {
                    p.Vel.y -= p.Gravity * dt;
                    if (p.Drag > 0f) p.Vel *= Mathf.Exp(-p.Drag * dt);
                    p.Pos += p.Vel * dt;
                }
                p.Angle += p.Spin * dt;
                _particles[i] = p;
            }
            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                var p = _popups[i];
                p.Age += dt;
                p.Pos.y += 90f * dt * Mathf.Max(0f, 1f - p.Age);
                _popups[i] = p;
                if (p.Age > 1.1f) _popups.RemoveAt(i);
            }
        }

        public void Draw(SpriteBatch back, SpriteBatch glow)
        {
            foreach (var p in _particles)
            {
                float k = p.Age / p.Life;
                float size = Mathf.Lerp(p.Size0, p.Size1, k);
                var c = p.Colour;
                c.a *= p.Fade ? 1f - k : k > 0.7f ? (1f - k) / 0.3f : 1f;
                var pos = p.Pos;
                float angle = p.Angle;
                if (p.Drag <= -900f)
                {
                    float a = p.Vel.x + p.Age * 7f;
                    pos += new Vector2(Mathf.Cos(a) * p.Vel.y, Mathf.Sin(a) * p.Vel.y * 0.35f);
                    angle = a * 40f;
                }
                // the sprite's longest side = size
                float unit = size * p.Sprite.Ppt / Mathf.Max(p.Sprite.Size.x, p.Sprite.Size.y);
                (p.Glow ? glow : back).Draw(p.Sprite, pos, unit, angle, c);
            }
            foreach (var p in _popups)
            {
                float pop = p.Age < 0.12f ? Mathf.Lerp(0.4f, 1.25f, p.Age / 0.12f) : p.Age < 0.25f ? Mathf.Lerp(1.25f, 1f, (p.Age - 0.12f) / 0.13f) : 1f;
                var c = p.Colour;
                c.a *= p.Age > 0.8f ? 1f - (p.Age - 0.8f) / 0.3f : 1f;
                Number(back, _art, p.Text, p.Pos, p.Height * pop, c);
            }
        }

        /// <summary>
        /// Text in the sheet's rounded digits ("0".."9", "+", "-", "x"), centred on <paramref name="centre"/> by default
        /// (<paramref name="align"/> 0 = left edge at it, 1 = right edge), <paramref name="height"/> pixels tall.
        /// </summary>
        public static void Number(SpriteBatch batch, ArcadeArt art, string text, Vector2 centre, float height, Color colour, float align = 0.5f)
        {
            if (string.IsNullOrEmpty(text)) return;
            float advance = height * 0.6f, total = advance * text.Length;
            float x = centre.x - total * align + advance * 0.5f;
            foreach (char ch in text)
            {
                string name = ch switch
                {
                    '+' => "n_plus",
                    '-' => "n_minus",
                    'x' => "n_x",
                    _ => ch >= '0' && ch <= '9' ? "n_" + ch : null,
                };
                if (name != null)
                {
                    var s = art[name];
                    batch.Draw(s, new Vector2(x, centre.y), height * s.Ppt / s.Size.y * 1.08f, 0f, colour);
                }
                x += advance;
            }
        }
    }
}
