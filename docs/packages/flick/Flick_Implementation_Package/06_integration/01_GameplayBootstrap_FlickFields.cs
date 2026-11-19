// ==== 片段：Bootstrap 序列化字段：flickHitClip / flickLeftPrefab / flickRightPrefab ====
// 源文件: Assets/InFalsus/Gameplay/Runtime/Bootstrap/GameplayBootstrap.cs
// 行范围: 47-78 （共 32 行；本片段为逐字摘录，未改代码）
// 说明  : 这些是 Inspect 里挂资产的入口。prefab 由 Editor/GameplaySceneBuilder.cs 写入；运行时由 NoteRenderer 的 14 参构造消费（flickLeft/flickRight 在第 7、8 位）。
// ---------------------------------------------------------------------------

	public AudioClip sideTapHitClip;

	public AudioClip flickHitClip;

	public AudioClip groundHoldLoopClip;

	public AudioClip skyAreaHoldLoopClip;

	public Texture2D judgementExactPlusTexture;

	public Texture2D judgementExactTexture;

	public Texture2D judgementNearTexture;

	public Texture2D judgementBreakTexture;

	public Material judgementFeedbackMaterial;

	[Header("Presentation")]
	public int speedPref = 500;

	public GameObject skyAreaPrefab;

	public Material skyAreaMaterial;

	public GameObject holdPrefab;

	public GameObject flickLeftPrefab;

	public GameObject flickRightPrefab;

	public GameObject tapPrefab;
