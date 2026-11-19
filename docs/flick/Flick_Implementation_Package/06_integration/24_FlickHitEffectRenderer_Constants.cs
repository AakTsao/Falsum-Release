// ==== 片段：★ 命中特效全部可调常量 ====
// 源文件: Assets/InFalsus/Gameplay/Presentation/Effects/FlickHitEffectRenderer.cs
// 行范围: 11-30 （共 20 行；本片段为逐字摘录，未改代码）
// 说明  : 0.5333s 时长 / 淡出幂 2.0 / 反馈 0.58s / 池 4+8 / 轨道半宽 0.81 / EnableExactFeedback=false。
// ---------------------------------------------------------------------------

    public sealed class FlickHitEffectRenderer : MonoBehaviour
    {
        private const float BurstDurationFallback = 0.5333333f;
        // The recovered atlas contains a broad tail plate in its later frames.
        private const float BurstFadePower = 2.0f;
        private const float FeedbackDuration = 0.58f;
        private const float FeedbackHeight = 0.045f;
        private const float FeedbackScale = 0.09f;
        private const float TrackHalfWidth = 0.81f;
        private const int BurstPoolPerDirection = 4;
        private const int FeedbackPoolSize = 8;
        private const bool EnableExactFeedback = false;

        private sealed class BurstInstance
        {
            public GameObject Root;
            public Animator Animator;
            public SpriteRenderer Renderer;
            public Vector3 LocalRotation;
            public Vector3 LocalScale;
