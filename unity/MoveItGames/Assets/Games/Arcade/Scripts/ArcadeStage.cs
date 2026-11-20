using System.Collections.Generic;
using MoveIt.Motion;
using UnityEngine;
using Joint = MoveIt.Motion.Joint;

namespace MoveIt.Games.Arcade
{
    /// <summary>
    /// Where the booth is on the screen and where each player's body is in it. The camera picture is framed on the
    /// player(s) (<see cref="Crop"/>, in the camera's view units) and held still while a booth plays, so the moles, the
    /// basket's strip and the wall stay where they were put. Each player's stage is fixed when the countdown starts: its
    /// origin where their hips were, its unit their torso length then (<see cref="Lock"/>). Their parts are followed on
    /// the camera's clock (a <see cref="HandInterpolator"/> each, on the raw samples, like Vishu!'s cursors: ~75 ms from
    /// the picture), in HUD pixels through the framing, and handed to the rules in stage units
    /// (<see cref="Bodies"/>). The booths' rules filter the pose model's glitches themselves.
    /// </summary>
    public sealed class ArcadeStage
    {
        public const float HudHeight = 1080f;

        public Rect Crop { get; private set; } = new Rect(0f, 0f, 16f / 9f, 1f);
        public float GuiWidth = 1920f;
        public readonly Vector2[] Origin = { new Vector2(0.89f, 0.35f), new Vector2(0.89f, 0.35f) };
        public readonly float[] Torso = { 0.2f, 0.2f };
        public readonly StageBody[] Bodies = { new StageBody(), new StageBody() };
        readonly PartTrack[] _tracks = { new PartTrack(), new PartTrack() };
        bool _cropValid;

        sealed class PartTrack
        {
            public readonly HandInterpolator[] Parts = new HandInterpolator[StageBody.PartCount];
            public double LastSample;
            public Body Body;

            public PartTrack()
            {
                for (int i = 0; i < Parts.Length; i++)
                {
                    bool hand = i == (int)Part.LeftHand || i == (int)Part.RightHand;
                    Parts[i] = hand ? new HandInterpolator(1f, 4f, 2f) { Margin = 0.5f, MaxExtrapolation = 0.08f }
                                    : new HandInterpolator(1f, 2f, 1f) { Margin = 0.5f, MaxExtrapolation = 0.05f };
                }
            }

            public void Reset()
            {
                foreach (var p in Parts) p.Reset();
                LastSample = 0;
            }
        }

        public Vector2 ViewToWorld(Vector2 v) => new Vector2((v.x - Crop.xMin) / Crop.width * GuiWidth, (v.y - Crop.yMin) / Crop.height * HudHeight);
        public Vector2 WorldToView(Vector2 w) => new Vector2(Crop.xMin + w.x / GuiWidth * Crop.width, Crop.yMin + w.y / HudHeight * Crop.height);
        /// <summary>HUD pixels a torso length of player <paramref name="p"/> spans.</summary>
        public float UnitPx(int p) => Torso[p] / Crop.height * HudHeight;
        public Vector2 StageToWorld(int p, Vector2 s) => ViewToWorld(Origin[p] + s * Torso[p]);
        public Vector2 WorldToStage(int p, Vector2 w) => (WorldToView(w) - Origin[p]) / Torso[p];

        /// <summary>
        /// Fixes each player's stage where they stand now and frames the picture around the stages: from
        /// <paramref name="below"/> torso lengths below the hips to <paramref name="above"/> above, <paramref name="side"/>
        /// to each side. A player not in view gets a stage in the middle of the picture.
        /// </summary>
        public void Lock(IList<Body> bodies, int players, float imageAspect, float screenAspect, float above, float below, float side)
        {
            for (int p = 0; p < 2; p++)
            {
                var b = p < players && bodies != null && p < bodies.Count ? bodies[p] : null;
                if (b != null && b.IsTracking && b.TorsoLength > 0.02f)
                {
                    Origin[p] = b.HipCenter;
                    Torso[p] = b.TorsoLength;
                }
                else
                {
                    Torso[p] = 0.2f;
                    float x = players == 2 ? imageAspect * (p == 0 ? 0.33f : 0.67f) : imageAspect * 0.5f;
                    Origin[p] = new Vector2(x, 0.38f);
                }
            }
            float xMin = float.MaxValue, xMax = float.MinValue, yMin = float.MaxValue, yMax = float.MinValue;
            for (int p = 0; p < players; p++)
            {
                xMin = Mathf.Min(xMin, Origin[p].x - side * Torso[p]);
                xMax = Mathf.Max(xMax, Origin[p].x + side * Torso[p]);
                yMin = Mathf.Min(yMin, Origin[p].y - below * Torso[p]);
                yMax = Mathf.Max(yMax, Origin[p].y + above * Torso[p]);
            }
            float height = Mathf.Max(yMax - yMin, (xMax - xMin) / screenAspect);
            Crop = Fit((xMin + xMax) * 0.5f, (yMin + yMax) * 0.5f, height, imageAspect, screenAspect);
            _cropValid = true;
            for (int p = 0; p < 2; p++) _tracks[p].Reset();
        }

        /// <summary>Between booths: the picture follows the player(s) smoothly (from the knees to above the head).</summary>
        public void Follow(IList<Body> bodies, int players, float imageAspect, float screenAspect, float dt)
        {
            float xMin = float.MaxValue, xMax = float.MinValue, yMin = float.MaxValue, yMax = float.MinValue;
            int seen = 0;
            for (int p = 0; p < players && bodies != null && p < bodies.Count; p++)
            {
                var b = bodies[p];
                if (b == null || !b.IsTracking || !b.ShouldersVisible) continue;
                float t = b.TorsoLength;
                seen++;
                xMin = Mathf.Min(xMin, b.HipCenter.x - 1.8f * t);
                xMax = Mathf.Max(xMax, b.HipCenter.x + 1.8f * t);
                yMin = Mathf.Min(yMin, b.HipCenter.y - 1.1f * t);
                yMax = Mathf.Max(yMax, b.HipCenter.y + 2.3f * t);
            }
            var target = seen == 0
                ? Fit(imageAspect * 0.5f, 0.5f, 1f, imageAspect, screenAspect)
                : Fit((xMin + xMax) * 0.5f, (yMin + yMax) * 0.5f, Mathf.Max(yMax - yMin, (xMax - xMin) / screenAspect), imageAspect, screenAspect);
            if (!_cropValid)
            {
                Crop = target;
                _cropValid = true;
                return;
            }
            float k = 1f - Mathf.Exp(-dt / 0.6f);
            Crop = new Rect(Vector2.Lerp(Crop.position, target.position, k), Vector2.Lerp(Crop.size, target.size, k));
        }

        static Rect Fit(float cx, float cy, float height, float imageAspect, float screenAspect)
        {
            float h = Mathf.Min(height, 1f), w = h * screenAspect;
            if (w > imageAspect)
            {
                w = imageAspect;
                h = w / screenAspect;
            }
            return new Rect(Mathf.Clamp(cx - w * 0.5f, 0f, Mathf.Max(0f, imageAspect - w)), Mathf.Clamp(cy - h * 0.5f, 0f, Mathf.Max(0f, 1f - h)), w, h);
        }

        /// <summary>Player <paramref name="p"/>'s body this frame (null = nobody): their parts in stage units.</summary>
        public void Track(int p, Body body, double now, float dt)
        {
            var t = _tracks[p];
            var stage = Bodies[p];
            if (body != t.Body)
            {
                t.Reset();
                t.Body = body;
            }
            if (body == null || !body.IsTracking || body.TorsoLength < 0.02f)
            {
                stage.Present = false;
                return;
            }
            if (body.SampleTime != t.LastSample)
            {
                t.LastSample = body.SampleTime;
                var raw = body.Raw;
                float torso = body.FastTorsoLength;
                Sample(t, Part.Head, (Vector2)raw[(int)Joint.Nose] + new Vector2(0f, 0.1f * torso), body, now);
                Sample(t, Part.LeftShoulder, raw[(int)Joint.LeftShoulder], body, now);
                Sample(t, Part.RightShoulder, raw[(int)Joint.RightShoulder], body, now);
                Sample(t, Part.LeftElbow, raw[(int)Joint.LeftElbow], body, now);
                Sample(t, Part.RightElbow, raw[(int)Joint.RightElbow], body, now);
                // (Body.HandPoint: the wrist carried on along the arm from the steady shoulder; pushed along the forearm
                // until 2026-11-17, which brought in the elbow's errors: the hammer and the blade jumped a third more)
                Sample(t, Part.LeftHand, body.HandPoint(raw, Joint.LeftShoulder, Joint.LeftWrist), body, now);
                Sample(t, Part.RightHand, body.HandPoint(raw, Joint.RightShoulder, Joint.RightWrist), body, now);
                Sample(t, Part.LeftHip, raw[(int)Joint.LeftHip], body, now);
                Sample(t, Part.RightHip, raw[(int)Joint.RightHip], body, now);
                Sample(t, Part.LeftKnee, raw[(int)Joint.LeftKnee], body, now);
                Sample(t, Part.RightKnee, raw[(int)Joint.RightKnee], body, now);
                Sample(t, Part.LeftFoot, raw[(int)Joint.LeftAnkle], body, now);
                Sample(t, Part.RightFoot, raw[(int)Joint.RightAnkle], body, now);
            }
            var vis = body.Visibility;
            stage.Seen[(int)Part.Head] = vis[(int)Joint.Nose];
            stage.Seen[(int)Part.LeftShoulder] = vis[(int)Joint.LeftShoulder];
            stage.Seen[(int)Part.RightShoulder] = vis[(int)Joint.RightShoulder];
            stage.Seen[(int)Part.LeftElbow] = vis[(int)Joint.LeftElbow];
            stage.Seen[(int)Part.RightElbow] = vis[(int)Joint.RightElbow];
            stage.Seen[(int)Part.LeftHand] = vis[(int)Joint.LeftWrist];
            stage.Seen[(int)Part.RightHand] = vis[(int)Joint.RightWrist];
            stage.Seen[(int)Part.LeftHip] = vis[(int)Joint.LeftHip];
            stage.Seen[(int)Part.RightHip] = vis[(int)Joint.RightHip];
            stage.Seen[(int)Part.LeftKnee] = vis[(int)Joint.LeftKnee];
            stage.Seen[(int)Part.RightKnee] = vis[(int)Joint.RightKnee];
            stage.Seen[(int)Part.LeftFoot] = vis[(int)Joint.LeftAnkle];
            stage.Seen[(int)Part.RightFoot] = vis[(int)Joint.RightAnkle];
            bool any = false;
            for (int i = 0; i < StageBody.PartCount; i++)
            {
                var interp = t.Parts[i];
                if (!interp.HasSample)
                {
                    stage.Seen[i] = 0f;
                    continue;
                }
                any = true;
                stage.At[i] = WorldToStage(p, interp.Evaluate(now, dt, out _));
            }
            stage.Present = any;
        }

        void Sample(PartTrack t, Part part, Vector2 view, Body body, double now) =>
            t.Parts[(int)part].Sample(body.SampleTime, ViewToWorld(view), false, now);
    }

    /// <summary>
    /// Muri!'s hole as a distance field (R8: 0.5 on the edge, toward 1 inside the shape, toward 0 outside, over
    /// <see cref="Range"/> torso lengths either way), rastered on the CPU when a wall appears (128 x 128, ~2-4 ms on the
    /// box), from the pose's shape built of capsules on each player's own proportions in HUD pixels.
    /// </summary>
    public sealed class WallMask
    {
        public const float Range = 0.3f;

        public struct Capsule
        {
            public Vector2 A, B;
            public float Radius;
        }

        public Texture2D Texture { get; private set; }
        byte[] _data;
        int _w, _h;

        public void Draw(Rect wall, List<Capsule> shape, float unitPx, int width, int height)
        {
            if (Texture == null || _w != width || _h != height)
            {
                if (Texture != null) Object.Destroy(Texture);
                _w = width;
                _h = height;
                Texture = new Texture2D(width, height, TextureFormat.R8, false, true)
                {
                    name = "Wall hole", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
                };
                _data = new byte[width * height];
            }
            float scale = 1f / Mathf.Max(1f, unitPx) / (2f * Range);
            for (int y = 0; y < _h; y++)
            {
                float wy = wall.yMin + (y + 0.5f) / _h * wall.height;
                for (int x = 0; x < _w; x++)
                {
                    var pt = new Vector2(wall.xMin + (x + 0.5f) / _w * wall.width, wy);
                    float d = float.MaxValue;
                    for (int c = 0; c < shape.Count; c++)
                    {
                        var cap = shape[c];
                        float dist = Geo.SegmentDistance(pt, cap.A, cap.B) - cap.Radius;
                        if (dist < d) d = dist;
                    }
                    _data[y * _w + x] = (byte)(Mathf.Clamp01(0.5f - d * scale) * 255f);
                }
            }
            Texture.SetPixelData(_data, 0);
            Texture.Apply(false);
        }

        public void Dispose()
        {
            if (Texture != null) Object.Destroy(Texture);
            Texture = null;
        }

        /// <summary>
        /// The capsules of a person making <paramref name="pose"/> (stage units through <paramref name="toWorld"/>, HUD
        /// pixels out), roomy by <paramref name="pad"/> torso lengths: the head, the neck, a wide torso, the arms by the
        /// pose's angles, the legs straight down (or apart), the hips lower for a squat and at the pose's spot.
        /// </summary>
        public static void PoseShape(WallPose pose, System.Func<Vector2, Vector2> toWorld, float unitPx, List<Capsule> into, float pad = 0.1f)
        {
            float hipY = pose.Squat ? -0.42f : 0f;
            var hips = new Vector2(pose.Step, hipY);
            var shoulders = hips + new Vector2(0f, 1f);
            var head = shoulders + new Vector2(0f, 0.47f);
            void Add(Vector2 a, Vector2 b, float r) => into.Add(new Capsule { A = toWorld(a), B = toWorld(b), Radius = (r + pad) * unitPx });
            Add(head, head, 0.33f);
            Add(head, shoulders, 0.18f);
            Add(hips + new Vector2(-0.12f, 0.12f), shoulders + new Vector2(-0.12f, -0.08f), 0.32f);
            Add(hips + new Vector2(0.12f, 0.12f), shoulders + new Vector2(0.12f, -0.08f), 0.32f);
            for (int side = 0; side < 2; side++)
            {
                bool left = side == 0;
                var shoulder = shoulders + new Vector2(left ? -0.42f : 0.42f, 0f);
                var elbow = shoulder + Geo.Limb(left, left ? pose.LeftUpper : pose.RightUpper) * 0.6f;
                var hand = elbow + Geo.Limb(left, left ? pose.LeftFore : pose.RightFore) * 0.62f;
                Add(shoulder, elbow, 0.16f);
                Add(elbow, hand, 0.14f);
                Add(hand, hand, 0.18f);
                var hip = hips + new Vector2(left ? -0.2f : 0.2f, 0f);
                float thigh = left ? pose.LeftThigh : pose.RightThigh;
                var knee = hip + Geo.Limb(left, pose.Legs ? thigh : 6f) * 0.92f;
                var foot = knee + Geo.Limb(left, pose.Legs ? thigh : 4f) * (pose.Squat ? 0.55f : 0.92f);
                Add(hip, knee, 0.19f);
                Add(knee, foot, 0.17f);
            }
        }
    }
}
