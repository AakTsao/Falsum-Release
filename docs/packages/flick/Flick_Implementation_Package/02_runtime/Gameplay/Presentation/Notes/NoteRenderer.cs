using System;
using System.Collections.Generic;
using InFalsus.Gameplay.Core.Charts;
using InFalsus.Gameplay.Core.Judgement;
using InFalsus.Gameplay.Core.Timing;
using InFalsus.Gameplay.Falsum.Core;
using InFalsus.Gameplay.Falsum.Runtime;
using InFalsus.Gameplay.Runtime.SceneBinding;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace InFalsus.Gameplay.Presentation.Notes
{

public sealed class NoteRenderer
{
	private struct NoteVisualKey : IEquatable<NoteVisualKey>
	{
		public NoteKind Kind;

		public int StartLane;

		public int WidthLanes;

		public double DurationMs;

		public double StartPos;

		public double StartWidth;

		public int FlickDirection;

		public static NoteVisualKey FromNote(ChartNote note)
		{
			return new NoteVisualKey
			{
				Kind = note.Kind,
				StartLane = note.StartLane,
				WidthLanes = note.WidthLanes,
				DurationMs = note.DurationMs,
				StartPos = note.StartPos,
				StartWidth = note.StartWidth,
				FlickDirection = note.FlickDirection
			};
		}

		public bool Equals(NoteVisualKey other)
		{
			if (Kind == other.Kind && StartLane == other.StartLane && WidthLanes == other.WidthLanes && DurationMs.Equals(other.DurationMs) && StartPos.Equals(other.StartPos) && StartWidth.Equals(other.StartWidth))
			{
				return FlickDirection == other.FlickDirection;
			}
			return false;
		}

		public override bool Equals(object obj)
		{
			if (obj is NoteVisualKey)
			{
				return Equals((NoteVisualKey)obj);
			}
			return false;
		}

		public override int GetHashCode()
		{
			return ((((((((((((int)Kind * 397) ^ StartLane) * 397) ^ WidthLanes) * 397) ^ DurationMs.GetHashCode()) * 397) ^ StartPos.GetHashCode()) * 397) ^ StartWidth.GetHashCode()) * 397) ^ FlickDirection;
		}
	}

	private sealed class NoteVisual
	{
		public ChartNote Note;

		public GameObject Root;

		public Transform RootTransform;

		public Renderer[] Renderers;

		public NoteRendererPropertyState[] PropertyStates;

		public Vector3 BaseLocalPosition;

		public Quaternion BaseLocalRotation;

		public Vector3 BaseLocalScale;

		public Color Tint = Color.white;

		public Transform HoldFront;

		public Transform HoldMiddle;

		public Transform HoldBack;

		public MeshRenderer HoldMiddleRenderer;

		public MeshRenderer HoldBackRenderer;

		public float HoldFrontLength;

		public float HoldMiddleBaseLength;

		public float HoldBackBaseLength;

		// Per-note instance mesh of the middle segment (RecenterHoldMesh already
		// instanced it) plus its authored atlas UVs, so the repeating band can be
		// displaced without touching the shared material.
		public Mesh HoldMiddleMesh;

		public Vector2[] HoldMiddleBaseUv;

		public Vector2[] HoldMiddleUv;

		// v of the tail end of the length ramp; the window is anchored here.
		public float HoldMiddleUvTail;

		// v of the head end; together with the tail it is the authored band
		// the displaced UVs must wrap inside.
		public float HoldMiddleUvHead;

		// Geometry scale the current UV window was built for (-1 = never applied).
		public float AppliedHoldUvScale = -1f;

		public Transform FlickHead;

		public Transform FlickPlane;

		public Vector3 FlickPlaneBaseScale;

		// Stripes per world unit for this flick, so density stays constant
		// regardless of the note's width. 0 = use the material default.
		public float FlickStripesPerUnit;

		// World-space tail length measured once when the plate was laid out.
		// Reading mesh.vertices per frame allocated a fresh array for every
		// flick on screen; the plate only changes during setup.
		public float FlickTailWorldLength;

		public bool IsGhost;

		public float GhostAlpha = 1f;

		public bool HasAppliedVisualState;
		public float AppliedFade, AppliedHoldProgress, AppliedRepeat;
		public bool AppliedHolding;
		public Color AppliedTint;
		public NoteKind AppliedKind;
		public int AppliedDirection;

		public NoteVisual()
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		}
	}

	private sealed class SkyVisual
	{
		public int GroupId;

		public readonly List<ChartNote> Segments = new List<ChartNote>();

		public double FirstTimeMs;

		public double LastTimeMs;

		public GameObject Root;

		public Mesh Mesh;

		public MeshRenderer Renderer;

		public MaterialPropertyBlock Block;
		public readonly List<SkyAreaRowCache> RowCaches = new List<SkyAreaRowCache>();
		public bool HasMesh;
		public double MeshScaledNow;
		public bool HasAppearance;
		public Color AppliedColor, AppliedCenter, AppliedGlow;
		public float AppliedTrackLength, AppliedTravel;

		public GameObject PromptRoot;

		public Transform BracketLeft;

		public Transform BracketRight;

		public MeshRenderer ShadowRenderer;

		public bool Logged;

		public bool IsGhost;

		public float GhostAlpha = 1f;

		public readonly List<Vector3> Vertices = new List<Vector3>();

		public readonly List<Vector2> Uv0 = new List<Vector2>();

		public readonly List<Vector2> Uv1 = new List<Vector2>();

		public readonly List<Vector2> Uv2 = new List<Vector2>();

		public readonly List<Vector4> Uv3 = new List<Vector4>();

		public readonly List<Vector4> Uv4 = new List<Vector4>();

		public readonly List<int> Triangles = new List<int>();

	}

	private const float HoldTemplateLengthFallback = 1f;

	private const float HoldJoinOverlap = 0.05f;

	private const float FlickHeadOverlap = 0.08f;
	// 0 = tail starts exactly at the arrow's back edge (no overlap).
	// Positive = tuck the tail slightly UNDER the head (hides AA seam shimmer).
		// 0 = exact butt. Negative = leave a visible gap.
		private const float FlickTailInset = 0.0f;
		// Extra shift of the plate's back vertices. MUST stay 0: this
		// is what used to push the tail into the arrow head.
		private const float FlickVertexSeamOffset = 0.0f;
		// Stripes per WORLD unit along the tail. Constant regardless of the
		// flick's width, so wide and narrow flicks read the same density.
		private const float FlickStripesPerWorldUnit = 11.0f;
		// 1 = keep the mesh's intrinsic height (already 91.25% of the arrow's height).
		// This does NOT scale with the note's width anymore. >1 taller, <1 shorter.
		private const float FlickTailHeightScale = 0.93f;

	private const float TrackHalfWidth = 0.81f;

	private const float SkyTrackWidth = 1.62f;

	private const float SkyY = 0.4698f;

	private const double VisualWindowPaddingMs = 250.0;

	// How much farther than the track end the window reaches. TrackMotion.Fade
	// reaches 0 at exactly TrackLength, so a note created past that point is
	// never drawn; 1.1 overshoots the fade band a little so the creation
	// frame is guaranteed to come before the first visible frame.
	private const double VisualWindowFarEndScale = 1.1;

	// Hard cap on the lead time. Charts in the wild go as low as track(0.02)
	// and track(0.01); without a cap the distance mapping asks for minutes
	// of chart time and keeps hundreds of instances alive for a handful of
	// visible objects. 40 s still covers the whole track down to track
	// speed ~0.09 (most slow sections in the shipped charts are 0.1+), and
	// below that the ceiling degrades gracefully instead of popping notes
	// in at the judgement line. Dense windows are spread over frames by
	// VisualCreateBudgetPerFrame rather than shrunk here.
	private const double VisualWindowCeilingMs = 40000.0;

	// How long a note may outlive the judgement line. Notes past the line are
	// already faded to zero by TrackMotion.Fade, so this only keeps the
	// recycle cost off the frame that crosses the line.
	private const double VisualWindowTrailingMs = 1500.0;

	// Most notes a single frame may create. Creation (instantiate + material
	// clone) is far costlier than reuse, so a seek or a speed change that
	// widens the window must be spread over frames instead of hitching once.
	private const int VisualCreateBudgetPerFrame = 48;

	// Notes this close ahead of the playhead are created regardless of the
	// per-frame budget: they are about to be drawn, so delaying them by a
	// frame would show a hole. Larger than one frame's worth of scroll even
	// at the highest supported flow speed.
	private const double VisualUrgentLeadMs = 250.0;

	private const int MaxPooledVisualsPerKey = 12;

	private const int MaxPooledVisuals = 384;

	private readonly GameplaySceneBinding _binding;

	private readonly Transform _renderRoot;

	private readonly Transform _skyRoot;

	private readonly TrackMotion _motion;

	private readonly Chart _chart;

	private readonly Material _skyAreaMaterial;

	private readonly Mesh _skyBracketMesh;

	private readonly Material _skyBracketMaterial;

	private readonly Material _skyShadowMaterial;

	private readonly GameObject _holdPrefab;

	private readonly GameObject _flickLeftPrefab;

	private readonly GameObject _flickRightPrefab;

	private readonly GameObject _tapPrefab;

	private readonly GameObject _tapSpanPrefab;

	private readonly GameObject _sideLeftTapPrefab;

	private readonly GameObject _sideRightTapPrefab;

	private readonly GameObject _sideLeftHoldPrefab;

	private readonly GameObject _sideRightHoldPrefab;

	private readonly List<NoteVisual> _visuals = new List<NoteVisual>();

	private readonly Dictionary<ChartNote, NoteVisual> _visualByNote = new Dictionary<ChartNote, NoteVisual>();

	private readonly Dictionary<NoteVisualKey, Stack<NoteVisual>>
		_visualPool =
			new Dictionary<NoteVisualKey, Stack<NoteVisual>>();

	private readonly List<SkyVisual> _skies = new List<SkyVisual>();

	private readonly float[] _laneCenterX;

	private readonly List<NoteVisual> _editorGhostVisuals = new List<NoteVisual>();

	private readonly List<SkyVisual> _editorGhostSkies = new List<SkyVisual>();

	private int _pooledVisualCount;

	// Last lead time computed while the track was moving. A stopped track
	// has no distance-to-time mapping, so reuse it instead of collapsing
	// the window and re-creating everything on the next frame.
	private double _lastVisualLeadMs = VisualWindowCeilingMs;

	// Direction of track travel in the last moving frame: +1 means notes
	// approach from the far end (future), -1 means the track scrolls
	// backwards and later notes enter from behind the judgement line.
	private int _lastVisualDirection = 1;

	private Material _editorGhostSkyMaterial;

	private bool _editorDimGroundNotes;

	private bool _editorDimSkyNotes;

	private bool _editorShowGroundNotes = true;

	private bool _editorShowSkyNotes = true;

	// Global SkyArea appearance overrides, sourced from the user's Visual
	// preferences. The fill alpha is absolute (it replaces the material's
	// authored _Color.a), the glow value is a multiplier over the authored
	// _EdgeCenterColor.a / _EdgeGlowColor.a so the 1.0 -> 0/1 authored spread
	// survives. Kept as fields instead of new constructor parameters: the
	// constructor signature is part of the scene-builder contract (B-10).
	private float _skyAreaOpacity =
		FalsumVisualPreferences.DefaultSkyAreaOpacity;

	private float _skyAreaGlow =
		FalsumVisualPreferences.DefaultSkyAreaGlow;

	private static readonly int ColorId = Shader.PropertyToID("_Color");

	private static readonly int EdgeCenterColorId = Shader.PropertyToID("_EdgeCenterColor");

	private static readonly int EdgeGlowColorId = Shader.PropertyToID("_EdgeGlowColor");

	private static readonly int FillCenterColorId = Shader.PropertyToID("_FillCenterColor");

	private static readonly int VisualStartId = Shader.PropertyToID("_VisualStart");

	private static readonly int VisualLengthId = Shader.PropertyToID("_VisualLength");

	private static readonly int TrackVisualLengthId = Shader.PropertyToID("_TrackVisualLength");

	private static readonly int VisualChartProgressId = Shader.PropertyToID("_VisualChartProgress");

	private static readonly int ArchTextureId = Shader.PropertyToID("_ArchTexture");

	private static readonly int DotTextureId = Shader.PropertyToID("_DotTexture");

	private static readonly int TailCapZId = Shader.PropertyToID("_TailCapZ");

	private static readonly int ShowTailOutlineId = Shader.PropertyToID("_ShowTailOutline");

	public int SkyActiveCount { get; private set; }
	public int SkyMeshUploadCount { get; private set; }
	public int SkyRowSampleCount { get; private set; }
	public int PropertyBlockWriteCount { get; private set; }

	public int SkyTotalCount => _skies.Count;

	public int VisualCount => _visuals.Count;

	public int EditorGhostVisualCount => _editorGhostVisuals.Count;

	public int LastSyncReusedCount { get; private set; }

	public int LastSyncCreatedCount { get; private set; }

	public bool EditorDimGroundNotes => _editorDimGroundNotes;

	public bool EditorDimSkyNotes => _editorDimSkyNotes;

	public bool EditorShowGroundNotes => _editorShowGroundNotes;

	public bool EditorShowSkyNotes => _editorShowSkyNotes;

	private Material EditorGhostSkyMaterial
	{
		get
		{
			//IL_0026: Unknown result type (might be due to invalid IL or missing references)
			//IL_002b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0046: Unknown result type (might be due to invalid IL or missing references)
			//IL_0056: Expected Obj, but got Unknown
			if ((Object)(object)_editorGhostSkyMaterial == (Object)null && (Object)(object)_skyAreaMaterial != (Object)null)
			{
				_editorGhostSkyMaterial = new Material(_skyAreaMaterial)
				{
					name = ((Object)_skyAreaMaterial).name + " Editor Ghost",
					renderQueue = 3025
				};
				ScaleMaterialAlpha(_editorGhostSkyMaterial, "_Color", 0.34f);
				ScaleMaterialAlpha(_editorGhostSkyMaterial, "_EdgeCenterColor", 0.55f);
				ScaleMaterialAlpha(_editorGhostSkyMaterial, "_EdgeGlowColor", 0.4f);
			}
			return _editorGhostSkyMaterial;
		}
	}

	public void SetEditorDimmedKinds(bool dimGroundNotes, bool dimSkyNotes)
	{
		_editorDimGroundNotes = dimGroundNotes;
		_editorDimSkyNotes = dimSkyNotes;
	}

	/// <summary>
	/// Applies the user's SkyArea transparency settings. <paramref name="opacity"/>
	/// replaces the fill's authored alpha, <paramref name="glow"/> multiplies
	/// the two edge colours' authored alpha. The values are taken from the
	/// material on every frame (<see cref="UpdateSky"/>), so a rebuild is not
	/// required: the next presentation refresh picks them up.
	/// </summary>
	public void SetSkyAreaAppearance(float opacity, float glow)
	{
		_skyAreaOpacity = Mathf.Clamp01(opacity);
		_skyAreaGlow = Mathf.Clamp01(glow);
	}

	public float SkyAreaOpacity => _skyAreaOpacity;

	public float SkyAreaGlow => _skyAreaGlow;

	public void SetEditorVisibleLayers(bool showGroundNotes, bool showSkyNotes)
	{
		_editorShowGroundNotes = showGroundNotes;
		_editorShowSkyNotes = showSkyNotes;
	}

	public NoteRenderer(GameplaySceneBinding binding, TrackMotion motion, Chart chart, GameObject skyPrefab, Material skyAreaMaterial, GameObject holdPrefab, GameObject flickLeftPrefab, GameObject flickRightPrefab, GameObject tapPrefab, GameObject tapSpanPrefab, GameObject sideLeftTapPrefab, GameObject sideRightTapPrefab, GameObject sideLeftHoldPrefab, GameObject sideRightHoldPrefab)
	{
		//IL_00d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f2: Unknown result type (might be due to invalid IL or missing references)
		//IL_0102: Expected Obj, but got Unknown
		_binding = binding;
		_renderRoot = binding.MasterScene;
		_skyRoot = (((Object)(object)binding.SkyTemplate != (Object)null) ? binding.SkyTemplate.transform.parent : binding.MasterScene);
		_motion = motion;
		_chart = chart;
		_skyAreaMaterial = skyAreaMaterial;
		if ((Object)(object)_skyAreaMaterial != (Object)null)
		{
			ConfigureSkyAreaTexture(_skyAreaMaterial, ArchTextureId);
			ConfigureSkyAreaTexture(_skyAreaMaterial, DotTextureId);
			Shader bracketShader = Resources.Load<Shader>(
				"SkyArea/Shaders/SkyAreaBracket");
			Shader shadowShader = Resources.Load<Shader>(
				"SkyArea/Shaders/SkyAreaShadow");
			Texture2D bracketTexture = Resources.Load<Texture2D>(
				"SkyArea/SquareBracket-Diffuse");
			_skyBracketMesh = LoadBracketMesh();
			if (bracketShader == null ||
				shadowShader == null ||
				bracketTexture == null ||
				_skyBracketMesh == null)
			{
				throw new InvalidOperationException(
					"Original SkyArea bracket resources are missing.");
			}
			_skyBracketMaterial = new Material(bracketShader)
			{
				name = "SkyArea Bracket",
				renderQueue = 3012
			};
			_skyBracketMaterial.SetTexture("_MainTex", bracketTexture);
			_skyBracketMaterial.SetColor("_Color", Color.white);
			_skyShadowMaterial = new Material(shadowShader)
			{
				name = "SkyArea Ground Shadow",
				renderQueue = 3006
			};
			_skyShadowMaterial.SetColor(
				"_Color",
				new Color(0.1415094f, 0.1415094f, 0.1415094f, 0.2941177f));
		}
		_holdPrefab = holdPrefab;
		_flickLeftPrefab = flickLeftPrefab;
		_flickRightPrefab = flickRightPrefab;
		_tapPrefab = tapPrefab;
		_tapSpanPrefab = tapSpanPrefab;
		_sideLeftTapPrefab = sideLeftTapPrefab;
		_sideRightTapPrefab = sideRightTapPrefab;
		_sideLeftHoldPrefab = sideLeftHoldPrefab;
		_sideRightHoldPrefab = sideRightHoldPrefab;
		_laneCenterX = binding.LaneCenterX;
		BuildSkyChains();
	}

	public void ResetVisuals()
	{
		foreach (NoteVisual visual in _visuals)
		{
			if ((Object)(object)visual.Root != (Object)null)
			{
				visual.Root.SetActive(false);
			}
		}
		foreach (SkyVisual sky in _skies)
		{
			if ((Object)(object)sky.Root != (Object)null)
			{
				sky.Root.SetActive(false);
			}
		}
	}

	private static Mesh LoadBracketMesh()
	{
		GameObject bracketAsset = Resources.Load<GameObject>(
			"SkyArea/BRACKET");
		if (bracketAsset == null)
		{
			return null;
		}
		MeshFilter filter = bracketAsset.GetComponentInChildren<MeshFilter>(
			true);
		return filter != null ? filter.sharedMesh : null;
	}

	private static void ConfigureSkyAreaTexture(
		Material material,
		int propertyId)
	{
		Texture texture = material.GetTexture(propertyId);
		if (texture != null)
		{
			texture.wrapMode = TextureWrapMode.Repeat;
		}
	}

	public void Update(double nowMs)
	{
		SkyActiveCount = 0;
		SkyJudgementLineEffect skyLineEffect =
			SkyJudgementLineEffect.Active;
		if (skyLineEffect != null)
		{
			skyLineEffect.BeginFrame();
		}
		EnsureVisualsForTime(nowMs);
		foreach (NoteVisual visual in _visuals)
		{
			UpdateVisual(visual, nowMs);
		}
		foreach (SkyVisual sky in _skies)
		{
			UpdateSky(sky, nowMs);
		}
		if (skyLineEffect != null)
		{
			skyLineEffect.EndFrame();
		}
	}

	public void AddNoteVisual(ChartNote note)
	{
		if (note != null)
		{
			if (note.Kind == NoteKind.SkyArea)
			{
				RebuildSkyAreas();
			}
			else if (FindVisual(note) == null)
			{
				AddVisual(note);
			}
		}
	}

	public void AddNoteVisuals(IList<ChartNote> notes)
	{
		if (notes == null)
		{
			return;
		}
		bool rebuildSky = false;
		for (int i = 0; i < notes.Count; i++)
		{
			ChartNote note = notes[i];
			if (note != null)
			{
				if (note.Kind == NoteKind.SkyArea)
				{
					rebuildSky = true;
				}
				else if (FindVisual(note) == null)
				{
					AddVisual(note);
				}
			}
		}
		if (rebuildSky)
		{
			RebuildSkyAreas();
		}
	}

	public void RemoveNoteVisual(ChartNote note)
	{
		if (note == null)
		{
			return;
		}
		if (note.Kind == NoteKind.SkyArea)
		{
			RebuildSkyAreas();
			return;
		}
		NoteVisual visual = FindVisual(note);
		if (visual != null)
		{
			DestroyObject(visual.Root);
			RemoveVisual(visual);
		}
	}

	public void RemoveNoteVisuals(IList<ChartNote> notes)
	{
		if (notes == null)
		{
			return;
		}
		bool rebuildSky = false;
		for (int i = 0; i < notes.Count; i++)
		{
			ChartNote note = notes[i];
			if (note == null)
			{
				continue;
			}
			if (note.Kind == NoteKind.SkyArea)
			{
				rebuildSky = true;
				continue;
			}
			NoteVisual visual = FindVisual(note);
			if (visual != null)
			{
				DestroyObject(visual.Root);
				RemoveVisual(visual);
			}
		}
		if (rebuildSky)
		{
			RebuildSkyAreas();
		}
	}

	public void RefreshNoteVisual(ChartNote note)
	{
		if (note == null)
		{
			return;
		}
		if (note.Kind == NoteKind.SkyArea)
		{
			RebuildSkyAreas();
			return;
		}
		NoteVisual visual = FindVisual(note);
		if (visual != null)
		{
			DestroyObject(visual.Root);
			RemoveVisual(visual);
		}
		AddVisual(note);
	}

	public void RefreshNoteVisuals(IList<ChartNote> notes)
	{
		if (notes == null)
		{
			return;
		}
		bool rebuildSky = false;
		for (int i = 0; i < notes.Count; i++)
		{
			ChartNote note = notes[i];
			if (note == null)
			{
				continue;
			}
			if (note.Kind == NoteKind.SkyArea)
			{
				rebuildSky = true;
				continue;
			}
			NoteVisual visual = FindVisual(note);
			if (visual != null)
			{
				DestroyObject(visual.Root);
				RemoveVisual(visual);
			}
			AddVisual(note);
		}
		if (rebuildSky)
		{
			RebuildSkyAreas();
		}
	}

	public void RebuildSkyAreas()
	{
		DestroySkyVisuals();
		BuildSkyChains();
	}

	public bool TryGetNoteWorldBounds(ChartNote note, double nowMs, out Bounds bounds)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		bounds = default;
		if (note == null)
		{
			return false;
		}
		if (note.Kind == NoteKind.SkyArea)
		{
			return TryGetSkyNoteBounds(note, nowMs, out bounds);
		}
		List<Vector3> points = new List<Vector3>();
		if (!TryGetNoteWorldPoints(note, nowMs, points))
		{
			return false;
		}
		bounds = new Bounds(points[0], Vector3.zero);
		for (int i = 1; i < points.Count; i++)
		{
			bounds.Encapsulate(points[i]);
		}
		return true;
	}

	public bool TryGetNoteWorldPoints(ChartNote note, double nowMs, List<Vector3> points)
	{
		if (note == null || points == null)
		{
			return false;
		}
		points.Clear();
		if (note.Kind == NoteKind.SkyArea)
		{
			return false;
		}
		NoteVisual visual = FindVisual(note);
		if (visual == null || (Object)(object)visual.Root == (Object)null)
		{
			return false;
		}
		if (note.Kind == NoteKind.Hold)
		{
			AppendMeshPoints((Renderer)(object)(((Object)(object)visual.HoldFront != (Object)null) ? ((Component)visual.HoldFront).GetComponent<MeshRenderer>() : null), points, requireEnabled: true);
			AppendMeshPoints((Renderer)(object)visual.HoldMiddleRenderer, points, requireEnabled: true);
			AppendMeshPoints((Renderer)(object)visual.HoldBackRenderer, points, requireEnabled: true);
			return points.Count >= 3;
		}
		for (int i = 0; i < visual.Renderers.Length; i++)
		{
			Renderer renderer = visual.Renderers[i];
			if (!((Object)(object)renderer == (Object)null) && ((Object)renderer).name.IndexOf("SideGuide", StringComparison.OrdinalIgnoreCase) < 0)
			{
				AppendMeshPoints(renderer, points, requireEnabled: false);
			}
		}
		return points.Count >= 3;
	}

	private static void AppendMeshPoints(Renderer renderer, List<Vector3> points, bool requireEnabled)
	{
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0064: Unknown result type (might be due to invalid IL or missing references)
		//IL_006a: Unknown result type (might be due to invalid IL or missing references)
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Unknown result type (might be due to invalid IL or missing references)
		//IL_0087: Unknown result type (might be due to invalid IL or missing references)
		//IL_008d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0093: Unknown result type (might be due to invalid IL or missing references)
		//IL_0098: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00af: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00eb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0102: Unknown result type (might be due to invalid IL or missing references)
		//IL_0108: Unknown result type (might be due to invalid IL or missing references)
		//IL_010f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0114: Unknown result type (might be due to invalid IL or missing references)
		//IL_0125: Unknown result type (might be due to invalid IL or missing references)
		//IL_012c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0132: Unknown result type (might be due to invalid IL or missing references)
		//IL_0139: Unknown result type (might be due to invalid IL or missing references)
		//IL_013e: Unknown result type (might be due to invalid IL or missing references)
		//IL_014f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0155: Unknown result type (might be due to invalid IL or missing references)
		//IL_015c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0163: Unknown result type (might be due to invalid IL or missing references)
		//IL_0168: Unknown result type (might be due to invalid IL or missing references)
		//IL_0179: Unknown result type (might be due to invalid IL or missing references)
		//IL_0180: Unknown result type (might be due to invalid IL or missing references)
		//IL_0187: Unknown result type (might be due to invalid IL or missing references)
		//IL_018e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0193: Unknown result type (might be due to invalid IL or missing references)
		if (!((Object)(object)renderer == (Object)null) && (!requireEnabled || renderer.enabled))
		{
			MeshFilter filter = ((Component)renderer).GetComponent<MeshFilter>();
			Mesh mesh = (((Object)(object)filter != (Object)null) ? filter.sharedMesh : null);
			if (!((Object)(object)mesh == (Object)null))
			{
				Bounds bounds = mesh.bounds;
				Vector3 min = bounds.min;
				Vector3 max = bounds.max;
				points.Add(((Component)renderer).transform.TransformPoint(new Vector3(min.x, min.y, min.z)));
				points.Add(((Component)renderer).transform.TransformPoint(new Vector3(max.x, min.y, min.z)));
				points.Add(((Component)renderer).transform.TransformPoint(new Vector3(min.x, max.y, min.z)));
				points.Add(((Component)renderer).transform.TransformPoint(new Vector3(max.x, max.y, min.z)));
				points.Add(((Component)renderer).transform.TransformPoint(new Vector3(min.x, min.y, max.z)));
				points.Add(((Component)renderer).transform.TransformPoint(new Vector3(max.x, min.y, max.z)));
				points.Add(((Component)renderer).transform.TransformPoint(new Vector3(min.x, max.y, max.z)));
				points.Add(((Component)renderer).transform.TransformPoint(new Vector3(max.x, max.y, max.z)));
			}
		}
	}

	public bool IsNoteVisualActive(ChartNote note)
	{
		if (note == null || note.Kind == NoteKind.SkyArea)
		{
			return note != null;
		}
		NoteVisual visual = FindVisual(note);
		if (visual != null && (Object)(object)visual.Root != (Object)null)
		{
			return visual.Root.activeInHierarchy;
		}
		return false;
	}

	public string DebugDescribeNoteVisual(ChartNote note)
	{
		NoteVisual visual = FindVisual(note);
		if (visual == null)
		{
			return "visual=none visuals=" + _visuals.Count;
		}
		int renderers = 0;
		int meshes = 0;
		int enabled = 0;
		int active = 0;
		for (int i = 0; i < visual.Renderers.Length; i++)
		{
			Renderer renderer = visual.Renderers[i];
			if (!((Object)(object)renderer == (Object)null))
			{
				renderers++;
				MeshFilter filter = ((Component)renderer).GetComponent<MeshFilter>();
				if ((Object)(object)filter != (Object)null && (Object)(object)filter.sharedMesh != (Object)null)
				{
					meshes++;
				}
				if (renderer.enabled)
				{
					enabled++;
				}
				if (((Component)renderer).gameObject.activeInHierarchy)
				{
					active++;
				}
			}
		}
		return "visuals=" + _visuals.Count + " rootActive=" + ((Object)(object)visual.Root != (Object)null && visual.Root.activeInHierarchy) + " renderers=" + renderers + " meshes=" + meshes + " enabled=" + enabled + " active=" + active;
	}

	public void SetEditorGhost(ChartNote note, Color tint)
	{
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		if (note == null)
		{
			ClearEditorGhost();
			return;
		}
		SetEditorGhosts(new ChartNote[1] { note }, tint);
	}

	public void SetEditorGhosts(IList<ChartNote> notes, Color tint)
	{
		//IL_00a6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a7: Unknown result type (might be due to invalid IL or missing references)
		ClearEditorGhost();
		if (notes == null)
		{
			return;
		}
		for (int i = 0; i < notes.Count; i++)
		{
			ChartNote note = notes[i];
			if (note != null)
			{
				if (note.Kind == NoteKind.SkyArea)
				{
					SkyVisual sky = new SkyVisual
					{
						GroupId = note.GroupId,
						FirstTimeMs = note.TimeMs,
						LastTimeMs = note.EndTimeMs,
						IsGhost = true,
						GhostAlpha = 0.34f
					};
					sky.Segments.Add(note);
					_editorGhostSkies.Add(CreateSky(sky, EditorGhostSkyMaterial, createPrompt: false));
				}
				else
				{
					NoteVisual visual = CreateVisual(note);
					visual.IsGhost = true;
					visual.GhostAlpha = 0.34f;
					visual.Tint = tint;
					_editorGhostVisuals.Add(visual);
				}
			}
		}
	}

	public void UpdateEditorGhost(double nowMs)
	{
		for (int i = 0; i < _editorGhostVisuals.Count; i++)
		{
			UpdateVisual(_editorGhostVisuals[i], nowMs);
		}
		for (int j = 0; j < _editorGhostSkies.Count; j++)
		{
			UpdateSky(_editorGhostSkies[j], nowMs);
		}
	}

	public void ClearEditorGhost()
	{
		for (int i = 0; i < _editorGhostVisuals.Count; i++)
		{
			NoteVisual visual = _editorGhostVisuals[i];
			if (visual != null && (Object)(object)visual.Root != (Object)null)
			{
				DestroyObject(visual.Root);
			}
		}
		_editorGhostVisuals.Clear();
		for (int j = 0; j < _editorGhostSkies.Count; j++)
		{
			SkyVisual sky = _editorGhostSkies[j];
			if (sky != null && (Object)(object)sky.Root != (Object)null)
			{
				DestroyObject(sky.Root);
			}
		}
		_editorGhostSkies.Clear();
	}

	public void Rebuild()
	{
		DestroyVisuals();
		BuildSkyChains();
	}

	public void SyncFromChart()
	{
		LastSyncReusedCount = 0;
		LastSyncCreatedCount = 0;
		DestroyVisuals();
		BuildSkyChains();
	}

	public void Dispose()
	{
		ClearEditorGhost();
		DestroyVisuals();
	}

	private void DestroyVisuals()
	{
		for (int i = 0; i < _visuals.Count; i++)
		{
			NoteVisual visual = _visuals[i];
			if (visual != null && (Object)(object)visual.Root != (Object)null)
			{
				DestroyObject(visual.Root);
			}
		}
		_visuals.Clear();
		_visualByNote.Clear();
		foreach (Stack<NoteVisual> pool in _visualPool.Values)
		{
			while (pool.Count > 0)
			{
				NoteVisual visual = pool.Pop();
				if (visual != null &&
					(Object)(object)visual.Root != (Object)null)
				{
					DestroyObject(visual.Root);
				}
			}
		}
		_visualPool.Clear();
		_pooledVisualCount = 0;
		_lastVisualLeadMs = VisualWindowCeilingMs;
		DestroySkyVisuals();
	}

	private void DestroySkyVisuals()
	{
		for (int i = 0; i < _skies.Count; i++)
		{
			SkyVisual sky = _skies[i];
			if (sky != null && (Object)(object)sky.Root != (Object)null)
			{
				DestroyObject(sky.Root);
			}
		}
		_skies.Clear();
	}

	private static void DestroyObject(GameObject target)
	{
		if (!((Object)(object)target == (Object)null))
		{
			if (Application.isPlaying)
			{
				Object.Destroy((Object)(object)target);
			}
			else
			{
				Object.DestroyImmediate((Object)(object)target);
			}
		}
	}

	private void EnsureVisualsForTime(double nowMs)
	{
		VisualWindowEdges(
			nowMs,
			VisualWindowCeilingMs,
			out double pastEdgeMs,
			out double futureEdgeMs);
		// SkyArea chains are a single mesh per group instead of one object
		// per segment, so they reuse the note window; they do not need a
		// separate (larger) ceiling.
		double skyPastEdgeMs = pastEdgeMs;
		double skyFutureEdgeMs = futureEdgeMs;
		for (int i = _visuals.Count - 1; i >= 0; i--)
		{
			NoteVisual visual = _visuals[i];
			if (visual == null || visual.Note == null || !IsInVisualWindow(visual.Note.TimeMs, visual.Note.EndTimeMs, nowMs, pastEdgeMs, futureEdgeMs))
			{
				RecycleVisualAt(i);
			}
		}
		int createdThisFrame = 0;
		for (int j = 0; j < _chart.Notes.Count; j++)
		{
			ChartNote note = _chart.Notes[j];
			if (note.Kind == NoteKind.SkyArea ||
				FindVisual(note) != null ||
				!IsInVisualWindow(note.TimeMs, note.EndTimeMs, nowMs, pastEdgeMs, futureEdgeMs))
			{
				continue;
			}
			// Notes already on the track are urgent: after a seek or a resume
			// they must appear this frame or the player sees a hole. Only the
			// pre-warmed, not-yet-drawn tail of the window is rationed.
			bool urgent =
				note.TimeMs <= nowMs &&
				(nowMs <= note.EndTimeMs + VisualWindowTrailingMs ||
				 nowMs <= note.TimeMs + VisualUrgentLeadMs);
			if (!urgent && createdThisFrame >= VisualCreateBudgetPerFrame)
			{
				// Ration only the pre-warmed tail. _chart.Notes is not
				// guaranteed to be time-ordered while editing, so breaking
				// here could skip an urgent note sitting behind a future one
				// and punch a hole at the judgement line.
				continue;
			}
			if (AddVisual(note))
			{
				LastSyncCreatedCount++;
				createdThisFrame++;
			}
			else
			{
				LastSyncReusedCount++;
			}
		}
		for (int i2 = _skies.Count - 1; i2 >= 0; i2--)
		{
			SkyVisual sky = _skies[i2];
			if (sky == null)
			{
				_skies.RemoveAt(i2);
			}
			else if (!IsInVisualWindow(sky.FirstTimeMs, sky.LastTimeMs, nowMs, skyPastEdgeMs, skyFutureEdgeMs))
			{
				ReleaseSky(sky);
			}
			else if ((Object)(object)sky.Root == (Object)null)
			{
				CreateSky(sky);
			}
		}
	}

	private double VisualLeadMs(double nowMs, double ceilingMs)
	{
		// A note is only hidden once its world z leaves the track, but the
		// window is a *time* duration. At low track speed the far end of the
		// track is many seconds away, so the old fixed 1250/(0.3*speed)
		// duration (≈1.25 track lengths, hard-capped at 5 s and floored at
		// 0.25 speed) recycled notes while they were still fully opaque in
		// the middle of the track — they then reappeared out of nowhere.
		// Map the visible track length back to chart time instead, so the
		// window always reaches past the far end fade.
		double chartSpeed = _chart.PreviewSpeedMultiplierAt(nowMs);
		if (!(Math.Abs(chartSpeed) > 0.0001))
		{
			// Track stopped. Notes do not move while stopped, so mapping
			// distance to time is meaningless; keep the last lead time
			// instead of collapsing it, otherwise the first frame after
			// resuming pops notes in.
			return Math.Min(ceilingMs, _lastVisualLeadMs);
		}
		double signedSpeed = _motion.Speed * chartSpeed;
		double trackUnitsPerSecond = Math.Abs(
			_motion.TrackLength * TrackMotion.TrackScale * signedSpeed);
		double leadMs = VisualLeadMsForTrackSpeed(
			_motion.TrackLength,
			trackUnitsPerSecond,
			ceilingMs);
		_lastVisualLeadMs = leadMs;
		_lastVisualDirection = signedSpeed < 0.0 ? -1 : 1;
		return leadMs;
	}

	private void VisualWindowEdges(
		double nowMs,
		double ceilingMs,
		out double pastEdgeMs,
		out double futureEdgeMs)
	{
		// Charts use negative track() speeds (track(-20), track(-74)) where
		// the track scrolls backwards, so "later" notes enter from the far
		// end *behind* the judgement line. The wide side of the window has
		// to follow the direction of travel; the narrow side only needs to
		// cover the fade-out on the side the note just left.
		double leadMs = VisualLeadMs(nowMs, ceilingMs);
		if (_lastVisualDirection < 0)
		{
			pastEdgeMs = Math.Max(leadMs, VisualWindowTrailingMs);
			futureEdgeMs = Math.Min(leadMs, VisualWindowTrailingMs);
			return;
		}
		pastEdgeMs = Math.Min(leadMs, VisualWindowTrailingMs);
		futureEdgeMs = Math.Max(leadMs, VisualWindowTrailingMs);
	}

	// Pure part of the lead-time computation, kept static so the editor
	// self-check can assert the far-end coverage invariant without
	// building a scene. Public because Editor self-checks live in the
	// Assembly-CSharp-Editor assembly and cannot see internals here.
	public static double VisualLeadMsForTrackSpeed(
		double trackLength,
		double trackUnitsPerSecond,
		double ceilingMs)
	{
		double visibleZ = trackLength * VisualWindowFarEndScale;
		double leadMs =
			VisualWindowPaddingMs +
			visibleZ * 1000.0 /
			Math.Max(0.001, trackUnitsPerSecond);
		return Math.Max(
			750.0,
			Math.Min(ceilingMs, leadMs));
	}

	// TAP is the only kind that hides the instant it crosses the judgement
	// line, because it has no tail to draw. That has to be a *chart-time*
	// test with a tolerance, not a sign test on z.
	//
	// Two independent rounding paths put a note that was placed *on* the line
	// on the wrong side of it:
	//  - the editor integerises the pointer time with MidpointRounding
	//    AwayFromZero, so a playhead at 10000.4 ms stores the note at 10000 ms
	//    (up to 0.5 ms behind the playhead);
	//  - z is rebuilt from ScaledTimeAt, whose piecewise sums carry float
	//    error, so an exactly-equal note time can still evaluate to a tiny
	//    negative z.
	// Hiding on z < 0 then made a genuinely placed note invisible: it existed
	// in the chart but had no model to grab. The tolerance covers the 0.5 ms
	// integerisation plus the scaled-time error, and is sub-pixel at every
	// supported track speed.
	private const double JudgeLineVisibilityToleranceMs = 1.0;

	private static bool IsTapPastJudgeLine(ChartNote note, double nowMs)
	{
		return nowMs > note.TimeMs + JudgeLineVisibilityToleranceMs;
	}

	private static bool IsInVisualWindow(double startTimeMs, double endTimeMs, double nowMs, double pastEdgeMs, double futureEdgeMs)
	{
		if (endTimeMs < nowMs - pastEdgeMs)
		{
			return false;
		}
		return startTimeMs <= nowMs + futureEdgeMs;
	}

	private static void ReleaseSky(SkyVisual sky)
	{
		if ((Object)(object)sky.Root != (Object)null)
		{
			DestroyObject(sky.Root);
		}
		sky.Root = null;
		sky.Mesh = null;
		sky.Renderer = null;
		sky.Block = null;
		sky.PromptRoot = null;
		sky.BracketLeft = null;
		sky.BracketRight = null;
		sky.ShadowRenderer = null;
		sky.RowCaches.Clear();
		sky.HasMesh = false;
		sky.HasAppearance = false;
	}

	private void BuildSkyChains()
	{
		List<ChartNote> skyNotes = new List<ChartNote>();
		for (int i = 0; i < _chart.Notes.Count; i++)
		{
			ChartNote note = _chart.Notes[i];
			if (note.Kind == NoteKind.SkyArea)
			{
				skyNotes.Add(note);
			}
		}
		skyNotes.Sort((ChartNote left, ChartNote right) =>
		{
			int num = left.TimeMs.CompareTo(right.TimeMs);
			return (num == 0) ? left.Id.CompareTo(right.Id) : num;
		});
		Dictionary<int, SkyVisual> groups =
			new Dictionary<int, SkyVisual>();
		for (int i2 = 0; i2 < skyNotes.Count; i2++)
		{
			ChartNote note2 = skyNotes[i2];
			SkyVisual current;
			if (!groups.TryGetValue(note2.GroupId, out current))
			{
				current = new SkyVisual
				{
					GroupId = note2.GroupId,
					FirstTimeMs = note2.TimeMs,
					LastTimeMs = note2.EndTimeMs
				};
				groups.Add(note2.GroupId, current);
				_skies.Add(current);
			}
			current.FirstTimeMs = Math.Min(
				current.FirstTimeMs,
				note2.TimeMs);
			current.LastTimeMs = Math.Max(
				current.LastTimeMs,
				note2.EndTimeMs);
			current.Segments.Add(note2);
		}
	}

	private static void CopyMaterialColor(Material source, Material destination, string property)
	{
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		if (source.HasProperty(property) && destination.HasProperty(property))
		{
			destination.SetColor(property, source.GetColor(property));
		}
	}

	private static void CopyMaterialFloat(Material source, Material destination, string property)
	{
		if (source.HasProperty(property) && destination.HasProperty(property))
		{
			destination.SetFloat(property, source.GetFloat(property));
		}
	}

	private SkyVisual CreateSky(SkyVisual sky, Material overrideMaterial = null, bool createPrompt = true)
	{
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Expected Obj, but got Unknown
		//IL_0079: Unknown result type (might be due to invalid IL or missing references)
		//IL_007f: Expected Obj, but got Unknown
		//IL_00bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c6: Expected Obj, but got Unknown
		//IL_00db: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e1: Expected Obj, but got Unknown
		//IL_0127: Unknown result type (might be due to invalid IL or missing references)
		//IL_012e: Expected Obj, but got Unknown
		//IL_0163: Unknown result type (might be due to invalid IL or missing references)
		//IL_0168: Unknown result type (might be due to invalid IL or missing references)
		//IL_017e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0183: Unknown result type (might be due to invalid IL or missing references)
		//IL_0199: Unknown result type (might be due to invalid IL or missing references)
		//IL_019e: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b4: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01dd: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f3: Unknown result type (might be due to invalid IL or missing references)
		//IL_0204: Unknown result type (might be due to invalid IL or missing references)
		//IL_0209: Unknown result type (might be due to invalid IL or missing references)
		//IL_021a: Unknown result type (might be due to invalid IL or missing references)
		//IL_021f: Unknown result type (might be due to invalid IL or missing references)
		//IL_023e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0243: Unknown result type (might be due to invalid IL or missing references)
		//IL_0254: Unknown result type (might be due to invalid IL or missing references)
		//IL_0259: Unknown result type (might be due to invalid IL or missing references)
		//IL_026a: Unknown result type (might be due to invalid IL or missing references)
		//IL_026f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0280: Unknown result type (might be due to invalid IL or missing references)
		//IL_0285: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f3: Unknown result type (might be due to invalid IL or missing references)
		//IL_030e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0313: Unknown result type (might be due to invalid IL or missing references)
		//IL_0369: Unknown result type (might be due to invalid IL or missing references)
		//IL_0379: Unknown result type (might be due to invalid IL or missing references)
		if ((Object)(object)_skyAreaMaterial == (Object)null)
		{
			throw new InvalidOperationException("SkyArea material is missing.");
		}
		GameObject root = new GameObject("PhaseA_SkyArea_Group_" + sky.GroupId);
		root.transform.SetParent(_skyRoot, false);
		MeshFilter val = root.AddComponent<MeshFilter>();
		MeshRenderer renderer = root.AddComponent<MeshRenderer>();
		((Renderer)renderer).sharedMaterial = (((Object)(object)overrideMaterial != (Object)null) ? overrideMaterial : _skyAreaMaterial);
		((Renderer)renderer).shadowCastingMode = (ShadowCastingMode)0;
		((Renderer)renderer).receiveShadows = false;
		Mesh mesh = new Mesh();
		((Object)mesh).name = "PhaseA_SkyArea_Group_" + sky.GroupId;
		mesh.MarkDynamic();
		val.sharedMesh = mesh;
		sky.Root = root;
		sky.Mesh = mesh;
		sky.Renderer = renderer;
		sky.Block = new MaterialPropertyBlock();
		sky.HasMesh = false;
		sky.HasAppearance = false;

		if ((Object)(object)_skyShadowMaterial != (Object)null && !sky.IsGhost)
		{
			GameObject shadow = new GameObject("GroundShadow");
			shadow.transform.SetParent(root.transform, false);
			shadow.transform.localPosition = new Vector3(0f, 0.005f, 0f);
			MeshFilter shadowFilter = shadow.AddComponent<MeshFilter>();
			shadowFilter.sharedMesh = mesh;
			MeshRenderer shadowRenderer = shadow.AddComponent<MeshRenderer>();
			shadowRenderer.sharedMaterial = _skyShadowMaterial;
			shadowRenderer.shadowCastingMode = ShadowCastingMode.Off;
			shadowRenderer.receiveShadows = false;
			sky.ShadowRenderer = shadowRenderer;
		}

		GameObject prompt = new GameObject("PhaseA_SkyArea_Prompt_" + sky.GroupId);
		prompt.SetActive(createPrompt);
		prompt.transform.SetParent(root.transform, false);
		if (_skyBracketMesh != null && _skyBracketMaterial != null)
		{
			sky.BracketLeft = CreateSkyBracket(
				prompt.transform,
				"Left",
				false);
			sky.BracketRight = CreateSkyBracket(
				prompt.transform,
				"Right",
				true);
		}
		sky.PromptRoot = prompt;
		prompt.SetActive(false);
		root.transform.localPosition = Vector3.zero;
		root.transform.localRotation = Quaternion.identity;
		root.SetActive(false);
		return sky;
	}

	private Transform CreateSkyBracket(
		Transform parent,
		string suffix,
		bool mirrored)
	{
		GameObject bracket = new GameObject("SquareBracket" + suffix);
		bracket.transform.SetParent(parent, false);
		MeshFilter filter = bracket.AddComponent<MeshFilter>();
		filter.sharedMesh = _skyBracketMesh;
		MeshRenderer renderer = bracket.AddComponent<MeshRenderer>();
		renderer.sharedMaterial = _skyBracketMaterial;
		renderer.shadowCastingMode = ShadowCastingMode.Off;
		renderer.receiveShadows = false;
		bracket.transform.localRotation = mirrored
			? Quaternion.Euler(0f, 180f, 0f)
			: Quaternion.identity;
		return bracket.transform;
	}

	private NoteVisual CreateVisual(ChartNote note)
	{
		//IL_0110: Unknown result type (might be due to invalid IL or missing references)
		//IL_011a: Expected Obj, but got Unknown
		//IL_0173: Unknown result type (might be due to invalid IL or missing references)
		//IL_0178: Unknown result type (might be due to invalid IL or missing references)
		//IL_0180: Unknown result type (might be due to invalid IL or missing references)
		//IL_0182: Unknown result type (might be due to invalid IL or missing references)
		//IL_0187: Unknown result type (might be due to invalid IL or missing references)
		//IL_019a: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c9: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_01db: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e0: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ba: Unknown result type (might be due to invalid IL or missing references)
		//IL_02bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_0216: Unknown result type (might be due to invalid IL or missing references)
		//IL_021b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0220: Unknown result type (might be due to invalid IL or missing references)
		//IL_0336: Unknown result type (might be due to invalid IL or missing references)
		//IL_033b: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ed: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_02fb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0300: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_02cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d9: Unknown result type (might be due to invalid IL or missing references)
		//IL_02de: Unknown result type (might be due to invalid IL or missing references)
		//IL_024e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0255: Unknown result type (might be due to invalid IL or missing references)
		//IL_025c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0261: Unknown result type (might be due to invalid IL or missing references)
		//IL_0225: Unknown result type (might be due to invalid IL or missing references)
		//IL_022c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0233: Unknown result type (might be due to invalid IL or missing references)
		//IL_023a: Unknown result type (might be due to invalid IL or missing references)
		//IL_023f: Unknown result type (might be due to invalid IL or missing references)
		//IL_036e: Unknown result type (might be due to invalid IL or missing references)
		//IL_037f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0390: Unknown result type (might be due to invalid IL or missing references)
		//IL_0315: Unknown result type (might be due to invalid IL or missing references)
		//IL_031a: Unknown result type (might be due to invalid IL or missing references)
		//IL_031f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0325: Unknown result type (might be due to invalid IL or missing references)
		//IL_032a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0276: Unknown result type (might be due to invalid IL or missing references)
		//IL_027b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0280: Unknown result type (might be due to invalid IL or missing references)
		//IL_0286: Unknown result type (might be due to invalid IL or missing references)
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
			{
				template = _sideLeftHoldPrefab;
			}
		}
		else if (note.StartLane != 0)
		{
			if (note.StartLane == 5)
			{
				template = _sideRightTapPrefab;
			}
			else
			{
				template = ((note.WidthLanes <= 1) ? _tapPrefab : _tapSpanPrefab);
			}
		}
		else
		{
			template = _sideLeftTapPrefab;
		}
		if ((Object)(object)template == (Object)null)
		{
			throw new InvalidOperationException("No visual template for note " + note.Id + " kind=" + note.Kind);
		}
		GameObject clone = Object.Instantiate<GameObject>(template, _renderRoot, false);
		clone.SetActive(true);
		NoteVisual visual = new NoteVisual
		{
			Note = note,
			Root = clone,
			RootTransform = clone.transform,
			Renderers = clone.GetComponentsInChildren<Renderer>(true)
		};
		RenameVisual(visual, note);
		if (note.StartLane == 0 || note.StartLane == 5)
		{
			DisableChildContaining(clone.transform, "SideGuide");
		}
		GameObject laneTemplate = _binding.TemplateForLane(note.StartLane);
		Transform sideLane = _binding.SideLaneForLane(note.StartLane);
		Vector3 linePosition = _binding.JudgementLine.position;
		Vector3 lineLocal = _renderRoot.InverseTransformPoint(linePosition);
		if (note.Kind == NoteKind.Flick)
		{
			visual.BaseLocalPosition = new Vector3(FlickAnchorX(note), lineLocal.y + 0.4698f, lineLocal.z);
			visual.BaseLocalRotation = ((note.FlickDirection == 16) ? Quaternion.identity : template.transform.localRotation);
			visual.Tint = Color.white;
		}
		else if (note.Kind == NoteKind.Hold)
		{
			bool num = note.StartLane == 0 || note.StartLane == 5;
			Vector3 templateLocal = _renderRoot.InverseTransformPoint(laneTemplate.transform.position);
			if (num)
			{
				visual.BaseLocalPosition = new Vector3(templateLocal.x, templateLocal.y, lineLocal.z);
			}
			else
			{
				visual.BaseLocalPosition = new Vector3(LaneCenterX(note), templateLocal.y, lineLocal.z);
			}
			visual.BaseLocalRotation = LaneLocalRotation(laneTemplate.transform, sideLane, template.transform.localRotation);
			visual.Tint = Color.white;
		}
		else
		{
			bool num2 = note.StartLane == 0 || note.StartLane == 5;
			Vector3 templateLocal2 = _renderRoot.InverseTransformPoint(laneTemplate.transform.position);
			if (num2)
			{
				visual.BaseLocalPosition = new Vector3(templateLocal2.x, templateLocal2.y, lineLocal.z);
			}
			else
			{
				visual.BaseLocalPosition = new Vector3(LaneCenterX(note), templateLocal2.y, lineLocal.z);
			}
			visual.BaseLocalRotation = LaneLocalRotation(laneTemplate.transform, sideLane, template.transform.localRotation);
			visual.Tint = Color.white;
		}
		visual.BaseLocalScale = template.transform.localScale;
		if (note.Kind == NoteKind.Flick && note.FlickDirection == 16)
		{
			visual.BaseLocalScale.x *= -1f;
		}
		visual.RootTransform.localPosition = visual.BaseLocalPosition;
		visual.RootTransform.localRotation = visual.BaseLocalRotation;
		visual.RootTransform.localScale = visual.BaseLocalScale;
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
		else if (note.Kind == NoteKind.Hold)
		{
			InitializeHoldGeometry(visual);
		}
		ApplyGroundWidthScale(visual, note);
		ApplyVisualState(visual, 1f, 0.0);
		clone.SetActive(false);
		return visual;
	}

	private static void ApplyGroundWidthScale(NoteVisual visual, ChartNote note)
	{
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		if (visual != null && note != null && note.Kind == NoteKind.Tap && note.WidthLanes > 1)
		{
			float scale = GroundNoteWidthScale(note.Kind, note.WidthLanes);
			visual.RootTransform.localScale = new Vector3(visual.BaseLocalScale.x * scale, visual.BaseLocalScale.y, visual.BaseLocalScale.z);
		}
	}

	internal static float GroundNoteWidthScale(NoteKind kind, int widthLanes)
	{
		widthLanes = Mathf.Clamp(widthLanes, 1, 4);
		if (kind != NoteKind.Tap)
		{
			return 1f;
		}
		return widthLanes;
	}

	private static void RenameVisual(NoteVisual visual, ChartNote note)
	{
		if (visual != null && !((Object)(object)visual.Root == (Object)null) && note != null)
		{
			((Object)visual.Root).name = "PhaseA_Note_" + note.Id.ToString("D4") + "_" + note.Kind;
		}
	}

	private Quaternion LaneLocalRotation(Transform laneTemplate, Transform sideLane, Quaternion templateLocalRotation)
	{
		//IL_006a: Unknown result type (might be due to invalid IL or missing references)
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0075: Unknown result type (might be due to invalid IL or missing references)
		//IL_007a: Unknown result type (might be due to invalid IL or missing references)
		//IL_007f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		if ((Object)(object)sideLane != (Object)null)
		{
			Vector3 widthAxis = Quaternion.Inverse(_renderRoot.rotation) * sideLane.rotation * Vector3.forward;
			float slope = Mathf.Atan2(widthAxis.y, widthAxis.x) * 57.29578f + 180f;
			return Quaternion.Euler(0f, 0f, slope) * templateLocalRotation;
		}
		return Quaternion.Inverse(_renderRoot.rotation) * laneTemplate.rotation * templateLocalRotation;
	}

	// World-space X extent of the arrow head, used to keep the tail from overhanging it.
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

	// Current world-space length of the tail plate, used to keep stripe density constant.
	private static float FlickTailWorldLength(NoteVisual visual)
	{
		if ((Object)(object)visual.FlickPlane == (Object)null)
		{
			return 0f;
		}
		MeshFilter mf = ((Component)visual.FlickPlane).GetComponent<MeshFilter>();
		if ((Object)(object)mf == (Object)null)
		{
			return 0f;
		}
		// Use the INSTANCE mesh: AlignFlickPlane rewrites its vertices for each flick,
		// so sharedMesh would report the undeformed length.
		Mesh mesh = mf.mesh;
		if ((Object)(object)mesh == (Object)null)
		{
			return 0f;
		}
		Vector3[] v = mesh.vertices;
		if (v.Length == 0)
		{
			return 0f;
		}
		float minX = float.MaxValue;
		float maxX = float.MinValue;
		for (int i = 0; i < v.Length; i++)
		{
			Vector3 p = visual.FlickPlane.TransformPoint(v[i]);
			minX = Mathf.Min(minX, p.x);
			maxX = Mathf.Max(maxX, p.x);
		}
		return Mathf.Abs(maxX - minX);
	}

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
		for (int i = 0; i < planeVertices.Length; i++)
		{
			rootLocal[i] = visual.RootTransform.InverseTransformPoint(
				visual.FlickPlane.TransformPoint(planeVertices[i]));
			plateMinX = Mathf.Min(plateMinX, rootLocal[i].x);
			plateMaxX = Mathf.Max(plateMaxX, rootLocal[i].x);
		}
		float plateCenterX = (plateMinX + plateMaxX) * 0.5f;

		// The tail grows toward the side of the head it sits on. Decide from the
		// relative centres, NOT from the object's absolute position on screen.
		// (The old code used planeCenterX < 0 and therefore flipped its behaviour
		// depending on where the note happened to sit.)
		bool tailGrowsPositive = plateCenterX >= headCenterX;

		// The head edge facing the tail.
		float headBackEdgeX = tailGrowsPositive ? headMaxX : headMinX;

		// The plate corner facing the head.
		float plateNearX = tailGrowsPositive ? plateMinX : plateMaxX;

		// Target: butt that corner against the head's back edge. A POSITIVE inset moves
		// the tail AWAY from the head (clean gap, never overlap).
		// A tiny negative epsilon is NOT used: overlapping is what let the plate show
		// through the arrow in the first place.
		float targetNearX = headBackEdgeX + (tailGrowsPositive
			? FlickTailInset
			: (-FlickTailInset));
		float deltaRootX = targetNearX - plateNearX;

		// Apply the root-space correction in the plane's PARENT frame. Converting via
		// world space keeps this correct for any parent rotation or negative scale,
		// which is what the previous code got wrong.
		if (Mathf.Abs(deltaRootX) > 1e-7f)
		{
			// Convert the ROOT-space X delta into the plane's LOCAL X delta. The plane
			// sits under v5-3 (180deg about Y) and has scale(-1,-1,-1), so root X and
			// local X differ by a sign. Measure the actual mapping instead of assuming
			// it: nudge the plane by +1 local unit and see how far root X moves.
			Vector3 savedPos = visual.FlickPlane.localPosition;
			float rootXBefore = visual.RootTransform.InverseTransformPoint(
				visual.FlickPlane.TransformPoint(planeVertices[0])).x;
			visual.FlickPlane.localPosition = savedPos + new Vector3(1f, 0f, 0f);
			float rootXAfter = visual.RootTransform.InverseTransformPoint(
				visual.FlickPlane.TransformPoint(planeVertices[0])).x;
			visual.FlickPlane.localPosition = savedPos;

			float dRootPerLocalX = rootXAfter - rootXBefore;
			if (Mathf.Abs(dRootPerLocalX) > 1e-7f)
			{
				Vector3 moved = savedPos;
				moved.x += deltaRootX / dRootPerLocalX;
				visual.FlickPlane.localPosition = moved;
			}
		}

		// ---- conform the butt edge to the head's back profile ----
		// Re-read the corners now that the plate has moved.
		for (int j = 0; j < rootLocal.Length; j++)
		{
			rootLocal[j] = visual.RootTransform.InverseTransformPoint(
				visual.FlickPlane.TransformPoint(planeVertices[j]));
		}
		// Head profile at its bottom and top rows, on the back-edge side.
		float backBottomX = tailGrowsPositive ? float.MinValue : float.MaxValue;
		float backTopX = backBottomX;
		float yTolerance = Mathf.Max(0.0001f, (headMaxY - headMinY) * 0.02f);
		for (int l = 0; l < headVertices.Length; l++)
		{
			Vector3 hp = visual.RootTransform.InverseTransformPoint(
				visual.FlickHead.TransformPoint(headVertices[l]));
			if (Mathf.Abs(hp.y - headMinY) <= yTolerance)
			{
				backBottomX = tailGrowsPositive
					? Mathf.Max(backBottomX, hp.x)
					: Mathf.Min(backBottomX, hp.x);
			}
			if (Mathf.Abs(hp.y - headMaxY) <= yTolerance)
			{
				backTopX = tailGrowsPositive
					? Mathf.Max(backTopX, hp.x)
					: Mathf.Min(backTopX, hp.x);
			}
		}
		float yRange = Mathf.Max(0.0001f, headMaxY - headMinY);
		for (int m = 0; m < rootLocal.Length; m++)
		{
			int pairedIndex = -1;
			float bestYDelta = float.MaxValue;
			for (int n = 0; n < rootLocal.Length; n++)
			{
				if (n != m)
				{
					float yDelta = Mathf.Abs(rootLocal[n].y - rootLocal[m].y);
					if (yDelta < bestYDelta)
					{
						bestYDelta = yDelta;
						pairedIndex = n;
					}
				}
			}
			// Only the corner pair that faces the head gets conformed to its profile.
			bool facesHead = tailGrowsPositive
				? (rootLocal[m].x <= rootLocal[pairedIndex].x)
				: (rootLocal[m].x >= rootLocal[pairedIndex].x);
			if (pairedIndex >= 0 && !(bestYDelta > 0.001f) && facesHead)
			{
				float normalizedY = Mathf.Clamp01((rootLocal[m].y - headMinY) / yRange);
				// No inward push: FlickVertexSeamOffset is 0 by default and the butt
				// already sits exactly on the head's profile.
				rootLocal[m].x = Mathf.Lerp(backBottomX, backTopX, normalizedY)
					+ (tailGrowsPositive ? FlickVertexSeamOffset : (-FlickVertexSeamOffset));
				planeVertices[m] = visual.FlickPlane.InverseTransformPoint(
					visual.RootTransform.TransformPoint(rootLocal[m]));
			}
		}
		planeMesh.MarkDynamic();
		planeMesh.vertices = planeVertices;
		planeMesh.RecalculateBounds();
	}


	private void InitializeHoldGeometry(NoteVisual visual)
	{
		visual.HoldFront = FindChildContaining(visual.RootTransform, "hotehold_A");
		visual.HoldMiddle = FindChildContaining(visual.RootTransform, "hotehold_B");
		visual.HoldBack = FindChildContaining(visual.RootTransform, "hotehold_C");
		if ((Object)(object)visual.HoldFront == (Object)null || (Object)(object)visual.HoldMiddle == (Object)null || (Object)(object)visual.HoldBack == (Object)null)
		{
			throw new InvalidOperationException("Hold prefab is missing hotehold_A/B/C: " + ((Object)visual.Root).name);
		}
		visual.HoldFrontLength = RecenterHoldMesh(visual.HoldFront, anchorAtMinimumZ: false);
		visual.HoldMiddleBaseLength = RecenterHoldMesh(visual.HoldMiddle, anchorAtMinimumZ: true);
		visual.HoldBackBaseLength = RecenterHoldMesh(visual.HoldBack, anchorAtMinimumZ: true);
		visual.HoldMiddleRenderer = ((Component)visual.HoldMiddle).GetComponent<MeshRenderer>();
		visual.HoldBackRenderer = ((Component)visual.HoldBack).GetComponent<MeshRenderer>();
		CacheHoldMiddleUv(visual);
	}

	// The middle segment samples a band of the hold atlas with ABSOLUTE UVs (v runs
	// 0.20 -> 0.70 from head to tail), and the HoldNote shader passes UV through
	// untouched (every map is [NoScaleOffset]; IF_Vertex forwards uv verbatim), so
	// there is no texture-offset property to animate. Displacement therefore has to
	// be baked into this note's own instance UVs.
	private static void CacheHoldMiddleUv(NoteVisual visual)
	{
		MeshFilter filter = ((Component)visual.HoldMiddle).GetComponent<MeshFilter>();
		if ((Object)(object)filter == (Object)null)
		{
			return;
		}
		// mf.mesh (not sharedMesh): RecenterHoldMesh already instanced the geometry
		// per note, so writing UVs here can never leak into another hold.
		Mesh mesh = filter.mesh;
		if ((Object)(object)mesh == (Object)null)
		{
			return;
		}
		Vector2[] uv = mesh.uv;
		if (uv.Length == 0)
		{
			return;
		}
		Vector2[] baseUv = new Vector2[uv.Length];
		Array.Copy(uv, baseUv, uv.Length);
		float minV = float.MaxValue;
		float maxV = float.MinValue;
		for (int i = 0; i < baseUv.Length; i++)
		{
			minV = Mathf.Min(minV, baseUv[i].y);
			maxV = Mathf.Max(maxV, baseUv[i].y);
		}
		visual.HoldMiddleMesh = mesh;
		visual.HoldMiddleBaseUv = baseUv;
		visual.HoldMiddleUv = new Vector2[uv.Length];
		visual.HoldMiddleUvTail = (maxV > minV ? maxV : 0.7f);
		visual.HoldMiddleUvHead = (maxV > minV ? minV : 0.2f);
		visual.AppliedHoldUvScale = -1f;
	}

	// Samples the middle band at a CONSTANT world scale and shrinks its UV window so
	// the pattern translates as the hold shortens, instead of being squeezed.
	// The window is anchored at the tail (the judged end) and grows/shrinks from the
	// head, so the visible body always ends exactly where the judgement line is.
	private static void UpdateHoldMiddleUv(NoteVisual visual, float uvScale)
	{
		if ((Object)(object)visual.HoldMiddleMesh == (Object)null ||
			visual.HoldMiddleBaseUv == null ||
			visual.HoldMiddleUv == null)
		{
			return;
		}
		if (visual.AppliedHoldUvScale >= 0f &&
			Mathf.Abs(visual.AppliedHoldUvScale - uvScale) <= 0.0001f)
		{
			return;
		}
		visual.AppliedHoldUvScale = uvScale;
		Vector2[] baseUv = visual.HoldMiddleBaseUv;
		Vector2[] uv = visual.HoldMiddleUv;
		// Distance in v between the head end of the ramp and the anchored tail end.
		float tail = visual.HoldMiddleUvTail;
		float head = visual.HoldMiddleUvHead;
		float scale = Mathf.Clamp01(uvScale);
		for (int i = 0; i < uv.Length; i++)
		{
			// Shrink the sampled interval with the geometry, anchored at the
			// tail. Independent wrapping folds both endpoints onto the same V
			// and introduces a discontinuity inside an atlas sprite.
			float v = Mathf.Clamp(tail + (baseUv[i].y - tail) * scale, head, tail);
			uv[i] = new Vector2(baseUv[i].x, v);
		}
		// Interpolation stays within the authored band and preserves the
		// original UVs at full length.
		visual.HoldMiddleMesh.uv = uv;
	}

	private static float MinHoldUv(Vector2[] uv)
	{
		float min = float.MaxValue;
		for (int i = 0; i < uv.Length; i++)
		{
			min = Mathf.Min(min, uv[i].y);
		}
		return min;
	}

	private static float RecenterHoldMesh(Transform segment, bool anchorAtMinimumZ)
	{
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f2: Unknown result type (might be due to invalid IL or missing references)
		MeshFilter component = ((Component)segment).GetComponent<MeshFilter>();
		if ((Object)(object)component == (Object)null)
		{
			throw new InvalidOperationException("Hold segment has no MeshFilter: " + ((Object)segment).name);
		}
		Mesh mesh = component.mesh;
		Vector3[] vertices = mesh.vertices;
		if (vertices.Length == 0)
		{
			return 1f;
		}
		Bounds bounds = mesh.bounds;
		float centerX = bounds.center.x;
		float edgeZ = (anchorAtMinimumZ ? float.MaxValue : float.MinValue);
		for (int i = 0; i < vertices.Length; i++)
		{
			edgeZ = (anchorAtMinimumZ ? Mathf.Min(edgeZ, vertices[i].z) : Mathf.Max(edgeZ, vertices[i].z));
		}
		for (int j = 0; j < vertices.Length; j++)
		{
			vertices[j].x -= centerX;
			vertices[j].z -= edgeZ;
		}
		mesh.MarkDynamic();
		mesh.vertices = vertices;
		mesh.RecalculateBounds();
		bounds = mesh.bounds;
		float length = bounds.size.z;
		if (!(length > 0.0001f))
		{
			return 1f;
		}
		return length;
	}

	private static void UpdateHoldGeometry(NoteVisual visual, float totalLength)
	{
		//IL_0082: Unknown result type (might be due to invalid IL or missing references)
		//IL_009d: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00de: Unknown result type (might be due to invalid IL or missing references)
		//IL_0101: Unknown result type (might be due to invalid IL or missing references)
		//IL_0122: Unknown result type (might be due to invalid IL or missing references)
		float length = Mathf.Max(0.001f, totalLength);
		int width = Mathf.Max(1, visual.Note.WidthLanes);
		float frontLength = Mathf.Min(visual.HoldFrontLength, length);
		float remaining = length - frontLength;
		float middleLength = Mathf.Min(visual.HoldMiddleBaseLength, remaining);
		remaining -= middleLength;
		float backLength = remaining;
		float middleOverlap = Mathf.Min(0.05f, middleLength * 0.5f);
		float backOverlap = Mathf.Min(0.05f, backLength * 0.5f);
		visual.HoldFront.localScale = new Vector3((float)width, 1f, frontLength / visual.HoldFrontLength);
		visual.HoldFront.localPosition = new Vector3(0f, 0f, frontLength);
		visual.HoldMiddle.localScale = new Vector3((float)width, 1f, (middleLength + middleOverlap) / visual.HoldMiddleBaseLength);
		visual.HoldMiddle.localPosition = new Vector3(0f, 0f, frontLength - middleOverlap);
		// A judged hold shortens, and scaling the middle segment along Z
		// squashes its texture with it -- the band visibly compresses in place
		// as it is consumed. Slide the UV window instead, anchored at the tail,
		// so the pattern keeps its authored density and travels backward
		// through the segment rather than collapsing.
		if (visual.HoldMiddleBaseLength > 0.0001f)
		{
			UpdateHoldMiddleUv(
				visual,
				Mathf.Clamp01(
					middleLength / visual.HoldMiddleBaseLength));
		}
		visual.HoldBack.localScale = new Vector3((float)width, 1f, (backLength + backOverlap) / visual.HoldBackBaseLength);
		visual.HoldBack.localPosition = new Vector3(0f, 0f, frontLength + middleLength - backOverlap);
		SetRendererEnabled((Renderer)(object)visual.HoldMiddleRenderer, middleLength > 0.0001f);
		SetRendererEnabled((Renderer)(object)visual.HoldBackRenderer, backLength > 0.0001f);
	}

	private static void SetRendererEnabled(Renderer renderer, bool enabled)
	{
		if ((Object)(object)renderer != (Object)null)
		{
			if (renderer.enabled != enabled)
			{
				renderer.enabled = enabled;
			}
		}
	}

	private void UpdateVisual(NoteVisual visual, double nowMs)
	{
		//IL_0166: Unknown result type (might be due to invalid IL or missing references)
		//IL_016b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0187: Unknown result type (might be due to invalid IL or missing references)
		ChartNote note = visual.Note;
		if ((Object)(object)visual.Root == (Object)null)
		{
			return;
		}
		if (!IsEditorKindVisible(visual.Note.Kind))
		{
			if (visual.Root.activeSelf)
			{
				visual.Root.SetActive(false);
			}
			return;
		}
		double z = _motion.NoteZ(note, nowMs);
		bool isHold = note.Kind == NoteKind.Hold;
		float holdLength = (isHold ? Mathf.Max(0.01f, (float)_motion.HoldLength(note)) : 0f);
		float holdStart = 0f;
		float visibleHoldLength = holdLength;
		bool visible = true;
		if (isHold)
		{
			float trackLength = (float)_motion.TrackLength;
			float num = (float)z + holdLength;
			holdStart = Mathf.Clamp((float)z, 0f, trackLength);
			visibleHoldLength = Mathf.Clamp(num, 0f, trackLength) - holdStart;
			visible = visibleHoldLength > 0.001f && nowMs <= note.EndTimeMs + 200.0;
		}
		else
		{
			visible = _motion.IsVisible(z);
		}
		if (note.Kind == NoteKind.Tap && IsTapPastJudgeLine(note, nowMs))
		{
			visible = false;
		}
		if (note.Kind == NoteKind.Flick && note.Judged && IsHit(note.Result))
		{
			visible = false;
		}
		if (!isHold && note.Judged && !note.Holding && nowMs > note.TimeMs + 200.0)
		{
			visible = false;
		}
		if (!visible)
		{
			if (visual.Root.activeSelf)
			{
				visual.Root.SetActive(false);
			}
			return;
		}
		if (!visual.Root.activeSelf)
		{
			visual.Root.SetActive(true);
		}
		Vector3 position = visual.BaseLocalPosition;
		position.z += (isHold ? holdStart : ((float)z));
		visual.RootTransform.localPosition = position;
		ApplyVisualState(visual, _motion.Fade((float)z), nowMs);
		if (isHold)
		{
			UpdateHoldGeometry(visual, visibleHoldLength);
		}
	}

	private void UpdateSky(SkyVisual sky, double nowMs)
	{
		if ((Object)(object)sky.Root == (Object)null)
		{
			return;
		}
		if (!_editorShowSkyNotes)
		{
			if (sky.Root.activeSelf)
			{
				sky.Root.SetActive(false);
			}
			return;
		}
		double trackLength = _motion.TrackLength;
		double headZ = _motion.NoteZAt(sky.FirstTimeMs, nowMs);
		double tailZ = _motion.NoteZAt(sky.LastTimeMs, nowMs);
		bool visible = tailZ >= 0.0 && headZ <= trackLength && nowMs <= sky.LastTimeMs + 200.0;
		if (!visible)
		{
			if (sky.Root.activeSelf)
			{
				sky.Root.SetActive(false);
			}
			return;
		}
		if (!sky.Root.activeSelf)
		{
			sky.Root.SetActive(true);
		}
		SkyActiveCount++;
		UpdateSkyMesh(sky, nowMs, trackLength);
		Color color = _skyAreaMaterial.GetColor(ColorId);
		Color center = _skyAreaMaterial.GetColor(EdgeCenterColorId);
		Color glow = _skyAreaMaterial.GetColor(EdgeGlowColorId);
		// The authored material carries the fill alpha (0.5) and the two edge
		// alphas (1.0 center / 0.0 glow). The user setting replaces the fill
		// alpha outright and scales the edge alphas together, which is what
		// keeps the glow gradient's shape when the multiplier changes.
		color.a = _skyAreaOpacity;
		center.a *= _skyAreaGlow;
		glow.a *= _skyAreaGlow;
		float alphaScale = 1f;
		if (sky.IsGhost)
		{
			alphaScale = sky.GhostAlpha * 0.34f;
		}
		else if (_editorDimSkyNotes)
		{
			alphaScale = 0.24f;
		}
		color.a *= alphaScale;
		center.a *= alphaScale;
		glow.a *= alphaScale;
		float travel = (float)_motion.TrackTravelAt(nowMs);
		if (!sky.HasAppearance || sky.AppliedColor != color || sky.AppliedCenter != center ||
			sky.AppliedGlow != glow || sky.AppliedTrackLength != (float)trackLength || sky.AppliedTravel != travel)
		{
			sky.Block.SetColor(ColorId, color);
			sky.Block.SetColor(EdgeCenterColorId, center);
			sky.Block.SetColor(EdgeGlowColorId, glow);
			sky.Block.SetFloat(VisualStartId, 0f);
			sky.Block.SetFloat(VisualLengthId, 1f);
			sky.Block.SetFloat(TrackVisualLengthId, (float)trackLength);
			sky.Block.SetFloat(VisualChartProgressId, travel);
			sky.Renderer.SetPropertyBlock(sky.Block);
			PropertyBlockWriteCount++;
			sky.HasAppearance = true;
			sky.AppliedColor = color;
			sky.AppliedCenter = center;
			sky.AppliedGlow = glow;
			sky.AppliedTrackLength = (float)trackLength;
			sky.AppliedTravel = travel;
		}
		if (sky.ShadowRenderer != null)
		{
			sky.ShadowRenderer.gameObject.SetActive(
				!sky.IsGhost && !_editorDimSkyNotes);
		}

		bool showHeadPrompt =
			visible &&
			!sky.IsGhost &&
			!_editorDimSkyNotes &&
			nowMs < sky.FirstTimeMs - 5.0;
		if ((Object)(object)sky.PromptRoot != (Object)null)
		{
			sky.PromptRoot.SetActive(showHeadPrompt);
			if (showHeadPrompt)
			{
				ChartNote first = sky.Segments[0];
				float startCenter = TrackX((float)first.StartPos);
				float promptHalfWidth = Mathf.Max(
					0.005f,
					(float)first.StartWidth * SkyTrackWidth * 0.5f);
				sky.PromptRoot.transform.localPosition = Vector3.zero;
				sky.PromptRoot.transform.localRotation = Quaternion.identity;
				sky.PromptRoot.transform.localScale = Vector3.one;
				if (sky.BracketLeft != null)
				{
					sky.BracketLeft.localPosition = new Vector3(
						startCenter - promptHalfWidth,
						0f,
						(float)headZ);
					sky.BracketLeft.localRotation = Quaternion.identity;
				}
				if (sky.BracketRight != null)
				{
					sky.BracketRight.localPosition = new Vector3(
						startCenter + promptHalfWidth,
						0f,
						(float)headZ);
					sky.BracketRight.localRotation =
						Quaternion.Euler(0f, 180f, 0f);
				}
			}
		}
		sky.Logged = true;
	}

	/// <summary>
	/// One evaluated SkyArea row: everything <see cref="AppendSkyAreaQuad"/>
	/// needs from a sample parameter, computed once so two neighbouring
	/// row bands can share it.
	/// </summary>
	private struct SkyRowSample
	{
		public float Left;
		public float Right;
		public float Z;
		public float Distance;
	}

	private void UpdateSkyMesh(SkyVisual sky, double nowMs, double trackLength)
	{
		double scaledNow = _motion.ScaledNowAt(nowMs);
		bool changed = !sky.HasMesh || sky.MeshScaledNow != scaledNow;
		while (sky.RowCaches.Count < sky.Segments.Count) sky.RowCaches.Add(new SkyAreaRowCache());
		for (int i = 0; i < sky.Segments.Count; i++)
		{
			var segment = sky.Segments[i];
			double length = Math.Max(0.0001, _motion.HoldLength(segment));
			changed |= sky.RowCaches[i].Prepare(_chart, _motion, segment, length);
			double headZ = _motion.NoteZAt(segment.TimeMs, nowMs);
			double tailZ = _motion.NoteZAt(segment.EndTimeMs, nowMs);
			if (tailZ >= -0.001 && headZ <= trackLength + 0.001)
			{
				float progress = Mathf.Clamp01((float)(-headZ / length));
				if (progress > 0f && progress < 1f)
				{
					EvaluateSkyNormalizedEdges(segment, progress, out float left, out float right);
					SkyJudgementLineEffect.Active?.SetRange(left, right);
				}
			}
		}
		if (!changed) return;
		sky.Vertices.Clear();
		sky.Uv0.Clear();
		sky.Uv1.Clear();
		sky.Uv2.Clear();
		sky.Uv3.Clear();
		sky.Uv4.Clear();
		sky.Triangles.Clear();
		for (int i = 0; i < sky.Segments.Count; i++)
		{
			var segment = sky.Segments[i];
			double headZ = _motion.NoteZAt(segment.TimeMs, nowMs);
			double tailZ = _motion.NoteZAt(segment.EndTimeMs, nowMs);
			if (tailZ < -0.001 || headZ > trackLength + 0.001) continue;
			double length = Math.Max(0.0001, _motion.HoldLength(segment));
			float progress = Mathf.Clamp01((float)(-headZ / length));
			var cache = sky.RowCaches[i];
			cache.VisibleRows(scaledNow, _motion, out int first, out int last);
			int count = cache.Count;
			int progressRow = Mathf.Clamp((int)Math.Ceiling((double)progress * count), 1, count);
			first = Math.Max(first, progressRow);
			if (first > last) continue;
			var missing = BuildSkyMissingRange(sky, i);
			// This value belongs to the segment, not each quad. Evaluating it
			// without TrackTravelAt also leaves the playhead memo undisturbed.
			var metadata = new Vector4(i == 0 ? 1f : 0f,
				i == sky.Segments.Count - 1 ? 1f : 0f,
				(float)(_chart.ScaledTimeAt(segment.TimeMs) / 1000.0 *
					_motion.TrackLength * TrackMotion.TrackScale * _motion.Speed), 0f);
			float t0 = Mathf.Max((float)(first - 1) / count, progress);
			var initial = t0 == (float)(first - 1) / count ? cache.Get(first - 1)
				: SkyAreaRowCache.Evaluate(_chart, segment, t0, length);
			SkyRowSample previous = CachedSkyRow(initial, scaledNow, trackLength);
			for (int row = first; row <= last; row++)
			{
				float t1 = (float)row / count;
				SkyRowSample current = CachedSkyRow(cache.Get(row), scaledNow, trackLength);
				SkyRowSampleCount++;
				// Both rows clamped to the same boundary form zero-area quads.
				// Keep interior stopped/reversing rows and exact crossing bands.
				if (!(previous.Z == current.Z && (current.Z == 0f || current.Z == (float)trackLength)))
					AppendSkyAreaQuad(sky, t0, t1, ref previous, ref current, missing, metadata, length);
				previous = current;
				t0 = t1;
			}
		}
		sky.Mesh.Clear(false);
		sky.Mesh.SetVertices(sky.Vertices);
		sky.Mesh.SetUVs(0, sky.Uv0);
		sky.Mesh.SetUVs(1, sky.Uv1);
		sky.Mesh.SetUVs(2, sky.Uv2);
		sky.Mesh.SetUVs(3, sky.Uv3);
		sky.Mesh.SetUVs(4, sky.Uv4);
		sky.Mesh.SetTriangles(sky.Triangles, 0, false);
		sky.Mesh.bounds = new Bounds(new Vector3(0f, 0f, (float)trackLength * 0.5f),
			new Vector3(1.82f, 0.2f, (float)trackLength));
		sky.HasMesh = true;
		sky.MeshScaledNow = scaledNow;
		SkyMeshUploadCount++;
	}

	private SkyRowSample CachedSkyRow(SkyAreaRowCache.Row row, double scaledNow, double trackLength)
	{
		return new SkyRowSample
		{
			Left = row.Left, Right = row.Right, Distance = row.Distance,
			Z = Mathf.Clamp(SkyAreaRowCache.RawZ(row, scaledNow, _motion), 0f, (float)trackLength),
		};
	}


	private void AppendSkyAreaQuad(
		SkyVisual sky,
		float t0,
		float t1,
		ref SkyRowSample start,
		ref SkyRowSample end,
		Vector4 missingRange,
		Vector4 metadata,
		double segmentLength)
	{
		t0 = Mathf.Clamp01(t0);
		t1 = Mathf.Clamp01(t1);
		float left0 = start.Left;
		float right0 = start.Right;
		float z0 = start.Z;
		float distance0 = start.Distance;
		float left1 = end.Left;
		float right1 = end.Right;
		float z1 = end.Z;
		float distance1 = end.Distance;
		float width0 = Mathf.Max(0.0001f, right0 - left0);
		float width1 = Mathf.Max(0.0001f, right1 - left1);
		float leftU0 = Mathf.Clamp01(
			(left0 + TrackHalfWidth) / SkyTrackWidth);
		float rightU0 = Mathf.Clamp01(
			(right0 + TrackHalfWidth) / SkyTrackWidth);
		float leftU1 = Mathf.Clamp01(
			(left1 + TrackHalfWidth) / SkyTrackWidth);
		float rightU1 = Mathf.Clamp01(
			(right1 + TrackHalfWidth) / SkyTrackWidth);

		int baseIndex = sky.Vertices.Count;
		sky.Vertices.Add(new Vector3(left0, 0f, z0));
		sky.Vertices.Add(new Vector3(left1, 0f, z1));
		sky.Vertices.Add(new Vector3(right1, 0f, z1));
		sky.Vertices.Add(new Vector3(right0, 0f, z0));

		sky.Uv0.Add(new Vector2(leftU0, t0));
		sky.Uv0.Add(new Vector2(leftU1, t1));
		sky.Uv0.Add(new Vector2(rightU1, t1));
		sky.Uv0.Add(new Vector2(rightU0, t0));

		sky.Uv1.Add(new Vector2(0f, distance0));
		sky.Uv1.Add(new Vector2(0f, distance1));
		sky.Uv1.Add(new Vector2(1f, distance1));
		sky.Uv1.Add(new Vector2(1f, distance0));

		sky.Uv2.Add(new Vector2(
			width0 / SkyTrackWidth,
			(float)segmentLength));
		sky.Uv2.Add(new Vector2(
			width1 / SkyTrackWidth,
			(float)segmentLength));
		sky.Uv2.Add(new Vector2(
			width1 / SkyTrackWidth,
			(float)segmentLength));
		sky.Uv2.Add(new Vector2(
			width0 / SkyTrackWidth,
			(float)segmentLength));

		sky.Uv3.Add(missingRange);
		sky.Uv3.Add(missingRange);
		sky.Uv3.Add(missingRange);
		sky.Uv3.Add(missingRange);
		sky.Uv4.Add(metadata);
		sky.Uv4.Add(metadata);
		sky.Uv4.Add(metadata);
		sky.Uv4.Add(metadata);

		sky.Triangles.Add(baseIndex);
		sky.Triangles.Add(baseIndex + 1);
		sky.Triangles.Add(baseIndex + 3);
		sky.Triangles.Add(baseIndex + 3);
		sky.Triangles.Add(baseIndex + 1);
		sky.Triangles.Add(baseIndex + 2);
	}


	private static void EvaluateSkyNormalizedEdges(
		ChartNote segment,
		float progress,
		out float left,
		out float right)
	{
		float startLeft =
			(float)segment.StartPos -
			(float)segment.StartWidth * 0.5f;
		float startRight =
			(float)segment.StartPos +
			(float)segment.StartWidth * 0.5f;
		float endLeft =
			(float)segment.EndPos -
			(float)segment.EndWidth * 0.5f;
		float endRight =
			(float)segment.EndPos +
			(float)segment.EndWidth * 0.5f;
		left = Mathf.Lerp(
			startLeft,
			endLeft,
			SkyAreaGeometry.Ease(progress, segment.LeftEasing));
		right = Mathf.Lerp(
			startRight,
			endRight,
			SkyAreaGeometry.Ease(progress, segment.RightEasing));
	}

	private static Vector4 BuildSkyMissingRange(
		SkyVisual sky,
		int index)
	{
		Vector2 start = new Vector2(0f, 1f);
		Vector2 end = new Vector2(0f, 1f);
		ChartNote current = sky.Segments[index];

		if (index > 0)
		{
			ChartNote previous = sky.Segments[index - 1];
			if (SkyAreaGeometry.IsSharedBoundary(
				previous.EndTimeMs,
				current.TimeMs))
			{
				start = SkySpanOverlap(
					previous.EndPos,
					previous.EndWidth,
					current.StartPos,
					current.StartWidth);
			}
		}

		if (index + 1 < sky.Segments.Count)
		{
			ChartNote next = sky.Segments[index + 1];
			if (SkyAreaGeometry.IsSharedBoundary(
				current.EndTimeMs,
				next.TimeMs))
			{
				end = SkySpanOverlap(
					current.EndPos,
					current.EndWidth,
					next.StartPos,
					next.StartWidth);
			}
		}

		return new Vector4(start.x, start.y, end.x, end.y);
	}

	private static Vector2 SkySpanOverlap(
		double leftCenter,
		double leftWidth,
		double rightCenter,
		double rightWidth)
	{
		float leftMin = (float)(leftCenter -
			Math.Max(0.0, leftWidth) * 0.5);
		float leftMax = (float)(leftCenter +
			Math.Max(0.0, leftWidth) * 0.5);
		float rightMin = (float)(rightCenter -
			Math.Max(0.0, rightWidth) * 0.5);
		float rightMax = (float)(rightCenter +
			Math.Max(0.0, rightWidth) * 0.5);
		float min = Mathf.Max(leftMin, rightMin);
		float max = Mathf.Min(leftMax, rightMax);
		return min <= max
			? new Vector2(min, max)
			: new Vector2(0f, 1f);
	}

	private bool IsEditorKindVisible(NoteKind kind)
	{
		if (kind != NoteKind.Tap && kind != NoteKind.Hold)
		{
			return _editorShowSkyNotes;
		}
		return _editorShowGroundNotes;
	}

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

	private float LaneCenterX(ChartNote note)
	{
		int start = Mathf.Clamp(note.StartLane, 0, 5);
		int end = Mathf.Clamp(note.StartLane + Mathf.Max(1, note.WidthLanes) - 1, 0, 5);
		return (_laneCenterX[start] + _laneCenterX[end]) * 0.5f;
	}

	private float FlickAnchorX(ChartNote note)
	{
		return TrackX((float)note.FlickHeadPos);
	}

	private static float FlickWorldWidth(ChartNote note)
	{
		return Mathf.Max(0f, (float)note.FlickWidth) * 1.62f;
	}

	private NoteVisual FindVisual(ChartNote note)
	{
		if (note == null)
		{
			return null;
		}
		if (!_visualByNote.TryGetValue(note, out var visual))
		{
			return null;
		}
		return visual;
	}

	private bool AddVisual(ChartNote note)
	{
		NoteVisualKey key = NoteVisualKey.FromNote(note);
		NoteVisual visual = null;
		Stack<NoteVisual> pool;
		if (_visualPool.TryGetValue(key, out pool) &&
			pool.Count > 0)
		{
			visual = pool.Pop();
			_pooledVisualCount--;
			visual.Note = note;
			visual.IsGhost = false;
			visual.GhostAlpha = 1f;
			visual.Tint = Color.white;
			visual.HasAppliedVisualState = false;
			visual.Root.SetActive(false);
			RenameVisual(visual, note);
		}
		bool created = visual == null;
		if (created)
		{
			visual = CreateVisual(note);
		}
		_visuals.Add(visual);
		_visualByNote[note] = visual;
		return created;
	}

	private void RemoveVisual(NoteVisual visual)
	{
		if (visual != null)
		{
			_visuals.Remove(visual);
			if (visual.Note != null)
			{
				_visualByNote.Remove(visual.Note);
			}
		}
	}

	private void RemoveVisualAt(int index)
	{
		if (index >= 0 && index < _visuals.Count)
		{
			NoteVisual visual = _visuals[index];
			_visuals.RemoveAt(index);
			if (visual != null && visual.Note != null)
			{
				_visualByNote.Remove(visual.Note);
			}
		}
	}

	private void RecycleVisualAt(int index)
	{
		if (index < 0 || index >= _visuals.Count)
		{
			return;
		}
		NoteVisual visual = _visuals[index];
		_visuals.RemoveAt(index);
		if (visual == null)
		{
			return;
		}
		if (visual.Note != null)
		{
			_visualByNote.Remove(visual.Note);
		}
		if ((Object)(object)visual.Root == (Object)null)
		{
			return;
		}

		visual.Root.SetActive(false);
		if (visual.Note == null ||
			_pooledVisualCount >= MaxPooledVisuals)
		{
			DestroyObject(visual.Root);
			return;
		}

		NoteVisualKey key = NoteVisualKey.FromNote(visual.Note);
		Stack<NoteVisual> pool;
		if (!_visualPool.TryGetValue(key, out pool))
		{
			pool = new Stack<NoteVisual>();
			_visualPool.Add(key, pool);
		}
		if (pool.Count >= MaxPooledVisualsPerKey)
		{
			DestroyObject(visual.Root);
			return;
		}
		pool.Push(visual);
		_pooledVisualCount++;
	}

	private bool TryGetSkyNoteBounds(ChartNote note, double nowMs, out Bounds bounds)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0117: Unknown result type (might be due to invalid IL or missing references)
		//IL_011c: Unknown result type (might be due to invalid IL or missing references)
		bounds = default;
		if ((Object)(object)_skyRoot == (Object)null)
		{
			return false;
		}
		float startZ = (float)_motion.NoteZ(note, nowMs);
		float endZ = (float)_motion.NoteZAt(note.EndTimeMs, nowMs);
		float startLeft = TrackX((float)note.StartPos) - (float)note.StartWidth * 0.81f;
		float startRight = TrackX((float)note.StartPos) + (float)note.StartWidth * 0.81f;
		float endLeft = TrackX((float)note.EndPos) - (float)note.EndWidth * 0.81f;
		float endRight = TrackX((float)note.EndPos) + (float)note.EndWidth * 0.81f;
		bool hasBounds = false;
		AddSkyBoundPoint(_skyRoot.TransformPoint(new Vector3(startLeft, -0.025f, startZ)), ref bounds, ref hasBounds);
		AddSkyBoundPoint(_skyRoot.TransformPoint(new Vector3(startRight, 0.025f, startZ)), ref bounds, ref hasBounds);
		AddSkyBoundPoint(_skyRoot.TransformPoint(new Vector3(endLeft, -0.025f, endZ)), ref bounds, ref hasBounds);
		AddSkyBoundPoint(_skyRoot.TransformPoint(new Vector3(endRight, 0.025f, endZ)), ref bounds, ref hasBounds);
		return hasBounds;
	}

	private static void AddSkyBoundPoint(Vector3 point, ref Bounds bounds, ref bool hasBounds)
	{
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		if (!hasBounds)
		{
			bounds = new Bounds(point, Vector3.zero);
			hasBounds = true;
		}
		else
		{
			bounds.Encapsulate(point);
		}
	}

	private static void ScaleMaterialAlpha(Material material, string property, float multiplier)
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		if ((Object)(object)material != (Object)null && material.HasProperty(property))
		{
			Color color = material.GetColor(property);
			color.a *= multiplier;
			material.SetColor(property, color);
		}
	}

	private static float TrackX(float position)
	{
		return Mathf.Lerp(-0.81f, 0.81f, position);
	}

	private static Transform FindChildContaining(Transform root, string token)
	{
		for (int i = 0; i < root.childCount; i++)
		{
			Transform child = root.GetChild(i);
			if (((Object)child).name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return child;
			}
			Transform nested = FindChildContaining(child, token);
			if ((Object)(object)nested != (Object)null)
			{
				return nested;
			}
		}
		return null;
	}

	private static void DisableChildContaining(Transform root, string token)
	{
		for (int i = 0; i < root.childCount; i++)
		{
			Transform child = root.GetChild(i);
			if (((Object)child).name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				((Component)child).gameObject.SetActive(false);
			}
			else
			{
				DisableChildContaining(child, token);
			}
		}
	}

	private static bool IsHit(JudgementResult result)
	{
		if (result != JudgementResult.Shiny && result != JudgementResult.PerfectEarly && result != JudgementResult.PerfectLate && result != JudgementResult.NearEarly)
		{
			return result == JudgementResult.NearLate;
		}
		return true;
	}
}
}
