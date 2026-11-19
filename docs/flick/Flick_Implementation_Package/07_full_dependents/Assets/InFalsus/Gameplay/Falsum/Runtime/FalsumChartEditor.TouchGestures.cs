using System.Collections.Generic;
using UnityEngine;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    /// <summary>
    /// Two-finger touch gestures for the editor, mapped onto the SAME actions
    /// the mouse and keyboard already drive:
    ///
    ///   two fingers swipe LEFT  -> undo
    ///   two fingers swipe RIGHT -> redo
    ///   two fingers swipe UP    -> scroll the timeline forward (wheel up)
    ///   two fingers swipe DOWN  -> scroll the timeline backward (wheel down)
    ///
    /// WHY A SEPARATE FILE: touch is a parallel input channel, not a mouse
    /// emulation. Unity's legacy Input already synthesises mouse events from a
    /// single finger; layering gesture handling onto that path would make a
    /// two-finger swipe also register as a drag, which is exactly how a
    /// gesture ends up editing the chart by accident.
    ///
    /// The gesture is DISCRETE on purpose. It fires once per completed swipe
    /// and then requires the fingers to lift, so one long drag cannot repeat
    /// undo/redo dozens of times -- the classic over-triggering bug for swipe
    /// gestures. Scrolling is the one gesture that may repeat, because a wheel
    /// repeats too and stepping through many grid lines in one motion is the
    /// point of a scroll.
    /// </summary>
    public sealed partial class FalsumChartEditor
    {
        /// <summary>Kind of gesture the current two-finger drag is.</summary>
        private enum TwoFingerGesture
        {
            None,
            Horizontal,
            Vertical,
        }

        /// <summary>
        /// Minimum swipe travel, as a fraction of the SMALLER screen dimension.
        /// Using screen size rather than a pixel constant keeps the gesture
        /// equally easy on a phone and on a large touch display.
        /// </summary>
        private const float TwoFingerSwipeFraction = 0.08f;

        /// <summary>
        /// A swipe must also be clearly one axis. The larger component has to
        /// beat the smaller by this ratio, so a sloppy diagonal does not
        /// trigger a random undo instead of a scroll.
        /// </summary>
        private const float TwoFingerAxisDominance = 1.6f;

        /// <summary>
        /// After firing, the gesture stays consumed until the fingers lift.
        /// Without this a single swipe would retrigger every frame once past
        /// the threshold.
        /// </summary>
        private bool _twoFingerGestureFired;

        /// <summary>Cached gesture classification for the current touch hold.</summary>
        private TwoFingerGesture _twoFingerGesture;

        /// <summary>Screen position where the current two-finger hold began.</summary>
        private Vector2 _twoFingerStart;

        /// <summary>Whether a two-finger hold was active last frame.</summary>
        private bool _twoFingerTracking;

        /// <summary>
        /// Number of whole swipe steps already dispatched during this hold.
        /// Scrolling compares the TOTAL travel against this so each step fires
        /// once, instead of accumulating a per-frame delta that would count the
        /// same distance over and over.
        /// </summary>
        private int _twoFingerScrollSteps;

        /// <summary>Next time a two-finger scroll step may fire.</summary>
        private double _twoFingerScrollNextAt;

        /// <summary>How long between two-finger scroll steps.</summary>
        private const double TwoFingerScrollIntervalSeconds = 0.06;

        /// <summary>
        /// Handles touch gestures for this frame. Called from the main Update
        /// before the mouse path, so a gesture can consume input that would
        /// otherwise be interpreted as a drag.
        /// </summary>
        private void HandleTouchGestures()
        {
            if (!Input.touchSupported || Input.touchCount < 2)
            {
                // Releasing every finger ends the gesture. Lifting ONE finger
                // also ends it: keeping it alive would mean a half-lifted hand
                // continues scrolling the chart.
                ResetTwoFingerGesture();
                return;
            }

            var first = Input.GetTouch(0);
            var second = Input.GetTouch(1);
            if (first.phase == TouchPhase.Ended ||
                first.phase == TouchPhase.Canceled ||
                second.phase == TouchPhase.Ended ||
                second.phase == TouchPhase.Canceled)
            {
                ResetTwoFingerGesture();
                return;
            }

            var center = (first.position + second.position) * 0.5f;
            if (!_twoFingerTracking)
            {
                _twoFingerTracking = true;
                _twoFingerStart = center;
                _twoFingerGesture = TwoFingerGesture.None;
                _twoFingerGestureFired = false;
                ExposeCanvasForTouch(center);
                return;
            }

            // A gesture over editor chrome (menus, timeline panel, dialogs)
            // belongs to the UI, not to the chart.
            if (_view.IsPointerOverUi(center))
            {
                ResetTwoFingerGesture();
                return;
            }

            var delta = center - _twoFingerStart;
            var threshold = Mathf.Min(Screen.width, Screen.height) *
                            TwoFingerSwipeFraction;
            if (delta.magnitude < threshold)
            {
                return;
            }

            if (_twoFingerGesture == TwoFingerGesture.None)
            {
                var horizontal = Mathf.Abs(delta.x);
                var vertical = Mathf.Abs(delta.y);
                var dominant = Mathf.Max(horizontal, vertical);
                var dominated = Mathf.Min(horizontal, vertical);
                if (dominant < dominated * TwoFingerAxisDominance)
                {
                    // Ambiguous diagonal: wait for more travel rather than
                    // guessing, so a sloppy swipe does not undo the chart.
                    return;
                }
                _twoFingerGesture = horizontal >= vertical
                    ? TwoFingerGesture.Horizontal
                    : TwoFingerGesture.Vertical;
            }

            if (_twoFingerGesture == TwoFingerGesture.Horizontal)
            {
                ApplyTwoFingerHorizontal(delta.x);
                return;
            }
            ApplyTwoFingerVertical(delta.y);
        }

        /// <summary>
        /// Left swipe undoes, right swipe redoes. Fires ONCE per swipe.
        /// </summary>
        private void ApplyTwoFingerHorizontal(float dx)
        {
            if (_twoFingerGestureFired)
            {
                return;
            }
            _twoFingerGestureFired = true;
            // Screen +x is to the right, so a LEFT swipe has dx < 0.
            // Left = undo, right = redo.
            if (dx < 0.0f)
            {
                Undo();
            }
            else
            {
                Redo();
            }
        }

        /// <summary>
        /// A two-finger swipe scrolls the timeline: UP goes backward, DOWN goes
        /// forward.
        ///
        /// This is DELIBERATELY the opposite of the mouse wheel. On a touch
        /// surface the gesture that feels natural is "drag the content": pulling
        /// downward brings earlier material into view from above, exactly like
        /// dragging a scrollable list. Matching the wheel's sign here (up =
        /// forward) reads as inverted to a touch user, which is what the user
        /// reported after trying it.
        ///
        /// Scrolling repeats while the fingers keep moving, but it accrues in
        /// whole steps with a short cooldown so a fast flick cannot jump the
        /// whole song.
        /// </summary>
        private void ApplyTwoFingerVertical(float dy)
        {
            // `dy` is the TOTAL travel since the two-finger hold began, not a
            // per-frame delta. The completed step count therefore has to be
            // derived from the total and compared against what was already
            // dispatched -- adding dy into an accumulator every frame would
            // count the same distance repeatedly and run away.
            var step = Mathf.Min(Screen.height, Screen.width) *
                       TwoFingerSwipeFraction;
            var completedSteps = Mathf.FloorToInt(
                Mathf.Abs(dy) / step);
            if (completedSteps <= _twoFingerScrollSteps)
            {
                return;
            }
            if (Time.unscaledTimeAsDouble < _twoFingerScrollNextAt)
            {
                return;
            }
            // Screen +y is up. Up swipe (dy > 0) scrolls BACKWARD; down swipe
            // scrolls FORWARD. See the summary above for why this is inverted
            // relative to the mouse wheel.
            var direction = dy > 0.0f ? -1.0 : 1.0;
            _twoFingerScrollSteps = completedSteps;
            ScrollTimeline(direction);
            _twoFingerScrollNextAt =
                Time.unscaledTimeAsDouble + TwoFingerScrollIntervalSeconds;
        }

        /// <summary>
        /// Ends the current gesture. Called when the finger count drops and
        /// after a gesture has been discharged.
        /// </summary>
        private void ResetTwoFingerGesture()
        {
            _twoFingerTracking = false;
            _twoFingerGesture = TwoFingerGesture.None;
            _twoFingerGestureFired = false;
            _twoFingerScrollSteps = 0;
            _twoFingerScrollNextAt = 0.0;
        }

        /// <summary>
        /// Whether a two-finger gesture currently owns the pointer, so the
        /// mouse path must not also act on this frame.
        /// </summary>
        private bool IsTwoFingerGestureActive
        {
            get { return _twoFingerTracking; }
        }

        /// <summary>
        /// Position the canvas pointer for a touch gesture. Touch has no
        /// cursor, so the chart's pointer state has to be driven explicitly or
        /// the gesture would act on a stale pointer.
        /// </summary>
        private void ExposeCanvasForTouch(Vector2 screenPoint)
        {
            if (_view == null || _view.IsPointerOverUi(screenPoint))
            {
                _hasPointer = false;
                return;
            }
            double rawTime;
            float lanePosition;
            int lane;
            _hasPointer = TryPointerTrack(
                screenPoint,
                _playheadMs,
                out rawTime,
                out lanePosition,
                out lane);
            if (_hasPointer)
            {
                _pointerRawTime = rawTime;
                _pointerTime = ResolveSkyAreaPlacementSnapTime(rawTime);
                _pointerLanePosition = lanePosition;
                _pointerLane = lane;
            }
        }
    }
}
