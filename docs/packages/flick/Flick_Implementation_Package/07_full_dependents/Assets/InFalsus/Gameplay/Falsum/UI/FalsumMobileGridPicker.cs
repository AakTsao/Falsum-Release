using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.UI
{
    /// <summary>A captured hold-and-slide gesture; only release commits a subdivision.</summary>
    internal sealed class FalsumMobileGridPicker : IDisposable
    {
        private static readonly int[] Values = { 2, 3, 4, 6, 8 };
        private readonly VisualElement _root;
        private readonly VisualElement _layer;
        private readonly Button _button;
        private readonly Clickable _originalClickable;
        private readonly Action _toggle;
        private readonly Action<int> _select;
        private readonly Func<Rect> _safeBounds;
        private readonly List<Label> _options = new List<Label>();
        private readonly Label _badge;
        private VisualElement _popup;
        private IVisualElementScheduledItem _hold;
        private int _pointer = -1;
        private int _subdivision = 4;
        private Vector2 _pressPosition;
        private Vector2 _position;
        private bool _moved;

        public FalsumMobileGridPicker(VisualElement root, VisualElement layer, Button button,
            Action toggle, Action<int> select, Func<Rect> safeBounds)
        {
            _root = root;
            _layer = layer ?? root;
            _button = button;
            _toggle = toggle;
            _select = select;
            _safeBounds = safeBounds;
            // Own the entire press, so a long press never also invokes Clickable.
            _originalClickable = button.clickable;
            button.clickable = null;
            _badge = new Label { pickingMode = PickingMode.Ignore };
            _badge.AddToClassList("mobile-grid-value");
            button.Add(_badge);
            button.RegisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
            button.RegisterCallback<PointerMoveEvent>(OnMove);
            button.RegisterCallback<PointerUpEvent>(OnUp);
            button.RegisterCallback<PointerCancelEvent>(OnCancel);
            button.RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
            SetSubdivision(4);
        }

        public void SetSubdivision(int value)
        {
            _subdivision = Mathf.Max(1, value);
            _badge.text = _subdivision.ToString(CultureInfo.InvariantCulture);
            RefreshText();
            for (var i = 0; i < _options.Count; i++)
                _options[i].EnableInClassList("is-selected", Values[i] == _subdivision);
        }

        public void RefreshText()
        {
            _button.tooltip = L.Get("falsum.mobile.gridHold", ("value", _subdivision));
        }

        private void OnDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || _pointer >= 0) return;
            _pointer = evt.pointerId;
            _pressPosition = _position = evt.position;
            _moved = false;
            _button.CapturePointer(_pointer);
            _button.AddToClassList("is-grid-pressed");
            _hold = _button.schedule.Execute(Open).StartingIn(350);
            evt.StopImmediatePropagation();
        }

        private void OnMove(PointerMoveEvent evt)
        {
            if (evt.pointerId != _pointer) return;
            _position = evt.position;
            if (_popup == null && Vector2.Distance(_position, _pressPosition) > 12.0f)
            {
                _moved = true;
                _hold?.Pause();
            }
            UpdateHover();
            evt.StopImmediatePropagation();
        }

        private void Open()
        {
            if (_pointer < 0 || _moved || _popup != null) return;
            _popup = new VisualElement { name = "MobileGridPicker", pickingMode = PickingMode.Ignore };
            _popup.AddToClassList("mobile-grid-picker");
            var buttonBounds = _button.worldBound;
            var safe = _safeBounds();
            const float width = 178.0f;
            const float height = 38.0f;
            var position = _layer.WorldToLocal(new Vector2(
                Mathf.Max(safe.xMin + 4.0f, buttonBounds.xMin - width - 6.0f),
                Mathf.Clamp(buttonBounds.center.y - height * 0.5f,
                    safe.yMin + 4.0f, safe.yMax - height - 4.0f)));
            _popup.style.left = position.x;
            _popup.style.top = position.y;
            for (var i = 0; i < Values.Length; i++)
            {
                var option = new Label(Values[i].ToString(CultureInfo.InvariantCulture))
                {
                    pickingMode = PickingMode.Ignore,
                };
                option.AddToClassList("mobile-grid-option");
                option.EnableInClassList("is-selected", Values[i] == _subdivision);
                _popup.Add(option);
                _options.Add(option);
            }
            _layer.Add(_popup);
            _popup.BringToFront();
            _button.AddToClassList("is-grid-open");
            // Geometry is available after layout; keep the initial hover in sync.
            _popup.schedule.Execute(UpdateHover);
        }

        private int HoveredIndex(Vector2 position)
        {
            for (var i = 0; i < _options.Count; i++)
            {
                var bounds = _options[i].worldBound;
                // A small vertical allowance accommodates fingertip occlusion.
                bounds.yMin -= 8.0f;
                bounds.yMax += 8.0f;
                if (bounds.Contains(position)) return i;
            }
            return -1;
        }

        private void UpdateHover()
        {
            var index = HoveredIndex(_position);
            for (var i = 0; i < _options.Count; i++)
                _options[i].EnableInClassList("is-hovered", i == index);
        }

        private void OnUp(PointerUpEvent evt)
        {
            if (evt.pointerId != _pointer) return;
            var wasOpen = _popup != null;
            var index = wasOpen ? HoveredIndex(evt.position) : -1;
            var tap = !wasOpen && !_moved && _button.worldBound.Contains(evt.position);
            Cancel();
            if (index >= 0) _select(Values[index]);
            else if (tap) _toggle();
            evt.StopImmediatePropagation();
        }

        private void OnCancel(PointerCancelEvent evt)
        {
            if (evt.pointerId == _pointer) Cancel();
        }

        private void OnCaptureOut(PointerCaptureOutEvent evt)
        {
            if (evt.pointerId == _pointer) Cancel();
        }

        private void Cancel()
        {
            _hold?.Pause();
            _hold = null;
            var pointer = _pointer;
            _pointer = -1;
            if (pointer >= 0 && _button.HasPointerCapture(pointer)) _button.ReleasePointer(pointer);
            _popup?.RemoveFromHierarchy();
            _popup = null;
            _options.Clear();
            _button.RemoveFromClassList("is-grid-pressed");
            _button.RemoveFromClassList("is-grid-open");
        }

        public void Dispose()
        {
            Cancel();
            _badge.RemoveFromHierarchy();
            _button.UnregisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
            _button.UnregisterCallback<PointerMoveEvent>(OnMove);
            _button.UnregisterCallback<PointerUpEvent>(OnUp);
            _button.UnregisterCallback<PointerCancelEvent>(OnCancel);
            _button.UnregisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
            _button.clickable = _originalClickable;
        }
    }
}
