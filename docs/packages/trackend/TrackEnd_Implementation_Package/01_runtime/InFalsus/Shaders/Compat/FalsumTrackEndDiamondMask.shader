Shader "InFalsus/FalsumTrackEndDiamondMask"
{
    Properties
    {
        [HideInInspector] _TimeOverride ("Time Override", Float) = -1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float _TimeOverride;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float4 uv0 : TEXCOORD0;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
        };

        float OriginalSign(float value)
        {
            return value >= -value ? 1.0 : -1.0;
        }

        float2 TriangleSeed(float triangleId)
        {
            float2 hash = frac(
                sin(float2(triangleId + 1.0, triangleId + 2.0)) *
                43758.546875);
            return hash * float2(3.0, 0.1) + float2(-1.5, 0.1);
        }

        float2 TriangleOffset(float2 seed, float verticalSpeed)
        {
            float effectTime = _TimeOverride >= 0.0
                ? _TimeOverride
                : _Time.y;
            float phase =
                seed.y * (effectTime + 100.0) * verticalSpeed;
            float radial =
                OriginalSign(phase) * frac(abs(phase));
            return 1.5 * radial *
                float2(sin(seed.x), 1.35 * cos(seed.x));
        }

        Varyings VertTriangleWhite(Attributes input)
        {
            Varyings output;
            float2 seed = TriangleSeed(input.uv0.x);
            float2 offset = TriangleOffset(seed, 1.0);

            float xScale =
                1.0 - (abs(offset.x) - 0.7) * 3.25;
            float2 position = abs(offset.x) > 0.7
                ? input.positionOS.xy * xScale
                : input.positionOS.xy;
            position += offset;

            float l1 = abs(position.x) + abs(position.y);
            if (l1 > 1.5)
            {
                position *= 1.5 / l1;
            }

            output.positionCS = float4(
                position,
                input.positionOS.zw);
            return output;
        }

        Varyings VertTriangleBlack(Attributes input)
        {
            Varyings output;
            float2 seed = TriangleSeed(input.uv0.x);
            float2 offset = TriangleOffset(seed, 0.7);
            output.positionCS = input.positionOS +
                float4(offset, 0.0, 0.0);
            return output;
        }

        Varyings VertHexagon(Attributes input)
        {
            Varyings output;
            output.positionCS = input.positionOS;
            return output;
        }

        float4 FragWhite(Varyings input) : SV_Target
        {
            return float4(1.0, 1.0, 1.0, 1.0);
        }

        float4 FragBlack(Varyings input) : SV_Target
        {
            return float4(0.0, 0.0, 0.0, 0.0);
        }
        ENDHLSL

        Pass
        {
            Name "TrianglesWhite"
            Tags { "LightMode" = "UniversalForward" }
            Blend One Zero
            ZTest LEqual
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex VertTriangleWhite
            #pragma fragment FragWhite
            ENDHLSL
        }

        Pass
        {
            Name "TrianglesBlack"
            Tags { "LightMode" = "UniversalForward" }
            Blend One Zero
            ZTest LEqual
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex VertTriangleBlack
            #pragma fragment FragBlack
            ENDHLSL
        }

        Pass
        {
            Name "HexagonWhite"
            Tags { "LightMode" = "UniversalForward" }
            Blend One Zero
            ZTest LEqual
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex VertHexagon
            #pragma fragment FragWhite
            ENDHLSL
        }
    }
}
