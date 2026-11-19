using System;
using System.Collections.Generic;
using InFalsus.Gameplay.Core.Charts;
using InFalsus.Gameplay.Core.Judgement;
using InFalsus.Gameplay.Runtime.SceneBinding;
using UnityEngine;

namespace InFalsus.Gameplay.Presentation.Effects
{
    public sealed class NoteHitEffectRenderer : MonoBehaviour
    {
        private const float MainInitialDuration = 0.70f;
        private const float MainFrameRate = 60.0f;
        private const float HoldLoopDecisionTime = 9.0f / MainFrameRate;
        private const float MainTailStartTime = 25.0f / MainFrameRate;
        private const float MainTailDuration =
            MainInitialDuration - MainTailStartTime;
        private const float RadialDurationFallback = 0.5333333f;
        private const float HoldLoopDurationFallback = 0.3333333f;
        private const float SideHoldScaleMultiplier = 1.4f;
        private const int PoolSize = 16;

        private enum EffectKind
        {
            Radial,
            Hold,
            Main,
        }

        private enum HoldMainPhase
        {
            Initial,
            Loop,
            Tail,
        }

        private sealed class EffectInstance
        {
            public GameObject Root;
            public Transform Transform;
            public Animator Animator;
            public SpriteRenderer Renderer;
            public EffectKind Kind;
            public Vector3 BaseScale;
            public Quaternion BaseLocalRotation;
            public float LocalYOffset;
            public float Duration;
            public ChartNote Note;
            public double StartedMs;
            public bool OwnedByHold;
            public HoldMainPhase MainPhase;
            public double PhaseStartedMs;
            public float MainClipLength;
            public bool Running;
        }

        private readonly List<EffectInstance> _radial =
            new List<EffectInstance>();
        private readonly List<EffectInstance> _hold =
            new List<EffectInstance>();
        private readonly List<EffectInstance> _main =
            new List<EffectInstance>();

        private JudgementEngine _engine;
        private GameplaySceneBinding _binding;
        private Transform _root;
        private bool _initialized;

        public int ActiveEffectCount { get; private set; }
        public int PlayedNoteCount { get; private set; }
        public int ActiveHoldEffectCount { get; private set; }

        private static readonly int MainState =
            Animator.StringToHash("NoteFX_Main");
        private static readonly int MainLoopState =
            Animator.StringToHash("NoteFX_Main_HoldLoop");
        private static readonly int RadialState =
            Animator.StringToHash("NoteFX_Radial");
        private static readonly int HoldState =
            Animator.StringToHash("NoteHoldFXV2");

        public void Initialize(
            JudgementEngine engine,
            GameplaySceneBinding binding)
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
            _root = binding.MasterScene;

            BuildPool(
                _radial,
                binding.NoteFXRadialTemplate,
                EffectKind.Radial,
                "Radial",
                RadialState,
                24);
            BuildPool(
                _hold,
                binding.NoteFXHoldTemplate,
                EffectKind.Hold,
                "Hold",
                HoldState,
                25);
            BuildPool(
                _main,
                binding.NoteFXMainTemplate,
                EffectKind.Main,
                "Main",
                MainState,
                26);

            _engine.NoteStarted += HandleNoteStarted;
            _engine.Judged += HandleJudged;
            _initialized = true;
        }

        public void Tick(double nowMs, bool paused)
        {
            if (!_initialized)
            {
                return;
            }

            var active = 0;
            var holdActive = 0;
            active += TickPool(_radial, nowMs, paused, ref holdActive);
            active += TickPool(_hold, nowMs, paused, ref holdActive);
            active += TickPool(_main, nowMs, paused, ref holdActive);
            ActiveEffectCount = active;
            ActiveHoldEffectCount = holdActive;
        }

        public void ResetVisuals()
        {
            ResetPool(_radial);
            ResetPool(_hold);
            ResetPool(_main);
            ActiveEffectCount = 0;
            ActiveHoldEffectCount = 0;
            PlayedNoteCount = 0;
        }

        public void SetEngine(JudgementEngine engine)
        {
            if (!_initialized)
            {
                throw new InvalidOperationException(
                    "NoteHitEffectRenderer is not initialized.");
            }
            if (_engine != null)
            {
                _engine.NoteStarted -= HandleNoteStarted;
                _engine.Judged -= HandleJudged;
            }
            _engine = engine;
            _engine.NoteStarted += HandleNoteStarted;
            _engine.Judged += HandleJudged;
            ResetVisuals();
        }

        private void BuildPool(
            List<EffectInstance> pool,
            GameObject template,
            EffectKind kind,
            string label,
            int stateHash,
            int sortingOrder)
        {
            if (template == null)
            {
                throw new InvalidOperationException(
                    "Note effect template is missing: " + label);
            }

            var templateTransform = template.transform;
            for (var i = 0; i < PoolSize; i++)
            {
                var clone = Instantiate(template, _root, false);
                clone.name = "PhaseA_NoteFX_" + label + "_" + i;
                clone.SetActive(false);

                var animator = clone.GetComponent<Animator>();
                var renderer = clone.GetComponent<SpriteRenderer>();
                if (animator == null || renderer == null)
                {
                    throw new InvalidOperationException(
                        "Note effect " + label +
                        " requires Animator and SpriteRenderer.");
                }

                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.applyRootMotion = false;
                renderer.sortingOrder = sortingOrder;

                var duration = ResolveDuration(
                    kind,
                    animator,
                    stateHash);
                pool.Add(new EffectInstance
                {
                    Root = clone,
                    Transform = clone.transform,
                    Animator = animator,
                    Renderer = renderer,
                    Kind = kind,
                    BaseScale = templateTransform.localScale,
                    BaseLocalRotation = templateTransform.localRotation,
                    LocalYOffset = templateTransform.localPosition.y,
                    Duration = duration,
                    MainClipLength = kind == EffectKind.Main
                        ? ResolveClipLength(
                            animator,
                            "NoteFX_Main",
                            MainInitialDuration)
                        : duration,
                });
            }
        }

        private static float ResolveClipLength(
            Animator animator,
            string clipName,
            float fallback)
        {
            var controller = animator.runtimeAnimatorController;
            if (controller != null)
            {
                var clips = controller.animationClips;
                for (var i = 0; i < clips.Length; i++)
                {
                    var clip = clips[i];
                    if (clip != null &&
                        clip.name == clipName &&
                        clip.length > 0.0f)
                    {
                        return clip.length;
                    }
                }
            }
            return fallback;
        }

        private static float ResolveDuration(
            EffectKind kind,
            Animator animator,
            int stateHash)
        {
            if (kind == EffectKind.Main)
            {
                return MainInitialDuration;
            }

            var controller = animator.runtimeAnimatorController;
            if (controller != null)
            {
                var clips = controller.animationClips;
                for (var i = 0; i < clips.Length; i++)
                {
                    var clip = clips[i];
                    if (clip == null)
                    {
                        continue;
                    }
                    var isExpected =
                        (kind == EffectKind.Radial &&
                         clip.name == "NoteFX_Radial") ||
                        (kind == EffectKind.Hold &&
                         clip.name == "NoteHoldFXV2");
                    if (isExpected && clip.length > 0.0f)
                    {
                        return clip.length;
                    }
                }
            }

            return kind == EffectKind.Radial
                ? RadialDurationFallback
                : HoldLoopDurationFallback;
        }

        private void HandleNoteStarted(JudgementEvent judgement)
        {
            if (!_initialized ||
                judgement == null ||
                judgement.Note == null ||
                judgement.Note.Kind != NoteKind.Hold ||
                !IsHit(judgement.Result))
            {
                return;
            }

            StartNoteEffects(
                judgement.Note,
                judgement.InputTimeMs,
                true);
        }

        private void HandleJudged(JudgementEvent judgement)
        {
            if (!_initialized ||
                judgement == null ||
                judgement.Note == null ||
                judgement.Note.Kind != NoteKind.Tap ||
                !IsHit(judgement.Result))
            {
                return;
            }

            StartNoteEffects(
                judgement.Note,
                judgement.InputTimeMs,
                false);
        }

        private void StartNoteEffects(
            ChartNote note,
            double inputTimeMs,
            bool ownedByHold)
        {
            var width = Mathf.Clamp(
                note.WidthLanes,
                1,
                Mathf.Max(1, 6 - note.StartLane));

            StartEffect(
                Acquire(_radial),
                note,
                inputTimeMs,
                false,
                width,
                RadialState);
            StartEffect(
                Acquire(_hold),
                note,
                inputTimeMs,
                ownedByHold,
                width,
                HoldState);
            StartEffect(
                Acquire(_main),
                note,
                inputTimeMs,
                ownedByHold,
                width,
                MainState);

            PlayedNoteCount++;
        }

        private static EffectInstance Acquire(
            List<EffectInstance> pool)
        {
            EffectInstance oldest = null;
            for (var i = 0; i < pool.Count; i++)
            {
                var effect = pool[i];
                if (!effect.Running)
                {
                    return effect;
                }
                if (oldest == null ||
                    effect.StartedMs < oldest.StartedMs)
                {
                    oldest = effect;
                }
            }
            return oldest;
        }

        private void StartEffect(
            EffectInstance effect,
            ChartNote note,
            double inputTimeMs,
            bool ownedByHold,
            int width,
            int stateHash)
        {
            if (effect == null)
            {
                return;
            }

            effect.Running = true;
            effect.StartedMs = inputTimeMs;
            effect.Note = note;
            effect.OwnedByHold = ownedByHold;
            effect.MainPhase = HoldMainPhase.Initial;
            effect.PhaseStartedMs = inputTimeMs;
            effect.Root.SetActive(true);
            ConfigureTransform(effect, note, width);

            effect.Animator.enabled = true;
            effect.Animator.speed = 1.0f;
            effect.Animator.Rebind();
            effect.Animator.Play(stateHash, 0, 0.0f);
            effect.Animator.Update(0.0001f);
            effect.Renderer.color = Color.white;
        }

        private void ConfigureTransform(
            EffectInstance effect,
            ChartNote note,
            int width)
        {
            var lane = Mathf.Clamp(note.StartLane, 0, 5);
            var endLane = Mathf.Clamp(
                lane + width - 1,
                0,
                5);
            var laneTemplate = _binding.TemplateForLane(lane);
            var endTemplate = _binding.TemplateForLane(endLane);
            var startLocal = _root.InverseTransformPoint(
                laneTemplate.transform.position);
            var endLocal = _root.InverseTransformPoint(
                endTemplate.transform.position);
            var lineLocal = _root.InverseTransformPoint(
                _binding.JudgementLine.position);

            var position = startLocal;
            if (effect.Kind == EffectKind.Hold)
            {
                position = (startLocal + endLocal) * 0.5f;
            }
            position.y += effect.LocalYOffset;
            position.z = lineLocal.z;
            effect.Transform.localPosition = position;
            effect.Transform.localRotation = ResolveRotation(
                effect,
                lane);

            var scale = effect.BaseScale;
            if (effect.Kind == EffectKind.Hold)
            {
                scale.x *= width;
                if (_binding.SideLaneForLane(lane) != null)
                {
                    scale *= SideHoldScaleMultiplier;
                }
            }
            effect.Transform.localScale = scale;
        }

        private Quaternion ResolveRotation(
            EffectInstance effect,
            int lane)
        {
            var laneTemplate = _binding.TemplateForLane(lane);
            var sideLane = _binding.SideLaneForLane(lane);
            if (sideLane != null)
            {
                var sideLocalRotation =
                    Quaternion.Inverse(_root.rotation) * sideLane.rotation;
                var widthAxis = sideLocalRotation * Vector3.forward;
                var slope = Mathf.Atan2(widthAxis.y, widthAxis.x) *
                            Mathf.Rad2Deg + 180.0f;
                return Quaternion.Euler(0.0f, 0.0f, slope) *
                       effect.BaseLocalRotation;
            }

            return Quaternion.Inverse(_root.rotation) *
                   laneTemplate.transform.rotation *
                   effect.BaseLocalRotation;
        }

        private int TickPool(
            List<EffectInstance> pool,
            double nowMs,
            bool paused,
            ref int activeHoldEffects)
        {
            var active = 0;
            for (var i = 0; i < pool.Count; i++)
            {
                var effect = pool[i];
                if (!effect.Running)
                {
                    continue;
                }

                var note = effect.Note;
                var elapsedMs = nowMs - effect.StartedMs;
                var holdEnded =
                    note == null ||
                    !note.Holding ||
                    note.Judged ||
                    nowMs >= note.EndTimeMs;
                if (effect.OwnedByHold &&
                    holdEnded &&
                    effect.Kind != EffectKind.Main)
                {
                    StopEffect(effect);
                    continue;
                }

                if (effect.Kind == EffectKind.Main &&
                    effect.OwnedByHold)
                {
                    if (effect.MainPhase == HoldMainPhase.Initial &&
                        !holdEnded &&
                        elapsedMs >= HoldLoopDecisionTime * 1000.0)
                    {
                        PlayMainPhase(
                            effect,
                            HoldMainPhase.Loop,
                            nowMs,
                            MainLoopState,
                            0.0f);
                    }
                    else if (effect.MainPhase == HoldMainPhase.Loop &&
                             holdEnded)
                    {
                        var normalizedTime = Mathf.Clamp01(
                            MainTailStartTime / effect.MainClipLength);
                        PlayMainPhase(
                            effect,
                            HoldMainPhase.Tail,
                            nowMs,
                            MainState,
                            normalizedTime);
                    }
                    else if (effect.MainPhase == HoldMainPhase.Initial &&
                             elapsedMs >= MainInitialDuration * 1000.0)
                    {
                        StopEffect(effect);
                        continue;
                    }
                    else if (effect.MainPhase == HoldMainPhase.Tail &&
                             nowMs - effect.PhaseStartedMs >=
                             MainTailDuration * 1000.0)
                    {
                        StopEffect(effect);
                        continue;
                    }
                }
                else if (effect.Kind == EffectKind.Main &&
                         !effect.OwnedByHold &&
                         elapsedMs >= MainInitialDuration * 1000.0)
                {
                    StopEffect(effect);
                    continue;
                }

                if (effect.Kind == EffectKind.Hold &&
                    !effect.OwnedByHold &&
                    elapsedMs >= effect.Duration * 1000.0)
                {
                    StopEffect(effect);
                    continue;
                }

                if (effect.Kind == EffectKind.Radial &&
                    elapsedMs >= effect.Duration * 1000.0)
                {
                    StopEffect(effect);
                    continue;
                }

                effect.Animator.speed = paused ? 0.0f : 1.0f;
                active++;
                if (effect.OwnedByHold)
                {
                    activeHoldEffects++;
                }
            }
            return active;
        }

        private static void PlayMainPhase(
            EffectInstance effect,
            HoldMainPhase phase,
            double nowMs,
            int stateHash,
            float normalizedTime)
        {
            effect.MainPhase = phase;
            effect.PhaseStartedMs = nowMs;
            effect.Animator.speed = 1.0f;
            effect.Animator.Play(stateHash, 0, normalizedTime);
            effect.Animator.Update(0.0001f);
        }

        private static void StopEffect(EffectInstance effect)
        {
            if (effect == null)
            {
                return;
            }

            effect.Running = false;
            effect.MainPhase = HoldMainPhase.Initial;
            effect.PhaseStartedMs = 0.0;
            effect.Note = null;
            if (effect.Root != null)
            {
                effect.Root.SetActive(false);
            }
        }

        private static void ResetPool(List<EffectInstance> pool)
        {
            for (var i = 0; i < pool.Count; i++)
            {
                StopEffect(pool[i]);
            }
        }

        private static bool IsHit(JudgementResult result)
        {
            return result == JudgementResult.Shiny ||
                   result == JudgementResult.PerfectEarly ||
                   result == JudgementResult.PerfectLate ||
                   result == JudgementResult.NearEarly ||
                   result == JudgementResult.NearLate;
        }

        private void OnDestroy()
        {
            if (_engine != null)
            {
                _engine.NoteStarted -= HandleNoteStarted;
                _engine.Judged -= HandleJudged;
            }
        }
    }
}

