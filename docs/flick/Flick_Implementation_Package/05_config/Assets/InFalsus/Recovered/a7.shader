Shader "InFalsus/Replica/SkyJudgementLineGlow"
{
    Properties
    {
        [NoScaleOffset] _Main ("Main", 2D) = "white" {}
        [NoScaleOffset] _DownscaledGrid ("Downscaled Grid", 2D) =
            "black" {}
        [NoScaleOffset] _DownscaledBlurredGrid (
            "Downscaled Blurred Grid", 2D) = "black" {}
        _CenterX ("Center X", Float) = 2.0
        _Width ("Width", Float) = 2.5
        _Opacity ("Opacity", Float) = 1.0
        _TriangleBoundary ("Triangle Boundary", Float) = 0.95
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent+11"
            "UniversalMaterialType" = "Unlit"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Universal Forward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_DownscaledGrid);
            SAMPLER(sampler_DownscaledGrid);
            TEXTURE2D(_DownscaledBlurredGrid);
            SAMPLER(sampler_DownscaledBlurredGrid);

            CBUFFER_START(UnityPerMaterial)
                float4 _Main_ST;
                float4 _DownscaledGrid_ST;
                float4 _DownscaledBlurredGrid_ST;
                float _CenterX;
                float _Width;
                float _Opacity;
                float _TriangleBoundary;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS =
                    TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float3 LinearToSRGB(float3 value)
            {
                float3 low = value * 12.92321;
                float3 high =
                    1.055 * pow(abs(value), 1.0 / 2.4) - 0.055;
                return lerp(
                    high,
                    low,
                    step(abs(value), 0.003131));
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float2 p;
                p.x = uv.x - _CenterX * 0.8 - 0.1;
                p.y = uv.y;
                p = p * float2(40.0, 2.0) + float2(0.0, -1.0);
                p = abs(p) -
                    float2(_Width * 16.8, _Opacity * 0.1);
                p += 0.5;

                float2 positive = max(p, 0.0);
                float corner = min(max(p.y, p.x), 0.0);
                float signedDistance =
                    length(positive) + corner;
                float glowCore =
                    1.0 - (signedDistance - 0.5);
                float verticalFade =
                    1.0 - 2.0 * abs(uv.y - 0.5);
                glowCore = saturate(glowCore * verticalFade);
                glowCore = pow(glowCore, 0.74);

                float grid = SAMPLE_TEXTURE2D(
                    _DownscaledGrid,
                    sampler_DownscaledGrid,
                    uv).r;
                float blurred = SAMPLE_TEXTURE2D(
                    _DownscaledBlurredGrid,
                    sampler_DownscaledBlurredGrid,
                    uv).r;
                float maskSource = max(max(grid, blurred), 0.0);
                float mask = pow(maskSource, 0.3);
                mask = min(mask, 1.0);
                float smoothMask =
                    mask * mask * (3.0 - 2.0 * mask);
                float coverage =
                    min(max(smoothMask, glowCore), 1.0);

                float thresholdPass = step(
                    _TriangleBoundary,
                    smoothMask);
                float fringe = thresholdPass *
                    (smoothMask - _TriangleBoundary) /
                    max(1.0 - _TriangleBoundary, 0.00001);
                fringe = saturate(fringe);

                float intensity = coverage * glowCore;
                float colorAmountA = saturate(
                    (intensity - 0.450004) * 5.3125);
                float colorAmountB = saturate(
                    (intensity - 0.638239) * 3.366293);
                float ramp = min(intensity * 2.222203, 1.0);

                float3 color =
                    ramp * float3(
                        0.173436,
                        0.064836,
                        0.179245) +
                    float3(
                        0.211688,
                        0.170657,
                        0.613208);
                color = lerp(
                    color,
                    float3(0.507770, 0.247196, 0.952830),
                    colorAmountA);
                color = lerp(color, 1.0, colorAmountB);
                color = LinearToSRGB(color);
                color = lerp(color, 1.0, fringe);

                float alpha = saturate(coverage * _Opacity);
                return float4(color, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
