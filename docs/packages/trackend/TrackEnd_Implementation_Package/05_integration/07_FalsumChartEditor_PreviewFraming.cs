// ==== TrackEnd 依赖片段（非完整文件）
// 源文件: Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.cs
// 行范围: 4349-4440
// 谱面预览取景联动 SetPreviewFraming / RestoreChartPreviewFraming

                    Mathf.Tan(
                        _chartPreviewBaseFieldOfView *
                        0.5f *
                        Mathf.Deg2Rad) /
                    opticalZoom;
                camera.fieldOfView =
                    2.0f *
                    Mathf.Atan(halfFovTangent) *
                    Mathf.Rad2Deg;
            }

            var composer = _host.Backgrounds != null
                ? _host.Backgrounds.TrackEndComposer
                : null;
            composer?.SetPreviewFraming(zoom, topInset);
        }

        private void CaptureChartPreviewBaseFieldOfView()
        {
            var camera = Camera.main;
            if (camera == null || camera.orthographic)
            {
                return;
            }
            _chartPreviewBaseFieldOfView = camera.fieldOfView;
            _hasChartPreviewBaseFieldOfView = true;
        }

        private void RestoreChartPreviewFraming()
        {
            if (_host == null || _host.Binding == null)
            {
                return;
            }
            var camera = Camera.main;
            if (camera != null &&
                !camera.orthographic &&
                _hasChartPreviewBaseFieldOfView)
            {
                camera.fieldOfView = _chartPreviewBaseFieldOfView;
            }
            _host.Backgrounds?.TrackEndComposer?.SetPreviewFraming(
                1.0f,
                0.0f);
        }

        private void EnterChartPreviewMode()
        {
            if (!_initialized ||
                _chartPreviewPhase != ChartPreviewPhase.Inactive)
            {
                return;
            }
            if (!RequireLoadedPackage())
            {
                return;
            }
            if (_host.Clock != null && _host.Clock.IsPlaying)
            {
                PausePlaybackAt(_host.Clock.TimeMs);
            }

            PrepareForPlayback();
            _host.StopPreviewTapCues();
            _spaceHeld = false;
            _chartPreviewRateIndex = _rateIndex;
            _rateIndex = 0;
            _host.SetPlaybackRate(PlaybackRates[_rateIndex]);
            _view.SetPlaybackRate(PlaybackRates[_rateIndex]);
            _chartPreviewGridWasEnabled = _grid.Enabled;
            _grid.Enabled = false;
            _view.SetGridEnabled(false);
            // Chart preview is the "what will the player see" view, so it
            // always runs the real track() timing regardless of the editing
            // toggle. Exit restores whatever the editor had.
            _chartPreviewTrackSpeedEffectsWasEnabled =
                _chart.TrackSpeedEffectsEnabled;
            SetTrackSpeedEffectsEnabled(
                true,
                announce: false,
                refresh: false);
            _chartPreviewPhase = ChartPreviewPhase.WaitingForChrome;
            _playheadMs = 0.0;
            RefreshEditorAtChartTime(0.0);
            _view.SetPlaybackState(false);
            _view.SetChartPreviewMode(true);
        }

        private void ExitChartPreviewMode(bool pausePlayback = true)
        {
            if (_chartPreviewPhase == ChartPreviewPhase.Inactive)
            {
