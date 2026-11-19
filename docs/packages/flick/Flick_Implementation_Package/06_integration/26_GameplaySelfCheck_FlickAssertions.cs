// ==== 片段：★ 现成的 Flick 验收断言（6 个资产 + 动画时长 ≥0.5s） ====
// 源文件: Assets/InFalsus/Editor/GameplaySelfCheck.cs
// 行范围: 45-66 （共 22 行；本片段为逐字摘录，未改代码）
// 说明  : 可直接搬到别的工程当回归门；缺任一资产会 throw。
// ---------------------------------------------------------------------------


	private static void ValidateFlickEffects()
	{
		RuntimeAnimatorController val = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/InFalsus/Recovered/AnimatorOverrideController/FlickBurst_0 R.overrideController");
		RuntimeAnimatorController leftController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/InFalsus/Recovered/AnimatorController/FlickBurst_0.controller");
		Texture2D exact = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/InFalsus/Recovered/Texture2D/judgement-02-Exact.png");
		Texture2D exactPlus = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/InFalsus/Recovered/Texture2D/judgement-01-ExactPlus.png");
		Sprite rightFrame = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/InFalsus/Recovered/Sprite/FlickBurstR_0.asset");
		Sprite leftFrame = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/InFalsus/Recovered/Sprite/FlickBurst_0.asset");
		if ((Object)(object)val == (Object)null || (Object)(object)leftController == (Object)null || (Object)(object)exact == (Object)null || (Object)(object)exactPlus == (Object)null || (Object)(object)rightFrame == (Object)null || (Object)(object)leftFrame == (Object)null)
		{
			throw new InvalidOperationException("Flick hit-effect assets are incomplete.");
		}
		AnimationClip[] rightClips = val.animationClips;
		AnimationClip[] leftClips = leftController.animationClips;
		if (rightClips.Length == 0 || leftClips.Length == 0 || rightClips[0].length < 0.5f || leftClips[0].length < 0.5f)
		{
			throw new InvalidOperationException("FlickBurst animation clips are missing or too short.");
		}
		Debug.Log((object)("[InFalsus] Flick effects passed: right=" + rightClips[0].length.ToString("0.000") + "s left=" + leftClips[0].length.ToString("0.000") + "s"));
	}

