using UnityEngine;

namespace MoveIt.Games.Arcade
{
    /// <summary>
    /// Topi! (keep the ball up, after EyeToy's Keep Ups and Bluey's keepy-uppy): a ball falls onto the player and bounces
    /// off whatever part of the body it meets (head, arms, hands, chest, knees, feet): keep it in the air. It starts as a
    /// slow balloon, a third of the way in it is a beach ball, the last third a football. Forgiving on purpose (the
    /// camera's ~0.1 s of lag and a child's timing): every touch sends it up at least <see cref="Rise"/> torso lengths
    /// above where it was met, and nudges it back toward the middle near the sides. Head and feet are worth the most
    /// (football's skills), touches in a row raise the points; a ball reaching the floor ends the run and a new one drops.
    /// </summary>
    public sealed class KeepUpRules : MachineRules
    {
        public override Machine Booth => Machine.KeepUp;

        public enum BallKind { Balloon, Beach, Football }

        public const float Top = 3.4f, Floor = -1.6f, Restitution = 0.55f;
        /// <summary>Seconds between two counted touches of the same part.</summary>
        public const float Debounce = 0.22f;

        public sealed class Ball
        {
            public BallKind Kind;
            public Vector2 Pos, Vel;
            public float Radius, Angle, Spin;
            public bool Live;
            public float Wait = 0.8f;
            public float LastTouch = -9f;
            public Part LastPart = Part.Head;
            public int Streak, BestStreak, Touches, Drops, Headers;
        }

        public readonly Ball[] Balls;
        /// <summary>The invisible walls that keep the ball in view, torso lengths either side (2.7; two players: closer).</summary>
        public readonly float Side;
        readonly StageBody[] _previous;
        readonly bool[] _havePrevious;
        readonly System.Random _rng;

        public KeepUpRules(int players, float duration = 60f, int seed = 1, float side = 2.7f) : base(players, duration)
        {
            Side = side;
            _rng = new System.Random(seed);
            Balls = new Ball[Players];
            _previous = new StageBody[Players];
            _havePrevious = new bool[Players];
            for (int p = 0; p < Players; p++)
            {
                Balls[p] = new Ball();
                _previous[p] = new StageBody();
            }
        }

        public BallKind KindNow => Progress < 1f / 3f ? BallKind.Balloon : Progress < 2f / 3f ? BallKind.Beach : BallKind.Football;

        public static float GravityOf(BallKind k) => k switch { BallKind.Balloon => 1.3f, BallKind.Beach => 2.5f, _ => 3.9f };
        public static float RadiusOf(BallKind k) => k switch { BallKind.Balloon => 0.36f, BallKind.Beach => 0.31f, _ => 0.23f };
        static float DragOf(BallKind k) => k switch { BallKind.Balloon => 0.45f, BallKind.Beach => 0.12f, _ => 0.03f };
        /// <summary>How far above the touch a touch sends it, at least.</summary>
        static float Rise(BallKind k) => k switch { BallKind.Balloon => 0.95f, BallKind.Beach => 1.05f, _ => 1.15f };

        public static int PointsOf(Part part) => part switch
        {
            Part.Head => 30,
            Part.LeftFoot or Part.RightFoot => 30,
            Part.LeftKnee or Part.RightKnee => 20,
            _ => 10,
        };

        // the body's shapes: a part (or a bone between two parts) and its radius
        static readonly (Part a, Part b, float radius)[] Shapes =
        {
            (Part.Head, Part.Head, 0.3f),
            (Part.LeftHand, Part.LeftHand, 0.17f), (Part.RightHand, Part.RightHand, 0.17f),
            (Part.LeftElbow, Part.LeftHand, 0.1f), (Part.RightElbow, Part.RightHand, 0.1f),
            (Part.LeftShoulder, Part.LeftElbow, 0.12f), (Part.RightShoulder, Part.RightElbow, 0.12f),
            (Part.LeftKnee, Part.LeftKnee, 0.17f), (Part.RightKnee, Part.RightKnee, 0.17f),
            (Part.LeftFoot, Part.LeftFoot, 0.18f), (Part.RightFoot, Part.RightFoot, 0.18f),
            (Part.LeftKnee, Part.LeftFoot, 0.11f), (Part.RightKnee, Part.RightFoot, 0.11f),
            (Part.LeftHip, Part.LeftKnee, 0.13f), (Part.RightHip, Part.RightKnee, 0.13f),
        };

        protected override void OnStep(float dt, StageBody[] bodies)
        {
            for (int p = 0; p < Players; p++)
            {
                var ball = Balls[p];
                var body = BodyOf(bodies, p);
                if (!ball.Live)
                {
                    ball.Wait -= dt;
                    if (ball.Wait <= 0f && Left > 1.5f) Serve(p, ball, body);
                }
                else
                {
                    const int steps = 3;
                    for (int s = 0; s < steps; s++) Move(p, ball, body, dt / steps, s == steps - 1);
                }
                if (body != null)
                {
                    _previous[p].CopyFrom(body);
                    _havePrevious[p] = true;
                }
                else _havePrevious[p] = false;
            }
        }

        void Serve(int p, Ball ball, StageBody body)
        {
            ball.Kind = KindNow;
            ball.Radius = RadiusOf(ball.Kind);
            float x = body != null && body.Sees(Part.Head) ? Mathf.Clamp(body[Part.Head].x, -1.4f, 1.4f) : 0f;
            ball.Pos = new Vector2(x + Range(_rng, -0.25f, 0.25f), Top - 0.2f);
            ball.Vel = new Vector2(0f, -0.4f);
            ball.Spin = Range(_rng, -90f, 90f);
            ball.Live = true;
            ball.Streak = 0;
            Emit(EventKind.NewBall, p, ball.Pos, variant: (int)ball.Kind);
        }

        void Move(int p, Ball ball, StageBody body, float dt, bool last)
        {
            float g = GravityOf(ball.Kind);
            ball.Vel.y -= g * dt;
            ball.Vel *= 1f - DragOf(ball.Kind) * dt;
            ball.Pos += ball.Vel * dt;
            ball.Angle += ball.Spin * dt;
            // the sides and the top of the stage keep it in view
            if (ball.Pos.x < -Side + ball.Radius && ball.Vel.x < 0f || ball.Pos.x > Side - ball.Radius && ball.Vel.x > 0f)
            {
                ball.Vel.x = -ball.Vel.x * 0.7f;
                ball.Pos.x = Mathf.Clamp(ball.Pos.x, -Side + ball.Radius, Side - ball.Radius);
            }
            if (ball.Pos.y > Top - ball.Radius && ball.Vel.y > 0f) ball.Vel.y = -ball.Vel.y * 0.3f;
            if (body != null) Touch(p, ball, body, dt);
            if (ball.Pos.y < Floor)
            {
                ball.Live = false;
                ball.Wait = 1.1f;
                ball.Drops++;
                Emit(EventKind.Drop, p, new Vector2(ball.Pos.x, Floor), combo: ball.Streak, variant: (int)ball.Kind);
                ball.Streak = 0;
            }
        }

        void Touch(int p, Ball ball, StageBody body, float dt)
        {
            float frameDt = Mathf.Max(dt * 3f, 1e-3f);
            foreach (var (a, b, radius) in Shapes)
            {
                if (!body.Sees(a) || !body.Sees(b)) continue;
                var closest = Geo.Closest(ball.Pos, body[a], body[b]);
                var delta = ball.Pos - closest;
                float d = delta.magnitude, reach = radius + ball.Radius;
                if (d >= reach) continue;
                var n = d > 1e-4f ? delta / d : Vector2.up;
                // the part's own speed (from the last frame), so a header or a kick sends it off faster
                var partVel = Vector2.zero;
                if (_havePrevious[p] && _previous[p].Sees(a) && _previous[p].Sees(b))
                {
                    var before = Geo.Closest(ball.Pos, _previous[p][a], _previous[p][b]);
                    partVel = Vector2.ClampMagnitude((closest - before) / frameDt, 6f);
                }
                var rel = ball.Vel - partVel;
                float vn = Vector2.Dot(rel, n);
                ball.Pos = closest + n * reach;
                if (vn >= 0f) continue;   // already leaving it
                rel -= (1f + Restitution) * vn * n;
                ball.Vel = rel + partVel;
                // a touch from below always sends it up high enough to play again, and back toward the middle
                if (n.y > -0.3f)
                {
                    float g = GravityOf(ball.Kind);
                    float up = Mathf.Sqrt(2f * g * Rise(ball.Kind));
                    if (ball.Vel.y < up) ball.Vel.y = up;
                    // and a little to a side, so the player has to follow it (back toward the middle near the edges)
                    float side = Range(_rng, 0.45f, 0.85f) * (ball.Kind == BallKind.Balloon ? 0.75f : ball.Kind == BallKind.Beach ? 1f : 1.1f);
                    float sign = Mathf.Abs(ball.Pos.x) > 1f ? -Mathf.Sign(ball.Pos.x) : _rng.NextDouble() < 0.5 ? -1f : 1f;
                    ball.Vel.x = Mathf.Clamp(ball.Vel.x * 0.5f + sign * side, -1.4f, 1.4f);
                }
                ball.Vel = Vector2.ClampMagnitude(ball.Vel, 6f);
                ball.Spin = Mathf.Clamp(ball.Spin + ball.Vel.x * 80f, -400f, 400f);
                var part = Representative(a, b);
                if (Time - ball.LastTouch >= Debounce || part != ball.LastPart)
                {
                    ball.LastTouch = Time;
                    ball.LastPart = part;
                    ball.Streak++;
                    ball.Touches++;
                    ball.BestStreak = Mathf.Max(ball.BestStreak, ball.Streak);
                    if (part == Part.Head) ball.Headers++;
                    int points = Mathf.RoundToInt(PointsOf(part) * (1f + Mathf.Min(ball.Streak, 20) / 10f));
                    Add(p, points);
                    Emit(EventKind.Touch, p, closest + n * radius, points, ball.Streak, variant: (int)part, direction: n);
                }
                break;
            }
        }

        /// <summary>The part a touch is credited to: the end of a bone (the hand of a forearm, the foot of a shin...).</summary>
        static Part Representative(Part a, Part b) => b switch
        {
            Part.LeftHand or Part.RightHand => b,
            Part.LeftFoot or Part.RightFoot => b,
            Part.LeftKnee or Part.RightKnee => b,
            Part.LeftElbow or Part.RightElbow => b,
            _ => a,
        };
    }
}
