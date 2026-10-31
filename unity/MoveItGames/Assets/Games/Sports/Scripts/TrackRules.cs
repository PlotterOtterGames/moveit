using System;
using UnityEngine;

namespace MoveIt.Games.Sports
{
    public sealed class Runner
    {
        public int Lane;
        public bool Human;
        public string Name;
        public float Distance, Speed;
        /// <summary>The computer's top speed (m/s).</summary>
        public float Top;
        /// <summary>Seconds over the line (-1 = still running), the start's reaction after the gun (-1 = not yet).</summary>
        public float Finish = -1f, Reaction = -1f;
        /// <summary>Leaned at the line (it gains <see cref="TrackRace.DipGain"/>).</summary>
        public bool Dipped;
        public bool Finished => Finish >= 0f;
    }

    public enum RacePhase { Marks, Set, Running, Over }

    public enum RaceEvent { Set, Gun, FalseStart, Dip, Finish, Over }

    /// <summary>
    /// The 100 metres (Kinect Sports' sprint), without a scene. "On your marks", "set", then the gun after a moment
    /// that changes every time; running before it is a false start (the race starts again). The player's speed is the
    /// running on the spot (<see cref="RunDetector"/>); three computer runners speed up to their own top speeds. Leaning
    /// at the line (<see cref="Dip"/>, the last <see cref="DipWindow"/> metres) takes <see cref="DipGain"/> off the time.
    /// Points: the faster the more (a hundredth of a second each under 22 s).
    /// </summary>
    public sealed class TrackRace
    {
        public const float Length = 100f, GiveUp = 40f, DipWindow = 8f, DipGain = 0.06f;
        public static readonly string[] RivalNames = { "Drini", "Era", "Arbi", "Ana", "Beni", "Iris" };

        public readonly Runner[] Runners;
        public Runner Human => Runners[HumanIndex];
        public int HumanIndex { get; }
        public RacePhase Phase { get; private set; }
        public float Time { get; private set; }
        public float PhaseTime { get; private set; }
        public int FalseStarts { get; private set; }
        /// <summary>When the gun goes after "set" (s into the set).</summary>
        public float GunAt { get; private set; }
        public event Action<RaceEvent, Runner, int> Events;

        readonly System.Random _rng;

        public TrackRace(int seed, string humanName = "Ti")
        {
            _rng = new System.Random(seed);
            // the computer's runners: a fast one, a middle one, a slower one (about 12, 13.4 and 15 s); the player runs
            // in the second lane
            float[] times = { 11.9f, 13.4f, 15f };
            Runners = new Runner[4];
            HumanIndex = 1;
            int t = 0, name = _rng.Next(RivalNames.Length);
            for (int i = 0; i < 4; i++)
            {
                Runners[i] = new Runner { Lane = i, Human = i == HumanIndex, Name = i == HumanIndex ? humanName : RivalNames[(name + t) % RivalNames.Length] };
                if (i == HumanIndex) continue;
                float time = times[t++] + (float)(_rng.NextDouble() - 0.5) * 0.6f;
                Runners[i].Top = Length / (time - 0.9f);
            }
            Restart();
        }

        /// <summary>The finishing place of the player (1-4, 0 = not finished).</summary>
        public int HumanPlace { get; private set; }

        public int Points => Human.Finished ? Mathf.Max(0, Mathf.RoundToInt((22f - Human.Finish) * 100f)) : 0;

        /// <summary>Medal points: platinum for about 10.8 s, gold 12.3, silver 14, bronze 16.5.</summary>
        public static int[] Needs => new[] { 550, 800, 970, 1120 };

        /// <summary>A runner's place by time (1 = the fastest), once they have finished (0 before).</summary>
        public int PlaceOf(Runner r)
        {
            if (!r.Finished) return 0;
            int place = 1;
            foreach (var o in Runners)
                if (o != r && o.Finished && o.Finish < r.Finish) place++;
            return place;
        }

        void Restart()
        {
            foreach (var r in Runners)
            {
                r.Distance = r.Speed = 0f;
                r.Finish = r.Reaction = -1f;
                r.Dipped = false;
            }
            Time = 0f;
            Phase = RacePhase.Marks;
            PhaseTime = 0f;
            HumanPlace = 0;
        }

        /// <summary>The player leans at the line: counts in the last <see cref="DipWindow"/> metres, once.</summary>
        public bool Dip()
        {
            var h = Human;
            if (Phase != RacePhase.Running || h.Finished || h.Dipped || Length - h.Distance > DipWindow) return false;
            h.Dipped = true;
            Events?.Invoke(RaceEvent.Dip, h, -1);
            return true;
        }

        /// <summary>One step: <paramref name="humanSpeed"/> = how fast the player runs on the spot (m/s).</summary>
        public void Step(float dt, float humanSpeed)
        {
            if (dt <= 0f || Phase == RacePhase.Over) return;
            PhaseTime += dt;
            switch (Phase)
            {
                case RacePhase.Marks:
                    if (PhaseTime >= 1.6f)
                    {
                        Phase = RacePhase.Set;
                        PhaseTime = 0f;
                        GunAt = 1.2f + (float)_rng.NextDouble() * 1.2f;
                        Events?.Invoke(RaceEvent.Set, null, -1);
                    }
                    return;
                case RacePhase.Set:
                    if (humanSpeed > 3f && PhaseTime > 0.3f)
                    {
                        FalseStarts++;
                        Events?.Invoke(RaceEvent.FalseStart, Human, FalseStarts);
                        Restart();
                        return;
                    }
                    if (PhaseTime >= GunAt)
                    {
                        Phase = RacePhase.Running;
                        PhaseTime = 0f;
                        Events?.Invoke(RaceEvent.Gun, null, -1);
                    }
                    return;
            }

            Time += dt;
            foreach (var r in Runners)
            {
                if (r.Finished)
                {
                    r.Speed = Mathf.MoveTowards(r.Speed, 2f, dt * 4f);
                    r.Distance += r.Speed * dt;
                    continue;
                }
                float want = r.Human ? humanSpeed : r.Top * (1f - Mathf.Exp(-Time / 0.8f));
                r.Speed = r.Human ? want : Mathf.MoveTowards(r.Speed, want, dt * 12f);
                if (r.Reaction < 0f && (r.Human ? r.Speed > 2f : Time > 0.14f + 0.05f * r.Lane)) r.Reaction = Time;
                r.Distance += r.Speed * dt;
                // the computer's runners lean at the line too, most of the time
                if (!r.Human && !r.Dipped && Length - r.Distance < 2f && _rng.NextDouble() < 0.7) r.Dipped = true;
                if (r.Distance >= Length)
                {
                    float over = (r.Distance - Length) / Mathf.Max(0.1f, r.Speed);
                    r.Finish = Mathf.Max(0.5f, Time - Mathf.Min(over, dt) - (r.Dipped ? DipGain : 0f));
                    if (r.Human) HumanPlace = PlaceOf(r);
                    Events?.Invoke(RaceEvent.Finish, r, PlaceOf(r));
                }
            }
            bool done = Human.Finished && AllFinishedOrLate();
            if (done || Time >= GiveUp)
            {
                HumanPlace = Human.Finished ? PlaceOf(Human) : 4;
                Phase = RacePhase.Over;
                Events?.Invoke(RaceEvent.Over, Human, HumanPlace);
            }
        }

        bool AllFinishedOrLate()
        {
            foreach (var r in Runners)
                if (!r.Finished && Time - Human.Finish < 1.5f) return false;
            return true;
        }
    }
}
