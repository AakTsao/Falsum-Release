Shader "InFalsus/FalsumTrackEndDiamondOverlay"
{
    Properties
    {
        _BlurredTexture ("Blurred Texture", 2D) = "black" {}
        _BackingTexture ("Backing Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "TrackEndDiamondOverlay"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BlurredTexture);
            SAMPLER(sampler_BlurredTexture);
            TEXTURE2D(_BackingTexture);
            SAMPLER(sampler_BackingTexture);

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

            float4 Frag(Varyings input) : SV_Target
            {
                float grid = SAMPLE_TEXTURE2D(
                    _BlurredTexture,
                    sampler_BlurredTexture,
                    input.uv).r;
                float alpha = saturate(grid);

                float signedFraction = grid >= 0.0
                    ? frac(grid)
                    : -frac(-grid);
                float outerFade =
                    smoothstep(0.02, 0.34, signedFraction) *
                    (1.0 - smoothstep(
                        0.46,
                        0.72,
                        signedFraction));
                float glow = outerFade * 0.055;

                float3 backing = SAMPLE_TEXTURE2D_BIAS(
                    _BackingTexture,
                    sampler_BackingTexture,
                    input.uv + float2(0.0, 0.5),
                    0.0).rgb;

                const float3 glowColor =
                    float3(0.54, 0.70, 1.00);
                float3 color =
                    backing + (glowColor - backing) * glow;
                return float4(color, alpha);
            }
            ENDHLSL
        }
    }
}
