// ==== TrackEnd 依赖片段（非完整文件）
// 源文件: Assets/InFalsus/Gameplay/Runtime/Bootstrap/GameplayBootstrap.cs
// 行范围: 310-314
// 重开谱面时把时间归零

		_sideLaneScroller = new SideLaneSurfaceScroller(_binding, _motion);
		_presentationClock = new PresentationAnimationClock();
		_backgroundPresenter?.TrackEndComposer?.SetPresentationTime(0.0);
		_laneState = new LaneStateController(_binding, _chart, _motion);
		_measureLines = new MeasureLineRenderer(_binding, _chart, _motion);
