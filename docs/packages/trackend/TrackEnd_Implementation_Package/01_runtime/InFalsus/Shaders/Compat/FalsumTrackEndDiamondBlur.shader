Shader "InFalsus/FalsumTrackEndDiamondBlur"
{
    Properties
    {
        _VerticalBlur ("Vertical Blur", Float) = 0.000187
        _HorizontalBlur ("Horizontal Blur", Float) = 0.000187
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float _VerticalBlur;
            float _HorizontalBlur;
        CBUFFER_END

        float3 SampleSource(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X_LOD(
                _BlitTexture,
                sampler_LinearClamp,
                uv,
                _BlitMipLevel).rgb;
        }

        float3 SampleFiveTap(float2 uv, float2 nearOffset, float2 farOffset)
        {
            return
                SampleSource(uv) * 0.227027 +
                SampleSource(uv + nearOffset) * 0.316216 +
                SampleSource(uv - nearOffset) * 0.316216 +
                SampleSource(uv + farOffset) * 0.070270 +
                SampleSource(uv - farOffset) * 0.070270;
        }

        float4 FragVertical(Varyings input) : SV_Target
        {
            float radius =
                _ScreenParams.y * _VerticalBlur /
                _BlitTexture_TexelSize.w;
            float2 nearOffset = float2(0.0, 0.428571 * radius);
            float2 farOffset = float2(0.0, radius);
            return float4(
                SampleFiveTap(input.texcoord, nearOffset, farOffset),
                1.0);
        }

        float4 FragHorizontal(Varyings input) : SV_Target
        {
            float radius =
                _ScreenParams.x * _HorizontalBlur /
                _BlitTexture_TexelSize.z;
            float2 nearOffset = float2(0.428571 * radius, 0.0);
            float2 farOffset = float2(radius, 0.0);
            return float4(
                SampleFiveTap(input.texcoord, nearOffset, farOffset),
                1.0);
        }
        ENDHLSL

        Pass
        {
            Name "BlurPassVertical"
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment FragVertical
            ENDHLSL
        }

        Pass
        {
            Name "BlurPassHorizontal"
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment FragHorizontal
            ENDHLSL
        }
    }
}
