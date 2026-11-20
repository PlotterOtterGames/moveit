using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace MoveIt.Games.Space
{
    /// <summary>
    /// What a family has found, kept on the box (no accounts): the planets found (the home planet from the start), the
    /// systems visited, the system they're in, the time played. A system can be flown to when it's visited or linked to
    /// one that is (the map's fog). Pure logic (tests); <see cref="SpaceGame"/> saves it in PlayerPrefs.
    /// </summary>
    public sealed class GalaxyProgress
    {
        public readonly HashSet<int> Found = new HashSet<int>();
        public readonly HashSet<int> Visited = new HashSet<int>();
        public int Current;
        public float Seconds;
        /// <summary>The suit the astronaut wears (unlocked by planets found).</summary>
        public int Suit;

        /// <summary>A fresh start: home visited, the home planet found.</summary>
        public static GalaxyProgress Start(Galaxy g)
        {
            var p = new GalaxyProgress { Current = g.Home };
            p.Visited.Add(g.Home);
            foreach (var planet in g.All[g.Home].Planets)
                if (planet.Type == PlanetType.Home) p.Found.Add(planet.Id);
            return p;
        }

        public int FoundIn(StarSystem s)
        {
            int n = 0;
            foreach (var p in s.Planets)
                if (Found.Contains(p.Id)) n++;
            return n;
        }

        public bool Complete(StarSystem s) => FoundIn(s) == s.Planets.Count;
        public bool AllFound(Galaxy g) => Found.Count >= g.Planets.Count;

        /// <summary>Can be flown to: visited, or linked to a visited system.</summary>
        public bool Reachable(Galaxy g, int system)
        {
            if (Visited.Contains(system)) return true;
            foreach (int n in g.All[system].Links)
                if (Visited.Contains(n)) return true;
            return false;
        }

        /// <summary>Where the map suggests flying next: the nearest system on the map that can be flown to and still has
        /// planets to find (not the one they're in, unless it's the last); -1 when everything is found.</summary>
        public int Suggest(Galaxy g)
        {
            var here = g.All[Current].Map;
            int best = -1;
            float bestD = float.MaxValue;
            foreach (var s in g.All)
            {
                if (s.Id == Current || Complete(s) || !Reachable(g, s.Id)) continue;
                float d = Vector2.Distance(here, s.Map);
                if (d < bestD)
                {
                    bestD = d;
                    best = s.Id;
                }
            }
            if (best < 0 && !Complete(g.All[Current])) best = Current;
            return best;
        }

        /// <summary>The planets to find for each suit (the first from the start).</summary>
        public static readonly int[] SuitAt = { 0, 20, 50, 100, 160, 230 };

        /// <summary>The suits unlocked by the planets found.</summary>
        public int Suits
        {
            get
            {
                int n = 1;
                while (n < SuitAt.Length && Found.Count >= SuitAt[n]) n++;
                return n;
            }
        }

        public string Save(Galaxy g)
        {
            var sb = new StringBuilder("1|");
            sb.Append(Current.ToString(CultureInfo.InvariantCulture)).Append('|');
            sb.Append(Seconds.ToString("0", CultureInfo.InvariantCulture)).Append('|');
            sb.Append(Suit.ToString(CultureInfo.InvariantCulture)).Append('|');
            sb.Append(Bits(Found, g.Planets.Count)).Append('|');
            sb.Append(Bits(Visited, g.All.Count));
            return sb.ToString();
        }

        /// <summary>The saved progress (a fresh start for nothing saved or something unreadable, or another galaxy's).</summary>
        public static GalaxyProgress Load(Galaxy g, string text)
        {
            var fresh = Start(g);
            if (string.IsNullOrEmpty(text)) return fresh;
            var parts = text.Split('|');
            if (parts.Length != 6 || parts[0] != "1") return fresh;
            try
            {
                var p = new GalaxyProgress
                {
                    Current = int.Parse(parts[1], CultureInfo.InvariantCulture),
                    Seconds = float.Parse(parts[2], CultureInfo.InvariantCulture),
                    Suit = int.Parse(parts[3], CultureInfo.InvariantCulture),
                };
                if (!Unbits(parts[4], g.Planets.Count, p.Found) || !Unbits(parts[5], g.All.Count, p.Visited)) return fresh;
                if (p.Current < 0 || p.Current >= g.All.Count || !p.Visited.Contains(p.Current)) p.Current = g.Home;
                p.Visited.Add(g.Home);
                foreach (int f in fresh.Found) p.Found.Add(f);
                p.Suit = Mathf.Clamp(p.Suit, 0, p.Suits - 1);
                return p;
            }
            catch (FormatException)
            {
                return fresh;
            }
            catch (OverflowException)
            {
                return fresh;
            }
        }

        static string Bits(HashSet<int> set, int count)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < count; i += 4)
            {
                int v = 0;
                for (int b = 0; b < 4; b++)
                    if (set.Contains(i + b)) v |= 1 << b;
                sb.Append("0123456789abcdef"[v]);
            }
            return sb.ToString();
        }

        static bool Unbits(string text, int count, HashSet<int> into)
        {
            if (text.Length != (count + 3) / 4) return false;
            for (int k = 0; k < text.Length; k++)
            {
                int v = "0123456789abcdef".IndexOf(text[k]);
                if (v < 0) return false;
                for (int b = 0; b < 4; b++)
                    if ((v & (1 << b)) != 0 && k * 4 + b < count) into.Add(k * 4 + b);
            }
            return true;
        }
    }

    /// <summary>One player, or two flying together (one trip: both lean to steer, either one's arms up boost).</summary>
    public enum SpaceMode { Solo, Together }

    public enum ExploreEventKind { Found, SystemComplete, GalaxyComplete, Bump, TurnBack }

    public struct ExploreEvent
    {
        public ExploreEventKind Kind;
        public Planet Planet;
        public Vector2 At;
    }

    /// <summary>
    /// Flying round a star system (2026-11-14, the exploration rework): the astronaut always flies on (it's never stuck),
    /// turning as the body leans, faster with both arms up (a boost), slower when ducking. Flying into a planet's zone
    /// (<see cref="FindReach"/>) starts its <b>scan</b>: the astronaut brakes, swings round the planet close to it while
    /// the player doesn't steer (an orbit: the planet fills the screen and rolls by), and after
    /// <see cref="ScanSeconds"/> inside the zone the planet is found (a scan half done stays half done). Planets and the
    /// star can't be flown into (it slides round them), and past the system's edge it turns back. The nearest planet not
    /// yet found is the target: the radar points to it, and while the player isn't steering, the autopilot
    /// (<see cref="Assist"/>) turns gently toward it, so a child who only lets it fly still finds everything. On the
    /// plane: x across, y along (the star at the origin); the heading 0 = along +y. Pure logic (tests).
    /// </summary>
    public sealed class Explorer
    {
        public const float Cruise = 50f, Fast = 190f, Slow = 18f, Look = 30f;
        /// <summary>Turning (degrees a second) at a full lean, the autopilot's, and while it swings round a planet.</summary>
        public const float TurnRate = 62f, AssistTurn = 26f, OrbitTurn = 40f;
        /// <summary>Seconds inside a planet's zone to find it.</summary>
        public const float ScanSeconds = 4f;
        /// <summary>The radar sees planets not yet found within this (m).</summary>
        public const float ScanRange = 1200f;

        public readonly Galaxy Galaxy;
        public readonly GalaxyProgress Progress;
        public StarSystem System { get; private set; }
        public Vector2 Position;
        public float Heading, Speed, Boost, Time;
        /// <summary>The autopilot's share (0 = off .. 1).</summary>
        public float Assist = 1f;
        /// <summary>The planet not yet found the radar points to (null when the system is done).</summary>
        public Planet Target { get; private set; }
        /// <summary>The planet being scanned (in its zone, not yet found), or null.</summary>
        public Planet Scanning { get; private set; }
        public readonly List<ExploreEvent> Events = new List<ExploreEvent>();
        readonly Dictionary<int, float> _scan = new Dictionary<int, float>();

        public Explorer(Galaxy galaxy, GalaxyProgress progress)
        {
            Galaxy = galaxy;
            Progress = progress;
            Enter(progress.Current);
        }

        public Vector2 Forward => new Vector2(Mathf.Sin(Heading), Mathf.Cos(Heading));

        /// <summary>How near a planet its zone starts (m from its middle).</summary>
        public static float FindReach(Planet p) => p.Radius * 1.9f + 50f;

        /// <summary>The swing round a planet being scanned (m from its middle).</summary>
        public static float OrbitRadius(Planet p) => Mathf.Max(p.Radius * 1.32f + 26f, FindReach(p) * 0.8f);

        /// <summary>How far a planet's scan is (0..1; 1 = found).</summary>
        public float ScanOf(Planet p) => Found(p) ? 1f : _scan.TryGetValue(p.Id, out float v) ? v : 0f;

        /// <summary>Into system <paramref name="id"/>: at its edge, heading in toward the star (a little to one side, so
        /// the star isn't straight ahead).</summary>
        public void Enter(int id)
        {
            System = Galaxy.All[id];
            Progress.Current = id;
            Progress.Visited.Add(id);
            float a = (id * 2.399f) % (Mathf.PI * 2f);
            Position = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (System.Edge - 120f);
            var toStar = -Position.normalized;
            Heading = Mathf.Atan2(toStar.x, toStar.y) + 0.35f;
            Speed = Cruise;
            Boost = 0f;
            Target = null;
            Scanning = null;
            _scan.Clear();
            ChooseTarget();
        }

        public float Distance(Planet p) => Vector2.Distance(Position, p.Position);
        public bool Found(Planet p) => Progress.Found.Contains(p.Id);
        public bool InRange(Planet p) => Distance(p) <= ScanRange + p.Radius;
        public bool SystemDone => Progress.Complete(System);

        /// <summary>One step: <paramref name="steer"/> -1 (left) .. 1 (right), boosting, slowing down.</summary>
        public void Step(float dt, float steer, bool boost, bool slow)
        {
            if (dt <= 0f) return;
            Time += dt;
            Progress.Seconds += dt;
            ChooseTarget();
            Scanning = null;
            foreach (var p in System.Planets)
                if (!Found(p) && Distance(p) <= FindReach(p)) Scanning = p;
            // turning: the player's lean, or the autopilot (round the planet being scanned, else toward the target)
            float turn = Mathf.Clamp(steer, -1f, 1f) * TurnRate;
            if (Mathf.Abs(steer) < 0.15f && Assist > 0f)
            {
                if (Scanning != null) turn = Orbit(Scanning) * Assist;
                else if (Target != null)
                {
                    float off = Mathf.DeltaAngle(Heading * Mathf.Rad2Deg, Angle(Target.Position - Position) * Mathf.Rad2Deg);
                    turn = Mathf.Clamp(off * 1.5f, -AssistTurn, AssistTurn) * Assist;
                }
            }
            // near the edge, heading out: no boost, turning back toward the star (past it, hard)
            float out_ = Position.magnitude - (System.Edge - 150f);
            bool outward = Vector2.Dot(Position, Forward) > 0f;
            if (out_ > 0f && (outward || out_ > 150f))
            {
                float off = Mathf.DeltaAngle(Heading * Mathf.Rad2Deg, Angle(-Position) * Mathf.Rad2Deg);
                turn = Mathf.Clamp(off * 2f, -TurnRate, TurnRate) * Mathf.Clamp01(out_ / 150f + 0.4f);
                boost = false;
                if (out_ > 150f && out_ < 150f + dt * Speed * 1.5f) Events.Add(new ExploreEvent { Kind = ExploreEventKind.TurnBack, At = Position });
            }
            Heading += turn * Mathf.Deg2Rad * dt;
            // speed: cruising, boosting (arms up), slowing (ducking); easing down near a planet not yet found, braking in
            // its zone whatever the arms do (a scan is a look)
            Boost = Mathf.MoveTowards(Boost, boost && Scanning == null ? 1f : 0f, dt * (boost ? 0.9f : 1.6f));
            float target = slow ? Slow : Mathf.Lerp(Cruise, Fast, Boost);
            float brake = 110f;
            if (Scanning != null)
            {
                target = Mathf.Min(target, Look);
                brake = 170f;
            }
            else if (!boost)
                foreach (var p in System.Planets)
                    if (!Found(p) && Distance(p) < FindReach(p) * 1.6f) target = Mathf.Min(target, Look);
            Speed = Mathf.MoveTowards(Speed, target, dt * (target > Speed ? 70f : brake));
            Position += Forward * (Speed * dt);
            Avoid();
            Scan(dt);
        }

        static float Angle(Vector2 d) => Mathf.Atan2(d.x, d.y);

        /// <summary>The autopilot's turn (degrees a second) swinging round <paramref name="p"/>: along the way it's already
        /// going round, drawn in to (or out to) <see cref="OrbitRadius"/>.</summary>
        float Orbit(Planet p)
        {
            var d = Position - p.Position;
            float dist = Mathf.Max(1e-3f, d.magnitude);
            var n = d / dist;
            var tangent = new Vector2(-n.y, n.x);
            if (Vector2.Dot(tangent, Forward) < 0f) tangent = -tangent;
            float r = OrbitRadius(p);
            float pull = Mathf.Clamp((dist - r) / r * 2.5f, -0.8f, 1.2f);
            var want = tangent - n * pull;
            float off = Mathf.DeltaAngle(Heading * Mathf.Rad2Deg, Angle(want) * Mathf.Rad2Deg);
            return Mathf.Clamp(off * 2f, -OrbitTurn, OrbitTurn);
        }

        /// <summary>Nothing can be flown into: the astronaut slides round planets and the star.</summary>
        void Avoid()
        {
            Slide(Vector2.zero, System.StarRadius * 1.5f + 20f);
            foreach (var p in System.Planets) Slide(p.Position, p.Radius * 1.22f + 10f);
        }

        void Slide(Vector2 centre, float keep)
        {
            var d = Position - centre;
            float dist = d.magnitude;
            if (dist >= keep || dist < 1e-3f) return;
            var n = d / dist;
            Position = centre + n * keep;
            // the heading turns along the surface (the side nearer to where it was going)
            var f = Forward;
            var tangent = new Vector2(-n.y, n.x);
            if (Vector2.Dot(tangent, f) < 0f) tangent = -tangent;
            float along = Mathf.Atan2(tangent.x, tangent.y);
            Heading = Mathf.LerpAngle(Heading * Mathf.Rad2Deg, along * Mathf.Rad2Deg, 0.35f) * Mathf.Deg2Rad;
            Events.Add(new ExploreEvent { Kind = ExploreEventKind.Bump, At = Position });
        }

        /// <summary>The scan of the planet whose zone it's in; found when done.</summary>
        void Scan(float dt)
        {
            var p = Scanning;
            if (p == null || Distance(p) > FindReach(p)) return;
            float v = (_scan.TryGetValue(p.Id, out float s) ? s : 0f) + dt / ScanSeconds;
            if (v < 1f)
            {
                _scan[p.Id] = v;
                return;
            }
            _scan.Remove(p.Id);
            Progress.Found.Add(p.Id);
            Events.Add(new ExploreEvent { Kind = ExploreEventKind.Found, Planet = p, At = p.Position });
            if (SystemDone) Events.Add(new ExploreEvent { Kind = ExploreEventKind.SystemComplete });
            if (Progress.AllFound(Galaxy)) Events.Add(new ExploreEvent { Kind = ExploreEventKind.GalaxyComplete });
            Scanning = null;
            Target = null;
        }

        /// <summary>The nearest planet not yet found (kept until another is clearly nearer: the arrow doesn't flicker).</summary>
        void ChooseTarget()
        {
            Planet best = null;
            float bestD = float.MaxValue;
            foreach (var p in System.Planets)
            {
                if (Found(p)) continue;
                float d = Distance(p);
                if (d < bestD)
                {
                    bestD = d;
                    best = p;
                }
            }
            if (Target != null && !Found(Target) && best != null && Distance(Target) < bestD * 1.3f) return;
            Target = best;
        }
    }
}
