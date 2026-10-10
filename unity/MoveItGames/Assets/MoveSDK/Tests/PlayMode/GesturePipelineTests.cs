using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MoveIt.Motion.Tests
{
    /// <summary>
    /// End-to-end: motion-service-py/fake_sender.py (scripted player) -> UDP -> MotionSystem -> BodyTracker ->
    /// gesture code. Each scripted move must produce exactly the expected result, with no false triggers.
    /// </summary>
    public class GesturePipelineTests
    {
        const int Port = 5011;

        GameObject _go;
        Process _sender;

        [TearDown]
        public void TearDown()
        {
            if (_sender != null && !_sender.HasExited) _sender.Kill();
            _sender?.Dispose();
            _sender = null;
            if (_go != null) Object.Destroy(_go);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator BasicScript_ProducesExpectedGesturesInOrder()
        {
            var gestures = CreateRig().GetComponent<BodyGestures>();
            var seen = new List<Gesture>();
            float jumpHeightAtDetection = float.NaN, sideAtFirstLane = float.NaN;
            gestures.OnGesture += g =>
            {
                seen.Add(g);
                if (g == Gesture.Jump) jumpHeightAtDetection = gestures.VerticalOffset;
                if (g == Gesture.LaneLeft && float.IsNaN(sideAtFirstLane)) sideAtFirstLane = Mathf.Abs(gestures.LateralOffset);
            };

            yield return RunFakePlayer("basic", null);

            var expected = new[]
            {
                Gesture.PlayerFound, Gesture.Calibrated,
                Gesture.LaneLeft, Gesture.LaneCenter, Gesture.LaneRight, Gesture.LaneCenter,
                Gesture.Jump, Gesture.CrouchStart, Gesture.CrouchEnd, Gesture.HandsUp,
            };
            CollectionAssert.AreEqual(expected, seen, "Gestures seen: " + string.Join(", ", seen));
            UnityEngine.Debug.Log($"[Test] Jump detected at {jumpHeightAtDetection:0.00} torso lengths above standing, " +
                                  $"first lane switch at {sideAtFirstLane:0.00} torso lengths out (threshold {gestures.LaneEnter:0.00})");
            // This script's jump starts at full speed from standing (no knee dip, physically impossible); it rises 0.74
            // torso lengths. The body-relative filters (2026-10-08) catch it at ~0.19-0.20, a few ms later than before,
            // while real knee-dip jumps got much faster (RealJumpsWithKneeDip test). 0.25 = still the first third.
            Assert.LessOrEqual(jumpHeightAtDetection, 0.25f, "Jump should be detected at take-off, early in the rise");
            Assert.Less(sideAtFirstLane, gestures.LaneEnter, "A fast sidestep should switch lanes before reaching the threshold (look-ahead)");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator RealJumpsWithKneeDip_AreJumpsNotCrouches()
        {
            var gestures = CreateRig().GetComponent<BodyGestures>();
            var seen = new List<Gesture>();
            gestures.OnGesture += seen.Add;

            yield return RunFakePlayer("jumps", null);

            string all = string.Join(", ", seen);
            var vertical = seen.FindAll(g => g == Gesture.Jump || g == Gesture.CrouchStart || g == Gesture.CrouchEnd);
            Assert.AreEqual(3, vertical.FindAll(g => g == Gesture.Jump).Count, "Hop, small hop and big jump must all be jumps. Seen: " + all);
            CollectionAssert.AreEqual(new[] { Gesture.Jump, Gesture.Jump }, vertical.GetRange(0, 2),
                "The knee dip before a normal or small hop must not be read as a crouch (the Runner would slide). Seen: " + all);
            CollectionAssert.AreEqual(new[] { Gesture.Jump, Gesture.CrouchStart, Gesture.CrouchEnd }, vertical.GetRange(vertical.Count - 3, 3),
                "The duck must be a crouch, and standing up fast after it must not be a jump. Seen: " + all);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator IdleFidgeting_TriggersNothing()
        {
            var gestures = CreateRig().GetComponent<BodyGestures>();
            var seen = new List<Gesture>();
            gestures.OnGesture += seen.Add;
            var detector = new UnderhandThrowDetector();
            int throws = 0;

            yield return RunFakePlayer("idle", () =>
            {
                var body = MotionSystem.Instance.ActiveBody;
                if (gestures.IsCalibrated && detector.Update(body, gestures.BaselineTorso, out _)) throws++;
            });

            CollectionAssert.AreEqual(new[] { Gesture.PlayerFound, Gesture.Calibrated }, seen,
                "Swaying, scratching the head, a small knee bend or tiptoes must not trigger anything. Seen: " + string.Join(", ", seen));
            Assert.AreEqual(0, throws, "No bowling throws while fidgeting");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator BriefDropout_KeepsPlayerAndCalibration()
        {
            var gestures = CreateRig().GetComponent<BodyGestures>();
            var seen = new List<Gesture>();
            gestures.OnGesture += seen.Add;

            yield return RunFakePlayer("dropout", null);

            var expected = new[] { Gesture.PlayerFound, Gesture.Calibrated, Gesture.LaneLeft, Gesture.LaneCenter };
            CollectionAssert.AreEqual(expected, seen,
                "Losing the player for 1 s must not lose or recalibrate them. Seen: " + string.Join(", ", seen));
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator BowlScript_DetectsThreeThrowsAndIgnoresHandsUp()
        {
            var gestures = CreateRig().GetComponent<BodyGestures>();
            var detector = new UnderhandThrowDetector();
            var throws = new List<ThrowInfo>();
            bool handsUpSeen = false;
            gestures.OnGesture += g => handsUpSeen |= g == Gesture.HandsUp;

            yield return RunFakePlayer("bowl", () =>
            {
                var body = MotionSystem.Instance.ActiveBody;
                if (gestures.IsCalibrated && detector.Update(body, gestures.BaselineTorso, out var info)) throws.Add(info);
            });

            Assert.IsTrue(handsUpSeen, "The script's final 'hands up' was not recognised, so the no-throw check proves nothing.");
            Assert.AreEqual(3, throws.Count, "Throws: " + string.Join(", ", throws.ConvertAll(t => $"{(t.RightHand ? "R" : "L")} {t.Speed:0.0}")));
            foreach (var t in throws)
            {
                Assert.IsTrue(t.RightHand, "The script swings the right arm");
                Assert.GreaterOrEqual(t.Speed, detector.MinUpSpeed);
                Assert.Less(Mathf.Abs(t.SideSpeed), 0.6f, "A straight swing must roll straight (inside Bowling's aim dead zone)");
            }
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator SlashScript_RightHandSweepsAcrossScreenFast()
        {
            var hands = CreateRig().GetComponent<BodyHands>();
            typeof(BodyHands).GetField("mouseFallback", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(hands, false);
            float minX = float.MaxValue, maxX = float.MinValue, maxSpeed = 0f;

            yield return RunFakePlayer("slash", () =>
            {
                var right = hands.Right;
                if (!right.Tracked) return;
                minX = Mathf.Min(minX, right.Position.x);
                maxX = Mathf.Max(maxX, right.Position.x);
                maxSpeed = Mathf.Max(maxSpeed, right.Speed);
            });

            Assert.Less(minX, 0.3f, "Hand should reach the left part of the screen");
            Assert.Greater(maxX, 0.7f, "Hand should reach the right part of the screen");
            Assert.Greater(maxSpeed, 1.8f, "Swipe should be fast enough to count as a slash in Fruta");
        }

        GameObject CreateRig()
        {
            _go = new GameObject("Motion (test)");
            _go.SetActive(false);
            var system = _go.AddComponent<MotionSystem>();
            typeof(MotionSystem).GetField("port", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(system, Port);
            _go.AddComponent<BodyGestures>();
            _go.AddComponent<BodyHands>();
            _go.SetActive(true);
            return _go;
        }

        IEnumerator RunFakePlayer(string script, Action everyFrame)
        {
            string serviceDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../motion-service-py"));
            string python = Path.Combine(serviceDir, ".venv/Scripts/python.exe");
            Assume.That(File.Exists(python), $"Python venv not found at {python}");

            _sender = Process.Start(new ProcessStartInfo(python, $"fake_sender.py --script {script} --port {Port} --once")
            {
                WorkingDirectory = serviceDir,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            float deadline = Time.realtimeSinceStartup + 40f;
            while (!_sender.HasExited && Time.realtimeSinceStartup < deadline)
            {
                everyFrame?.Invoke();
                yield return null;
            }
            Assert.IsTrue(_sender.HasExited, $"fake_sender.py --script {script} did not finish in time");
        }
    }
}
