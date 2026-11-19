// ==== 片段：重开/Seek 时重置视觉 ====
// 源文件: Assets/InFalsus/Gameplay/Runtime/Bootstrap/GameplayBootstrap.cs
// 行范围: 970-980 （共 11 行；本片段为逐字摘录，未改代码）
// 说明  : ResetVisuals 把在飞的 burst 全部回收，避免重开后残影。
// ---------------------------------------------------------------------------

		if ((Object)(object)_hitAudio != (Object)null)
		{
			_hitAudio.ResetAudio();
		}
		if ((Object)(object)_flickEffects != (Object)null)
		{
			_flickEffects.ResetVisuals();
		}
		if ((Object)(object)_noteEffects != (Object)null)
		{
			_noteEffects.ResetVisuals();
