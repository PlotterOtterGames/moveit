using NUnit.Framework;
using UnityEngine;

namespace MoveIt.Games.Paint.Tests
{
    /// <summary>
    /// A colouring page's areas (<see cref="PageRegions"/>) and the lock that keeps a brush inside one
    /// (<see cref="RegionLock"/>), on a small page: a line down the middle, a ring on the left of it.
    /// </summary>
    public class PageRegionsTests
    {
        const int W = 240, H = 120;

        /// <summary>A vertical line at x 118..121, a ring (radii 26..30) around (60, 60), and a 3-pixel speck sealed off
        /// inside a blob of ink at (200, 100).</summary>
        static bool[] Walls()
        {
            var walls = new bool[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float d = Mathf.Sqrt((x - 60) * (x - 60) + (y - 60) * (y - 60));
                bool line = x >= 118 && x <= 121;
                bool ring = d >= 26f && d <= 30f;
                bool blob = Mathf.Abs(x - 200) <= 4 && Mathf.Abs(y - 100) <= 3 && !(y == 100 && Mathf.Abs(x - 200) <= 1);
                walls[y * W + x] = line || ring || blob;
            }
            return walls;
        }

        static PageRegions Page() => PageRegions.FromWalls(Walls(), W, H, minArea: 20, countArea: 300, backgroundShare: 0.4f);

        [Test]
        public void TheLinesMakeAreas_AndEveryPixelBelongsToOne()
        {
            var page = Page();
            Assert.AreEqual(3, page.Count, "left of the line (around the ring), inside the ring, right of the line; the speck is no area");
            int inside = page.At(60, 60), left = page.At(10, 10), right = page.At(180, 30);
            Assert.AreNotEqual(inside, left);
            Assert.AreNotEqual(left, right);
            Assert.AreNotEqual(inside, right);
            foreach (byte label in page.Labels) Assert.AreNotEqual(0, label, "line pixels go to the nearest area");
            Assert.AreEqual(left, page.At(118, 60), "the line's left half belongs to the left area");
            Assert.AreEqual(right, page.At(121, 60), "...its right half to the right one: paint from both sides meets under the line");
            Assert.AreEqual(right, page.At(200, 100), "the speck sealed in ink joined the area around it");
            Assert.AreEqual(right, page.At(500, -20), "points off the page are clamped onto it");
        }

        [Test]
        public void OnlyAreasForAHand_NotTheBackground_CountTowardFinished()
        {
            var page = Page();
            int inside = page.At(60, 60), left = page.At(10, 10), right = page.At(180, 30);
            Assert.IsTrue(page.Counts(inside), "the ring's inside: ~2100 px");
            Assert.IsTrue(page.Counts(left), "left of the line: under 40 % of the page");
            Assert.IsFalse(page.Counts(right), "right of the line: 49 % of the page, a background");
            Assert.AreEqual(2, page.CountedTotal);
            Assert.IsFalse(page.Finished);
            page.MarkDone(inside);
            page.MarkDone(left);
            Assert.IsTrue(page.Finished, "the picture is finished without the background");
        }

        [Test]
        public void Colouring_CoversAnArea_UntilItShouldFillItself()
        {
            var page = Page();
            int inside = page.At(60, 60);
            Assert.AreEqual(0f, page.Coverage(inside));
            Assert.IsFalse(page.ShouldComplete(inside));
            // scribble across the lower half of the ring's inside with a 16 px brush
            for (int x = 36; x <= 84; x += 4) page.Cover(inside, new Vector2(x, 50), 8f);
            float half = page.Coverage(inside);
            Assert.Greater(half, 0.25f);
            Assert.Less(half, 0.7f);
            Assert.IsFalse(page.ShouldComplete(inside), "not coloured enough yet");
            for (int y = 40; y <= 80; y += 6)
            for (int x = 36; x <= 84; x += 4)
                page.Cover(inside, new Vector2(x, y), 8f);
            Assert.GreaterOrEqual(page.Coverage(inside), 0.7f);
            Assert.IsTrue(page.ShouldComplete(inside), "mostly coloured: the game fills the gaps");
            page.MarkDone(inside);
            Assert.IsTrue(page.Done(inside));
            Assert.AreEqual(1f, page.Coverage(inside));
            Assert.IsFalse(page.ShouldComplete(inside), "only once");
        }

        [Test]
        public void AStampCoversOnlyItsOwnArea()
        {
            var page = Page();
            int inside = page.At(60, 60), left = page.At(10, 10);
            page.Cover(inside, new Vector2(60, 60), 40f);   // a huge brush, clipped to the ring's inside by the game
            Assert.Greater(page.Coverage(inside), 0.9f);
            Assert.AreEqual(0f, page.Coverage(left), "the area around the ring got no paint");
        }

        [Test]
        public void TheEraser_OpensAFinishedAreaAgain()
        {
            var page = Page();
            int inside = page.At(60, 60);
            page.MarkDone(inside);
            page.Uncover(new Vector2(60, 60), 14f);
            Assert.IsFalse(page.Done(inside));
            Assert.Less(page.Coverage(inside), 1f);
            Assert.Greater(page.Coverage(inside), 0.5f, "only what the eraser rubbed is open");
        }

        [Test]
        public void Undo_BringsBackWhatWasColoured()
        {
            var page = Page();
            int inside = page.At(60, 60);
            page.Cover(inside, new Vector2(50, 50), 8f);
            float before = page.Coverage(inside);
            var saved = page.Save();
            page.Cover(inside, new Vector2(70, 70), 12f);
            page.MarkDone(inside);
            page.Restore(saved);
            Assert.AreEqual(before, page.Coverage(inside), 1e-5f);
            Assert.IsFalse(page.Done(inside));
            page.Clear();
            Assert.AreEqual(0f, page.Coverage(inside));
        }

        [Test]
        public void TheMiddleOfAnArea_IsInsideIt()
        {
            var page = Page();
            foreach (var probe in new[] { new Vector2(60, 60), new Vector2(10, 10), new Vector2(180, 30) })
            {
                int id = page.At(probe.x, probe.y);
                var middle = page.Middle(id);
                Assert.AreEqual(id, page.At(middle.x, middle.y), $"area {id}: its middle {middle} lies in it (the area around the ring is a hollow shape)");
            }
        }

        [Test]
        public void TheLock_KeepsABrushInItsArea_UntilItGoesWellIntoAnother()
        {
            var page = Page();
            int left = page.At(100, 20), right = page.At(140, 20);
            var brush = new RegionLock { SwitchDistance = 24f, SwitchSeconds = 0.6f };
            Assert.IsFalse(brush.Step(left, new Vector2(100, 20)));
            Assert.AreEqual(left, brush.Region, "locked where the brush went down");
            // a scribble overshoots the line by 12 px and comes back, three times
            for (int pass = 0; pass < 3; pass++)
            {
                for (int x = 102; x <= 132; x += 3) brush.Step(page.At(x, 20), new Vector2(x, 20));
                Assert.AreEqual(left, brush.Region, "a little past the line: still the left area (its stamps are clipped there)");
                for (int x = 132; x >= 100; x -= 3) brush.Step(page.At(x, 20), new Vector2(x, 20));
                Assert.AreEqual(left, brush.Region);
            }
            // now it goes on into the right area
            bool switched = false;
            for (int x = 102; x <= 160 && !switched; x += 3) switched = brush.Step(page.At(x, 20), new Vector2(x, 20));
            Assert.IsTrue(switched);
            Assert.AreEqual(right, brush.Region, "24 px into the next area: that one is being coloured now");
            Assert.AreEqual(right, page.At(brush.EnteredAt.x, brush.EnteredAt.y), "it remembers where it crossed, to paint that bit after all");
            Assert.Less(brush.EnteredAt.x, 126f);
        }

        [Test]
        public void TheLock_AlsoFollowsABrushThatStaysInAnotherArea()
        {
            var brush = new RegionLock { SwitchDistance = 24f, SwitchSeconds = 0.6f };
            brush.Step(1, Vector2.zero);
            bool switched = false;
            int frames = 0;
            for (; frames < 60 && !switched; frames++) switched = brush.Step(2, new Vector2(5f, 0f), 1f / 60f);
            Assert.IsTrue(switched, "0.6 s in the other area, hardly moving: it follows");
            Assert.AreEqual(36, frames, 1);
            Assert.AreEqual(2, brush.Region);
            brush.Reset();
            Assert.AreEqual(0, brush.Region);
        }
    }
}
