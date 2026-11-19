using System;
using UnityEngine;
using UnityEngine.UIElements;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.UI
{
    /// <summary>
    /// The "keep or revert display settings?" confirmation that appears after
    /// committing a display change. Owns its countdown so the timer cannot keep
    /// firing after the owning dialog has closed, which was the reason this
    /// state used to be spread across the preferences view.
    /// </summary>
    internal sealed class FalsumDisplayConfirmDialog
    {
        private readonly VisualElement _layer;
        private readonly Label _countdownLabel;
        private readonly VisualElement _schedulerSource;
        private IVisualElementScheduledItem _countdown;
        private Action _keepAction;
        private Action _revertAction;
        private int _secondsRemaining;
        private bool _open;

        public FalsumDisplayConfirmDialog(
            VisualElement layer,
            Label countdownLabel,
            VisualElement schedulerSource)
        {
            _layer = layer;
            _countdownLabel = countdownLabel;
            _schedulerSource = schedulerSource;
            if (_layer == null)
            {
                return;
            }
            var keep = _layer.Q<Button>("DisplayConfirmKeep");
            if (keep != null)
            {
                keep.clicked += OnKeepClicked;
            }
            var revert = _layer.Q<Button>("DisplayConfirmRevert");
            if (revert != null)
            {
                revert.clicked += OnRevertClicked;
            }
        }

        private void OnKeepClicked()
        {
            Keep();
        }

        private void OnRevertClicked()
        {
            Revert();
        }

        /// <summary>Confirms the new display settings.</summary>
        public void Keep()
        {
            var keep = _keepAction;
            Hide(false);
            if (keep != null)
            {
                keep();
            }
        }

        /// <summary>Discards the new display settings.</summary>
        public void Revert()
        {
            var revert = _revertAction;
            Hide(false);
            if (revert != null)
            {
                revert();
            }
        }

        public bool IsOpen
        {
            get { return _open; }
        }

        /// <summary>The focus-trap root, exposed so the shell can route Tab.</summary>
        public VisualElement Root
        {
            get { return _layer; }
        }

        /// <summary>
        /// Shows the confirmation. When the layer is missing (headless
        /// construction), the keep action runs immediately so callers keep
        /// their previous behaviour.
        /// </summary>
        public void Show(
            int seconds,
            Action keepAction,
            Action revertAction)
        {
            if (_layer == null)
            {
                if (keepAction != null)
                {
                    keepAction();
                }
                return;
            }
            Hide(false);
            _open = true;
            _keepAction = keepAction;
            _revertAction = revertAction;
            _secondsRemaining = Mathf.Max(1, seconds);
            FalsumPreferencesUiHelpers.SetVisible(_layer, true);
            FalsumUiMotion.AnimateWindowIn(
                _layer.Q<VisualElement>(
                    className: "display-confirm-panel"),
                0.14f,
                0.0f,
                0.97f);
            UpdateCountdown();
            var revert = _layer.Q<Button>("DisplayConfirmRevert");
            if (revert != null)
            {
                revert.Focus();
            }
            _countdown = _schedulerSource
                .schedule
                .Execute(Tick)
                .Every(1000);
        }

        public void Hide(bool invokeRevert)
        {
            if (_countdown != null)
            {
                _countdown.Pause();
                _countdown = null;
            }
            _open = false;
            FalsumPreferencesUiHelpers.SetVisible(_layer, false);
            if (invokeRevert)
            {
                var revert = _revertAction;
                if (revert != null)
                {
                    revert();
                }
            }
            _keepAction = null;
            _revertAction = null;
        }

        /// <summary>Esc when the confirmation is open reverts, matching the old behaviour.</summary>
        public void CancelByEscape()
        {
            Hide(true);
        }

        private void Tick()
        {
            if (!_open)
            {
                return;
            }
            _secondsRemaining--;
            if (_secondsRemaining <= 0)
            {
                var revert = _revertAction;
                Hide(false);
                if (revert != null)
                {
                    revert();
                }
                return;
            }
            UpdateCountdown();
        }

        private void UpdateCountdown()
        {
            if (_countdownLabel == null)
            {
                return;
            }
            _countdownLabel.text = L.Get(
                "falsum.preferences.displayConfirm.countdownValue",
                ("seconds", _secondsRemaining));
        }
    }
}
