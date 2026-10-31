using System;
using UnityEngine;

namespace MoveIt.Motion
{
    /// <summary>The lowest part of the body a game needs the camera to see.</summary>
    public enum BodyPart { Shoulders, Hips, Knees, Ankles }

    /// <summary>What's wrong with where the player stands, the most important first.</summary>
    public enum PlacementIssue
    {
        None,
        /// <summary>The body doesn't fit (head or <see cref="PlayArea.Lowest"/> out of the picture), or no room on either side.</summary>
        StepBack,
        /// <summary>Too little room on the screen's right: move to the left (the screen is a mirror: the player's left too).</summary>
        MoveLeft,
        MoveRight,
        /// <summary>Small in the picture: tracking gets noisier. Advice only, never blocks.</summary>
        ComeCloser,
    }

    /// <summary>Edges of the picture the body crosses or has too little room before. Left = the screen's left (view space).</summary>
    [Flags]
    public enum PictureEdges { None = 0, Left = 1, Right = 2, Top = 4, Bottom = 8 }

    /// <summary>
    /// What a game needs of where the player stands, in torso lengths, so the same numbers fit a child and an adult at
    /// any distance. Each game sets its own (<c>PlayerGate.Use</c>).
    /// </summary>
    public sealed class PlayArea
    {
        /// <summary>The lowest joints that must be in the picture. With the head, they also tell "too close".</summary>
        public readonly BodyPart Lowest;
        /// <summary>How far the player steps to each side of where they stand when a round starts (the torso's middle).</summary>
        public readonly float Side;
        /// <summary>Room above the head (jumps).</summary>
        public readonly float Above;
        /// <summary>During play the body may look this much bigger (steps toward the TV: 0.2 = 20 %).</summary>
        public readonly float Grow;

        PlayArea _inPlay;

        public PlayArea(BodyPart lowest, float side = 0f, float above = 0f, float grow = 0f)
        {
            Lowest = lowest;
            Side = side;
            Above = above;
            Grow = grow;
        }

        /// <summary>
        /// During play the player moves around inside the area on purpose, so only the body itself is checked, with a
        /// margin at the sides: the warning comes before the shoulders leave the picture.
        /// </summary>
        public PlayArea InPlay => _inPlay ??= new PlayArea(Lowest, PlacementCheck.WarnSide);
    }

    /// <summary>
    /// Is the player where the game needs them: the whole body that matters in the picture, with room to move? Shipped
    /// camera games all check this rather than calibrate: Kinect reported for each player which edges of the picture cut
    /// the body (<c>Body.ClippedEdges</c>) and its games paused with "step back" / "step forward"; Nex Playground asks
    /// for 1-4 m from the camera and a clear 1.8 m square, and its tracking help says a body that doesn't fit means the
    /// camera sits too low or too high. Our calibration is automatic and body-relative already; this is the missing part.
    /// <list type="bullet">
    /// <item>top: the head (nose + <see cref="HeadAboveNose"/>, or shoulders + <see cref="HeadAboveShoulders"/> when the
    /// face isn't seen) plus <see cref="PlayArea.Above"/>;</item>
    /// <item>bottom: the lower of <see cref="PlayArea.Lowest"/>'s two joints (one the camera barely sees doesn't count;
    /// neither seen = out of the picture);</item>
    /// <item>sides: <see cref="PlayArea.Side"/> + <see cref="BodyHalfWidth"/> from the torso's middle to each side;</item>
    /// <item>all of it as if the body were <see cref="PlayArea.Grow"/> bigger, around the torso's middle.</item>
    /// </list>
    /// Head or feet cut, or no room on either side = <see cref="PlacementIssue.StepBack"/>; no room on one side = move
    /// the other way; a torso under <see cref="FarTorso"/> picture heights = <see cref="PlacementIssue.ComeCloser"/>
    /// (advice). A flagged edge clears only with <see cref="Clear"/> more room, so standing on a border doesn't flicker.
    /// Pure logic on view-space joints (x in [0, aspect] to the screen's right, y in [0, 1] up): PlacementCheckTests.
    /// </summary>
    public sealed class PlacementCheck
    {
        /// <summary>Half the body's width beside the torso's middle, torso lengths: the shoulders (about 0.4) and a little.</summary>
        public const float BodyHalfWidth = 0.5f;
        /// <summary>Top of the head above the nose, torso lengths (an adult's is ~0.25, a child's ~0.33).</summary>
        public const float HeadAboveNose = 0.35f;
        /// <summary>Top of the head above the shoulders' middle when the face isn't seen, torso lengths.</summary>
        public const float HeadAboveShoulders = 0.75f;
        /// <summary>Picture heights kept free at every edge: the camera loses joints right at the border.</summary>
        public const float EdgeMargin = 0.02f;
        /// <summary>A flagged edge clears only with this much more room, torso lengths.</summary>
        public const float Clear = 0.1f;
        /// <summary>During play, the warning comes when the shoulders are this close to a side, torso lengths.</summary>
        public const float WarnSide = 0.2f;
        /// <summary>
        /// A torso smaller than this (picture heights) is far away: ~3.5 m for a child with the box's 100° camera, ~5 m for
        /// an adult. Clears above <see cref="FarClear"/>. A guess until a real camera says where the noise gets too big.
        /// </summary>
        public const float FarTorso = 0.075f, FarClear = 0.085f;
        /// <summary>A joint seen less surely than this doesn't count as the lowest one.</summary>
        public const float SeenVisibility = 0.3f;

        public PlacementIssue Issue { get; private set; }
        public PictureEdges Edges { get; private set; }
        /// <summary>Camera time (seconds) the current <see cref="Issue"/> started.</summary>
        public double IssueSince { get; private set; }

        /// <summary>The player can't calibrate or start here. <see cref="PlacementIssue.ComeCloser"/> is only advice.</summary>
        public bool Blocks => Issue is PlacementIssue.StepBack or PlacementIssue.MoveLeft or PlacementIssue.MoveRight;

        bool _far;

        public void Reset()
        {
            Issue = PlacementIssue.None;
            Edges = PictureEdges.None;
            _far = false;
        }

        /// <summary>Checks the smooth track (no jitter at the borders). <paramref name="area"/> null = nothing to check.</summary>
        public PlacementIssue Update(Body body, PlayArea area) =>
            Update(body.Joints, body.Visibility, body.Aspect, area, body.SampleTime);

        public PlacementIssue Update(Vector3[] joints, float[] visibility, float aspect, PlayArea area, double time)
        {
            var edges = PictureEdges.None;
            bool far = false;
            Vector2 shoulders = Mid(joints, Joint.LeftShoulder, Joint.RightShoulder);
            Vector2 hips = Mid(joints, Joint.LeftHip, Joint.RightHip);
            Vector2 c = (shoulders + hips) * 0.5f;
            float t = Vector2.Distance(shoulders, hips);
            if (area != null && t > 0.01f)
            {
                float grow = 1f + area.Grow;
                // Each edge: the room there (torso lengths, the margin taken off) against what the game needs.
                bool Short(PictureEdges edge, float room, float need) =>
                    (room - EdgeMargin) / t < need + ((Edges & edge) != 0 ? Clear : 0f);

                float side = (area.Side + BodyHalfWidth) * grow;
                if (Short(PictureEdges.Left, c.x, side)) edges |= PictureEdges.Left;
                if (Short(PictureEdges.Right, aspect - c.x, side)) edges |= PictureEdges.Right;

                float head = visibility[(int)Joint.Nose] >= 0.5f
                    ? joints[(int)Joint.Nose].y + HeadAboveNose * t
                    : shoulders.y + HeadAboveShoulders * t;
                if (Short(PictureEdges.Top, 1f - c.y, ((head - c.y) / t + area.Above) * grow)) edges |= PictureEdges.Top;

                if (area.Lowest != BodyPart.Shoulders)
                {
                    var (a, b) = Pair(area.Lowest);
                    bool seenA = visibility[(int)a] >= SeenVisibility, seenB = visibility[(int)b] >= SeenVisibility;
                    float low = seenA && seenB ? Mathf.Min(joints[(int)a].y, joints[(int)b].y)
                        : seenA ? joints[(int)a].y : seenB ? joints[(int)b].y : -1f;   // neither seen: out of the picture
                    if (Short(PictureEdges.Bottom, c.y, (c.y - low) / t * grow)) edges |= PictureEdges.Bottom;
                }
                far = t < (_far ? FarClear : FarTorso);
            }

            Edges = edges;
            _far = far;
            var issue = IssueFor(edges, far);
            if (issue != Issue) IssueSince = time;
            Issue = issue;
            return issue;
        }

        static PlacementIssue IssueFor(PictureEdges edges, bool far)
        {
            const PictureEdges sides = PictureEdges.Left | PictureEdges.Right;
            if ((edges & (PictureEdges.Top | PictureEdges.Bottom)) != 0 || (edges & sides) == sides) return PlacementIssue.StepBack;
            if ((edges & PictureEdges.Left) != 0) return PlacementIssue.MoveRight;
            if ((edges & PictureEdges.Right) != 0) return PlacementIssue.MoveLeft;
            return far ? PlacementIssue.ComeCloser : PlacementIssue.None;
        }

        static (Joint, Joint) Pair(BodyPart part) => part switch
        {
            BodyPart.Hips => (Joint.LeftHip, Joint.RightHip),
            BodyPart.Knees => (Joint.LeftKnee, Joint.RightKnee),
            BodyPart.Ankles => (Joint.LeftAnkle, Joint.RightAnkle),
            _ => (Joint.LeftShoulder, Joint.RightShoulder),
        };

        static Vector2 Mid(Vector3[] joints, Joint a, Joint b) => (joints[(int)a] + joints[(int)b]) * 0.5f;
    }
}
