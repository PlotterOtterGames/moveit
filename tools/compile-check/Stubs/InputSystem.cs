// Stand-in for the parts of Unity's Input System package (com.unity.inputsystem) the game code uses, so it compiles
// outside Unity (see check.py). Signatures only: nothing here runs. Add members as the game starts using them.
using System;
using System.Collections.Generic;

namespace UnityEngine.InputSystem.Controls
{
    public class InputControl
    {
        public string name => "";
        public InputDevice device => null;
    }

    public class ButtonControl : InputControl
    {
        public bool isPressed => false;
        public bool wasPressedThisFrame => false;
        public bool wasReleasedThisFrame => false;
        public float ReadValue() => 0f;
    }

    public class KeyControl : ButtonControl
    {
        public Key keyCode => Key.None;
    }

    public class DpadControl : InputControl
    {
        public ButtonControl up => null;
        public ButtonControl down => null;
        public ButtonControl left => null;
        public ButtonControl right => null;
    }

    public class Vector2Control : InputControl
    {
        public Vector2 ReadValue() => default;
    }
}

namespace UnityEngine.InputSystem
{
    using UnityEngine.InputSystem.Controls;

    public enum Key
    {
        None, Space, Enter, Tab, Backquote, Quote, Semicolon, Comma, Period, Slash, Backslash, LeftBracket, RightBracket,
        Minus, Equals, A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
        Digit1, Digit2, Digit3, Digit4, Digit5, Digit6, Digit7, Digit8, Digit9, Digit0,
        LeftShift, RightShift, LeftAlt, RightAlt, LeftCtrl, RightCtrl, LeftMeta, RightMeta, ContextMenu, Escape,
        LeftArrow, RightArrow, UpArrow, DownArrow, Backspace, PageDown, PageUp, Home, End, Insert, Delete, CapsLock,
        NumLock, PrintScreen, ScrollLock, Pause, NumpadEnter, NumpadDivide, NumpadMultiply, NumpadPlus, NumpadMinus,
        NumpadPeriod, NumpadEquals, Numpad0, Numpad1, Numpad2, Numpad3, Numpad4, Numpad5, Numpad6, Numpad7, Numpad8,
        Numpad9, F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    }

    public class InputDevice : InputControl
    {
        public bool enabled => true;
        public int deviceId => 0;
        public virtual void MakeCurrent() { }
    }

    public class Keyboard : InputDevice
    {
        public static Keyboard current => null;
        public KeyControl this[Key key] => null;
        public KeyControl anyKey => null;
        public KeyControl spaceKey => null;
        public KeyControl enterKey => null;
        public KeyControl numpadEnterKey => null;
        public KeyControl escapeKey => null;
        public KeyControl backspaceKey => null;
        public KeyControl tabKey => null;
        public KeyControl leftArrowKey => null;
        public KeyControl rightArrowKey => null;
        public KeyControl upArrowKey => null;
        public KeyControl downArrowKey => null;
        public KeyControl leftShiftKey => null;
        public KeyControl rightShiftKey => null;
        public KeyControl leftCtrlKey => null;
        public KeyControl rightCtrlKey => null;
        public KeyControl aKey => null;
        public KeyControl bKey => null;
        public KeyControl cKey => null;
        public KeyControl dKey => null;
        public KeyControl eKey => null;
        public KeyControl fKey => null;
        public KeyControl gKey => null;
        public KeyControl hKey => null;
        public KeyControl iKey => null;
        public KeyControl jKey => null;
        public KeyControl kKey => null;
        public KeyControl lKey => null;
        public KeyControl mKey => null;
        public KeyControl nKey => null;
        public KeyControl oKey => null;
        public KeyControl pKey => null;
        public KeyControl qKey => null;
        public KeyControl rKey => null;
        public KeyControl sKey => null;
        public KeyControl tKey => null;
        public KeyControl uKey => null;
        public KeyControl vKey => null;
        public KeyControl wKey => null;
        public KeyControl xKey => null;
        public KeyControl yKey => null;
        public KeyControl zKey => null;
        public KeyControl digit0Key => null;
        public KeyControl digit1Key => null;
        public KeyControl digit2Key => null;
        public KeyControl digit3Key => null;
        public KeyControl digit4Key => null;
        public KeyControl digit5Key => null;
        public KeyControl digit6Key => null;
        public KeyControl digit7Key => null;
        public KeyControl digit8Key => null;
        public KeyControl digit9Key => null;
        public KeyControl f1Key => null;
        public KeyControl f2Key => null;
        public KeyControl f3Key => null;
        public KeyControl f4Key => null;
        public KeyControl f5Key => null;
        public KeyControl f6Key => null;
        public KeyControl f7Key => null;
        public KeyControl f8Key => null;
        public KeyControl f9Key => null;
        public KeyControl f10Key => null;
        public KeyControl f11Key => null;
        public KeyControl f12Key => null;
    }

    public class Gamepad : InputDevice
    {
        public static Gamepad current => null;
        public DpadControl dpad => null;
        public ButtonControl buttonSouth => null;
        public ButtonControl buttonEast => null;
        public ButtonControl buttonNorth => null;
        public ButtonControl buttonWest => null;
        public ButtonControl startButton => null;
        public ButtonControl selectButton => null;
        public ButtonControl leftShoulder => null;
        public ButtonControl rightShoulder => null;
    }

    public class Mouse : InputDevice
    {
        public static Mouse current => null;
        public Vector2Control position => null;
        public Vector2Control delta => null;
        public ButtonControl leftButton => null;
        public ButtonControl rightButton => null;
    }

    public class InputSettings : ScriptableObject
    {
        public enum BackgroundBehavior { ResetAndDisableNonBackgroundDevices, ResetAndDisableAllDevices, IgnoreFocus }
        public enum EditorInputBehaviorInPlayMode { PointersAndKeyboardsRespectGameViewFocus, AllDeviceInputAlwaysGoesToGameView, AllDevicesRespectGameViewFocus }

        public BackgroundBehavior backgroundBehavior { get; set; }
        public EditorInputBehaviorInPlayMode editorInputBehaviorInPlayMode { get; set; }
    }

    public static class InputSystem
    {
        public static InputSettings settings { get; set; }
        public static IReadOnlyList<InputDevice> devices => Array.Empty<InputDevice>();
        public static TDevice AddDevice<TDevice>(string name = null) where TDevice : InputDevice => null;
        public static void RemoveDevice(InputDevice device) { }
        public static void EnableDevice(InputDevice device) { }
        public static void DisableDevice(InputDevice device) { }
        public static void QueueStateEvent<TState>(InputDevice device, TState state, double time = -1) where TState : struct { }
        public static void Update() { }
    }
}

namespace UnityEngine.InputSystem.LowLevel
{
    public struct KeyboardState
    {
        public KeyboardState(params Key[] pressedKeys) { }
    }
}
