using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using MoveIt.Motion;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MoveIt.Games.Arcade.Tests
{
    /// <summary>
    /// Lunapark! end to end (2026-11-09): motion-service-py/fake_sender.py (a scripted player and a drawn camera picture)
    /// -> UDP -> the Arcade scene. Each booth played by its script for a while: the hands bonk moles and slice fruit, a held
    /// Y passes Y walls, steps catch sweets, flapping flies the bird, the hands keep a ball up; two players each get their
    /// own moles; a party runs through its five rounds. Screenshots: unity/captures/arcade_*.png.
    /// </summary>
    public class ArcadeGameTests
    {
        const int MotionPort = 5024, VideoPort = 5026, ControlPort = 5027;

        Process _sender;
        ArcadeGame _game;
        readonly List<ArcadeEvent> _events = new List<ArcadeEvent>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            PlayerPrefs.DeleteKey("arcade.progress");
            PlayerPrefs.SetInt("arcade.players", 0);
            PlayerPrefs.SetInt("arcade.party", 0);
            CameraFeed.VideoPort = VideoPort;
            CameraFeed.ControlPort = ControlPort;
            SceneManager.LoadScene("Arcade");
            yield return null;
            yield return null;
            _game = Object.FindAnyObjectByType<ArcadeGame>();
            Assert.IsNotNull(_game, "Arcade scene has no ArcadeGame");
            var motion = MotionSystem.Instance;
            motion.enabled = false;
            typeof(MotionSystem).GetField("port", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(motion, MotionPort);
            motion.enabled = true;
            _events.Clear();
            _game.Events += e => _events.Add(e);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_sender != null && !_sender.HasExited) _sender.Kill();
            _sender?.Dispose();
            _sender = null;
            if (MotionSystem.Instance != null) Object.Destroy(MotionSystem.Instance.gameObject);
            if (_game != null) Object.Destroy(_game.gameObject);
            yield return null;
            CameraFeed.VideoPort = 5006;
            CameraFeed.ControlPort = 5007;
            PlayerPrefs.DeleteKey("arcade.progress");
            PlayerPrefs.Save();
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator Hall_TheBooths_ThePrizeShelf_TheRows()
        {
            yield return new WaitForSecondsRealtime(1.5f);
            Assert.AreEqual(ArcadeGame.Phase.Hall, _game.Current);
            Assert.Greater(_game.Art.Count, 90, "the sprite sheet loaded");
            yield return Screenshot("arcade_hall.png");
            // a few tickets on the shelf and another booth chosen
            _game.Progress.Tickets = 75;
            _game.Progress.Best[0] = 2100;
            _game.Progress.Best[3] = 500;
            _game.TwoPlayers = true;
            _game.PartyMode = true;
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Screenshot("arcade_hall_party.png");
        }

        [UnityTest, Timeout(150000)]
        public IEnumerator Whack_TheHandsBonkTheMoles()
        {
            yield return PlayBooth(Machine.Whack, "arcade_whack", 30f, new[]
            {
                ("arcade_howto.png", 2f, ArcadeGame.Phase.HowTo), ("arcade_countdown.png", 1.5f, ArcadeGame.Phase.Countdown),
                ("arcade_whack.png", 14f, ArcadeGame.Phase.Playing), ("arcade_whack_late.png", 30f, ArcadeGame.Phase.Playing),
            });
            int hits = _events.Count(e => e.Kind is EventKind.Hit or EventKind.Gold);
            Log($"Godit!: {hits} moles bonked, {_events.Count(e => e.Kind == EventKind.Bad)} hedgehogs, {_events.Count(e => e.Kind == EventKind.Escape)} escaped");
            Assert.GreaterOrEqual(hits, 5, "the pecking hands bonked moles");
            yield return Screenshot("arcade_results.png", 2.5f);
        }

        [UnityTest, Timeout(150000)]
        public IEnumerator Slice_SwipesSliceTheFruit()
        {
            yield return PlayBooth(Machine.Slice, "arcade_slice", 30f, new[] { ("arcade_slice.png", 16f, ArcadeGame.Phase.Playing) });
            int sliced = _events.Count(e => e.Kind == EventKind.Slice);
            Log($"Fruta!: {sliced} sliced, {_events.Count(e => e.Kind == EventKind.Boom)} bombs, {_events.Count(e => e.Kind == EventKind.Combo)} combos");
            Assert.GreaterOrEqual(sliced, 5, "the swipes sliced fruit");
        }

        [UnityTest, Timeout(150000)]
        public IEnumerator Wall_AHeldY_PassesTheYWalls()
        {
            var y = Array.Find(WallRules.Poses, p => p.Id == "y");
            yield return PlayBooth(Machine.Wall, "arcade_wall", 30f, new[]
            {
                ("arcade_wall_far.png", 9.6f, ArcadeGame.Phase.Playing), ("arcade_wall_near.png", 12.2f, ArcadeGame.Phase.Playing),
            }, rules => ((WallRules)rules).Choose = () => y);
            var board = ((WallRules)_game.Rules).Boards[0];
            Log($"Muri!: {board.Passed} passed ({board.Perfects} perfect), {board.Failed} failed");
            Assert.GreaterOrEqual(board.Passed, 4, "the held Y passed the Y walls");
            Assert.AreEqual(0, board.Failed, "and none failed");
        }

        [UnityTest, Timeout(150000)]
        public IEnumerator Catch_StepsCatchTheSweets()
        {
            yield return PlayBooth(Machine.Catch, "arcade_catch", 30f, new[] { ("arcade_catch.png", 16f, ArcadeGame.Phase.Playing) });
            int caught = _events.Count(e => e.Kind == EventKind.Catch);
            Log($"Kap!: {caught} caught, {_events.Count(e => e.Kind == EventKind.Drop)} dropped, {_events.Count(e => e.Kind == EventKind.Boom)} bombs");
            Assert.GreaterOrEqual(caught, 4, "stepping side to side caught sweets");
        }

        [UnityTest, Timeout(150000)]
        public IEnumerator Flap_FlappingFliesTheBird()
        {
            float highest = 0f;
            yield return PlayBooth(Machine.Flap, "arcade_flap", 30f, new[] { ("arcade_flap.png", 16f, ArcadeGame.Phase.Playing) }, each: rules =>
            {
                highest = Mathf.Max(highest, ((FlapRules)rules).Birds[0].Y);
            });
            var bird = ((FlapRules)_game.Rules).Birds[0];
            Log($"Zogu!: {bird.Flaps} flaps, {bird.Stars} stars, {bird.Balloons} balloons, {bird.Rings} rings, {bird.Bumps} clouds; highest {highest:0.00}");
            Assert.GreaterOrEqual(bird.Flaps, 20, "the flapping arms flapped");
            Assert.Greater(highest, 0.5f, "and the bird flew up");
        }

        [UnityTest, Timeout(150000)]
        public IEnumerator KeepUp_TheHandsKeepTheBallUp()
        {
            yield return PlayBooth(Machine.KeepUp, "arcade_keepup", 30f, new[] { ("arcade_keepup.png", 12f, ArcadeGame.Phase.Playing) });
            int touches = _events.Count(e => e.Kind == EventKind.Touch);
            Log($"Topi!: {touches} touches, {_events.Count(e => e.Kind == EventKind.Drop)} drops");
            Assert.GreaterOrEqual(touches, 3, "the ball bounced off the raised hands and the head");
        }

        [UnityTest, Timeout(150000)]
        public IEnumerator TwoPlayers_EachBonksTheirOwnMoles()
        {
            Start("arcade_duo", alternate: true);
            yield return WaitFor(() => _game.Slots[0].Calibrated && _game.Slots[1].Calibrated, 15f, "both players calibrated");
            _game.SkipHowTo = true;
            _game.StartRound(Machine.Whack, 2);
            yield return WaitFor(() => _game.Current == ArcadeGame.Phase.Playing, 6f, "the round started");
            yield return new WaitForSecondsRealtime(14f);
            yield return Screenshot("arcade_two.png");
            yield return new WaitForSecondsRealtime(8f);
            var scores = _game.Rules.Score;
            Log($"two players: {scores[0]} / {scores[1]} points");
            Assert.AreEqual(2, _game.Rules.Players);
            Assert.Greater(scores[0], 0, "player 1 bonked moles");
            Assert.Greater(scores[1], 0, "player 2 bonked moles");
            _game.FinishRound();
            yield return Screenshot("arcade_two_results.png", 2f);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Party_FiveRounds_ThenTheTotal()
        {
            _game.PartyMode = true;
            _game.SkipHowTo = true;
            _game.Continue();
            Assert.IsNotNull(_game.Party);
            var played = new List<Machine>();
            for (int round = 0; round < ArcadeParty.Rounds; round++)
            {
                yield return WaitFor(() => _game.Current == ArcadeGame.Phase.Playing, 8f, $"round {round + 1} playing");
                played.Add(_game.Playing);
                yield return new WaitForSecondsRealtime(1.5f);
                if (round == 2) yield return Screenshot($"arcade_party_{_game.Playing.ToString().ToLowerInvariant()}.png");
                _game.FinishRound();
                Assert.AreEqual(ArcadeGame.Phase.RoundEnd, _game.Current);
                yield return new WaitForSecondsRealtime(0.5f);
                _game.Continue();
            }
            Assert.AreEqual(ArcadeGame.Phase.PartyEnd, _game.Current);
            Assert.AreEqual(5, played.Distinct().Count(), "five different booths");
            yield return Screenshot("arcade_party_end.png", 2.5f);
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>The script plays booth <paramref name="m"/>: started once the player is calibrated, played
        /// <paramref name="seconds"/>; screenshots at given times (since the round started) in given phases.</summary>
        IEnumerator PlayBooth(Machine m, string script, float seconds, (string file, float at, ArcadeGame.Phase phase)[] shots,
                              Action<MachineRules> setup = null, Action<MachineRules> each = null)
        {
            Start(script);
            yield return WaitFor(() => MotionSystem.Instance.ActiveBody != null && MotionSystem.Instance.ActiveBody.IsTracking, 10f, "the player");
            yield return new WaitForSecondsRealtime(2.2f);
            _game.StartRound(m);
            setup?.Invoke(_game.Rules);
            var pending = new List<(string file, float at, ArcadeGame.Phase phase)>(shots);
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < seconds + 10f)
            {
                float t = Time.realtimeSinceStartup - start;
                for (int i = pending.Count - 1; i >= 0; i--)
                    if (t >= pending[i].at && _game.Current == pending[i].phase)
                    {
                        var file = pending[i].file;
                        pending.RemoveAt(i);
                        yield return Screenshot(file);
                    }
                if (_game.Current == ArcadeGame.Phase.Playing) each?.Invoke(_game.Rules);
                if (_game.Current == ArcadeGame.Phase.Playing && _game.Rules.Time >= seconds) break;
                yield return null;
            }
            Assert.AreEqual(ArcadeGame.Phase.Playing, _game.Current, $"{m} was playing");
            _game.FinishRound();
            Assert.AreEqual(ArcadeGame.Phase.RoundEnd, _game.Current);
        }

        void Start(string script, bool alternate = false)
        {
            string serviceDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../motion-service-py"));
            string python = Path.Combine(serviceDir, ".venv/Scripts/python.exe");
            Assume.That(File.Exists(python), $"Python venv not found at {python}");
            _sender = Process.Start(new ProcessStartInfo(python,
                $"fake_sender.py --script {script} --port {MotionPort} --video-control-port {ControlPort}{(alternate ? " --alternate" : "")} --once")
            {
                WorkingDirectory = serviceDir, UseShellExecute = false, CreateNoWindow = true,
            });
        }

        static IEnumerator WaitFor(Func<bool> condition, float seconds, string what)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(condition(), $"timed out waiting for {what}");
        }

        static void Log(string text) => UnityEngine.Debug.Log("[Test] " + text);

        static IEnumerator Screenshot(string name, float after = 0f)
        {
            if (after > 0f) yield return new WaitForSecondsRealtime(after);
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../captures"));
            Directory.CreateDirectory(dir);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, name));
            yield return null;
            yield return null;
        }
    }
}
