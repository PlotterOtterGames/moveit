using System;
using System.Collections.Generic;
using System.Globalization;

namespace MoveIt.Games.Dress
{
    /// <summary>
    /// What the player wears: one item (or nothing) per slot, each in a chosen colour, plus the magic effect.
    /// Choosing the item that is already worn changes its colour (like Pikturo's stamps: press again for the next
    /// one); after the item's colourways come the finishes unlocked so far (rainbow, glitter, gold, galaxy).
    /// No engine types: tested in EditMode and in tools/logic-tests.
    /// </summary>
    public sealed class DressLook
    {
        public readonly int[] Item = new int[DressCatalog.SlotCount];
        public readonly int[] Shade = new int[DressCatalog.SlotCount];
        public Effect Effect;

        public DressLook() => Clear();

        public void Clear()
        {
            for (int i = 0; i < Item.Length; i++)
            {
                Item[i] = -1;
                Shade[i] = 0;
            }
            Effect = Effect.None;
        }

        public void CopyFrom(DressLook other)
        {
            Array.Copy(other.Item, Item, Item.Length);
            Array.Copy(other.Shade, Shade, Shade.Length);
            Effect = other.Effect;
        }

        public DressItem Worn(Slot slot)
        {
            int i = Item[(int)slot];
            var items = DressCatalog.Items[(int)slot];
            return i >= 0 && i < items.Length ? items[i] : null;
        }

        public bool IsWorn(Slot slot) => Worn(slot) != null;

        public int Count
        {
            get
            {
                int n = 0;
                for (int s = 0; s < DressCatalog.SlotCount; s++)
                    if (IsWorn((Slot)s)) n++;
                return n;
            }
        }

        /// <summary>Colour choices an item has: its colourways, then the unlocked finishes.</summary>
        public static int Choices(DressItem item, int finishes) => item.Colors.Length + Math.Max(0, finishes);

        /// <summary>The palette colour of the worn item (for a finish: its first colourway, the finish's base).</summary>
        public int ColorOf(Slot slot)
        {
            var item = Worn(slot);
            if (item == null) return -1;
            int s = Shade[(int)slot];
            return s < item.Colors.Length ? item.Colors[s] : item.Colors[0];
        }

        public Finish FinishOf(Slot slot)
        {
            var item = Worn(slot);
            if (item == null) return Finish.Plain;
            int s = Shade[(int)slot] - item.Colors.Length;
            return s < 0 ? Finish.Plain : (Finish)Math.Min(1 + s, (int)Finish.Galaxy);
        }

        public enum Change { Wore, Recolored, Removed, Nothing }

        /// <summary>
        /// Picks item <paramref name="index"/> of the slot (-1 = take it off). The same item again: the next colour.
        /// </summary>
        public Change Wear(Slot slot, int index, int finishes)
        {
            int s = (int)slot;
            var items = DressCatalog.Items[s];
            if (index < 0 || index >= items.Length)
            {
                if (Item[s] < 0) return Change.Nothing;
                Item[s] = -1;
                return Change.Removed;
            }
            if (Item[s] == index)
            {
                Shade[s] = (Shade[s] + 1) % Choices(items[index], finishes);
                return Change.Recolored;
            }
            Item[s] = index;
            Shade[s] = 0;
            return Change.Wore;
        }

        /// <summary>The next magic effect (off after the last).</summary>
        public Effect NextEffect()
        {
            Effect = (Effect)(((int)Effect + 1) % (int)(Effect.Stars + 1));
            return Effect;
        }

        /// <summary>
        /// "Surprise me": a whole look for a theme (a random one if null): a top and a skirt that fit it, most of the
        /// accessories, colours the theme likes, and its effect now and then.
        /// </summary>
        public Theme Surprise(Random rng, Theme? theme = null, int finishes = 0)
        {
            var t = theme ?? (Theme)rng.Next(DressCatalog.ThemeCount);
            Clear();
            double[] chance = { 1, 1, 0.9, 0.75, 0.55, 0.7, 0.6 };
            for (int s = 0; s < DressCatalog.SlotCount; s++)
            {
                if (rng.NextDouble() > chance[s]) continue;
                var items = DressCatalog.Items[s];
                var fitting = new List<int>();
                for (int i = 0; i < items.Length; i++)
                    if (items[i].Fits(t)) fitting.Add(i);
                if (fitting.Count == 0)
                {
                    if (s < 2) fitting.Add(rng.Next(items.Length));   // always a top and a skirt
                    else continue;
                }
                int pick = fitting[rng.Next(fitting.Count)];
                Item[s] = pick;
                Shade[s] = PreferredShade(items[pick], t, rng);
                if (finishes > 0 && rng.NextDouble() < 0.12) Shade[s] = items[pick].Colors.Length + rng.Next(finishes);
            }
            if (rng.NextDouble() < 0.6)
                for (int e = 1; e <= (int)Effect.Stars; e++)
                    if (DressCatalog.EffectFits((Effect)e, t) && (Effect == Effect.None || rng.NextDouble() < 0.5)) Effect = (Effect)e;
            return t;
        }

        static int PreferredShade(DressItem item, Theme theme, Random rng)
        {
            var good = new List<int>();
            var colors = DressCatalog.ThemeColors[(int)theme];
            for (int i = 0; i < item.Colors.Length; i++)
                if (Array.IndexOf(colors, item.Colors[i]) >= 0) good.Add(i);
            return good.Count > 0 ? good[rng.Next(good.Count)] : 0;
        }

        // ---------------------------------------------------------------- saving (one line of text)

        public string Save()
        {
            var parts = new List<string> { "1", ((int)Effect).ToString(CultureInfo.InvariantCulture) };
            for (int s = 0; s < DressCatalog.SlotCount; s++) parts.Add($"{Item[s]}:{Shade[s]}");
            return string.Join("|", parts);
        }

        public static DressLook Load(string text, int finishes)
        {
            var look = new DressLook();
            if (string.IsNullOrEmpty(text)) return look;
            var parts = text.Split('|');
            if (parts.Length != 2 + DressCatalog.SlotCount || parts[0] != "1") return look;
            try
            {
                int e = int.Parse(parts[1], CultureInfo.InvariantCulture);
                look.Effect = e >= 0 && e <= (int)Effect.Stars ? (Effect)e : Effect.None;
                for (int s = 0; s < DressCatalog.SlotCount; s++)
                {
                    var f = parts[2 + s].Split(':');
                    if (f.Length != 2) continue;
                    int item = int.Parse(f[0], CultureInfo.InvariantCulture), shade = int.Parse(f[1], CultureInfo.InvariantCulture);
                    var items = DressCatalog.Items[s];
                    if (item < 0 || item >= items.Length) continue;
                    look.Item[s] = item;
                    look.Shade[s] = shade >= 0 && shade < Choices(items[item], finishes) ? shade : 0;
                }
            }
            catch (FormatException)
            {
                return new DressLook();
            }
            catch (OverflowException)
            {
                return new DressLook();
            }
            return look;
        }
    }

    /// <summary>Between visits: stars from the fashion shows (they unlock the finishes), shows played, the best show.</summary>
    public sealed class DressProgress
    {
        public int Stars, Shows, BestShow;

        public int Finishes => DressCatalog.FinishesUnlocked(Stars);

        /// <summary>Stars still needed for the next finish, -1 when all are unlocked.</summary>
        public int StarsToNext => Finishes < DressCatalog.FinishStars.Length ? DressCatalog.FinishStars[Finishes] - Stars : -1;

        /// <summary>Adds a show's stars. Returns the finishes it unlocked (usually none).</summary>
        public List<Finish> AddShow(int stars)
        {
            int before = Finishes;
            Stars += Math.Max(0, stars);
            Shows++;
            BestShow = Math.Max(BestShow, stars);
            var unlocked = new List<Finish>();
            for (int k = before; k < Finishes; k++) unlocked.Add((Finish)(k + 1));
            return unlocked;
        }

        public string Save() => string.Join("|", "1", Stars.ToString(CultureInfo.InvariantCulture),
            Shows.ToString(CultureInfo.InvariantCulture), BestShow.ToString(CultureInfo.InvariantCulture));

        public static DressProgress Load(string text)
        {
            var p = new DressProgress();
            if (string.IsNullOrEmpty(text)) return p;
            var parts = text.Split('|');
            if (parts.Length < 4 || parts[0] != "1") return p;
            try
            {
                p.Stars = Math.Max(0, int.Parse(parts[1], CultureInfo.InvariantCulture));
                p.Shows = Math.Max(0, int.Parse(parts[2], CultureInfo.InvariantCulture));
                p.BestShow = Math.Max(0, int.Parse(parts[3], CultureInfo.InvariantCulture));
            }
            catch (FormatException)
            {
                return new DressProgress();
            }
            catch (OverflowException)
            {
                return new DressProgress();
            }
            return p;
        }
    }
}
