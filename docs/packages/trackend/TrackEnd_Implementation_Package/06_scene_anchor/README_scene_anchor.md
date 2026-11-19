# TrackEnd 场景锚点（06）

TrackEnd 的输出 Quad 不是场景里的预制体，而是 **运行时由
`FalsumTrackEndComposer` 用代码生成**的。它唯一依赖的场景对象是锚点
`PLANE_END`。

## 1. 硬编码路径

`Assets/InFalsus/Gameplay/Runtime/SceneBinding/GameplaySceneBinding.cs:52-54`：

```csharp
public const string TrackEndAnchorPath =
    "GameCamera/master_scene_v10/MasterScenev10/JUDGEMENT_LINE/" +
    "JUDGE_SKY/v5-3R/Plane/PLANE_END";
```

## 2. 场景里实际有两个 `PLANE_END`

`Assets/InFalsus/Gameplay/Recovered/Scenes/GameplayRuntime.unity` 里存在
**两个** 同名节点，只有 `v5-3R` 那一支会被绑定：

| 完整路径 | GameObject fileID | Transform fileID | Layer | 是否被绑定 |
|---|---|---|---|---|
| `GameCamera/master_scene_v10/MasterScenev10/JUDGEMENT_LINE/JUDGE_SKY/v5-3/Plane/PLANE_END` | 247 | 4635 | 7 | 否 |
| `GameCamera/master_scene_v10/MasterScenev10/JUDGEMENT_LINE/JUDGE_SKY/v5-3R/Plane/PLANE_END` | 782 | 4062 | 7 | **是** |

两个节点本地变换完全相同：

```yaml
m_LocalRotation: {x: 0, y: -2.1175824e-22, z: -0, w: 1}
m_LocalPosition: {x: -0.7445592, y: 0.11485599, z: 7.4505804e-11}
m_LocalScale: {x: 1, y: 1, z: 1}
```

但父级 `Plane` 的 X 不同（`v5-3` 是 `-0.791`，`v5-3R` 是 `-0.789`），
所以世界坐标也略有差异。Composer 只用锚点的 **世界位置**做视口投影，
不使用旋转与缩放，因此这个差异只体现在输出 Quad 的水平定位上。

## 3. 路径链（v5-3R 支）

```text
GameCamera                 localPos (0, 0, -7.0200005)
└─ master_scene_v10        localPos (0, 0, 0)
   └─ MasterScenev10       localPos (0, 0.6152811, 9.411812)
      └─ JUDGEMENT_LINE    localPos (0.002000305, -0.6152811, -10.911811)
         └─ JUDGE_SKY      localPos (1.8369701e-16, 0.46980885, 0)
            └─ v5-3R       localPos (-0, 0, 0)
               └─ Plane    localPos (-0.789, -0.04234165, 0.0008153589)
                  └─ PLANE_END  localPos (-0.7445592, 0.11485599, 7.4505804e-11)
```

## 4. 缺失时的行为

`FalsumBackgroundPresenter.Initialize`（`:133`、`:141-154`）：

```csharp
_trackEndAnchor = binding?.TrackEndAnchor;
...
if (_trackEndAnchor != null) {
    _trackEndComposer = gameObject.AddComponent<FalsumTrackEndComposer>();
    _trackEndComposer.Initialize(_trackEndAnchor);
} else {
    LastError = "Track-end anchor is missing.";
    Debug.LogError("[Falsum] " + LastError);
}
```

- 锚点缺失：**报错但不崩溃**，`TrackEndComposer` 保持 `null`，
  背景 Presenter 其余功能正常，只是没有 TrackEnd 菱形层。
- 主相机缺失 / 相机为正交：`UpdateOutputPlacement` 直接把输出 Quad
  设为 inactive（`FalsumTrackEndComposer.cs:788-792`）。
- 锚点在相机后方：同样隐藏（`:796-800`）。

## 5. 复刻时的最小要求

如果要在别的工程里复刻，锚点只要满足：

1. 是场景中一个存在的 `Transform`，且能用固定路径找到。
2. 位于判定线末端、轨道尽头（本例本地 Z ≈ 0 但父链把它放到轨道末端深度）。
3. Layer 与输出 Quad 一致即可（Composer 用
   `_trackEndAnchor.gameObject.layer` 给输出对象，`:706`）。
4. 遮罩用的两个 MeshRenderer 固定使用 Layer 31（`HiddenMaskLayer`），
   由 RendererFeature 手动 `DrawRenderer` 绘制，**不参与常规相机渲染**。
   因此工程里 **不要求** Layer 31 被任何相机 Culling Mask 包含。
