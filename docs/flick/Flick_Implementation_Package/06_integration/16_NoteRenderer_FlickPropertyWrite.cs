// ==== 片段：★ 每帧把 _Color/_BarRepeat/_Opacity 写进 MaterialPropertyBlock ====
// 源文件: Assets/InFalsus/Gameplay/Presentation/Notes/NoteRenderer.cs
// 行范围: 2624-2663 （共 40 行；本片段为逐字摘录，未改代码）
// 说明  : 含 fast-path：色/方向/重复次数都没变就整段跳过，避免每帧刷 MPB。
// ---------------------------------------------------------------------------

	private void ApplyVisualState(NoteVisual visual, float fade, double nowMs)
	{
		ChartNote note = visual.Note;
		if (visual.IsGhost) fade *= visual.GhostAlpha;
		else if ((_editorDimGroundNotes && (note.Kind == NoteKind.Tap || note.Kind == NoteKind.Hold)) ||
			(_editorDimSkyNotes && note.Kind == NoteKind.Flick)) fade *= 0.24f;
		float progress = note.Kind == NoteKind.Hold && note.Holding
			? Mathf.Clamp01((float)((nowMs - note.TimeMs) / Math.Max(1.0, note.DurationMs))) : 0f;
		float repeat = visual.FlickStripesPerUnit > 0f
			? visual.FlickTailWorldLength * visual.FlickStripesPerUnit : 0f;
		// Most TAP/FLICK frames are opaque and unchanged. Preserve the old
		// whole-note fast path before entering native Renderer APIs, while
		// including direction/repeat so changing those cannot strand a color.
		if (visual.HasAppliedVisualState && visual.AppliedKind == note.Kind &&
			Mathf.Abs(visual.AppliedFade - fade) <= 0.00001f &&
			Mathf.Abs(visual.AppliedHoldProgress - progress) <= 0.00001f &&
			visual.AppliedHolding == note.Holding && visual.AppliedTint == visual.Tint &&
			visual.AppliedDirection == note.FlickDirection && visual.AppliedRepeat == repeat)
			return;
		if (visual.PropertyStates == null)
		{
			visual.PropertyStates = new NoteRendererPropertyState[visual.Renderers.Length];
			for (int i = 0; i < visual.Renderers.Length; i++)
				visual.PropertyStates[i] = new NoteRendererPropertyState(visual.Renderers[i]);
		}
		bool force = !visual.HasAppliedVisualState;
		for (int i = 0; i < visual.PropertyStates.Length; i++)
		{
			if (visual.PropertyStates[i].Apply(note.Kind, fade, progress, note.Holding,
				visual.Tint, note.FlickDirection, repeat, force)) PropertyBlockWriteCount++;
		}
		visual.HasAppliedVisualState = true;
		visual.AppliedFade = fade;
		visual.AppliedHoldProgress = progress;
		visual.AppliedRepeat = repeat;
		visual.AppliedHolding = note.Holding;
		visual.AppliedTint = visual.Tint;
		visual.AppliedKind = note.Kind;
		visual.AppliedDirection = note.FlickDirection;
	}
