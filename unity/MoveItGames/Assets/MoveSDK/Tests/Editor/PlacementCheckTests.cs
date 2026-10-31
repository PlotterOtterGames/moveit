using NUnit.Framework;
using UnityEngine;

namespace MoveIt.Motion.Tests
{
    /// <summary>
    /// The placement rule on players seen by a level pinhole camera 1 m high: the box's ~100° camera (60° vertical) and the
    /// laptop's ~75° one (41° vertical). The player is fake_sender's standing 1.70 m body, scaled for a child.
    /// </summary>
    public sealed class PlacementCheckTests
    {
        const float Aspect = 16f / 9f;
        const float BoxFov = 60f, LaptopFov = 41f;

        static readonly PlayArea Runner = new PlayArea(BodyPart.Knees, side: 0.8f, above: 0.3f);
        static readonly PlayArea Tennis = new PlayArea(BodyPart.Knees, side: 1f, above: 0.1f, grow: 0.2f);

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

        /// <summary>A player <paramref name="distance"/> m from the camera, <paramref name="sideways"/> m to the screen's right.</summary>
        sealed class Seen
        {
            public readonly Vector3[] Joints = new Vector3[Skeleton.JointCount];
            public readonly float[] Visibility = new float[Skeleton.JointCount];

            public Seen(float distance, float height = 1.70f, float sideways = 0f, float fov = BoxFov)
            {
                float picture = 2f * distance * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);   // metres seen from top to bottom
                float k = height / 1.70f;
                foreach (var (joint, x, y) in Stand)
                {
                    Joints[(int)joint] = new Vector3(Aspect * 0.5f + (x * k + sideways) / picture, 0.5f + (y * k - 1f) / picture, 0f);
                    Visibility[(int)joint] = 0.98f;
                }
            }

            public PlacementIssue Check(PlacementCheck check, PlayArea area, double time = 0) =>
                check.Update(Joints, Visibility, Aspect, area, time);

            public PlacementIssue Check(PlayArea area) => Check(new PlacementCheck(), area);
        }

        [Test]
        public void AtTheRightDistance_AdultsAndChildrenCanPlay()
        {
            Assert.AreEqual(PlacementIssue.None, new Seen(2.2f).Check(Runner), "an adult 2.2 m from the box's camera");
            Assert.AreEqual(PlacementIssue.None, new Seen(2.2f).Check(Tennis), "with room to step to the net");
            Assert.AreEqual(PlacementIssue.None, new Seen(2.2f, height: 1.2f).Check(Runner), "a child");
            Assert.AreEqual(PlacementIssue.None, new Seen(3f, fov: LaptopFov).Check(Runner), "an adult 3 m from the laptop's camera");
            Assert.AreEqual(PlacementIssue.None, new Seen(1.2f).Check(null), "a game that needs nothing checks nothing");
        }

        [Test]
        public void TooClose_HeadOrKneesOutOfThePicture_StepBack()
        {
            var check = new PlacementCheck();
            Assert.AreEqual(PlacementIssue.StepBack, new Seen(1.2f).Check(check, Runner), "an adult 1.2 m away");
            Assert.AreEqual(PictureEdges.Top, check.Edges, "the head (and the jumps above it) don't fit");
            Assert.IsTrue(check.Blocks);

            Assert.AreEqual(PlacementIssue.StepBack, new Seen(1f, height: 1.2f).Check(check, Runner), "a child walked up to the TV");
            Assert.AreEqual(PictureEdges.Bottom, check.Edges, "the knees are out of the picture");

            Assert.AreEqual(PlacementIssue.StepBack, new Seen(2.2f, fov: LaptopFov).Check(Runner),
                "the laptop's narrower camera needs about 3 m (docs/hardware.md)");
        }

        [Test]
        public void KneesTheCameraDoesntSee_CountAsOutOfThePicture()
        {
            var seen = new Seen(2.2f);
            seen.Visibility[(int)Joint.LeftKnee] = seen.Visibility[(int)Joint.RightKnee] = 0.1f;
            Assert.AreEqual(PlacementIssue.StepBack, seen.Check(Runner));
            seen.Visibility[(int)Joint.LeftKnee] = 0.9f;
            Assert.AreEqual(PlacementIssue.None, seen.Check(Runner), "one knee seen is enough");
        }

        [Test]
        public void ABodyThatGrowsAtTheNet_MustFitBigger()
        {
            var plain = new PlayArea(BodyPart.Knees);
            var grows = new PlayArea(BodyPart.Knees, grow: 0.2f);
            Assert.AreEqual(PlacementIssue.None, new Seen(1.45f).Check(plain), "the body fits as it stands");
            Assert.AreEqual(PlacementIssue.StepBack, new Seen(1.45f).Check(grows), "but not 20 % bigger");
        }

        [Test]
        public void NearASide_MoveTheOtherWay()
        {
            Assert.AreEqual(PlacementIssue.MoveRight, new Seen(2.2f, sideways: -1.65f).Check(Runner),
                "near the screen's left: no room for a lane step that way");
            Assert.AreEqual(PlacementIssue.MoveLeft, new Seen(2.2f, sideways: 1.65f).Check(Runner));
            Assert.AreEqual(PlacementIssue.None, new Seen(2.2f, sideways: -1.65f).Check(new PlayArea(BodyPart.Hips, side: 0.3f)),
                "a game played on the spot is fine there");
        }

        [Test]
        public void OnTheBorder_TheMessageDoesntFlicker()
        {
            var check = new PlacementCheck();
            Assert.AreEqual(PlacementIssue.MoveRight, new Seen(2.2f, sideways: -1.65f).Check(check, Runner, 1.0));
            Assert.AreEqual(1.0, check.IssueSince);
            Assert.AreEqual(PlacementIssue.MoveRight, new Seen(2.2f, sideways: -1.57f).Check(check, Runner, 1.1),
                "a few centimetres back toward the middle isn't enough to clear it");
            Assert.AreEqual(1.0, check.IssueSince, "the same issue keeps its start");
            Assert.AreEqual(PlacementIssue.None, new Seen(2.2f, sideways: -1.45f).Check(check, Runner, 1.2), "a real step clears it");
            Assert.AreEqual(PlacementIssue.None, new Seen(2.2f, sideways: -1.57f).Check(check, Runner, 1.3),
                "and the same spot isn't flagged again on the way out");
            Assert.AreEqual(PlacementIssue.None, new Seen(2.2f, sideways: -1.57f).Check(Runner), "(it wouldn't be from scratch)");
        }

        [Test]
        public void FarAway_ComeCloser_IsOnlyAdvice()
        {
            var check = new PlacementCheck();
            Assert.AreEqual(PlacementIssue.ComeCloser, new Seen(4f, height: 1.2f).Check(check, Runner), "a child 4 m away");
            Assert.IsFalse(check.Blocks, "the game can still be played");
            Assert.AreEqual(PlacementIssue.None, new Seen(4f).Check(Runner), "an adult is big enough there");
        }

        [Test]
        public void DuringPlay_TheWarningComesBeforeTheBodyLeaves()
        {
            var inPlay = Runner.InPlay;
            Assert.AreEqual(PlacementIssue.None, new Seen(2.2f, sideways: -1.65f).Check(inPlay),
                "standing where a round couldn't start is fine in the middle of one (the player stepped there)");
            var check = new PlacementCheck();
            Assert.AreEqual(PlacementIssue.MoveRight, new Seen(2.2f, sideways: -1.9f).Check(check, inPlay),
                "the shoulder ~17 cm from the edge: move back");
            Assert.AreEqual(PictureEdges.Left, check.Edges);
            Assert.AreEqual(PlacementIssue.StepBack, new Seen(1.2f).Check(inPlay), "the head out of the picture");
        }

        [Test]
        public void WithoutTheFace_TheHeadIsEstimatedFromTheShoulders()
        {
            var seen = new Seen(1.2f);
            seen.Visibility[(int)Joint.Nose] = 0f;   // turned away, or the face above the picture
            Assert.AreEqual(PlacementIssue.StepBack, seen.Check(Runner));
            seen = new Seen(2.2f);
            seen.Visibility[(int)Joint.Nose] = 0f;
            Assert.AreEqual(PlacementIssue.None, seen.Check(Runner));
        }
    }
}
