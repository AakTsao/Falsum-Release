using System;
using UnityEngine;
using UnityEngine.UIElements;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.UI
{
    /// <summary>
    /// Row / group / field factory shared by every preferences page.
    ///
    /// Extracted from the old monolithic view so a page can build its own
    /// controls without reaching into the shell's visual tree. The class name
    /// and members mirror the original helpers so the migrated page code is a
    /// pure move, not a rewrite.
    /// </summary>
    internal static class FalsumPreferencesWidgets
    {
        public static VisualElement CreateGroup(string label)
        {
            var group = new VisualElement();
            group.AddToClassList("preferences-group");
            var title = new Label(label);
            title.AddToClassList("preferences-group-label");
            group.Add(title);
            return group;
        }

        public static VisualElement CreateRow(
            string title,
            string description,
            string badge,
            string badgeClass,
            VisualElement control,
            string controlClass)
        {
            var row = new VisualElement();
            row.AddToClassList("preferences-row");
            var copy = new VisualElement();
            copy.AddToClassList("preferences-row-copy");
            copy.tooltip = description ?? string.Empty;
            var titleElement = new VisualElement();
            titleElement.style.flexDirection = FlexDirection.Row;
            titleElement.style.alignItems = Align.Center;
            var titleLabel = new Label(title);
            titleLabel.AddToClassList("preferences-row-title");
            titleElement.Add(titleLabel);
            if (!string.IsNullOrWhiteSpace(badge))
            {
                var badgeLabel = new Label(badge);
                badgeLabel.AddToClassList("preferences-badge");
                if (!string.IsNullOrWhiteSpace(badgeClass))
                {
                    badgeLabel.AddToClassList(badgeClass);
                }
                titleElement.Add(badgeLabel);
            }
            copy.Add(titleElement);
            if (!string.IsNullOrWhiteSpace(description))
            {
                var descriptionLabel = new Label(description);
                descriptionLabel.AddToClassList(
                    "preferences-row-description");
                descriptionLabel.EnableInClassList(
                    "preferences-description-verbose",
                    IsVerboseDescription(description));
                copy.Add(descriptionLabel);
            }
            row.Add(copy);
            if (control != null)
            {
                var holder = new VisualElement();
                holder.AddToClassList("preferences-row-control");
                if (!string.IsNullOrWhiteSpace(controlClass))
                {
                    holder.AddToClassList(controlClass);
                }
                holder.Add(control);
                row.Add(holder);
            }
            return row;
        }

        /// <summary>
        /// Localized display name for a difficulty index (0..3). Shared by the
        /// backup list and the chart info panel.
        /// </summary>
        public static string DifficultyDisplayName(int index)
        {
            switch (index)
            {
                case 1:
                    return L.Get("falsum.difficulty.evo");
                case 2:
                    return L.Get("falsum.difficulty.ult");
                case 3:
                    return L.Get("falsum.difficulty.fbd");
                default:
                    return L.Get("falsum.difficulty.min");
            }
        }

        /// <summary>
        /// Layout policy: some descriptions are long enough to need the
        /// compact row layout. Which prefixes count as verbose is a localized
        /// string table (C-1 in the invariants), not a hardcoded list.
        /// </summary>
        public static bool IsVerboseDescription(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                return false;
            }
            var segments = L.Get(
                    "falsum.preferences.layout.verbosePrefixes")
                .Split('|');
            for (var i = 0; i < segments.Length; i++)
            {
                var prefix = segments[i];
                if (!string.IsNullOrEmpty(prefix) &&
                    description.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
