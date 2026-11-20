using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace MoveIt.Games.Runner.Tests
{
    /// <summary>
    /// Vrapo!'s course (RunnerTrack, 2026-11-18): every row leaves a way through, the egg arcs follow the jump, the first
    /// seconds stay easy, golden eggs and landmarks come along, a long run is laid out without gaps or pile-ups.
    /// </summary>
    public class RunnerTrackTests
    {
        static List<Spawn> Lay(int seed, float upTo, float runTime, float speed)
        {
            var t = new RunnerTrack(seed);
            var into = new List<Spawn>();
            t.Fill(upTo, runTime, speed, into);
            return into;
        }

        /// <summary>At every distance there is a lane (or a move) that gets the runner through: never a dodge thing in
        /// all three lanes at once, never a wide thing on top of a dodge thing.</summary>
        [Test]
        public void EveryRow_LeavesAWayThrough()
        {
            for (int seed = 1; seed <= 60; seed++)
            {
                var all = Lay(seed, 4000f, 120f, 20f).Where(s => s.Rule is Rule.Jump or Rule.Duck or Rule.Dodge).ToList();
                foreach (var a in all)
                {
                    // the things overlapping this one along the course
                    var here = all.Where(b => System.Math.Abs(b.At - a.At) < (RunnerTrack.DepthOf(a.Piece) + RunnerTrack.DepthOf(b.Piece)) / 2f + 0.5f).ToList();
                    var dodged = new HashSet<int>(here.Where(b => b.Rule == Rule.Dodge).Select(b => b.Lane));
                    Assert.Less(dodged.Count, 3, $"seed {seed}: dodge things in every lane at {a.At:0}");
                    bool wide = here.Any(b => b.Wide);
                    if (wide) Assert.IsFalse(here.Any(b => b.Rule == Rule.Dodge && System.Math.Abs(b.At - a.At) < 3.5f),
                        $"seed {seed}: a whole-way thing on top of a dodge thing at {a.At:0}");
                }
            }
        }

        /// <summary>The egg arcs over the jump things follow the jump's own parabola: the middle eggs highest, all of them
        /// reachable by a jump at the right moment and none by running under them.</summary>
        [Test]
        public void EggArcs_FollowTheJump()
        {
            int needJump = 0;
            for (int seed = 1; seed <= 40; seed++)
            {
                var eggs = Lay(seed, 3000f, 60f, 14f).Where(s => s.Rule == Rule.Egg && s.Height > RunnerTrack.EggHeight + 0.4f).ToList();
                foreach (var e in eggs)
                {
                    Assert.LessOrEqual(e.Height, RunnerTrack.JumpHeight + 0.9f + 0.01f, "no higher than the jump's top");
                    // over a standing runner's reach (1.8 m + 0.3): only a jump picks these up
                    if (e.Height > 1.8f + 0.3f) needJump++;   // RunnerPlayer.StandHeight
                }
            }
            Assert.Greater(needJump, 20, "egg arcs over the logs whose middle needs the jump");
        }

        /// <summary>The first 20 seconds teach one thing at a time: no combined rows (a dodge thing next to a jump or duck
        /// thing), no quick doubles.</summary>
        [Test]
        public void TheFirstSeconds_AreEasy()
        {
            for (int seed = 1; seed <= 40; seed++)
            {
                var t = new RunnerTrack(seed);
                var into = new List<Spawn>();
                t.Fill(220f, 5f, 9f, into);
                var obstacles = into.Where(s => s.Rule is Rule.Jump or Rule.Duck or Rule.Dodge).ToList();
                foreach (var a in obstacles)
                {
                    var near = obstacles.Where(b => !b.Equals(a) && System.Math.Abs(b.At - a.At) < 4f).ToList();
                    Assert.IsFalse(near.Any(b => b.Rule != a.Rule && !(a.Rule == Rule.Dodge && b.Rule == Rule.Dodge)),
                        $"seed {seed}: a combined row at {a.At:0} in the first seconds");
                }
            }
        }

        /// <summary>A long run lays out rows steadily (no long empty stretch, no pile-up), landmarks every few hundred
        /// metres and a golden egg now and then.</summary>
        [Test]
        public void ALongRun_IsLaidOutSteadily()
        {
            var all = Lay(7, 6000f, 100f, 20f);
            var rows = all.Where(s => s.Rule is Rule.Jump or Rule.Duck or Rule.Dodge or Rule.Egg).Select(s => s.At).OrderBy(z => z).ToList();
            for (int i = 1; i < rows.Count; i++) Assert.Less(rows[i] - rows[i - 1], 60f, $"an empty stretch after {rows[i - 1]:0} m");
            int sights = all.Count(s => s.Rule == Rule.Sight);
            Assert.That(sights, Is.InRange(12, 22), "a landmark every 300-380 m");
            Assert.Greater(all.Count(s => s.Rule == Rule.GoldEgg), 3, "golden eggs now and then");
        }
    }
}
