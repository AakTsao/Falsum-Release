// ==== 片段：★ AlignFlickPlane：把尾巴顶点搬到箭头后缘 ====
// 源文件: Assets/InFalsus/Gameplay/Presentation/Notes/NoteRenderer.cs
// 行范围: 1792-1830 （共 39 行；本片段为逐字摘录，未改代码）
// 说明  : 注意它改写的是 INSTANCE mesh 顶点，不是 Transform——所以 FlickTailWorldLength 必须读实例网格。
// ---------------------------------------------------------------------------

	private static void AlignFlickPlane(NoteVisual visual)
	{
		MeshFilter planeFilter = ((Component)visual.FlickPlane).GetComponent<MeshFilter>();
		MeshFilter headFilter = ((Object)(object)visual.FlickHead != (Object)null) ? ((Component)visual.FlickHead).GetComponent<MeshFilter>() : null;
		if ((Object)(object)planeFilter == (Object)null || (Object)(object)headFilter == (Object)null || (Object)(object)headFilter.sharedMesh == (Object)null)
		{
			return;
		}
		Mesh planeMesh = planeFilter.mesh;
		Vector3[] planeVertices = planeMesh.vertices;
		if (planeVertices.Length != 4)
		{
			return;
		}
		// ---- head extent in ROOT space ----
		Vector3[] headVertices = headFilter.sharedMesh.vertices;
		if (headVertices.Length == 0)
		{
			return;
		}
		float headMinX = float.MaxValue;
		float headMaxX = float.MinValue;
		float headMinY = float.MaxValue;
		float headMaxY = float.MinValue;
		for (int k = 0; k < headVertices.Length; k++)
		{
			Vector3 p = visual.RootTransform.InverseTransformPoint(
				visual.FlickHead.TransformPoint(headVertices[k]));
			headMinX = Mathf.Min(headMinX, p.x);
			headMaxX = Mathf.Max(headMaxX, p.x);
			headMinY = Mathf.Min(headMinY, p.y);
			headMaxY = Mathf.Max(headMaxY, p.y);
		}
		float headCenterX = (headMinX + headMaxX) * 0.5f;

		// ---- plate corners in ROOT space ----
		Vector3[] rootLocal = new Vector3[4];
		float plateMinX = float.MaxValue;
		float plateMaxX = float.MinValue;
