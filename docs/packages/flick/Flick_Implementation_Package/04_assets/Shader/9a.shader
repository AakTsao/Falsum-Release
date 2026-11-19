Shader "InFalsus/Compat/StenciledUnlitSprite"
{
    Properties
    {
        [MainTexture] _MainTex ("Texture", 2D) = "white" {}
        [MainColor][HDR] _Color ("Color", Color) = (1,1,1,1)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        _QueueOffset ("Queue Offset", Float) = 0
        _Lowiro_MaskingParameterIndex ("Mask Parameter Index", Float) = 0
        _Lowiro_TilingRepeat ("Tiling Repeat", Vector) = (0,0,1,1)
        _Lowiro_AlphaMaskTexture ("Alpha Mask Texture", 2D) = "white" {}
        _Lowiro_EnableAlphaMask ("Enable Alpha Mask", Float) = 0
        _Lowiro_AlphaMaskThreshold ("Alpha Mask Threshold", Float) = 0
        _Lowiro_EffectColor ("Effect Color", Vector) = (1,1,1,0)
        _Lowiro_EffectType ("Effect Type", Float) = 0
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
            Name "Universal Forward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_Lowiro_AlphaMaskTexture);
            SAMPLER(sampler_Lowiro_AlphaMaskTexture);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float4 _Lowiro_TilingRepeat;
                float4 _Lowiro_EffectColor;
                float _Lowiro_MaskingParameterIndex;
                float _Lowiro_EnableAlphaMask;
                float _Lowiro_AlphaMaskThreshold;
                float _Lowiro_EffectType;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float4 textureColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                float4 color = textureColor * _Color * input.color;

                if (_Lowiro_EnableAlphaMask > 0.5)
                {
                    float mask = SAMPLE_TEXTURE2D(
                        _Lowiro_AlphaMaskTexture,
                        sampler_Lowiro_AlphaMaskTexture,
                        input.uv).a;
                    color.a *= step(_Lowiro_AlphaMaskThreshold, mask);
                }

                return color;
            }
            ENDHLSL
        }
    }
}
