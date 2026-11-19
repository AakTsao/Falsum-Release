// ==== 片段：RebuildChartRuntime 后重绑引擎 ====
// 源文件: Assets/InFalsus/Gameplay/Runtime/Bootstrap/GameplayBootstrap.cs
// 行范围: 508-516 （共 9 行；本片段为逐字摘录，未改代码）
// 说明  : Chart 换了之后必须 SetEngine，否则特效收不到新谱面的判定事件。
// ---------------------------------------------------------------------------

		{
			_hitAudio.Rebuild(_chart, _engine);
		}
		if ((Object)(object)_flickEffects != (Object)null)
		{
			_flickEffects.SetEngine(_engine);
		}
		if ((Object)(object)_noteEffects != (Object)null)
		{
