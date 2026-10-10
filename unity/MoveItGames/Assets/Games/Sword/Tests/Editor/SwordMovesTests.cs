using NUnit.Framework;
using UnityEngine;

namespace MoveIt.Games.Sword.Tests
{
    /// <summary>The sword and shield rules on the screen (x right, y up, screen heights), fed with positions.</summary>
    public sealed class SwordMovesTests
    {
        const float Frame = 1f / 60f;

        /// <summary>Moves a 0.4-long upright blade from <paramref name="from"/> to <paramref name="to"/> (its hilt) in
        /// <paramref name="seconds"/>, at 60 fps.</summary>
        static void Sweep(SwordMoves moves, Vector2 from, Vector2 to, float seconds, System.Action<SwordMoves> eachFrame = null)
        {
            int frames = Mathf.Max(1, Mathf.RoundToInt(seconds / Frame));
            for (int i = 0; i <= frames; i++)
            {
                var hilt = Vector2.Lerp(from, to, i / (float)frames);
                moves.Update(hilt, hilt + new Vector2(0f, 0.4f), Frame);
                eachFrame?.Invoke(moves);
            }
        }

        [Test]
        public void Classify_PicksTheNearestOfTheFourCuts()
        {
            Assert.AreEqual(Cut.Right, SwordMoves.Classify(new Vector2(2f, 0.5f)));
            Assert.AreEqual(Cut.Left, SwordMoves.Classify(new Vector2(-2f, -1f)));
            Assert.AreEqual(Cut.Down, SwordMoves.Classify(new Vector2(0.3f, -2f)));
            Assert.AreEqual(Cut.Up, SwordMoves.Classify(new Vector2(-0.5f, 1f)));
            Assert.AreEqual(Cut.None, SwordMoves.Classify(Vector2.zero));
        }

        [Test]
        public void AFastSweep_IsOneSwing_ASlowOneIsNot()
        {
            var moves = new SwordMoves();
            bool swung = false;
            Sweep(moves, new Vector2(0.5f, 0.3f), new Vector2(0.9f, 0.3f), 1.0f, m => swung |= m.Swinging);   // 0.4 screens/s
            Assert.IsFalse(swung, "moving the sword into place slowly is not a swing");

            int before = moves.SwingId;
            Sweep(moves, new Vector2(0.9f, 0.3f), new Vector2(0.2f, 0.3f), 0.2f);                               // 3.5 screens/s
            Assert.AreEqual(before + 1, moves.SwingId, "one swing");
            Assert.AreEqual(Cut.Left, moves.SwingCut);
            Sweep(moves, new Vector2(0.2f, 0.3f), new Vector2(0.2f, 0.3f), 0.3f);
            Assert.IsFalse(moves.Swinging, "the swing ends when the sword stops");
        }

        [Test]
        public void SweptThrough_CatchesAFastBladeBetweenFrames()
        {
            var moves = new SwordMoves();
            var knight = new Vector2(0.8f, 0.6f);
            // one frame jumps the blade from the knight's left to its right: it went through
            moves.Update(new Vector2(0.4f, 0.4f), new Vector2(0.5f, 0.75f), Frame);
            moves.Update(new Vector2(1.1f, 0.4f), new Vector2(1.2f, 0.75f), Frame);
            Assert.IsTrue(moves.SweptThrough(knight, 0.1f));
            // the same sweep well below the knight misses
            var low = new SwordMoves();
            low.Update(new Vector2(0.4f, 0.0f), new Vector2(0.5f, 0.2f), Frame);
            low.Update(new Vector2(1.1f, 0.0f), new Vector2(1.2f, 0.2f), Frame);
            Assert.IsFalse(low.SweptThrough(knight, 0.1f));
        }

        static SwordMoves Held(Vector2 hilt, Vector2 tip)
        {
            var moves = new SwordMoves();
            for (int i = 0; i < 10; i++) moves.Update(hilt, tip, Frame);
            return moves;
        }

        [Test]
        public void Parries_NeedTheSwordAcrossTheBlow()
        {
            var chest = new Vector2(0.9f, 0.25f);
            const float shoulders = 0.32f;
            var level = Held(new Vector2(1.1f, 0.55f), new Vector2(0.7f, 0.6f));      // level, above the head
            Assert.IsTrue(level.Parries(AttackFrom.Overhead, chest, shoulders));
            Assert.IsFalse(level.Parries(AttackFrom.Left, chest, shoulders), "a level sword doesn't stop a blow from the side");

            var upLeft = Held(new Vector2(0.65f, 0.3f), new Vector2(0.62f, 0.7f));    // upright, left of the chest
            Assert.IsTrue(upLeft.Parries(AttackFrom.Left, chest, shoulders));
            Assert.IsFalse(upLeft.Parries(AttackFrom.Right, chest, shoulders), "the blow from the right lands on the open side");
            Assert.IsFalse(upLeft.Parries(AttackFrom.Overhead, chest, shoulders));

            var upRight = Held(new Vector2(1.15f, 0.3f), new Vector2(1.2f, 0.7f));
            Assert.IsTrue(upRight.Parries(AttackFrom.Right, chest, shoulders));

            var low = Held(new Vector2(1.1f, 0.1f), new Vector2(0.7f, 0.12f));        // level but down at the hips
            Assert.IsFalse(low.Parries(AttackFrom.Overhead, chest, shoulders), "an overhead blow needs the sword up high");

            var waving = new SwordMoves();                                           // level and high, but swinging
            for (int i = 0; i <= 12; i++)
            {
                var hilt = Vector2.Lerp(new Vector2(1.3f, 0.55f), new Vector2(0.6f, 0.55f), i / 12f);
                waving.Update(hilt, hilt + new Vector2(-0.4f, 0.05f), Frame);
            }
            Assert.IsFalse(waving.Parries(AttackFrom.Overhead, chest, shoulders), "a parry is held, not swung");
        }

        [Test]
        public void ShieldCovers_TheSpotWhenRoughlyOverIt()
        {
            var moves = new SwordMoves();
            var spot = new Vector2(0.9f, 0.7f);
            Assert.IsFalse(moves.ShieldCovers(spot, 0.1f), "no shield yet");
            moves.UpdateShield(new Vector2(0.95f, 0.66f), 0.12f);
            Assert.IsTrue(moves.ShieldCovers(spot, 0.1f), "a little off the middle still blocks");
            moves.UpdateShield(new Vector2(1.06f, 0.7f), 0.12f);
            Assert.IsTrue(moves.ShieldCovers(spot, 0.1f), "the shield's own size plus a bit of the spot's");
            moves.UpdateShield(new Vector2(1.25f, 0.7f), 0.12f);
            Assert.IsFalse(moves.ShieldCovers(spot, 0.1f), "beside the spot: the blow gets through");
            moves.UpdateShield(new Vector2(0.9f, 0.3f), 0.12f);
            Assert.IsFalse(moves.ShieldCovers(spot, 0.1f), "a shield held low doesn't stop a blow from above");
        }
    }
}
