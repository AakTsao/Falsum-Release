Shader "InFalsus/SkyAreaPrompt"
{
    Properties
    {
        [HDR] _OutlineColor ("Bracket Color", Color) = (1.75, 1.72, 1.90, 1)
        [HDR] _GlowColor ("Bracket Glow", Color) = (1.05, 0.82, 1.10, 0.86)
        _PromptOutlineWidthPx ("Line Width (Pixels)", Range(0.5, 8)) = 3.0
        _PromptGlowWidthPx ("Glow Width (Pixels)", Range(1, 30)) = 10
        _CornerArmPx ("Bracket Arm (Pixels)", Range(2, 20)) = 6
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "UniversalMaterialType" = "Unlit"
        }

        Pass
        {
            Name "SkyAreaPromptOverlay"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 4.0
            #pragma vertex IF_Vert
            #pragma fragment IF_Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float4 _GlowColor;
                float  _PromptOutlineWidthPx;
                float  _PromptGlowWidthPx;
                float  _CornerArmPx;
            CBUFFER_END

            struct IF_Attr
            {
                float4 positionOS : POSITION;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
            };

            struct IF_Var
            {
                float4 positionCS : SV_POSITION;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
            };

            IF_Var IF_Vert(IF_Attr IN)
            {
                IF_Var OUT;
                OUT.positionCS =
                    TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv0 = IN.uv0;
                OUT.uv1 = IN.uv1;
                return OUT;
            }

            float IF_PixelLine(float distancePx, float widthPx)
            {
                float aa = max(fwidth(distancePx), 0.65);
                return 1.0 - smoothstep(
                    widthPx - aa,
                    widthPx + aa,
                    distancePx);
            }

            half4 IF_Frag(IF_Var IN) : SV_Target0
            {
                float u = saturate(IN.uv0.x);
                float v = saturate(IN.uv1.y);

                float xPx = min(u, 1.0 - u) /
                    max(fwidth(u), 0.00001);
                float yPx = min(v, 1.0 - v) /
                    max(fwidth(v), 0.00001);

                float xLine = IF_PixelLine(
                    xPx,
                    _PromptOutlineWidthPx);
                float yLine = IF_PixelLine(
                    yPx,
                    _PromptOutlineWidthPx);
                float xAA = max(fwidth(xPx), 0.65);
                float yAA = max(fwidth(yPx), 0.65);
                float bracketNearX = 1.0 - smoothstep(
                    _CornerArmPx - xAA,
                    _CornerArmPx + xAA,
                    xPx);
                float bracketNearY = 1.0 - smoothstep(
                    _CornerArmPx - yAA,
                    _CornerArmPx + yAA,
                    yPx);
                float verticalArms = xLine * bracketNearY;
                float horizontalArms = yLine * bracketNearX;
                float bracket = max(verticalArms, horizontalArms);

                float glowWidth = max(_PromptGlowWidthPx, 1.0);
                float glow = max(
                    exp(-1.55 * xPx / glowWidth) * bracketNearY,
                    exp(-1.55 * yPx / glowWidth) * bracketNearX);
                float3 color = lerp(
                    _GlowColor.rgb,
                    _OutlineColor.rgb,
                    bracket);
                float alpha = saturate(max(
                    bracket * _OutlineColor.a,
                    glow * _GlowColor.a * 0.52));
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
