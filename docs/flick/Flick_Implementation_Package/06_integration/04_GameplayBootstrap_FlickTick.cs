// ==== 片段：★ 每帧 Tick：注意它在「是否播放」判断之外 ====
// 源文件: Assets/InFalsus/Gameplay/Runtime/Bootstrap/GameplayBootstrap.cs
// 行范围: 415-423 （共 9 行；本片段为逐字摘录，未改代码）
// 说明  : 暂停时仍以 paused=true 推进淡出，所以暂停后残留的 burst 会自然消失。把它挪进 _clock.IsPlaying 分支会导致暂停时特效卡住。
// ---------------------------------------------------------------------------

			_sideLaneScroller.Update(now);
			_laneState.Update(now);
			_measureLines.Update(now);
		}
		if ((Object)(object)_flickEffects != (Object)null)
		{
			_flickEffects.Tick(_clock.TimeMs, !_clock.IsPlaying);
		}
		if ((Object)(object)_noteEffects != (Object)null)
