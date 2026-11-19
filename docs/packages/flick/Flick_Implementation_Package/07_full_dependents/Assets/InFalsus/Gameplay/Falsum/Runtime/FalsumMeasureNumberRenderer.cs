using System.Collections.Generic;
using InFalsus.Gameplay.Falsum.UI;
using InFalsus.Gameplay.Runtime.SceneBinding;
using UnityEngine;
using UnityEngine.Rendering;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    /// <summary>
    /// Draws numbered square badges at the outer right end of every measure
    /// line. The square is a world-space quad so it follows the track's
    /// perspective, while the number is drawn on the screen-space overlay so
    /// it stays readable at every depth.
    /// </summary>
    public sealed class FalsumMeasureNumberRenderer
    {
        private const string FontResource = "Falsum/Exo-Medium";
        private const int RenderQueue = 3040;
        // The number scales with the badge's perspective footprint, keeping
        // only a small floor so far-away badges stay legible.
        private const float ReferenceBadgePixels = 22.0f;
        private const float MinimumBadgePixels = 9.0f;
        private const int ReferenceFontSize = 15;
        private const int MinimumFontSize = 6;
        private const int MaximumFontSize = 64;

        private static readonly Color QuadFill =
            new Color(0.941f, 0.812f, 0.286f, 0.96f);
        private static readonly Color GlyphTint =
            new Color(0.098f, 0.086f, 0.062f, 1.0f);

        private readonly Transform _parent;
        private readonly Transform _root;
        private readonly Transform _quadRoot;
        private readonly MeshFilter _quadFilter;
        private readonly MeshRenderer _quadRenderer;
        private readonly Mesh _quadMesh;
        private readonly FalsumWorldTextOverlay _labels;
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Color> _colors = new List<Color>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<int> _triangles = new List<int>();
        private readonly List<Vector2> _placedScreen = new List<Vector2>();
        private int _badgeCount;
        private bool _visible = true;

        public FalsumMeasureNumberRenderer(
            GameplaySceneBinding binding,
            Canvas canvas)
        {
            if (binding == null)
            {
                return;
            }
            _parent = binding.MasterScene;
            if (_parent == null)
            {
                return;
            }
            _labels = new FalsumWorldTextOverlay(
                canvas,
                "FalsumMeasureNumbers",
                Resources.Load<Font>(FontResource));
            _root = CreateRoot("FalsumMeasureNumberQuads");
            _quadRoot = _root;
            _quadMesh = new Mesh
            {
                name = "FalsumMeasureNumberQuads_Mesh",
            };
            _quadMesh.MarkDynamic();
            _quadFilter = _quadRoot.gameObject.AddComponent<MeshFilter>();
            _quadRenderer =
                _quadRoot.gameObject.AddComponent<MeshRenderer>();
            _quadFilter.sharedMesh = _quadMesh;
            _quadRenderer.sharedMaterial = CreateQuadMaterial();
            _quadRenderer.shadowCastingMode =
                ShadowCastingMode.Off;
            _quadRenderer.receiveShadows = false;
        }

        public bool Available
        {
            get { return _quadRenderer != null; }
        }

        public int VisibleBadgeCount
        {
            get { return _badgeCount; }
        }

        public string DebugSummary
        {
            get
            {
                return "badges=" + _badgeCount +
                       "/" + FalsumMeasureNumberLayout.MaxBadges +
                       " placed=" + _placedScreen.Count +
                       " labels=" +
                       (_labels != null
                           ? _labels.ActiveCount.ToString()
                           : "none");
            }
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (_root != null &&
                _root.gameObject.activeSelf != visible)
            {
                _root.gameObject.SetActive(visible);
            }
            if (_labels != null)
            {
                _labels.SetVisible(visible);
            }
        }

        public void Clear()
        {
            _badgeCount = 0;
            _placedScreen.Clear();
            _vertices.Clear();
            _colors.Clear();
            _uvs.Clear();
            _normals.Clear();
            _triangles.Clear();
            if (_quadRenderer != null)
            {
                _quadRenderer.enabled = false;
            }
            if (_labels != null)
            {
                _labels.Clear();
            }
        }

        /// <summary>
        /// Places one badge. <paramref name="anchor"/> is the measure line's
        /// outer right end in master-local space; <paramref name="outward"/>
        /// and <paramref name="up"/> grow the square away from the line.
        /// </summary>
        public bool TryAdd(
            Vector3 anchor,
            Vector3 outward,
            Vector3 up,
            Vector2 screenPoint,
            int number)
        {
            if (!Available || !_visible || number <= 0)
            {
                return false;
            }
            if (_badgeCount >=
                FalsumMeasureNumberLayout.MaxBadges)
            {
                return false;
            }
            if (!FalsumMeasureNumberLayout.Accepts(
                    _placedScreen,
                    screenPoint))
            {
                return false;
            }

            var size = FalsumMeasureNumberLayout.BadgeSize;
            var gap = FalsumMeasureNumberLayout.AnchorGap;
            var origin = anchor + outward * gap + up * gap;
            var index = _badgeCount * 4;
            _vertices.Add(origin);
            _vertices.Add(origin + outward * size);
            _vertices.Add(origin + outward * size + up * size);
            _vertices.Add(origin + up * size);
            for (var v = 0; v < 4; v++)
            {
                _colors.Add(QuadFill);
                _uvs.Add(Vector2.zero);
                _normals.Add(-outward);
            }
            _triangles.Add(index);
            _triangles.Add(index + 1);
            _triangles.Add(index + 2);
            _triangles.Add(index);
            _triangles.Add(index + 2);
            _triangles.Add(index + 3);

            _badgeCount++;
            _placedScreen.Add(screenPoint);
            PlaceLabel(origin, outward, up, size, number);
            return true;
        }

        public void Apply()
        {
            if (_quadMesh == null)
            {
                return;
            }
            if (_badgeCount == 0)
            {
                _quadRenderer.enabled = false;
                return;
            }
            _quadRenderer.enabled = true;
            _quadMesh.Clear();
            _quadMesh.SetVertices(_vertices);
            _quadMesh.SetColors(_colors);
            _quadMesh.SetUVs(0, _uvs);
            _quadMesh.SetNormals(_normals);
            _quadMesh.SetTriangles(_triangles, 0, false);
            _quadMesh.RecalculateBounds();
        }

        private void PlaceLabel(
            Vector3 origin,
            Vector3 outward,
            Vector3 up,
            float size,
            int number)
        {
            if (_labels == null || !_labels.Available)
            {
                return;
            }
            var camera = Camera.main;
            if (camera == null)
            {
                return;
            }
            // Convert the square's screen footprint into pixels so the text
            // tracks the badge but never shrinks below a readable size.
            var cornerA = camera.WorldToScreenPoint(
                _parent.TransformPoint(origin));
            var cornerB = camera.WorldToScreenPoint(
                _parent.TransformPoint(
                    origin + outward * size + up * size));
            if (cornerA.z <= 0.0f || cornerB.z <= 0.0f)
            {
                return;
            }
            var pixels = Mathf.Max(
                Mathf.Abs(cornerB.x - cornerA.x),
                Mathf.Abs(cornerB.y - cornerA.y));
            // Let perspective drive the size; only lift the floor so far
            // badges do not become unreadable.
            var badgePixels = Mathf.Max(
                pixels,
                MinimumBadgePixels);
            var scale = badgePixels / ReferenceBadgePixels;
            // The flat label must sit on the world square and inside it.
            var centerWorld = _parent.TransformPoint(
                origin + outward * (size * 0.5f) +
                up * (size * 0.5f));
            Vector2 canvasPoint;
            if (!_labels.TryProject(centerWorld, out canvasPoint))
            {
                return;
            }

            var value = FalsumMeasureNumberLayout.Format(number);
            var width = badgePixels;
            var height = badgePixels;
            var fontSize = Mathf.Clamp(
                Mathf.RoundToInt(ReferenceFontSize * scale),
                MinimumFontSize,
                MaximumFontSize);
            // The world square keeps its perspective; the flat label mirrors
            // its on-screen size so the two read as one badge.
            var item = _labels.Place(
                canvasPoint,
                new Vector2(width, height * 0.98f),
                new Vector2(0.5f, 0.5f),
                value,
                fontSize,
                GlyphTint,
                TextAnchor.MiddleCenter);
            if (item != null)
            {
                item.Label.verticalOverflow =
                    VerticalWrapMode.Truncate;
                item.Label.horizontalOverflow =
                    HorizontalWrapMode.Overflow;
                item.Label.fontStyle = FontStyle.Bold;
            }
        }

        private Transform CreateRoot(string name)
        {
            var root = new GameObject(name);
            root.transform.SetParent(_parent, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            return root.transform;
        }

        private static Material CreateQuadMaterial()
        {
            var shader = Resources.Load<Shader>(
                "Falsum/Shaders/FalsumEditorGridLine");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }
            if (shader == null)
            {
                throw new System.InvalidOperationException(
                    L.Get("falsum.error.shader.editorOverlayMissing"));
            }
            var material = new Material(shader)
            {
                name = "FalsumMeasureNumberQuad",
                renderQueue = RenderQueue,
            };
            material.SetColor("_Color", Color.white);
            return material;
        }
    }
}
