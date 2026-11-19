// ==== 片段：★ 方向 → 模板选择：4 用 Right，其余用 Left ====
// 源文件: Assets/InFalsus/Gameplay/Presentation/Notes/NoteRenderer.cs
// 行范围: 1520-1535 （共 16 行；本片段为逐字摘录，未改代码）
// 说明  : FlickDirection==4 → _flickRightPrefab(即 v5-3R)，否则 _flickLeftPrefab(v5-3)。
// ---------------------------------------------------------------------------

		//IL_028b: Unknown result type (might be due to invalid IL or missing references)
		//IL_03e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_03ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_0433: Unknown result type (might be due to invalid IL or missing references)
		GameObject template;
		if (note.Kind == NoteKind.Flick)
		{
			template = ((note.FlickDirection == 4) ? _flickRightPrefab : _flickLeftPrefab);
		}
		else if (note.Kind == NoteKind.Hold)
		{
			if (note.StartLane != 0)
			{
				template = ((note.StartLane != 5) ? _holdPrefab : _sideRightHoldPrefab);
			}
			else
