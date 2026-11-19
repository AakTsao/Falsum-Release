using System;
using System.Collections.Generic;
using InFalsus.Gameplay.Core.Charts;
using InFalsus.Gameplay.Core.Timing;
using InFalsus.Gameplay.Runtime.SceneBinding;
using UnityEngine;
using UnityEngine.Rendering;

namespace InFalsus.Gameplay.Presentation.Tracks
{
    public sealed class MeasureLineRenderer
    {
        // Wider than before, but still below the editor's 0.026 grid width.
        private const float LineWidth = 0.018f;
        private const float FloorLift = 0.003f;

        private sealed class DynamicLineMesh
        {
            public Mesh Mesh;
            public MeshRenderer Renderer;
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Color> Colors = new List<Color>();
            public readonly List<int> Triangles = new List<int>();
        }

        private sealed class SideGeometry
        {
            public Transform Transform;
            public Vector3 JudgementWorld;
            public Vector3 EndWorld;
            public Vector3 JudgementLocal;
            public Vector3 EndLocal;
            public float WidthMin;
            public float WidthMax;
            public float CenterX;
        }

        private readonly Chart _chart;
        private readonly TrackMotion _motion;
        private readonly Transform _root;
        private readonly DynamicLineMesh _middle;
        private readonly DynamicLineMesh _left;
        private readonly DynamicLineMesh _right;
        private readonly SideGeometry _leftGeometry;
        private readonly SideGeometry _rightGeometry;
        private readonly double _lineBaseZ;
        private readonly float _trackEndZ;
        private readonly float _middleLeftX;
        private readonly float _middleRightX;
        private readonly float _middleY;

        public int VisibleLineCount { get; private set; }

        public MeasureLineRenderer(
            GameplaySceneBinding binding,
            Chart chart,
            TrackMotion motion)
        {
            _chart = chart;
            _motion = motion;
            _root = binding.MasterScene;
            _lineBaseZ = binding.MasterScene
                .InverseTransformPoint(binding.JudgementLine.position).z;
            _middle = CreateLineMesh("PhaseA_MeasureLines_Main");
            _left = CreateLineMesh("PhaseA_MeasureLines_Shift");
            _right = CreateLineMesh("PhaseA_MeasureLines_Space");
            _leftGeometry = CreateSideGeometry(
                binding,
                binding.LeftSideLane);
            _rightGeometry = CreateSideGeometry(
                binding,
                binding.RightSideLane);
            _trackEndZ = ResolveTrackEndZ(
                _root,
                _leftGeometry,
                _rightGeometry,
                (float)_lineBaseZ);

            Vector3 leftPoint;
            Vector3 rightPoint;
            if (TryGetMiddlePoints(
                    binding,
                    out leftPoint,
                    out rightPoint))
            {
                _middleLeftX = leftPoint.x;
                _middleRightX = rightPoint.x;
                _middleY = (leftPoint.y + rightPoint.y) * 0.5f +
                           FloorLift;
            }
            else
            {
                _middleLeftX = -0.8f;
                _middleRightX = 0.8f;
                _middleY = FloorLift;
            }
        }

        public void Update(double nowMs)
        {
            _middle.Vertices.Clear();
            _middle.Colors.Clear();
            _middle.Triangles.Clear();
            _left.Vertices.Clear();
            _left.Colors.Clear();
            _left.Triangles.Clear();
            _right.Vertices.Clear();
            _right.Colors.Clear();
            _right.Triangles.Clear();
            VisibleLineCount = 0;

            for (var i = 0; i < _chart.MeasureLineTimes.Count; i++)
            {
                var timeMs = _chart.MeasureLineTimes[i];
                var z = _motion.NoteZAt(timeMs, nowMs);
                if (!_motion.IsVisible(z))
                {
                    continue;
                }

                var targetZ = (float)(_lineBaseZ + z);
                if (targetZ < _lineBaseZ - 0.001 ||
                    targetZ > _trackEndZ + 0.001)
                {
                    continue;
                }
                VisibleLineCount++;
                var core = new Color(0.72f, 0.94f, 1.0f, 0.92f);
                AddMiddleLine(
                    targetZ,
                    LineWidth,
                    core);
                AddSideLine(
                    _left,
                    _leftGeometry,
                    targetZ,
                    LineWidth,
                    core);
                AddSideLine(
                    _right,
                    _rightGeometry,
                    targetZ,
                    LineWidth,
                    core);
            }

            ApplyMesh(_middle);
            ApplyMesh(_left);
            ApplyMesh(_right);
        }

        private void AddMiddleLine(
            float z,
            float width,
            Color color)
        {
            var index = _middle.Vertices.Count;
            _middle.Vertices.Add(new Vector3(
                _middleLeftX, _middleY, z - width * 0.5f));
            _middle.Vertices.Add(new Vector3(
                _middleRightX, _middleY, z - width * 0.5f));
            _middle.Vertices.Add(new Vector3(
                _middleLeftX, _middleY, z + width * 0.5f));
            _middle.Vertices.Add(new Vector3(
                _middleRightX, _middleY, z + width * 0.5f));
            for (var i = 0; i < 4; i++)
            {
                _middle.Colors.Add(color);
            }
            AddQuadTriangles(_middle.Triangles, index);
        }

        private void AddSideLine(
            DynamicLineMesh output,
            SideGeometry geometry,
            float z,
            float width,
            Color color)
        {
            if (geometry == null)
            {
                return;
            }

            Vector3 lowA;
            Vector3 lowB;
            Vector3 highA;
            Vector3 highB;
            if (!TryGetSidePoints(
                    geometry,
                    z - width * 0.5f,
                    out lowA,
                    out lowB) ||
                !TryGetSidePoints(
                    geometry,
                    z + width * 0.5f,
                    out highA,
                    out highB))
            {
                return;
            }

            var index = output.Vertices.Count;
            output.Vertices.Add(lowA);
            output.Vertices.Add(lowB);
            output.Vertices.Add(highA);
            output.Vertices.Add(highB);
            for (var i = 0; i < 4; i++)
            {
                output.Colors.Add(color);
            }
            AddQuadTriangles(output.Triangles, index);
        }

        private bool TryGetSidePoints(
            SideGeometry geometry,
            float targetZ,
            out Vector3 left,
            out Vector3 right)
        {
            left = Vector3.zero;
            right = Vector3.zero;
            if (geometry == null)
            {
                return false;
            }

            var normal = _root.forward;
            var axis = geometry.EndWorld - geometry.JudgementWorld;
            var denominator = Vector3.Dot(axis, normal);
            if (Mathf.Abs(denominator) < 0.00001f)
            {
                return false;
            }

            var planePoint = _root.TransformPoint(
                new Vector3(0.0f, 0.0f, targetZ));
            var progress =
                (Vector3.Dot(planePoint, normal) -
                 Vector3.Dot(geometry.JudgementWorld, normal)) /
                denominator;
            if (progress < -0.02f || progress > 1.02f)
            {
                return false;
            }

            var center = Vector3.Lerp(
                geometry.JudgementLocal,
                geometry.EndLocal,
                progress);
            left = _root.InverseTransformPoint(
                geometry.Transform.TransformPoint(new Vector3(
                    geometry.CenterX,
                    center.y,
                    geometry.WidthMin)));
            right = _root.InverseTransformPoint(
                geometry.Transform.TransformPoint(new Vector3(
                    geometry.CenterX,
                    center.y,
                    geometry.WidthMax)));
            return true;
        }

        private DynamicLineMesh CreateLineMesh(string name)
        {
            var shader = Shader.Find("InFalsus/Gameplay/MeasureLine");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "Measure line shader is missing.");
            }

            var root = new GameObject(name);
            root.transform.SetParent(_root, false);
            var filter = root.AddComponent<MeshFilter>();
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = new Material(shader)
            {
                name = name + "_Material",
                renderQueue = 3035,
            };
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var mesh = new Mesh
            {
                name = name + "_Mesh",
            };
            mesh.MarkDynamic();
            filter.sharedMesh = mesh;
            return new DynamicLineMesh
            {
                Mesh = mesh,
                Renderer = renderer,
            };
        }

        private static void ApplyMesh(DynamicLineMesh output)
        {
            output.Mesh.Clear();
            if (output.Vertices.Count == 0)
            {
                output.Renderer.enabled = false;
                return;
            }

            output.Renderer.enabled = true;
            output.Mesh.SetVertices(output.Vertices);
            output.Mesh.SetColors(output.Colors);
            output.Mesh.SetTriangles(output.Triangles, 0, false);
            output.Mesh.RecalculateBounds();
        }

        private static void AddQuadTriangles(List<int> triangles, int index)
        {
            triangles.Add(index);
            triangles.Add(index + 1);
            triangles.Add(index + 2);
            triangles.Add(index + 2);
            triangles.Add(index + 1);
            triangles.Add(index + 3);
        }

        private static bool TryGetMiddlePoints(
            GameplaySceneBinding binding,
            out Vector3 left,
            out Vector3 right)
        {
            left = Vector3.zero;
            right = Vector3.zero;
            var leftFilter = binding.LaneAnchors[1]
                .GetComponent<MeshFilter>();
            var rightFilter = binding.LaneAnchors[4]
                .GetComponent<MeshFilter>();
            if (leftFilter == null ||
                rightFilter == null ||
                leftFilter.sharedMesh == null ||
                rightFilter.sharedMesh == null)
            {
                return false;
            }

            var leftBounds = leftFilter.sharedMesh.bounds;
            var rightBounds = rightFilter.sharedMesh.bounds;
            left = binding.MasterScene.InverseTransformPoint(
                leftFilter.transform.TransformPoint(new Vector3(
                    leftBounds.min.x,
                    leftBounds.center.y,
                    leftBounds.center.z)));
            right = binding.MasterScene.InverseTransformPoint(
                rightFilter.transform.TransformPoint(new Vector3(
                    rightBounds.max.x,
                    rightBounds.center.y,
                    rightBounds.center.z)));
            return true;
        }

        private static SideGeometry CreateSideGeometry(
            GameplaySceneBinding binding,
            Transform transform)
        {
            if (transform == null)
            {
                return null;
            }
            var filter = transform.GetComponent<MeshFilter>();
            var renderer = transform.GetComponent<Renderer>();
            var material = renderer != null
                ? renderer.sharedMaterial
                : null;
            if (filter == null ||
                filter.sharedMesh == null ||
                material == null ||
                !material.HasProperty("_JudgementLineWorldPosition") ||
                !material.HasProperty("_EndOfTrackWorldPosition"))
            {
                return null;
            }

            var judgement = (Vector3)material.GetVector(
                "_JudgementLineWorldPosition");
            var end = (Vector3)material.GetVector(
                "_EndOfTrackWorldPosition");
            var bounds = filter.sharedMesh.bounds;
            return new SideGeometry
            {
                Transform = transform,
                JudgementWorld = judgement,
                EndWorld = end,
                JudgementLocal = transform.InverseTransformPoint(judgement),
                EndLocal = transform.InverseTransformPoint(end),
                WidthMin = bounds.min.z,
                WidthMax = bounds.max.z,
                CenterX = bounds.center.x,
            };
        }

        private static float ResolveTrackEndZ(
            Transform root,
            SideGeometry left,
            SideGeometry right,
            float fallbackStartZ)
        {
            var hasEnd = false;
            var endZ = float.MinValue;
            if (left != null)
            {
                endZ = Mathf.Max(
                    endZ,
                    root.InverseTransformPoint(left.EndWorld).z);
                hasEnd = true;
            }
            if (right != null)
            {
                endZ = Mathf.Max(
                    endZ,
                    root.InverseTransformPoint(right.EndWorld).z);
                hasEnd = true;
            }
            return hasEnd
                ? endZ
                : fallbackStartZ + (float)TrackMotion.DefaultTrackLength;
        }
    }
}
