using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Paint
{
    public enum PenState
    {
        /// <summary>Hand not seen or resting at the side: no cursor.</summary>
        Away,
        /// <summary>Cursor shown, brush up: on its way in, over a toolbar, or (two hands) the switch is down.</summary>
        Aiming,
        /// <summary>Brush down: the hand paints wherever it goes.</summary>
        Painting,
    }

    /// <summary>Why the brush lifted.</summary>
    public enum PenLift { None, Switch, OffPaper, Rest, Lost }

    /// <summary>Easy way: what told the pen that the hand had arrived.</summary>
    public enum PenArrival { None, Stop, Turn, Timeout, Back }

    /// <summary>How Pikturo decides when a hand in the air paints.</summary>
    public enum PaintWay
    {
        /// <summary>Raise a hand and it paints, lower it to stop. For children: nothing to learn.</summary>
        Easy,
        /// <summary>One hand paints while the other is raised (the switch): exact starts and stops.</summary>
        TwoHands,
    }

    /// <summary>One moment of a painting hand, for <see cref="PaintPen.LeavingSince"/>: speeds in HUD pixels a second.</summary>
    public struct PenTrace
    {
        public float Time, Speed, Down;
    }

    /// <summary>
    /// When does a hand in the air paint? A camera can't see a pen touching paper, and nobody who built a drawing toy
    /// on a body camera found a natural answer (Kinect Sparkler read a fist against an open hand from its depth
    /// camera and flickered; Kinect Paint wanted a push toward the screen; PlayStation's Beat Sketcher had a wand with
    /// a button; Nex Playground has no drawing game). Pikturo has two ways (<see cref="PaintWay"/>):
    ///
    /// <b>Easy</b> (the default since 2026-11-10, for children): <b>raise a hand and it paints</b>, lower it and it
    /// stops. The brush goes down once the hand has <i>arrived</i> on the paper: it stopped (stayed within
    /// <see cref="StopRadius"/> for <see cref="StopSeconds"/>), or turned (heads <see cref="TurnDegrees"/> away from the
    /// way it came in), or <see cref="Timeout"/> passed. So raising the arm from the side, or coming back from the
    /// colours, draws no line up to where the stroke starts. It lifts when the hand leaves the paper, hangs at the
    /// side or is lost, and the game wipes the still-wet paint of that last movement (<see cref="LeavingSince"/>). A
    /// hand that slips off the paper's edge and is back within <see cref="ReturnSeconds"/> paints on at once (a big
    /// circle that dips below the paper is still one circle). Measured on the box's recordings of real arms with
    /// tools/motion/paint_sim.py: raises and drops leave no lines, circles come out whole.
    ///
    /// <b>Two hands</b> (2026-10-09, the user's idea): one hand paints, the other is the switch. Free hand raised to
    /// the chest = brush down, lowered = brush up. Starting and stopping is instant and exactly where the player wants,
    /// and the painting hand can move freely to a new start point with the switch down.
    ///
    /// (Before 2026-10-09 each hand painted after holding still for 0.35 s: the player found moving to a new spot meant
    /// dropping the arm, and a slow hand still on its way already counted as still.)
    ///
    /// Toolbars are never painted on: over a toolbar the cursor presses buttons instead.
    /// </summary>
    public sealed class PaintPen
    {
        /// <summary>Easy way, in torso lengths: staying within this far of a spot...</summary>
        public float StopRadius = 0.075f;
        /// <summary>...for this long is a stop: the hand has arrived.</summary>
        public float StopSeconds = 0.14f;
        /// <summary>Heading this many degrees away from the way the hand came in is a turn: arrived.</summary>
        public float TurnDegrees = 65f;
        /// <summary>The way in is only known after this much travel (torso lengths).</summary>
        public float HeadingDistance = 0.15f;
        /// <summary>A hand that neither stops nor turns paints after this long on the paper.</summary>
        public float Timeout = 0.8f;
        /// <summary>Off the paper and back within this long (without resting): the same stroke goes on at once.</summary>
        public float ReturnSeconds = 0.6f;
        /// <summary>Onto the paper only when this far inside its edge, off it when this far outside (torso lengths).</summary>
        public float EdgeMargin = 0.03f;

        public PenState State { get; private set; }
        /// <summary>HUD position (pixels of a 1080 px tall screen, y down).</summary>
        public Vector2 Position { get; private set; }
        public bool Visible => State != PenState.Away;
        /// <summary>Why the brush last lifted.</summary>
        public PenLift LastLift { get; private set; }
        /// <summary>Easy way: how the brush last went down.</summary>
        public PenArrival LastArrival { get; private set; }
        /// <summary>Easy way: on the paper, the brush not down yet.</summary>
        public bool Arriving => State == PenState.Aiming && _onPaper;

        const int TrailSize = 16;
        readonly double[] _trailTime = new double[TrailSize];
        readonly Vector2[] _trail = new Vector2[TrailSize];
        int _trailCount;
        bool _onPaper;
        double _entryTime, _anchorTime, _leftAt = double.NegativeInfinity;
        Vector2 _entry, _anchor;

        /// <summary>
        /// The easy way, one frame of a hand. <paramref name="inside"/> = how far inside the paper the cursor is (HUD
        /// pixels, negative outside or over a toolbar); <paramref name="resting"/> = arm hanging at the side;
        /// <paramref name="unit"/> = HUD pixels per torso length. Returns +1 when the brush goes down this frame, -1
        /// when it lifts, 0 otherwise.
        /// </summary>
        public int UpdateEasy(double now, bool tracked, Vector2 position, float inside, bool resting, float unit)
        {
            Position = position;
            var before = State;
            if (!tracked || resting)
            {
                if (before == PenState.Painting) LastLift = tracked ? PenLift.Rest : PenLift.Lost;
                State = PenState.Away;
                _onPaper = false;
                _leftAt = double.NegativeInfinity;   // a hand that rested starts over
                return Change(before);
            }
            float margin = EdgeMargin * unit;
            bool wasOn = _onPaper;
            _onPaper = inside >= (wasOn ? -margin : margin);
            if (!_onPaper)
            {
                if (before == PenState.Painting)
                {
                    LastLift = PenLift.OffPaper;
                    _leftAt = now;
                }
                State = PenState.Aiming;
                return Change(before);
            }
            if (!wasOn)
            {
                if (now - _leftAt <= ReturnSeconds)   // slipped off the edge and came back: the same movement
                {
                    _leftAt = double.NegativeInfinity;
                    LastArrival = PenArrival.Back;
                    State = PenState.Painting;
                    return Change(before);
                }
                _entryTime = _anchorTime = now;
                _entry = _anchor = position;
                _trailCount = 0;
                State = PenState.Aiming;
            }
            if (State != PenState.Painting)
            {
                var arrival = Arrived(now, position, unit);
                if (arrival != PenArrival.None)
                {
                    LastArrival = arrival;
                    State = PenState.Painting;
                }
            }
            return Change(before);
        }

        PenArrival Arrived(double now, Vector2 p, float unit)
        {
            if (_trailCount == TrailSize)
            {
                System.Array.Copy(_trailTime, 1, _trailTime, 0, TrailSize - 1);
                System.Array.Copy(_trail, 1, _trail, 0, TrailSize - 1);
                _trailCount--;
            }
            _trailTime[_trailCount] = now;
            _trail[_trailCount++] = p;

            if (Vector2.Distance(p, _anchor) > StopRadius * unit)
            {
                _anchor = p;
                _anchorTime = now;
            }
            if (now - _anchorTime >= StopSeconds) return PenArrival.Stop;
            if (now - _entryTime >= Timeout) return PenArrival.Timeout;

            Vector2 came = p - _entry;
            if (came.magnitude < HeadingDistance * unit) return PenArrival.None;
            for (int i = _trailCount - 1; i >= 0; i--)
            {
                if (now - _trailTime[i] < 0.1) continue;
                Vector2 heading = p - _trail[i];
                if (heading.magnitude >= 0.04f * unit && Vector2.Angle(came, heading) > TurnDegrees) return PenArrival.Turn;
                break;
            }
            return PenArrival.None;
        }

        /// <summary>
        /// Two hands, one frame of the painting hand. <paramref name="overPicture"/> = on the paper and not over a
        /// toolbar; <paramref name="resting"/> = arm hanging at the side; <paramref name="switchOn"/> = the other hand
        /// is raised. Returns +1 when the brush goes down this frame, -1 when it lifts, 0 otherwise.
        /// </summary>
        public int UpdateSwitch(bool tracked, Vector2 position, bool onScreen, bool overPicture, bool resting, bool switchOn)
        {
            Position = position;
            var before = State;
            _onPaper = false;
            if (!tracked || !onScreen || resting) State = PenState.Away;
            else State = overPicture && switchOn ? PenState.Painting : PenState.Aiming;
            if (before == PenState.Painting && State != PenState.Painting)
                LastLift = !tracked || !onScreen ? PenLift.Lost : resting ? PenLift.Rest : !overPicture ? PenLift.OffPaper : PenLift.Switch;
            return Change(before);
        }

        /// <summary>
        /// Two hands: is the switch (the free hand) up? <paramref name="height"/> = its wrist above the hips in torso
        /// lengths (0 = hips, 1 = shoulders). On above <paramref name="onHeight"/> (lifted to the chest), off again only
        /// below <paramref name="offHeight"/>, so it doesn't flicker.
        /// </summary>
        public static bool SwitchState(bool wasOn, float height, float onHeight, float offHeight) =>
            wasOn ? height > offHeight : height > onHeight;

        /// <summary>A hand that only points (two hands: the switch hand presses buttons, it never paints).</summary>
        public int UpdatePointer(bool shown, Vector2 position)
        {
            Position = position;
            var before = State;
            _onPaper = false;
            State = shown ? PenState.Aiming : PenState.Away;
            if (before == PenState.Painting) LastLift = PenLift.Switch;
            return Change(before);
        }

        /// <summary>Mouse as the brush (development without a camera): paints while the button is held.</summary>
        public int UpdateMouse(Vector2 position, bool onScreen, bool overPicture, bool buttonHeld)
        {
            Position = position;
            var before = State;
            _onPaper = false;
            State = !onScreen ? PenState.Away : overPicture && buttonHeld ? PenState.Painting : PenState.Aiming;
            if (before == PenState.Painting && State != PenState.Painting) LastLift = PenLift.Switch;
            return Change(before);
        }

        /// <summary>Lifts the brush at once (mode switch, toolbar action). The hand has to arrive again.</summary>
        public int Lift()
        {
            var before = State;
            if (State == PenState.Painting) State = PenState.Aiming;
            _onPaper = false;
            _leftAt = double.NegativeInfinity;
            if (before == PenState.Painting) LastLift = PenLift.Switch;
            return Change(before);
        }

        int Change(PenState before)
        {
            if (State == PenState.Painting && before != PenState.Painting) return 1;
            if (State != PenState.Painting && before == PenState.Painting) return -1;
            return 0;
        }

        /// <summary>
        /// The brush lifted because the hand left (dropped, went for a button, was lost). From which moment on was it
        /// leaving, not painting? Walks back through <paramref name="history"/> (oldest first) while the hand was fast
        /// (<paramref name="tailSpeed"/>) or, when it left downward (<paramref name="down"/>: the arm dropped), moving
        /// down faster than <paramref name="downSpeed"/>; it may have slowed for a moment at the very end. Returns the
        /// time to wipe the wet paint from, or +infinity when the hand just stopped where it was.
        /// </summary>
        public static float LeavingSince(IReadOnlyList<PenTrace> history, float now, float tailSpeed, float downSpeed, bool down)
        {
            int k = history.Count - 1;
            while (k >= 0 && !Leaving(history[k], tailSpeed, downSpeed, down) && now - history[k].Time < 0.15f) k--;
            if (k < 0 || !Leaving(history[k], tailSpeed, downSpeed, down)) return float.PositiveInfinity;
            while (k > 0 && Leaving(history[k - 1], tailSpeed, downSpeed, down)) k--;
            return history[k].Time - 0.05f;
        }

        static bool Leaving(in PenTrace t, float tailSpeed, float downSpeed, bool down) =>
            t.Speed >= tailSpeed || (down && t.Down >= downSpeed);
    }
}
