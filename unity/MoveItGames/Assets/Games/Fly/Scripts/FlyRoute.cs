using UnityEngine;

namespace MoveIt.Games.Fly
{
    public enum TurnChoice { Straight, Left, Right }

    /// <summary>The streets leaving the next crossroads (seen from the hero), and how far it is.</summary>
    public struct Junction
    {
        public bool Left, Straight, Right;
        /// <summary>Metres from the hero's track to the crossroads' centre (negative once past it).</summary>
        public float Distance;
        /// <summary>Straight on is impossible: the hero turns here whatever the player does.</summary>
        public bool MustTurn => !Straight;
    }

    /// <summary>
    /// The hero's way through the city: a grid of streets, one every <see cref="Pitch"/> metres both ways, with a few
    /// segments missing so some crossroads are T-junctions or corners. The hero flies along the street centre lines;
    /// reaching a crossroads, the steering decides: far enough right (<see cref="TurnAt"/>) turns right, 90° on an arc,
    /// far enough left turns left, otherwise straight on. Where straight on is impossible the hero turns anyway, toward
    /// the side it is on (a child who doesn't choose is never punished). Turning into a missing street = straight on.
    /// <para>Every crossroads keeps at least two of its four streets (at most one north-south and one east-west segment
    /// is missing), so the hero always has a way on. Engine-free: tested without a scene (FlyRouteTests);
    /// <see cref="FlyWorld"/> draws the same grid, <see cref="FlyGame"/> moves the hero along it.</para>
    /// Coordinates: x east, y north (Unity's z); headings in degrees clockwise from north (Unity's yaw).
    /// </summary>
    public sealed class FlyRoute
    {
        /// <summary>Metres between crossroads (centre to centre).</summary>
        public float Pitch = 80f;
        /// <summary>Street width, building line to building line (road and both pavements).</summary>
        public float StreetWidth = 14f;
        /// <summary>The turning arc's radius: the track stays inside the crossroads.</summary>
        public float TurnRadius = 7f;
        /// <summary>Steering (the hero's place in its window, -1..1) beyond this turns at a crossroads.</summary>
        public float TurnAt = 0.45f;
        /// <summary>Share of the candidate segments that are missing (only a quarter of the segments are candidates).</summary>
        public float MissingChance = 0.7f;
        public readonly int Seed;

        // directions: 0 north (+y), 1 east (+x), 2 south, 3 west
        static readonly int[] Dx = { 0, 1, 0, -1 };
        static readonly int[] Dy = { 1, 0, -1, 0 };

        int _dir;
        int _nextX, _nextY;   // the crossroads ahead (grid indices)
        bool _turning;
        int _turnSign, _turnTo;
        float _arcDone;       // radians of the arc flown
        Vector2 _arcStart, _arcCentre;

        public FlyRoute(int seed = 7)
        {
            Seed = seed;
            Reset();
        }

        /// <summary>Where the hero's track is (city metres).</summary>
        public Vector2 Position { get; private set; }
        /// <summary>Degrees clockwise from north; a multiple of 90 except while turning.</summary>
        public float Heading { get; private set; }
        public bool Turning => _turning;
        /// <summary>+1 while turning right, -1 while turning left, 0 flying straight (for banking).</summary>
        public float TurnDirection => _turning ? _turnSign : 0f;
        /// <summary>0..1 through the current turn.</summary>
        public float TurnProgress => _turning ? _arcDone / (Mathf.PI * 0.5f) : 0f;
        public int TurnsTaken { get; private set; }
        public float Travelled { get; private set; }
        /// <summary>The grid direction flown (0 north, 1 east, 2 south, 3 west), the one turned to while turning.</summary>
        public int Direction => _turning ? _turnTo : _dir;

        /// <summary>Back to the start: the middle of a street segment that exists, heading north.</summary>
        public void Reset()
        {
            int x = 0;
            while (!NorthSouth(x, 0)) x++;
            _dir = 0;
            _nextX = x;
            _nextY = 1;
            _turning = false;
            Position = new Vector2(x * Pitch, Pitch * 0.5f);
            Heading = 0f;
            TurnsTaken = 0;
            Travelled = 0f;
        }

        /// <summary>Puts the track <paramref name="before"/> metres before crossroads (i, j), flying in grid direction
        /// <paramref name="dir"/> (the street it arrives on must exist).</summary>
        public void StartAt(int i, int j, int dir, float before)
        {
            _dir = dir & 3;
            _nextX = i;
            _nextY = j;
            _turning = false;
            Position = Crossroads(i, j) - Dir(_dir) * before;
            Heading = _dir * 90f;
        }

        // ---------------------------------------------------------------- the street grid

        /// <summary>The north-south street segment from crossroads (i, j) to (i, j + 1).</summary>
        public bool NorthSouth(int i, int j) => !(Mod(i + 2 * j, 4) == 0 && Hash01(i, j, 1) < MissingChance);

        /// <summary>The east-west street segment from crossroads (i, j) to (i + 1, j).</summary>
        public bool EastWest(int i, int j) => !(Mod(2 * i + j, 4) == 1 && Hash01(i, j, 2) < MissingChance);

        /// <summary>The segment leaving crossroads (i, j) in grid direction <paramref name="dir"/>.</summary>
        public bool Street(int i, int j, int dir) => (dir & 3) switch
        {
            0 => NorthSouth(i, j),
            1 => EastWest(i, j),
            2 => NorthSouth(i, j - 1),
            _ => EastWest(i - 1, j),
        };

        /// <summary>A stable 0..1 value for (a, b, salt): the same city every time (also the city's looks).</summary>
        public float Hash01(int a, int b, int salt)
        {
            unchecked
            {
                uint h = (uint)a * 73856093u ^ (uint)b * 19349663u ^ (uint)salt * 83492791u ^ (uint)Seed * 2654435761u;
                h ^= h >> 13;
                h *= 0x5bd1e995u;
                h ^= h >> 15;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        static int Mod(int a, int m) => ((a % m) + m) % m;
        static Vector2 Dir(int d) => new Vector2(Dx[d & 3], Dy[d & 3]);
        public Vector2 Crossroads(int i, int j) => new Vector2(i * Pitch, j * Pitch);

        // ---------------------------------------------------------------- flying

        /// <summary>The crossroads ahead (after the current turn, while turning).</summary>
        public Junction Next
        {
            get
            {
                int dir = Direction, x = _nextX, y = _nextY;
                if (_turning)
                {
                    x += Dx[_turnTo];
                    y += Dy[_turnTo];
                }
                var c = Crossroads(x, y);
                return new Junction
                {
                    Straight = Street(x, y, dir),
                    Right = Street(x, y, dir + 1),
                    Left = Street(x, y, dir + 3),
                    Distance = Vector2.Dot(c - Position, Dir(dir)),
                };
            }
        }

        /// <summary>What steering at <paramref name="steer"/> (-1..1) would do at the next crossroads.</summary>
        public TurnChoice ChoiceAt(float steer) => Decide(Next, steer, _nextX + _nextY);

        TurnChoice Decide(Junction j, float steer, int salt)
        {
            if (steer > TurnAt && j.Right) return TurnChoice.Right;
            if (steer < -TurnAt && j.Left) return TurnChoice.Left;
            if (j.Straight) return TurnChoice.Straight;
            // straight on is impossible: toward the side the hero is on, or (in the middle) either side
            if (j.Right && j.Left)
            {
                if (Mathf.Abs(steer) > 0.05f) return steer > 0f ? TurnChoice.Right : TurnChoice.Left;
                return Hash01(salt, TurnsTaken, 3) < 0.5f ? TurnChoice.Right : TurnChoice.Left;
            }
            return j.Right ? TurnChoice.Right : TurnChoice.Left;
        }

        /// <summary>
        /// Flies <paramref name="distance"/> metres along the route. <paramref name="steer"/> is the hero's place in its
        /// window (-1..1): it decides each crossroads reached during this step. Returns the turn started during this
        /// step (Straight = none).
        /// </summary>
        public TurnChoice Advance(float distance, float steer)
        {
            var started = TurnChoice.Straight;
            if (distance <= 0f) return started;
            Travelled += distance;
            for (int guard = 0; distance > 1e-5f && guard < 16; guard++)
            {
                if (_turning)
                {
                    distance = FlyArc(distance);
                    continue;
                }
                var d = Dir(_dir);
                var c = Crossroads(_nextX, _nextY);
                float toArc = Vector2.Dot(c - Position, d) - TurnRadius;
                if (toArc > distance)
                {
                    Position += d * distance;
                    return started;
                }
                Position += d * Mathf.Max(0f, toArc);
                distance -= Mathf.Max(0f, toArc);
                var choice = Decide(Next, steer, _nextX + _nextY);
                if (choice == TurnChoice.Straight)
                {
                    // fly on through the crossroads: the one after it becomes the target
                    _nextX += Dx[_dir];
                    _nextY += Dy[_dir];
                    continue;
                }
                StartTurn(choice == TurnChoice.Right ? 1 : -1);
                started = choice;
            }
            return started;
        }

        void StartTurn(int sign)
        {
            var d = Dir(_dir);
            var side = Dir(_dir + sign);
            _turning = true;
            _turnSign = sign;
            _turnTo = (_dir + sign + 4) & 3;
            _arcDone = 0f;
            _arcStart = Position;
            _arcCentre = Position + side * TurnRadius;
            TurnsTaken++;
        }

        /// <summary>Flies along the arc; returns the distance left over after it ends.</summary>
        float FlyArc(float distance)
        {
            const float quarter = Mathf.PI * 0.5f;
            float step = Mathf.Min(distance, (quarter - _arcDone) * TurnRadius);
            _arcDone += step / TurnRadius;
            distance -= step;
            var d = Dir(_dir);
            var side = Dir(_dir + _turnSign);
            Position = _arcCentre - side * (TurnRadius * Mathf.Cos(_arcDone)) + d * (TurnRadius * Mathf.Sin(_arcDone));
            Heading = Mathf.Repeat(_dir * 90f + _turnSign * _arcDone * Mathf.Rad2Deg, 360f);
            if (_arcDone >= quarter - 1e-5f)
            {
                // on the new street's centre line, heading exactly along it; its next crossroads is one pitch on
                var c = Crossroads(_nextX, _nextY);
                _dir = _turnTo;
                Position = c + Dir(_dir) * TurnRadius;
                Heading = _dir * 90f;
                _nextX += Dx[_dir];
                _nextY += Dy[_dir];
                _turning = false;
            }
            return distance;
        }
    }
}
