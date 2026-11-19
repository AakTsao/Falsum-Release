using System;
using System.Collections.Generic;
using InFalsus.Gameplay.Falsum.Runtime;
using UnityEngine;
using UnityEngine.UIElements;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.UI
{
    /// <summary>
    /// Owns the "press a key now" state of the shortcuts page.
    ///
    /// Capture is deliberately kept above the pages rather than inside the
    /// shortcuts page: while a capture is pending the whole dialog must
    /// swallow key input (Esc cancels, every other key becomes the new
    /// binding), and the shell still has to refuse to commit. Putting this
    /// state in a page would let a tab switch silently drop a half-finished
    /// capture.
    /// </summary>
    internal sealed class FalsumKeyCaptureController
    {
        private readonly Dictionary<FalsumInputAction, Button>
            _buttons = new Dictionary<FalsumInputAction, Button>();
        private readonly Dictionary<FalsumInputAction, Button>
            _resetButtons = new Dictionary<FalsumInputAction, Button>();
        private readonly Dictionary<FalsumInputAction, FalsumKeyBinding>
            _defaults = new Dictionary<FalsumInputAction, FalsumKeyBinding>();

        private Action<string> _showError;
        private Action _clearStatus;
        private Func<FalsumPreferences> _preferences;
        private Button _capturingButton;
        private FalsumInputAction? _capturingAction;

        public bool IsCapturing
        {
            get { return _capturingAction.HasValue; }
        }

        /// <summary>
        /// Rebinds the callbacks and clears the per-page button registries.
        /// Called whenever the shortcuts page is rebuilt, because the old
        /// buttons are discarded with the page content.
        /// </summary>
        public void Rebind(
            Func<FalsumPreferences> preferences,
            Action<string> showError,
            Action clearStatus)
        {
            _preferences = preferences;
            _showError = showError;
            _clearStatus = clearStatus;
            _buttons.Clear();
            _resetButtons.Clear();
            _defaults.Clear();
            _capturingButton = null;
            _capturingAction = null;
        }

        public void RegisterRow(
            FalsumInputAction action,
            Button button,
            Button resetButton,
            FalsumKeyBinding defaultBinding)
        {
            _buttons[action] = button;
            _resetButtons[action] = resetButton;
            _defaults[action] = defaultBinding != null
                ? defaultBinding.Clone()
                : null;
        }

        public void Begin(FalsumInputAction action, Button button)
        {
            if (_capturingButton != null && _capturingAction.HasValue)
            {
                var oldAction = _capturingAction.Value;
                _capturingButton.text = FalsumInputRouter.Format(
                    _preferences().FindBinding(oldAction));
                _capturingButton.RemoveFromClassList("is-capturing");
            }
            _capturingAction = action;
            _capturingButton = button;
            button.text = L.Get("falsum.preferences.keys.capture");
            button.AddToClassList("is-capturing");
            button.Focus();
            _clearStatus();
        }

        public void Cancel()
        {
            if (_capturingButton == null || !_capturingAction.HasValue)
            {
                return;
            }
            _capturingButton.text = FalsumInputRouter.Format(
                _preferences().FindBinding(_capturingAction.Value));
            _capturingButton.RemoveFromClassList("is-capturing");
            _capturingAction = null;
            _capturingButton = null;
            _clearStatus();
        }

        /// <summary>
        /// Handles a key-down while a capture is pending. Returns true when
        /// the event was consumed, so the caller can stop routing it.
        /// </summary>
        public bool HandleKeyDown(KeyDownEvent evt)
        {
            if (!_capturingAction.HasValue)
            {
                return false;
            }
            evt.StopPropagation();
            if (evt.keyCode == KeyCode.Escape)
            {
                Cancel();
                return true;
            }
            FalsumKeyBinding binding;
            if (!FalsumInputRouter.TryCapture(evt, out binding))
            {
                return true;
            }

            var preferences = _preferences();
            var action = _capturingAction.Value;
            var button = _capturingButton;
            FalsumInputAction conflict;
            if (preferences.HasBindingConflict(
                    action,
                    binding.Modifiers,
                    binding.Key,
                    out conflict))
            {
                _showError(
                    L.Get(
                        "falsum.preferences.keys.conflict",
                        ("action", FalsumInputRouter.ActionLabel(conflict))));
                button.AddToClassList("is-conflict");
                button.text = FalsumInputRouter.Format(binding);
                _capturingAction = null;
                _capturingButton = null;
                button.schedule.Execute(
                    (Action)delegate
                    {
                        button.text = FalsumInputRouter.Format(
                            preferences.FindBinding(action));
                        button.RemoveFromClassList("is-conflict");
                    }).ExecuteLater(900);
                return true;
            }

            preferences.SetBinding(action, binding.Modifiers, binding.Key);
            Button resetButton;
            FalsumKeyBinding defaultBinding;
            if (_resetButtons.TryGetValue(action, out resetButton) &&
                _defaults.TryGetValue(action, out defaultBinding))
            {
                resetButton.EnableInClassList(
                    "hidden",
                    FalsumInputRouter.BindingsEqual(
                        binding,
                        defaultBinding));
            }
            button.text = FalsumInputRouter.Format(binding);
            button.RemoveFromClassList("is-capturing");
            _capturingAction = null;
            _capturingButton = null;
            _clearStatus();
            return true;
        }
    }
}
