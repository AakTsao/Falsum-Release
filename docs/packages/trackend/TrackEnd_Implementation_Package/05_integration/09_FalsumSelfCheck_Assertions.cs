// ==== TrackEnd 依赖片段（非完整文件）
// 源文件: Assets/InFalsus/Editor/FalsumSelfCheck.cs
// 行范围: 631-660
// 自检断言（Shader Pass 数/遮罩尺寸/UV/种子）

		}
		Require(File.Exists(Path.Combine(Application.streamingAssetsPath, "Falsum", "Playfield", "playfieldBG.png")), "Track-end playfield image is missing.");
		Shader maskShader = Shader.Find("InFalsus/FalsumTrackEndDiamondMask");
		Shader blurShader = Shader.Find("InFalsus/FalsumTrackEndDiamondBlur");
		Shader overlayShader = Shader.Find("InFalsus/FalsumTrackEndDiamondOverlay");
		Require((Object)(object)maskShader != (Object)null, "Track-end mask shader is missing.");
		Require((Object)(object)blurShader != (Object)null, "Track-end blur shader is missing.");
		Require((Object)(object)overlayShader != (Object)null, "Track-end overlay shader is missing.");
		Require(maskShader.passCount == 3, "Track-end mask shader must have three passes.");
		Material maskMaterial = new Material(maskShader);
		Require(maskMaterial.HasProperty("_TimeOverride"), "Track-end mask shader is missing _TimeOverride.");
		Object.DestroyImmediate(maskMaterial);
		Require(blurShader.passCount == 2, "Track-end blur shader must have two passes.");
		Require(overlayShader.passCount >= 1, "Track-end overlay shader has no pass.");
		Require((Object)(object)Shader.Find("InFalsus/FalsumTrackEndMask") == (Object)null, "Legacy TrackEnd mask shader is still present.");
		Require((Object)(object)Shader.Find("InFalsus/FalsumTrackEndBlur") == (Object)null, "Legacy TrackEnd blur shader is still present.");
		Require((Object)(object)Shader.Find("InFalsus/FalsumTrackEndOverlay") == (Object)null, "Legacy TrackEnd overlay shader is still present.");
		string installerError;
		Require(FalsumTrackEndComposerInstaller.IsInstalled(out installerError), installerError);
		Require(FalsumTrackEndComposer.GetMaskSize(3840) == new Vector2Int(1200, 800), "Track-end mask size at 3840 is incorrect.");
		Require(FalsumTrackEndComposer.GetMaskSize(2560) == new Vector2Int(900, 600), "Track-end mask size at 2560 is incorrect.");
		Require(FalsumTrackEndComposer.OutputUvMax == 0.5f, "Track-end output must show only the lower half.");
		int seededHashA = FalsumTrackEndComposer.ComputeTriangleLayoutHash(1337);
		int seededHashB = FalsumTrackEndComposer.ComputeTriangleLayoutHash(1337);
		int otherSeedHash = FalsumTrackEndComposer.ComputeTriangleLayoutHash(1338);
		Require(seededHashA == seededHashB, "Track-end fixed seed is not deterministic.");
		Require(seededHashA != otherSeedHash, "Track-end random seed has no effect.");
		checks.Add("background_resources");
	}

