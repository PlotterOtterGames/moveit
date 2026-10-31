using UnityEngine;

namespace MoveIt.Games.Tennis
{
    /// <summary>
    /// How strong a computer player is: match 1 is gentle (slow balls, mostly back to you, many mistakes), each next
    /// player hits harder, misses less, aims into the corners more, plays short balls and lobs, serves faster and copes
    /// with hard balls better. Every number is capped, so the ladder never becomes impossible. <see cref="For"/> holds
    /// them all.
    /// </summary>
    public sealed class TennisLevel
    {
        /// <summary>Match number on the ladder, 1 = the first player.</summary>
        public int Number;
        /// <summary>Their usual rally speed (m/s across the court).</summary>
        public float Speed;
        /// <summary>How often they get a normal ball back (before the ball's difficulty is taken off).</summary>
        public float Return;
        /// <summary>How much a hard ball (fast, perfect, smashed, far away) takes off <see cref="Return"/>: 1 at first,
        /// less for later players.</summary>
        public float Pressure;
        /// <summary>How often a shot they hit goes long, wide or into the net.</summary>
        public float Error;
        /// <summary>How often they aim into the corner away from you instead of back to you.</summary>
        public float AimAway;
        /// <summary>How often they play a short, slow ball (you have to step toward the TV for it).</summary>
        public float ShortChance;
        public float LobChance;
        public float ServeSpeed;
        public float PowerServeChance;
        /// <summary>The player's shot goes wide when its timing is beyond this share of the window (early or late):
        /// never against the first two players (children win their first matches), the window's last 10 % against the
        /// 3rd and 4th, the last 25 % from the 5th on (else a player who moves well never loses a point).</summary>
        public float WideFrom;
        /// <summary>Their run speed (m/s) and how long before they react to a shot (s).</summary>
        public float RunSpeed, Reaction;
        /// <summary>How much their aim wanders (s of timing; metres = 3x).</summary>
        public float Wobble;
        /// <summary>Their first name on the scoreboard (the user should review the names).</summary>
        public string Name;
        /// <summary>Their shirt colour index (the game's palette).</summary>
        public int Colour;

        static readonly string[] Names = { "Ana", "Beni", "Era", "Dini", "Lea", "Toni", "Mira", "Leka", "Nora", "Gent" };

        public static TennisLevel For(int n)
        {
            n = Mathf.Max(1, n);
            int k = n - 1;
            return new TennisLevel
            {
                Number = n,
                Speed = Mathf.Min(19f, 11.5f + 1.0f * k),
                Return = Mathf.Min(0.97f, 0.8f + 0.025f * k),
                Pressure = Mathf.Max(0.35f, 1f - 0.08f * k),
                Error = Mathf.Max(0.04f, 0.14f - 0.015f * k),
                AimAway = Mathf.Min(0.7f, 0.12f + 0.09f * k),
                ShortChance = Mathf.Min(0.18f, 0.03f * k),
                LobChance = Mathf.Min(0.14f, 0.03f + 0.02f * k),
                ServeSpeed = Mathf.Min(19f, 12f + 1f * k),
                PowerServeChance = Mathf.Min(0.35f, 0.06f * k),
                RunSpeed = Mathf.Min(8.5f, 6.5f + 0.3f * k),
                Reaction = Mathf.Max(0.15f, 0.3f - 0.02f * k),
                Wobble = Mathf.Max(0.08f, 0.22f - 0.02f * k),
                WideFrom = n <= 2 ? 99f : n <= 4 ? 0.9f : 0.75f,
                Name = Names[k % Names.Length],
                Colour = k % 4,
            };
        }
    }
}
