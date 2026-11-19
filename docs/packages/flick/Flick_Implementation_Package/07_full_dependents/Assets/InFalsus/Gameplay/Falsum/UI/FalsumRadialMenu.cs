using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.UI
{
    public enum FalsumRadialAction
    {
        None,
        SegmentSelect,
        PointPlace,
        PointPlaceNone,
        PlaceTap,
        PlaceHold,
        PlaceFlick,
        PlaceSkyArea,
        GridToggle,
        Paste,
        Copy,
        Cut,
        Mirror,
        MirrorFrontBack,
        ExitPlacement,
        Close,
    }

    public sealed class FalsumRadialMenu
    {
        private sealed class Entry
        {
            public FalsumRadialAction Action;
            public Button Root;
        }

        private readonly VisualElement _layer;
        private readonly VisualElement _root;
        private readonly VisualElement _frame;
        private readonly VisualElement _center;
        private readonly Label _centerLabel;
        private readonly VisualElement _entriesRoot;
        private readonly VisualTreeAsset _entryTemplate;
        private readonly List<Entry> _entries = new List<Entry>();
        private Action<FalsumRadialAction> _callback;
        private Vector2 _centerPosition;
        private float _radius;
        private bool _open;

        public bool IsOpen
        {
            get { return _open; }
        }

        public bool HasEntry(FalsumRadialAction action)
        {
            for (var i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Action == action)
                {
                    return true;
                }
            }
            return false;
        }

        public string DebugEntryLabel(FalsumRadialAction action)
        {
            for (var i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Action == action)
                {
                    return _entries[i].Root.text;
                }
            }
            return string.Empty;
        }

        public Vector2 DebugCenterPanelPosition
        {
            get
            {
                return _center != null
                    ? _center.worldBound.center
                    : Vector2.zero;
            }
        }

        public FalsumRadialMenu(VisualElement layer)
        {
            _layer = layer;
            if (_layer == null)
            {
                throw new ArgumentNullException("layer");
            }

            var menuTemplate = Resources.Load<VisualTreeAsset>(
                "Falsum/UI/FalsumRadialMenu");
            _entryTemplate = Resources.Load<VisualTreeAsset>(
                "Falsum/UI/FalsumRadialEntry");
            if (menuTemplate == null || _entryTemplate == null)
            {
                throw new InvalidOperationException(
                    L.Get("falsum.error.ui.radialMenuResourcesMissing"));
            }

            var instance = menuTemplate.Instantiate();
            _root = Require<VisualElement>(instance, "RadialMenu");
            _frame = Require<VisualElement>(instance, "RadialFrame");
            _center = Require<VisualElement>(instance, "RadialCenter");
            _centerLabel =
                Require<Label>(instance, "RadialCenterLabel");
            _entriesRoot =
                Require<VisualElement>(instance, "RadialEntries");
            _layer.Add(_root);
            Hide();
        }

        public void ShowRoot(
            Vector2 screenPosition,
            bool hasClipboard,
            bool gridEnabled,
            Action<FalsumRadialAction> callback)
        {
            var entries = new List<Tuple<FalsumRadialAction, string>>
            {
                Tuple.Create(
                    FalsumRadialAction.SegmentSelect,
                    L.Get("falsum.radial.segmentSelect")),
                Tuple.Create(
                    FalsumRadialAction.PointPlace,
                    L.Get("falsum.radial.pointPlace")),
                Tuple.Create(
                    FalsumRadialAction.GridToggle,
                    L.Get(
                        gridEnabled
                            ? "falsum.radial.gridOn"
                            : "falsum.radial.gridOff")),
            };
            if (hasClipboard)
            {
                entries.Add(Tuple.Create(
                    FalsumRadialAction.Paste,
                    L.Get("falsum.action.paste")));
            }
            Show(
                screenPosition,
                L.Get("falsum.brand.short"),
                entries,
                callback);
        }

        public void ShowPlacement(
            Vector2 screenPosition,
            bool gridEnabled,
            Action<FalsumRadialAction> callback)
        {
            Show(
                screenPosition,
                L.Get("falsum.radial.pointPlace"),
                new List<Tuple<FalsumRadialAction, string>>
                {
                    Tuple.Create(
                        FalsumRadialAction.PointPlaceNone,
                        L.Get("falsum.radial.placeNone")),
                    Tuple.Create(
                        FalsumRadialAction.PlaceTap,
                        L.Get("falsum.noteKind.tap")),
                    Tuple.Create(
                        FalsumRadialAction.PlaceHold,
                        L.Get("falsum.noteKind.hold")),
                    Tuple.Create(
                        FalsumRadialAction.PlaceFlick,
                        L.Get("falsum.noteKind.flick")),
                    Tuple.Create(
                        FalsumRadialAction.PlaceSkyArea,
                        L.Get("falsum.noteKind.skyArea")),
                    Tuple.Create(
                        FalsumRadialAction.GridToggle,
                        L.Get(
                            gridEnabled
                                ? "falsum.radial.gridOn"
                                : "falsum.radial.gridOff")),
                    Tuple.Create(
                        FalsumRadialAction.ExitPlacement,
                        L.Get("falsum.action.exit")),
                },
                callback);
        }

        public void ShowSelection(
            Vector2 screenPosition,
            Action<FalsumRadialAction> callback)
        {
            Show(
                screenPosition,
                L.Get("falsum.radial.selection"),
                new List<Tuple<FalsumRadialAction, string>>
                {
                    Tuple.Create(
                        FalsumRadialAction.Copy,
                        L.Get("falsum.action.copy")),
                    Tuple.Create(
                        FalsumRadialAction.Cut,
                        L.Get("falsum.action.cut")),
                    Tuple.Create(
                        FalsumRadialAction.Mirror,
                        L.Get("falsum.action.mirror")),
                    Tuple.Create(
                        FalsumRadialAction.MirrorFrontBack,
                        L.Get("falsum.action.mirrorFrontBack")),
                },
                callback);
        }

        public void Hide()
        {
            _open = false;
            _callback = null;
            if (_root != null)
            {
                _root.AddToClassList("hidden");
            }
        }

        public void Tick()
        {
        }

        public bool SelectAt(Vector2 screenPosition)
        {
            if (!_open || _layer == null || _layer.panel == null)
            {
                return false;
            }
            var panelPoint = ScreenToPanelTopLeft(screenPosition);
            for (var i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                if (!entry.Root.worldBound.Contains(panelPoint))
                {
                    continue;
                }
                var selected = entry.Action;
                var callback = _callback;
                Hide();
                if (callback != null)
                {
                    callback(selected);
                }
                return true;
            }
            Hide();
            return false;
        }

        private void Show(
            Vector2 screenPosition,
            string centerLabel,
            IList<Tuple<FalsumRadialAction, string>> entries,
            Action<FalsumRadialAction> callback)
        {
            ClearEntries();
            _callback = callback;
            _open = true;
            _root.RemoveFromClassList("hidden");
            _centerLabel.text = centerLabel;

            var count = entries.Count;
            var preferredRadius =
                count <= 4 ? 136.0f : 150.0f;
            SetCenterPosition(
                screenPosition,
                preferredRadius);
            for (var i = 0; i < count; i++)
            {
                var angle = 90.0f -
                            i * (360.0f / Mathf.Max(1, count));
                var radians = angle * Mathf.Deg2Rad;
                var position = new Vector2(
                    Mathf.Cos(radians) * _radius,
                    -Mathf.Sin(radians) * _radius);
                var entry = CreateEntry(
                    entries[i].Item1,
                    entries[i].Item2,
                    position);
                _entries.Add(entry);
            }
        }

        private Entry CreateEntry(
            FalsumRadialAction action,
            string label,
            Vector2 position)
        {
            var instance = _entryTemplate.Instantiate();
            var button = Require<Button>(
                instance,
                "RadialEntryButton");
            button.text = label;
            var center = GetCenterPosition();
            button.style.left =
                center.x + position.x - 62.0f;
            button.style.top =
                center.y + position.y - 23.0f;
            _entriesRoot.Add(button);
            return new Entry
            {
                Action = action,
                Root = button,
            };
        }

        private void SetCenterPosition(
            Vector2 screenPosition,
            float preferredRadius)
        {
            if (_layer.panel == null)
            {
                return;
            }
            var panelPoint = ScreenToPanelTopLeft(screenPosition);
            var local = _layer.WorldToLocal(panelPoint);
            const float Margin = 8.0f;
            var frameWidth = Mathf.Min(
                440.0f,
                Mathf.Max(260.0f, _layer.layout.width -
                                   Margin * 2.0f));
            var frameHeight = Mathf.Min(
                390.0f,
                Mathf.Max(220.0f, _layer.layout.height -
                                   Margin * 2.0f));
            var halfWidth = frameWidth * 0.5f;
            var halfHeight = frameHeight * 0.5f;
            var centerX = Mathf.Clamp(
                local.x,
                Mathf.Min(halfWidth + Margin,
                          _layer.layout.width * 0.5f),
                Mathf.Max(_layer.layout.width -
                          halfWidth - Margin,
                          _layer.layout.width * 0.5f));
            var centerY = Mathf.Clamp(
                local.y,
                Mathf.Min(halfHeight + Margin,
                          _layer.layout.height * 0.5f),
                Mathf.Max(_layer.layout.height -
                          halfHeight - Margin,
                          _layer.layout.height * 0.5f));
            _centerPosition = new Vector2(centerX, centerY);
            _radius = Mathf.Max(
                54.0f,
                Mathf.Min(
                    preferredRadius,
                    Mathf.Min(
                        Mathf.Min(centerX, _layer.layout.width - centerX),
                        Mathf.Min(centerY,
                                  _layer.layout.height - centerY)) -
                    70.0f));
            _center.style.left = centerX - 56.0f;
            _center.style.top = centerY - 22.0f;
            _frame.style.width = frameWidth;
            _frame.style.height = frameHeight;
            _frame.style.left = centerX - halfWidth;
            _frame.style.top = centerY - halfHeight;
            _root.style.left = 0;
            _root.style.top = 0;
        }

        private Vector2 GetCenterPosition()
        {
            return _centerPosition;
        }

        private Vector2 ScreenToPanelTopLeft(Vector2 screenPosition)
        {
            var raw = RuntimePanelUtils.ScreenToPanel(
                _layer.panel,
                screenPosition);
            return new Vector2(
                raw.x,
                Mathf.Max(0.0f, _layer.layout.height) - raw.y);
        }

        private void ClearEntries()
        {
            for (var i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Root != null)
                {
                    _entries[i].Root.RemoveFromHierarchy();
                }
            }
            _entries.Clear();
        }

        private static T Require<T>(
            VisualElement root,
            string name)
            where T : VisualElement
        {
            var element = root.Q<T>(name);
            if (element == null)
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.ui.missingElement",
                        ("element", name)));
            }
            return element;
        }
    }
}
