using System;
using System.Collections.Generic;
using InFalsus.Gameplay.Core.Charts;
using InFalsus.Gameplay.Falsum.Core;
using InFalsus.Gameplay.Falsum.UI;
using UnityEngine;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    /// <summary>
    /// Direct-manipulation anchors drawn on the selection box: a second quick
    /// edit channel beside the middle-mouse fine edit.
    ///
    /// <para>THE RULE THIS FILE EXISTS TO ENFORCE: an anchor does not
    /// reimplement editing. It declares one operation, builds the same
    /// <see cref="MiddleEditState"/> the middle-mouse channel builds, and calls
    /// the middle-mouse channel's body with it. Two hand-written copies of the
    /// clamp/snap/edge math drifted apart badly enough that the handles produced
    /// values that did not match the box they were drawn on; the only way to
    /// stop that recurring is to have one implementation.</para>
    ///
    /// <para>THE ONE EXCEPTION is
    /// <see cref="ApplyAnchorEndpointLateral"/>: a skyarea front/back edge
    /// midpoint dragged LATERALLY moves that single endpoint's position and
    /// leaves its width alone. The middle-mouse modes have no equivalent
    /// (Boundary edits edges, Position translates the whole note), so this one
    /// operation is bespoke by necessity.</para>
    ///
    /// <para>Every drag runs inside exactly one
    /// <c>BeginChange</c>/<c>CommitChange</c> pair, so it produces exactly one
    /// undo record, matching the middle-mouse channel.</para>
    /// </summary>
    public sealed partial class FalsumChartEditor
    {
        /// <summary>Screen-pixel grab radius for an anchor.</summary>
        private const float AnchorPickRadiusPixels = 14.0f;

        /// <summary>
        /// Hover uses a wider radius than the click, so "move near the handle
        /// and click" works without pixel-perfect aiming.
        /// </summary>
        private const float AnchorHoverRadiusScale = 1.45f;

        /// <summary>Minimum skyarea endpoint width during a lateral move.</summary>
        private const double AnchorMinimumEndpointWidth = 0.0001;

        private int _anchorHoverIndex = -1;

        /// <summary>
        /// The single operation an anchor performs. Derived once when the
        /// anchor is pressed, from the note kind plus the anchor's Kind/Edge/
        /// Tail, and never re-decided mid-drag. A user who grabs a handle knows
        /// which axis they meant; guessing from the first few pixels of travel
        /// is what made a lateral nudge able to silently retime a note.
        /// </summary>
        private enum AnchorOperation
        {
            /// <summary>Lateral boundary of a TAP/HOLD endpoint (middle Boundary).</summary>
            GroundBoundary,
            /// <summary>Lateral boundary of a FLICK/SKYAREA endpoint (middle Boundary).</summary>
            SkyBoundary,
            /// <summary>Start or end time (middle StartEnd).</summary>
            StartEndTime,
            /// <summary>Translate the whole note laterally (middle Position).</summary>
            Translate,
            /// <summary>Move one skyarea endpoint laterally, width preserved.</summary>
            EndpointLateral,
            /// <summary>Click-only: cycle the skyarea easing.</summary>
            DragEasing,
            /// <summary>Click-only: flip the flick direction.</summary>
            FlipFlickColor,
            /// <summary>
            /// Multi-selected SkyArea chain: scale the group's time span from
            /// its head, keeping every seam at exactly zero.
            /// </summary>
            GroupTimelineScale,
        }

        private enum AnchorPhase
        {
            Idle,
            Pressed,
            Dragging,
        }

        private sealed class AnchorDragState
        {
            public AnchorPhase Phase;
            public ChartNote Note;
            public AnchorOperation Operation;
            public MiddleEditTimeSide TimeSide;
            /// <summary>The anchor being dragged, for its snap geometry.</summary>
            public FalsumSelectionAnchor Anchor;
            /// <summary>Canvas point where the drag was pressed.</summary>
            public Vector2 PressCanvas;
            /// <summary>Easing value at press time (only for DragEasing).</summary>
            public int PressEasingSlot;
            /// <summary>Slot offset last written, so a detent only fires once.</summary>
            public int PressEasingSlots;
            /// <summary>The shared state handed to the middle-edit bodies.</summary>
            public MiddleEditState Shared;
            public Vector2 PressScreen;
            public double PressStartPos;
            public double PressEndPos;
            public double PressStartWidth;
            public double PressEndWidth;
            /// <summary>Press-time capture of the whole SkyArea group (group ops).</summary>
            public FalsumSkyAreaGroupSnapshot GroupSnapshot;
            /// <summary>Pointer chart time at press time (group timeline op).</summary>
            public double PressRawTime;
        }

        private AnchorDragState _anchorDrag;

        private bool IsAnchorDragActive
        {
            get
            {
                return _anchorDrag != null &&
                       _anchorDrag.Phase != AnchorPhase.Idle;
            }
        }

        /// <summary>
        /// Press-time state for a whole-group SkyArea scale drag.
        ///
        /// <para>It captures a <see cref="FalsumSkyAreaGroupSnapshot"/> and
        /// writes ABSOLUTE values from it every frame. Multiplying the live
        /// notes frame by frame would accumulate rounding drift and slowly
        /// deform the group -- the same reason the middle-mouse bodies keep
        /// press snapshots.</para>
        /// </summary>
        private AnchorDragState CreateGroupAnchorState(
            FalsumSelectionAnchor anchor,
            AnchorOperation operation,
            Vector2 screenPosition)
        {
            var notes = SelectedSkyAreaNotes();
            var snapshot = FalsumSkyAreaGroupScale.Capture(notes);
            if (snapshot == null || snapshot.Count < 2)
            {
                return null;
            }

            var state = new AnchorDragState
            {
                Note = notes[0],
                Operation = operation,
                Anchor = anchor,
                PressScreen = screenPosition,
                PressCanvas = screenPosition,
                GroupSnapshot = snapshot,
                PressRawTime = _pointerRawTime,
                PressStartPos = notes[0].StartPos,
                PressEndPos = notes[0].EndPos,
                PressStartWidth = notes[0].StartWidth,
                PressEndWidth = notes[0].EndWidth,
            };

            // The group MOVE handle runs through the ordinary multi-target
            // position body, so it needs the same MiddleEditState the
            // single-note path builds. Scaling handles do not: they write a
            // snapshot directly. Leaving Shared null for a Translate drag was
            // an instant NullReferenceException mid-drag.
            if (operation == AnchorOperation.Translate)
            {
                var shared = new MiddleEditState
                {
                    Phase = MiddleEditPhase.Pressed,
                    Mode = FalsumMiddleEditMode.Position,
                    ActiveNote = notes[0],
                    Side = MiddleEditSide.Left,
                    TimeSide = MiddleEditTimeSide.Head,
                    PressScreen = screenPosition,
                    PressLanePosition = _pointerLanePosition,
                    PressRawTime = _pointerRawTime,
                };
                shared.ProjectionTimeMs = snapshot.AnchorTimeMs;
                float pressPlaneX;
                if (TryGetPointerNormalizedOnTimePlane(
                        screenPosition,
                        snapshot.AnchorTimeMs,
                        _playheadMs,
                        out pressPlaneX))
                {
                    shared.HasPlanePointer = true;
                    shared.PressPlaneX = pressPlaneX;
                    shared.CurrentPlaneX = pressPlaneX;
                }
                for (var i = 0; i < notes.Count; i++)
                {
                    shared.SelectionIds.Add(notes[i].Id);
                    AddMiddleTarget(
                        shared,
                        notes[i],
                        ReferenceEquals(notes[i], notes[0]));
                }
                if (shared.Targets.Count == 0)
                {
                    return null;
                }
                shared.Grid = ComputeMiddleEditGrid(
                    shared.Mode,
                    shared.Targets,
                    notes[0]);
                state.Shared = shared;
            }
            return state;
        }

        /// <summary>Scales the group's time span from its head.</summary>
        private void ApplyGroupTimelineScale(AnchorDragState state)
        {
            var snapshot = state.GroupSnapshot;
            if (snapshot == null)
            {
                return;
            }
            // Snap the dragged end to a GRID LINE first, then scale from the
            // untouched opposite end. Scaling from an unsnapped pointer time
            // put every segment boundary off-lattice and made the drag feel
            // like it ignored the grid entirely.
            var pointerTime = _world.SnapTime(_pointerRawTime);
            // The grabbed end is the one being moved; the opposite end is the
            // scale pivot, so dragging the FRONT handle pins the group's last
            // time and dragging the BACK handle pins its first.
            var movesFront = state.Anchor != null &&
                             state.Anchor.Edge == FalsumAnchorEdge.Front;
            var pivotTime = movesFront
                ? GroupLastTimeMs(snapshot)
                : snapshot.AnchorTimeMs;
            // The pressed end is the boundary the handle already sat on, taken
            // from the snapshot rather than the raw pixel-derived time: pairing
            // an exact boundary with an off-lattice press point would bake a
            // constant ratio error into every frame of the drag.
            var pressedEdge = movesFront
                ? snapshot.AnchorTimeMs
                : GroupLastTimeMs(snapshot);
            var denominator = pressedEdge - pivotTime;
            // Grabbed end pinned on the pivot: no meaningful ratio.
            if (Math.Abs(denominator) < 0.5)
            {
                return;
            }
            var scale = (pointerTime - pivotTime) / denominator;
            FalsumSkyAreaGroupScale.ApplyTimelineScale(
                snapshot,
                pivotTime,
                scale);
        }

        /// <summary>Latest end time across a captured group snapshot.</summary>
        private static double GroupLastTimeMs(
            FalsumSkyAreaGroupSnapshot snapshot)
        {
            var last = snapshot.AnchorTimeMs;
            for (var i = 0; i < snapshot.Count; i++)
            {
                var end = snapshot.TimeMs[i] + snapshot.DurationMs[i];
                if (end > last)
                {
                    last = end;
                }
            }
            return last;
        }

        /// <summary>
        /// Puts every segment of the group back to its press-time values.
        /// The middle-mouse channel is covered by its own snapshot/replace
        /// machinery, but the anchor channel drives the notes directly, so a
        /// cancelled group drag has to undo itself explicitly. The restore
        /// itself lives in Core: the anchor layer never assigns note fields
        /// (that rule is what keeps the two editing copies from drifting).
        /// </summary>
        private static void RollbackGroupDrag(AnchorDragState state)
        {
            if (state == null)
            {
                return;
            }
            FalsumSkyAreaGroupScale.Restore(state.GroupSnapshot);
        }

        private static bool IsGroupOperation(AnchorOperation operation)
        {
            return operation == AnchorOperation.GroupTimelineScale;
        }

        /// <summary>
        /// The operations whose drag runs as a plane-facing middle edit, i.e.
        /// the ones whose value lives on the ruler's lateral lattice. Everything
        /// else (easing, endpoint lateral, the group timeline scale) does not:
        /// the easing control edits a three-state value, the endpoint move slides
        /// one edge without changing its width, and the group scale writes a
        /// snapshot. Surfacing the plane for those would put a GRID readout next
        /// to a gesture that has no grid.
        /// </summary>
        /// <summary>
        /// Anchor operations that surface the GRID ruler while they run.
        ///
        /// <para><see cref="AnchorOperation.EndpointLateral"/> is included: a
        /// skyarea front/back edge dragged sideways snaps on the same
        /// normalized lattice the ruler draws (its shared state runs the Position
        /// body), so hiding the ruler made the author drag blind. Easing and the
        /// group timeline scale are still excluded -- neither of them
        /// quantizes onto that lattice.</para>
        /// </summary>
        private static bool IsAnchorPlaneOperation(AnchorOperation operation)
        {
            return operation == AnchorOperation.Translate ||
                   operation == AnchorOperation.GroundBoundary ||
                   operation == AnchorOperation.SkyBoundary ||
                   operation == AnchorOperation.EndpointLateral;
        }

        /// <summary>
        /// Publishes the ruler state for a plane-facing anchor drag.
        ///
        /// <para>Invariant A-18: this layer performs NO grid math. It reads the
        /// shared state's <c>Grid</c> and <c>ProjectionTimeMs</c> and forwards
        /// the pointer's already-normalized plane position; the cell
        /// arithmetic and the info-box text stay in <c>FalsumChartEditor.cs</c>.
        /// </para>
        /// </summary>
        private void PublishAnchorPlaneState(
            AnchorOperation operation,
            MiddleEditState shared)
        {
            if (shared == null || !_hasPointer)
            {
                _anchorPlaneVisible = false;
                return;
            }
            _anchorPlaneVisible = true;
            _anchorPlaneMode = shared.Mode;
            _anchorPlaneGrid = shared.Grid;
            _anchorPlaneKind = shared.ActiveNote != null
                ? shared.ActiveNote.Kind
                : NoteKind.Tap;
            _anchorPlaneTimeMs = shared.ProjectionTimeMs;
            _anchorPlaneStartNormalized = float.NaN;
            _anchorPlaneEndNormalized = float.NaN;
            // A TAP/HOLD boundary moves in whole lanes, not along the plane's
            // normalized lattice, so it has no marker to place. The plane still
            // surfaces (the drag runs the shared boundary body), it just shows
            // GRID without a position row.
            if (operation == AnchorOperation.GroundBoundary ||
                !shared.HasPlanePointer)
            {
                return;
            }
            _anchorPlaneStartNormalized = shared.CurrentPlaneX;
            // A skyarea endpoint's lateral drag edits ONE edge of the note.
            // Marker it on the edge under the pointer: reading CurrentPlaneX
            // keeps the tick on the cursor, which is what the snap actually
            // follows. The opposite edge is the other end of the note's
            // footprint, so the ruler shows the whole span being resized.
            if (operation == AnchorOperation.EndpointLateral)
            {
                var active = shared.ActiveNote;
                if (active != null && active.Kind == NoteKind.SkyArea)
                {
                    var tail =
                        shared.TimeSide == MiddleEditTimeSide.Tail;
                    _anchorPlaneEndNormalized =
                        (float)(tail
                            ? active.StartPos
                            : active.EndPos);
                }
            }
        }

        /// <summary>
        /// Clears the anchor drag's contribution to the ruler. Called from
        /// commit and cancel, so a finished drag leaves the plane exactly as the
        /// ambient mode would have described it.
        /// </summary>
        private void ClearAnchorPlaneState()
        {
            _anchorPlaneVisible = false;
            _anchorPlaneStartNormalized = float.NaN;
            _anchorPlaneEndNormalized = float.NaN;
        }

        private ChartNote AnchorSingleSelection()
        {
            return _selected.Count == 1 ? _selected[0] : null;
        }

        /// <summary>
        /// True when the selection is a multi-note SkyArea chain, i.e. the case
        /// that gets whole-group scaling handles. Deliberately narrow: a mixed
        /// tap+skyarea selection keeps the old "no anchors on multi-select".
        /// </summary>
        private bool IsSkyAreaGroupSelection()
        {
            if (_selected.Count < 2)
            {
                return false;
            }
            for (var i = 0; i < _selected.Count; i++)
            {
                var note = _selected[i];
                if (note == null || note.Kind != NoteKind.SkyArea)
                {
                    return false;
                }
            }
            return true;
        }

        private List<ChartNote> SelectedSkyAreaNotes()
        {
            var notes = new List<ChartNote>();
            for (var i = 0; i < _selected.Count; i++)
            {
                var note = _selected[i];
                if (note != null && note.Kind == NoteKind.SkyArea)
                {
                    notes.Add(note);
                }
            }
            return notes;
        }

        /// <summary>
        /// Anchor hit test. Called BEFORE note picking so grabbing a handle
        /// never reselects or starts a box drag underneath it.
        /// </summary>
        private bool TryBeginAnchorDrag(Vector2 screenPosition)
        {
            var note = AnchorSingleSelection();
            var isGroupSelection = note == null &&
                                   IsSkyAreaGroupSelection();
            if ((note == null && !isGroupSelection) ||
                _package == null ||
                _view.IsModalOpen ||
                _view.IsTyping() ||
                (_host.Clock != null && _host.Clock.IsPlaying) ||
                _session.HasPendingEdit)
            {
                return false;
            }

            Vector2 pressCanvas;
            int anchorIndex;
            var anchor = _world.FindAnchorAt(
                screenPosition,
                AnchorPickRadiusPixels,
                out pressCanvas,
                out anchorIndex);
            if (anchor == null)
            {
                return false;
            }

            // The flick colour control is still a CLICK: it has exactly two
            // states and no meaningful travel, so a drag would be fiddly.
            //
            // The easing control is now BOTH: it cycles on a click that does not
            // move, and it changes value while dragged. That is why it goes
            // through the drag state machine instead of being handled here.
            var operation = ResolveAnchorOperation(
                note,
                anchor,
                isGroupSelection);
            if (operation == AnchorOperation.FlipFlickColor)
            {
                if (note == null)
                {
                    return false;
                }
                ToggleFlickDirectionFromAnchor(note);
                return true;
            }
            if (isGroupSelection)
            {
                // Only the group-scaling channels exist on a group handle set;
                // the per-note boundary/time/easing operations do not apply.
                // The lateral width channel is gone for good: the builder no
                // longer emits a Boundary anchor for a group.
                if (operation != AnchorOperation.GroupTimelineScale &&
                    operation != AnchorOperation.Translate)
                {
                    return false;
                }
                _anchorDrag = CreateGroupAnchorState(
                    anchor,
                    operation,
                    screenPosition);
                if (_anchorDrag == null)
                {
                    return false;
                }
                _anchorDrag.Phase = AnchorPhase.Pressed;
                return true;
            }

            _anchorDrag = CreateAnchorState(note, anchor, operation, screenPosition);
            if (_anchorDrag == null)
            {
                return false;
            }
            _anchorDrag.Phase = AnchorPhase.Pressed;
            return true;
        }

        /// <summary>
        /// Maps (note kind, anchor Kind/Edge/Tail) onto exactly one operation.
        /// Returning null means the pairing is not editable, which should be
        /// impossible because the builder only emits editable anchors.
        ///
        /// <para>The caller passes <paramref name="skyAreaGroup"/> because the
        /// same Handle kind means "edit this endpoint" for one note and "scale
        /// the whole group" for a multi-selected SkyArea chain. The decision is
        /// made exactly once per drag, at press time, and stays static so the
        /// role mapping never depends on mutable editor state beyond that one
        /// explicit flag.</para>
        ///
        /// <para>Only <see cref="FalsumAnchorKind.StartEnd"/> has a group
        /// meaning left. <see cref="FalsumAnchorKind.Boundary"/> never answers
        /// with a group operation: the group's lateral width handle was removed,
        /// so a group selection emits no such anchor and this method can no
        /// longer hand back a width-scale operation.</para>
        /// </summary>
        private static AnchorOperation ResolveAnchorOperation(
            ChartNote note,
            FalsumSelectionAnchor anchor,
            bool skyAreaGroup)
        {
            switch (anchor.Kind)
            {
                case FalsumAnchorKind.Color:
                    return AnchorOperation.FlipFlickColor;
                case FalsumAnchorKind.Easing:
                    return AnchorOperation.DragEasing;
                case FalsumAnchorKind.Move:
                    return AnchorOperation.Translate;
                case FalsumAnchorKind.StartEnd:
                    // A multi-selected SkyArea chain scales its time span; the
                    // single-note SKYAREA midpoint keeps its two-axis split.
                    if (skyAreaGroup)
                    {
                        return AnchorOperation.GroupTimelineScale;
                    }
                    // The skyarea front/back midpoint owns two axes: it edits
                    // time AND that endpoint's lateral position. It is the only
                    // anchor that has to choose, so it keeps the explicit
                    // two-button split (vertical = middle StartEnd, horizontal =
                    // the bespoke endpoint move).
                    return note != null &&
                           note.Kind == NoteKind.SkyArea
                        ? AnchorOperation.EndpointLateral
                        : AnchorOperation.StartEndTime;
                default:
                    // A group selection emits no Boundary anchor at all (its
                    // lateral width handle was removed), so a group can never
                    // reach this default branch through the builder. Should one
                    // ever arrive, the per-note mapping below is what it gets --
                    // and TryBeginAnchorDrag rejects every non-group operation
                    // for a group selection, so no handle becomes live.
                    return note == null
                        ? AnchorOperation.GroundBoundary
                        : (note.Kind == NoteKind.Tap ||
                           note.Kind == NoteKind.Hold
                            ? AnchorOperation.GroundBoundary
                            : AnchorOperation.SkyBoundary);
            }
        }

        private AnchorDragState CreateAnchorState(
            ChartNote note,
            FalsumSelectionAnchor anchor,
            AnchorOperation operation,
            Vector2 screenPosition)
        {
            var side = anchor.Edge == FalsumAnchorEdge.Right
                ? MiddleEditSide.Right
                : MiddleEditSide.Left;
            var timeSide = anchor.Tail
                ? MiddleEditTimeSide.Tail
                : MiddleEditTimeSide.Head;

            // The easing control does not go through the middle-mouse edit at
            // all: it is a three-state value of its own. It builds a lightweight
            // drag state and snaps against the anchor's stored snap geometry.
            if (operation == AnchorOperation.DragEasing)
            {
                if (note.Kind != NoteKind.SkyArea || !anchor.HasSnapGeometry)
                {
                    return null;
                }
                Vector2 pressCanvas;
                if (!_world.TryScreenToCanvas(screenPosition, out pressCanvas))
                {
                    pressCanvas = anchor.CanvasPosition;
                }
                // The starting slot must be what this HANDLE shows, not the raw
                // stored value: when in/out is traded for this side the handle
                // sits on the opposite outer detent, so seeding from the raw
                // field would jump the control by one slot on press.
                var leftHandleSide = anchor.Edge == FalsumAnchorEdge.Left;
                var currentEasing =
                    FalsumSkyAreaGroupScale.EasingToHandleValue(
                        note,
                        leftHandleSide,
                        leftHandleSide
                            ? note.LeftEasing
                            : note.RightEasing);
                return new AnchorDragState
                {
                    Note = note,
                    Operation = operation,
                    Anchor = anchor,
                    PressCanvas = pressCanvas,
                    PressEasingSlot =
                        Mathf.Clamp(currentEasing, 0, 2) - 1,
                    PressEasingSlots = 0,
                    PressScreen = screenPosition,
                    PressStartPos = note.StartPos,
                    PressEndPos = note.EndPos,
                    PressStartWidth = note.StartWidth,
                    PressEndWidth = note.EndWidth,
                };
            }

            var mode = MiddleEditModeFor(operation);
            if (mode == FalsumMiddleEditMode.Boundary &&
                IsAnchorGroundBoundaryMissing(note, operation))
            {
                // TAP/HOLD boundary editing only exists for the central lanes;
                // a ground note on a side lane has no lateral boundary to edit.
                return null;
            }
            if (operation == AnchorOperation.EndpointLateral &&
                !CanMoveSkyAreaEndpoint(note, timeSide))
            {
                return null;
            }

            var shared = new MiddleEditState
            {
                Phase = MiddleEditPhase.Pressed,
                Mode = mode,
                ActiveNote = note,
                Side = side,
                TimeSide = timeSide,
                PressScreen = screenPosition,
                PressLanePosition = _pointerLanePosition,
                PressRawTime = _pointerRawTime,
            };
            shared.ProjectionTimeMs = AnchorProjectionTimeMs(note, timeSide);
            float pressPlaneX;
            if (TryGetPointerNormalizedOnTimePlane(
                    screenPosition,
                    shared.ProjectionTimeMs,
                    _playheadMs,
                    out pressPlaneX))
            {
                shared.HasPlanePointer = true;
                shared.PressPlaneX = pressPlaneX;
                shared.CurrentPlaneX = pressPlaneX;
            }
            shared.SelectionIds.Add(note.Id);
            AddMiddleTarget(shared, note, true);
            if (shared.Targets.Count == 0)
            {
                return null;
            }
            shared.Grid = ComputeMiddleEditGrid(
                shared.Mode,
                shared.Targets,
                note);

            return new AnchorDragState
            {
                Note = note,
                Operation = operation,
                TimeSide = timeSide,
                Anchor = anchor,
                PressCanvas = screenPosition,
                Shared = shared,
                PressScreen = screenPosition,
                PressStartPos = note.StartPos,
                PressEndPos = note.EndPos,
                PressStartWidth = note.StartWidth,
                PressEndWidth = note.EndWidth,
            };
        }

        /// <summary>
        /// The middle-mouse mode a DRAG operation runs as. Only called for
        /// operations that build a shared <see cref="MiddleEditState"/>;
        /// <see cref="AnchorOperation.DragEasing"/> and
        /// <see cref="AnchorOperation.FlipFlickColor"/> are handled before that
        /// state exists, and the explicit default throws so a future operation
        /// cannot silently inherit a drag mode it was never designed for.
        /// </summary>
        private static FalsumMiddleEditMode MiddleEditModeFor(
            AnchorOperation operation)
        {
            switch (operation)
            {
                case AnchorOperation.GroundBoundary:
                case AnchorOperation.SkyBoundary:
                    return FalsumMiddleEditMode.Boundary;
                case AnchorOperation.StartEndTime:
                    return FalsumMiddleEditMode.StartEnd;
                case AnchorOperation.Translate:
                case AnchorOperation.EndpointLateral:
                    return FalsumMiddleEditMode.Position;
                case AnchorOperation.GroupTimelineScale:
                case AnchorOperation.DragEasing:
                case AnchorOperation.FlipFlickColor:
                    // These never run through the middle-mouse bodies: they
                    // short-circuit in UpdateAnchorDrag. Throwing here keeps a
                    // future operation from silently inheriting a mode.
                    throw new ArgumentOutOfRangeException(
                        "operation",
                        operation,
                        "this anchor operation does not run as a middle edit");
                default:
                    throw new ArgumentOutOfRangeException(
                        "operation",
                        operation,
                        "this anchor operation does not run as a middle edit");
            }
        }

        /// <summary>
        /// A TAP/HOLD on a side lane is not a central ground note, which the
        /// middle-mouse Boundary mode refuses. The same rule applies here, so
        /// the anchor layer reports it instead of silently editing nothing.
        /// </summary>
        private static bool IsAnchorGroundBoundaryMissing(
            ChartNote note,
            AnchorOperation operation)
        {
            return operation == AnchorOperation.GroundBoundary &&
                   !IsEditableCentralGroundNote(note);
        }

        private static bool CanMoveSkyAreaEndpoint(
            ChartNote note,
            MiddleEditTimeSide timeSide)
        {
            if (note == null || note.Kind != NoteKind.SkyArea)
            {
                return false;
            }
            var width = timeSide == MiddleEditTimeSide.Tail
                ? note.EndWidth
                : note.StartWidth;
            // A zero-width endpoint has no lateral position to move.
            return width > AnchorMinimumEndpointWidth;
        }

        private static double AnchorProjectionTimeMs(
            ChartNote note,
            MiddleEditTimeSide timeSide)
        {
            return timeSide == MiddleEditTimeSide.Tail
                ? note.EndTimeMs
                : note.TimeMs;
        }

        private void UpdateAnchorDrag()
        {
            var state = _anchorDrag;
            if (state == null || state.Phase == AnchorPhase.Idle)
            {
                return;
            }
            if (!_hasPointer)
            {
                CancelAnchorDrag();
                return;
            }
            if (state.Phase == AnchorPhase.Pressed)
            {
                if (!TryResolveAnchorDirection(state))
                {
                    return;
                }
                state.Phase = AnchorPhase.Dragging;
                _session.BeginChange(AnchorHistoryLabel(state.Operation));
            }

            // The easing control does not read the pointer-on-time-plane state
            // (it snaps in canvas space against the anchor's own geometry), so
            // it short-circuits before the shared state is touched.
            if (state.Operation == AnchorOperation.DragEasing)
            {
                ApplyAnchorEasingDrag(state, state.PressCanvas);
                ShowAnchorDragReadout(state);
                RefreshAnchorDragVisuals();
                return;
            }

            // Whole-group SkyArea scaling bypasses the shared middle-edit state
            // entirely: it writes a group snapshot, not per-target deltas.
            if (IsGroupOperation(state.Operation))
            {
                ApplyGroupTimelineScale(state);
                ShowAnchorDragReadout(state);
                RefreshAnchorDragVisuals();
                return;
            }

            var shared = state.Shared;
            if (shared == null)
            {
                // Defensive: a group operation that reached this path without
                // building the multi-target state must not crash mid-drag.
                CancelAnchorDrag();
                return;
            }
            // Refresh the pointer state the shared bodies read.
            shared.CurrentPlaneX = shared.PressPlaneX;
            float currentPlaneX;
            if (shared.HasPlanePointer &&
                TryGetPointerNormalizedOnTimePlane(
                    Input.mousePosition,
                    shared.ProjectionTimeMs,
                    _playheadMs,
                    out currentPlaneX))
            {
                shared.CurrentPlaneX = currentPlaneX;
            }
            if (IsAnchorPlaneOperation(state.Operation))
            {
                PublishAnchorPlaneState(state.Operation, shared);
            }
            else
            {
                ClearAnchorPlaneState();
            }

            switch (state.Operation)
            {
                case AnchorOperation.GroundBoundary:
                case AnchorOperation.SkyBoundary:
                    ApplyBoundaryEdit(shared, _pointerLanePosition);
                    break;
                case AnchorOperation.StartEndTime:
                    ApplyStartEndEdit(shared, _pointerRawTime);
                    break;
                case AnchorOperation.Translate:
                    ApplyPositionEdit(shared, _pointerLanePosition);
                    break;
                case AnchorOperation.EndpointLateral:
                    ApplyAnchorEndpointLateral(
                        state,
                        AnchorEndpointLateralDelta(state));
                    break;
            }
            ShowAnchorDragReadout(state);
            RefreshAnchorDragVisuals();
        }

        /// <summary>
        /// Drags the easing value along the control's own three-state axis.
        ///
        /// The snap points come from the ANCHOR's stored geometry
        /// (<see cref="FalsumSelectionAnchor.SnapCenter"/> /
        /// <see cref="FalsumSelectionAnchor.SnapDirection"/> /
        /// <see cref="FalsumSelectionAnchor.SnapSpacing"/>), which is the exact
        /// geometry the builder painted the glyph with. Snapping against a
        /// second copy of the spacing would let the two drift, and then the
        /// handle would jump to a state it was never drawn at.
        ///
        /// Travel is projected onto the control's axis and rounded to the
        /// nearest slot, so half a spacing does nothing and a full spacing
        /// advances exactly one state. That is what makes the gesture read as
        /// moving the handle instead of scrubbing a number.
        ///
        /// The slot is an offset from the value at press time, so the drag is
        /// always relative -- the handle follows the pointer from wherever it
        /// was grabbed rather than snapping to an absolute screen position.
        /// </summary>
        private void ApplyAnchorEasingDrag(
            AnchorDragState state,
            Vector2 pressCanvas)
        {
            var note = state.Note;
            var anchor = state.Anchor;
            if (note == null ||
                anchor == null ||
                note.Kind != NoteKind.SkyArea ||
                !anchor.HasSnapGeometry)
            {
                return;
            }
            Vector2 currentCanvas;
            if (!_world.TryScreenToCanvas(
                    Input.mousePosition,
                    out currentCanvas))
            {
                return;
            }
            var travel = Vector2.Dot(
                currentCanvas - pressCanvas,
                anchor.SnapDirection);
            var slots = Mathf.RoundToInt(
                travel / anchor.SnapSpacing);
            if (slots == state.PressEasingSlots)
            {
                // Inside the same detent: leave the value alone, so a wobble
                // during a click cannot flip it.
                return;
            }
            state.PressEasingSlots = slots;
            var slot = Mathf.Clamp(
                state.PressEasingSlot + slots,
                -1,
                1);
            // Slot -> value is NOT slot+1: the value enum is Line=0, Ease out=1,
            // Ease in=2 while the visual slots are -1/0/+1. The inverse lives on
            // the model so the drag and the painter cannot disagree.
            var easing = FalsumSelectionAnchor.EasingSlotToValue(slot);
            // The handle still owns its own note field; only the detent
            // MEANING may be inverted for this side (ease in <-> ease out).
            var leftHandle = anchor.Edge == FalsumAnchorEdge.Left;
            WriteAnchorEasing(
                note,
                leftHandle,
                FalsumSkyAreaGroupScale.HandleValueToStored(
                    note,
                    leftHandle,
                    easing));
        }

        /// <summary>
        /// Writes the easing value and refreshes the visuals, without opening a
        /// session change -- the drag owns that transaction.
        /// </summary>
        private void WriteAnchorEasing(
            ChartNote note,
            bool leftSide,
            int easing)
        {
            if (note == null)
            {
                return;
            }
            easing = Mathf.Clamp(easing, 0, 2);
            if (leftSide)
            {
                if (note.LeftEasing == easing)
                {
                    return;
                }
                note.LeftEasing = easing;
            }
            else
            {
                if (note.RightEasing == easing)
                {
                    return;
                }
                note.RightEasing = easing;
            }
            ShowHint(
                L.Get(
                    leftSide
                        ? "falsum.hint.middleEdit.leftEasing"
                        : "falsum.hint.middleEdit.rightEasing",
                    ("easing", EasingName(easing))),
                1.0f);
        }

        /// <summary>
        /// The skyarea front/back midpoint is the one anchor that genuinely owns
        /// two axes. It spends a dead zone deciding which one this drag is, then
        /// commits to it for the rest of the drag. Every other anchor already
        /// knows its single operation, so it never reaches the decision.
        /// </summary>
        private bool TryResolveAnchorDirection(AnchorDragState state)
        {
            if (state.Operation != AnchorOperation.EndpointLateral)
            {
                // Single-operation anchors: nothing to decide, start dragging.
                return true;
            }

            var delta = (Vector2)Input.mousePosition - state.PressScreen;
            if (delta.sqrMagnitude < AnchorDirectionDeadZonePixels *
                AnchorDirectionDeadZonePixels)
            {
                return false;
            }
            if (Mathf.Abs(delta.y) > Mathf.Abs(delta.x))
            {
                // Vertical travel: hand the drag to the middle start/end body
                // for the rest of its life.
                state.Operation = AnchorOperation.StartEndTime;
                state.Shared.Mode = FalsumMiddleEditMode.StartEnd;
            }
            return true;
        }

        private static readonly float AnchorDirectionDeadZonePixels = 4.0f;

        /// <summary>
        /// The lateral delta for a skyarea endpoint move, expressed in the same
        /// normalized 0..1 track space the note's position fields use.
        /// </summary>
        private double AnchorEndpointLateralDelta(AnchorDragState state)
        {
            var shared = state.Shared;
            var raw = shared.HasPlanePointer
                ? (double)shared.CurrentPlaneX - shared.PressPlaneX
                : _pointerLanePosition - shared.PressLanePosition;
            var target = FindMiddleActiveTarget(shared);
            var grid = target != null && target.NativeGrid > 0
                ? target.NativeGrid
                : 4;
            return SnapNormalizedDelta(raw, grid);
        }

        /// <summary>
        /// THE ONE BESPOKE ANCHOR OPERATION.
        ///
        /// Moves exactly one skyarea endpoint's lateral position, keeping that
        /// endpoint's width from press time. The other endpoint is untouched,
        /// its time is untouched, and the note is NOT translated (translating
        /// was the reported bug: grabbing an edge handle shunted the whole
        /// note sideways).
        /// </summary>
        private void ApplyAnchorEndpointLateral(
            AnchorDragState state,
            double normalizedDelta)
        {
            var note = state.Note;
            if (note == null || note.Kind != NoteKind.SkyArea)
            {
                return;
            }
            if (normalizedDelta == 0.0)
            {
                return;
            }
            var tail = state.TimeSide == MiddleEditTimeSide.Tail;
            double center;
            double width;
            if (tail)
            {
                center = state.PressEndPos;
                width = state.PressEndWidth;
            }
            else
            {
                center = state.PressStartPos;
                width = state.PressStartWidth;
            }
            var half = width * 0.5;
            var moved = center + normalizedDelta;
            if (!_disableOutOfBoundsClamping)
            {
                // Keep the whole endpoint span inside 0..1 without changing the
                // span: clamp the CENTRE, not the width.
                var minimumCenter = half;
                var maximumCenter = 1.0 - half;
                if (maximumCenter >= minimumCenter)
                {
                    moved = Math.Max(
                        minimumCenter,
                        Math.Min(maximumCenter, moved));
                }
            }
            if (tail)
            {
                note.EndPos = moved;
                note.EndWidth = width;
            }
            else
            {
                note.StartPos = moved;
                note.StartWidth = width;
            }
        }

        /// <summary>
        /// Cycles a skyarea edge's easing on click: Line -> Ease out -> Ease in
        /// -> Line.
        /// </summary>
        private void CycleSkyAreaEasingFromAnchor(
            ChartNote note,
            FalsumSelectionAnchor anchor)
        {
            if (note == null ||
                note.Kind != NoteKind.SkyArea ||
                anchor == null)
            {
                return;
            }
            var leftSide = anchor.Edge == FalsumAnchorEdge.Left;
            _session.BeginChange(L.Get("falsum.history.toggleEasing"));
            if (leftSide)
            {
                note.LeftEasing = (note.LeftEasing + 1) % 3;
            }
            else
            {
                note.RightEasing = (note.RightEasing + 1) % 3;
            }
            if (_session.CommitChange())
            {
                _session.NotifyChanged();
                if (_host.NoteVisuals != null)
                {
                    _host.NoteVisuals.RefreshNoteVisual(note);
                }
                _host.UpdateChartPlaybackFrame(_playheadMs);
                RefreshPropertyPanel();
                ShowHint(
                    L.Get(
                        leftSide
                            ? "falsum.hint.anchor.easing"
                            : "falsum.hint.anchor.easingRight",
                        ("easing", EasingName(
                            leftSide
                                ? note.LeftEasing
                                : note.RightEasing))),
                    1.5f);
            }
            UpdateViews();
        }

        private void ToggleFlickDirectionFromAnchor(ChartNote note)
        {
            if (note == null || note.Kind != NoteKind.Flick)
            {
                return;
            }
            _session.BeginChange(L.Get("falsum.history.toggleFlickColor"));
            note.FlickDirection = note.FlickDirection == 16 ? 4 : 16;
            if (_session.CommitChange())
            {
                _session.NotifyChanged();
                if (_host.NoteVisuals != null)
                {
                    _host.NoteVisuals.RefreshNoteVisual(note);
                }
                _host.UpdateChartPlaybackFrame(_playheadMs);
                RefreshPropertyPanel();
                ShowHint(
                    L.Get(
                        "falsum.hint.flickColor",
                        ("color", L.Get(
                            note.FlickDirection == 16
                                ? "falsum.color.yellowLeft"
                                : "falsum.color.greenRight"))),
                    1.5f);
            }
            UpdateViews();
        }

        /// <summary>
        /// Rebuilds the note's box and its anchors from the live note data so
        /// the handles track the pointer during the drag instead of staying
        /// frozen where the drag started.
        /// </summary>
        private void RefreshAnchorDragVisuals()
        {
            var state = _anchorDrag;
            if (state == null || state.Note == null)
            {
                return;
            }
            var note = state.Note;
            if (_host.NoteVisuals != null)
            {
                _host.NoteVisuals.RefreshNoteVisual(note);
            }
            _world.SetSelectionOverlayVisible(true);
            _host.UpdateChartPlaybackFrame(_playheadMs);
            UpdateViews();
        }

        private void CommitAnchorDrag()
        {
            var state = _anchorDrag;
            if (state == null)
            {
                return;
            }
            _anchorDrag = null;
            ClearAnchorPlaneState();
            if (state.Phase != AnchorPhase.Dragging ||
                !_session.HasPendingEdit)
            {
                // Pressed-but-never-dragged. For the easing control that is a
                // CLICK, and a click still cycles the value -- dragging is the
                // new, optional gesture, not a replacement for the old one.
                if (state.Phase == AnchorPhase.Pressed &&
                    state.Operation == AnchorOperation.DragEasing &&
                    state.Note != null &&
                    state.Anchor != null)
                {
                    CycleSkyAreaEasingFromAnchor(state.Note, state.Anchor);
                    return;
                }
                UpdateViews();
                return;
            }
            if (ContainsNegativeMusicTime(new[] { state.Note }))
            {
                _session.CancelChange();
                RollbackGroupDrag(state);
                RefreshEditorVisuals();
                ShowNegativeMusicTimeWarning();
                return;
            }
            if (!_session.CommitChange())
            {
                UpdateViews();
                return;
            }
            _session.NotifyChanged();
            if (state.Note.Kind == NoteKind.SkyArea &&
                _host.NoteVisuals != null)
            {
                _host.NoteVisuals.RebuildSkyAreas();
                // The rebuild already used the group ids NotifyChanged
                // normalized, so the frame-end dirty flush must not repeat it.
                _skyGroupingDirty = false;
                _host.RefreshSkyPresentation(_playheadMs);
            }
            if (_host.NoteVisuals != null)
            {
                // A group drag touched several notes, not just the anchor's
                // own; refreshing only state.Note left the rest stale.
                if (IsGroupOperation(state.Operation) &&
                    state.GroupSnapshot != null)
                {
                    _host.NoteVisuals.RefreshNoteVisuals(
                        state.GroupSnapshot.Notes);
                }
                else
                {
                    _host.NoteVisuals.RefreshNoteVisual(state.Note);
                }
            }
            _host.UpdateChartPlaybackFrame(_playheadMs);
            RefreshPropertyPanel();
            UpdateViews();
        }

        /// <summary>
        /// Every frame, arms the anchor nearest the pointer (if any) so the
        /// overlay can show "this handle is grabbable" before the click lands.
        /// While dragging, the dragged anchor stays armed.
        /// </summary>
        private void UpdateAnchorHover()
        {
            // Nothing can be grabbed while the chart is playing: the mouse path
            // is skipped entirely then (see Update), so an armed handle would
            // only be decorative -- and arming it costs a panel hit test every
            // frame this method runs. Skipping keeps the playback frame free of
            // pointer work while the cursor moves.
            if (_host != null &&
                _host.Clock != null &&
                _host.Clock.IsPlaying)
            {
                if (_anchorHoverIndex != -1 || _anchorDrag != null)
                {
                    _anchorHoverIndex = -1;
                    _world.SetAnchorHighlight(-1, false);
                }
                return;
            }
            if (!_hasPointer ||
                _view.IsPointerOverUi(Input.mousePosition))
            {
                if (_anchorHoverIndex != -1 || _anchorDrag != null)
                {
                    _anchorHoverIndex = -1;
                    _world.SetAnchorHighlight(-1, false);
                }
                return;
            }
            if (_anchorDrag != null)
            {
                _world.SetAnchorHighlight(_anchorHoverIndex, true);
                return;
            }
            var index = (AnchorSingleSelection() != null ||
                         IsSkyAreaGroupSelection())
                ? _world.FindAnchorNear(
                    (Vector2)Input.mousePosition,
                    AnchorPickRadiusPixels * AnchorHoverRadiusScale)
                : -1;
            if (index != _anchorHoverIndex)
            {
                _anchorHoverIndex = index;
                _world.SetAnchorHighlight(index, false);
            }
        }

        /// <summary>
        /// Live readout during a drag, so the user can see the value the anchor
        /// is producing instead of guessing from the handle position. The
        /// readout follows the OPERATION, not the anchor's slot on the box, so
        /// dragging a boundary never shows a time that is not actually changing.
        /// </summary>
        private void ShowAnchorDragReadout(AnchorDragState state)
        {
            var note = state != null ? state.Note : null;
            if (note == null)
            {
                return;
            }
            switch (state.Operation)
            {
                case AnchorOperation.DragEasing:
                    ShowHint(
                        L.Get(
                            state.Anchor != null &&
                            state.Anchor.Edge == FalsumAnchorEdge.Left
                                ? "falsum.hint.anchor.easing"
                                : "falsum.hint.anchor.easingRight",
                            ("easing", EasingName(
                                state.Anchor != null &&
                                state.Anchor.Edge == FalsumAnchorEdge.Left
                                    ? note.LeftEasing
                                    : note.RightEasing))),
                        0.5f);
                    return;
                case AnchorOperation.StartEndTime:
                    ShowHint(
                        L.Get(
                            "falsum.hint.anchor.timeReadout",
                            ("time", note.TimeMs.ToString("F0")),
                            ("length", note.DurationMs.ToString("F0"))),
                        0.5f);
                    return;
                case AnchorOperation.GroundBoundary:
                    ShowHint(
                        L.Get(
                            "falsum.hint.anchor.laneReadout",
                            ("lane", note.StartLane.ToString()),
                            ("width", Mathf.Max(
                                1, note.WidthLanes).ToString())),
                        0.5f);
                    return;
                case AnchorOperation.GroupTimelineScale:
                    ShowHint(
                        L.Get(
                            "falsum.hint.anchor.groupLengthReadout",
                            ("length", GroupDuration(state)),
                            ("segments", state.GroupSnapshot != null
                                ? state.GroupSnapshot.Count
                                : 0)),
                        0.5f);
                    return;
                default:
                    ShowHint(
                        L.Get(
                            "falsum.hint.anchor.positionReadout",
                            ("position", note.StartPos.ToString("F3")),
                            ("width", note.StartWidth.ToString("F3"))),
                        0.5f);
                    return;
            }
        }

        /// <summary>Total time span of the drag group, in milliseconds.</summary>
        private static string GroupDuration(AnchorDragState state)
        {
            return FalsumSkyAreaGroupScale.TotalSpanMs(
                state != null ? state.GroupSnapshot : null).ToString("F0");
        }

        private void CancelAnchorDrag()
        {
            var state = _anchorDrag;
            if (state == null)
            {
                return;
            }
            _anchorDrag = null;
            _anchorHoverIndex = -1;
            ClearAnchorPlaneState();
            _world.SetAnchorHighlight(-1, false);
            if (state.Phase == AnchorPhase.Dragging &&
                _session.HasPendingEdit)
            {
                _session.CancelChange();
                // The group ops write the notes directly rather than through
                // the session's snapshot, so cancelling has to undo them here.
                RollbackGroupDrag(state);
                RefreshEditorVisuals();
                RefreshPropertyPanel();
            }
            UpdateViews();
        }

        private static string AnchorHistoryLabel(AnchorOperation operation)
        {
            switch (operation)
            {
                case AnchorOperation.StartEndTime:
                    return L.Get("falsum.history.anchorStartEnd");
                case AnchorOperation.EndpointLateral:
                    // A distinct label: this is the one operation the middle
                    // mouse cannot do, and the undo entry must not claim the
                    // drag retimed the note when it only moved one endpoint
                    // sideways.
                    return L.Get("falsum.history.anchorEndpointLateral");
                case AnchorOperation.Translate:
                    return L.Get("falsum.history.anchorMove");
                case AnchorOperation.DragEasing:
                    return L.Get("falsum.history.toggleEasing");
                case AnchorOperation.FlipFlickColor:
                    return L.Get("falsum.history.toggleFlickColor");
                case AnchorOperation.GroupTimelineScale:
                    return L.Get("falsum.history.skyAreaGroupLength");
                default:
                    return L.Get("falsum.history.anchorEdit");
            }
        }
    }
}
