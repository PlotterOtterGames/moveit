using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace MoveIt.Games.Fly
{
    /// <summary>What a mission asks for, in one run.</summary>
    public enum MissionKind
    {
        SmashRobots, SmashDrones, WinClashes, ReachDistrict, CollectStars, Combo, QuickClash, BeatBoss, FullSwarm, ReturnSparks,
        CleanRush,
    }

    /// <summary>One of the three missions on offer: reach <see cref="Target"/> in one run for <see cref="Reward"/> stars
    /// (for <see cref="MissionKind.QuickClash"/>, win a clash in <see cref="Target"/> seconds or less).</summary>
    public sealed class Mission
    {
        public MissionKind Kind;
        public int Target, Reward;
        /// <summary>How far this run has got (0 between runs).</summary>
        public int Progress;
        /// <summary>Done in this run: its stars are counted, it's replaced when the run ends.</summary>
        public bool Done;
    }

    /// <summary>A run's numbers the missions look at (a snapshot of <see cref="FlyRun"/>).</summary>
    public struct RunStats
    {
        public int Score, District, RobotsSmashed, DronesSmashed, ClashesWon, StarsCollected, BestCombo, BossesBeaten,
                   SwarmsCleared, FireballsReturned, CleanRushes;
        /// <summary>The quickest clash won (s), large before one.</summary>
        public float QuickestClash;

        public static RunStats Of(FlyRun run) => new RunStats
        {
            Score = run.Score, District = run.District, RobotsSmashed = run.RobotsSmashed, DronesSmashed = run.DronesSmashed,
            ClashesWon = run.ClashesWon, StarsCollected = run.StarsCollected, BestCombo = run.BestCombo,
            BossesBeaten = run.BossesBeaten, SwarmsCleared = run.SwarmsCleared, FireballsReturned = run.FireballsReturned,
            CleanRushes = run.CleanRushes, QuickestClash = run.QuickestClash,
        };
    }

    /// <summary>What a run brought, for the results screen.</summary>
    public sealed class RunResult
    {
        public int StarsFromRun, StarsFromMissions, Score, District;
        public bool NewBestScore, NewBestDistrict;
        public readonly List<Mission> Completed = new List<Mission>();
        /// <summary>Outfits unlocked by this run's stars (indexes into <see cref="FlyProgress.OutfitStars"/>).</summary>
        public readonly List<int> NewOutfits = new List<int>();
    }

    /// <summary>
    /// Fluturo!'s progress between runs, kept on the box (no accounts, nothing sold): the endless runner recipe of Jetpack
    /// Joyride and Subway Surfers without the shop. <b>Stars</b> collected in every run add up for good and unlock the
    /// hero's outfits one after another (the newest is put on at once); <b>three missions</b> at a time, each to do in
    /// one run, give bonus stars and are replaced by a harder one; and the best score and district. Pure logic (tests);
    /// <see cref="FlyGame"/> saves it with <see cref="Save"/> / <see cref="Load"/>.
    /// </summary>
    public sealed class FlyProgress
    {
        /// <summary>Stars (all runs together) that unlock each outfit; the first is the hero's own.</summary>
        public static readonly int[] OutfitStars = { 0, 40, 100, 180, 300, 450, 650, 900 };
        public const int MissionSlots = 3;

        public int Stars, MissionsDone, BestScore, BestDistrict, Outfit;
        public readonly List<Mission> Missions = new List<Mission>();

        /// <summary>Outfits unlocked so far (always at least the first).</summary>
        public int Unlocked => UnlockedAt(Stars);

        /// <summary>Stars needed for the next outfit, -1 when all are unlocked.</summary>
        public int NextOutfitStars => Unlocked < OutfitStars.Length ? OutfitStars[Unlocked] : -1;

        public FlyProgress() => Fill();

        /// <summary>A run starts: the missions count from zero.</summary>
        public void BeginRun()
        {
            foreach (var m in Missions)
            {
                m.Progress = 0;
                m.Done = false;
            }
        }

        /// <summary>The run so far (call every step): missions it has just completed (their stars are added at once).</summary>
        public List<Mission> Update(FlyRun run) => Update(RunStats.Of(run));

        public List<Mission> Update(RunStats run)
        {
            List<Mission> done = null;
            foreach (var m in Missions)
            {
                if (m.Done) continue;
                m.Progress = ProgressOf(m, run);
                if (m.Progress < Goal(m)) continue;
                m.Done = true;
                MissionsDone++;
                Stars += m.Reward;
                (done ??= new List<Mission>()).Add(m);
            }
            return done ?? NoMissions;
        }

        static readonly List<Mission> NoMissions = new List<Mission>();

        /// <summary>The run is over: its stars added, the bests, finished missions replaced, new outfits (the newest worn).</summary>
        public RunResult EndRun(FlyRun run) => EndRun(RunStats.Of(run));

        public RunResult EndRun(RunStats run)
        {
            Update(run);
            var result = new RunResult { StarsFromRun = run.StarsCollected, Score = run.Score, District = run.District };
            // (the missions' stars were added as they were done)
            int unlockedBefore = UnlockedAt(Stars - MissionStarsThisRun());
            Stars += run.StarsCollected;
            foreach (var m in Missions)
                if (m.Done)
                {
                    result.Completed.Add(m);
                    result.StarsFromMissions += m.Reward;
                }
            result.NewBestScore = run.Score > BestScore;
            result.NewBestDistrict = run.District > BestDistrict;
            BestScore = Mathf.Max(BestScore, run.Score);
            BestDistrict = Mathf.Max(BestDistrict, run.District);
            for (int i = unlockedBefore; i < Unlocked; i++) result.NewOutfits.Add(i);
            if (result.NewOutfits.Count > 0) Outfit = result.NewOutfits[result.NewOutfits.Count - 1];
            Missions.RemoveAll(m => m.Done);
            Fill();
            BeginRun();
            return result;
        }

        int MissionStarsThisRun()
        {
            int n = 0;
            foreach (var m in Missions)
                if (m.Done) n += m.Reward;
            return n;
        }

        static int UnlockedAt(int stars)
        {
            int n = 0;
            while (n < OutfitStars.Length && OutfitStars[n] <= stars) n++;
            return Mathf.Max(1, n);
        }

        /// <summary>Wears the next (or, <paramref name="step"/> -1, the previous) unlocked outfit.</summary>
        public void CycleOutfit(int step) => Outfit = ((Outfit + step) % Unlocked + Unlocked) % Unlocked;

        /// <summary>The number to reach (for a quick clash: 1, it's done or not).</summary>
        public static int Goal(Mission m) => m.Kind == MissionKind.QuickClash ? 1 : m.Target;

        static int ProgressOf(Mission m, RunStats run) => m.Kind switch
        {
            MissionKind.SmashRobots => run.RobotsSmashed,
            MissionKind.SmashDrones => run.DronesSmashed,
            MissionKind.WinClashes => run.ClashesWon,
            MissionKind.ReachDistrict => run.District,
            MissionKind.CollectStars => run.StarsCollected,
            MissionKind.Combo => run.BestCombo,
            MissionKind.QuickClash => run.QuickestClash <= m.Target ? 1 : 0,
            MissionKind.BeatBoss => run.BossesBeaten,
            MissionKind.FullSwarm => run.SwarmsCleared,
            MissionKind.ReturnSparks => run.FireballsReturned,
            MissionKind.CleanRush => run.CleanRushes,
            _ => 0,
        };

        void Fill()
        {
            var rng = new System.Random(MissionsDone * 7919 + Missions.Count * 31 + 5);
            while (Missions.Count < MissionSlots)
            {
                // a kind not on offer yet (bosses and sparks only once a run has got that far)
                var kinds = new List<MissionKind>();
                foreach (MissionKind k in Enum.GetValues(typeof(MissionKind)))
                {
                    if (Missions.Exists(m => m.Kind == k)) continue;
                    if (k is MissionKind.BeatBoss or MissionKind.ReturnSparks && BestDistrict < 2) continue;
                    kinds.Add(k);
                }
                Missions.Add(Make(kinds[rng.Next(kinds.Count)], MissionsDone / 3));
            }
        }

        /// <summary>A mission of <paramref name="kind"/> for the player's <paramref name="tier"/> (missions done / 3): each
        /// tier asks a little more and pays a little more.</summary>
        public static Mission Make(MissionKind kind, int tier)
        {
            int target = kind switch
            {
                MissionKind.SmashRobots => 15 + 10 * tier,
                MissionKind.SmashDrones => 10 + 6 * tier,
                MissionKind.WinClashes => Mathf.Min(1 + tier / 2, 8),
                MissionKind.ReachDistrict => Mathf.Min(2 + (tier + 1) / 2, 12),
                MissionKind.CollectStars => 25 + 15 * tier,
                MissionKind.Combo => Mathf.Min(8 + 3 * tier, 40),
                MissionKind.QuickClash => Mathf.Max(3, 7 - tier / 2),
                MissionKind.BeatBoss => 1 + tier / 3,
                MissionKind.FullSwarm => 1 + tier / 3,
                MissionKind.ReturnSparks => 2 + tier,
                MissionKind.CleanRush => 1 + tier / 3,
                _ => 1,
            };
            return new Mission { Kind = kind, Target = target, Reward = Mathf.Min(15 + 5 * tier, 60) };
        }

        // ---------------------------------------------------------------- saving (one line of text)

        public string Save()
        {
            var parts = new List<string> { "1", Stars.ToString(), MissionsDone.ToString(), BestScore.ToString(), BestDistrict.ToString(), Outfit.ToString() };
            foreach (var m in Missions) parts.Add($"{(int)m.Kind}:{m.Target}:{m.Reward}");
            return string.Join("|", parts);
        }

        /// <summary>The saved progress (a fresh start for nothing saved or something unreadable).</summary>
        public static FlyProgress Load(string text)
        {
            var p = new FlyProgress();
            if (string.IsNullOrEmpty(text)) return p;
            try
            {
                var parts = text.Split('|');
                if (parts.Length < 6 || parts[0] != "1") return p;
                int Int(string s) => int.Parse(s, CultureInfo.InvariantCulture);
                p.Stars = Mathf.Max(0, Int(parts[1]));
                p.MissionsDone = Mathf.Max(0, Int(parts[2]));
                p.BestScore = Mathf.Max(0, Int(parts[3]));
                p.BestDistrict = Mathf.Max(0, Int(parts[4]));
                p.Missions.Clear();
                for (int i = 6; i < parts.Length && p.Missions.Count < MissionSlots; i++)
                {
                    var f = parts[i].Split(':');
                    if (f.Length != 3) continue;
                    int kind = Int(f[0]);
                    if (!Enum.IsDefined(typeof(MissionKind), kind)) continue;
                    p.Missions.Add(new Mission { Kind = (MissionKind)kind, Target = Mathf.Max(1, Int(f[1])), Reward = Mathf.Max(1, Int(f[2])) });
                }
                p.Outfit = Mathf.Clamp(Int(parts[5]), 0, p.Unlocked - 1);
                p.Fill();
            }
            catch (FormatException)
            {
                return new FlyProgress();
            }
            catch (OverflowException)
            {
                return new FlyProgress();
            }
            return p;
        }
    }
}
