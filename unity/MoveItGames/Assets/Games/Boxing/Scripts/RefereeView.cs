using UnityEngine;

namespace MoveIt.Games.Boxing
{
    /// <summary>
    /// The referee (2026-11-19 overhaul): a chibi in a striped shirt and a bow tie (<see cref="BoxingLooks.Referee"/>,
    /// the low-detail model lit per vertex), part of the fight like Wii Sports Club's: he watches from the side (at the
    /// screen's right, clear of the opponent; the side shots look from the other side); a knockdown, and he walks over to
    /// whoever is down and counts with his arm, one swing a number; at the end he points up at the winner.
    /// Everything he does comes from the bout's state and its events (<see cref="Follow"/>, <see cref="Count"/>).
    /// </summary>
    public sealed class RefereeView : MonoBehaviour
    {
        Chibi _chibi;
        Vector3 _pos, _target;
        float _facing, _wantFacing, _walk, _time, _countAt = -10f;
        float _arms;   // 0 at his sides .. 1 the winner's arm up (pointing)
        int _winner = -1;

        /// <summary>Where he watches from: the opponent's left, our right. (Between the boxers for the bell he stood in
        /// front of one of them in every shot.)</summary>
        public static readonly Vector3 Watch = new Vector3(1.72f, 0f, 1.25f);

        /// <summary>Where he stands at the end, from the hero's feet: behind its right shoulder, in the winner's shot beside
        /// it (<see cref="BoxingGame"/>'s WinnerShot) and never between it and the camera.</summary>
        public static readonly Vector3 EndSpot = new Vector3(0.7f, 0f, -0.55f);

        public static RefereeView Create(Transform parent, float ringHeight)
        {
            var go = new GameObject("Referee");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<RefereeView>();
            view._chibi = Chibi.Create("Chibi", BoxingLooks.Referee(), go.transform);
            if (view._chibi != null)
            {
                view._chibi.transform.localScale = Vector3.one * 0.95f;
                view._chibi.Low = true;
                view._chibi.VertexLit = true;
            }
            view._pos = view._target = Watch + Vector3.up * ringHeight;
            view._facing = view._wantFacing = -110f;
            go.transform.localPosition = view._pos;
            return view;
        }

        /// <summary>A number of the count: his arm swings down once.</summary>
        public void Count() => _countAt = _time;

        /// <summary>The fight is over: he points at <paramref name="fighter"/> (0 the hero, 1 the opponent).</summary>
        public void Winner(int fighter) => _winner = fighter;

        /// <summary>A new fight: back to his place, no winner.</summary>
        public void Reset(float ringHeight)
        {
            _winner = -1;
            _pos = _target = Watch + Vector3.up * ringHeight;
            transform.localPosition = _pos;
        }

        /// <summary>Where to be and what to do now: <paramref name="bout"/> (null = between fights: at his place), the
        /// hero's and the opponent's places on the canvas (their feet, local to the stage).</summary>
        public void Follow(Bout bout, Vector3 hero, Vector3 foe, float ringHeight, float dt)
        {
            _time += dt;
            var ring = Vector3.up * ringHeight;
            Vector3 lookAt = (hero + foe) * 0.5f;
            if (bout == null) _target = Watch + ring;
            else if (bout.Phase == BoutPhase.Down)
            {
                // beside whoever is down, toward the screen's right so the count shows
                var down = bout.DownFighter == 0 ? hero : foe;
                _target = down + new Vector3(0.95f, 0f, bout.DownFighter == 0 ? 0.15f : -0.15f);
                _target.y = ringHeight;
                lookAt = down;
            }
            else if (bout.Phase == BoutPhase.Over && _winner >= 0)
            {
                _target = hero + EndSpot;
                _target.y = ringHeight;
                lookAt = _winner == 0 ? hero : foe;
            }
            else
            {
                // watching: a slow shuffle round his place
                _target = Watch + ring + new Vector3(Mathf.Sin(_time * 0.37f) * 0.16f, 0f, Mathf.Sin(_time * 0.23f) * 0.22f);
            }
            var move = _target - _pos;
            move.y = 0f;
            float step = Mathf.Min(move.magnitude, 1.7f * dt);
            bool walking = move.magnitude > 0.05f;
            if (walking) _pos += move.normalized * step;
            _pos.y = ringHeight;
            _walk = Mathf.MoveTowards(_walk, walking ? 1f : 0f, dt * 4f);
            var to = lookAt - _pos;
            if (walking && move.magnitude > 0.4f) to = move;
            if (to.sqrMagnitude > 1e-4f) _wantFacing = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            _facing = Mathf.MoveTowardsAngle(_facing, _wantFacing, 360f * dt);
            transform.localPosition = _pos + Vector3.up * (Mathf.Abs(Mathf.Sin(_time * 9f)) * 0.025f * _walk);
            transform.localRotation = Quaternion.Euler(0f, _facing, 0f);
            _arms = Mathf.MoveTowards(_arms, bout != null && bout.Phase == BoutPhase.Over && _winner >= 0 && !walking ? 1f : 0f, dt * 2.5f);
            Pose(bout, dt);
        }

        void Pose(Bout bout, float dt)
        {
            if (_chibi == null) return;
            _chibi.ResetPose();
            float swing = Mathf.Sin(_time * 9f) * 28f * _walk;
            _chibi.SwingLeg(0, swing);
            _chibi.SwingLeg(1, -swing);
            bool counting = bout != null && bout.Phase == BoutPhase.Down;
            // leaning over whoever is down while he counts
            _chibi.TurnSpine(0f, counting ? 16f : 0f, 0f);
            _chibi.TurnHead(0f, counting ? 12f : 0f);
            var t = transform;
            for (int s = 0; s < 2; s++)
            {
                float side = s == 0 ? -1f : 1f;
                var shoulder = _chibi.Shoulder(s);
                // at his sides, swinging as he walks
                var hand = shoulder + t.TransformDirection(new Vector3(side * 0.12f, -0.36f, 0.04f + Mathf.Sin(_time * 9f + s * Mathf.PI) * 0.1f * _walk));
                if (counting && s == 1)
                {
                    // the count: his right arm up, then down at the boxer on the canvas, once a number
                    float u = Mathf.Clamp01((_time - _countAt) / 0.45f);
                    var up = shoulder + t.TransformDirection(new Vector3(0.12f, 0.42f, 0.1f));
                    var down = shoulder + t.TransformDirection(new Vector3(0.06f, -0.22f, 0.36f));
                    hand = u < 1f ? Vector3.Lerp(up, down, u * u * (3f - 2f * u)) : up;
                }
                if (_arms > 0.01f && s == 1)
                    hand = Vector3.Lerp(hand, shoulder + t.TransformDirection(new Vector3(0.1f, 0.48f, 0.12f)), _arms);
                _chibi.ReachArm(s, hand, t.TransformDirection(new Vector3(side * 0.6f, -1f, -0.2f)));
            }
            _chibi.SetFace(counting ? Chibi.Face.Sad : _winner >= 0 ? Chibi.Face.Happy : Chibi.Face.Smile);
        }
    }
}
