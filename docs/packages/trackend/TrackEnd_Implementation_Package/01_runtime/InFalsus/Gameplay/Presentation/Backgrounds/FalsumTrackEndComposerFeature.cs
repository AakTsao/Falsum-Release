using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace InFalsus.Gameplay.Presentation.Backgrounds
{
    public sealed class FalsumTrackEndComposerFeature :
        ScriptableRendererFeature
    {
        private sealed class ComposerPass : ScriptableRenderPass
        {
            private sealed class DrawPassData
            {
                public FalsumTrackEndComposer Composer;
            }

            private sealed class BlitPassData
            {
                public TextureHandle Source;
                public Material Material;
                public int PassIndex;
            }

            private FalsumTrackEndComposer _composer;

            public ComposerPass()
            {
                renderPassEvent =
                    RenderPassEvent.AfterRenderingOpaques;
            }

            public void Setup(FalsumTrackEndComposer composer)
            {
                _composer = composer;
            }

            public override void RecordRenderGraph(
                RenderGraph renderGraph,
                ContextContainer frameData)
            {
                if (_composer == null || !_composer.IsReady)
                {
                    return;
                }
                // The mask shader animates triangles from _Time.y.
                // This pass must therefore rebuild the blur chain every frame.
                var cameraData = frameData.Get<UniversalCameraData>();
                if (cameraData.camera == null ||
                    cameraData.camera != Camera.main)
                {
                    return;
                }

                var mask = renderGraph.ImportTexture(
                    _composer.MaskRTHandle);
                DrawMask(renderGraph, mask);

                var descriptor = _composer.MaskTexture.descriptor;
                descriptor.depthBufferBits = 0;

                var halfDescriptor = descriptor;
                halfDescriptor.width /= 2;
                halfDescriptor.height /= 2;
                var quarterDescriptor = halfDescriptor;
                quarterDescriptor.width /= 2;
                quarterDescriptor.height /= 2;

                var halfA = UniversalRenderer.CreateRenderGraphTexture(
                    renderGraph,
                    halfDescriptor,
                    "_FalsumTrackEndDiamondDownOne",
                    false);
                var halfB = UniversalRenderer.CreateRenderGraphTexture(
                    renderGraph,
                    halfDescriptor,
                    "_FalsumTrackEndDiamondUpOne",
                    false);
                var quarterA =
                    UniversalRenderer.CreateRenderGraphTexture(
                        renderGraph,
                        quarterDescriptor,
                        "_FalsumTrackEndDiamondDownTwo",
                        false);
                var quarterB =
                    UniversalRenderer.CreateRenderGraphTexture(
                        renderGraph,
                        quarterDescriptor,
                        "_FalsumTrackEndDiamondUpTwo",
                        false);

                AddBlit(
                    renderGraph,
                    "Falsum TrackEnd Diamond - Down One Vertical",
                    mask,
                    halfA,
                    0);
                AddBlit(
                    renderGraph,
                    "Falsum TrackEnd Diamond - Down One Horizontal",
                    halfA,
                    halfB,
                    1);
                AddBlit(
                    renderGraph,
                    "Falsum TrackEnd Diamond - Down Two Vertical",
                    halfB,
                    quarterA,
                    0);
                AddBlit(
                    renderGraph,
                    "Falsum TrackEnd Diamond - Down Two Horizontal",
                    quarterA,
                    quarterB,
                    1);
                AddBlit(
                    renderGraph,
                    "Falsum TrackEnd Diamond - Up One Vertical",
                    quarterB,
                    halfA,
                    0);
                AddBlit(
                    renderGraph,
                    "Falsum TrackEnd Diamond - Up One Horizontal",
                    halfA,
                    halfB,
                    1);
                AddBlit(
                    renderGraph,
                    "Falsum TrackEnd Diamond - Final Horizontal",
                    halfB,
                    mask,
                    1);
                FalsumTrackEndComposerFeature.MaskRebuildCount++;
            }

            private void DrawMask(
                RenderGraph renderGraph,
                TextureHandle mask)
            {
                using (var builder =
                       renderGraph.AddRasterRenderPass<DrawPassData>(
                           "Falsum TrackEnd Diamond - Draw Mask",
                           out var passData))
                {
                    passData.Composer = _composer;
                    builder.AllowPassCulling(false);
                    builder.SetRenderAttachment(mask, 0);
                    builder.SetRenderFunc(
                        (DrawPassData data, RasterGraphContext context) =>
                        {
                            var command = context.cmd;
                            command.ClearRenderTarget(
                                true,
                                true,
                                Color.clear,
                                0.0f);
                            command.DrawRenderer(
                                data.Composer.TriangleRenderer,
                                data.Composer.TriangleMaterial,
                                0,
                                0);
                            command.DrawRenderer(
                                data.Composer.TriangleRenderer,
                                data.Composer.TriangleMaterial,
                                0,
                                1);
                            command.DrawRenderer(
                                data.Composer.HexagonRenderer,
                                data.Composer.HexagonMaterial,
                                0,
                                2);
                        });
                }
            }

            private void AddBlit(
                RenderGraph renderGraph,
                string passName,
                TextureHandle source,
                TextureHandle destination,
                int passIndex)
            {
                using (var builder =
                       renderGraph.AddRasterRenderPass<BlitPassData>(
                           passName,
                           out var passData))
                {
                    passData.Source = source;
                    passData.Material = _composer.BlurMaterial;
                    passData.PassIndex = passIndex;
                    builder.AllowPassCulling(false);
                    builder.UseTexture(source, AccessFlags.Read);
                    builder.SetRenderAttachment(destination, 0);
                    builder.SetRenderFunc(
                        (BlitPassData data, RasterGraphContext context) =>
                        {
                            Blitter.BlitTexture(
                                context.cmd,
                                data.Source,
                                new Vector4(1.0f, 1.0f, 0.0f, 0.0f),
                                data.Material,
                                data.PassIndex);
                        });
                }
            }

        }

        private ComposerPass _pass;

        public static int MaskRebuildCount { get; private set; }

        public override void Create()
        {
            _pass = new ComposerPass();
        }

        public override void AddRenderPasses(
            ScriptableRenderer renderer,
            ref RenderingData renderingData)
        {
            var composer = FalsumTrackEndComposer.Active;
            if (composer == null || !composer.IsReady)
            {
                return;
            }
            _pass.Setup(composer);
            renderer.EnqueuePass(_pass);
        }
    }
}
