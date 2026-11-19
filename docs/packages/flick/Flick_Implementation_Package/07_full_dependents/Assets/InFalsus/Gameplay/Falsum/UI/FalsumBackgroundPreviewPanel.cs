using System;
using System.Collections.Generic;
using InFalsus.Gameplay.Falsum.Runtime;
using UnityEngine;
using UnityEngine.UIElements;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.UI
{
    /// <summary>
    /// The gameplay-background picker shown on top of the preferences dialog.
    ///
    /// It is a sub-modal rather than a page widget because selecting a
    /// background changes the live editor scene (the host is told to swap the
    /// background while the panel is open). It therefore owns its own open
    /// state, thumbnail lifetime and focus restore, and reports back through
    /// the <code>onChanged</code> callback instead of reaching into editor
    /// internals.
    /// </summary>
    internal sealed class FalsumBackgroundPreviewPanel
    {
        private readonly VisualElement _panel;
        private readonly VisualElement _hero;
        private readonly Label _nameLabel;
        private readonly Label _metaLabel;
        private readonly ScrollView _options;
        private readonly Label _selectionLabel;
        private readonly Label _statusLabel;
        private readonly VisualElement _overlay;
        private readonly VisualElement _editorRoot;
        private readonly VisualElement _visualRoot;
        private readonly Action<string> _onChanged;
        private readonly Action _onExited;

        private readonly List<FalsumBackgroundEntry> _backgrounds =
            new List<FalsumBackgroundEntry>();
        private readonly List<Button> _optionButtons =
            new List<Button>();

        private IVisualElementScheduledItem _optionsRefresh;
        private Texture2D _heroTexture;
        private VisualElement _focusBefore;
        private string _original;
        private string _selected;

        public FalsumBackgroundPreviewPanel(
            VisualElement panel,
            VisualElement hero,
            Label nameLabel,
            Label metaLabel,
            ScrollView options,
            Label selectionLabel,
            Label statusLabel,
            VisualElement overlay,
            VisualElement editorRoot,
            VisualElement visualRoot,
            Action<string> onChanged,
            Action onExited)
        {
            _panel = panel;
            _hero = hero;
            _nameLabel = nameLabel;
            _metaLabel = metaLabel;
            _options = options;
            _selectionLabel = selectionLabel;
            _statusLabel = statusLabel;
            _overlay = overlay;
            _editorRoot = editorRoot;
            _visualRoot = visualRoot;
            _onChanged = onChanged;
            _onExited = onExited;

            if (_panel == null)
            {
                return;
            }
            var close = _panel.Q<Button>("BackgroundPreviewClose");
            if (close != null)
            {
                close.clicked += Cancel;
            }
            var cancel = _panel.Q<Button>("BackgroundPreviewCancel");
            if (cancel != null)
            {
                cancel.clicked += Cancel;
            }
            var confirm = _panel.Q<Button>("BackgroundPreviewConfirm");
            if (confirm != null)
            {
                confirm.clicked += Confirm;
            }
        }

        public bool IsOpen
        {
            get
            {
                return _panel != null &&
                       !_panel.ClassListContains("hidden");
            }
        }

        /// <summary>The focus-trap root, exposed so the shell can route Tab.</summary>
        public VisualElement Root
        {
            get { return _panel; }
        }

        public string SelectedBackground
        {
            get { return _selected ?? string.Empty; }
        }

        /// <param name="current">Background file the draft currently holds.</param>
        public void Show(string current)
        {
            if (_panel == null)
            {
                return;
            }
            LoadBackgrounds();
            _original = current;
            _selected = current;
            _focusBefore = _overlay != null && _overlay.panel != null
                ? _overlay.panel.focusController.focusedElement
                    as VisualElement
                : null;
            FalsumPreferencesUiHelpers.SetVisible(_panel, true);
            FalsumUiMotion.AnimateWindowIn(
                _panel,
                0.14f,
                0.0f,
                0.98f);
            _overlay.AddToClassList("background-preview-mode");
            _overlay.style.backgroundColor = Color.clear;
            if (_editorRoot != null)
            {
                _editorRoot.AddToClassList("background-preview-active");
            }
            if (_visualRoot != null)
            {
                _visualRoot.AddToClassList("background-preview-active");
            }
            ScheduleOptionsRebuild();
            ApplySelectedPreview();
            var close = _panel.Q<Button>("BackgroundPreviewClose");
            if (close != null)
            {
                close.Focus();
            }
        }

        /// <summary>Esc / close: restores the background the draft started with.</summary>
        public void Cancel()
        {
            var original = _original;
            Exit();
            if (!string.IsNullOrWhiteSpace(original) &&
                _onChanged != null)
            {
                _onChanged(original);
            }
        }

        /// <summary>Enter / confirm: keeps the highlighted background.</summary>
        public void Confirm()
        {
            // The selection already reached the editor through _onChanged when
            // it was picked; closing is all that remains.
            Exit();
        }

        /// <summary>Closes without restoring; used when the dialog tears down.</summary>
        public void CloseImmediate()
        {
            Exit();
        }

        public void ShowStatus(string message)
        {
            if (_statusLabel == null)
            {
                return;
            }
            _statusLabel.text = message ?? string.Empty;
            FalsumPreferencesUiHelpers.SetVisible(
                _statusLabel,
                !string.IsNullOrWhiteSpace(message));
        }

        public void SelectByFileName(string fileName)
        {
            for (var i = 0; i < _backgrounds.Count; i++)
            {
                var entry = _backgrounds[i];
                if (string.Equals(
                        entry.FileName,
                        fileName,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        entry.DisplayName,
                        System.IO.Path.GetFileNameWithoutExtension(
                            fileName),
                        StringComparison.OrdinalIgnoreCase))
                {
                    Select(entry);
                    return;
                }
            }
        }

        private void Exit()
        {
            if (_optionsRefresh != null)
            {
                _optionsRefresh.Pause();
                _optionsRefresh = null;
            }
            var focus = _focusBefore;
            _focusBefore = null;
            FalsumPreferencesUiHelpers.SetVisible(_panel, false);
            if (_overlay != null)
            {
                _overlay.RemoveFromClassList("background-preview-mode");
                _overlay.style.backgroundColor =
                    FalsumUiMotion.DefaultBackdrop;
            }
            if (_editorRoot != null)
            {
                _editorRoot.RemoveFromClassList(
                    "background-preview-active");
            }
            if (_visualRoot != null)
            {
                _visualRoot.RemoveFromClassList(
                    "background-preview-active");
            }
            FalsumPreferencesUiHelpers.SetVisible(_statusLabel, false);
            if (focus != null && focus.panel != null)
            {
                focus.Focus();
            }
            if (_onExited != null)
            {
                _onExited();
            }
        }

        private void LoadBackgrounds()
        {
            FalsumBackgroundCatalog.ReleaseThumbnails(_backgrounds);
            _backgrounds.Clear();
            List<FalsumBackgroundEntry> entries;
            string error;
            if (!FalsumBackgroundCatalog.TryLoad(out entries, out error))
            {
                ShowStatus(error);
                return;
            }
            _backgrounds.AddRange(entries);
        }

        private void ScheduleOptionsRebuild()
        {
            if (_panel == null)
            {
                return;
            }
            if (_optionsRefresh != null)
            {
                _optionsRefresh.Pause();
                _optionsRefresh = null;
            }
            _optionsRefresh = _panel
                .schedule.Execute(
                    (Action)delegate
                    {
                        _optionsRefresh = null;
                        RebuildOptions();
                    });
            _optionsRefresh.ExecuteLater(1);
        }

        private void RebuildOptions()
        {
            if (_options == null || !IsOpen)
            {
                return;
            }
            _options.Clear();
            _optionButtons.Clear();
            for (var i = 0; i < _backgrounds.Count; i++)
            {
                var entry = _backgrounds[i];
                var button = new Button();
                button.AddToClassList("background-option");
                button.EnableInClassList(
                    "is-selected",
                    entry.FileName == _selected);
                var thumb = new VisualElement();
                thumb.AddToClassList("background-option-thumb");
                var texture =
                    FalsumBackgroundCatalog.LoadThumbnail(entry);
                if (texture != null)
                {
                    thumb.style.backgroundImage =
                        new StyleBackground(texture);
                }
                button.Add(thumb);
                var copy = new VisualElement();
                copy.AddToClassList("background-option-copy");
                var name = new Label(entry.FileName);
                name.AddToClassList("background-option-name");
                copy.Add(name);
                var meta = new Label(entry.DisplayName);
                meta.AddToClassList("background-option-meta");
                copy.Add(meta);
                button.Add(copy);
                var captured = entry;
                button.clicked += delegate
                {
                    Select(captured);
                };
                _options.Add(button);
                _optionButtons.Add(button);
            }
        }

        private void Select(FalsumBackgroundEntry entry)
        {
            if (entry == null)
            {
                return;
            }
            _selected = entry.FileName;
            ApplySelectedPreview();
            // Live-preview the choice: the editor swaps the real background
            // while the panel is open, so a selection must reach it at once
            // rather than only on confirm. Cancel restores the original.
            if (_onChanged != null)
            {
                _onChanged(_selected);
            }
            for (var i = 0;
                 i < _optionButtons.Count && i < _backgrounds.Count;
                 i++)
            {
                _optionButtons[i].EnableInClassList(
                    "is-selected",
                    _backgrounds[i].FileName == _selected);
            }
        }

        private void ApplySelectedPreview()
        {
            FalsumBackgroundEntry selected = null;
            for (var i = 0; i < _backgrounds.Count; i++)
            {
                if (_backgrounds[i].FileName == _selected)
                {
                    selected = _backgrounds[i];
                    break;
                }
            }
            var texture = selected != null
                ? FalsumBackgroundCatalog.LoadThumbnail(selected)
                : null;
            _heroTexture = texture;
            if (_hero != null)
            {
                _hero.style.backgroundImage = texture != null
                    ? new StyleBackground(texture)
                    : new StyleBackground();
            }
            if (_nameLabel != null)
            {
                _nameLabel.text = _selected ?? string.Empty;
            }
            if (_metaLabel != null)
            {
                _metaLabel.text = selected != null
                    ? L.Get(
                        "falsum.preferences.background.livePreview",
                        ("name", selected.DisplayName))
                    : L.Get(
                        "falsum.preferences.background.fileMissing");
            }
            if (_selectionLabel != null)
            {
                _selectionLabel.text = _selected ?? string.Empty;
            }
        }

        /// <summary>Releases catalog thumbnails; call when the dialog tears down.</summary>
        public void Dispose()
        {
            if (_optionsRefresh != null)
            {
                _optionsRefresh.Pause();
                _optionsRefresh = null;
            }
            _heroTexture = null;
            FalsumBackgroundCatalog.ReleaseThumbnails(_backgrounds);
            _backgrounds.Clear();
            _optionButtons.Clear();
        }
    }
}
