using System;
using System.Collections.Generic;
using InFalsus.Gameplay.Core.Charts;
using InFalsus.Gameplay.Core.Judgement;
using InFalsus.Gameplay.Runtime.SceneBinding;
using UnityEngine;
using UnityEngine.Rendering;

namespace InFalsus.Gameplay.Presentation.Effects
{
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
            public float LocalY;
            public float LocalZ;
            public float Duration;
            public double StartedMs;
            public bool Running;
        }

        private sealed class FeedbackInstance
        {
            public GameObject Root;
            public Transform Transform;
            public SpriteRenderer Renderer;
            public MaterialPropertyBlock Block;
            public Vector3 BasePosition;
            public double StartedMs;
            public bool Running;
        }

        private readonly List<BurstInstance> _rightBursts =
            new List<BurstInstance>();
        private readonly List<BurstInstance> _leftBursts =
            new List<BurstInstance>();
        private readonly List<FeedbackInstance> _feedbacks =
            new List<FeedbackInstance>();

        private JudgementEngine _engine;
        private GameplaySceneBinding _binding;
        private Camera _camera;
        private Material _burstMaterial;
        private Material _exactPlusMaterial;
        private Material _exactMaterial;
        private Material _nearMaterial;
        private Material _breakMaterial;
        private Sprite _exactPlusSprite;
        private Sprite _exactSprite;
        private Sprite _nearSprite;
        private Sprite _breakSprite;
        private bool _initialized;

        public int ActiveBurstCount { get; private set; }
        public int ActiveFeedbackCount { get; private set; }
        public int PlayedBurstCount { get; private set; }

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int ZTestId = Shader.PropertyToID("_ZTest");
        private static readonly int BurstStateHash =
            Animator.StringToHash("FlickBurstAnim");

        public void Initialize(
            JudgementEngine engine,
            GameplaySceneBinding binding,
            Texture2D exactPlusTexture,
            Texture2D exactTexture,
            Texture2D nearTexture,
            Texture2D breakTexture,
            Material feedbackMaterial)
        {
            if (engine == null)
            {
                throw new ArgumentNullException("engine");
            }
            if (binding == null)
            {
                throw new ArgumentNullException("binding");
            }

            _engine = engine;
            _binding = binding;
            _camera = Camera.main;
            _burstMaterial = CreateBurstMaterial();

            BuildBurstPool(
                _rightBursts,
                binding.FlickBurstRightTemplate,
                "Right");
            BuildBurstPool(
                _leftBursts,
                binding.FlickBurstLeftTemplate,
                "Left");
            BuildFeedbackPool(
                exactPlusTexture,
                exactTexture,
                nearTexture,
                breakTexture,
                feedbackMaterial);

            _engine.Judged += HandleJudged;
            _initialized = true;
        }

        public void Tick(double nowMs, bool paused)
        {
            if (!_initialized)
            {
                return;
            }

            ActiveBurstCount = TickBursts(_rightBursts, nowMs, paused);
            ActiveBurstCount += TickBursts(_leftBursts, nowMs, paused);
            ActiveFeedbackCount = TickFeedbacks(nowMs);
        }

        public void ResetVisuals()
        {
            ResetBursts(_rightBursts);
            ResetBursts(_leftBursts);
            for (var i = 0; i < _feedbacks.Count; i++)
            {
                var feedback = _feedbacks[i];
                feedback.Running = false;
                feedback.StartedMs = 0.0;
                if (feedback.Root != null)
                {
                    feedback.Root.SetActive(false);
                }
            }
            ActiveBurstCount = 0;
            ActiveFeedbackCount = 0;
            PlayedBurstCount = 0;
        }

        public void SetEngine(JudgementEngine engine)
        {
            if (!_initialized)
            {
                throw new InvalidOperationException(
                    "FlickHitEffectRenderer is not initialized.");
            }
            if (_engine != null)
            {
                _engine.Judged -= HandleJudged;
            }
            _engine = engine;
            _engine.Judged += HandleJudged;
            ResetVisuals();
        }

        private Material CreateBurstMaterial()
        {
            var shader = Shader.Find("InFalsus/Compat/StenciledUnlitSprite");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "Unable to find a shader for the FlickBurst effect.");
            }

            var material = new Material(shader)
            {
                name = "PhaseA_FlickBurst_Runtime",
                renderQueue = 3015,
            };
            if (material.HasProperty(ZTestId))
            {
                material.SetFloat(ZTestId, (float)CompareFunction.Always);
            }
            if (material.HasProperty(ColorId))
            {
                material.SetColor(ColorId, Color.white);
            }
            return material;
        }

        private void BuildBurstPool(
            List<BurstInstance> pool,
            GameObject template,
            string label)
        {
            if (template == null)
            {
                throw new InvalidOperationException(
                    "FlickBurst " + label + " template is missing.");
            }

            var templateTransform = template.transform;
            for (var i = 0; i < BurstPoolPerDirection; i++)
            {
                var clone = Instantiate(
                    template,
                    templateTransform.parent,
                    false);
                clone.name = "PhaseA_FlickBurst_" + label + "_" + i;
                clone.SetActive(false);

                var animator = clone.GetComponent<Animator>();
                var spriteRenderer = clone.GetComponent<SpriteRenderer>();
                if (animator == null || spriteRenderer == null)
                {
                    throw new InvalidOperationException(
                        "FlickBurst " + label +
                        " requires Animator and SpriteRenderer.");
                }

                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.applyRootMotion = false;
                spriteRenderer.sharedMaterial = _burstMaterial;
                spriteRenderer.color = Color.white;

                pool.Add(new BurstInstance
                {
                    Root = clone,
                    Animator = animator,
                    Renderer = spriteRenderer,
                    LocalY = templateTransform.localPosition.y,
                    LocalZ = templateTransform.localPosition.z,
                    LocalRotation =
                        templateTransform.localEulerAngles,
                    LocalScale = templateTransform.localScale,
                    Duration = ResolveClipLength(animator),
                });
            }
        }

        private static float ResolveClipLength(Animator animator)
        {
            var controller = animator.runtimeAnimatorController;
            if (controller != null)
            {
                var clips = controller.animationClips;
                for (var i = 0; i < clips.Length; i++)
                {
                    if (clips[i] != null && clips[i].length > 0.0f)
                    {
                        return clips[i].length;
                    }
                }
            }
            return BurstDurationFallback;
        }

        private void BuildFeedbackPool(
            Texture2D exactPlusTexture,
            Texture2D exactTexture,
            Texture2D nearTexture,
            Texture2D breakTexture,
            Material templateMaterial)
        {
            if (templateMaterial == null ||
                (exactPlusTexture == null &&
                 exactTexture == null &&
                 nearTexture == null &&
                 breakTexture == null))
            {
                return;
            }

            _exactPlusMaterial = CreateFeedbackMaterial(
                templateMaterial,
                exactPlusTexture,
                "ExactPlus");
            _exactMaterial = CreateFeedbackMaterial(
                templateMaterial,
                exactTexture,
                "Exact");
            _nearMaterial = CreateFeedbackMaterial(
                templateMaterial,
                nearTexture,
                "Near");
            _breakMaterial = CreateFeedbackMaterial(
                templateMaterial,
                breakTexture,
                "Break");

            _exactPlusSprite = CreateFeedbackSprite(exactPlusTexture);
            _exactSprite = CreateFeedbackSprite(exactTexture);
            _nearSprite = CreateFeedbackSprite(nearTexture);
            _breakSprite = CreateFeedbackSprite(breakTexture);

            for (var i = 0; i < FeedbackPoolSize; i++)
            {
                var root = new GameObject("PhaseA_FlickFeedback_" + i);
                root.transform.SetParent(_binding.JudgementLine, false);
                var spriteRenderer = root.AddComponent<SpriteRenderer>();
                spriteRenderer.shadowCastingMode =
                    ShadowCastingMode.Off;
                spriteRenderer.receiveShadows = false;
                spriteRenderer.sortingOrder = 20;
                root.SetActive(false);
                _feedbacks.Add(new FeedbackInstance
                {
                    Root = root,
                    Transform = root.transform,
                    Renderer = spriteRenderer,
                    Block = new MaterialPropertyBlock(),
                });
            }
        }

        private static Material CreateFeedbackMaterial(
            Material template,
            Texture2D texture,
            string label)
        {
            if (texture == null)
            {
                return null;
            }

            var material = new Material(template)
            {
                name = "PhaseA_FlickFeedback_" + label,
                renderQueue = 3110,
            };
            material.SetTexture("_MainTex", texture);
            if (material.HasProperty(ZTestId))
            {
                material.SetFloat(ZTestId, (float)CompareFunction.Always);
            }
            if (material.HasProperty(ColorId))
            {
                material.SetColor(ColorId, Color.white);
            }
            return material;
        }

        private static Sprite CreateFeedbackSprite(Texture2D texture)
        {
            if (texture == null)
            {
                return null;
            }

            return Sprite.Create(
                texture,
                new Rect(0.0f, 0.0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100.0f,
                0,
                SpriteMeshType.FullRect);
        }

        private void HandleJudged(JudgementEvent judgement)
        {
            if (!_initialized ||
                judgement == null ||
                judgement.Note == null ||
                judgement.Note.Kind != NoteKind.Flick ||
                !IsHit(judgement.Result))
            {
                return;
            }

            var note = judgement.Note;
            var burst = AcquireBurst(note.FlickDirection == 4);
            if (burst != null)
            {
                StartBurst(
                    burst,
                    note.FlickHeadPos,
                    judgement.InputTimeMs);
            }
            if (ShouldShowFeedback(judgement.Result))
            {
                StartFeedback(note, judgement.Result, judgement.InputTimeMs);
            }
        }

        private BurstInstance AcquireBurst(bool right)
        {
            var pool = right ? _rightBursts : _leftBursts;
            BurstInstance oldest = null;
            for (var i = 0; i < pool.Count; i++)
            {
                var burst = pool[i];
                if (!burst.Running)
                {
                    return burst;
                }
                if (oldest == null ||
                    burst.StartedMs < oldest.StartedMs)
                {
                    oldest = burst;
                }
            }
            return oldest;
        }

        private void StartBurst(
            BurstInstance burst,
            double headPosition,
            double inputTimeMs)
        {
            burst.Running = true;
            burst.StartedMs = inputTimeMs;
            burst.Root.SetActive(true);
            var transform = burst.Root.transform;
            transform.localPosition = new Vector3(
                TrackX((float)headPosition),
                burst.LocalY,
                burst.LocalZ);
            transform.localRotation = Quaternion.Euler(
                burst.LocalRotation);
            transform.localScale = burst.LocalScale;

            burst.Animator.enabled = true;
            burst.Animator.speed = 1.0f;
            burst.Animator.Rebind();
            burst.Animator.Play(BurstStateHash, 0, 0.0f);
            burst.Animator.Update(0.0001f);
            PlayedBurstCount++;
        }

        private void StartFeedback(
            ChartNote note,
            JudgementResult result,
            double inputTimeMs)
        {
            if (_feedbacks.Count == 0)
            {
                return;
            }

            var material = MaterialFor(result);
            var sprite = SpriteFor(result);
            if (material == null || sprite == null)
            {
                return;
            }

            FeedbackInstance feedback = null;
            FeedbackInstance oldest = null;
            for (var i = 0; i < _feedbacks.Count; i++)
            {
                var candidate = _feedbacks[i];
                if (!candidate.Running)
                {
                    feedback = candidate;
                    break;
                }
                if (oldest == null ||
                    candidate.StartedMs < oldest.StartedMs)
                {
                    oldest = candidate;
                }
            }
            if (feedback == null)
            {
                feedback = oldest;
            }

            var template = note.FlickDirection == 4
                ? _binding.FlickBurstRightTemplate
                : _binding.FlickBurstLeftTemplate;
            if (template == null)
            {
                return;
            }

            var templateTransform = template.transform;
            var parent = templateTransform.parent;
            // Feedback belongs to the judgement line, not the burst sprite offset.
            var worldPosition = parent.TransformPoint(
                new Vector3(
                    TrackX((float)note.FlickCenterPos),
                    0.0f,
                    templateTransform.localPosition.z));
            var camera = _camera != null ? _camera : Camera.main;
            if (camera != null)
            {
                worldPosition += camera.transform.up * FeedbackHeight;
            }
            else
            {
                worldPosition += Vector3.up * FeedbackHeight;
            }

            feedback.Running = true;
            feedback.StartedMs = inputTimeMs;
            feedback.BasePosition = worldPosition;
            feedback.Root.SetActive(true);
            feedback.Transform.position = worldPosition;
            if (camera != null)
            {
                feedback.Transform.rotation = camera.transform.rotation;
            }
            feedback.Renderer.sharedMaterial = material;
            feedback.Renderer.sprite = sprite;
            feedback.Renderer.color = Color.white;
            feedback.Transform.localScale =
                Vector3.one * FeedbackScale;
            ApplyFeedbackVisual(feedback, 0.0f, camera);
        }

        private Material MaterialFor(JudgementResult result)
        {
            switch (Judgement.TierOf(result))
            {
                case JudgementTier.ExactPlus:
                    return _exactPlusMaterial;
                case JudgementTier.Exact:
                    return _exactMaterial;
                case JudgementTier.Near:
                    return _nearMaterial;
                default:
                    return _breakMaterial;
            }
        }

        private Sprite SpriteFor(JudgementResult result)
        {
            switch (Judgement.TierOf(result))
            {
                case JudgementTier.ExactPlus:
                    return _exactPlusSprite;
                case JudgementTier.Exact:
                    return _exactSprite;
                case JudgementTier.Near:
                    return _nearSprite;
                default:
                    return _breakSprite;
            }
        }

        private static int TickBursts(
            List<BurstInstance> pool,
            double nowMs,
            bool paused)
        {
            var active = 0;
            for (var i = 0; i < pool.Count; i++)
            {
                var burst = pool[i];
                if (!burst.Running)
                {
                    continue;
                }

                var elapsedMs = nowMs - burst.StartedMs;
                if (elapsedMs >= burst.Duration * 1000.0)
                {
                    burst.Running = false;
                    burst.Root.SetActive(false);
                    continue;
                }

                var elapsedSeconds = (float)(elapsedMs / 1000.0);
                var normalized = Mathf.Clamp01(
                    elapsedSeconds / Mathf.Max(0.0001f, burst.Duration));
                burst.Renderer.color = new Color(
                    1.0f,
                    1.0f,
                    1.0f,
                    Mathf.Pow(1.0f - normalized, BurstFadePower));
                burst.Animator.speed = paused ? 0.0f : 1.0f;
                active++;
            }
            return active;
        }

        private int TickFeedbacks(double nowMs)
        {
            var camera = _camera != null ? _camera : Camera.main;
            var active = 0;
            for (var i = 0; i < _feedbacks.Count; i++)
            {
                var feedback = _feedbacks[i];
                if (!feedback.Running)
                {
                    continue;
                }

                var elapsedSeconds = (float)(
                    (nowMs - feedback.StartedMs) / 1000.0);
                if (elapsedSeconds >= FeedbackDuration)
                {
                    feedback.Running = false;
                    feedback.Root.SetActive(false);
                    continue;
                }

                // StartFeedback already activated the root. Re-activating it
                // every frame is a redundant SetActive (it dirties the
                // transform hierarchy for no visual change), so only the
                // inactive case is repaired here.
                if (!feedback.Root.activeSelf)
                {
                    feedback.Root.SetActive(true);
                }
                if (camera != null)
                {
                    feedback.Transform.rotation =
                        camera.transform.rotation;
                }
                ApplyFeedbackVisual(
                    feedback,
                    elapsedSeconds,
                    camera);
                active++;
            }
            return active;
        }

        private void ApplyFeedbackVisual(
            FeedbackInstance feedback,
            float elapsedSeconds,
            Camera camera)
        {
            var fadeIn = SmoothStep(0.0f, 0.035f, elapsedSeconds);
            var fadeOut = 1.0f - SmoothStep(
                0.30f,
                FeedbackDuration,
                elapsedSeconds);
            var pulse = Mathf.Lerp(
                1.08f,
                1.0f,
                SmoothStep(0.0f, 0.11f, elapsedSeconds));
            var rise = elapsedSeconds * 0.12f;
            var up = camera != null
                ? camera.transform.up
                : Vector3.up;

            feedback.Transform.position =
                feedback.BasePosition + up * rise;
            feedback.Transform.localScale =
                Vector3.one * FeedbackScale * pulse;
            feedback.Renderer.GetPropertyBlock(feedback.Block);
            feedback.Block.SetColor(
                ColorId,
                new Color(1.0f, 1.0f, 1.0f, fadeIn * fadeOut));
            feedback.Renderer.SetPropertyBlock(feedback.Block);
            feedback.Renderer.color = Color.white;
        }

        private static void ResetBursts(List<BurstInstance> pool)
        {
            for (var i = 0; i < pool.Count; i++)
            {
                var burst = pool[i];
                burst.Running = false;
                burst.StartedMs = 0.0;
                if (burst.Root != null)
                {
                    burst.Root.SetActive(false);
                }
            }
        }

        private static float SmoothStep(float from, float to, float value)
        {
            if (Mathf.Approximately(from, to))
            {
                return value >= to ? 1.0f : 0.0f;
            }
            var t = Mathf.Clamp01((value - from) / (to - from));
            return t * t * (3.0f - 2.0f * t);
        }

        private static float TrackX(float position)
        {
            return Mathf.Lerp(-TrackHalfWidth, TrackHalfWidth, position);
        }

        private static bool IsHit(JudgementResult result)
        {
            return result == JudgementResult.Shiny ||
                   result == JudgementResult.PerfectEarly ||
                   result == JudgementResult.PerfectLate ||
                   result == JudgementResult.NearEarly ||
                   result == JudgementResult.NearLate;
        }

        private static bool ShouldShowFeedback(JudgementResult result)
        {
            var tier = Judgement.TierOf(result);
            return EnableExactFeedback ||
                   (tier != JudgementTier.ExactPlus &&
                    tier != JudgementTier.Exact);
        }

        private void OnDestroy()
        {
            if (_engine != null)
            {
                _engine.Judged -= HandleJudged;
            }

            DestroyMaterial(_burstMaterial);
            DestroyMaterial(_exactPlusMaterial);
            DestroyMaterial(_exactMaterial);
            DestroyMaterial(_nearMaterial);
            DestroyMaterial(_breakMaterial);
            DestroySprite(_exactPlusSprite);
            DestroySprite(_exactSprite);
            DestroySprite(_nearSprite);
            DestroySprite(_breakSprite);
        }

        private static void DestroyMaterial(Material material)
        {
            if (material != null)
            {
                Destroy(material);
            }
        }

        private static void DestroySprite(Sprite sprite)
        {
            if (sprite != null)
            {
                Destroy(sprite);
            }
        }
    }
}

