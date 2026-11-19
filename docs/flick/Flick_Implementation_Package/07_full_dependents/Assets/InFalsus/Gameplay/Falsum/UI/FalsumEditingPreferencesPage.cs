using System.Collections.Generic;
using System.Globalization;
using InFalsus.Gameplay.Falsum.Core;
using InFalsus.Gameplay.Falsum.Runtime;
using UnityEngine;
using UnityEngine.UIElements;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.UI
{
    /// <summary>
    /// "Editing" page: how mouse interaction edits notes, plus how the editing
    /// surface is drawn.
    ///
    /// Grid colours and width used to live under "Advanced", which made sense
    /// only while everything was flat. They are drawing settings for the
    /// editing surface, so they belong next to the editing behaviour, and the
    /// out-of-bounds clamp is an editing guard as well.
    ///
    /// The grid-colour field reports invalid input live and an invalid sequence
    /// has to block commit, so the flag lives on this page instance and is read
    /// back by the shell.
    /// </summary>
    internal sealed class FalsumEditingPreferencesPage
    {
        private static readonly CultureInfo Invariant =
            CultureInfo.InvariantCulture;

        /// <summary>
        /// True while the grid-colour field holds text that does not parse.
        /// The shell refuses to commit in that state.
        /// </summary>
        public bool GridColorsInvalid { get; set; }
        public bool TimeSubdivisionInvalid { get; set; }

        /// <summary>
        /// True while the grid-denominator sequence field holds text that
        /// does not parse. Same contract as
        /// <see cref="GridColorsInvalid"/>: the shell refuses to commit.
        /// </summary>
        public bool DenominatorSequenceInvalid { get; set; }

        public void Build(
            VisualElement content,
            FalsumPreferences preferences,
            IFalsumPreferencesPageHost host)
        {
            GridColorsInvalid = false;
            TimeSubdivisionInvalid = false;
            DenominatorSequenceInvalid = false;
            BuildBehaviour(content, preferences);
            BuildBounds(content, preferences, host);
            BuildGrid(content, preferences, host);
        }

        private void BuildBehaviour(
            VisualElement content,
            FalsumPreferences preferences)
        {
            var group = FalsumPreferencesWidgets.CreateGroup(
                L.Get("falsum.preferences.editing.group"));

            var directDrag = new Toggle
            {
                value = preferences.Editing.DirectDragUnselectedNotes,
            };
            directDrag.AddToClassList("preferences-toggle");
            directDrag.RegisterValueChangedCallback(
                evt =>
                {
                    preferences.Editing.DirectDragUnselectedNotes =
                        evt.newValue;
                });
            group.Add(FalsumPreferencesWidgets.CreateRow(
                L.Get("falsum.preferences.editing.directDrag"),
                L.Get(
                    "falsum.preferences.editing.directDragDescription"),
                string.Empty,
                string.Empty,
                directDrag,
                string.Empty));

            var multi = new Toggle
            {
                value = preferences.Editing.MiddleEditAffectsAllSelected,
            };
            multi.AddToClassList("preferences-toggle");
            multi.RegisterValueChangedCallback(
                evt =>
                {
                    preferences.Editing.MiddleEditAffectsAllSelected =
                        evt.newValue;
                });
            group.Add(FalsumPreferencesWidgets.CreateRow(
                L.Get("falsum.preferences.editing.middleAllSelected"),
                L.Get(
                    "falsum.preferences.editing.middleAllSelectedDescription"),
                string.Empty,
                string.Empty,
                multi,
                string.Empty));

            var skyGroupSnap = new Toggle
            {
                value = preferences.Editing.SkyAreaGroupTimeSnap,
            };
            skyGroupSnap.AddToClassList("preferences-toggle");
            skyGroupSnap.RegisterValueChangedCallback(
                evt =>
                {
                    preferences.Editing.SkyAreaGroupTimeSnap =
                        evt.newValue;
                });
            group.Add(FalsumPreferencesWidgets.CreateRow(
                L.Get("falsum.preferences.editing.skyAreaSnap"),
                L.Get(
                    "falsum.preferences.editing.skyAreaSnapDescription"),
                string.Empty,
                string.Empty,
                skyGroupSnap,
                string.Empty));

            content.Add(group);
        }

        private static void BuildBounds(
            VisualElement content,
            FalsumPreferences preferences,
            IFalsumPreferencesPageHost host)
        {
            var boundsGroup = FalsumPreferencesWidgets.CreateGroup(
                L.Get("falsum.preferences.editing.boundsGroup"));
            var disableBoundsClamp = new Toggle
            {
                name = "PreferencesDisableOutOfBoundsClamping",
                value = preferences.Editing.DisableOutOfBoundsClamping,
            };
            disableBoundsClamp.AddToClassList("preferences-toggle");
            disableBoundsClamp.RegisterValueChangedCallback(
                evt =>
                {
                    preferences.Editing.DisableOutOfBoundsClamping =
                        evt.newValue;
                    host.ClearStatus();
                });
            boundsGroup.Add(FalsumPreferencesWidgets.CreateRow(
                L.Get("falsum.preferences.editing.disableClamp"),
                L.Get(
                    "falsum.preferences.editing.disableClampDescription"),
                string.Empty,
                string.Empty,
                disableBoundsClamp,
                string.Empty));
            content.Add(boundsGroup);
        }

        private void BuildGrid(
            VisualElement content,
            FalsumPreferences preferences,
            IFalsumPreferencesPageHost host)
        {
            var gridGroup = FalsumPreferencesWidgets.CreateGroup(
                L.Get("falsum.preferences.editing.gridGroup"));
            var timeSubdivision = new TextField
            {
                name = "PreferencesGridTimeSubdivision",
                value = preferences.Grid.TimeSubdivision.ToString(CultureInfo.InvariantCulture),
                maxLength = 4,
            };
            timeSubdivision.AddToClassList("preferences-field");
            timeSubdivision.RegisterValueChangedCallback(evt =>
            {
                var text = FalsumEditorView.SanitizeNumericInput(evt.newValue);
                timeSubdivision.SetValueWithoutNotify(text);
                TimeSubdivisionInvalid = !int.TryParse(text, out var value) || value < 1;
                timeSubdivision.EnableInClassList("is-invalid", TimeSubdivisionInvalid);
                if (TimeSubdivisionInvalid) return;
                preferences.Grid.TimeSubdivision = value;
                host.ClearStatus();
            });
            gridGroup.Add(FalsumPreferencesWidgets.CreateRow(
                L.Get("falsum.preferences.editing.timeSubdivision"),
                L.Get("falsum.preferences.editing.timeSubdivisionDescription"),
                string.Empty, string.Empty, timeSubdivision, string.Empty));
            var colors = new TextField
            {
                value = preferences.Grid.LineColors,
            };
            colors.AddToClassList("preferences-field");
            colors.AddToClassList("preferences-grid-colors");
            colors.RegisterValueChangedCallback(
                evt =>
                {
                    List<Color> parsed;
                    string error;
                    if (!FalsumBeatGrid.TryParseColorSequence(
                            evt.newValue,
                            out parsed,
                            out error))
                    {
                        colors.AddToClassList("is-invalid");
                        GridColorsInvalid = true;
                        return;
                    }
                    colors.RemoveFromClassList("is-invalid");
                    GridColorsInvalid = false;
                    preferences.Grid.LineColors =
                        FalsumBeatGrid.FormatColorSequence(parsed);
                    host.ClearStatus();
                });
            gridGroup.Add(FalsumPreferencesWidgets.CreateRow(
                L.Get("falsum.preferences.editing.gridColors"),
                L.Get(
                    "falsum.preferences.editing.gridColorsDescription"),
                string.Empty,
                string.Empty,
                colors,
                "extra-wide"));

            var gridWidth = new Slider(0.25f, 3.0f)
            {
                value = Mathf.Clamp(
                    preferences.Grid.LineWidthScale,
                    0.25f,
                    3.0f),
            };
            gridWidth.AddToClassList("falsum-slider");
            gridWidth.AddToClassList("preferences-volume-slider");
            var gridWidthValue = new Label(
                GridWidthLabel(gridWidth.value));
            gridWidthValue.AddToClassList("preferences-volume-value");
            gridWidth.RegisterValueChangedCallback(
                evt =>
                {
                    var next = Mathf.Clamp(evt.newValue, 0.25f, 3.0f);
                    preferences.Grid.LineWidthScale = next;
                    gridWidthValue.text = GridWidthLabel(next);
                });
            var gridWidthControl = new VisualElement();
            gridWidthControl.AddToClassList("preferences-volume-control");
            gridWidthControl.Add(gridWidth);
            gridWidthControl.Add(gridWidthValue);
            gridGroup.Add(FalsumPreferencesWidgets.CreateRow(
                L.Get("falsum.preferences.editing.gridWidth"),
                L.Get(
                    "falsum.preferences.editing.gridWidthDescription"),
                string.Empty,
                string.Empty,
                gridWidthControl,
                "stretch"));

            var denominators = new TextField
            {
                value = preferences.Grid.DenominatorSequence,
            };
            denominators.AddToClassList("preferences-field");
            denominators.AddToClassList("preferences-grid-colors");
            denominators.RegisterValueChangedCallback(
                evt =>
                {
                    List<int> parsed;
                    string error;
                    if (!FalsumBeatGrid.TryParseDenominatorSequence(
                            evt.newValue,
                            out parsed,
                            out error))
                    {
                        denominators.AddToClassList("is-invalid");
                        DenominatorSequenceInvalid = true;
                        return;
                    }
                    denominators.RemoveFromClassList("is-invalid");
                    DenominatorSequenceInvalid = false;
                    preferences.Grid.DenominatorSequence =
                        FalsumBeatGrid.FormatDenominatorSequence(parsed);
                    host.ClearStatus();
                });
            gridGroup.Add(FalsumPreferencesWidgets.CreateRow(
                L.Get(
                    "falsum.preferences.editing.denominatorSequence"),
                L.Get(
                    "falsum.preferences.editing.denominatorSequenceDescription"),
                string.Empty,
                string.Empty,
                denominators,
                "extra-wide"));

            if (FalsumMeasureNumberFeature.Available)
            {
                var measureNumbers = new Toggle
                {
                    name = "PreferencesShowMeasureNumbers",
                    value = preferences.Experimental.ShowMeasureNumbers,
                };
                measureNumbers.AddToClassList("preferences-toggle");
                measureNumbers.RegisterValueChangedCallback(
                    evt =>
                    {
                        preferences.Experimental.ShowMeasureNumbers =
                            evt.newValue;
                        host.ClearStatus();
                    });
                gridGroup.Add(FalsumPreferencesWidgets.CreateRow(
                    L.Get(
                        "falsum.preferences.editing.measureNumbers"),
                    L.Get(
                        "falsum.preferences.editing.measureNumbersDescription"),
                    string.Empty,
                    string.Empty,
                    measureNumbers,
                    string.Empty));
            }
            content.Add(gridGroup);
        }

        private static string GridWidthLabel(float value)
        {
            return L.Get(
                "falsum.preferences.editing.gridWidthValue",
                ("value", Mathf.Clamp(value, 0.25f, 3.0f)
                    .ToString("0.00", Invariant)));
        }
    }
}
