using System.Collections.Generic;
using InFalsus.Gameplay.Falsum.Runtime;
using UnityEngine.UIElements;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.UI
{
    /// <summary>
    /// "Shortcuts" page: one row per input action, grouped by purpose.
    ///
    /// Capture itself is not owned here: a pending capture is dialog-wide
    /// state (it must survive a tab switch and block commit), so this page
    /// only starts captures and registers its rows with the controller.
    /// </summary>
    internal static class FalsumInputPreferencesPage
    {
        public static void Build(
            VisualElement content,
            FalsumPreferences preferences,
            IFalsumPreferencesPageHost host)
        {
            host.KeyCapture.Rebind(
                () => preferences,
                host.ShowError,
                host.ClearStatus);

            var reset = new Button
            {
                text = L.Get("falsum.preferences.keys.resetDefaults"),
            };
            reset.AddToClassList("preferences-inline-button");
            reset.clicked += delegate
            {
                var defaults = FalsumPreferences.CreateDefault();
                preferences.Bindings = defaults.Bindings;
                host.ShowInfo(
                    L.Get("falsum.preferences.keys.defaultsRestored"));
                host.RebuildTab(FalsumPreferencesTab.Keys);
            };
            content.Add(reset);

            var defaults = FalsumPreferences.CreateDefault();
            AddGroup(
                content,
                preferences,
                host,
                L.Get("falsum.preferences.keys.group.playback"),
                new[]
                {
                    FalsumInputAction.PlayPause,
                    FalsumInputAction.TemporaryAudition,
                },
                defaults);
            AddGroup(
                content,
                preferences,
                host,
                L.Get("falsum.preferences.keys.group.fileHistory"),
                new[]
                {
                    FalsumInputAction.Save,
                    FalsumInputAction.Undo,
                    FalsumInputAction.Redo,
                },
                defaults);
            AddGroup(
                content,
                preferences,
                host,
                L.Get("falsum.preferences.keys.group.clipboardSelection"),
                new[]
                {
                    FalsumInputAction.Copy,
                    FalsumInputAction.Cut,
                    FalsumInputAction.Paste,
                    FalsumInputAction.DeleteSelection,
                    FalsumInputAction.SegmentSelect,
                },
                defaults);
            AddGroup(
                content,
                preferences,
                host,
                L.Get("falsum.preferences.tab.middleEdit"),
                new[]
                {
                    FalsumInputAction.MiddleEditBoundary,
                    FalsumInputAction.MiddleEditStartEnd,
                    FalsumInputAction.MiddleEditPosition,
                },
                defaults);
            AddGroup(
                content,
                preferences,
                host,
                L.Get("falsum.preferences.keys.group.editing"),
                new[]
                {
                    FalsumInputAction.Mirror,
                    FalsumInputAction.MirrorFrontBack,
                    FalsumInputAction.ToggleFlickColor,
                    FalsumInputAction.ToggleGrid,
                    FalsumInputAction.PointPlaceTap,
                    FalsumInputAction.PointPlaceHold,
                    FalsumInputAction.PointPlaceFlick,
                    FalsumInputAction.PointPlaceSkyArea,
                    FalsumInputAction.DecreaseFlowSpeed,
                    FalsumInputAction.IncreaseFlowSpeed,
                    FalsumInputAction.ToggleTrackSpeedEffects,
                },
                defaults);
            AddGroup(
                content,
                preferences,
                host,
                L.Get("falsum.preferences.tab.display"),
                new[]
                {
                    FalsumInputAction.ToggleFullscreen,
                    FalsumInputAction.ToggleSkyAreaBank,
                },
                defaults);
        }

        private static void AddGroup(
            VisualElement content,
            FalsumPreferences preferences,
            IFalsumPreferencesPageHost host,
            string label,
            FalsumInputAction[] actions,
            FalsumPreferences defaults)
        {
            var group = FalsumPreferencesWidgets.CreateGroup(label);
            for (var i = 0; i < actions.Length; i++)
            {
                var action = actions[i];
                var binding = preferences.FindBinding(action);
                var defaultBinding = defaults.FindBinding(action);
                var row = new VisualElement();
                row.AddToClassList("preferences-key-row");
                var copy = new VisualElement();
                copy.AddToClassList("preferences-key-copy");
                var actionLabel = new Label(
                    FalsumInputRouter.ActionLabel(action));
                actionLabel.AddToClassList("preferences-key-label");
                copy.Add(actionLabel);
                var defaultLabel = new Label(
                    L.Get(
                        "falsum.preferences.keys.default",
                        ("shortcut",
                         FalsumInputRouter.Format(defaultBinding))));
                defaultLabel.AddToClassList("preferences-key-default");
                copy.Add(defaultLabel);
                row.Add(copy);

                var button = new Button
                {
                    text = FalsumInputRouter.Format(binding),
                };
                button.AddToClassList("preferences-key-button");
                button.clicked += delegate
                {
                    host.KeyCapture.Begin(action, button);
                };

                var resetButton = new Button
                {
                    text = L.Get("falsum.preferences.keys.restore"),
                };
                resetButton.AddToClassList("preferences-key-reset");
                resetButton.EnableInClassList(
                    "hidden",
                    FalsumInputRouter.BindingsEqual(
                        binding,
                        defaultBinding));
                resetButton.clicked += delegate
                {
                    if (defaultBinding == null)
                    {
                        return;
                    }
                    preferences.SetBinding(
                        action,
                        defaultBinding.Modifiers,
                        defaultBinding.Key);
                    button.text = FalsumInputRouter.Format(
                        preferences.FindBinding(action));
                    resetButton.AddToClassList("hidden");
                    host.ClearStatus();
                };

                host.KeyCapture.RegisterRow(
                    action,
                    button,
                    resetButton,
                    defaultBinding);
                row.Add(resetButton);
                row.Add(button);
                group.Add(row);
            }
            content.Add(group);
        }
    }
}
