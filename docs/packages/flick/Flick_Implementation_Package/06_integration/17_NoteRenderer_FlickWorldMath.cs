// ==== 片段：flick 世界坐标换算：锚点与宽度 ====
// 源文件: Assets/InFalsus/Gameplay/Presentation/Notes/NoteRenderer.cs
// 行范围: 2672-2680 （共 9 行；本片段为逐字摘录，未改代码）
// 说明  : FlickAnchorX 走 TrackX(FlickHeadPos)；FlickWorldWidth = FlickWidth * 1.62。
// ---------------------------------------------------------------------------

	private float FlickAnchorX(ChartNote note)
	{
		return TrackX((float)note.FlickHeadPos);
	}

	private static float FlickWorldWidth(ChartNote note)
	{
		return Mathf.Max(0f, (float)note.FlickWidth) * 1.62f;
	}
