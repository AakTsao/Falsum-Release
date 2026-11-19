// ==== TrackEnd 依赖片段（非完整文件）
// 源文件: Assets/InFalsus/Gameplay/Runtime/Bootstrap/GameplayBootstrap.cs
// 行范围: 436-446
// 每帧写表现时间（PresentationAnimationClock）

		var running =
			_running &&
			_clock != null &&
			_clock.IsPlaying;
		_presentationClock.Tick(
			running,
			Time.unscaledDeltaTime);
		var seconds = _presentationClock.TimeSeconds;
		_backgroundPresenter?.TrackEndComposer?.SetPresentationTime(seconds);
	}

