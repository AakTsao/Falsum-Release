using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace InFalsus.Gameplay.Presentation.Notes
{
    public sealed class SkyJudgementLineEffectFeature :
        ScriptableRendererFeature
    {
        private sealed class EffectPass : ScriptableRenderPass
        {
            private sealed class DrawPassData
            {
                public SkyJudgementLineEffect Effect;
            }

            private sealed class BlitPassData
            {
                public TextureHandle Source;
                public Material Material;
                public int PassIndex;
            }

            private SkyJudgementLineEffect _effect;

            public EffectPass()
            {
                renderPassEvent =
                    RenderPassEvent.AfterRenderingOpaques;
            }

            public void Setup(SkyJudgementLineEffect effect)
            {
                _effect = effect;
            }

            public override void RecordRenderGraph(
                RenderGraph renderGraph,
                ContextContainer frameData)
            {
                if (_effect == null || !_effect.IsReady)
                {
                    return;
                }
                var cameraData =
                    frameData.Get<UniversalCameraData>();
                if (cameraData.camera == null ||
                    !cameraData.camera.CompareTag("MainCamera"))
                {
                    return;
                }

                var fullMask = renderGraph.ImportTexture(
                    _effect.FullMaskRTHandle);
                var filteredMask = renderGraph.ImportTexture(
                    _effect.FilteredMaskRTHandle);
                var blurredMask = renderGraph.ImportTexture(
                    _effect.BlurredMaskRTHandle);

                DrawMask(renderGraph, fullMask);

                var filteredDescriptor =
                    _effect.FilteredMaskTexture.descriptor;
                filteredDescriptor.depthBufferBits = 0;
                var downOneDescriptor = filteredDescriptor;
                downOneDescriptor.width /= 2;
                downOneDescriptor.height /= 2;
                var downTwoDescriptor = downOneDescriptor;
                downTwoDescriptor.width /= 2;
                downTwoDescriptor.height /= 2;

                var downOne = UniversalRenderer.CreateRenderGraphTexture(
                    renderGraph,
                    downOneDescriptor,
                    "_SkyJudgementLine_DownOne",
                    false);
                var upOne = UniversalRenderer.CreateRenderGraphTexture(
                    renderGraph,
                    downOneDescriptor,
                    "_SkyJudgementLine_UpOne",
                    false);
                var downTwo = UniversalRenderer.CreateRenderGraphTexture(
                    renderGraph,
                    downTwoDescriptor,
                    "_SkyJudgementLine_DownTwo",
                    false);
                var upTwo = UniversalRenderer.CreateRenderGraphTexture(
                    renderGraph,
                    downTwoDescriptor,
                    "_SkyJudgementLine_UpTwo",
                    false);

                AddBlit(
                    renderGraph,
                    "Sky Judgement Line - Down One Horizontal",
                    fullMask,
                    filteredMask,
                    0);
                AddBlit(
                    renderGraph,
                    "Sky Judgement Line - Down One Vertical",
                    filteredMask,
                    downOne,
                    1);
                AddBlit(
                    renderGraph,
                    "Sky Judgement Line - Down Two Horizontal",
                    downOne,
                    downTwo,
                    0);
                AddBlit(
                    renderGraph,
                    "Sky Judgement Line - Down Two Vertical",
                    downTwo,
                    upTwo,
                    1);
                AddBlit(
                    renderGraph,
                    "Sky Judgement Line - Up One Horizontal",
                    upTwo,
                    downOne,
                    0);
                AddBlit(
                    renderGraph,
                    "Sky Judgement Line - Up One Vertical",
                    downOne,
                    upOne,
                    1);
                AddBlit(
                    renderGraph,
                    "Sky Judgement Line - Final Vertical",
                    upOne,
                    blurredMask,
                    1);
            }

            private void DrawMask(
                RenderGraph renderGraph,
                TextureHandle fullMask)
            {
                using (var builder =
                       renderGraph.AddRasterRenderPass<DrawPassData>(
                           "Sky Judgement Line - Draw Mesh",
                           out var passData))
                {
                    passData.Effect = _effect;
                    builder.AllowPassCulling(false);
                    builder.SetRenderAttachment(fullMask, 0);
                    builder.SetRenderFunc(
                        (DrawPassData data, RasterGraphContext context) =>
                        {
                            var command = context.cmd;
                            command.ClearRenderTarget(
                                true,
                                true,
                                Color.clear,
                                1.0f);
                            command.DrawRenderer(
                                data.Effect.TriangleRenderer,
                                data.Effect.TriangleMaterial,
                                0,
                                -1);
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
                    passData.Material = _effect.BlurMaterial;
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
                                new Vector4(
                                    1.0f,
                                    1.0f,
                                    0.0f,
                                    0.0f),
                                data.Material,
                                data.PassIndex);
                        });
                }
            }
        }

        private EffectPass _pass;

        public override void Create()
        {
            _pass = new EffectPass();
        }

        public override void AddRenderPasses(
            ScriptableRenderer renderer,
            ref RenderingData renderingData)
        {
            var effect = SkyJudgementLineEffect.Active;
            if (effect == null || !effect.ShouldRender)
            {
                return;
            }
            _pass.Setup(effect);
            renderer.EnqueuePass(_pass);
        }
    }
}
