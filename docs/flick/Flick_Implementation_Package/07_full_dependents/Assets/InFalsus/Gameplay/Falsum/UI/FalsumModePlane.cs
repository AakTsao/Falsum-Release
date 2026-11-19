using UnityEngine;
using UnityEngine.UI;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.UI
{
    public enum FalsumModePlaneKind
    {
        None,
        SegmentSelect,
        Flick,
        SkyArea,
        /// <summary>
        /// Dragging a note anchor (boundary / start-end / position). It drives
        /// the same bodies as the middle-button edit, but it is NOT the middle
        /// button: nothing it renders may name a mouse button, so it owns its
        /// own caption keys instead of borrowing the middle-edit ones.
        /// </summary>
        AnchorEdit,
        MiddleEdit,
    }

    public sealed class FalsumModePlane
    {
        private const float PlaneCanvasWidth = 1620.0f;

        private static readonly Color SelectBackground =
            new Color(0.31f, 0.56f, 0.85f, 0.10f);
        private static readonly Color FlickBackground =
            new Color(0.31f, 0.56f, 0.85f, 0.08f);
        private static readonly Color SkyBackground =
            new Color(0.64f, 0.48f, 0.72f, 0.09f);

        private readonly Transform _parent;
        private Canvas _canvas;
        private RectTransform _canvasRect;
        private RectTransform _root;
        private RectTransform _faceRect;
        private Image[] _borderParts;
        private RectTransform _content;
        private Text _prompt;
        private Text _modeLabel;
        private Text _keyHint;
        private FalsumModePlaneKind _kind;
        private int _gridDivisions = -1;
        private int _focusLine = -1;
        private FalsumWorldTextOverlay _overlay;
        private bool _overlayActive;

        public FalsumModePlane(Transform parent)
        {
            _parent = parent;
        }

        /// <summary>
        /// Routes the plane's captions through screen-space text so they stay
        /// readable at any distance. The plane body stays in world space.
        /// </summary>
        public void AttachOverlay(Canvas canvas)
        {
            if (_root == null || canvas == null)
            {
                return;
            }
            _overlay = new FalsumWorldTextOverlay(
                canvas,
                "FalsumModePlaneText",
                FalsumFonts.Cjk);
            _overlayActive = _overlay.Available;
            if (!_overlayActive)
            {
                return;
            }
            // Keep the world-space captions as layout/driver only; the visible
            // text is drawn by the overlay.
            SetWorldTextEnabled(_prompt, false);
            SetWorldTextEnabled(_modeLabel, false);
            SetWorldTextEnabled(_keyHint, false);
        }

        private static void SetWorldTextEnabled(Text text, bool enabled)
        {
            if (text != null)
            {
                text.enabled = enabled;
            }
        }

        public bool Visible
        {
            get
            {
                return _root != null && _root.gameObject.activeSelf;
            }
        }

        public string DebugSummary
        {
            get
            {
                if (!Visible)
                {
                    return "hidden";
                }
                return _kind + " grid=" + _gridDivisions +
                       " focusLine=" + _focusLine +
                       " label=" +
                       (_modeLabel != null
                           ? _modeLabel.text + "@" +
                             _modeLabel.rectTransform.anchoredPosition +
                             "/" + _modeLabel.gameObject.activeSelf
                           : "none") +
                       " canvas=" + CanvasDebugSummary();
            }
        }

        public void Build()
        {
            if (_root != null || _parent == null)
            {
                return;
            }

            var canvasObject = new GameObject(
                "FalsumWorldModePlane",
                typeof(RectTransform),
                typeof(Canvas));
            canvasObject.transform.SetParent(_parent, false);
            _canvasRect = canvasObject.GetComponent<RectTransform>();
            _canvasRect.sizeDelta = new Vector2(PlaneCanvasWidth, 620.0f);
            _canvasRect.localScale = new Vector3(
                0.001f,
                0.001f,
                0.001f);
            _canvasRect.localRotation = Quaternion.identity;
            _canvas = canvasObject.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.worldCamera = Camera.main;
            _canvas.sortingOrder = 50;

            _root = FalsumUiFactory.CreateRect(
                "FalsumModePlane",
                _canvas.transform,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero);

            _faceRect = FalsumUiFactory.CreateRect(
                "Face",
                _root,
                Vector2.zero,
                Vector2.one,
                new Vector2(2.0f, 2.0f),
                new Vector2(-2.0f, -2.0f));
            FalsumUiFactory.AddImage(
                _faceRect.gameObject,
                SelectBackground).raycastTarget = false;

            _borderParts = new Image[4];
            var borderThickness = 5.0f;
            _borderParts[0] = CreateBorderPart(
                "BorderTop",
                _root,
                new Vector2(0.0f, 1.0f),
                new Vector2(1.0f, 1.0f),
                new Vector2(0.0f, -borderThickness),
                Vector2.zero);
            _borderParts[1] = CreateBorderPart(
                "BorderBottom",
                _root,
                Vector2.zero,
                new Vector2(1.0f, 0.0f),
                Vector2.zero,
                new Vector2(0.0f, borderThickness));
            _borderParts[2] = CreateBorderPart(
                "BorderLeft",
                _root,
                Vector2.zero,
                new Vector2(0.0f, 1.0f),
                Vector2.zero,
                new Vector2(borderThickness, 0.0f));
            _borderParts[3] = CreateBorderPart(
                "BorderRight",
                _root,
                new Vector2(1.0f, 0.0f),
                Vector2.one,
                new Vector2(-borderThickness, 0.0f),
                Vector2.zero);

            _content = FalsumUiFactory.CreateRect(
                "Content",
                _root,
                Vector2.zero,
                Vector2.one,
                new Vector2(54.0f, 112.0f),
                new Vector2(-54.0f, -102.0f));

            _prompt = FalsumUiFactory.CreateText(
                "Prompt",
                _content,
                string.Empty,
                64,
                TextAnchor.MiddleLeft,
                Color.white,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero);

            _modeLabel = FalsumUiFactory.CreateTextAnchored(
                "ModeLabel",
                _root,
                string.Empty,
                60,
                TextAnchor.MiddleLeft,
                Color.white,
                new Vector2(0.0f, 0.0f),
                new Vector2(0.0f, 0.0f),
                new Vector2(54.0f, 22.0f),
                new Vector2(520.0f, 54.0f),
                true);

            _keyHint = FalsumUiFactory.CreateTextAnchored(
                "KeyHint",
                _root,
                L.Get("falsum.modePlane.gridAdjustHint"),
                24,
                TextAnchor.MiddleLeft,
                new Color(0.67f, 0.71f, 0.73f, 1.0f),
                new Vector2(0.0f, 0.0f),
                new Vector2(0.0f, 0.0f),
                new Vector2(610.0f, 32.0f),
                new Vector2(500.0f, 36.0f));

            _prompt.verticalOverflow = VerticalWrapMode.Overflow;
            _modeLabel.verticalOverflow = VerticalWrapMode.Overflow;
            _keyHint.verticalOverflow = VerticalWrapMode.Overflow;
            FalsumUiFactory.SetRaycastTarget(_root.gameObject, false);
            _root.gameObject.SetActive(false);
        }

        public void SetState(
            FalsumModePlaneKind kind,
            string prompt,
            int gridDivisions,
            int focusLine,
            string modeTextOverride = null,
            string keyTextOverride = null)
        {
            if (_root == null)
            {
                return;
            }
            if (kind == FalsumModePlaneKind.None)
            {
                Hide();
                return;
            }

            _root.gameObject.SetActive(true);
            _kind = kind;

            Color background;
            Color accent;
            string modeText;
            string keyText;
            var showGrid = kind == FalsumModePlaneKind.Flick ||
                           kind == FalsumModePlaneKind.SkyArea ||
                           kind == FalsumModePlaneKind.AnchorEdit ||
                           kind == FalsumModePlaneKind.MiddleEdit;
            switch (kind)
            {
                case FalsumModePlaneKind.SegmentSelect:
                    background = SelectBackground;
                    accent = new Color(0.31f, 0.56f, 0.85f, 0.98f);
                    modeText = L.Get("falsum.modePlane.segmentSelect");
                    keyText = L.Get("falsum.modePlane.segmentSelectHint");
                    break;
                case FalsumModePlaneKind.Flick:
                    background = FlickBackground;
                    accent = new Color(0.44f, 0.68f, 0.93f, 0.98f);
                    modeText = L.Get("falsum.modePlane.flick");
                    keyText = L.Get("falsum.modePlane.gridAdjustHint");
                    break;
                case FalsumModePlaneKind.SkyArea:
                    background = SkyBackground;
                    accent = new Color(0.64f, 0.48f, 0.72f, 0.98f);
                    modeText = L.Get("falsum.modePlane.skyArea");
                    keyText = L.Get("falsum.modePlane.gridAdjustHint");
                    break;
                case FalsumModePlaneKind.AnchorEdit:
                    background = FlickBackground;
                    accent = new Color(0.79f, 0.42f, 0.40f, 0.98f);
                    // Anchor editing is not a mouse button, so this branch must
                    // never name one. The grid hint it shares with Flick /
                    // SkyArea is already button-neutral.
                    modeText = L.Get("falsum.modePlane.anchorEdit");
                    keyText = L.Get("falsum.modePlane.gridAdjustHint");
                    break;
                case FalsumModePlaneKind.MiddleEdit:
                    background = FlickBackground;
                    accent = new Color(0.79f, 0.42f, 0.40f, 0.98f);
                    modeText = L.Get("falsum.modePlane.middleEdit");
                    keyText = L.Get("falsum.modePlane.gridAdjustHint");
                    break;
                default:
                    Hide();
                    return;
            }

            var faceImage = _faceRect != null
                ? _faceRect.GetComponent<Image>()
                : null;
            if (faceImage != null)
            {
                faceImage.color = background;
            }
            if (_faceRect != null)
            {
                _faceRect.offsetMin = new Vector2(2.0f, 2.0f);
                _faceRect.offsetMax = new Vector2(-2.0f, -2.0f);
            }
            if (_borderParts != null)
            {
                for (var i = 0; i < _borderParts.Length; i++)
                {
                    if (_borderParts[i] != null)
                    {
                        _borderParts[i].color = accent;
                    }
                }
            }
            _prompt.text = prompt ?? string.Empty;
            _modeLabel.text = string.IsNullOrEmpty(modeTextOverride)
                ? modeText
                : modeTextOverride;
            _keyHint.text = string.IsNullOrEmpty(keyTextOverride)
                ? keyText
                : keyTextOverride;
            _keyHint.gameObject.SetActive(showGrid);

            if (showGrid)
            {
                var divisions = Mathf.Max(1, gridDivisions);
                _gridDivisions = divisions;
                _focusLine = Mathf.Clamp(
                    focusLine,
                    -1,
                    divisions);
                _content.offsetMax = new Vector2(-54.0f, -102.0f);
                _prompt.alignment = TextAnchor.MiddleLeft;
                _prompt.fontSize = divisions >= 36 ? 58 : 64;
            }
            else
            {
                _content.offsetMax = new Vector2(-54.0f, -14.0f);
                _prompt.alignment = TextAnchor.MiddleCenter;
                _prompt.fontSize = 70;
                _gridDivisions = -1;
                _focusLine = -1;
            }
        }

        public void SetWorldPose(
            Vector3 localPosition,
            float worldHeight)
        {
            if (_canvasRect == null)
            {
                return;
            }
            _canvasRect.localPosition = localPosition;
            _canvasRect.localRotation = Quaternion.identity;
            _canvasRect.localScale = new Vector3(
                0.001f,
                0.001f,
                0.001f);
            _canvasRect.sizeDelta = new Vector2(
                PlaneCanvasWidth,
                Mathf.Max(360.0f, worldHeight * 1000.0f));
            if (_canvas != null)
            {
                _canvas.worldCamera = Camera.main;
            }
            UpdateOverlayText();
        }

        /// <summary>
        /// Mirrors the world-space captions onto the screen-space overlay.
        /// Sizes come from the plane's on-screen height, clamped so the text
        /// never becomes unreadable far away.
        /// </summary>
        private void UpdateOverlayText()
        {
            if (!_overlayActive || _overlay == null)
            {
                return;
            }
            _overlay.Clear();
            if (_kind == FalsumModePlaneKind.None)
            {
                return;
            }
            var camera = Camera.main;
            if (camera == null || _canvasRect == null)
            {
                return;
            }
            var corners = new Vector3[4];
            _canvasRect.GetWorldCorners(corners);
            var bottomLeft = camera.WorldToScreenPoint(corners[0]);
            var topRight = camera.WorldToScreenPoint(corners[2]);
            if (bottomLeft.z <= 0.0f || topRight.z <= 0.0f)
            {
                return;
            }
            var planePixels = Mathf.Abs(
                topRight.y - bottomLeft.y);
            // Pixels per plane canvas unit: the flat captions keep the same
            // relative size they had as world text.
            var planeHeight = Mathf.Max(
                360.0f,
                _canvasRect.sizeDelta.y);
            var scale = planePixels / planeHeight;
            var showGrid = _gridDivisions > 0;
            PlaceWorldText(_modeLabel, Color.white, scale);
            if (showGrid)
            {
                PlaceWorldText(
                    _keyHint,
                    new Color(0.67f, 0.71f, 0.73f, 1.0f),
                    scale);
            }
            PlaceWorldText(_prompt, Color.white, scale);
        }

        /// <summary>
        /// Copies one world-space caption onto the overlay using that caption's
        /// own rect as both anchor and box, so the flat text lands exactly
        /// where the world text used to be and keeps its relative size.
        /// </summary>
        private void PlaceWorldText(
            Text source,
            Color color,
            float scale)
        {
            if (source == null ||
                string.IsNullOrEmpty(source.text) ||
                !source.gameObject.activeInHierarchy)
            {
                return;
            }
            var camera = Camera.main;
            if (camera == null)
            {
                return;
            }
            var rect = source.rectTransform;
            var worldCenter = rect.TransformPoint(rect.rect.center);
            Vector2 canvasPoint;
            if (!_overlay.TryProject(worldCenter, out canvasPoint))
            {
                return;
            }
            var rectWorld = rect.rect;
            var worldA = rect.TransformPoint(
                new Vector3(rectWorld.xMin, rectWorld.yMin, 0.0f));
            var worldB = rect.TransformPoint(
                new Vector3(rectWorld.xMax, rectWorld.yMax, 0.0f));
            var screenA = camera.WorldToScreenPoint(worldA);
            var screenB = camera.WorldToScreenPoint(worldB);
            if (screenA.z <= 0.0f || screenB.z <= 0.0f)
            {
                return;
            }
            // Screen-space box matches the world rect's projected size, so the
            // text keeps its original place and alignment.
            var pixelWidth = Mathf.Abs(screenB.x - screenA.x);
            var pixelHeight = Mathf.Abs(screenB.y - screenA.y);
            // The anchor is the projected rect centre, so the pivot must also
            // be the centre; alignment inside the box reproduces the original
            // left/right/centre placement.
            var pivot = new Vector2(0.5f, 0.5f);
            // Both the source font size and the box are in plane-canvas units;
            // multiplying by the plane's pixels-per-unit yields screen pixels,
            // so the caption keeps its original size.
            var fontSize = Mathf.RoundToInt(
                Mathf.Max(1.0f, source.fontSize * scale));
            _overlay.Place(
                canvasPoint,
                new Vector2(pixelWidth, pixelHeight),
                pivot,
                source.text,
                fontSize,
                color,
                source.alignment);
            _lastSourceScreenCenter = camera.WorldToScreenPoint(
                worldCenter);
        }

        private Vector2 _lastSourceScreenCenter;

        /// <summary>
        /// Offset in screen pixels between the flat caption and the world
        /// caption it replaces; near zero means the position is preserved.
        /// </summary>
        public string DebugOverlayAlignment
        {
            get
            {
                if (!_overlayActive)
                {
                    return "no-overlay";
                }
                if (_overlay.ActiveCount == 0)
                {
                    return "no-labels";
                }
                var delta = _overlay.LastScreenCenter -
                            _lastSourceScreenCenter;
                return "offset=" + delta.ToString("F1") +
                       " labels=" + _overlay.ActiveCount;
            }
        }

        public void Hide()
        {
            if (_root != null)
            {
                _root.gameObject.SetActive(false);
            }
            if (_overlay != null)
            {
                _overlay.Clear();
            }
            _kind = FalsumModePlaneKind.None;
            // Clear the readout state too: DebugSummary must not report values
            // from a mode that is no longer on screen.
            _gridDivisions = -1;
            _focusLine = -1;
        }

        private static Image CreateBorderPart(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            var rect = FalsumUiFactory.CreateRect(
                name,
                parent,
                anchorMin,
                anchorMax,
                offsetMin,
                offsetMax);
            var image = FalsumUiFactory.AddImage(
                rect.gameObject,
                Color.white);
            image.raycastTarget = false;
            return image;
        }

        private string CanvasDebugSummary()
        {
            if (_canvasRect == null)
            {
                return "none";
            }
            var corners = new Vector3[4];
            _canvasRect.GetWorldCorners(corners);
            var camera = Camera.main;
            if (camera == null)
            {
                return "noCamera pos=" + _canvasRect.localPosition;
            }
            var first = camera.WorldToScreenPoint(corners[0]);
            var last = camera.WorldToScreenPoint(corners[2]);
            return _canvasRect.localPosition.ToString("F2") +
                   " scale=" + _canvasRect.localScale.ToString("F3") +
                   " screen=" + first.ToString("F0") +
                   ".." + last.ToString("F0");
        }
    }
}
