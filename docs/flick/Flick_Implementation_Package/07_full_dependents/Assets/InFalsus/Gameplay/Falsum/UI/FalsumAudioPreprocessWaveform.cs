using System;
using System.Collections.Generic;
using InFalsus.Gameplay.Falsum.Core;
using InFalsus.Gameplay.Falsum.Runtime;
using UnityEngine;
using UnityEngine.UIElements;

namespace InFalsus.Gameplay.Falsum.UI
{
    /// <summary>
    /// The waveform is drawn with <see cref="Painter2D"/> rather than a
    /// texture for three reasons: the beat grid has to stay pixel exact at any
    /// window size (a rasterised waveform would blur it), the progress line has
    /// to move every frame without re-uploading megabytes, and the delay blocks
    /// have to follow the silence value live while the user types it. The
    /// precedent in this project is <c>FalsumPerformanceWindow</c>, which draws
    /// its graphs the same way.
    ///
    /// Everything here is in "new timeline" milliseconds: 0 is where the
    /// rewritten audio starts, which for a positive audio delay is still inside
    /// the inserted silence.
    /// </summary>
    public sealed class FalsumAudioPreprocessWaveform : VisualElement
    {
        private const float BeatLineWidth = 1.0f;
        private const float DownbeatLineWidth = 1.6f;
        private const float ProgressLineWidth = 2.0f;
        private static readonly Color BackgroundColor =
            new Color(0.039f, 0.047f, 0.051f, 1.0f);
        private static readonly Color WaveColor =
            new Color(0.498f, 0.788f, 0.498f, 1.0f);
        private static readonly Color WaveFillColor =
            new Color(0.498f, 0.788f, 0.498f, 0.35f);
        private static readonly Color TrimColor =
            new Color(0.78f, 0.24f, 0.24f, 0.34f);
        private static readonly Color PadColor =
            new Color(0.32f, 0.62f, 0.32f, 0.34f);
        private static readonly Color BeatColor =
            new Color(0.30f, 0.72f, 0.92f, 0.62f);
        private static readonly Color DownbeatColor =
            new Color(0.30f, 0.72f, 0.92f, 1.0f);
        private static readonly Color ProgressColor =
            new Color(1.0f, 0.82f, 0.18f, 1.0f);

        private readonly List<int> _visibleBeats = new List<int>(64);
        private FalsumAudioPreprocessEnvelopeSample[] _samples;
        private double _audioDurationMs;
        private double _delayMs;
        private double _bpm;
        private double _beatsPerMeasure = 4.0;
        private double _viewStartMs;
        private double _viewDurationMs = 30000.0;
        private double _progressMs = double.NaN;
        private bool _dragging;
        private double _dragStartX;
        private double _dragStartViewStartMs;

        public FalsumAudioPreprocessWaveform()
        {
            pickingMode = PickingMode.Position;
            generateVisualContent += Draw;
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<WheelEvent>(OnWheel);
        }

        public double ViewStartMs
        {
            get { return _viewStartMs; }
        }

        public double ViewDurationMs
        {
            get { return _viewDurationMs; }
        }

        public int EnvelopeSampleCount
        {
            get { return _samples != null ? _samples.Length : 0; }
        }

        public void SetEnvelope(
            FalsumAudioPreprocessEnvelopeSample[] samples,
            double audioDurationMs)
        {
            _samples = samples;
            _audioDurationMs = Math.Max(0.0, audioDurationMs);
            MarkDirtyRepaint();
        }

        public void SetParams(
            double delayMs,
            double bpm,
            double beatsPerMeasure)
        {
            _delayMs = double.IsNaN(delayMs) || double.IsInfinity(delayMs)
                ? 0.0
                : delayMs;
            _bpm = bpm;
            _beatsPerMeasure = beatsPerMeasure > 0.0
                ? beatsPerMeasure
                : 4.0;
            MarkDirtyRepaint();
        }

        public void SetProgress(double newTimeMs)
        {
            if (double.IsNaN(newTimeMs) || newTimeMs < 0.0)
            {
                if (double.IsNaN(_progressMs))
                {
                    return;
                }
                _progressMs = double.NaN;
                MarkDirtyRepaint();
                return;
            }
            // A repaint per frame while auditioning is the point: the line is a
            // few vec4s, while a texture-backed playhead would cost a blit.
            _progressMs = newTimeMs;
            MarkDirtyRepaint();
        }

        public void ClearProgress()
        {
            if (double.IsNaN(_progressMs))
            {
                return;
            }
            _progressMs = double.NaN;
            MarkDirtyRepaint();
        }

        public void SetZoom(double durationMs)
        {
            var clamped = Math.Max(500.0, durationMs);
            if (Math.Abs(_viewDurationMs - clamped) < 0.001)
            {
                return;
            }
            _viewDurationMs = clamped;
            ClampViewStart();
            MarkDirtyRepaint();
        }

        public void CenterOn(double timeMs)
        {
            _viewStartMs = timeMs - _viewDurationMs * 0.5;
            ClampViewStart();
            MarkDirtyRepaint();
        }

        // ----------------------------------------------------------------
        // Input
        // ----------------------------------------------------------------

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0)
            {
                return;
            }
            _dragging = true;
            _dragStartX = evt.position.x;
            _dragStartViewStartMs = _viewStartMs;
            PointerCaptureHelper.CapturePointer(this, evt.pointerId);
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (!_dragging)
            {
                return;
            }
            var rect = contentRect;
            if (rect.width < 2.0f)
            {
                return;
            }
            var deltaPx = evt.position.x - _dragStartX;
            var deltaMs = -deltaPx / rect.width * _viewDurationMs;
            _viewStartMs = _dragStartViewStartMs + deltaMs;
            ClampViewStart();
            MarkDirtyRepaint();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (!_dragging)
            {
                return;
            }
            _dragging = false;
            PointerCaptureHelper.ReleasePointer(this, evt.pointerId);
        }

        private void OnWheel(WheelEvent evt)
        {
            if (evt.delta.y == 0.0f)
            {
                return;
            }
            // Zoom around the cursor, not around the left edge: the user is
            // looking at whatever is under the pointer.
            var rect = contentRect;
            if (rect.width < 2.0f)
            {
                return;
            }
            var cursorFraction = Mathf.Clamp01(
                (evt.mousePosition.x - rect.xMin) / rect.width);
            var anchorMs = _viewStartMs + cursorFraction * _viewDurationMs;
            var factor = evt.delta.y > 0.0f ? 1.25 : 0.8;
            _viewDurationMs = Mathf.Clamp(
                (float)(_viewDurationMs * factor),
                500.0f,
                300000.0f);
            _viewStartMs = anchorMs - cursorFraction * _viewDurationMs;
            ClampViewStart();
            MarkDirtyRepaint();
            evt.StopPropagation();
        }

        private void ClampViewStart()
        {
            var extent = Math.Max(_audioDurationMs, _viewDurationMs);
            var maxStart = Math.Max(0.0, extent - _viewDurationMs);
            var minStart = Math.Min(0.0, extent - _viewDurationMs);
            _viewStartMs = _viewStartMs < minStart
                ? minStart
                : (_viewStartMs > maxStart ? maxStart : _viewStartMs);
        }

        // ----------------------------------------------------------------
        // Drawing
        // ----------------------------------------------------------------

        private void Draw(MeshGenerationContext context)
        {
            var rect = contentRect;
            if (rect.width < 2.0f || rect.height < 2.0f)
            {
                return;
            }
            var painter = context.painter2D;

            painter.fillColor = BackgroundColor;
            painter.BeginPath();
            painter.MoveTo(new Vector2(rect.xMin, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax, rect.yMax));
            painter.LineTo(new Vector2(rect.xMin, rect.yMax));
            painter.ClosePath();
            painter.Fill();

            DrawDelayBlock(painter, rect);
            DrawWave(painter, rect);
            DrawBeats(painter, rect);
            DrawProgress(painter, rect);
        }

        private void DrawDelayBlock(Painter2D painter, Rect rect)
        {
            if (Math.Abs(_delayMs) < 0.001)
            {
                return;
            }
            // A positive audio delay inserts silence, so the block marks a
            // region that has no source audio. A negative delay trims the head,
            // so the block marks audio the user is about to lose. Red for
            // loss, green for silence, matching the design.
            var startMs = _delayMs > 0.0 ? 0.0 : _delayMs;
            var endMs = _delayMs > 0.0 ? _delayMs : 0.0;
            var x0 = TimeToX(rect, startMs);
            var x1 = TimeToX(rect, endMs);
            if (x1 <= rect.xMin || x0 >= rect.xMax)
            {
                return;
            }
            x0 = Math.Max(x0, rect.xMin);
            x1 = Math.Min(x1, rect.xMax);
            if (x1 - x0 < 0.5f)
            {
                return;
            }
            painter.fillColor = _delayMs > 0.0 ? PadColor : TrimColor;
            painter.BeginPath();
            painter.MoveTo(new Vector2(x0, rect.yMin));
            painter.LineTo(new Vector2(x1, rect.yMin));
            painter.LineTo(new Vector2(x1, rect.yMax));
            painter.LineTo(new Vector2(x0, rect.yMax));
            painter.ClosePath();
            painter.Fill();
        }

        private void DrawWave(Painter2D painter, Rect rect)
        {
            if (_samples == null ||
                _samples.Length == 0 ||
                _audioDurationMs <= 0.0)
            {
                return;
            }
            var midY = (rect.yMin + rect.yMax) * 0.5f;
            var halfHeight = rect.height * 0.5f - 2.0f;
            var pixelColumns = Math.Max(
                1,
                (int)Mathf.Round(rect.width));

            painter.strokeColor = WaveColor;
            painter.lineWidth = 1.0f;
            painter.BeginPath();
            var started = false;
            for (var column = 0; column < pixelColumns; column++)
            {
                var x = rect.xMin + column;
                var timeMs = _viewStartMs +
                             (column + 0.5) /
                             pixelColumns * _viewDurationMs;
                var index = EnvelopeIndexAt(timeMs);
                if (index < 0)
                {
                    started = false;
                    continue;
                }
                var sample = _samples[index];
                var top = midY - sample.Max * halfHeight;
                var bottom = midY - sample.Min * halfHeight;
                if (top > bottom)
                {
                    var swap = top;
                    top = bottom;
                    bottom = swap;
                }
                if (bottom - top < 1.0f)
                {
                    bottom = top + 1.0f;
                }
                painter.MoveTo(new Vector2(x, top));
                painter.LineTo(new Vector2(x, bottom));
                started = true;
            }
            painter.Stroke();
            if (!started)
            {
                return;
            }

            // A faint filled band makes the peaks readable at 5 s/box without
            // turning the plot into a solid block.
            var minPixel = Math.Max(
                0,
                (int)Mathf.Round(
                    (float)(-0.03 * _viewDurationMs)));
            var maxPixel = Math.Min(
                pixelColumns - 1,
                (int)Mathf.Round(
                    (float)(_viewDurationMs +
                            0.03 * _viewDurationMs)));
            painter.fillColor = WaveFillColor;
            painter.BeginPath();
            painter.MoveTo(new Vector2(rect.xMin + minPixel, midY));
            for (var column = minPixel; column <= maxPixel; column++)
            {
                var timeMs = _viewStartMs +
                             column / pixelColumns * _viewDurationMs;
                var index = EnvelopeIndexAt(timeMs);
                var value = index >= 0 ? _samples[index].Max : 0.0f;
                painter.LineTo(new Vector2(
                    rect.xMin + column,
                    midY - value * halfHeight));
            }
            painter.LineTo(new Vector2(rect.xMin + maxPixel, midY));
            painter.ClosePath();
            painter.Fill();
        }

        /// <summary>
        /// Source-audio time for a position on the new timeline.
        ///
        /// THIS CONVERSION IS THE WHOLE POINT OF THE WINDOW and it is public so
        /// the self check can pin it: a positive delay pads the head with
        /// silence, so new time t maps to source time t - delay; a negative
        /// delay trims the head, so new time t maps to source time t + |delay|.
        /// Sampling with the raw t (the original code) repainted only the delay
        /// block while the waveform itself never moved.
        ///
        /// Returns a negative value for the padded region, which callers must
        /// treat as "no audio here".
        /// </summary>
        public static double SourceTimeMsFor(
            double newTimeMs,
            double delayMs)
        {
            return newTimeMs - delayMs;
        }

        private int EnvelopeIndexAt(double newTimeMs)
        {
            if (_samples == null ||
                _samples.Length == 0 ||
                _audioDurationMs <= 0.0)
            {
                return -1;
            }
            var sourceTimeMs = SourceTimeMsFor(newTimeMs, _delayMs);
            if (sourceTimeMs < 0.0 || sourceTimeMs > _audioDurationMs)
            {
                return -1;
            }
            var index = (int)(sourceTimeMs / _audioDurationMs *
                              _samples.Length);
            if (index < 0)
            {
                return -1;
            }
            return index < _samples.Length ? index : _samples.Length - 1;
        }

        private void DrawBeats(Painter2D painter, Rect rect)
        {
            var beatDurationMs =
                FalsumAudioPreprocessMath.BeatDurationMs(_bpm);
            if (!(beatDurationMs > 0.0) ||
                _viewDurationMs <= 0.0)
            {
                return;
            }
            // Cap the line count: at 5 s/box a 60 s window would be 12 lines,
            // but a pathological BPM plus a wide window can otherwise emit
            // thousands of strokes.
            var maxLines = Math.Max(
                16,
                (int)Mathf.Min(512.0f, rect.width));
            var beatsPerMeasure = Mathf.Max(
                1,
                Mathf.RoundToInt((float)_beatsPerMeasure));
            var collected =
                FalsumAudioPreprocessMath.CollectVisibleBeats(
                    _viewStartMs,
                    _viewStartMs + _viewDurationMs,
                    beatDurationMs,
                    maxLines,
                    _visibleBeats);

            for (var i = 0; i < collected; i++)
            {
                var beatIndex = _visibleBeats[i];
                var x = TimeToX(rect, beatIndex * beatDurationMs);
                var downbeat =
                    FalsumAudioPreprocessMath.IsMeasureFirstBeat(
                        beatIndex,
                        beatsPerMeasure);
                painter.strokeColor = downbeat
                    ? DownbeatColor
                    : BeatColor;
                painter.lineWidth = downbeat
                    ? DownbeatLineWidth
                    : BeatLineWidth;
                painter.BeginPath();
                painter.MoveTo(new Vector2(
                    x,
                    downbeat ? rect.yMin : rect.yMin + rect.height * 0.34f));
                painter.LineTo(new Vector2(
                    x,
                    downbeat ? rect.yMax : rect.yMin + rect.height * 0.66f));
                painter.Stroke();
            }
        }

        private void DrawProgress(Painter2D painter, Rect rect)
        {
            if (double.IsNaN(_progressMs))
            {
                return;
            }
            var x = TimeToX(rect, _progressMs);
            if (x < rect.xMin || x > rect.xMax)
            {
                return;
            }
            painter.strokeColor = ProgressColor;
            painter.lineWidth = ProgressLineWidth;
            painter.BeginPath();
            painter.MoveTo(new Vector2(x, rect.yMin));
            painter.LineTo(new Vector2(x, rect.yMax));
            painter.Stroke();
        }

        private float TimeToX(Rect rect, double timeMs)
        {
            if (_viewDurationMs <= 0.0)
            {
                return rect.xMin;
            }
            var fraction = (timeMs - _viewStartMs) / _viewDurationMs;
            return rect.xMin + (float)fraction * rect.width;
        }
    }
}
