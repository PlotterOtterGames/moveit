using UnityEngine;

namespace MoveIt.Games
{
    public enum Medal { None, Bronze, Silver, Gold, Platinum }

    /// <summary>Medals from points: each activity says how many points each medal needs (engine-free, tested).</summary>
    public static class Medals
    {
        /// <summary>The best medal <paramref name="points"/> earn against <paramref name="needs"/> (bronze, silver, gold,
        /// platinum).</summary>
        public static Medal For(int points, int[] needs)
        {
            var medal = Medal.None;
            for (int i = 0; i < needs.Length && i < 4; i++)
                if (points >= needs[i]) medal = (Medal)(i + 1);
            return medal;
        }

        /// <summary>Medal points as shares of the most an activity can give (rounded up, never two the same).</summary>
        public static int[] Shares(int max, float bronze = 0.3f, float silver = 0.5f, float gold = 0.7f, float platinum = 0.88f)
        {
            var shares = new[] { bronze, silver, gold, platinum };
            var needs = new int[4];
            for (int i = 0; i < 4; i++)
                needs[i] = Mathf.Max(i == 0 ? 1 : needs[i - 1] + 1, Mathf.CeilToInt(max * shares[i] - 1e-3f));   // (30.000002 is 30)
            return needs;
        }
    }
}
