//==============================================================================
// FlickV5.shader  鈥? In Falsus 澶嶅埢
// 鍘熷悕: Shader Graphs/FlickV5   (pathID -3912889037052749343)
//==============================================================================
// 鏉ユ簮 bundle : 8719fa86ae7f9dbe9b11654f9dc69197.bundle
// 鎻愬彇宸ュ叿    : .workbuddy/tools/shader_final_extract.py
// 鍘熷鏁版嵁    : .workbuddy/out/shaders_authoritative.json 鈫?shaders["Shader Graphs/FlickV5"]
// 宸茬紪璇戝瓧鑺傜爜: .workbuddy/out/shaders/FlickV5_dxbc00..05.dxbc (SM4.0 DXBC)
//
//------------------------------------------------------------------------------
// 銆怱hader Graph 鍥炬暟鎹€戜笉瀛樺湪锛堝浘 JSON 鍦ㄦ墦鍖呮椂琚墺绂伙紝璇﹁ SkyArea.shader 澶存敞锛?
//
//------------------------------------------------------------------------------
// 銆愭覆鏌撶姸鎬?鈥斺€?m_ParsedForm.m_SubShaders[0].m_Passes[0].m_State 瀹炶瘉銆?
//   LightMode "Universal Forward"
//   ZWrite Off   ZTest LessEqual   Cull Back
//   Blend RGB : OneMinusDstColor / SrcAlphaSaturate
//   Blend A   : One             / SrcAlphaSaturate     鈫?棰勪箻 alpha 閫忔槑
//   SubShader tags: Queue=Transparent, ShaderGraphShader=true,
//                    ShaderGraphTargetId=UniversalUnlitSubTarget  鈫?绾?Unlit
//
//------------------------------------------------------------------------------
// 銆愬弬鏁版帴鍙?鈥斺€?姣嶇増榛樿 涓?鏉愯川銆孎lickV5銆嶅疄璇佸€煎畬鍏ㄤ竴鑷淬€?
//   _Base      Texture2D = "FlickNote-Base"   (tex0, 2048x128)
//   _Bar       Texture2D = "FlickNote-Bar"    (tex1, 64x128)
//   _Core      Texture2D = "FlickNote-Core"   (tex2, 2048x128)
//   _Scale     Float    = 1.0
//   _Opacity   Float    = 1.0
//   鈿?姣嶇増閲?*娌℃湁浠讳綍 Color 灞炴€?*銆俧lick 鐨勯鑹茶涔夌敱 C# 渚?
//     (MaterialPropertyBlock / Material.SetColor) 鍦ㄨ繍琛屾椂鎸夋墜鍔挎柟鍚戝啓鍏ワ細
//     褰撳墠鍥哄畾璇箟涓?arg4==4 鈫?缁胯壊鍚戝彸銆乤rg4==16 鈫?榛勮壊鍚戝乏
//     (瑙?out/in_falsus_skyarea_flick_round2.md 搂2.1)銆?
//     鍥犱负鍘?shader 娌℃湁棰滆壊灞炴€э紝棰滆壊鍙兘鏉ヨ嚜 **椤剁偣鑹?/ UV 缂栫爜 / 璐村浘鏈韩**銆?
//     鏈鍒绘柊澧?_Color锛堥粯璁ょ櫧锛変綔涓鸿繍琛屾椂瑕佸啓鐨勫叆鍙ｏ紝骞跺湪娉ㄩ噴閲岃鏄庤繖涓€鐐广€?
//
//------------------------------------------------------------------------------
// 銆愯创鍥惧疄璇?鈥斺€?宸插鍑哄埌 .workbuddy/out/shaders/tex/銆?
//   FlickNote-Base : 2048x128锛屾í鍚戜竴鏉″甫锛屼笂涓嬩袱绔湁娓愬彉锛屼腑娈佃繎涔庡叏榛?閫忔槑)
//   FlickNote-Bar  : 64x128  锛岀珫绐勶紝闈犳í鍚?1px 鐨勪寒绾匡紙鈫?鏄€屾潯銆嶄笉鏄摵婊★級
//   FlickNote-Core : 2048x128锛屼腑闂翠竴鏉￠珮浜姱绾?
//   鈫?涓夊紶鍥鹃兘鏄?**妯悜 U 閾烘弧銆乂 涓婁笅鍒嗗竷** 鐨勬潯鐘跺浘锛?
//     鎵€浠?V 鏂瑰悜涓嶈骞抽摵锛孶 鏂瑰悜 0..1 鐩存帴鐢ㄣ€?
//     杩欎笌 SkyArea 鐨?ARCHITECT-LINES/DOT-GRID锛堢珫闀裤€佹部 V 骞抽摵锛夋濂界浉鍙嶏紝
//     鏄湰宸ョ▼閲屼袱绫婚煶绗︽渶纭殑褰㈡€佸樊寮傘€?
//
//------------------------------------------------------------------------------
// 銆愯繕鍘熷害銆戜腑
//   瀹炶瘉閮ㄥ垎锛氬弬鏁板悕/绫诲瀷/榛樿鍊笺€佹贩鍚堟ā寮忋€佹繁搴?鍓旈櫎鐘舵€併€佽创鍥炬Ы涓庤创鍥惧疄鍥俱€?
//             椤剁偣杈撳叆璇箟(POSITION/NORMAL/TANGENT/TEXCOORD)銆佸崟 RT 杈撳嚭銆?
//   鎺ㄦ柇閮ㄥ垎锛氫笁寮犺创鍥剧殑鍚堟垚鏉冮噸銆乢Scale 鐨勪綔鐢ㄨ酱銆乫lick 鎵囧舰/姊舰鐨勫舰鐘跺嚱鏁般€?
//   鐞嗙敱锛氬師 shader 鍙湁 2 涓?float 鍙傛暟锛坃Scale/_Opacity锛夈€? 寮犵函鐏板害鏉＄姸璐村浘锛?
//         娌℃湁浠讳綍鍑犱綍鍙傛暟锛屽舰鐘跺嚑涔庡繀鐒舵槸**纭紪鐮佸湪鍥鹃噷鐨勫父閲?*锛?
//         鑰岃繖浜涘父閲忓凡缂栬瘧杩?DXBC 鍗虫椂鏁扳€斺€旀湰鏈烘病鏈?DXBC 鍙嶆眹缂栧櫒
//         (dxc/fxc/spirv-dis 鍧囦笉瀛樺湪)锛屾棤娉曢€愭潯杩樺師锛屾晠褰㈢姸涓烘帹鏂€?
//==============================================================================

Shader "InFalsus/Replica/FlickV5"
{
//------------------------------------------------------------------------------
// 銆?026-10-16 鏇存銆戞覆鏌撶姸鎬佺湡鍊煎凡浠?bundle 鍘熷鏁存暟閲嶆柊鏍稿畾
//   m_ParsedForm.m_SubShaders[0].m_Passes[0].m_State 閲屾瘡涓瓧娈甸兘鏄?{val, name}锛?
//     路 name == "<noninit>"  鈫?瀛楅潰閲忥紝鐪熷€煎氨鏄?val
//     路 name == "_ZTest" 绛? 鈫?**灞炴€у紩鐢?*锛岃繍琛屾湡鐢辨潗璐ㄥ彇鍊煎喅瀹氾紝鐪熷€煎彇鏉愯川
//   鈿?涔嬪墠鎶?{val:5} 璇戞垚 OneMinusDstColor 鏄?*閿欑殑**锛? = SrcAlpha锛?0 = OneMinusSrcColorAlpha銆?
//     5/10 鎵嶆槸鏍囧噯 Alpha 娣峰悎锛圫rcAlpha OneMinusSrcColorAlpha锛夈€?
//   鏈枃浠剁殑 Blend/BlendAlpha/ZTest/Cull/ZWrite 宸叉寜鐪熷€艰〃 out/shader_state_truth.json 淇銆?
//------------------------------------------------------------------------------

    Properties
    {
        [NoScaleOffset] _Base ("Base (鏉″甫搴?", 2D) = "white" {}
        [NoScaleOffset] _Bar  ("Bar  (寮曞鏉?", 2D) = "white" {}
        [NoScaleOffset] _Core ("Core (鑺嚎)",   2D) = "white" {}

        _Scale   ("Scale",   Float) = 1.0
        _Opacity ("Opacity", Float) = 1.0

        // ---- 澶嶅埢鏂板锛堝師 shader 鏃犻鑹插睘鎬э紝瑙佸ご娉級----
        [HDR]_Color ("Color (4=缁胯壊鍚戝彸 / 16=榛勮壊鍚戝乏)", Color) = (1,1,1,1)
        _CoreBoost  ("Core Boost (鑺嚎棰濆浜害, HDR 闈?Bloom)", Range(0,8)) = 1.6
        _BarRepeat  ("Bar Repeat (stripe count)", Range(1,32)) = 10.0
        _BarContrast("Bar Contrast (stripe strength)", Range(0,4)) = 2.6
        // 尾巴亮度：1 = 原始混色，>1 提亮尾巴（补偿 _Bar 混色与 _Color 相乘的压暗）
        _TailLift   ("Tail Lift (尾巴提亮)",   Range(1,2.5)) = 1.45
        // 尾巴不透明度：只作用于尾巴（V 较大的区域），不改变箭头
        _TailOpacity("Tail Opacity (尾巴不透明度)", Range(0.3,1)) = 1.0

        // 尾巴去染色程度：0 = 完全保留 _Color 染色，1 = 完全用原始色
        _TailDetint ("Tail Detint (尾巴去染色)", Range(0,1)) = 0.40
        // 条纹强度：0 = 无条纹，1 = 最强（补偿尾巴提亮造成的对比度下降）
        _BarStrength("Bar Strength (条纹强度)", Range(0,1)) = 0.55

        // Desaturation: 0 = original colour, 1 = fully gray.
        _Desaturate ("Desaturate", Range(0,1)) = 0.0
        // 1 = unchanged, <1 = calmer / less vivid (no whitening)
        _SaturationTrim ("Saturation Trim", Range(0,1)) = 0.72
        // Overall dim: 1 = original brightness, lower = darker.
        _OverallDim ("Overall Dim", Range(0.5,1.2)) = 0.86
        // HDR 发光强度：核心越过 Bloom 阈值的倍数（>1 才会产生光晕）
        _GlowPower  ("Glow Power (HDR glow strength)", Range(1,8)) = 1.8
// [Header(Shader Graph 琛ㄩ潰璁剧疆瀛樻牴 鈥斺€?涓嶅弬涓庤繍绠?]
        [HideInInspector] _Surface ("_Surface", Float) = 1.0
        [HideInInspector] _Blend   ("_Blend",   Float) = 0.0
        [HideInInspector] _AlphaClip ("_AlphaClip", Float) = 0.0
        [HideInInspector] _SrcBlend ("_SrcBlend", Float) = 1.0
        [HideInInspector] _DstBlend ("_DstBlend", Float) = 10.0
        [HideInInspector] _SrcBlendAlpha ("_SrcBlendAlpha", Float) = 1.0
        [HideInInspector] _DstBlendAlpha ("_DstBlendAlpha", Float) = 10.0
        [HideInInspector] _ZWrite   ("_ZWrite",   Float) = 0.0
        [HideInInspector] _ZTest    ("_ZTest",    Float) = 4.0
        [HideInInspector] _ZWriteControl ("_ZWriteControl", Float) = 0.0
        [HideInInspector] _Cull     ("_Cull",     Float) = 2.0
        [HideInInspector] _CastShadows ("_CastShadows", Float) = 0.0
        [HideInInspector] _AlphaToMask ("_AlphaToMask", Float) = 0.0
        [HideInInspector] _QueueOffset  ("_QueueOffset",  Float) = 0.0
        [HideInInspector] _QueueControl ("_QueueControl", Float) = -1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Universal Forward"
            Tags { "LightMode" = "UniversalForward" }

            // Output is premultiplied (IF_Premultiply), so the RGB blend factor must
            // be One, not SrcAlpha. Using SrcAlpha here multiplies by alpha twice
            // (rgb*a then *a again), which darkens the note and keeps HDR values
            // below the Bloom threshold.
            Blend One OneMinusSrcAlpha, One OneMinusSrcAlpha   // 鍘?BlendAlpha 鍚堝苟杩?Blend锛圫haderLab 鏃?BlendAlpha 鍛戒护锛?
            ZWrite     Off
            ZTest      LEqual
            Cull          Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex   IF_Vertex
            #pragma fragment IF_Fragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Infalsus_Common.hlsl"

            TEXTURE2D(_Base); SAMPLER(sampler_Base);
            TEXTURE2D(_Bar);  SAMPLER(sampler_Bar);
            TEXTURE2D(_Core); SAMPLER(sampler_Core);

            CBUFFER_START(UnityPerMaterial)
                float4 _Base_ST;
                float4 _Bar_ST;
                float4 _Core_ST;
                float4 _Color;        // 澶嶅埢鏂板
                float  _Scale;
                float  _Opacity;
                float  _CoreBoost;
                float  _BarRepeat;
                float  _BarContrast;
                float  _TailLift;
                float  _TailOpacity;
                float  _TailDetint;
                float  _BarStrength;
                float  _Desaturate;
                float  _SaturationTrim;
                float  _OverallDim;
                float  _GlowPower;
                float  _Surface, _Blend, _AlphaClip;
                float  _SrcBlend, _DstBlend, _SrcBlendAlpha, _DstBlendAlpha;
                float  _ZWrite, _ZTest, _ZWriteControl, _Cull;
                float  _CastShadows, _AlphaToMask, _QueueOffset, _QueueControl;
            CBUFFER_END
            #define FlickTailDetint _TailDetint


            //==========================================================================
            // FlickV5 fragment - reconstructed from the shipped DXBC byte code.
            //
            // The original pixel shader (Shader_Graphs_FlickV5.platform-4.program-1,
            // ps_4_0) was recovered instruction by instruction. The essential part
            // that produces the diagonal stripes is:
            //
            //   mul  r0.xy, v1.xyxx, cb1[4].xxxx                       // uv * _Scale
            //   mul  r0.xy, r0.xyxx, cb1[1].xyxx                       // * _Bar_ST.xy
            //   mad  r0.xy, r0.xyxx, l(32.0,0.0,0.0,0.0), cb1[1].zwzz  // U*32, V*0
            //   sample_b r0.xyzw, r0.xyxx, t0.xyzw, s1                 // _Bar, REPEAT
            //
            // Note the multiplier literal is (32, 0): U is replicated 32 times while
            // V collapses to _Bar_ST.w. _Bar is bound to the REPEAT sampler (s1) and
            // _Base/_Core to the CLAMP sampler (s0). Sampling _Bar with plain
            // uv.x (no x32) or with a clamp sampler destroys every stripe - the two
            // failure modes this project previously hit.
            //
            // Final composite (also straight from the asm):
            //   rgb = (bar.rgb*bar.a + base.rgb*(1-bar.a))*(1-core.a) + core.rgb*core.a
            //   a   = max(base.a, core.a) * _Opacity
            //==========================================================================

            // The stripes are *not* a screen-space or UV rotation: they emerge because
            // the _Bar texture is a 64px-wide vertical bar replicated 32x along U and
            // then cut by the plate's parallelogram silhouette. Keep the geometry as
            // the real mesh and the stripe slant follows automatically.
            float4 IF_Fragment(IF_Varyings i) : SV_Target
            {
                float2 uv = i.uv;

                // _Base/_Core are looked up with the raw mesh UV.
                // V saturation keeps the plate from sampling outside the bar band.
                float2 uvBase = float2(saturate(uv.x), saturate(uv.y));

                // ---- _Bar coordinate ----
                // The shipped asm multiplies U by a constant (32) and V by 0. That
                // literal is tuned for the original's UV range; applied to this
                // replica's full 0..1 plate UV it yields ~32 repeats, which is ~6x
                // denser than the original screenshots show (~5-6 diagonal bands).
                //
                // _Bar is a 64x128 texture holding exactly ONE stripe unit, so the
                // repeat count IS the visible stripe count. _BarRepeat therefore
                // controls stripe density directly: ~5.5 matches the reference.
                // V stays at _Bar_ST.w because the original multiplies V by zero.
                float  barU = uv.x * _Scale * _BarRepeat + _Bar_ST.z;
                float  barV = uv.y * _Scale * 0.0        + _Bar_ST.w;
                float2 uvBar = float2(barU, barV);

                // _Bar must use a REPEAT sampler. sampler_Bar is declared through
                // SAMPLER(), so Unity binds it per the texture's wrap mode. The
                // texture is forced to Repeat in the import settings; if it is ever
                // switched to Clamp the stripes vanish.
                float4 barC  = SAMPLE_TEXTURE2D(_Bar,  sampler_Bar,  uvBar);
                // _Bar's own alpha only spans ~0.44..0.89, which blends too gently
                // against _Base. Amplify its deviation around 0.66 so the diagonal
                // bands read as clearly as in the original screenshots.
                barC.a = saturate(0.66 + (barC.a - 0.66) * _BarContrast);
                float4 baseC = SAMPLE_TEXTURE2D(_Base, sampler_Base, uvBase);
                float4 coreC = SAMPLE_TEXTURE2D(_Core, sampler_Core, uvBase);

                // ---- original blend, rebuilt from the asm ----
                float3 rgb = (barC.rgb * barC.a + baseC.rgb * (1.0 - barC.a)) * (1.0 - coreC.a)
                           + coreC.rgb * coreC.a;
                float  a   = max(baseC.a, coreC.a) * _Opacity;

                // The replica keeps a baked-in albedo tint because this project writes
                // colour from C# (MaterialPropertyBlock) instead of using three
                // pre-coloured texture sets. It does NOT alter the stripe pattern.
                // Raw (untinted) composite, kept so the tail can recover luminance.
                float3 rawRgb = rgb;

                rgb *= _Color.rgb;

                // The tail (large u) reads darker than the head: _Color's blue channel is
                // only 0.18, so the tint crushes luminance and a plain multiply by
                // _TailLift cannot restore the lost channel balance.
                // Two-stage lift for the tail only:
                //   1) blend back toward the untinted colour (recovers brightness + balance)
                //   2) multiply by _TailLift for the final push
                float tailW = saturate((uv.x - 0.12) / 0.45);
                rgb = lerp(rgb, rawRgb, tailW * FlickTailDetint);
                rgb *= lerp(1.0, _TailLift, tailW);

                // Re-apply the stripe modulation AFTER brightening. The lift/detint above
                // flatten the bar's alpha contrast, which made the bands look faint (and
                // therefore "sparser") than the reference. This restores a clear dark
                // band on top of the brightened tail.
                // barC.a lives in ~0.443..0.890; normalise to 0..1 so the modulation is
                // well defined and never crushes the tail to black.
                float bandT = saturate((barC.a - 0.44) / 0.45);          // 0 = dark band, 1 = bright
                float bandMask = lerp(1.0, 0.55 + 0.45 * bandT, _BarStrength);
                // strength 0 -> flat (no stripes); strength 1 -> up to 45% darkening
                rgb *= bandMask;

                // ---- dull it down WITHOUT going white ----
                // Blending toward luma lifts a warm yellow's blue channel ~9x at 0.45,
                // which reads as washed-out white, not gray. Instead reduce VALUE while
                // keeping the hue ratio: scale the colour down and let desaturation stay
                // very small (or zero). _Dullness lowers brightness; _Desaturate is kept
                // only for a slight neutralisation.
                // Value-only dimming: multiply every channel by the same factor so the
                // hue ratio is preserved. A luma blend is NOT used by default because the
                // tinted colour's blue channel is ~0.0049 and any blend raises it sharply
                // (0.10 already triples it), which reads as white.
                rgb *= _OverallDim;
                // Reduce overly vivid colour without brightening.
                // Blending toward LUMA raises this tint's blue channel (~0.005) several
                // times over and reads as white. Blending toward the channel MEAN instead
                // lowers both the peak and the extremes, so vividness drops while overall
                // value also drops slightly -- gray, not white.
                if (_SaturationTrim < 1.0)
                {
                    float meanChannel = (rgb.r + rgb.g + rgb.b) / 3.0;
                    rgb = lerp(float3(meanChannel, meanChannel, meanChannel), rgb, _SaturationTrim);
                }
                // Optional manual neutralisation, kept for a deliberately gray look.
                if (_Desaturate > 0.0)
                {
                    float luma = dot(rgb, float3(0.2126, 0.7152, 0.0722));
                    rgb = lerp(rgb, float3(luma, luma, luma), _Desaturate);
                }

                // Keep the head fully opaque; only soften the tail if asked.
                a *= lerp(1.0, _TailOpacity, tailW);

                // ---- HDR glow ----
                // The original relies on Bloom (threshold 1.5, intensity 2.0). For that to
                // trigger, some pixels must exceed the threshold in LINEAR space. _Color's
                // blue channel is only 0.18, so the tinted product alone never gets there.
                // Push the core (the brightest part of the texture) up in HDR instead of
                // letting it clamp at 1.0, so the bright-pass picks it up.
                // NOTE: the HDR push itself must happen AFTER premultiplication (see the
                // return statement). Adding it here would be scaled down by alpha.
                float coreHot = coreC.a * coreC.a;                 // thin hot line only
                // keep the old subtle boost for parity with the previous look
                rgb += coreHot * (_CoreBoost - 1.0f) * 0.35f * _Color.rgb;

                // No hard alpha cut: a threshold like 0.0035 turned every semi-transparent
                // edge pixel into a hard stair-step, which is the jagged rim on the plate.
                // Blend handles the fade; discard only fully transparent fragments.
                if (a <= 0.0005) discard;

                // Premultiplied base colour, then the HDR glow added OUTSIDE the
                // premultiply so the bright-pass actually sees a value above threshold.
                float4 premul = IF_Premultiply(float4(rgb, a));
                premul.rgb += coreHot * (_GlowPower - 1.0f) * 1.6f;
                return premul;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask 0
            Cull Back
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Infalsus_Common.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Base_ST, _Bar_ST, _Core_ST;
                float4 _Color;
                float  _Scale, _Opacity, _CoreBoost;
                float  _BarRepeat;
                float  _BarContrast;
                float  _TailLift;
                float  _TailOpacity;
                float  _TailDetint;
                float  _BarStrength;
                float  _Desaturate;
                float  _SaturationTrim;
                float  _OverallDim;
                float  _GlowPower;
                float  _Surface, _Blend, _AlphaClip;
                float  _SrcBlend, _DstBlend, _SrcBlendAlpha, _DstBlendAlpha;
                float  _ZWrite, _ZTest, _ZWriteControl, _Cull;
                float  _CastShadows, _AlphaToMask, _QueueOffset, _QueueControl;
            CBUFFER_END
            struct DVary { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            DVary DepthVert(float3 p : POSITION, float2 uv : TEXCOORD0)
            {
                DVary o; o.positionCS = TransformObjectToHClip(p); o.uv = uv; return o;
            }
            half4 DepthFrag(DVary i) : SV_Target
            {
                float v = (i.uv.y - 0.5) / max(_Scale, 1e-4) + 0.5;
                float u = saturate(i.uv.x);
                float edge = saturate(v);
                float taper = lerp(0.92, 0.035, smoothstep(0.0, 1.0, u));
                float soft = max(fwidth(edge) * 1.5, 0.0015);
                float mask = 1.0 - smoothstep(
                    taper - soft,
                    taper + soft,
                    edge);
                clip(mask * smoothstep(0.0, 0.035, 1.0 - u) - 0.5);
                return 0;
            }
            ENDHLSL
        }
    }

    Fallback "Hidden/Shader Graph/FallbackError"
    CustomEditor "UnityEditor.ShaderGraph.GenericShaderGraphMaterialGUI"
}
