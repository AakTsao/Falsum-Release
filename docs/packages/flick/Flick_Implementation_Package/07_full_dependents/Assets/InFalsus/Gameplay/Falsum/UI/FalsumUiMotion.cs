using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace InFalsus.Gameplay.Falsum.UI
{
    public static class FalsumUiMotion
    {
        public const float ModalDurationSeconds = 0.16f;
        public static readonly Color DefaultBackdrop =
            new Color(0.012f, 0.020f, 0.027f, 0.82f);

        public static void AnimateModalIn(
            VisualElement overlay,
            VisualElement window,
            float duration = ModalDurationSeconds,
            float delayMs = 0.0f)
        {
            AnimateModalIn(
                overlay,
                window,
                DefaultBackdrop,
                duration,
                delayMs);
        }

        public static void AnimateModalIn(
            VisualElement overlay,
            VisualElement window,
            Color backdropColor,
            float duration = ModalDurationSeconds,
            float delayMs = 0.0f)
        {
            if (overlay == null || window == null)
            {
                return;
            }
            var targetColor = backdropColor;
            overlay.pickingMode = PickingMode.Position;
            overlay.style.opacity = 1.0f;
            overlay.style.backgroundColor =
                WithAlpha(targetColor, 0.0f);
            window.style.scale =
                new Scale(new Vector2(0.965f, 0.965f));
            window.style.opacity = 0.0f;
            Animate(
                window,
                duration,
                delayMs,
                delegate(float eased)
                {
                    overlay.style.backgroundColor =
                        WithAlpha(
                            targetColor,
                            targetColor.a * eased);
                    window.style.opacity = eased;
                    var scale = Mathf.LerpUnclamped(
                        0.965f,
                        1.0f,
                        eased);
                    window.style.scale =
                        new Scale(new Vector2(scale, scale));
                },
                delegate
                {
                    overlay.style.backgroundColor = targetColor;
                    window.style.opacity = 1.0f;
                    window.style.scale =
                        new Scale(Vector2.one);
                });
        }

        public static void AnimateModalOut(
            VisualElement overlay,
            VisualElement window,
            Action completed,
            float duration = 0.14f)
        {
            if (overlay == null || window == null)
            {
                if (completed != null)
                {
                    completed();
                }
                return;
            }
            var startColor = overlay.resolvedStyle.backgroundColor;
            if (startColor.a < 0.001f)
            {
                startColor = DefaultBackdrop;
            }
            var startOpacity = window.resolvedStyle.opacity;
            if (startOpacity <= 0.001f)
            {
                startOpacity = 1.0f;
            }
            overlay.pickingMode = PickingMode.Ignore;
            Animate(
                window,
                duration,
                0.0f,
                delegate(float eased)
                {
                    overlay.style.backgroundColor =
                        WithAlpha(
                            startColor,
                            startColor.a * (1.0f - eased));
                    window.style.opacity =
                        Mathf.LerpUnclamped(
                            startOpacity,
                            0.0f,
                            eased);
                    var scale = Mathf.LerpUnclamped(
                        1.0f,
                        0.98f,
                        eased);
                    window.style.scale =
                        new Scale(new Vector2(scale, scale));
                },
                delegate
                {
                    overlay.style.backgroundColor =
                        WithAlpha(startColor, 0.0f);
                    window.style.opacity = 1.0f;
                    window.style.scale =
                        new Scale(Vector2.one);
                    overlay.pickingMode = PickingMode.Position;
                    if (completed != null)
                    {
                        completed();
                    }
                });
        }

        public static void AnimateWindowIn(
            VisualElement window,
            float duration = ModalDurationSeconds,
            float delayMs = 0.0f,
            float fromScale = 0.97f)
        {
            if (window == null)
            {
                return;
            }
            window.style.opacity = 0.0f;
            window.style.scale =
                new Scale(new Vector2(fromScale, fromScale));
            Animate(
                window,
                duration,
                delayMs,
                delegate(float eased)
                {
                    window.style.opacity = eased;
                    var scale = Mathf.LerpUnclamped(
                        fromScale,
                        1.0f,
                        eased);
                    window.style.scale =
                        new Scale(new Vector2(scale, scale));
                },
                delegate
                {
                    window.style.opacity = 1.0f;
                    window.style.scale =
                        new Scale(Vector2.one);
                });
        }

        private static void Animate(
            VisualElement scheduler,
            float duration,
            float delayMs,
            Action<float> step,
            Action completed)
        {
            double? start = null;
            var progress = 0.0f;
            IVisualElementScheduledItem animation = null;
            animation = scheduler.schedule.Execute(
                    (Action)delegate
                    {
                        var now = Time.unscaledTimeAsDouble;
                        if (!start.HasValue)
                        {
                            start = now;
                        }
                        var timeProgress = Mathf.Clamp01(
                            (float)((now - start.Value) /
                                    Mathf.Max(0.001f, duration)));
                        progress = Mathf.Min(
                            timeProgress,
                            progress + 0.25f);
                        var eased = 1.0f -
                                    Mathf.Pow(1.0f - progress, 3.0f);
                        if (step != null)
                        {
                            step(eased);
                        }
                        if (progress < 1.0f)
                        {
                            return;
                        }
                        if (completed != null)
                        {
                            completed();
                        }
                        if (animation != null)
                        {
                            animation.Pause();
                        }
                    })
                .Every(16)
                .StartingIn((long)Mathf.Max(0.0f, delayMs));
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = Mathf.Clamp01(alpha);
            return color;
        }
    }
}

