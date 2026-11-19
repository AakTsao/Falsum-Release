using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace InFalsus.Gameplay.Falsum.UI
{
    /// <summary>
    /// Screen-space text pool for labels that belong to world-space editor
    /// geometry (measure line numbers, the mode plane captions). The owning
    /// geometry stays in the world, but the text is drawn on the overlay
    /// canvas so it keeps a constant, readable pixel size.
    /// </summary>
    public sealed class FalsumWorldTextOverlay
    {
        public sealed class Item
        {
            public RectTransform Root;
            public Image Fill;
            public Text Label;
        }

        private const int MaxItems = 64;

        private readonly Canvas _canvas;
        private readonly RectTransform _canvasRect;
        private readonly RectTransform _root;
        private readonly Font _font;
        private readonly List<Item> _items = new List<Item>(MaxItems);
        private int _used;

        public FalsumWorldTextOverlay(
            Canvas canvas,
            string rootName,
            Font font)
        {
            _canvas = canvas;
            _font = font;
            _canvasRect = canvas != null
                ? canvas.transform as RectTransform
                : null;
            if (_canvasRect == null)
            {
                return;
            }
            _root = FalsumUiFactory.CreateRect(
                rootName ?? "FalsumWorldText",
                _canvasRect,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero);
            _root.localScale = Vector3.one;
        }

        public bool Available
        {
            get
            {
                return _root != null &&
                       _canvasRect != null &&
                       _canvas != null;
            }
        }

        public int ActiveCount
        {
            get { return _used; }
        }

        /// <summary>Screen pixels per canvas unit for this overlay.</summary>
        public float CanvasScaleFactor
        {
            get
            {
                if (_canvas == null)
                {
                    return 1.0f;
                }
                var factor = _canvas.scaleFactor;
                return factor > 0.0001f ? factor : 1.0f;
            }
        }

        public void Clear()
        {
            _used = 0;
            for (var i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                if (item.Root.gameObject.activeSelf)
                {
                    item.Root.gameObject.SetActive(false);
                }
            }
        }

        public void SetVisible(bool visible)
        {
            if (_root != null &&
                _root.gameObject.activeSelf != visible)
            {
                _root.gameObject.SetActive(visible);
            }
        }

        /// <summary>
        /// Projects a world point into overlay canvas units (origin is the
        /// canvas centre, matching an anchored position).
        /// </summary>
        public bool TryProject(
            Vector3 worldPoint,
            out Vector2 canvasPoint)
        {
            canvasPoint = Vector2.zero;
            var camera = Camera.main;
            if (camera == null || _canvasRect == null)
            {
                return false;
            }
            var screen = camera.WorldToScreenPoint(worldPoint);
            if (screen.z <= 0.0f)
            {
                return false;
            }
            return RectTransformUtility
                .ScreenPointToLocalPointInRectangle(
                    _canvasRect,
                    screen,
                    null,
                    out canvasPoint);
        }

        /// <summary>
        /// Places one pooled label. <paramref name="canvasPoint"/> is the
        /// anchor position; the pivot decides which corner of the box lands
        /// on it. Sizes are given in screen pixels and converted to canvas
        /// units so callers can reason in pixel terms.
        /// </summary>
        public Item Place(
            Vector2 canvasPoint,
            Vector2 pixelSize,
            Vector2 pivot,
            string value,
            int pixelFontSize,
            Color textColor,
            TextAnchor alignment)
        {
            var item = Rent();
            var factor = CanvasScaleFactor;
            item.Root.anchorMin = new Vector2(0.5f, 0.5f);
            item.Root.anchorMax = new Vector2(0.5f, 0.5f);
            item.Root.pivot = pivot;
            item.Root.sizeDelta = new Vector2(
                Mathf.Max(1.0f, pixelSize.x) / factor,
                Mathf.Max(1.0f, pixelSize.y) / factor);
            item.Root.anchoredPosition = canvasPoint;
            item.Root.gameObject.SetActive(true);
            // The fill Image lives on the root itself, so its GameObject must
            // stay active; toggle the component instead.
            item.Fill.enabled = false;
            item.Label.text = value;
            // uGUI font sizes are canvas units, not screen pixels.
            item.Label.fontSize = Mathf.Max(
                1,
                Mathf.RoundToInt(pixelFontSize / factor));
            item.Label.color = textColor;
            item.Label.alignment = alignment;
            return item;
        }

        public void ShowFill(Item item, Color color)
        {
            if (item == null)
            {
                return;
            }
            item.Fill.enabled = true;
            item.Fill.color = color;
        }

        /// <summary>
        /// Screen centre of the most recently placed item, for layout checks.
        /// </summary>
        public Vector2 LastScreenCenter
        {
            get
            {
                if (_used == 0)
                {
                    return Vector2.zero;
                }
                var item = _items[_used - 1];
                return RectTransformUtility.WorldToScreenPoint(
                    null,
                    item.Root.TransformPoint(
                        item.Root.rect.center));
            }
        }

        private Item Rent()
        {
            if (_used < _items.Count)
            {
                return _items[_used++];
            }
            if (_items.Count >= MaxItems)
            {
                return _items[_items.Count - 1];
            }
            _items.Add(CreateItem());
            _used++;
            return _items[_used - 1];
        }

        private Item CreateItem()
        {
            var root = FalsumUiFactory.CreateRect(
                "Text_" + _items.Count,
                _root,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Vector2.zero);
            var fill = FalsumUiFactory.AddImage(
                root.gameObject,
                Color.white);
            fill.raycastTarget = false;
            var label = FalsumUiFactory.CreateText(
                "Label",
                root,
                string.Empty,
                12,
                TextAnchor.MiddleCenter,
                Color.white,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero);
            if (_font != null)
            {
                label.font = _font;
            }
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.raycastTarget = false;
            root.gameObject.SetActive(false);
            return new Item
            {
                Root = root,
                Fill = fill,
                Label = label,
            };
        }
    }
}
