// ==== 片段：★ 唯一的实例化点：NoteRenderer 14 参构造 + FlickHitEffectRenderer.Initialize ====
// 源文件: Assets/InFalsus/Gameplay/Runtime/Bootstrap/GameplayBootstrap.cs
// 行范围: 299-308 （共 10 行；本片段为逐字摘录，未改代码）
// 说明  : flickLeftPrefab/flickRightPrefab 从第 3/4 个参数位注入；FlickHitEffectRenderer 需要 6 个判定反馈贴图/材质。
// ---------------------------------------------------------------------------

		_hitAudio.Initialize(_chart, _engine, LoadRequiredClip(skyAreaStartClip, "58_note_hit_skylane_draft1"), LoadRequiredClip(laneTapHitClip, "59_note_hit_3_draft2"), LoadRequiredClip(sideTapHitClip, "59_note_side_hit_draft2"), LoadRequiredClip(flickHitClip, "60_note_flick_5_draft2"), LoadRequiredClip(groundHoldLoopClip, "58_note_hold_floor+sides_draft1"), LoadRequiredClip(skyAreaHoldLoopClip, "58_note_hold_skylane_5"), _bassAudio);
		long hitAudioMs = startupWatch.ElapsedMilliseconds;
		_noteRenderer = new NoteRenderer(_binding, _motion, _chart, skyAreaPrefab, skyAreaMaterial, holdPrefab, flickLeftPrefab, flickRightPrefab, tapPrefab, tapSpanPrefab, sideLeftTapPrefab, sideRightTapPrefab, sideLeftHoldPrefab, sideRightHoldPrefab);
		SkyJudgementLineEffect skyLineEffect =
			((Component)this).gameObject.AddComponent<SkyJudgementLineEffect>();
		skyLineEffect.Initialize(_binding);
		long noteRendererMs = startupWatch.ElapsedMilliseconds;
		_flickEffects = ((Component)this).gameObject.AddComponent<FlickHitEffectRenderer>();
		_flickEffects.Initialize(_engine, _binding, judgementExactPlusTexture, judgementExactTexture, judgementNearTexture, judgementBreakTexture, judgementFeedbackMaterial);
		_noteEffects = ((Component)this).gameObject.AddComponent<NoteHitEffectRenderer>();
