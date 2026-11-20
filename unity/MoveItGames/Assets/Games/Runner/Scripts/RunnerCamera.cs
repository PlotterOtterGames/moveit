using UnityEngine;

namespace MoveIt.Games.Runner
{
    /// <summary>
    /// Vrapo!'s camera (the console polish of 2026-11-18), shots instead of one fixed view: the title looks at Dino at his
    /// camp from just behind and beside him (he has turned to the camera; the valley and the volcano behind him), the
    /// logo's side of the picture left free; the countdown rises and pulls back to the runner's view as he turns to the
    /// path (6 m behind, 3.25 m up, about 13 degrees down); the run follows him across the lanes, rises with his jumps,
    /// rolls a little into lane changes and widens with speed; a crash circles round in front of him, a little high, to see
    /// his dizzy face. Shake on hits and landings.
    /// A shot is an orbit round Dino (yaw 0 = behind him, 180 = in front), looking at a point in the world given from his
    /// feet (not turned with the orbit), shifted sideways in the picture (Side: + puts him right of the middle).
    /// </summary>
    public sealed class RunnerCamera
    {
        public enum Shot { Title, Intro, Run, Crash }

        struct Pose
        {
            public float Yaw, Distance, Height, Fov, Side;
            public Vector3 Look;     // the point looked at, from Dino's feet (world axes)

            public static Pose Lerp(Pose a, Pose b, float t) => new Pose
            {
                Yaw = Mathf.LerpAngle(a.Yaw, b.Yaw, t), Distance = Mathf.Lerp(a.Distance, b.Distance, t),
                Height = Mathf.Lerp(a.Height, b.Height, t), Fov = Mathf.Lerp(a.Fov, b.Fov, t), Side = Mathf.Lerp(a.Side, b.Side, t),
                Look = Vector3.Lerp(a.Look, b.Look, t),
            };
        }

        public const float RunBack = 6f, RunUp = 3.25f, RunPitch = 13f;

        // the run's view: behind and above, looking at a point 10 m on
        static readonly Pose RunPose = new Pose
        {
            Yaw = 0f, Distance = RunBack, Height = RunUp, Fov = 60f,
            Look = new Vector3(0f, RunUp - 10f * Mathf.Sin(RunPitch * Mathf.Deg2Rad), -RunBack + 10f * Mathf.Cos(RunPitch * Mathf.Deg2Rad)),
        };

        readonly Camera _cam;
        readonly Transform _rig;
        Shot _shot = Shot.Title;
        Pose _from, _now;
        float _blend = 1f, _blendTime = 1f, _shotTime;
        float _shake;
        float _roll;
        Vector3 _follow;

        public RunnerCamera(Camera cam)
        {
            _cam = cam;
            _rig = cam != null ? cam.transform : null;
            _now = TitlePose(0f);
            _from = _now;
            if (_cam != null) _cam.farClipPlane = 620f;
        }

        public Shot Current => _shot;

        public void Cut(Shot shot, float blendSeconds)
        {
            _from = _now;
            _shot = shot;
            _shotTime = 0f;
            _blend = blendSeconds <= 0f ? 1f : 0f;
            _blendTime = Mathf.Max(0.01f, blendSeconds);
        }

        public void Shake(float amount) => _shake = Mathf.Max(_shake, amount);

        // the title: just behind Dino and to his right, low, looking a little past him so he stands right of the middle
        static Pose TitlePose(float t) => new Pose
        {
            Yaw = -14f + 6f * Mathf.Sin(t * 0.21f), Distance = 4.7f, Height = 1.45f, Fov = 40f, Side = 1.25f,
            Look = new Vector3(0f, 1.2f, 0f),
        };

        // the crash: in front of him and to the side, a little high (over a bridge or a neck he ran into), him on the right
        static readonly Pose CrashPose = new Pose { Yaw = 132f, Distance = 5.4f, Height = 3.1f, Fov = 42f, Side = 1.0f, Look = new Vector3(0f, 0.7f, 0f) };

        /// <summary>dino: Dino's feet (x, height); speedFraction 0..1 of the run's range; lean: lane change still to go (m).</summary>
        public void Tick(float dt, Vector3 dino, float speedFraction, float lean)
        {
            if (_cam == null) return;
            _shotTime += dt;
            Pose target;
            switch (_shot)
            {
                case Shot.Title:
                    target = TitlePose(Time.time);
                    break;
                case Shot.Crash:
                    target = CrashPose;
                    target.Yaw += 6f * Mathf.Sin(_shotTime * 0.35f);
                    break;
                default:
                    target = RunPose;
                    if (_shot == Shot.Run) target.Fov += 9f * speedFraction;
                    break;
            }
            _blend = Mathf.Min(1f, _blend + dt / _blendTime);
            var pose = Pose.Lerp(_from, target, Mathf.SmoothStep(0f, 1f, _blend));
            _now = pose;
            // following: the run keeps half of Dino's sideways moves (the lanes stay framed) and a third of his jumps
            var follow = _shot == Shot.Run || _shot == Shot.Intro
                ? new Vector3(dino.x * 0.5f, dino.y * 0.3f, 0f)
                : new Vector3(dino.x, 0f, 0f);
            _follow = Vector3.Lerp(_follow, follow, 1f - Mathf.Exp(-6f * dt));
            float yaw = pose.Yaw * Mathf.Deg2Rad;
            var pos = _follow + new Vector3(Mathf.Sin(yaw) * pose.Distance, pose.Height, -Mathf.Cos(yaw) * pose.Distance);
            var look = _follow + pose.Look;
            // sideways in the picture: the looked-at point moves against the camera's right, so Dino sits off the middle
            var right = Vector3.Cross(Vector3.up, (look - pos).normalized).normalized;
            look -= right * pose.Side;
            _roll = Mathf.Lerp(_roll, _shot == Shot.Run ? Mathf.Clamp(-lean * 0.9f, -2.2f, 2.2f) : 0f, 1f - Mathf.Exp(-5f * dt));
            _shake = Mathf.MoveTowards(_shake, 0f, dt * 1.6f);
            pos += Random.insideUnitSphere * (_shake * _shake * 0.32f);
            _rig.position = pos;
            _rig.rotation = Quaternion.LookRotation(look - pos, Vector3.up) * Quaternion.Euler(0f, 0f, _roll);
            _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, pose.Fov, 1f - Mathf.Exp(-(_shot == Shot.Run ? 2.5f : 8f) * dt));
        }
    }
}
