// ==== 片段：从箭头网格实测 X 极值（不用硬编码） ====
// 源文件: Assets/InFalsus/Gameplay/Presentation/Notes/NoteRenderer.cs
// 行范围: 1732-1756 （共 25 行；本片段为逐字摘录，未改代码）
// 说明  : 修复前用 ±0.08 魔数，导致尾巴插进箭头内部。
// ---------------------------------------------------------------------------

	private static float FlickHeadWorldWidth(NoteVisual visual)
	{
		if ((Object)(object)visual.FlickHead == (Object)null)
		{
			return 0.232f;
		}
		MeshFilter headFilter = ((Component)visual.FlickHead).GetComponent<MeshFilter>();
		if ((Object)(object)headFilter == (Object)null ||
			(Object)(object)headFilter.sharedMesh == (Object)null)
		{
			return 0.232f;
		}
		Vector3[] headVerts = headFilter.sharedMesh.vertices;
		float minX = float.MaxValue;
		float maxX = float.MinValue;
		for (int i = 0; i < headVerts.Length; i++)
		{
			float px = visual.RootTransform.InverseTransformPoint(
				visual.FlickHead.TransformPoint(headVerts[i])).x;
			minX = Mathf.Min(minX, px);
			maxX = Mathf.Max(maxX, px);
		}
		return Mathf.Abs(maxX - minX);
	}

