using MoveIt.Motion;
using UnityEngine;

namespace MoveIt.Games
{
    public enum GateState
    {
        /// <summary>No motion service running: keyboard/mouse play.</summary>
        NoService,
        /// <summary>Service running, nobody in view.</summary>
        NoPlayer,
        /// <summary>Someone in view, but not where the game needs them (too close, too near a side).</summary>
        Placing,
        /// <summary>Someone in view, standing still to calibrate.</summary>
        Calibrating,
        Ready,
    }

    /// <summary>
    /// The "step in → stand where the game needs you → hold still → raise hands" flow every game shares, the pause rule,
    /// and the warning before a player leaves the picture. Where a game needs the player: <see cref="Use"/>.
    /// </summary>
    public static class PlayerGate
    {
        /// <summary>Seconds a player stays near a side before the warning shows: a quick step there and back gets none.</summary>
        const float SideWarnAfter = 0.25f;
        /// <summary>Seconds the head or the legs stay out of the picture before "step back" shows: a jump takes the head out for a moment.</summary>
        const float CutWarnAfter = 0.6f;

        /// <summary>Call in a game's Start with where it needs the player (null = anywhere).</summary>
        public static void Use(BodyInput input, PlayArea area)
        {
            if (input != null && input.Gestures != null) input.Gestures.Area = area;
        }

        public static GateState State(BodyInput input)
        {
            var motion = MotionSystem.Instance;
            if (motion == null || !motion.IsConnected) return GateState.NoService;
            if (input == null || !input.HasPlayer) return GateState.NoPlayer;
            if (input.IsCalibrated) return GateState.Ready;
            return input.Gestures.Placement.Blocks ? GateState.Placing : GateState.Calibrating;
        }

        /// <summary>Pause while a camera player has left the view or is re-calibrating. Keyboard play never pauses.</summary>
        public static bool ShouldPause(BodyInput input) =>
            !input.UsingKeyboard && State(input) is GateState.NoPlayer or GateState.Placing or GateState.Calibrating;

        /// <summary>Start/confirm: both hands up (only where the game needs the player), Enter, or Space when playing with the keyboard.</summary>
        public static bool StartPressed(BodyInput input) =>
            input.ConfirmTriggered || (input.UsingKeyboard && input.JumpTriggered);

        /// <summary>
        /// Where the player must go before they can start, as one line ("Take a step back. The camera needs ..."), or null.
        /// For screens that start a round with hands up (the menu, a results screen).
        /// </summary>
        public static string PlacementHint(BodyInput input)
        {
            var g = input != null ? input.Gestures : null;
            if (g == null) return null;
            g.PlacementShownFrame = Time.frameCount;
            if (!g.HasPlayer || !g.Placement.Blocks) return null;
            var (title, why) = PlacementText(g);
            return g.Placement.Issue == PlacementIssue.StepBack ? $"{title}. {why}" : $"{title}, {why}";
        }

        /// <summary>A results screen's last line: Enter without a camera, where to stand when hands up can't start from here, else hands up.</summary>
        public static string PlayAgainText(BodyInput input) =>
            State(input) == GateState.NoService ? CommonStrings.PressEnter : PlacementHint(input) ?? CommonStrings.PlayAgain;

        /// <summary>Draws the waiting-screen prompt for the current gate state. Returns the state.</summary>
        public static GateState DrawWaiting(float width, BodyInput input, string title, string keyboardHelp)
        {
            var state = State(input);
            if (input != null && input.Gestures != null) input.Gestures.PlacementShownFrame = Time.frameCount;
            GameHud.Text(new Rect(0, 140, width, 120), title, GameHud.Big, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.2f));
            switch (state)
            {
                case GateState.NoService:
                    GameHud.Message(width, CommonStrings.PressEnter, $"{CommonStrings.NoMotionService}   ({keyboardHelp})");
                    break;
                case GateState.NoPlayer:
                    GameHud.Message(width, CommonStrings.StepIn);
                    break;
                case GateState.Placing:
                    DrawPlacement(width, input.Gestures);
                    break;
                case GateState.Calibrating:
                    GameHud.Message(width, CommonStrings.HoldStill, Advice(input) ?? $"{input.Gestures.CalibrationProgress * 100f:0}%");
                    break;
                default:
                    // Calibrated, then walked somewhere the game can't be played from: hands up doesn't start here.
                    if (input.Gestures.Placement.Blocks) DrawPlacement(width, input.Gestures);
                    else GameHud.Message(width, CommonStrings.RaiseHands, Advice(input) ?? CommonStrings.OrPressEnter);
                    break;
            }
            return state;
        }

        /// <summary>Message to show while <see cref="ShouldPause"/> is true.</summary>
        public static void DrawPaused(float width, BodyInput input)
        {
            if (input.Gestures != null) input.Gestures.PlacementShownFrame = Time.frameCount;
            if (!input.HasPlayer) GameHud.Message(width, CommonStrings.ComeBack);
            else if (input.Gestures.Placement.Blocks) DrawPlacement(width, input.Gestures);
            else GameHud.Message(width, CommonStrings.HoldStill);
        }

        /// <summary>
        /// During play: arrows at the screen's side and a banner when the player is about to leave the picture (or has
        /// left it with the head or the legs), before the game has to pause. Call while playing and not paused;
        /// <paramref name="bannerY"/> = where the banner goes (keep it clear of the game's own bottom line).
        /// </summary>
        public static void DrawEdgeWarning(float width, BodyInput input, float bannerY = 820f)
        {
            if (input == null || input.UsingKeyboard || State(input) != GateState.Ready) return;
            var g = input.Gestures;
            var check = g.InPlay;
            if (check.Issue is PlacementIssue.None or PlacementIssue.ComeCloser || g.Body == null) return;
            float age = (float)(g.Body.SampleTime - check.IssueSince);
            if (age < (check.Issue == PlacementIssue.StepBack ? CutWarnAfter : SideWarnAfter)) return;

            var color = Brand.Current.SecondaryColor;
            string text = check.Issue switch
            {
                PlacementIssue.MoveLeft => CommonStrings.MoveLeft,
                PlacementIssue.MoveRight => CommonStrings.MoveRight,
                _ => CommonStrings.StepBack,
            };
            GameHud.Banner(new Rect(width / 2f - 380f, bannerY, 760f, 76f), text, color);
            if (check.Issue == PlacementIssue.MoveLeft) GameHud.Chevrons(new Vector2(130f, 540f), -1, 110f, color);
            if (check.Issue == PlacementIssue.MoveRight) GameHud.Chevrons(new Vector2(width - 130f, 540f), 1, 110f, color);
        }

        /// <summary>The big "go there" message with arrows to the side the player should move to.</summary>
        static void DrawPlacement(float width, BodyGestures g)
        {
            var (title, why) = PlacementText(g);
            if (title == null) return;
            var color = Brand.Current.SecondaryColor;
            GameHud.Message(width, title, why, color);
            if (g.Placement.Issue == PlacementIssue.MoveLeft) GameHud.Chevrons(new Vector2(170f, 370f), -1, 120f, color);
            if (g.Placement.Issue == PlacementIssue.MoveRight) GameHud.Chevrons(new Vector2(width - 170f, 370f), 1, 120f, color);
        }

        static (string title, string why) PlacementText(BodyGestures g)
        {
            var p = g.Placement;
            bool cut = (p.Edges & (PictureEdges.Top | PictureEdges.Bottom)) != 0;
            return p.Issue switch
            {
                PlacementIssue.StepBack => (CommonStrings.StepBack,
                    cut ? CommonStrings.SeeFromHeadTo(g.Area != null ? g.Area.Lowest : BodyPart.Shoulders) : CommonStrings.RoomToMove),
                PlacementIssue.MoveLeft => (CommonStrings.MoveLeft, CommonStrings.RoomToMove),
                PlacementIssue.MoveRight => (CommonStrings.MoveRight, CommonStrings.RoomToMove),
                _ => (null, null),
            };
        }

        /// <summary>Advice that doesn't stop the player (far from the camera), for a message's second line.</summary>
        static string Advice(BodyInput input) =>
            input.Gestures.Placement.Issue == PlacementIssue.ComeCloser ? CommonStrings.ComeCloser : null;
    }
}
