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
        /// <summary>Someone in view, standing still to calibrate.</summary>
        Calibrating,
        Ready,
    }

    /// <summary>The "step in → hold still → raise hands" flow every game shares, plus the pause rule.</summary>
    public static class PlayerGate
    {
        public static GateState State(BodyInput input)
        {
            var motion = MotionSystem.Instance;
            if (motion == null || !motion.IsConnected) return GateState.NoService;
            if (input == null || !input.HasPlayer) return GateState.NoPlayer;
            return input.IsCalibrated ? GateState.Ready : GateState.Calibrating;
        }

        /// <summary>Pause while a camera player has left the view or is re-calibrating. Keyboard play never pauses.</summary>
        public static bool ShouldPause(BodyInput input) =>
            !input.UsingKeyboard && State(input) is GateState.NoPlayer or GateState.Calibrating;

        /// <summary>Start/confirm: both hands up, Enter, or Space when playing with the keyboard.</summary>
        public static bool StartPressed(BodyInput input) =>
            input.ConfirmTriggered || (input.UsingKeyboard && input.JumpTriggered);

        /// <summary>Draws the waiting-screen prompt for the current gate state. Returns the state.</summary>
        public static GateState DrawWaiting(float width, BodyInput input, string title, string keyboardHelp)
        {
            var state = State(input);
            GameHud.Text(new Rect(0, 140, width, 120), title, GameHud.Big, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.2f));
            switch (state)
            {
                case GateState.NoService:
                    GameHud.Message(width, CommonStrings.PressEnter, $"{CommonStrings.NoMotionService}   ({keyboardHelp})");
                    break;
                case GateState.NoPlayer:
                    GameHud.Message(width, CommonStrings.StepIn);
                    break;
                case GateState.Calibrating:
                    GameHud.Message(width, CommonStrings.HoldStill, $"{input.Gestures.CalibrationProgress * 100f:0}%");
                    break;
                default:
                    GameHud.Message(width, CommonStrings.RaiseHands, CommonStrings.OrPressEnter);
                    break;
            }
            return state;
        }

        /// <summary>Message to show while <see cref="ShouldPause"/> is true.</summary>
        public static void DrawPaused(float width, BodyInput input) =>
            GameHud.Message(width, input.HasPlayer ? CommonStrings.HoldStill : CommonStrings.ComeBack);
    }
}
