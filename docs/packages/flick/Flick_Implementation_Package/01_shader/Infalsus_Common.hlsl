//==============================================================================
// Infalsus_Common.hlsl
// In Falsus (Unity 6000.3.9f1 / URP) — 复刻用共用 HLSL 工具函数
//
// 来源 bundle:
//   infalsus_Data/StreamingAssets/aa/StandaloneWindows64/
//     8719fa86ae7f9dbe9b11654f9dc69197.bundle  (shader 母版包, 75 Shader / 57 Material)
//     34d4e589e2c4061d9fc85c81c1dd1c2d.bundle  (gameplay 包, 282 Material)
// 提取工具: .workbuddy/tools/shader_final_extract.py
// 原始数据: .workbuddy/out/shaders_authoritative.json
//
// 【重要事实】Shader Graph 的图数据(节点/连线 JSON)在打包时已被剥离，
// AssetBundle 里只剩编译后的 DXBC 字节码 + m_ParsedForm 属性表 + 渲染状态。
// 因此本文件的函数全部是「按参数语义 + 贴图外观 + 渲染状态实证复刻」，
// 不是从图数据逐节点翻译过来的。每处推断都在注释里标了 [推断] / [实证]。
//==============================================================================
#ifndef INFALSUS_COMMON_INCLUDED
#define INFALSUS_COMMON_INCLUDED

// Unity 6 的线性色彩工作流：URP 默认 Linear 色彩空间，
// 属性里那些 >1 的 HDR 值(例如 _EdgeCenterColor.b = 3.6057)必须原样送进 HDR 目标。
// 因此下面所有颜色运算一律在线性空间做，**不做任何 gamma 反变换**。[实证]

//------------------------------------------------------------------------------
// 轨道坐标 (track space)
//------------------------------------------------------------------------------
// [实证] _TrackVisualLength 默认 = 20（SkyArea 材质实证值，母版默认与材质一致）。
//        语义：整条轨道在「视觉上可见」的长度 = 20 个世界单位。
//        skyarea / flick 共用一套「全场 [0,1] 分数刻度」(见 out/in_falsus_skyarea_flick_round2.md §1.1)，
//        所以 _VisualStart/_VisualLength 都是以这个 20 为尺度的轨道坐标。
//
// tNorm : 当前片元在可见轨道窗口里的归一化位置 [0,1]
// tAbs  : 以世界单位计的轨道位置（0 = 轨道起点，_TrackVisualLength = 轨道终点）
float IF_TrackAbs(float tNorm, float trackVisualLength) { return tNorm * trackVisualLength; }

// [推断] 把「本音符沿轨道的区间」换算成可见窗口内的归一化区间。
// 贴图 V 方向用这个区间做锚定，可以让 arch/dot 纹理在相邻音符之间连续、不被拉伸。
float2 IF_TrackNormRange(float visualStart, float visualLength, float trackVisualLength)
{
    float inv = 1.0 / max(trackVisualLength, 1e-5);
    return float2(visualStart * inv, (visualStart + visualLength) * inv);
}

//------------------------------------------------------------------------------
// 端头收窄 (group head/tail taper)
//------------------------------------------------------------------------------
// [实证] 材质里有 _IsFirstOfGroup / _IsLastOfGroup 两个 0/1 float(母版默认 0，
//        材质实证值也是 0，由游戏在播放时逐音符写入)。
// [推断] 语义：这条 skyarea 是不是「同组连续段」的首端 / 末端。
//        是首端 → 靠近玩家那一头收窄成尖；是末端 → 远端收窄成尖；
//        两端都置1(独立成段) → 两头都收窄(菱形/纺锤形)；都为0 → 矩形平头。
//        这与 skyarea 谱面里「满宽 → 细线」连续插值的观感一致(见 round2 报告 §1.1 第2条)。
//
// dt   : 当前片元到该端头的归一化距离 [0,1]（0 = 就在端头上）
// capL : 收窄段占整条音符长度的比例 [0,1]
// isCap: 该端是否收窄
float IF_EndTaper(float dt, float capL, float isCap)
{
    float t = saturate(dt / max(capL, 1e-4));
    // smoothstep 让尖端不是硬切，而是有个圆润的收口
    return lerp(1.0, t * t * (3.0 - 2.0 * t), saturate(isCap));
}

//------------------------------------------------------------------------------
// 边缘发光 (edge glow) —— SkyArea 的招牌效果
//------------------------------------------------------------------------------
// [实证] _EdgeCenterColor = (1.1556, 0.9816, 3.6057, 1.0)  ← HDR，蓝紫色，b 通道 3.6
//        _EdgeGlowColor   = (0.3647, 0.1961, 0.8510, 0.0)  ← 紫色外辉，a=0(默认不参与)
//        两者都由材质实证，且蓝通道远超 1 → **URP 必须开 HDR + Bloom**，
//        否则 _EdgeCenterColor 会被 clamp 成白色，边缘线失去紫色调。
//
// dEdge  : 到最近侧边的归一化距离（0 = 在边上，1 = 在中线）
// glowSz : 发光宽度倍率（材质实证 _EdgeGlowSize = 1.0；_Width = 0.5）
// 返回 x = 芯(窄, 走 HDR 色), y = 辉(宽, 走柔色)
float2 IF_EdgeGlow(float dEdge, float coreW, float glowSz)
{
    float w = max(glowSz, 1e-4);
    float core = saturate(1.0 - dEdge / max(coreW * w, 1e-4));
    float glow = saturate(1.0 - dEdge / max(coreW * w * 6.0, 1e-4));
    // 芯用平方压得更紧，辉用三次拖得更长
    return float2(core * core, glow * glow * glow);
}

//------------------------------------------------------------------------------
// 「缺失段」裁切 (missing range)
//------------------------------------------------------------------------------
// [实证] _MissingRange 是 Vector4，母版默认与材质实证值都是 (0, 1, 0, 0)。
// [推断] 语义：前两个分量是沿轨道方向的归一化区间 [x, y]，
//        第三分量是「是否启用」的开关(0 = 不启用)。
//        默认 (0,1,0,0) → 开关为 0 → 什么都不裁，等价于整条实心。
//        谱面里 skyarea 有「塌缩成细线 / 满宽」的连续插值(见 round2 §1.1)，
//        这段区间很可能就是被挖空的那一段，用 0/1 边写可表达单端截断，
//        写成 (0.5, 0.5) 则退化成不裁，语义自洽。
float IF_MissingRangeCut(float t, float4 missingRange)
{
    if (missingRange.z < 0.5) return 1.0;               // 未启用
    float a = smoothstep(missingRange.x, missingRange.x + 1e-3, t);
    float b = 1.0 - smoothstep(missingRange.y - 1e-3, missingRange.y, t);
    return a * b;
}

//------------------------------------------------------------------------------
// 抗锯齿化的「在宽度内」软掩码
//------------------------------------------------------------------------------
// [推断] SkyArea 是很细的带状物(塌缩态宽度可跨到 0)，硬边会严重闪烁，
//        所以所有横向裁切都用 fwidth 做屏幕空间软过渡，而不是 step。
//        用 fwidth 而不是固定宽度，才能在近处/远处自动变宽变窄。
float IF_SoftBand(float signedDist, float softnessScale = 1.0)
{
    float w = max(fwidth(signedDist) * 1.5, 1e-5) * softnessScale;
    return saturate(0.5 - signedDist / (2.0 * w));
}

//------------------------------------------------------------------------------
// HDR 预乘 alpha 输出
//------------------------------------------------------------------------------
// [实证] SkyArea / FlickV5 / R03FlickNoteYellow 的 pass0 混合模式是
//        Blend OneMinusDstColor SrcAlphaSaturate  (rgb)
//        Blend One            SrcAlphaSaturate  (a)
//        这就是 Unity Shader Graph 里 Surface=Transparent + 「Premultiply」混合预设，
//        语义是**输出必须已经是预乘 alpha 的**。所以下面统一走预乘输出。
//        如果你的工程用的是普通 Alpha 混合，把 IF_Premultiply 置 0 即可。
#ifndef INFALSUS_PREMULTIPLY
#define INFALSUS_PREMULTIPLY 1
#endif

float4 IF_Premultiply(float4 c)
{
#if INFALSUS_PREMULTIPLY
    return float4(c.rgb * c.a, c.a);
#else
    return c;
#endif
}

//------------------------------------------------------------------------------
// 顶点阶段通用：object -> clip，并输出轨道/带宽坐标
//------------------------------------------------------------------------------
// [实证] 这批 shader 全部是 Shader Graph 的 UniversalUnlitSubTarget
//        (SubShader tags: ShaderGraphShader=true, ShaderGraphTargetId=UniversalUnlitSubTarget,
//         RenderPipeline=UniversalPipeline)，即**纯 unlit、无光照**。
//        所以顶点输出只需要裁剪空间位置 + 少量 uv，不需要法线/切线。
//
// 约定(复刻方需保证网格满足)：网格是一块单位面片，uv0 ∈ [0,1]²，
//   uv.x → 横向(横跨轨道宽度)，uv.y → 纵向(沿轨道方向 z)。
//   如果你的网格 uv 布局不同，只需改 IF_Vertex 里的赋值即可。
struct IF_Varyings
{
    float4 positionCS : SV_POSITION;
    float2 uv         : TEXCOORD0;
    float3 posWS      : TEXCOORD1;
    float4 screenPos  : TEXCOORD2;
};

// [实证] DXBC 的 ISGN 签名里，字符串表实证出现的顶点输入语义只有
//        POSITION / NORMAL / TANGENT / TEXCOORD —— 正是 URP 的 Attributes 结构。
//        → 这些 shader 用不到 UV 之外的自定义顶点流，TEXCOORD0 足够。
//        → 参数必须带语义，否则 ShaderLab 无法绑定顶点流（已踩过坑）。
void IF_Vertex(float3 posOS : POSITION, float2 uv : TEXCOORD0, out IF_Varyings o)
{
    VertexPositionInputs vpi = GetVertexPositionInputs(posOS);
    o.positionCS = vpi.positionCS;
    o.uv    = uv;
    o.posWS = vpi.positionWS;
    o.screenPos = ComputeScreenPos(vpi.positionCS);
}

#endif // INFALSUS_COMMON_INCLUDED