using System;
using System.Collections.Generic;
using InFalsus.Gameplay.Core.Charts;
using InFalsus.Gameplay.Falsum.Core;
using InFalsus.Gameplay.Falsum.UI;
using UnityEngine;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    public enum FalsumMiddleEditMode
    {
        Boundary,
        StartEnd,
        Position,
    }

    public sealed partial class FalsumChartEditor
    {
        private enum MiddleEditPhase
        {
            Idle,
            Pressed,
            Dragging,
        }

        private enum MiddleEditSide
        {
            Left,
            Right,
        }

        private enum MiddleEditTimeSide
        {
            Head,
            Tail,
        }

        private sealed class MiddleEditTarget
        {
            public ChartNote Note;
            public double TimeMs;
            public double DurationMs;
            public int StartLane;
            public int WidthLanes;
            public double StartPos;
            public double EndPos;
            public double StartWidth;
            public double EndWidth;
            public int FlickDirection;
            public MiddleEditTimeSide TimeSide;
            public int NativeGrid;
            public int PositionGrid;
            public bool Crossed;
        }

        private sealed class MiddleEditState
        {
            public MiddleEditPhase Phase;
            public FalsumMiddleEditMode Mode;
            public ChartNote ActiveNote;
            public MiddleEditSide Side;
            public MiddleEditTimeSide TimeSide;
            public Vector2 PressScreen;
            public float PressLanePosition;
            public float PressPlaneX;
            public float CurrentPlaneX;
            public bool HasPlanePointer;
            public double ProjectionTimeMs;
            public double PressRawTime;
            public int Grid;
            public bool GridWasAdjusted;
            public int SkippedCount;
            public readonly List<MiddleEditTarget> Targets =
                new List<MiddleEditTarget>();
            public readonly List<ChartNote> Notes =
                new List<ChartNote>();
            public readonly List<int> SelectionIds = new List<int>();
        }

        private FalsumMiddleEditMode _middleEditMode =
            FalsumMiddleEditMode.Boundary;
        private const double MiddleMinimumDurationMs = 1.0;
        private const double SkyMiddleDoubleClickSeconds = 0.32;
        private bool _middleEditAffectsAllSelected = true;
        private MiddleEditState _middleEdit;
        private double _middleEditVisualRefreshAt;
        private ChartNote _lastSkyMiddleBoundaryNote;
        private double _lastSkyMiddleBoundaryClickTime;
        private MiddleEditSide _lastSkyMiddleBoundarySide;

        public FalsumMiddleEditMode MiddleEditMode
        {
            get { return _middleEditMode; }
            set
            {
                if (IsMiddleEditActive)
                {
                    return;
                }
                _middleEditMode = value;
                RefreshMiddleEditModeStatus();
            }
        }

        public bool MiddleEditAffectsAllSelected
        {
            get { return _middleEditAffectsAllSelected; }
            set { _middleEditAffectsAllSelected = value; }
        }

        public string DebugMiddleEditMode
        {
            get
            {
                return _middleEditMode +
                       (_middleEditAffectsAllSelected
                           ? "/all"
                           : "/single");
            }
        }

        public string DebugMiddleEditStatus
        {
            get
            {
                return _view != null
                    ? _view.DebugMiddleEditStatus
                    : string.Empty;
            }
        }

        public string DebugRunMiddleEditChecks()
        {
            var originalText = ChartWriter.Write(
                _session.Chart,
                _session.DelayMs,
                _manifest.SongId,
                _currentDifficulty);
            var original = ChartParser.Parse(
                originalText.Replace("\r\n", "\n").Split('\n'),
                "<falsum-middle-edit-check>");
            var originalPlayhead = _playheadMs;
            var issues = new List<string>();

            MiddleEditMode = FalsumMiddleEditMode.StartEnd;
            if (MiddleEditMode != FalsumMiddleEditMode.StartEnd)
            {
                issues.Add("mode_interface");
            }
            MiddleEditMode = FalsumMiddleEditMode.Boundary;

            var easingSky = new ChartNote
            {
                Kind = NoteKind.SkyArea,
                TimeMs = 1000.0,
                DurationMs = 500.0,
                StartPos = 0.4,
                StartWidth = 0.2,
                EndPos = 0.6,
                EndWidth = 0.2,
                LeftEasing = 0,
                RightEasing = 0,
            };
            var easingState = new MiddleEditState
            {
                Mode = FalsumMiddleEditMode.Boundary,
                ActiveNote = easingSky,
                Side = MiddleEditSide.Left,
            };
            ResetSkyBoundaryDoubleClickTracking();
            if (TryCycleSkyBoundaryEasingOnDoubleClick(easingState) ||
                !TryCycleSkyBoundaryEasingOnDoubleClick(easingState) ||
                easingSky.LeftEasing != 1)
            {
                issues.Add("sky_left_middle_double_click_easing");
            }
            easingState.Side = MiddleEditSide.Right;
            ResetSkyBoundaryDoubleClickTracking();
            if (TryCycleSkyBoundaryEasingOnDoubleClick(easingState) ||
                !TryCycleSkyBoundaryEasingOnDoubleClick(easingState) ||
                easingSky.RightEasing != 1)
            {
                issues.Add("sky_right_middle_double_click_easing");
            }
            ResetSkyBoundaryDoubleClickTracking();
            _session.CancelChange();

            var centralTap = new ChartNote
            {
                Kind = NoteKind.Tap,
                StartLane = 2,
                WidthLanes = 2,
            };
            var sideTap = new ChartNote
            {
                Kind = NoteKind.Tap,
                StartLane = 0,
                WidthLanes = 1,
            };
            if (!IsEditableCentralGroundNote(centralTap) ||
                IsEditableCentralGroundNote(sideTap))
            {
                issues.Add("ground_lane_filter");
            }

            var groundTarget = CreateMiddleDebugTarget(
                centralTap,
                MiddleEditTimeSide.Head);
            ApplyGroundBoundary(
                groundTarget,
                MiddleEditSide.Left,
                -5);
            if (centralTap.StartLane != 1 ||
                centralTap.WidthLanes != 3)
            {
                issues.Add("ground_left_clamp");
            }
            var crossingGround = new ChartNote
            {
                Kind = NoteKind.Tap,
                StartLane = 3,
                WidthLanes = 1,
            };
            groundTarget = CreateMiddleDebugTarget(
                crossingGround,
                MiddleEditTimeSide.Head);
            ApplyGroundBoundary(
                groundTarget,
                MiddleEditSide.Right,
                -5);
            if (crossingGround.StartLane != 1 ||
                crossingGround.WidthLanes != 2 ||
                !groundTarget.Crossed)
            {
                issues.Add("ground_boundary_crossing");
            }

            var crossingFlick = new ChartNote
            {
                Kind = NoteKind.Flick,
                StartPos = 0.5,
                StartWidth = 0.2,
                StartCellCount = 24,
                EndCellCount = 24,
                FlickDirection = 4,
            };
            var crossingTarget = CreateMiddleDebugTarget(
                crossingFlick,
                MiddleEditTimeSide.Head);
            ApplySkyBoundary(
                crossingTarget,
                MiddleEditSide.Left,
                0.25,
                24,
                false,
                true);
            if (Math.Abs(crossingFlick.StartPos - 0.625) > 0.0001 ||
                Math.Abs(crossingFlick.StartWidth - 0.05) > 0.0001 ||
                crossingFlick.FlickDirection != 16)
            {
                issues.Add(
                    "flick_boundary_crossing:" +
                    crossingFlick.StartPos.ToString("0.#####") +
                    "," +
                    crossingFlick.StartWidth.ToString("0.#####") +
                    "," +
                    crossingFlick.FlickDirection);
            }

            var crossingSky = new ChartNote
            {
                Kind = NoteKind.SkyArea,
                StartPos = 0.5,
                StartWidth = 0.2,
                StartCellCount = 24,
                EndPos = 0.5,
                EndWidth = 0.2,
                EndCellCount = 24,
            };
            var crossingSkyTarget = CreateMiddleDebugTarget(
                crossingSky,
                MiddleEditTimeSide.Head);
            ApplySkyBoundary(
                crossingSkyTarget,
                MiddleEditSide.Left,
                0.3,
                24,
                false,
                true);
            var crossedWidth = crossingSky.StartWidth;
            ApplySkyBoundary(
                crossingSkyTarget,
                MiddleEditSide.Left,
                0.4,
                24,
                false,
                true);
            var firstCrossedWidth =
                SnapNormalizedDelta(0.3, 24) - 0.2;
            var secondCrossedWidth =
                SnapNormalizedDelta(0.4, 24) - 0.2;
            if (Math.Abs(crossedWidth - firstCrossedWidth) >
                    0.0001 ||
                Math.Abs(
                    crossingSky.StartWidth -
                    secondCrossedWidth) > 0.0001 ||
                !crossingSkyTarget.Crossed)
            {
                issues.Add("sky_boundary_crossing_continues");
            }
            var crossedFocusState = new MiddleEditState
            {
                Phase = MiddleEditPhase.Dragging,
                Mode = FalsumMiddleEditMode.Boundary,
                ActiveNote = crossingSky,
                Side = MiddleEditSide.Left,
                TimeSide = MiddleEditTimeSide.Head,
                Grid = 24,
            };
            crossedFocusState.Targets.Add(crossingSkyTarget);
            if (Math.Abs(
                    MiddleBoundaryFocusNormalized(
                        crossedFocusState,
                        crossingSkyTarget) -
                    (crossingSky.StartPos +
                     crossingSky.StartWidth * 0.5)) >
                0.0001)
            {
                issues.Add("crossed_boundary_arrow");
            }

            var noJumpFlick = new ChartNote
            {
                Kind = NoteKind.Flick,
                StartPos = 0.5,
                StartWidth = 0.2,
                StartCellCount = 24,
                EndCellCount = 24,
            };
            _middleEdit = new MiddleEditState
            {
                Phase = MiddleEditPhase.Dragging,
                Mode = FalsumMiddleEditMode.Boundary,
                ActiveNote = noJumpFlick,
                Side = MiddleEditSide.Left,
                TimeSide = MiddleEditTimeSide.Head,
                PressLanePosition = 0.5f,
                Grid = 24,
            };
            _middleEdit.Targets.Add(CreateMiddleDebugTarget(
                noJumpFlick,
                MiddleEditTimeSide.Head));
            _pointerLanePosition = 0.5f;
            ApplyMiddleBoundaryEdit();
            if (Math.Abs(noJumpFlick.StartPos - 0.5) > 0.0001 ||
                Math.Abs(noJumpFlick.StartWidth - 0.2) > 0.0001)
            {
                issues.Add("boundary_press_jump");
            }
            _middleEdit = null;

            var boundaryFlickA = new ChartNote
            {
                Kind = NoteKind.Flick,
                StartPos = 0.5,
                StartWidth = 0.2,
                StartCellCount = 16,
                EndCellCount = 16,
            };
            var boundaryFlickB = new ChartNote
            {
                Kind = NoteKind.Flick,
                StartPos = 0.5,
                StartWidth = 0.2,
                StartCellCount = 12,
                EndCellCount = 12,
            };
            _middleEdit = new MiddleEditState
            {
                Phase = MiddleEditPhase.Dragging,
                Mode = FalsumMiddleEditMode.Boundary,
                ActiveNote = boundaryFlickA,
                Side = MiddleEditSide.Left,
                TimeSide = MiddleEditTimeSide.Head,
                PressLanePosition = 0.5f,
                Grid = 16,
            };
            _middleEdit.Targets.Add(CreateMiddleDebugTarget(
                boundaryFlickA,
                MiddleEditTimeSide.Head));
            _middleEdit.Targets.Add(CreateMiddleDebugTarget(
                boundaryFlickB,
                MiddleEditTimeSide.Head));
            _pointerLanePosition = 0.6f;
            ApplyMiddleBoundaryEdit();
            if (Math.Abs(
                    boundaryFlickA.StartPos - 0.5625) > 0.0001 ||
                boundaryFlickA.StartCellCount != 16 ||
                boundaryFlickA.EndCellCount != 16 ||
                Math.Abs(
                    boundaryFlickB.StartPos -
                    (0.5 + 1.0 / 24.0)) > 0.0001 ||
                boundaryFlickB.StartCellCount != 12 ||
                boundaryFlickB.EndCellCount != 12)
            {
                issues.Add("boundary_per_target_grid");
            }
            _middleEdit = null;

            var tailSky = new ChartNote
            {
                Kind = NoteKind.SkyArea,
                StartPos = 0.2,
                StartWidth = 0.2,
                StartCellCount = 24,
                EndPos = 0.6,
                EndWidth = 0.2,
                EndCellCount = 24,
            };
            var tailSkyTarget = CreateMiddleDebugTarget(
                tailSky,
                MiddleEditTimeSide.Tail);
            ApplySkyBoundary(
                tailSkyTarget,
                MiddleEditSide.Right,
                -0.5,
                24,
                false,
                true);
            if (Math.Abs(tailSky.StartPos - 0.2) > 0.0001 ||
                Math.Abs(tailSky.StartWidth - 0.2) > 0.0001 ||
                Math.Abs(tailSky.EndPos - 0.35) > 0.0001 ||
                Math.Abs(
                    tailSky.EndWidth -
                    0.3) > 0.0001)
            {
                issues.Add("sky_tail_boundary");
            }

            var gridTargets = new List<MiddleEditTarget>
            {
                CreateMiddleDebugTarget(
                    new ChartNote
                    {
                        Kind = NoteKind.Flick,
                        StartCellCount = 24,
                        EndCellCount = 24,
                    },
                    MiddleEditTimeSide.Head),
                CreateMiddleDebugTarget(
                    new ChartNote
                    {
                        Kind = NoteKind.SkyArea,
                        StartCellCount = 36,
                        EndCellCount = 24,
                    },
                    MiddleEditTimeSide.Head),
            };
            if (ComputeMiddleEditGrid(
                    FalsumMiddleEditMode.Position,
                    gridTargets,
                    gridTargets[0].Note) != 72)
            {
                issues.Add("middle_grid_lcm");
            }
            var boundaryActive = new ChartNote
            {
                Kind = NoteKind.SkyArea,
                StartPos = 0.3,
                StartWidth = 0.1,
                StartCellCount = 36,
                EndPos = 0.7,
                EndWidth = 0.1,
                EndCellCount = 24,
            };
            var boundaryTail = CreateMiddleDebugTarget(
                boundaryActive,
                MiddleEditTimeSide.Tail);
            var boundaryHead = CreateMiddleDebugTarget(
                boundaryActive,
                MiddleEditTimeSide.Head);
            var boundaryTargets = new List<MiddleEditTarget>
            {
                CreateMiddleDebugTarget(
                    new ChartNote
                    {
                        Kind = NoteKind.Flick,
                        StartCellCount = 12,
                        EndCellCount = 12,
                    },
                    MiddleEditTimeSide.Head),
                boundaryTail,
                boundaryHead,
            };
            if (ComputeMiddleEditGrid(
                    FalsumMiddleEditMode.Boundary,
                    boundaryTargets,
                    boundaryActive) != 24 ||
                ComputeMiddleEditGrid(
                    FalsumMiddleEditMode.Boundary,
                    new[] { boundaryHead },
                    boundaryActive) != 36)
            {
                issues.Add("boundary_native_grid");
            }
            var adjustedBoundary = new ChartNote
            {
                Kind = NoteKind.SkyArea,
                StartPos = 0.3,
                StartWidth = 0.1,
                StartCellCount = 36,
                EndPos = 0.7,
                EndWidth = 0.1,
                EndCellCount = 24,
            };
            var adjustedBoundaryTarget =
                CreateMiddleDebugTarget(
                    adjustedBoundary,
                    MiddleEditTimeSide.Tail);
            ApplySkyBoundary(
                adjustedBoundaryTarget,
                MiddleEditSide.Right,
                0.0,
                8,
                true,
                true);
            if (adjustedBoundary.StartCellCount != 36 ||
                adjustedBoundary.EndCellCount != 8)
            {
                issues.Add("boundary_adjusted_grid_write");
            }
            if (StepMiddleEditGrid(16, -1) != 12 ||
                StepMiddleEditGrid(16, 1) != 24 ||
                StepMiddleEditGrid(100, 1) != 100 ||
                StepMiddleEditGrid(1, -1) != 12)
            {
                issues.Add("middle_grid_step");
            }

            var positionFlick = new ChartNote
            {
                Kind = NoteKind.Flick,
                StartPos = 0.2,
                StartWidth = 0.2,
                StartCellCount = 36,
                EndCellCount = 36,
            };
            var positionSky = new ChartNote
            {
                Kind = NoteKind.SkyArea,
                TimeMs = 1000.0,
                DurationMs = 500.0,
                StartPos = 0.5,
                StartWidth = 0.1,
                StartCellCount = 24,
                EndPos = 0.6,
                EndWidth = 0.1,
                EndCellCount = 24,
            };
            var positionTargets = new List<MiddleEditTarget>
            {
                CreateMiddleDebugTarget(
                    positionFlick,
                    MiddleEditTimeSide.Head),
                CreateMiddleDebugTarget(
                    positionSky,
                    MiddleEditTimeSide.Head),
            };
            _middleEdit = new MiddleEditState
            {
                Phase = MiddleEditPhase.Dragging,
                Mode = FalsumMiddleEditMode.Position,
                ActiveNote = positionFlick,
                Side = MiddleEditSide.Left,
                TimeSide = MiddleEditTimeSide.Head,
                PressLanePosition = 0.5f,
                Grid = 24,
            };
            _middleEdit.Targets.AddRange(positionTargets);
            _middleEdit.Notes.Add(positionFlick);
            _middleEdit.Notes.Add(positionSky);
            _pointerLanePosition = 0.7f;
            ApplyMiddlePositionEdit();
            var positionDelta = SnapNormalizedDelta(0.2, 24);
            if (Math.Abs(
                    positionFlick.StartPos -
                    (0.2 + positionDelta)) > 0.0001 ||
                Math.Abs(
                    positionSky.StartPos -
                    (0.5 + positionDelta)) > 0.0001 ||
                Math.Abs(
                    positionSky.EndPos -
                    (0.6 + positionDelta)) > 0.0001 ||
                Math.Abs(positionFlick.StartWidth - 0.2) > 0.0001 ||
                positionFlick.StartCellCount != 36 ||
                positionFlick.EndCellCount != 36 ||
                Math.Abs(positionSky.StartWidth - 0.1) > 0.0001 ||
                Math.Abs(positionSky.EndWidth - 0.1) > 0.0001)
            {
                issues.Add("position_preserve_width");
            }

            var gridLockFlick = new ChartNote
            {
                Kind = NoteKind.Flick,
                StartPos = 0.25,
                StartWidth = 0.1,
                StartCellCount = 24,
                EndCellCount = 24,
            };
            var gridLockSky = new ChartNote
            {
                Kind = NoteKind.SkyArea,
                StartPos = 0.5,
                StartWidth = 0.1,
                StartCellCount = 36,
                EndPos = 0.7,
                EndWidth = 0.1,
                EndCellCount = 36,
            };
            _middleEdit = new MiddleEditState
            {
                Phase = MiddleEditPhase.Dragging,
                Mode = FalsumMiddleEditMode.Position,
                ActiveNote = gridLockFlick,
                Side = MiddleEditSide.Left,
                TimeSide = MiddleEditTimeSide.Head,
                PressLanePosition = 0.5f,
                Grid = 72,
            };
            _middleEdit.Targets.Add(CreateMiddleDebugTarget(
                gridLockFlick,
                MiddleEditTimeSide.Head));
            _middleEdit.Targets.Add(CreateMiddleDebugTarget(
                gridLockSky,
                MiddleEditTimeSide.Head));
            _pointerLanePosition = 0.5f;
            ApplyMiddlePositionEdit();
            if (gridLockFlick.StartCellCount != 24 ||
                gridLockSky.StartCellCount != 36 ||
                gridLockSky.EndCellCount != 36)
            {
                issues.Add("position_grid_noop_write");
            }
            _middleEdit = null;

            var edgeFlick = new ChartNote
            {
                Kind = NoteKind.Flick,
                StartPos = 0.85,
                StartWidth = 0.1,
                StartCellCount = 24,
                EndCellCount = 24,
            };
            _middleEdit = new MiddleEditState
            {
                Phase = MiddleEditPhase.Dragging,
                Mode = FalsumMiddleEditMode.Position,
                ActiveNote = edgeFlick,
                Side = MiddleEditSide.Left,
                TimeSide = MiddleEditTimeSide.Head,
                PressLanePosition = 0.5f,
                Grid = 24,
            };
            _middleEdit.Targets.Add(CreateMiddleDebugTarget(
                edgeFlick,
                MiddleEditTimeSide.Head));
            _pointerLanePosition = 1.0f;
            ApplyMiddlePositionEdit();
            if (Math.Abs(edgeFlick.StartPos - 0.95) > 0.0001 ||
                Math.Abs(edgeFlick.StartWidth - 0.1) > 0.0001)
            {
                issues.Add("position_group_clamp");
            }
            _middleEdit = null;

            var holdNote = new ChartNote
            {
                Kind = NoteKind.Hold,
                TimeMs = 1000.0,
                DurationMs = 500.0,
                StartLane = 2,
                WidthLanes = 1,
            };
            var holdTarget = CreateMiddleDebugTarget(
                holdNote,
                MiddleEditTimeSide.Head);
            var holdPlaneState = new MiddleEditState
            {
                Mode = FalsumMiddleEditMode.StartEnd,
                TimeSide = MiddleEditTimeSide.Tail,
            };
            if (Math.Abs(
                    MiddlePlaneTime(holdPlaneState, holdTarget) -
                    holdNote.EndTimeMs) > 0.0001)
            {
                issues.Add("hold_tail_plane_time");
            }
            var timeStep = _grid.StepMs(_chart, holdNote.TimeMs);
            _playheadMs = 4321.0;
            _pointerRawTime =
                holdNote.TimeMs + timeStep * 2.3;
            _middleEdit = new MiddleEditState
            {
                Phase = MiddleEditPhase.Dragging,
                Mode = FalsumMiddleEditMode.StartEnd,
                ActiveNote = holdNote,
                Side = MiddleEditSide.Left,
                TimeSide = MiddleEditTimeSide.Head,
                PressRawTime = holdNote.TimeMs,
                Grid = 4,
            };
            _middleEdit.Targets.Add(holdTarget);
            _middleEdit.Notes.Add(holdNote);
            ApplyMiddleStartEndEdit();
            if (Math.Abs(holdNote.TimeMs - 1000.0) < 0.0001 ||
                Math.Abs(_playheadMs - 4321.0) > 0.0001)
            {
                issues.Add("start_end_no_playhead_move");
            }
            var expectedHead = IntegerTimeMs(_world.SnapTime(
                1000.0 + timeStep * 2.3,
                1000.0));
            if (Math.Abs(holdNote.TimeMs - expectedHead) > 0.001)
            {
                issues.Add("start_end_snap");
            }

            _middleEdit.TimeSide = MiddleEditTimeSide.Tail;
            _middleEdit.PressRawTime = holdNote.EndTimeMs;
            _pointerRawTime = -10000.0;
            ApplyMiddleStartEndEdit();
            var tailGap = MiddleMinimumDurationMs;
            if (holdNote.DurationMs < tailGap - 0.001)
            {
                issues.Add("start_end_tail_clamp");
            }
            _middleEdit = null;

            var shortHold = new ChartNote
            {
                Kind = NoteKind.Hold,
                TimeMs = 1000.0,
                DurationMs = 100.0,
                StartLane = 2,
                WidthLanes = 1,
            };
            _middleEdit = new MiddleEditState
            {
                Phase = MiddleEditPhase.Dragging,
                Mode = FalsumMiddleEditMode.StartEnd,
                ActiveNote = shortHold,
                Side = MiddleEditSide.Left,
                TimeSide = MiddleEditTimeSide.Tail,
                PressRawTime = shortHold.EndTimeMs,
                Grid = 4,
            };
            _middleEdit.Targets.Add(CreateMiddleDebugTarget(
                shortHold,
                MiddleEditTimeSide.Tail));
            _middleEdit.Notes.Add(shortHold);
            _pointerRawTime = shortHold.EndTimeMs + 2.0;
            ApplyMiddleStartEndEdit();
            if (Math.Abs(shortHold.DurationMs - 100.0) > 0.001)
            {
                issues.Add("short_hold_not_step_sized");
            }
            _middleEdit = null;

            var timeSky = new ChartNote
            {
                Kind = NoteKind.SkyArea,
                TimeMs = 2000.0,
                DurationMs = 600.0,
                StartPos = 0.25,
                StartWidth = 0.2,
                EndPos = 0.65,
                EndWidth = 0.3,
            };
            var savedSkyStart = timeSky.StartPos;
            var savedSkyEnd = timeSky.EndPos;
            var savedSkyStartWidth = timeSky.StartWidth;
            var savedSkyEndWidth = timeSky.EndWidth;
            _middleEdit = new MiddleEditState
            {
                Phase = MiddleEditPhase.Dragging,
                Mode = FalsumMiddleEditMode.StartEnd,
                ActiveNote = timeSky,
                Side = MiddleEditSide.Left,
                TimeSide = MiddleEditTimeSide.Tail,
                PressRawTime = timeSky.EndTimeMs,
                Grid = 4,
            };
            _middleEdit.Targets.Add(CreateMiddleDebugTarget(
                timeSky,
                MiddleEditTimeSide.Tail));
            _middleEdit.Notes.Add(timeSky);
            _pointerRawTime =
                timeSky.EndTimeMs + _grid.StepMs(
                    _chart,
                    timeSky.EndTimeMs) * 2.2;
            ApplyMiddleStartEndEdit();
            if (Math.Abs(timeSky.StartPos - savedSkyStart) > 0.0001 ||
                Math.Abs(timeSky.EndPos - savedSkyEnd) > 0.0001 ||
                Math.Abs(
                    timeSky.StartWidth -
                    savedSkyStartWidth) > 0.0001 ||
                Math.Abs(
                    timeSky.EndWidth -
                    savedSkyEndWidth) > 0.0001)
            {
                issues.Add("start_end_sky_horizontal");
            }
            _middleEdit = null;

            var preservedSkyEnd = timeSky.EndTimeMs;
            _middleEdit = new MiddleEditState
            {
                Phase = MiddleEditPhase.Dragging,
                Mode = FalsumMiddleEditMode.StartEnd,
                ActiveNote = timeSky,
                Side = MiddleEditSide.Left,
                TimeSide = MiddleEditTimeSide.Head,
                PressRawTime = timeSky.TimeMs,
                Grid = 4,
            };
            _middleEdit.Targets.Add(CreateMiddleDebugTarget(
                timeSky,
                MiddleEditTimeSide.Head));
            _middleEdit.Notes.Add(timeSky);
            _pointerRawTime =
                timeSky.TimeMs + _grid.StepMs(
                    _chart,
                    timeSky.TimeMs) * 2.2;
            ApplyMiddleStartEndEdit();
            if (Math.Abs(timeSky.EndTimeMs - preservedSkyEnd) >
                0.001)
            {
                issues.Add("start_end_sky_head_keeps_end");
            }
            _middleEdit = null;

            var propertySky = new ChartNote
            {
                Kind = NoteKind.SkyArea,
                TimeMs = 5000.0,
                DurationMs = 600.0,
            };
            var propertySkyEnd = propertySky.EndTimeMs;
            SetSkyAreaStartTimePreservingEnd(
                propertySky,
                4700.0);
            if (Math.Abs(propertySky.TimeMs - 4700.0) > 0.001 ||
                Math.Abs(propertySky.EndTimeMs - propertySkyEnd) >
                0.001)
            {
                issues.Add("sky_property_start_keeps_end");
            }

            var planeFlick = new ChartNote
            {
                Kind = NoteKind.Flick,
                TimeMs = 2500.0,
                StartPos = 0.5,
                StartWidth = 0.25,
                StartCellCount = 16,
                EndCellCount = 16,
            };
            var planeFlickTarget = CreateMiddleDebugTarget(
                planeFlick,
                MiddleEditTimeSide.Head);
            _playheadMs = planeFlick.TimeMs;
            Vector2 planeHandleScreen;
            float projectedPlaneX;
            if (!_world.TryGetMiddleHandleScreenPosition(
                    planeFlick,
                    false,
                    true,
                    _playheadMs,
                    out planeHandleScreen) ||
                !_world.TryGetPointerNormalizedOnTimePlane(
                    planeHandleScreen,
                    planeFlick.TimeMs,
                    _playheadMs,
                    out projectedPlaneX) ||
                Math.Abs(projectedPlaneX - 0.625f) > 0.001f)
            {
                issues.Add("middle_time_plane_projection");
            }
            var boundaryPlaneState = new MiddleEditState
            {
                Phase = MiddleEditPhase.Dragging,
                Mode = FalsumMiddleEditMode.Boundary,
                ActiveNote = planeFlick,
                Side = MiddleEditSide.Right,
                TimeSide = MiddleEditTimeSide.Head,
                Grid = 16,
            };
            boundaryPlaneState.Targets.Add(planeFlickTarget);
            _middleEdit = boundaryPlaneState;
            FalsumModePlaneKind planeKind;
            string planePrompt;
            string planeModeLabel;
            string planeKeyHint;
            int planeDivisions;
            int planeFocusLine;
            double planeTime;
            if (!TryGetMiddlePlaneState(
                    out planeKind,
                    out planePrompt,
                    out planeModeLabel,
                    out planeKeyHint,
                    out planeDivisions,
                    out planeFocusLine,
                    out planeTime) ||
                planeFocusLine != GridLineAt(0.625f, 16))
            {
                issues.Add("boundary_arrow_edge");
            }

            var planeHold = new ChartNote
            {
                Kind = NoteKind.Hold,
                TimeMs = 1000.0,
                DurationMs = 200.0,
                StartLane = 2,
                WidthLanes = 1,
            };
            var planeHoldTarget = CreateMiddleDebugTarget(
                planeHold,
                MiddleEditTimeSide.Tail);
            _middleEdit = new MiddleEditState
            {
                Phase = MiddleEditPhase.Dragging,
                Mode = FalsumMiddleEditMode.StartEnd,
                ActiveNote = planeHold,
                Side = MiddleEditSide.Left,
                TimeSide = MiddleEditTimeSide.Tail,
                Grid = 4,
            };
            _middleEdit.Targets.Add(planeHoldTarget);
            if (TryGetMiddlePlaneState(
                    out planeKind,
                    out planePrompt,
                    out planeModeLabel,
                    out planeKeyHint,
                    out planeDivisions,
                    out planeFocusLine,
                    out planeTime) ||
                TryGetMiddlePlaneTime(out planeTime) ||
                ShouldDisplayMiddlePlane(
                    FalsumMiddleEditMode.StartEnd,
                    NoteKind.Hold))
            {
                issues.Add("hold_start_end_plane_hidden");
            }

            var boundaryGroundKinds = new[]
            {
                NoteKind.Tap,
                NoteKind.Hold,
            };
            for (var i = 0; i < boundaryGroundKinds.Length; i++)
            {
                var groundKind = boundaryGroundKinds[i];
                var planeGround = new ChartNote
                {
                    Kind = groundKind,
                    TimeMs = 1000.0,
                    DurationMs = groundKind == NoteKind.Hold
                        ? 200.0
                        : 0.0,
                    StartLane = 2,
                    WidthLanes = 1,
                };
                var planeGroundTarget = CreateMiddleDebugTarget(
                    planeGround,
                    MiddleEditTimeSide.Head);
                _middleEdit = new MiddleEditState
                {
                    Phase = MiddleEditPhase.Dragging,
                    Mode = FalsumMiddleEditMode.Boundary,
                    ActiveNote = planeGround,
                    Side = MiddleEditSide.Left,
                    TimeSide = MiddleEditTimeSide.Head,
                    Grid = 4,
                };
                _middleEdit.Targets.Add(planeGroundTarget);
                if (TryGetMiddlePlaneState(
                        out planeKind,
                        out planePrompt,
                        out planeModeLabel,
                        out planeKeyHint,
                        out planeDivisions,
                        out planeFocusLine,
                        out planeTime) ||
                    TryGetMiddlePlaneTime(out planeTime) ||
                    ShouldDisplayMiddlePlane(
                        FalsumMiddleEditMode.Boundary,
                        groundKind))
                {
                    issues.Add(
                        "ground_boundary_plane_hidden_" +
                        groundKind);
                }
            }

            planeHold.DurationMs = 350.0;
            _middleEdit.Mode = FalsumMiddleEditMode.StartEnd;
            _middleEdit.TimeSide = MiddleEditTimeSide.Tail;
            if (Math.Abs(
                    MiddlePlaneTime(
                        _middleEdit,
                        planeHoldTarget) -
                    planeHold.EndTimeMs) > 0.0001)
            {
                issues.Add("start_end_plane_follows_note");
            }

            var planeSky = new ChartNote
            {
                Kind = NoteKind.SkyArea,
                TimeMs = 2000.0,
                DurationMs = 600.0,
                StartPos = 0.3,
                StartWidth = 0.2,
                EndPos = 0.7,
                EndWidth = 0.2,
            };
            var planeSkyTarget = CreateMiddleDebugTarget(
                planeSky,
                MiddleEditTimeSide.Tail);
            _middleEdit = new MiddleEditState
            {
                Phase = MiddleEditPhase.Dragging,
                Mode = FalsumMiddleEditMode.StartEnd,
                ActiveNote = planeSky,
                Side = MiddleEditSide.Left,
                TimeSide = MiddleEditTimeSide.Tail,
                Grid = 4,
            };
            _middleEdit.Targets.Add(planeSkyTarget);
            if (!TryGetMiddlePlaneState(
                    out planeKind,
                    out planePrompt,
                    out planeModeLabel,
                    out planeKeyHint,
                    out planeDivisions,
                    out planeFocusLine,
                    out planeTime) ||
                planeFocusLine != -1)
            {
                issues.Add("start_end_arrow_hidden");
            }
            _middleEdit.Mode = FalsumMiddleEditMode.Position;
            if (!TryGetMiddlePlaneState(
                    out planeKind,
                    out planePrompt,
                    out planeModeLabel,
                    out planeKeyHint,
                    out planeDivisions,
                    out planeFocusLine,
                    out planeTime) ||
                planeFocusLine != -1)
            {
                issues.Add("position_arrow_hidden");
            }
            _middleEdit = null;

            var multiFlick = new ChartNote
            {
                Kind = NoteKind.Flick,
                StartPos = 0.4,
                StartWidth = 0.1,
                StartCellCount = 24,
                EndCellCount = 24,
            };
            var multiSky = new ChartNote
            {
                Kind = NoteKind.SkyArea,
                StartPos = 0.5,
                StartWidth = 0.1,
                StartCellCount = 24,
                EndPos = 0.6,
                EndWidth = 0.1,
                EndCellCount = 24,
            };
            _selected.Clear();
            _selected.Add(multiFlick);
            _selected.Add(multiSky);
            var multiState = new MiddleEditState
            {
                Phase = MiddleEditPhase.Pressed,
                Mode = FalsumMiddleEditMode.Position,
                ActiveNote = multiFlick,
                Side = MiddleEditSide.Left,
                TimeSide = MiddleEditTimeSide.Head,
            };
            _middleEditAffectsAllSelected = true;
            BuildMiddleTargets(multiState);
            if (multiState.Targets.Count != 2)
            {
                issues.Add("middle_affects_all");
            }
            multiState.Targets.Clear();
            multiState.Notes.Clear();
            _middleEditAffectsAllSelected = false;
            BuildMiddleTargets(multiState);
            if (multiState.Targets.Count != 1 ||
                !ReferenceEquals(
                    multiState.Targets[0].Note,
                    multiFlick))
            {
                issues.Add("middle_affects_single");
            }
            _middleEditAffectsAllSelected = true;
            _selected.Clear();

            var endpointSky = new ChartNote
            {
                Kind = NoteKind.SkyArea,
                TimeMs = originalPlayhead,
                DurationMs = 400.0,
                StartPos = 0.25,
                StartWidth = 0.2,
                EndPos = 0.65,
                EndWidth = 0.25,
            };
            _playheadMs = originalPlayhead;
            var endpointTimes = new[]
            {
                MiddleEditTimeSide.Head,
                MiddleEditTimeSide.Tail,
            };
            var endpointSides = new[]
            {
                MiddleEditSide.Left,
                MiddleEditSide.Right,
            };
            for (var t = 0; t < endpointTimes.Length; t++)
            {
                for (var s = 0; s < endpointSides.Length; s++)
                {
                    Vector2 screen;
                    if (!_world.TryGetMiddleHandleScreenPosition(
                            endpointSky,
                            endpointTimes[t] ==
                            MiddleEditTimeSide.Tail,
                            endpointSides[s] ==
                            MiddleEditSide.Right,
                            originalPlayhead,
                            out screen))
                    {
                        issues.Add("sky_handle_projection");
                        continue;
                    }
                    var endpointState = new MiddleEditState
                    {
                        ActiveNote = endpointSky,
                        PressScreen = screen,
                    };
                    if (!TryResolveSkyBoundaryHandle(endpointState) ||
                        endpointState.TimeSide != endpointTimes[t] ||
                        endpointState.Side != endpointSides[s])
                    {
                        issues.Add("sky_handle_resolve");
                    }
                }
            }

            Vector2 headCenter;
            Vector2 tailCenter;
            if (TryGetMiddleCenterHandle(
                    endpointSky,
                    false,
                    out headCenter) &&
                TryGetMiddleCenterHandle(
                    endpointSky,
                    true,
                    out tailCenter))
            {
                if (ResolveShortestTimeSide(
                        headCenter,
                        endpointSky) !=
                    MiddleEditTimeSide.Head ||
                    ResolveShortestTimeSide(
                        tailCenter,
                        endpointSky) !=
                    MiddleEditTimeSide.Tail)
                {
                    issues.Add("sky_time_handle_resolve");
                }
            }
            else
            {
                issues.Add("sky_time_handle_projection");
            }

            var groupA = new ChartNote
            {
                Kind = NoteKind.SkyArea,
                TimeMs = 1000.0,
                DurationMs = 1000.0,
                StartPos = 0.3,
                StartWidth = 0.2,
                EndPos = 0.3,
                EndWidth = 0.2,
                GroupId = 1,
            };
            var groupB = new ChartNote
            {
                Kind = NoteKind.SkyArea,
                TimeMs = 3000.0,
                DurationMs = 500.0,
                StartPos = 0.7,
                StartWidth = 0.2,
                EndPos = 0.7,
                EndWidth = 0.2,
                GroupId = 2,
            };
            groupA.Id = _chart.Notes.Count;
            groupB.Id = _chart.Notes.Count + 1;
            _chart.Notes.Add(groupA);
            _chart.Notes.Add(groupB);
            if (groupA.GroupId == groupB.GroupId ||
                !ContainsSkyArea(new[] { groupA }))
            {
                issues.Add("sky_group_initial");
            }

            var snapCandidate = new ChartNote
            {
                Kind = NoteKind.SkyArea,
                TimeMs = 1990.0,
                DurationMs = 500.0,
                StartPos = 0.3,
                StartWidth = 0.2,
                EndPos = 0.3,
                EndWidth = 0.2,
            };
            snapCandidate.Id = _chart.Notes.Count;
            _chart.Notes.Add(snapCandidate);
            _playheadMs = 2000.0;
            var snapWasEnabled =
                _preferences.Editing.SkyAreaGroupTimeSnap;
            _preferences.Editing.SkyAreaGroupTimeSnap = true;
            double snapTime;
            SkyAreaSnapBoundary snapBoundary;
            if (!TryResolveSkyAreaGroupSnap(
                    snapCandidate.EndTimeMs,
                    snapCandidate.TimeMs,
                    groupA.StartPos,
                    groupA.StartWidth,
                    groupA.EndPos,
                    groupA.EndWidth,
                    SkyAreaSnapBoundary.Head,
                    new[] { groupA },
                    out snapTime,
                    out snapBoundary) ||
                Math.Abs(snapTime - snapCandidate.EndTimeMs) >
                0.001 ||
                snapBoundary != SkyAreaSnapBoundary.Head)
            {
                issues.Add("sky_group_snap_head");
            }
            if (!TryResolveSkyAreaGroupSnap(
                    snapCandidate.EndTimeMs,
                    snapCandidate.TimeMs,
                    groupA.StartPos,
                    groupA.StartWidth,
                    groupA.EndPos,
                    groupA.EndWidth,
                    SkyAreaSnapBoundary.Tail,
                    new[] { groupA },
                    out snapTime,
                    out snapBoundary) ||
                Math.Abs(snapTime - snapCandidate.TimeMs) >
                0.001 ||
                snapBoundary != SkyAreaSnapBoundary.Tail)
            {
                issues.Add("sky_group_snap_tail");
            }
            _preferences.Editing.SkyAreaGroupTimeSnap = false;
            if (TryResolveSkyAreaGroupSnap(
                    snapCandidate.EndTimeMs,
                    snapCandidate.TimeMs,
                    groupA.StartPos,
                    groupA.StartWidth,
                    groupA.EndPos,
                    groupA.EndWidth,
                    SkyAreaSnapBoundary.Either,
                    new[] { groupA },
                    out snapTime,
                    out snapBoundary))
            {
                issues.Add("sky_group_snap_disabled");
            }
            _preferences.Editing.SkyAreaGroupTimeSnap =
                snapWasEnabled;
            _chart.Notes.Remove(snapCandidate);

            groupB.TimeMs = groupA.EndTimeMs + 0.5;
            if (groupA.GroupId == groupB.GroupId)
            {
                issues.Add("middle_sky_group_preserve");
            }

            var cancelNote = new ChartNote
            {
                Kind = NoteKind.Flick,
                TimeMs = 3000.0,
                StartPos = 0.25,
                StartWidth = 0.2,
                StartCellCount = 24,
                EndCellCount = 24,
                FlickDirection = 4,
            };
            cancelNote.Id = _chart.Notes.Count;
            _chart.Notes.Add(cancelNote);
            _session.BeginChange(
                L.Get("falsum.history.middleEditCancelCheck"));
            cancelNote.StartPos = 0.4;
            _middleEdit = new MiddleEditState
            {
                Phase = MiddleEditPhase.Dragging,
                Mode = FalsumMiddleEditMode.Boundary,
                ActiveNote = cancelNote,
                Side = MiddleEditSide.Left,
                TimeSide = MiddleEditTimeSide.Head,
                Grid = 24,
            };
            _middleEdit.Targets.Add(CreateMiddleDebugTarget(
                cancelNote,
                MiddleEditTimeSide.Head));
            _middleEdit.Notes.Add(cancelNote);
            var cancelId = cancelNote.Id;
            CancelMiddleEdit();
            ChartNote restoredCancel = null;
            for (var i = 0; i < _chart.Notes.Count; i++)
            {
                if (_chart.Notes[i].Id == cancelId)
                {
                    restoredCancel = _chart.Notes[i];
                    break;
                }
            }
            if (restoredCancel == null ||
                Math.Abs(restoredCancel.StartPos - 0.25) > 0.0001)
            {
                issues.Add("middle_cancel_restore");
            }

            if (!original.Success)
            {
                issues.Add("restore_parse");
            }
            else
            {
                _session.Replace(original.Chart, original.Chart.DelayMs);
                _middleEdit = null;
                _middleEditAffectsAllSelected = true;
                _selected.Clear();
                _playheadMs = originalPlayhead;
                RefreshEditorVisuals();
            }
            return issues.Count == 0
                ? "pass"
                : string.Join(",", issues.ToArray());
        }

        private static MiddleEditTarget CreateMiddleDebugTarget(
            ChartNote note,
            MiddleEditTimeSide timeSide)
        {
            return new MiddleEditTarget
            {
                Note = note,
                TimeMs = note.TimeMs,
                DurationMs = note.DurationMs,
                StartLane = note.StartLane,
                WidthLanes = note.WidthLanes,
                StartPos = note.StartPos,
                EndPos = note.EndPos,
                StartWidth = note.StartWidth,
                EndWidth = note.EndWidth,
                FlickDirection = note.FlickDirection,
                TimeSide = timeSide,
                NativeGrid = ResolveMiddleEndpointGrid(
                    note,
                    timeSide),
                PositionGrid = ResolveMiddlePositionGrid(note),
            };
        }

        private bool IsMiddleEditActive
        {
            get
            {
                return _middleEdit != null &&
                       _middleEdit.Phase != MiddleEditPhase.Idle;
            }
        }

        private bool IsMiddleEditGridAdjustable
        {
            get
            {
                if (IsMiddleEditActive)
                {
                    return _middleEdit.Mode ==
                           FalsumMiddleEditMode.Boundary ||
                           _middleEdit.Mode ==
                           FalsumMiddleEditMode.Position;
                }
                // An anchor drag runs the SAME middle-edit bodies through
                // AnchorDragState.Shared, but it never assigns _middleEdit: the
                // drag owns its own state. Reading only _middleEdit therefore
                // made '-' / '=' dead for every anchor drag, even though the
                // shared Grid is exactly what those key presses must step.
                return AnchorGridTarget != null;
            }
        }

        /// <summary>
        /// The shared middle-edit state an anchor drag is currently driving,
        /// or null when the drag has none (easing, group scaling) or no drag is
        /// running. Those operations deliberately expose no grid to step.
        /// </summary>
        private MiddleEditState AnchorGridTarget
        {
            get
            {
                var drag = _anchorDrag;
                if (drag == null ||
                    drag.Phase == AnchorPhase.Idle ||
                    drag.Shared == null)
                {
                    return null;
                }
                return drag.Shared.Mode ==
                       FalsumMiddleEditMode.Boundary ||
                       drag.Shared.Mode ==
                       FalsumMiddleEditMode.Position
                    ? drag.Shared
                    : null;
            }
        }

        /// <summary>
        /// The grid field the '-' / '=' keys step: the anchor drag's shared
        /// state while one is running, otherwise the live middle-edit state.
        /// Both paths feed the identical step + write-back, so a drag and the
        /// equivalent middle edit cannot drift.
        /// </summary>
        private MiddleEditState GridAdjustTarget
        {
            get
            {
                var anchor = AnchorGridTarget;
                return anchor != null ? anchor : _middleEdit;
            }
        }

        private void SetMiddleEditMode(FalsumMiddleEditMode mode)
        {
            if (IsMiddleEditActive)
            {
                ShowHint(
                    L.Get(
                        "falsum.hint.middleEdit.cannotSwitchWhileDragging"),
                    1.5f);
                return;
            }
            _middleEditMode = mode;
            RefreshMiddleEditModeStatus();
            ShowHint(MiddleEditModeHint(mode), 2.2f);
        }

        private string MiddleEditModeHint(FalsumMiddleEditMode mode)
        {
            switch (mode)
            {
                case FalsumMiddleEditMode.StartEnd:
                    return L.Get(
                        "falsum.middleEdit.modeHint",
                        ("key", MiddleEditModeKey(mode)),
                        ("mode", MiddleEditModeName(mode)));
                case FalsumMiddleEditMode.Position:
                    return L.Get(
                        "falsum.middleEdit.modeHint",
                        ("key", MiddleEditModeKey(mode)),
                        ("mode", MiddleEditModeName(mode)));
                default:
                    return L.Get(
                        "falsum.middleEdit.modeHint",
                        ("key", MiddleEditModeKey(mode)),
                        ("mode", MiddleEditModeName(mode)));
            }
        }

        private void RefreshMiddleEditModeStatus()
        {
            if (_view == null)
            {
                return;
            }
            var current = _middleEditMode;
            var alternatives = new List<string>();
            AddMiddleEditAlternative(
                alternatives,
                current,
                FalsumMiddleEditMode.Boundary);
            AddMiddleEditAlternative(
                alternatives,
                current,
                FalsumMiddleEditMode.StartEnd);
            AddMiddleEditAlternative(
                alternatives,
                current,
                FalsumMiddleEditMode.Position);
            _view.SetMiddleEditStatus(
                L.Get(
                    "falsum.middleEdit.statusTitle",
                    ("mode", MiddleEditModeName(current)),
                    ("key", MiddleEditModeKey(current))),
                MiddleEditModeDescription(current),
                L.Get(
                    "falsum.middleEdit.otherModes",
                    ("alternatives", string.Join(
                        L.Get("falsum.middleEdit.separator"),
                        alternatives.ToArray()))));
        }

        private void AddMiddleEditAlternative(
            IList<string> alternatives,
            FalsumMiddleEditMode current,
            FalsumMiddleEditMode candidate)
        {
            if (candidate == current)
            {
                return;
            }
            alternatives.Add(
                L.Get(
                    "falsum.middleEdit.alternative",
                    ("key", MiddleEditModeKey(candidate)),
                    ("mode", MiddleEditModeName(candidate))));
        }

        private string MiddleEditModeKey(FalsumMiddleEditMode mode)
        {
            if (_preferences == null)
            {
                return "--";
            }
            return FalsumInputRouter.Format(
                _preferences.FindBinding(
                    MiddleEditInputAction(mode)));
        }

        private static FalsumInputAction MiddleEditInputAction(
            FalsumMiddleEditMode mode)
        {
            switch (mode)
            {
                case FalsumMiddleEditMode.StartEnd:
                    return FalsumInputAction.MiddleEditStartEnd;
                case FalsumMiddleEditMode.Position:
                    return FalsumInputAction.MiddleEditPosition;
                default:
                    return FalsumInputAction.MiddleEditBoundary;
            }
        }

        private static string MiddleEditModeName(
            FalsumMiddleEditMode mode)
        {
            switch (mode)
            {
                case FalsumMiddleEditMode.StartEnd:
                    return L.Get("falsum.middleEdit.mode.startEnd");
                case FalsumMiddleEditMode.Position:
                    return L.Get("falsum.middleEdit.mode.position");
                default:
                    return L.Get("falsum.middleEdit.mode.boundary");
            }
        }

        private static string MiddleEditModeDescription(
            FalsumMiddleEditMode mode)
        {
            switch (mode)
            {
                case FalsumMiddleEditMode.StartEnd:
                    return L.Get(
                        "falsum.middleEdit.description.startEnd");
                case FalsumMiddleEditMode.Position:
                    return L.Get(
                        "falsum.middleEdit.description.position");
                default:
                    return L.Get(
                        "falsum.middleEdit.description.boundary");
            }
        }

        private static string MiddleEditModeLabel(FalsumMiddleEditMode mode)
        {
            switch (mode)
            {
                case FalsumMiddleEditMode.StartEnd:
                    return L.Get(
                        "falsum.middleEdit.label.startEnd");
                case FalsumMiddleEditMode.Position:
                    return L.Get(
                        "falsum.middleEdit.label.position");
                default:
                    return L.Get(
                        "falsum.middleEdit.label.boundary");
            }
        }

        private bool CanBeginMiddleEdit()
        {
            return (_mode == EditorMode.Select ||
                    _mode == EditorMode.PointPlace) &&
                   !_view.IsModalOpen &&
                   !_view.IsTyping() &&
                   (_host.Clock == null || !_host.Clock.IsPlaying) &&
                   !_session.HasPendingEdit &&
                   !_draggingSelection &&
                   !_selectionDragPending &&
                   // The two drag channels are mutually exclusive. An anchor
                   // that is still in its Pressed phase has not opened a
                   // session change yet, so the pending-edit check above would
                   // not catch it and a middle click would start a second
                   // transient edit on the same note.
                   !IsAnchorDragActive;
        }

        private void BeginMiddleEdit(Vector2 screenPosition)
        {
            if (!CanBeginMiddleEdit() || !_hasPointer)
            {
                return;
            }

            List<ChartNote> candidates;
            if (!TryPickAllNotes(screenPosition, out candidates) ||
                candidates.Count == 0)
            {
                return;
            }
            var activeNote = ResolveMiddleActiveCandidate(
                screenPosition,
                candidates);

            var wasSelected = _selected.Contains(activeNote);
            if (!wasSelected)
            {
                var ctrl = FalsumModifierState.Control;
                if (!ctrl)
                {
                    _selected.Clear();
                }
                _selected.Add(activeNote);
                RefreshPropertyPanel();
            }

            var state = new MiddleEditState
            {
                Phase = MiddleEditPhase.Pressed,
                Mode = _middleEditMode,
                ActiveNote = activeNote,
                PressScreen = screenPosition,
                PressLanePosition = _pointerLanePosition,
                PressRawTime = _pointerRawTime,
            };
            for (var i = 0; i < _selected.Count; i++)
            {
                state.SelectionIds.Add(_selected[i].Id);
            }

            string reason;
            if (!TryResolveMiddleHandle(state, out reason))
            {
                ShowHint(reason, 1.8f);
                UpdateViews();
                return;
            }
            if (TryCycleSkyBoundaryEasingOnDoubleClick(state))
            {
                UpdateViews();
                return;
            }
            state.ProjectionTimeMs =
                MiddleProjectionTimeMs(state);
            float pressPlaneX;
            if (TryGetPointerNormalizedOnTimePlane(
                    screenPosition,
                    state.ProjectionTimeMs,
                    _playheadMs,
                    out pressPlaneX))
            {
                state.HasPlanePointer = true;
                state.PressPlaneX = pressPlaneX;
                state.CurrentPlaneX = pressPlaneX;
            }

            BuildMiddleTargets(state);
            if (state.Targets.Count == 0)
            {
                ShowHint(
                    L.Get(
                        "falsum.hint.middleEdit.modeNotApplicable"),
                    1.8f);
                UpdateViews();
                return;
            }
            if (state.SkippedCount > 0)
            {
                ShowHint(
                    L.Get(
                        "falsum.hint.middleEdit.skippedObjects",
                        ("count", state.SkippedCount)),
                    1.8f);
            }

            state.Grid = ComputeMiddleEditGrid(
                state.Mode,
                state.Targets,
                state.ActiveNote);
            _middleEdit = state;
            _middleEditVisualRefreshAt = 0.0;
            UpdateViews();
        }

        private ChartNote ResolveMiddleActiveCandidate(
            Vector2 screenPosition,
            IList<ChartNote> candidates)
        {
            if (_hasPickCycle &&
                IsSamePickCyclePosition(screenPosition) &&
                MatchesPickCycleCandidates(candidates) &&
                _pickCycleIndex >= 0 &&
                _pickCycleIndex < candidates.Count)
            {
                var cycleNote = candidates[_pickCycleIndex];
                if (_selected.Contains(cycleNote))
                {
                    return cycleNote;
                }
            }

            for (var i = 0; i < candidates.Count; i++)
            {
                if (_selected.Contains(candidates[i]))
                {
                    return candidates[i];
                }
            }
            return candidates[0];
        }

        private bool TryResolveMiddleHandle(
            MiddleEditState state,
            out string reason)
        {
            reason = string.Empty;
            var note = state.ActiveNote;
            if (state.Mode == FalsumMiddleEditMode.Boundary)
            {
                if (note.Kind == NoteKind.Tap ||
                    note.Kind == NoteKind.Hold)
                {
                    if (!IsEditableCentralGroundNote(note))
                    {
                        reason = L.Get(
                            "falsum.error.middleEdit.groundWidthTracks");
                        return false;
                    }
                    state.TimeSide = MiddleEditTimeSide.Head;
                    state.Side = ResolveShortestSide(
                        state.PressScreen,
                        note,
                        false);
                    return true;
                }
                if (note.Kind == NoteKind.Flick)
                {
                    state.TimeSide = MiddleEditTimeSide.Head;
                    state.Side = ResolveShortestSide(
                        state.PressScreen,
                        note,
                        false);
                    return true;
                }
                if (note.Kind == NoteKind.SkyArea)
                {
                    return TryResolveSkyBoundaryHandle(state);
                }
                reason = L.Get(
                    "falsum.hint.middleEdit.modeNotApplicable");
                return false;
            }

            if (state.Mode == FalsumMiddleEditMode.StartEnd)
            {
                if (note.Kind != NoteKind.Hold &&
                    note.Kind != NoteKind.SkyArea)
                {
                    reason = L.Get(
                        "falsum.error.middleEdit.startEndKinds");
                    return false;
                }
                state.Side = MiddleEditSide.Left;
                state.TimeSide = ResolveShortestTimeSide(
                    state.PressScreen,
                    note);
                return true;
            }

            if (note.Kind != NoteKind.Flick &&
                note.Kind != NoteKind.SkyArea)
            {
                reason = L.Get(
                    "falsum.error.middleEdit.positionKinds");
                return false;
            }
            state.Side = MiddleEditSide.Left;
            state.TimeSide = MiddleEditTimeSide.Head;
            return true;
        }

        private bool TryCycleSkyBoundaryEasingOnDoubleClick(
            MiddleEditState state)
        {
            var note = state != null ? state.ActiveNote : null;
            if (state == null ||
                state.Mode != FalsumMiddleEditMode.Boundary ||
                note == null ||
                note.Kind != NoteKind.SkyArea)
            {
                ResetSkyBoundaryDoubleClickTracking();
                return false;
            }

            var now = Time.unscaledTimeAsDouble;
            var isDoubleClick =
                ReferenceEquals(
                    _lastSkyMiddleBoundaryNote,
                    note) &&
                _lastSkyMiddleBoundarySide ==
                state.Side &&
                now - _lastSkyMiddleBoundaryClickTime <=
                SkyMiddleDoubleClickSeconds;
            _lastSkyMiddleBoundaryNote = note;
            _lastSkyMiddleBoundaryClickTime = now;
            _lastSkyMiddleBoundarySide = state.Side;
            if (!isDoubleClick)
            {
                return false;
            }

            ResetSkyBoundaryDoubleClickTracking();
            _session.BeginChange(
                L.Get("falsum.history.toggleEasing"));
            if (state.Side == MiddleEditSide.Left)
            {
                note.LeftEasing =
                    (note.LeftEasing + 1) % 3;
            }
            else
            {
                note.RightEasing =
                    (note.RightEasing + 1) % 3;
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
            }
            ShowHint(
                L.Get(
                    state.Side == MiddleEditSide.Left
                        ? "falsum.hint.middleEdit.leftEasing"
                        : "falsum.hint.middleEdit.rightEasing",
                    ("easing", EasingName(
                        state.Side == MiddleEditSide.Left
                            ? note.LeftEasing
                            : note.RightEasing))),
                1.5f);
            return true;
        }

        private void ResetSkyBoundaryDoubleClickTracking()
        {
            _lastSkyMiddleBoundaryNote = null;
            _lastSkyMiddleBoundaryClickTime = 0.0;
            _lastSkyMiddleBoundarySide =
                MiddleEditSide.Left;
        }

        private static string EasingName(int easing)
        {
            if (easing == 1)
            {
                return L.Get("falsum.easing.easeOut");
            }
            if (easing == 2)
            {
                return L.Get("falsum.easing.easeIn");
            }
            return L.Get("falsum.easing.line");
        }

        private bool TryResolveSkyBoundaryHandle(MiddleEditState state)
        {
            var note = state.ActiveNote;
            var best = float.MaxValue;
            var found = false;
            var bestTail = MiddleEditTimeSide.Head;
            var bestSide = MiddleEditSide.Left;
            CheckSkyHandle(
                state.PressScreen,
                note,
                MiddleEditTimeSide.Head,
                MiddleEditSide.Left,
                ref best,
                ref found,
                ref bestTail,
                ref bestSide);
            CheckSkyHandle(
                state.PressScreen,
                note,
                MiddleEditTimeSide.Head,
                MiddleEditSide.Right,
                ref best,
                ref found,
                ref bestTail,
                ref bestSide);
            CheckSkyHandle(
                state.PressScreen,
                note,
                MiddleEditTimeSide.Tail,
                MiddleEditSide.Left,
                ref best,
                ref found,
                ref bestTail,
                ref bestSide);
            CheckSkyHandle(
                state.PressScreen,
                note,
                MiddleEditTimeSide.Tail,
                MiddleEditSide.Right,
                ref best,
                ref found,
                ref bestTail,
                ref bestSide);
            if (!found)
            {
                return false;
            }
            state.TimeSide = bestTail;
            state.Side = bestSide;
            return true;
        }

        private void CheckSkyHandle(
            Vector2 pointer,
            ChartNote note,
            MiddleEditTimeSide timeSide,
            MiddleEditSide side,
            ref float best,
            ref bool found,
            ref MiddleEditTimeSide bestTimeSide,
            ref MiddleEditSide bestSide)
        {
            Vector2 handle;
            if (!_world.TryGetMiddleHandleScreenPosition(
                    note,
                    timeSide == MiddleEditTimeSide.Tail,
                    side == MiddleEditSide.Right,
                    _playheadMs,
                    out handle))
            {
                return;
            }
            var distance = (pointer - handle).sqrMagnitude;
            if (!found || distance < best)
            {
                found = true;
                best = distance;
                bestTimeSide = timeSide;
                bestSide = side;
            }
        }

        private MiddleEditSide ResolveShortestSide(
            Vector2 pointer,
            ChartNote note,
            bool tail)
        {
            var leftDistance = float.MaxValue;
            var rightDistance = float.MaxValue;
            Vector2 handle;
            if (_world.TryGetMiddleHandleScreenPosition(
                    note,
                    tail,
                    false,
                    _playheadMs,
                    out handle))
            {
                leftDistance = (pointer - handle).sqrMagnitude;
            }
            if (_world.TryGetMiddleHandleScreenPosition(
                    note,
                    tail,
                    true,
                    _playheadMs,
                    out handle))
            {
                rightDistance = (pointer - handle).sqrMagnitude;
            }
            return leftDistance <= rightDistance
                ? MiddleEditSide.Left
                : MiddleEditSide.Right;
        }

        private MiddleEditTimeSide ResolveShortestTimeSide(
            Vector2 pointer,
            ChartNote note)
        {
            Vector2 head;
            Vector2 tail;
            var hasHead = TryGetMiddleCenterHandle(
                note,
                false,
                out head);
            var hasTail = TryGetMiddleCenterHandle(
                note,
                true,
                out tail);
            if (!hasHead)
            {
                return MiddleEditTimeSide.Tail;
            }
            if (!hasTail)
            {
                return MiddleEditTimeSide.Head;
            }
            return (pointer - head).sqrMagnitude <=
                   (pointer - tail).sqrMagnitude
                ? MiddleEditTimeSide.Head
                : MiddleEditTimeSide.Tail;
        }

        private bool TryGetMiddleCenterHandle(
            ChartNote note,
            bool tail,
            out Vector2 center)
        {
            Vector2 left;
            Vector2 right;
            var hasLeft = _world.TryGetMiddleHandleScreenPosition(
                note,
                tail,
                false,
                _playheadMs,
                out left);
            var hasRight = _world.TryGetMiddleHandleScreenPosition(
                note,
                tail,
                true,
                _playheadMs,
                out right);
            if (!hasLeft && !hasRight)
            {
                center = Vector2.zero;
                return false;
            }
            center = hasLeft && hasRight
                ? (left + right) * 0.5f
                : (hasLeft ? left : right);
            return true;
        }

        private void BuildMiddleTargets(MiddleEditState state)
        {
            state.Targets.Clear();
            state.Notes.Clear();
            if (!_middleEditAffectsAllSelected)
            {
                AddMiddleTarget(state, state.ActiveNote, true);
                return;
            }

            for (var i = 0; i < _selected.Count; i++)
            {
                if (!AddMiddleTarget(
                        state,
                        _selected[i],
                        ReferenceEquals(_selected[i], state.ActiveNote)))
                {
                    state.SkippedCount++;
                }
            }
            if (state.Targets.Count == 0)
            {
                AddMiddleTarget(state, state.ActiveNote, true);
            }
        }

        private bool AddMiddleTarget(
            MiddleEditState state,
            ChartNote note,
            bool active)
        {
            if (note == null || !IsMiddleTargetApplicable(state.Mode, note))
            {
                return false;
            }
            if (state.Mode == FalsumMiddleEditMode.Boundary &&
                (note.Kind == NoteKind.Tap ||
                 note.Kind == NoteKind.Hold) &&
                !IsEditableCentralGroundNote(note))
            {
                return false;
            }

            var timeSide = state.TimeSide;
            if (state.Mode == FalsumMiddleEditMode.Boundary &&
                note.Kind == NoteKind.SkyArea &&
                !active)
            {
                timeSide = ResolveNearestTimeSide(note, _pointerRawTime);
            }
            var target = new MiddleEditTarget
            {
                Note = note,
                TimeMs = note.TimeMs,
                DurationMs = note.DurationMs,
                StartLane = note.StartLane,
                WidthLanes = note.WidthLanes,
                StartPos = note.StartPos,
                EndPos = note.EndPos,
                StartWidth = note.StartWidth,
                EndWidth = note.EndWidth,
                FlickDirection = note.FlickDirection,
                TimeSide = timeSide,
                NativeGrid = ResolveMiddleEndpointGrid(
                    note,
                    timeSide),
                PositionGrid = ResolveMiddlePositionGrid(note),
            };
            state.Targets.Add(target);
            state.Notes.Add(note);
            return true;
        }

        private static bool IsMiddleTargetApplicable(
            FalsumMiddleEditMode mode,
            ChartNote note)
        {
            switch (mode)
            {
                case FalsumMiddleEditMode.Boundary:
                    return note.Kind == NoteKind.Tap ||
                           note.Kind == NoteKind.Hold ||
                           note.Kind == NoteKind.Flick ||
                           note.Kind == NoteKind.SkyArea;
                case FalsumMiddleEditMode.StartEnd:
                    return note.Kind == NoteKind.Hold ||
                           note.Kind == NoteKind.SkyArea;
                case FalsumMiddleEditMode.Position:
                    return note.Kind == NoteKind.Flick ||
                           note.Kind == NoteKind.SkyArea;
                default:
                    return false;
            }
        }

        private static bool IsEditableCentralGroundNote(ChartNote note)
        {
            if (note == null ||
                (note.Kind != NoteKind.Tap &&
                 note.Kind != NoteKind.Hold))
            {
                return false;
            }
            var width = Mathf.Max(1, note.WidthLanes);
            return note.StartLane >= 1 &&
                   note.StartLane + width - 1 <= 4;
        }

        private static int ResolveMiddleEndpointGrid(
            ChartNote note,
            MiddleEditTimeSide timeSide)
        {
            if (note == null ||
                note.Kind == NoteKind.Tap ||
                note.Kind == NoteKind.Hold)
            {
                return 4;
            }
            var tail = timeSide == MiddleEditTimeSide.Tail;
            var declared = note.Kind == NoteKind.Flick
                ? note.StartCellCount
                : (tail ? note.EndCellCount : note.StartCellCount);
            if (declared > 0)
            {
                return Mathf.Clamp(
                    declared,
                    1,
                    MaxEditableFlickGrid);
            }
            var position = note.Kind == NoteKind.Flick
                ? note.StartPos
                : (tail ? note.EndPos : note.StartPos);
            var width = note.Kind == NoteKind.Flick
                ? note.StartWidth
                : (tail ? note.EndWidth : note.StartWidth);
            double normalizedPosition;
            double normalizedWidth;
            var inferred = FalsumFraction.NormalizePair(
                position,
                width,
                1,
                out normalizedPosition,
                out normalizedWidth);
            return Mathf.Clamp(
                inferred,
                1,
                MaxEditableFlickGrid);
        }

        private static int ResolveMiddlePositionGrid(ChartNote note)
        {
            if (note == null || note.Kind != NoteKind.SkyArea)
            {
                return ResolveMiddleEndpointGrid(
                    note,
                    MiddleEditTimeSide.Head);
            }
            return LeastCommonMultiple(
                ResolveMiddleEndpointGrid(
                    note,
                    MiddleEditTimeSide.Head),
                ResolveMiddleEndpointGrid(
                    note,
                    MiddleEditTimeSide.Tail));
        }

        private static MiddleEditTimeSide ResolveNearestTimeSide(
            ChartNote note,
            double timeMs)
        {
            return Math.Abs(note.TimeMs - timeMs) <=
                   Math.Abs(note.EndTimeMs - timeMs)
                ? MiddleEditTimeSide.Head
                : MiddleEditTimeSide.Tail;
        }

        private void UpdateMiddleEdit()
        {
            if (_middleEdit == null ||
                _middleEdit.Phase == MiddleEditPhase.Idle)
            {
                return;
            }
            if (!_hasPointer)
            {
                CancelMiddleEdit();
                return;
            }
            if (_middleEdit.Phase == MiddleEditPhase.Pressed)
            {
                _middleEdit.Phase = MiddleEditPhase.Dragging;
                _session.BeginChange(MiddleEditHistoryLabel(_middleEdit.Mode));
            }
            float currentPlaneX;
            if (_middleEdit.HasPlanePointer &&
                TryGetPointerNormalizedOnTimePlane(
                    Input.mousePosition,
                    _middleEdit.ProjectionTimeMs,
                    _playheadMs,
                    out currentPlaneX))
            {
                _middleEdit.CurrentPlaneX = currentPlaneX;
            }

            switch (_middleEdit.Mode)
            {
                case FalsumMiddleEditMode.Boundary:
                    ApplyMiddleBoundaryEdit();
                    break;
                case FalsumMiddleEditMode.StartEnd:
                    ApplyMiddleStartEndEdit();
                    break;
                case FalsumMiddleEditMode.Position:
                    ApplyMiddlePositionEdit();
                    break;
            }
            RefreshMiddleEditPreview();
        }

        private static string MiddleEditHistoryLabel(FalsumMiddleEditMode mode)
        {
            switch (mode)
            {
                case FalsumMiddleEditMode.StartEnd:
                    return L.Get(
                        "falsum.history.middleEditStartEnd");
                case FalsumMiddleEditMode.Position:
                    return L.Get(
                        "falsum.history.middleEditPosition");
                default:
                    return L.Get(
                        "falsum.history.middleEditBoundary");
            }
        }

        private void ApplyMiddleBoundaryEdit()
        {
            ApplyBoundaryEdit(_middleEdit, _pointerLanePosition);
        }

        /// <summary>
        /// The ONE boundary implementation. Both drag channels call it: the
        /// middle-mouse edit passes its live pointer state through
        /// <see cref="_middleEdit"/>, and the selection anchors build an
        /// equivalent <see cref="MiddleEditState"/> for their single note.
        ///
        /// Keeping a single body is the whole point of the anchor rebuild: two
        /// hand-written copies of the edge/clamp math drifted apart and the
        /// handles ended up producing values that did not match the box they
        /// were drawn on.
        /// </summary>
        private void ApplyBoundaryEdit(
            MiddleEditState state,
            float lanePosition)
        {
            var activeTarget = FindMiddleActiveTarget(state);
            if (activeTarget == null)
            {
                return;
            }

            var activeIsGround = activeTarget.Note.Kind == NoteKind.Tap ||
                                 activeTarget.Note.Kind == NoteKind.Hold;
            var groundDelta = Mathf.RoundToInt(
                (lanePosition - state.PressLanePosition) *
                6.0f);
            var skyDelta = (double)(state.HasPlanePointer
                ? state.CurrentPlaneX - state.PressPlaneX
                : lanePosition - state.PressLanePosition);
            if (activeIsGround)
            {
                skyDelta = groundDelta / 4.0;
            }
            else
            {
                groundDelta = Mathf.RoundToInt(
                    (float)(skyDelta * 4.0));
            }

            for (var i = 0; i < state.Targets.Count; i++)
            {
                var target = state.Targets[i];
                if (target.Note.Kind == NoteKind.Tap ||
                    target.Note.Kind == NoteKind.Hold)
                {
                    ApplyGroundBoundary(
                        target,
                        state.Side,
                        groundDelta);
                }
                else
                {
                    var targetGrid = state.GridWasAdjusted
                        ? state.Grid
                        : target.NativeGrid;
                    ApplySkyBoundary(
                        target,
                        state.Side,
                        skyDelta,
                        targetGrid,
                        state.GridWasAdjusted,
                        !_disableOutOfBoundsClamping);
                }
            }
        }

        private static MiddleEditTarget FindMiddleActiveTarget(
            MiddleEditState state)
        {
            for (var i = 0; i < state.Targets.Count; i++)
            {
                if (ReferenceEquals(
                        state.Targets[i].Note,
                        state.ActiveNote))
                {
                    return state.Targets[i];
                }
            }
            return null;
        }

        private static int ClampGroundBoundary(
            int value,
            int minimum,
            int maximum)
        {
            minimum = Mathf.Clamp(minimum, 1, 5);
            maximum = Mathf.Clamp(maximum, minimum, 5);
            return Mathf.Clamp(value, minimum, maximum);
        }

        private static void ApplyGroundBoundary(
            MiddleEditTarget target,
            MiddleEditSide side,
            int laneDelta)
        {
            var left = target.StartLane;
            var right = target.StartLane +
                        Mathf.Max(1, target.WidthLanes);
            var desired = side == MiddleEditSide.Left
                ? left + laneDelta
                : right + laneDelta;
            var crossed = side == MiddleEditSide.Left
                ? desired > right && right < 5
                : desired < left && left > 1;
            var newLeft = left;
            var newRight = right;
            if (side == MiddleEditSide.Left)
            {
                if (crossed)
                {
                    newLeft = right;
                    newRight = ClampGroundBoundary(
                        desired,
                        right + 1,
                        5);
                }
                else
                {
                    newLeft = ClampGroundBoundary(
                        desired,
                        1,
                        right - 1);
                    newRight = right;
                }
            }
            else
            {
                if (crossed)
                {
                    newLeft = ClampGroundBoundary(
                        desired,
                        1,
                        left - 1);
                    newRight = left;
                }
                else
                {
                    newLeft = left;
                    newRight = ClampGroundBoundary(
                        desired,
                        left + 1,
                        5);
                }
            }
            target.Crossed = crossed;
            target.Note.StartLane = newLeft;
            target.Note.WidthLanes = Mathf.Max(
                1,
                newRight - newLeft);
        }

        private static void GetTargetSpan(
            MiddleEditTarget target,
            MiddleEditTimeSide timeSide,
            out double center,
            out double width)
        {
            if (target.Note.Kind == NoteKind.Flick ||
                timeSide == MiddleEditTimeSide.Head)
            {
                center = target.StartPos;
                width = target.StartWidth;
                return;
            }
            center = target.EndPos;
            width = target.EndWidth;
        }

        private static void ApplySkyBoundary(
            MiddleEditTarget target,
            MiddleEditSide side,
            double normalizedDelta,
            int grid,
            bool gridWasAdjusted,
            bool clampToBounds)
        {
            var note = target.Note;
            double center;
            double width;
            GetTargetSpan(target, target.TimeSide, out center, out width);
            var left = center - width * 0.5;
            var right = center + width * 0.5;
            var minimumWidth = 1.0 / Mathf.Clamp(
                grid,
                1,
                MaxEditableFlickGrid);
            var snappedDelta = SnapNormalizedDelta(
                normalizedDelta,
                grid);
            var crossed = false;
            double newLeft = left;
            double newRight = right;

            if (side == MiddleEditSide.Left)
            {
                var desiredLeft = left + snappedDelta;
                if (desiredLeft > right)
                {
                    crossed = !clampToBounds ||
                              right + minimumWidth <= 1.0;
                    if (crossed)
                    {
                        newLeft = clampToBounds
                            ? Math.Min(
                                1.0,
                                Math.Max(
                                    right + minimumWidth,
                                    desiredLeft))
                            : Math.Max(
                                right + minimumWidth,
                                desiredLeft);
                        newRight = right;
                    }
                    else
                    {
                        newLeft = Math.Min(
                            right - minimumWidth,
                            desiredLeft);
                    }
                }
                else
                {
                    newLeft = clampToBounds
                        ? Math.Max(
                            0.0,
                            Math.Min(
                                right - minimumWidth,
                                desiredLeft))
                        : Math.Min(
                            right - minimumWidth,
                            desiredLeft);
                }
            }
            else
            {
                var desiredRight = right + snappedDelta;
                if (desiredRight < left)
                {
                    crossed = !clampToBounds ||
                              left - minimumWidth >= 0.0;
                    if (crossed)
                    {
                        newLeft = left;
                        newRight = clampToBounds
                            ? Math.Max(
                                0.0,
                                Math.Min(
                                    left - minimumWidth,
                                    desiredRight))
                            : Math.Min(
                                left - minimumWidth,
                                desiredRight);
                    }
                    else
                    {
                        newRight = Math.Max(
                            left + minimumWidth,
                            desiredRight);
                    }
                }
                else
                {
                    newRight = clampToBounds
                        ? Math.Min(
                            1.0,
                            Math.Max(
                                left + minimumWidth,
                                desiredRight))
                        : Math.Max(
                            left + minimumWidth,
                            desiredRight);
                }
            }

            if (clampToBounds)
            {
                newLeft = Math.Max(0.0, Math.Min(1.0, newLeft));
                newRight = Math.Max(0.0, Math.Min(1.0, newRight));
            }
            // A crossed flick handle intentionally reverses the edge roles.
            if (newLeft > newRight)
            {
                var swap = newLeft;
                newLeft = newRight;
                newRight = swap;
            }
            if (newRight - newLeft < minimumWidth)
            {
                if (side == MiddleEditSide.Right)
                {
                    newRight = clampToBounds
                        ? Math.Min(
                            1.0,
                            newLeft + minimumWidth)
                        : newLeft + minimumWidth;
                }
                else
                {
                    newLeft = clampToBounds
                        ? Math.Max(
                            0.0,
                            newRight - minimumWidth)
                        : newRight - minimumWidth;
                }
            }

            var newCenter = (newLeft + newRight) * 0.5;
            var newWidth = Math.Max(minimumWidth, newRight - newLeft);
            var writeGrid = gridWasAdjusted;
            target.Crossed = crossed;
            if (note.Kind == NoteKind.Flick ||
                target.TimeSide == MiddleEditTimeSide.Head)
            {
                note.StartPos = newCenter;
                note.StartWidth = newWidth;
                if (note.Kind == NoteKind.SkyArea)
                {
                    if (writeGrid)
                    {
                        note.StartCellCount = grid;
                    }
                }
            }
            if (note.Kind == NoteKind.Flick ||
                target.TimeSide == MiddleEditTimeSide.Tail)
            {
                if (note.Kind == NoteKind.SkyArea)
                {
                    note.EndPos = newCenter;
                    note.EndWidth = newWidth;
                    if (writeGrid)
                    {
                        note.EndCellCount = grid;
                    }
                }
            }
            if (note.Kind == NoteKind.Flick)
            {
                if (writeGrid)
                {
                    note.StartCellCount = grid;
                    note.EndCellCount = grid;
                }
                note.FlickDirection = crossed
                    ? FlipFlickDirection(target.FlickDirection)
                    : target.FlickDirection;
            }
        }

        private static int FlipFlickDirection(int direction)
        {
            return direction == 16 ? 4 : 16;
        }

        private void ApplyMiddleStartEndEdit()
        {
            ApplyStartEndEdit(_middleEdit, _pointerRawTime);
        }

        /// <summary>
        /// The ONE start/end implementation, shared by the middle-mouse edit and
        /// the selection anchors (see <see cref="ApplyBoundaryEdit"/> for why
        /// there is exactly one body).
        /// </summary>
        private void ApplyStartEndEdit(
            MiddleEditState state,
            double rawPointerTime)
        {
            var rawDelta = rawPointerTime - state.PressRawTime;
            double groupSnapDelta;
            var hasGroupSnap = TryResolveMiddleSkyAreaGroupSnap(
                state,
                rawDelta,
                out groupSnapDelta);
            for (var i = 0; i < state.Targets.Count; i++)
            {
                var target = state.Targets[i];
                var note = target.Note;
                var originalStart = target.TimeMs;
                var originalEnd = target.TimeMs + target.DurationMs;
                var effectiveDelta = hasGroupSnap
                    ? groupSnapDelta
                    : rawDelta;
                if (state.TimeSide == MiddleEditTimeSide.Head)
                {
                    var desired = Math.Max(
                        0.0,
                        originalStart + effectiveDelta);
                    // Snap to the nearest GRID LINE. The two-argument SnapTime
                    // returns `originalTime` whenever it is at least as close
                    // as the grid line, so anchoring it at the note's own start
                    // made a start edit stick to its original time until the
                    // pointer had travelled most of a cell -- the drag felt
                    // like it was editing the far end. A plain grid snap keeps
                    // every intermediate position on a line.
                    var candidate = hasGroupSnap
                        ? IntegerTimeMs(desired)
                        : IntegerTimeMs(_world.SnapTime(desired));
                    var minimumGap = MiddleMinimumDurationMs;
                    var maximum = Math.Max(
                        0.0,
                        originalEnd - minimumGap);
                    var newStart = Math.Max(
                        0.0,
                        Math.Min(candidate, maximum));
                    note.TimeMs = newStart;
                    note.DurationMs = Math.Max(
                        minimumGap,
                        originalEnd - newStart);
                }
                else
                {
                    var desired = Math.Max(
                        0.0,
                        originalEnd + effectiveDelta);
                    var candidate = hasGroupSnap
                        ? IntegerTimeMs(desired)
                        : IntegerTimeMs(_world.SnapTime(desired));
                    var minimumGap = MiddleMinimumDurationMs;
                    var minimum = originalStart + minimumGap;
                    var newEnd = Math.Max(minimum, candidate);
                    note.TimeMs = originalStart;
                    note.DurationMs = Math.Max(
                        minimumGap,
                        newEnd - originalStart);
                }
            }
        }

        private void ApplyMiddlePositionEdit()
        {
            ApplyPositionEdit(_middleEdit, _pointerLanePosition);
        }

        /// <summary>
        /// The ONE position implementation, shared by the middle-mouse edit and
        /// the selection anchors (see <see cref="ApplyBoundaryEdit"/> for why
        /// there is exactly one body).
        /// </summary>
        private void ApplyPositionEdit(
            MiddleEditState state,
            float lanePosition)
        {
            var rawDelta = state.HasPlanePointer
                ? state.CurrentPlaneX - state.PressPlaneX
                : lanePosition - state.PressLanePosition;
            var desired = SnapNormalizedDelta(rawDelta, state.Grid);
            var minimum = -double.MaxValue;
            var maximum = double.MaxValue;
            if (!_disableOutOfBoundsClamping)
            {
                // Clamp the whole selection with one delta so relative
                // spacing holds.
                for (var i = 0; i < state.Targets.Count; i++)
                {
                    var target = state.Targets[i];
                    AccumulatePositionLimits(
                        target,
                        target.TimeSide,
                        ref minimum,
                        ref maximum);
                    if (target.Note.Kind == NoteKind.SkyArea)
                    {
                        AccumulatePositionLimits(
                            target,
                            MiddleEditTimeSide.Tail,
                            ref minimum,
                            ref maximum);
                    }
                }
                if (minimum > maximum)
                {
                    minimum = 0.0;
                    maximum = 0.0;
                }
            }
            var delta = _disableOutOfBoundsClamping
                ? desired
                : Math.Max(
                    minimum,
                    Math.Min(maximum, desired));
            var writeGrid = state.GridWasAdjusted;
            for (var i = 0; i < state.Targets.Count; i++)
            {
                var target = state.Targets[i];
                var note = target.Note;
                if (note.Kind == NoteKind.Flick)
                {
                    note.StartPos = target.StartPos + delta;
                    note.StartWidth = target.StartWidth;
                    if (writeGrid)
                    {
                        note.EndCellCount = state.Grid;
                        note.StartCellCount = state.Grid;
                    }
                }
                else if (note.Kind == NoteKind.SkyArea)
                {
                    note.StartPos = target.StartPos + delta;
                    note.EndPos = target.EndPos + delta;
                    note.StartWidth = target.StartWidth;
                    note.EndWidth = target.EndWidth;
                    if (writeGrid)
                    {
                        note.StartCellCount = state.Grid;
                        note.EndCellCount = state.Grid;
                    }
                }
            }
        }

        private static void AccumulatePositionLimits(
            MiddleEditTarget target,
            MiddleEditTimeSide timeSide,
            ref double minimum,
            ref double maximum)
        {
            double center;
            double width;
            GetTargetSpan(target, timeSide, out center, out width);
            var left = center - width * 0.5;
            var right = center + width * 0.5;
            minimum = Math.Max(minimum, -left);
            maximum = Math.Min(maximum, 1.0 - right);
        }

        private static double SnapNormalizedDelta(
            double delta,
            int divisions)
        {
            divisions = Mathf.Clamp(
                divisions,
                1,
                MaxEditableFlickGrid);
            return Math.Round(
                       delta * divisions,
                       MidpointRounding.AwayFromZero) /
                   divisions;
        }

        private int ComputeMiddleEditGrid(
            FalsumMiddleEditMode mode,
            IList<MiddleEditTarget> targets,
            ChartNote activeNote)
        {
            if (mode == FalsumMiddleEditMode.StartEnd)
            {
                return 4;
            }
            if (mode == FalsumMiddleEditMode.Boundary)
            {
                for (var i = 0; i < targets.Count; i++)
                {
                    if (ReferenceEquals(
                            targets[i].Note,
                            activeNote))
                    {
                        return targets[i].NativeGrid;
                    }
                }
                return 4;
            }
            var result = 1;
            var found = false;
            for (var i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                AccumulateMiddleGrid(
                    target.PositionGrid,
                    ref result,
                    ref found);
            }
            return found
                ? Mathf.Clamp(result, 1, MaxEditableFlickGrid)
                : 4;
        }

        private static void AccumulateMiddleGrid(
            int grid,
            ref int result,
            ref bool found)
        {
            var normalized = grid > 0
                ? Mathf.Clamp(grid, 1, MaxEditableFlickGrid)
                : 24;
            found = true;
            result = LeastCommonMultiple(result, normalized);
        }

        private void AdjustMiddleEditGrid(int direction)
        {
            if (!IsMiddleEditGridAdjustable)
            {
                return;
            }
            var target = GridAdjustTarget;
            if (target == null)
            {
                return;
            }
            target.Grid = StepMiddleEditGrid(
                target.Grid,
                direction);
            target.GridWasAdjusted = true;
            // The anchor drag recomputes its own edit from the shared state; the
            // ordinary middle edit recomputes through UpdateMiddleEdit. Running
            // the wrong one would leave the note unsnapped to the new grid.
            var anchorState = AnchorGridTarget;
            if (anchorState != null)
            {
                UpdateAnchorDrag();
            }
            else if (_middleEdit.Phase == MiddleEditPhase.Dragging)
            {
                UpdateMiddleEdit();
            }
            ShowHint(
                L.Get(
                    "falsum.hint.middleEdit.grid",
                    ("grid", target.Grid)),
                1.2f);
        }

        /// <summary>
        /// Walks one step along the SAME denominator sequence the Flick /
        /// SkyArea grid uses, so the two <c>-</c> / <c>=</c> consumers cannot
        /// disagree about which denominators exist. A note whose native grid is
        /// not a sequence member (a 1/200 fraction, say) lands on the nearest
        /// member in the requested direction, which is what makes mixed
        /// denominators usable.
        ///
        /// <para>The walk itself lives in <see cref="FalsumBeatGrid"/>: this
        /// method only forwards the session sequence and re-applies the
        /// interactive ceiling, keeping invariant A-8 intact.</para>
        /// </summary>
        private int StepMiddleEditGrid(int current, int direction)
        {
            return Mathf.Clamp(
                FalsumBeatGrid.StepDenominator(
                    GridDenominatorSequence,
                    current,
                    direction),
                1,
                FalsumBeatGrid.MaxGridDenominator);
        }

        private void RefreshMiddleEditPreview()
        {
            if (_middleEdit == null)
            {
                return;
            }
            if (Time.unscaledTimeAsDouble -
                _middleEditVisualRefreshAt < 0.016)
            {
                return;
            }
            _middleEditVisualRefreshAt = Time.unscaledTimeAsDouble;
            RefreshPropertyPanel();
            if (_host.NoteVisuals != null)
            {
                _host.NoteVisuals.RefreshNoteVisuals(_middleEdit.Notes);
            }
            _host.UpdateChartPlaybackFrame(_playheadMs);
        }

        private void CommitMiddleEdit()
        {
            var state = _middleEdit;
            if (state == null)
            {
                return;
            }
            _middleEdit = null;
            if (state.Phase != MiddleEditPhase.Dragging ||
                !_session.HasPendingEdit)
            {
                UpdateViews();
                return;
            }
            if (ContainsNegativeMusicTime(state.Notes))
            {
                _session.CancelChange();
                RefreshEditorVisuals();
                ShowNegativeMusicTimeWarning();
                return;
            }
            if (_session.CommitChange())
            {
                _session.NotifyChanged();
                var touchesSkyArea =
                    ContainsSkyArea(state.Notes);
                if (touchesSkyArea &&
                    _host.NoteVisuals != null)
                {
                    _host.NoteVisuals.RebuildSkyAreas();
                    // The rebuild above already used the grouping that
                    // NotifyChanged just normalized.
                    _skyGroupingDirty = false;
                }
                if (_host.NoteVisuals != null)
                {
                    _host.NoteVisuals.RefreshNoteVisuals(state.Notes);
                }
                _host.UpdateChartPlaybackFrame(_playheadMs);
                RefreshPropertyPanel();
            }
            UpdateViews();
        }

        private static bool ContainsSkyArea(IList<ChartNote> notes)
        {
            if (notes == null)
            {
                return false;
            }
            for (var i = 0; i < notes.Count; i++)
            {
                if (notes[i] != null &&
                    notes[i].Kind == NoteKind.SkyArea)
                {
                    return true;
                }
            }
            return false;
        }

        private void CancelMiddleEdit()
        {
            // The two drag channels are mutually exclusive, but they share one
            // "abort the current transient edit" moment: view switch, playback,
            // modal, paste, tool pick. Cancelling only the middle edit left an
            // anchor drag's undo record alive across those transitions.
            CancelAnchorDragIfActive();
            var state = _middleEdit;
            if (state == null)
            {
                return;
            }
            _middleEdit = null;
            if (state.Phase == MiddleEditPhase.Dragging &&
                _session.HasPendingEdit)
            {
                _session.CancelChange();
                RemapSelectionAfterMiddleCancel(state.SelectionIds);
                RefreshEditorVisuals();
                RefreshPropertyPanel();
            }
            UpdateViews();
        }

        /// <summary>
        /// Cancels a live anchor drag without touching the history entry the
        /// middle edit owns. Kept separate so <see cref="CancelMiddleEdit"/>
        /// cannot accidentally ignore one channel when it is called from an
        /// unrelated code path.
        /// </summary>
        private void CancelAnchorDragIfActive()
        {
            if (IsAnchorDragActive)
            {
                CancelAnchorDrag();
            }
        }

        private void RemapSelectionAfterMiddleCancel(
            IList<int> previousIds)
        {
            if (previousIds == null || previousIds.Count == 0)
            {
                return;
            }
            var ids = new HashSet<int>();
            for (var i = 0; i < previousIds.Count; i++)
            {
                ids.Add(previousIds[i]);
            }
            _selected.Clear();
            for (var i = 0; i < _chart.Notes.Count; i++)
            {
                if (ids.Contains(_chart.Notes[i].Id))
                {
                    _selected.Add(_chart.Notes[i]);
                }
            }
        }

        private static double MiddleProjectionTimeMs(
            MiddleEditState state)
        {
            var note = state.ActiveNote;
            if (note == null)
            {
                return 0.0;
            }
            var useTail =
                state.Mode == FalsumMiddleEditMode.StartEnd &&
                state.TimeSide == MiddleEditTimeSide.Tail;
            useTail |=
                state.Mode == FalsumMiddleEditMode.Boundary &&
                note.Kind == NoteKind.SkyArea &&
                state.TimeSide == MiddleEditTimeSide.Tail;
            return useTail ? note.EndTimeMs : note.TimeMs;
        }

        private bool TryGetMiddlePlaneState(
            out FalsumModePlaneKind kind,
            out string prompt,
            out string modeLabel,
            out string keyHint,
            out int divisions,
            out int focusLine,
            out double planeTimeMs)
        {
            kind = FalsumModePlaneKind.None;
            prompt = string.Empty;
            modeLabel = string.Empty;
            keyHint = string.Empty;
            divisions = 0;
            focusLine = -1;
            planeTimeMs = _playheadMs;
            if (_middleEdit == null)
            {
                return false;
            }
            var active = FindMiddleActiveTarget(_middleEdit);
            if (active == null ||
                !ShouldDisplayMiddlePlane(
                    _middleEdit.Mode,
                    active.Note.Kind))
            {
                return false;
            }

            kind = FalsumModePlaneKind.MiddleEdit;
            modeLabel = MiddleEditModeLabel(_middleEdit.Mode);
            divisions = _middleEdit.Mode == FalsumMiddleEditMode.StartEnd
                ? 4
                : _middleEdit.Grid;
            keyHint = _middleEdit.Mode == FalsumMiddleEditMode.StartEnd
                ? L.Get(
                    "falsum.middleEdit.plane.startEndKeyHint")
                : L.Get("falsum.modePlane.gridAdjustHint");
            planeTimeMs = MiddlePlaneTime(_middleEdit, active);
            focusLine = _middleEdit.Mode ==
                        FalsumMiddleEditMode.Boundary
                ? GridLineAt(
                    (float)MiddleBoundaryFocusNormalized(
                        _middleEdit,
                        active),
                    divisions)
                : -1;

            switch (_middleEdit.Mode)
            {
                case FalsumMiddleEditMode.Boundary:
                    prompt = L.Get(
                        "falsum.middleEdit.prompt.boundary");
                    break;
                case FalsumMiddleEditMode.StartEnd:
                    prompt = L.Get(
                        "falsum.middleEdit.prompt.startEnd");
                    break;
                case FalsumMiddleEditMode.Position:
                    prompt = L.Get(
                        "falsum.middleEdit.prompt.position");
                    break;
            }
            return true;
        }

        private static double MiddlePlaneTime(
            MiddleEditState state,
            MiddleEditTarget active)
        {
            var note = active.Note;
            if (state.Mode == FalsumMiddleEditMode.StartEnd &&
                state.TimeSide == MiddleEditTimeSide.Tail)
            {
                return note.EndTimeMs;
            }
            if (state.Mode == FalsumMiddleEditMode.Boundary &&
                note.Kind == NoteKind.SkyArea &&
                active.TimeSide == MiddleEditTimeSide.Tail)
            {
                return note.EndTimeMs;
            }
            return note.TimeMs;
        }

        private bool TryGetMiddlePlaneTime(out double timeMs)
        {
            timeMs = _playheadMs;
            if (_middleEdit == null)
            {
                return false;
            }
            var active = FindMiddleActiveTarget(_middleEdit);
            if (active == null ||
                !ShouldDisplayMiddlePlane(
                    _middleEdit.Mode,
                    active.Note.Kind))
            {
                return false;
            }
            timeMs = MiddlePlaneTime(_middleEdit, active);
            return true;
        }

        private static bool ShouldDisplayMiddlePlane(
            FalsumMiddleEditMode mode,
            NoteKind kind)
        {
            var groundNote =
                kind == NoteKind.Tap ||
                kind == NoteKind.Hold;
            if (mode == FalsumMiddleEditMode.Boundary)
            {
                return !groundNote;
            }
            if (mode == FalsumMiddleEditMode.StartEnd)
            {
                return kind != NoteKind.Hold;
            }
            return true;
        }

        private double MiddleBoundaryFocusNormalized(
            MiddleEditState state,
            MiddleEditTarget active)
        {
            var note = active.Note;
            var side = active.Crossed
                ? (state.Side == MiddleEditSide.Left
                    ? MiddleEditSide.Right
                    : MiddleEditSide.Left)
                : state.Side;
            if (note.Kind == NoteKind.Tap ||
                note.Kind == NoteKind.Hold)
            {
                var boundary = side == MiddleEditSide.Left
                    ? note.StartLane
                    : note.StartLane +
                      Mathf.Max(1, note.WidthLanes);
                return _world.LaneBoundaryNormalizedPosition(boundary);
            }

            var tail = active.TimeSide == MiddleEditTimeSide.Tail;
            var center = note.Kind == NoteKind.Flick
                ? note.StartPos
                : (tail ? note.EndPos : note.StartPos);
            var width = note.Kind == NoteKind.Flick
                ? note.StartWidth
                : (tail ? note.EndWidth : note.StartWidth);
            var edge = center +
                       (side == MiddleEditSide.Right
                           ? width * 0.5
                           : -width * 0.5);
            return Math.Max(0.0, Math.Min(1.0, edge));
        }

        private Color MiddlePlaneTint()
        {
            if (_middleEdit == null)
            {
                return new Color(0.55f, 0.75f, 0.82f, 0.03f);
            }
            switch (_middleEdit.Mode)
            {
                case FalsumMiddleEditMode.Boundary:
                    return new Color(0.20f, 0.90f, 1.0f, 0.05f);
                case FalsumMiddleEditMode.StartEnd:
                    return new Color(1.0f, 0.62f, 0.25f, 0.05f);
                default:
                    return new Color(0.72f, 0.46f, 1.0f, 0.05f);
            }
        }

        private Color MiddleGridColor()
        {
            if (_middleEdit == null)
            {
                return new Color(1.0f, 0.70f, 0.24f, 0.95f);
            }
            return _middleEdit.Mode == FalsumMiddleEditMode.StartEnd
                ? new Color(1.0f, 0.70f, 0.24f, 0.95f)
                : new Color(1.0f, 0.43f, 0.47f, 0.98f);
        }
    }
}
