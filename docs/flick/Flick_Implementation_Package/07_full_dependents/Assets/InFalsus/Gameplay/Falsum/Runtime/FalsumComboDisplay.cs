using InFalsus.Gameplay.Core.Charts;
using InFalsus.Gameplay.Falsum.Core;
using InFalsus.Gameplay.Runtime.SceneBinding;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    public sealed class FalsumComboDisplay
    {
        private const string FontResource = "Falsum/Exo-SemiBold";
        private const string TextShaderResource =
            "Falsum/Shaders/FalsumComboText";
        private const string BackingShaderResource =
            "Falsum/Shaders/FalsumComboBacking";
        private const int BackingRenderQueue = 3002;
        private const int TextRenderQueue = 3009;
        private const float TextCharacterSize = 0.03f;
        private const float TextFillAlpha = 0.10f;
        private const float BackingAlpha = 0.30f;
        private const float BackingEdgeFade = 0.18f;
        private const float OutlineWidth = 0.45f;

        private Transform _root;
        private Transform _count;
        private Transform _backing;
        private MeshRenderer _backingRenderer;
        private Material _backingMaterial;
        private TextMesh _text;
        private GameObject _textObject;
        private Material _textMaterial;
        private Font _font;
        private bool _chartLoaded;
        private int _displayedCombo = -1;

        public int CurrentCombo { get; private set; }

        public bool NumberVisible
        {
            get
            {
                return _count != null &&
                       _count.gameObject.activeSelf;
            }
        }

        public bool IsReady
        {
            get { return _text != null; }
        }

        public FalsumComboDisplay(GameplaySceneBinding binding)
        {
            if (binding != null)
            {
                _root = binding.ComboRoot;
                _count = binding.ComboCount;
                _backing = binding.ComboBacking;
                if (_backing != null)
                {
                    _backingRenderer =
                        _backing.GetComponent<MeshRenderer>();
                }
            }
        }

        public void Initialize()
        {
            if (_root == null || _count == null)
            {
                Debug.LogWarning(
                    "[Falsum] Combo display nodes are missing: " +
                    GameplaySceneBinding.ComboPath + " / " +
                    GameplaySceneBinding.ComboCountPath);
                return;
            }

            _font = Resources.Load<Font>(FontResource);
            if (_font == null)
            {
                Debug.LogWarning(
                    "[Falsum] Combo font is missing: " +
                    FontResource);
                return;
            }

            _font.RequestCharactersInTexture(
                "0123456789",
                128,
                FontStyle.Normal);
            _textMaterial = CreateFontMaterial(
                _font,
                "FalsumComboText");
            if (_textMaterial == null)
            {
                Debug.LogWarning(
                    "[Falsum] Combo font material is unavailable.");
                return;
            }

            _textObject = new GameObject("FalsumComboText");
            _textObject.transform.SetParent(_count, false);
            _textObject.transform.localPosition = Vector3.zero;
            _textObject.transform.localRotation = Quaternion.identity;
            _textObject.transform.localScale = Vector3.one;
            _text = CreateTextMesh(
                _textObject,
                _textMaterial,
                new Color(
                    0.12941177f,
                    0.37254903f,
                    0.6039216f,
                    TextFillAlpha));
            ConfigureBackingMaterial();
            _count.gameObject.SetActive(false);
            _root.gameObject.SetActive(false);
        }

        public void Update(double chartTimeMs, Chart chart)
        {
            var hasChart = chart != null && chart.Notes.Count > 0;
            SetChartLoaded(hasChart);
            SetCombo(FalsumAutoCombo.CountAt(chart, chartTimeMs));
        }

        public void SetChartLoaded(bool loaded)
        {
            _chartLoaded = loaded;
            if (_root == null)
            {
                return;
            }

            if (_root.gameObject.activeSelf != loaded)
            {
                _root.gameObject.SetActive(loaded);
            }
            if (_backing != null &&
                _backing.gameObject.activeSelf != loaded)
            {
                _backing.gameObject.SetActive(loaded);
            }
            if (!loaded)
            {
                SetCombo(0);
            }
        }

        public void SetCombo(int combo)
        {
            CurrentCombo = Mathf.Max(0, combo);
            var visible = _chartLoaded &&
                          CurrentCombo >= 2 &&
                          _text != null;
            if (_count != null)
            {
                _count.gameObject.SetActive(visible);
            }
            if (!visible || _text == null)
            {
                if (_text != null)
                {
                    _text.text = string.Empty;
                }
                _displayedCombo = -1;
                return;
            }

            if (_displayedCombo == CurrentCombo)
            {
                return;
            }
            var text = CurrentCombo.ToString();
            _text.text = text;
            _displayedCombo = CurrentCombo;
        }

        public void Dispose()
        {
            if (_backingMaterial != null)
            {
                Object.Destroy(_backingMaterial);
                _backingMaterial = null;
            }
            if (_textMaterial != null)
            {
                Object.Destroy(_textMaterial);
                _textMaterial = null;
            }
            if (_textObject != null)
            {
                Object.Destroy(_textObject);
                _textObject = null;
            }
        }

        private TextMesh CreateTextMesh(
            GameObject host,
            Material material,
            Color color)
        {
            var text = host.GetComponent<TextMesh>();
            if (text == null)
            {
                text = host.AddComponent<TextMesh>();
            }
            text.font = _font;
            text.fontSize = 128;
            text.characterSize = TextCharacterSize;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = color;
            text.richText = false;
            text.text = string.Empty;

            var renderer = host.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                renderer = host.AddComponent<MeshRenderer>();
            }
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return text;
        }

        private void ConfigureBackingMaterial()
        {
            if (_backingRenderer == null)
            {
                return;
            }
            var source = _backingRenderer.sharedMaterial;
            var shader = Resources.Load<Shader>(
                BackingShaderResource);
            if (source == null ||
                shader == null ||
                source.GetTexture("_MainTex") == null)
            {
                Debug.LogWarning(
                    "[Falsum] Combo backing shader is unavailable.");
                return;
            }

            var color = source.HasProperty("_Color")
                ? source.GetColor("_Color")
                : Color.white;
            color.a *= BackingAlpha;
            _backingMaterial = new Material(shader)
            {
                name = "FalsumComboBacking",
                renderQueue = BackingRenderQueue,
            };
            _backingMaterial.SetTexture(
                "_MainTex",
                source.GetTexture("_MainTex"));
            _backingMaterial.SetColor("_Color", color);
            _backingMaterial.SetFloat(
                "_EdgeFade",
                BackingEdgeFade);
            _backingRenderer.sharedMaterial =
                _backingMaterial;
            _backingRenderer.shadowCastingMode =
                ShadowCastingMode.Off;
            _backingRenderer.receiveShadows = false;
        }

        private static Material CreateFontMaterial(
            Font font,
            string name)
        {
            if (font == null || font.material == null)
            {
                return null;
            }
            var shader = Resources.Load<Shader>(
                TextShaderResource);
            if (shader == null || font.material.mainTexture == null)
            {
                return null;
            }
            var material = new Material(shader)
            {
                name = name,
                renderQueue = TextRenderQueue,
            };
            material.SetTexture(
                "_MainTex",
                font.material.mainTexture);
            material.SetColor(
                "_OutlineColor",
                new Color(
                    0.78039217f,
                    0.91764706f,
                    0.9137255f,
                    1.0f));
            material.SetFloat("_OutlineWidth", OutlineWidth);
            return material;
        }
    }
}
