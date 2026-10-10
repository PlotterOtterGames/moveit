using System.Collections;
using System.Collections.Generic;
using System.IO;
using MoveIt.Games;
using MoveIt.Motion;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MoveIt.Shell.Tests
{
    /// <summary>Drives the box's home screen with simulated TV-remote keys (D-pad = arrows, OK = Enter, Back = Esc).</summary>
    public class HomeScreenTests
    {
        Keyboard _keyboard;
#if UNITY_EDITOR
        InputSettings.EditorInputBehaviorInPlayMode _editorInput;
#endif
        InputSettings.BackgroundBehavior _background;
        readonly List<InputDevice> _realKeyboards = new List<InputDevice>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
#if UNITY_EDITOR
            // By default the editor only lets keyboard input through while the Game view has focus, which made
            // this test flaky in full runs (focus is often on another window). The simulated remote must always count.
            _editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            // Losing focus resets and disables keyboards by default: keep the simulated one working.
            _background = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            // A keyboard of its own: with "all input goes to the game", any real keystroke on the machine (someone typing
            // in another window) replaced the whole simulated keyboard state and the test's key press was lost.
            foreach (var device in InputSystem.devices)
                if (device is Keyboard && device.enabled) _realKeyboards.Add(device);
            foreach (var device in _realKeyboards) InputSystem.DisableDevice(device);
            _keyboard = InputSystem.AddDevice<Keyboard>("Test remote");
            _keyboard.MakeCurrent();
            SceneManager.LoadScene("Menu");
            yield return null;
            yield return null;
            yield return new WaitForSecondsRealtime(0.3f);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // The motion rig survives scene loads (DontDestroyOnLoad); remove it so other tests start clean.
            if (MotionSystem.Instance != null) Object.Destroy(MotionSystem.Instance.gameObject);
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = _editorInput;
#endif
            InputSystem.settings.backgroundBehavior = _background;
            if (_keyboard != null) InputSystem.RemoveDevice(_keyboard);
            foreach (var device in _realKeyboards) InputSystem.EnableDevice(device);
            _realKeyboards.Clear();
            yield return null;
        }

        IEnumerator Press(Key key)
        {
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(key));
            yield return null;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;
        }

        static IEnumerator Screenshot(string name)
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../captures"));
            Directory.CreateDirectory(dir);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, name));
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Remote_NavigatesHome_AndSettings()
        {
            var menu = Object.FindAnyObjectByType<MainMenu>();
            Assert.IsNotNull(menu, "Menu scene has no MainMenu");
            Assert.AreEqual(1, menu.SelectedGame, "starts on the middle tile");
            yield return Screenshot("home.png");

            yield return Press(Key.RightArrow);
            Assert.AreEqual(2, menu.SelectedGame, "→ selects the next game");

            yield return Press(Key.DownArrow);
            Assert.IsTrue(menu.SettingsButtonFocused, "↓ reaches the Settings button");
            yield return Press(Key.Enter);
            Assert.IsTrue(menu.SettingsOpen, "OK opens Settings");
            Assert.AreEqual(MainMenu.SettingsItem.Wifi, menu.SettingsFocus);

            float before = AndroidDevice.Volume;
            yield return Press(Key.DownArrow);
            Assert.AreEqual(MainMenu.SettingsItem.Volume, menu.SettingsFocus);
            yield return Press(Key.RightArrow);
            Assert.AreEqual(Mathf.Min(1f, before + 0.1f), AndroidDevice.Volume, 0.001f, "→ raises the volume by 10%");

            for (int i = 0; i < 4; i++) yield return Press(Key.DownArrow);
            Assert.AreEqual(MainMenu.SettingsItem.Device, menu.SettingsFocus);
            Assert.IsFalse(menu.AdminUnlocked, "Android settings stay hidden by default");
            for (int i = 0; i < 7; i++) yield return Press(Key.Enter);
            Assert.IsTrue(menu.AdminUnlocked, "7 × OK on Device reveals the Android settings entry");
            yield return Screenshot("settings.png");

            yield return Press(Key.Escape);
            Assert.IsFalse(menu.SettingsOpen, "Back closes Settings");
            Assert.IsFalse(menu.AdminUnlocked, "closing Settings hides the Android entry again");
            Assert.AreEqual(2, menu.SelectedGame, "the selected game is unchanged after using → in Settings");
            AndroidDevice.Volume = before;
        }
    }
}
