using UnityEngine;
using UnityEngine.UI;

namespace InFalsus.Gameplay.Falsum.UI
{
    public static class FalsumUiFactory
    {
        private static Sprite _whiteSprite;
        private static Font _displayFont;

        public static Sprite WhiteSprite
        {
            get
            {
                if (_whiteSprite == null)
                {
                    var texture = new Texture2D(
                        1,
                        1,
                        TextureFormat.RGBA32,
                        false);
                    texture.name = "Falsum_UI_White";
                    texture.SetPixel(0, 0, Color.white);
                    texture.Apply();
                    _whiteSprite = Sprite.Create(
                        texture,
                        new Rect(0.0f, 0.0f, 1.0f, 1.0f),
                        new Vector2(0.5f, 0.5f),
                        1.0f);
                }
                return _whiteSprite;
            }
        }

        public static Font UiFont
        {
            get
            {
                return FalsumFonts.Cjk;
            }
        }

        public static Font DisplayFont
        {
            get
            {
                if (_displayFont == null)
                {
                    _displayFont = Resources.Load<Font>(
                        "Falsum/Exo-Medium");
                }
                return _displayFont != null
                    ? _displayFont
                    : UiFont;
            }
        }

        public static RectTransform CreateRect(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            rect.localScale = Vector3.one;
            return rect;
        }

        public static Image AddImage(
            GameObject target,
            Color color,
            Sprite sprite = null)
        {
            var image = target.AddComponent<Image>();
            image.color = color;
            image.sprite = sprite != null ? sprite : WhiteSprite;
            image.type = Image.Type.Simple;
            return image;
        }

        public static Text CreateText(
            string name,
            Transform parent,
            string value,
            int fontSize,
            TextAnchor alignment,
            Color color,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax,
            bool displayFont = false)
        {
            var rect = CreateRect(
                name,
                parent,
                anchorMin,
                anchorMax,
                offsetMin,
                offsetMax);
            var text = rect.gameObject.AddComponent<Text>();
            ConfigureText(
                text,
                value,
                fontSize,
                alignment,
                color,
                displayFont);
            return text;
        }

        public static Text CreateTextAnchored(
            string name,
            Transform parent,
            string value,
            int fontSize,
            TextAnchor alignment,
            Color color,
            Vector2 anchor,
            Vector2 pivot,
            Vector2 position,
            Vector2 size,
            bool displayFont = false)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
            var text = go.AddComponent<Text>();
            ConfigureText(
                text,
                value,
                fontSize,
                alignment,
                color,
                displayFont);
            return text;
        }

        public static void SetRaycastTarget(
            GameObject target,
            bool enabled)
        {
            if (target == null)
            {
                return;
            }
            var graphics = target.GetComponentsInChildren<Graphic>(true);
            for (var i = 0; i < graphics.Length; i++)
            {
                graphics[i].raycastTarget = enabled;
            }
        }

        public static void ConfigureText(
            Text text,
            string value,
            int fontSize,
            TextAnchor alignment,
            Color color,
            bool displayFont = false)
        {
            if (text == null)
            {
                return;
            }
            text.font = displayFont ? DisplayFont : UiFont;
            text.text = value;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            text.supportRichText = false;
            text.alignByGeometry = true;
            text.lineSpacing = 1.0f;
            text.resizeTextForBestFit = false;
        }
    }
}
