using UnityEngine;

namespace MoveIt.Games.Paint
{
    public enum PenState
    {
        /// <summary>Hand not seen, resting at the side, or off the screen: no cursor.</summary>
        Away,
        /// <summary>Cursor shown, brush up: the switch is down, or the cursor is over a toolbar.</summary>
        Aiming,
        /// <summary>Brush down: the hand paints wherever it goes until the switch drops, it rests, leaves the picture or is lost.</summary>
        Painting,
    }

    /// <summary>
    /// When does a hand in the air paint? A camera can't see a pen touching paper, so Pikturo gives the job to the
    /// other hand: <b>one hand paints, the other is the switch</b>. Free hand raised (to the chest) = brush down,
    /// lowered = brush up. Starting and stopping is instant and exactly where the player wants, and the painting hand
    /// can move freely to a new start point with the switch down.
    ///
    /// (Before 2026-10-09 each hand painted after holding still for a moment. The player found the brush went down
    /// "immediately", and moving to a new spot meant dropping the arm. The simulation in tools/motion/cursor_sim.py
    /// agreed: a slow hand still on its way already counted as still.)
    ///
    /// Toolbars are never painted on: over a toolbar the cursor presses buttons instead.
    /// </summary>
    public sealed class PaintPen
    {
        public PenState State { get; private set; }
        /// <summary>Screen position, 0..1 from the bottom-left.</summary>
        public Vector2 Position { get; private set; }
        public bool Visible => State != PenState.Away;

        /// <summary>
        /// One frame of the painting hand. <paramref name="overPicture"/> = on the screen and not over a toolbar;
        /// <paramref name="resting"/> = arm hanging at the side; <paramref name="switchOn"/> = the other hand is raised.
        /// Returns +1 when the brush goes down this frame, -1 when it lifts, 0 otherwise.
        /// </summary>
        public int UpdateSwitch(bool tracked, Vector2 position, bool onScreen, bool overPicture, bool resting, bool switchOn)
        {
            Position = position;
            var before = State;
            if (!tracked || !onScreen || resting) State = PenState.Away;
            else State = overPicture && switchOn ? PenState.Painting : PenState.Aiming;
            return Change(before);
        }

        /// <summary>Mouse as the brush (development without a camera): paints while the button is held.</summary>
        public int UpdateMouse(Vector2 position, bool onScreen, bool overPicture, bool buttonHeld)
        {
            Position = position;
            var before = State;
            State = !onScreen ? PenState.Away : overPicture && buttonHeld ? PenState.Painting : PenState.Aiming;
            return Change(before);
        }

        /// <summary>Lifts the brush at once (mode switch, toolbar action).</summary>
        public int Lift()
        {
            var before = State;
            if (State == PenState.Painting) State = PenState.Aiming;
            return Change(before);
        }

        int Change(PenState before)
        {
            if (State == PenState.Painting && before != PenState.Painting) return 1;
            if (State != PenState.Painting && before == PenState.Painting) return -1;
            return 0;
        }
    }
}
