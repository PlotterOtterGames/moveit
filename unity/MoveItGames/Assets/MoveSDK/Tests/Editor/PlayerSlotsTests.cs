using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MoveIt.Motion.Tests
{
    /// <summary>
    /// Two players in front of one camera (<see cref="PlayerSlots"/>, and <see cref="BodyTracker"/> under it): player 1 is
    /// whoever stands on the left, each one calibrates, steps and raises their hands on their own, numbers stay put during
    /// a match, a newcomer takes a free number, and the box's alternating packets (one person each, with track ids)
    /// never swap two people standing close together.
    /// </summary>
    public sealed class PlayerSlotsTests
    {
        const float Aspect = 16f / 9f, PictureMetres = 2.6f;

        // fake_sender's standing 1.70 m body (metres; x = the player's right on the screen)
        static readonly (Joint joint, float x, float y)[] Stand =
        {
            (Joint.Nose, 0f, 1.60f), (Joint.LeftEye, -0.03f, 1.64f), (Joint.RightEye, 0.03f, 1.64f),
            (Joint.LeftEar, -0.07f, 1.62f), (Joint.RightEar, 0.07f, 1.62f),
            (Joint.LeftShoulder, -0.19f, 1.42f), (Joint.RightShoulder, 0.19f, 1.42f),
            (Joint.LeftElbow, -0.24f, 1.12f), (Joint.RightElbow, 0.24f, 1.12f),
            (Joint.LeftWrist, -0.26f, 0.85f), (Joint.RightWrist, 0.26f, 0.85f),
            (Joint.LeftHip, -0.11f, 0.95f), (Joint.RightHip, 0.11f, 0.95f),
            (Joint.LeftKnee, -0.12f, 0.52f), (Joint.RightKnee, 0.12f, 0.52f),
            (Joint.LeftAnkle, -0.12f, 0.08f), (Joint.RightAnkle, 0.12f, 0.08f),
        };

        /// <summary>A person for a packet: standing <paramref name="x"/> m to the screen's right of the middle, hands up
        /// or not, with the service's track id.</summary>
        static PersonPacket Person(float x, bool handsUp = false, int tid = 0)
        {
            var k = new float[Skeleton.JointCount * 4];
            foreach (var (joint, jx, jy) in Stand)
            {
                float px = jx, py = jy;
                if (handsUp && (joint == Joint.LeftWrist || joint == Joint.RightWrist)) py = 1.95f;
                if (handsUp && (joint == Joint.LeftElbow || joint == Joint.RightElbow)) py = 1.66f;
                // view space: mirrored, y up; the packet carries raw camera space (x from the camera's left, y down)
                float viewX = Aspect * 0.5f + (x + px) / PictureMetres, viewY = 0.05f + py / PictureMetres;
                int o = (int)joint * 4;
                k[o] = 1f - viewX / Aspect;
                k[o + 1] = 1f - viewY;
                k[o + 2] = 0f;
                k[o + 3] = 0.98f;
            }
            return new PersonPacket { k = k, score = 0.98f, tid = tid };
        }

        /// <summary>A tracker fed one packet per call, 30 a second, on a clock of its own (it starts at the real clock, so
        /// <see cref="Body.IsTracking"/> holds).</summary>
        sealed class Camera
        {
            public readonly BodyTracker Tracker = new BodyTracker();
            public readonly PlayerSlots Slots = new PlayerSlots();
            long _t = 1_800_000_000_000;
            float _now = Time.unscaledTime;

            public void Frame(params PersonPacket[] people)
            {
                _t += 33;
                _now += 1f / 30f;
                Tracker.Update(new MotionPacket { v = 1, t = _t, w = 1280, h = 720, people = people }, _now, _now);
                Slots.Update(Tracker.Bodies);
            }

            public void Frames(int n, System.Func<int, PersonPacket[]> people)
            {
                for (int i = 0; i < n; i++) Frame(people(i));
            }
        }

        [Test]
        public void Player1_IsWhoeverStandsOnTheLeft()
        {
            var cam = new Camera();
            cam.Frames(30, i => new[] { Person(0.6f, tid: 1), Person(-0.6f, tid: 2) });   // the right one first in the packet
            Assert.AreEqual(2, cam.Slots.Present);
            Assert.Less(cam.Slots[0].Body.TorsoCenter.x, cam.Slots[1].Body.TorsoCenter.x, "player 1 on the left");
            // they cross over on a waiting screen (the service's track ids keep who is who): the numbers follow where they
            // stand, each keeping their own calibration
            Assert.IsTrue(cam.Slots[0].Calibrated && cam.Slots[1].Calibrated);
            var left = cam.Slots[0].Body;
            cam.Frames(60, i => { float k = Mathf.Min(1f, i / 40f); return new[] { Person(0.6f - 1.2f * k, tid: 1), Person(-0.6f + 1.2f * k, tid: 2) }; });
            Assert.AreNotSame(left, cam.Slots[0].Body, "the one now on the left is player 1");
            Assert.AreSame(left, cam.Slots[1].Body);
            Assert.IsTrue(cam.Slots[0].Calibrated && cam.Slots[1].Calibrated, "nobody had to calibrate again");
        }

        [Test]
        public void DuringAMatch_PlayerOneIsTheLeftSide_WhateverTheTrackerDoes()
        {
            // the service's two tracks change people while the two stand close together (its crops held both): the
            // bodies follow the tracks, so they end up the wrong way round; the slots put them back on their sides and
            // each side keeps where its player stood
            var cam = new Camera();
            cam.Frames(30, i => new[] { Person(-0.6f, tid: 1), Person(0.6f, tid: 2) });
            float leftSpot = cam.Slots[0].BaselineX, rightSpot = cam.Slots[1].BaselineX;
            Assert.Less(leftSpot, rightSpot);
            cam.Slots.Locked = true;
            cam.Frames(20, i => new[] { Person(-0.6f, tid: 2), Person(0.6f, tid: 1) });   // the tracks swapped people
            Assert.Less(cam.Slots[0].Body.TorsoCenter.x, cam.Slots[1].Body.TorsoCenter.x, "player 1's slot holds the left body again");
            Assert.AreEqual(1, cam.Slots.SideSwaps);
            Assert.AreEqual(leftSpot, cam.Slots[0].BaselineX, 1e-4f, "the left side keeps its spot");
            Assert.IsTrue(cam.Slots[0].Calibrated && cam.Slots[1].Calibrated, "nobody calibrates again");
            Assert.AreEqual(0f, cam.Slots[0].LateralOffset, 0.05f, "player 1 hasn't moved");
            Assert.AreEqual(0f, cam.Slots[1].LateralOffset, 0.05f, "player 2 hasn't moved");
        }

        [Test]
        public void DuringAMatch_StandingLevel_DoesntSwapTheSides()
        {
            var cam = new Camera();
            cam.Frames(30, i => new[] { Person(-0.6f, tid: 1), Person(0.6f, tid: 2) });
            cam.Slots.Locked = true;
            // they step together until level and sway past each other a little (less than the margin)
            cam.Frames(60, i => new[] { Person(-0.6f + 0.62f * Mathf.Min(1f, i / 30f), tid: 1), Person(0.6f - 0.62f * Mathf.Min(1f, i / 30f), tid: 2) });
            Assert.AreEqual(0, cam.Slots.SideSwaps, "a few centimetres past each other isn't the wrong way round");
        }

        [Test]
        public void DuringAMatch_APlayerFoundAgain_KeepsTheirSidesCalibration()
        {
            var cam = new Camera();
            cam.Frames(30, i => new[] { Person(-0.7f, tid: 1), Person(0.7f, tid: 2) });
            float rightSpot = cam.Slots[1].BaselineX;
            cam.Slots.Locked = true;
            cam.Frames(60, i => new[] { Person(-0.7f, tid: 1) });   // player 2 is lost for 2 s (the tracker lets go after 1.5)
            Assert.IsNull(cam.Slots[1].Body);
            Assert.IsTrue(cam.Slots[1].Calibrated, "the side keeps its calibration");
            cam.Frames(5, i => new[] { Person(-0.7f, tid: 1), Person(0.75f, tid: 3) });   // found again, a new track
            Assert.IsNotNull(cam.Slots[1].Body);
            Assert.AreEqual(rightSpot, cam.Slots[1].BaselineX, 1e-4f);
            Assert.AreEqual(0.05f / 0.47f, cam.Slots[1].LateralOffset, 0.06f, "steps count from where they stood before");
        }

        [Test]
        public void EachPlayer_CalibratesStepsAndRaisesHands_OnTheirOwn()
        {
            var cam = new Camera();
            cam.Frames(30, i => new[] { Person(-0.7f), Person(0.7f) });
            Assert.IsTrue(cam.Slots[0].Calibrated && cam.Slots[1].Calibrated, "both stood still");
            // player 1 steps 0.25 m to the screen's right (half a torso: shoulders 1.42, hips 0.95), player 2 stays
            cam.Frames(30, i => new[] { Person(-0.7f + 0.25f * Mathf.Min(1f, i / 10f)), Person(0.7f) });
            Assert.AreEqual(0.25f / 0.47f, cam.Slots[0].LateralOffset, 0.08f, "player 1's step, in torso lengths");
            Assert.AreEqual(0f, cam.Slots[1].LateralOffset, 0.05f, "player 2 didn't move");
            // player 2 raises both hands: only they are ready
            cam.Frames(20, i => new[] { Person(-0.45f), Person(0.7f, handsUp: true) });
            Assert.IsFalse(cam.Slots[0].Ready);
            Assert.IsTrue(cam.Slots[1].Ready);
            cam.Slots.ClearReady();
            Assert.IsFalse(cam.Slots[1].Ready);
        }

        [Test]
        public void ShoulderToShoulder_IsTooClose()
        {
            var cam = new Camera();
            cam.Frames(20, i => new[] { Person(-0.25f), Person(0.25f) });
            Assert.AreEqual(2, cam.Slots.Present);
            Assert.IsTrue(cam.Slots.TooClose, "0.5 m apart: they'd bump into each other");
            Assert.IsFalse(cam.Slots.BothCanPlay);
            cam.Frames(40, i => new[] { Person(-0.8f), Person(0.8f) });
            Assert.IsFalse(cam.Slots.TooClose, "1.6 m apart");
            Assert.IsTrue(cam.Slots.BothCanPlay);
        }

        [Test]
        public void ANewcomer_TakesTheFreeNumber()
        {
            var cam = new Camera();
            cam.Frames(30, i => new[] { Person(-0.7f), Person(0.7f) });
            cam.Slots.Locked = true;
            var p2 = cam.Slots[1].Body;
            cam.Frames(60, i => new[] { Person(-0.7f) });   // player 2 leaves for 2 s: the tracker lets them go after 1.5 s
            Assert.IsNull(cam.Slots[1].Body, "player 2's number is free");
            Assert.IsNotNull(cam.Slots[0].Body, "player 1 stays");
            cam.Frames(10, i => new[] { Person(-0.7f), Person(0.1f) });   // someone steps in, nearer the middle
            Assert.IsNotNull(cam.Slots[1].Body, "the newcomer is player 2");
            Assert.AreNotSame(p2, cam.Slots[1].Body);
        }

        [Test]
        public void AlternatingPackets_WithTrackIds_NeverSwapTwoPeopleStandingClose()
        {
            // the box's NPU with a shared budget: each packet carries one of the two (15 a second each); they stand 0.5 m
            // apart, then one sways toward the other: matching by position alone could hand one's sample to the other
            var cam = new Camera();
            var ids = new HashSet<int>();
            for (int i = 0; i < 120; i++)
            {
                float sway = 0.15f * Mathf.Sin(i * 0.2f);
                cam.Frame(i % 2 == 0 ? Person(-0.25f + sway, tid: 1) : Person(0.25f, tid: 2));
                foreach (var b in cam.Tracker.Bodies) ids.Add(b.Id);
            }
            Assert.AreEqual(2, cam.Tracker.Bodies.Count, "two bodies");
            Assert.AreEqual(2, ids.Count, "never a third: nobody was mistaken for someone new");
            foreach (var b in cam.Tracker.Bodies)
                Assert.AreEqual(b.ServiceTrack == 1 ? -0.25f : 0.25f, (b.TorsoCenter.x - Aspect * 0.5f) * PictureMetres, 0.2f,
                                $"track {b.ServiceTrack} stayed where that person is");
        }
    }
}
