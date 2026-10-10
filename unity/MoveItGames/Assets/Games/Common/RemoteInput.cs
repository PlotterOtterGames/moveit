using UnityEngine.InputSystem;

namespace MoveIt.Games
{
    /// <summary>
    /// TV-remote navigation in one place. On Android boxes the remote's D-pad arrives as arrow keys and Back as Escape.
    /// OK can arrive as Enter, as D-pad center, or as a gamepad button depending on the box, so all are accepted here.
    /// If a box's remote behaves differently (Phase 1 checklist), fix it in this class only.
    /// </summary>
    public static class RemoteInput
    {
        public static bool Up => Key(k => k.upArrowKey) || Pad(p => p.dpad.up.wasPressedThisFrame);
        public static bool Down => Key(k => k.downArrowKey) || Pad(p => p.dpad.down.wasPressedThisFrame);
        public static bool Left => Key(k => k.leftArrowKey) || Pad(p => p.dpad.left.wasPressedThisFrame);
        public static bool Right => Key(k => k.rightArrowKey) || Pad(p => p.dpad.right.wasPressedThisFrame);

        public static bool Confirm =>
            Key(k => k.enterKey) || Key(k => k.numpadEnterKey) || Key(k => k.spaceKey) || Pad(p => p.buttonSouth.wasPressedThisFrame);

        public static bool Back =>
            Key(k => k.escapeKey) || Key(k => k.backspaceKey) || Pad(p => p.buttonEast.wasPressedThisFrame);

        static bool Key(System.Func<Keyboard, UnityEngine.InputSystem.Controls.KeyControl> key)
        {
            var kb = Keyboard.current;
            return kb != null && key(kb).wasPressedThisFrame;
        }

        static bool Pad(System.Func<Gamepad, bool> pressed)
        {
            var pad = Gamepad.current;
            return pad != null && pressed(pad);
        }
    }
}
