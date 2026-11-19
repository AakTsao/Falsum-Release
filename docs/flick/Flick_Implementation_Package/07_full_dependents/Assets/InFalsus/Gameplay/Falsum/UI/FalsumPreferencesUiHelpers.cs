using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace InFalsus.Gameplay.Falsum.UI{
    /// <summary>
    /// Small UI-Toolkit helpers shared by the preferences dialog shell and its
    /// sub-dialogs. Kept in one place so the focus trap and the visibility
    /// convention cannot drift between the shell and a sub-dialog.
    /// </summary>
    internal static class FalsumPreferencesUiHelpers
    {
        public static void SetVisible(VisualElement element, bool visible)
        {
            if (element == null)
            {
                return;
            }
            element.EnableInClassList("hidden", !visible);
        }

        public static void TrapFocus(
            VisualElement root,
            KeyDownEvent evt)
        {
            if (root == null)
            {
                evt.StopImmediatePropagation();
                return;
            }

            var candidates = new List<VisualElement>();
            root.Query<VisualElement>().ForEach(
                element =>
                {
                    if (!element.focusable ||
                        !element.enabledSelf ||
                        element.resolvedStyle.display ==
                        DisplayStyle.None)
                    {
                        return;
                    }
                    candidates.Add(element);
                });
            if (candidates.Count == 0)
            {
                evt.StopImmediatePropagation();
                return;
            }

            var focused = root.panel != null
                ? root.panel.focusController.focusedElement as VisualElement
                : null;
            var current = candidates.IndexOf(focused);
            var reverse =
                (evt.modifiers & EventModifiers.Shift) != 0;
            var next = current < 0
                ? reverse ? candidates.Count - 1 : 0
                : (current +
                   (reverse ? -1 : 1) +
                   candidates.Count) %
                  candidates.Count;
            candidates[next].Focus();
            evt.StopImmediatePropagation();
        }
    }
}
