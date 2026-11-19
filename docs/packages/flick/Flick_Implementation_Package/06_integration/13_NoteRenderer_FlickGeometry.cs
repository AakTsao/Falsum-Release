// ==== 片段：★★ 核心：尾巴几何初始化（缩放/对齐/条纹密度） ====
// 源文件: Assets/InFalsus/Gameplay/Presentation/Notes/NoteRenderer.cs
// 行范围: 1621-1663 （共 43 行；本片段为逐字摘录，未改代码）
// 说明  : X 按 (span-headWidth)/meshLen 缩放，Y 只吃 FlickTailHeightScale，然后 AlignFlickPlane 做顶点级对齐，最后算 FlickStripesPerUnit。
// ---------------------------------------------------------------------------

		if (note.Kind == NoteKind.Flick)
		{
			visual.FlickHead = FindChildContaining(clone.transform, "Flick");
			visual.FlickPlane = FindChildContaining(clone.transform, "Plane");
			if ((Object)(object)visual.FlickPlane != (Object)null)
			{
				visual.FlickPlaneBaseScale = visual.FlickPlane.localScale;
				float span = Mathf.Max(0.05f, FlickWorldWidth(note));
				// Normalise by the plate mesh's ACTUAL local length instead of the
				// old 1.54 magic number (the mesh is 1.536), and scale Y by the same
				// ratio so the tail keeps its real aspect instead of getting shorter.
				MeshFilter planeMeshFilter = ((Component)visual.FlickPlane).GetComponent<MeshFilter>();
				float meshLen = 1.536f;
				if ((Object)(object)planeMeshFilter != (Object)null &&
					(Object)(object)planeMeshFilter.sharedMesh != (Object)null)
				{
					Bounds b = planeMeshFilter.sharedMesh.bounds;
					meshLen = Mathf.Max(0.0001f, b.size.x);
				}
				// The note's width already includes the arrow head, so the plate only
				// covers what is left after the head. Sizing the plate to the FULL
				// width made it overhang the head by exactly the head's width (this
				// is why it looked "too wide" and why the plate showed through the
				// arrow). Subtract the head's own world width here.
				float headWorldWidth = FlickHeadWorldWidth(visual);
				float tailSpan = Mathf.Max(meshLen * 0.02f, span - headWorldWidth);
				float widthRatio = tailSpan / meshLen;
				// Height must NOT follow width: the meshes have fixed intrinsic heights
				// (Plane 0.146 vs Flick 0.160 -> the tail is 91.25% of the arrow), so Y
				// keeps the prefab's own value and only takes the explicit factor.
				visual.FlickPlane.localScale = new Vector3(
					visual.FlickPlaneBaseScale.x * widthRatio,
					visual.FlickPlaneBaseScale.y * FlickTailHeightScale,
					visual.FlickPlaneBaseScale.z);
				AlignFlickPlane(visual);
				visual.FlickTailWorldLength = FlickTailWorldLength(visual);
				// Stripe density must not depend on the flick's width. The plate's UV is
				// always 0..1, so a constant _BarRepeat would make short flicks look denser.
				// Scale the repeat by the tail's actual world length instead, giving a
				// constant number of stripes per world unit for every flick.
				visual.FlickStripesPerUnit = FlickStripesPerWorldUnit;
			}
		}
