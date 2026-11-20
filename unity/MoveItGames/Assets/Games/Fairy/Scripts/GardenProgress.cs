using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MoveIt.Games.Fairy
{
    /// <summary>
    /// What a family keeps between Zana!'s runs (2026-11-16): the stardust ever gathered (it opens things: earned, never
    /// spent, like Fluturo!'s outfits), the best wave on each difficulty, every wave ever saved (the garden grows a
    /// flower, a mushroom or a crystal for each, up to <see cref="MaxDecorations"/>), the dress, the wings and the
    /// element chosen. Saved as one line of text. Pure logic, tested.
    /// </summary>
    public sealed class GardenProgress
    {
        /// <summary>Stardust that opens each pair of wings (FairyLooks.AllWings), each dress (FairyLooks.Dresses), and the
        /// two spells that aren't there from the start.</summary>
        public static readonly int[] WingsAt = { 0, 150, 350, 650, 1000, 1500, 2100, 2800 };
        public static readonly int[] DressAt = { 0, 250, 550, 900, 1400, 2000 };
        public const int FreezeAt = 400, HealAt = 1000;
        public const int MaxDecorations = 40;

        public int Stardust;
        public readonly int[] Best = new int[3];
        public int Waves, Runs;
        public int Dress, Wings;
        public Element Element;
        public float Seconds;

        public bool WingsOpen(int i) => i >= 0 && i < WingsAt.Length && Stardust >= WingsAt[i];
        public bool DressOpen(int i) => i >= 0 && i < DressAt.Length && Stardust >= DressAt[i];
        public bool HasFreeze => Stardust >= FreezeAt;
        public bool HasHeal => Stardust >= HealAt;
        public int Decorations => Math.Min(MaxDecorations, Waves);

        /// <summary>The spells a run starts with (a mask for <see cref="GardenRun.Spells"/>).</summary>
        public int SpellMask
        {
            get
            {
                int m = GardenRun.BaseSpells;
                if (HasFreeze) m |= 1 << (int)Spell.Freeze;
                if (HasHeal) m |= 1 << (int)Spell.Heal;
                return m;
            }
        }

        /// <summary>What a run's end opened.</summary>
        public struct Opened
        {
            public bool Wings, Dress, Freeze, Heal, Best;
            public bool Any => Wings || Dress || Freeze || Heal;
        }

        /// <summary>A run is over: its stardust and waves are kept, the best wave beaten or not, what opened.</summary>
        public Opened Add(GardenRun run, float seconds)
        {
            var before = Clone();
            Stardust += run.Stardust;
            Waves += run.WavesCleared;
            Runs++;
            Seconds += seconds;
            int d = (int)run.Difficulty;
            var o = new Opened { Best = run.WavesCleared > Best[d] };
            if (o.Best) Best[d] = run.WavesCleared;
            for (int i = 0; i < WingsAt.Length; i++)
                if (WingsOpen(i) && !before.WingsOpen(i))
                {
                    o.Wings = true;
                    Wings = i;
                }
            for (int i = 0; i < DressAt.Length; i++)
                if (DressOpen(i) && !before.DressOpen(i))
                {
                    o.Dress = true;
                    Dress = i;
                }
            o.Freeze = HasFreeze && !before.HasFreeze;
            o.Heal = HasHeal && !before.HasHeal;
            return o;
        }

        public GardenProgress Clone() => Load(Save());

        public string Save()
        {
            var sb = new StringBuilder();
            sb.Append("dust=").Append(Stardust);
            sb.Append(";best=").Append(Best[0]).Append(',').Append(Best[1]).Append(',').Append(Best[2]);
            sb.Append(";waves=").Append(Waves);
            sb.Append(";runs=").Append(Runs);
            sb.Append(";dress=").Append(Dress);
            sb.Append(";wings=").Append(Wings);
            sb.Append(";element=").Append((int)Element);
            sb.Append(";seconds=").Append(Seconds.ToString("0", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        public static GardenProgress Load(string text)
        {
            var p = new GardenProgress();
            if (string.IsNullOrEmpty(text)) return p;
            var values = new Dictionary<string, string>();
            foreach (var part in text.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq > 0) values[part.Substring(0, eq).Trim()] = part.Substring(eq + 1).Trim();
            }
            int Int(string key, int fallback = 0) =>
                values.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : fallback;
            p.Stardust = Math.Max(0, Int("dust"));
            if (values.TryGetValue("best", out var best))
            {
                var b = best.Split(',');
                for (int i = 0; i < 3 && i < b.Length; i++)
                    if (int.TryParse(b[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) p.Best[i] = Math.Max(0, n);
            }
            p.Waves = Math.Max(0, Int("waves"));
            p.Runs = Math.Max(0, Int("runs"));
            p.Dress = Clamp(Int("dress"), 0, DressAt.Length - 1);
            p.Wings = Clamp(Int("wings"), 0, WingsAt.Length - 1);
            p.Element = (Element)Clamp(Int("element"), 0, 3);
            if (values.TryGetValue("seconds", out var s) && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                p.Seconds = Math.Max(0f, f);
            // what's worn must be open
            if (!p.DressOpen(p.Dress)) p.Dress = 0;
            if (!p.WingsOpen(p.Wings)) p.Wings = 0;
            return p;
        }

        static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
