using System;
using System.Collections.Generic;
using InFalsus.Gameplay.Core.Charts;
using InFalsus.Gameplay.Falsum.Core;
using UnityEngine;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    public sealed partial class FalsumChartEditor
    {
        private const float SkyAreaGroupSnapPixels = 12.0f;

        private enum SkyAreaSnapBoundary
        {
            Head,
            Tail,
            Either,
        }

        private bool SkyAreaGroupTimeSnapEnabled
        {
            get
            {
                return _preferences != null &&
                       _preferences.Editing != null &&
                       _preferences.Editing.SkyAreaGroupTimeSnap;
            }
        }

        private double ResolveSkyAreaPlacementSnapTime(double rawTime)
        {
            var gridTime = _world.SnapTime(rawTime);
            if (!SkyAreaGroupTimeSnapEnabled ||
                _chart == null ||
                _mode != EditorMode.PlaceSkyArea)
            {
                return gridTime;
            }
            var boundary = _skyStage == 0
                ? SkyAreaSnapBoundary.Head
                : SkyAreaSnapBoundary.Tail;
            var center = SnapPlaneCell(gridTime);
            return ResolveSkyAreaBoundarySnapTime(
                rawTime,
                center,
                SkyAreaPointWidth,
                boundary,
                gridTime);
        }

        private double ResolveSkyAreaBoundarySnapTime(
            double rawTime,
            double center,
            double width,
            SkyAreaSnapBoundary boundary,
            double fallbackTime)
        {
            double candidateTime;
            if (!TryResolveSkyAreaGroupSnap(
                    rawTime,
                    rawTime,
                    center,
                    width,
                    center,
                    width,
                    boundary,
                    null,
                    out candidateTime,
                    out _))
            {
                return fallbackTime;
            }
            return Math.Max(0.0, candidateTime);
        }

        private bool TryResolveSelectionSkyAreaGroupSnap(
            double rawDelta,
            out double snappedDelta)
        {
            snappedDelta = 0.0;
            var anchor = _selectionDragAnchor;
            if (!SkyAreaGroupTimeSnapEnabled ||
                anchor == null ||
                anchor.Kind != NoteKind.SkyArea)
            {
                return false;
            }

            DragState anchorState = null;
            for (var i = 0; i < _dragStates.Count; i++)
            {
                if (ReferenceEquals(_dragStates[i].Note, anchor))
                {
                    anchorState = _dragStates[i];
                    break;
                }
            }
            if (anchorState == null)
            {
                return false;
            }

            var desiredHead = Math.Max(
                0.0,
                anchorState.TimeMs + rawDelta);
            var desiredTail = Math.Max(
                0.0,
                anchorState.TimeMs +
                anchorState.DurationMs +
                rawDelta);
            double candidateTime;
            SkyAreaSnapBoundary candidateBoundary;
            if (!TryResolveSkyAreaGroupSnap(
                    desiredHead,
                    desiredTail,
                    anchor.StartPos,
                    anchor.StartWidth,
                    anchor.EndPos,
                    anchor.EndWidth,
                    SkyAreaSnapBoundary.Either,
                    _selected,
                    out candidateTime,
                    out candidateBoundary))
            {
                return false;
            }

            var originalTime = candidateBoundary ==
                               SkyAreaSnapBoundary.Head
                ? anchorState.TimeMs
                : anchorState.TimeMs + anchorState.DurationMs;
            snappedDelta = candidateTime - originalTime;
            return true;
        }

        private bool TryResolveMiddleSkyAreaGroupSnap(
            MiddleEditState state,
            double rawDelta,
            out double snappedDelta)
        {
            snappedDelta = 0.0;
            if (!SkyAreaGroupTimeSnapEnabled ||
                state == null ||
                state.Mode != FalsumMiddleEditMode.StartEnd)
            {
                return false;
            }
            var active = FindMiddleActiveTarget(state);
            if (active == null ||
                active.Note == null ||
                active.Note.Kind != NoteKind.SkyArea)
            {
                return false;
            }

            var desiredHead = Math.Max(
                0.0,
                active.TimeMs + rawDelta);
            var desiredTail = Math.Max(
                0.0,
                active.TimeMs + active.DurationMs + rawDelta);
            var boundary = state.TimeSide == MiddleEditTimeSide.Head
                ? SkyAreaSnapBoundary.Head
                : SkyAreaSnapBoundary.Tail;
            double candidateTime;
            SkyAreaSnapBoundary candidateBoundary;
            if (!TryResolveSkyAreaGroupSnap(
                    desiredHead,
                    desiredTail,
                    active.StartPos,
                    active.StartWidth,
                    active.EndPos,
                    active.EndWidth,
                    boundary,
                    state.Notes,
                    out candidateTime,
                    out candidateBoundary))
            {
                return false;
            }

            var originalTime = candidateBoundary ==
                               SkyAreaSnapBoundary.Head
                ? active.TimeMs
                : active.TimeMs + active.DurationMs;
            snappedDelta = candidateTime - originalTime;
            return true;
        }

        private bool TryResolveSkyAreaGroupSnap(
            double desiredHeadTime,
            double desiredTailTime,
            double headCenter,
            double headWidth,
            double tailCenter,
            double tailWidth,
            SkyAreaSnapBoundary boundary,
            IList<ChartNote> excludedNotes,
            out double candidateTime,
            out SkyAreaSnapBoundary candidateBoundary)
        {
            candidateTime = 0.0;
            candidateBoundary = SkyAreaSnapBoundary.Head;
            if (!SkyAreaGroupTimeSnapEnabled ||
                _chart == null ||
                _chart.Notes == null)
            {
                return false;
            }

            var evaluateHead =
                boundary != SkyAreaSnapBoundary.Tail;
            var evaluateTail =
                boundary != SkyAreaSnapBoundary.Head;
            var bestScreenDistance = float.MaxValue;
            var bestTimeDistance = double.MaxValue;
            var bestId = int.MaxValue;
            var found = false;

            for (var i = 0; i < _chart.Notes.Count; i++)
            {
                var note = _chart.Notes[i];
                if (note == null ||
                    note.Kind != NoteKind.SkyArea ||
                    IsSkyAreaSnapExcluded(note, excludedNotes))
                {
                    continue;
                }
                if (evaluateHead &&
                    FalsumSkyAreaGrouping.SpansOverlap(
                        headCenter,
                        headWidth,
                        note.EndPos,
                        note.EndWidth))
                {
                    ConsiderSkyAreaSnapCandidate(
                        note.EndTimeMs,
                        desiredHeadTime,
                        headCenter,
                        SkyAreaSnapBoundary.Head,
                        note.Id,
                        ref bestScreenDistance,
                        ref bestTimeDistance,
                        ref bestId,
                        ref candidateTime,
                        ref candidateBoundary,
                        ref found);
                }
                if (evaluateTail &&
                    FalsumSkyAreaGrouping.SpansOverlap(
                        tailCenter,
                        tailWidth,
                        note.StartPos,
                        note.StartWidth))
                {
                    ConsiderSkyAreaSnapCandidate(
                        note.TimeMs,
                        desiredTailTime,
                        tailCenter,
                        SkyAreaSnapBoundary.Tail,
                        note.Id,
                        ref bestScreenDistance,
                        ref bestTimeDistance,
                        ref bestId,
                        ref candidateTime,
                        ref candidateBoundary,
                        ref found);
                }
            }
            return found;
        }

        private void ConsiderSkyAreaSnapCandidate(
            double timeMs,
            double desiredTimeMs,
            double center,
            SkyAreaSnapBoundary boundary,
            int noteId,
            ref float bestScreenDistance,
            ref double bestTimeDistance,
            ref int bestId,
            ref double bestTime,
            ref SkyAreaSnapBoundary bestBoundary,
            ref bool found)
        {
            float candidateY;
            float desiredY;
            if (timeMs < 0.0 ||
                !TryGetSkyAreaSnapScreenY(
                    timeMs,
                    center,
                    out candidateY) ||
                !TryGetSkyAreaSnapScreenY(
                    desiredTimeMs,
                    center,
                    out desiredY))
            {
                return;
            }

            var screenDistance = Math.Abs(candidateY - desiredY);
            if (screenDistance >= SkyAreaGroupSnapPixels)
            {
                return;
            }
            var timeDistance = Math.Abs(timeMs - desiredTimeMs);
            var isBetter =
                !found ||
                screenDistance < bestScreenDistance - 0.001f ||
                (Math.Abs(
                    screenDistance - bestScreenDistance) <= 0.001f &&
                 (timeDistance < bestTimeDistance - 0.001 ||
                  (Math.Abs(timeDistance - bestTimeDistance) <= 0.001 &&
                   noteId < bestId)));
            if (!isBetter)
            {
                return;
            }
            found = true;
            bestScreenDistance = screenDistance;
            bestTimeDistance = timeDistance;
            bestId = noteId;
            bestTime = timeMs;
            bestBoundary = boundary;
        }

        private bool TryGetSkyAreaSnapScreenY(
            double timeMs,
            double center,
            out float screenY)
        {
            screenY = 0.0f;
            Vector2 screen;
            if (_world == null ||
                !_world.ScreenPositionAt(
                    timeMs,
                    (float)Math.Max(
                        0.0,
                        Math.Min(1.0, center)),
                    _playheadMs,
                    out screen))
            {
                return false;
            }
            screenY = screen.y;
            return true;
        }

        private static bool IsSkyAreaSnapExcluded(
            ChartNote note,
            IList<ChartNote> excludedNotes)
        {
            if (excludedNotes == null)
            {
                return false;
            }
            for (var i = 0; i < excludedNotes.Count; i++)
            {
                if (ReferenceEquals(excludedNotes[i], note))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
