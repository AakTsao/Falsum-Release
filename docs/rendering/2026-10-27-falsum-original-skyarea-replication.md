# Falsum Original SkyArea Replication

Date: 2026-10-27

## Scope

This change replaces the approximate Falsum SkyArea presentation with
the rendering chain recovered from the shipped In Falsus build.

Source analysis:

- `D:\AakTsao WorkSpace\In Falsus Demo\analysis\reports\SkyArea_Unity_Full_Replication.md`
- `D:\AakTsao WorkSpace\In Falsus Demo\analysis\reconstructed`

Development copy:

- `D:\AakTsao WorkSpace\In Falsus Demo\backups\InFalsus-Gameplay-SkyArea-20261027`
- Git branch: `feature/original-skyarea`

The original game project directory was not modified.

## Runtime Structure

```text
JUDGE_SKY
├─ PhaseA_SkyArea_Group_N
│  ├─ MeshRenderer                  SkyArea body
│  ├─ GroundShadow                  same mesh, queue 3006
│  └─ PhaseA_SkyArea_Prompt_N
│     ├─ SquareBracketLeft          original BRACKET mesh
│     └─ SquareBracketRight         Y-rotated 180 degrees
├─ SkyJudgementLineGlow             quad, queue 3011
└─ SkyJudgementLineMaskResources    hidden 8 x 90 triangle mesh
```

## Group And Geometry Reconstruction

- SkyAreas are processed in source-file order. Each segment is compared
  only with the immediately preceding SkyArea segment.
- A segment joins the preceding group when the preceding end and current
  start differ by at most 2 ms. Horizontal overlap is not required.
- A disconnected SkyArea consumes one shared chart-object ordinal.
  Ground objects also consume ordinals, matching the shipped parser.
- First/last group rendering is derived from the resulting GroupId,
  rather than re-running an independent overlap heuristic.
- Each body segment is sampled every 5 ms:
  `rowCount = floor(durationMs / 5) + 1`.
- Each row emits one quad with four vertices:
  `left(t0), left(t1), right(t1), right(t0)`.
- Indices are `0,1,3, 3,1,2`.
- UV0 stores normalized track X and `t`.
- UV1 stores side and visual distance along the track.
- UV2 stores current width and segment visual length.
- UV3 stores start/end overlap ranges for seam removal.
- UV4 stores first/last segment flags and the segment visual start.
- Left and right boundaries independently use linear, sine-out, or
  sine-in easing.
- Seam suppression requires the preceding end and current start to be
  equal within `0.0001 ms`. The 2 ms grouping tolerance does not make a
  1-2 ms gap seam-connected.

The single-mesh group representation is a Falsum integration choice.
The per-segment inputs and fragment math match the shipped multi-mesh
SkyArea implementation.

## Body Shader

`Assets/InFalsus/Recovered/b3.shader`

- Recovers the 24-bit integer hash gradient noise.
- Samples `ARCHITECT-LINES` and `DOT-GRID` alpha channels.
- Reconstructs the original texture-space travel and slow phase drift.
- Reconstructs `_MissingRange` group seam suppression.
- Reconstructs the internal procedural outline, depth boost, HDR edge
  colors, linear-to-sRGB conversion, and 2x saturation around luma.
- Uses original default colors and queue 3010.
- `_VisualStart` is the segment's source-time travel distance and
  `_VisualChartProgress` is the current chart travel distance, so the V
  phase advances over time.
- `ARCHITECT-LINES` and `DOT-GRID` use Repeat wrapping. Clamp wrapping
  pins the accumulated V phase to the texture edge and creates the
  incorrect long vertical-stripe appearance.
- Falsum uses `_Color.a = 0.5` for the center body fill, halving its
  opacity relative to the original alpha of 1.0. The independently
  rendered edge center and edge glow colors are unchanged.

## Entry Brackets

- Mesh: original `BRACKET.obj`, loaded through its imported
  `MeshFilter`.
- Texture: original `SquareBracket-Diffuse.png`.
- Two instances are placed at the first segment's start boundaries.
- The right marker is rotated 180 degrees around Y.
- Brackets appear only while the group start is more than 5 ms away.
- Render queue is 3012.

## Ground Shadow

- The shadow renderer shares the exact body Mesh.
- Local offset is `+0.005` on Y.
- Shader returns only the material color.
- Color is `(0.1415094, 0.1415094, 0.1415094, 0.2941177)`.
- Queue is 3006, ZTest is Always, ZWrite is Off.
- No shadow map, projector, or depth sampling is used.

## Judgement Line Triangles

`SkyJudgementLineEffect` recreates the shipped CPU and GPU data flow:

- Mesh: 8 rows x 90 columns, 181 triangles per row, 1448 triangles.
- Every triangle owns three unique vertices, so `SV_VertexID / 3`
  equals the original triangle id.
- Four cycled structured buffers prevent CPU writes from overwriting
  the buffer currently consumed by the GPU.
- The absolute monotonic Stopwatch clock drives Perlin flashes.
- Row distance weakens outer rows.
- Active range triangles decay at 2.5x; out-of-range triangles decay at
  5x.
- A narrow range enters the all-triangles fast-decay branch.

The RenderGraph pass executes after opaques:

1. Draw the triangle mesh to the full mask.
2. Horizontal and vertical five-tap blur into `W x H/4`.
3. Two downsample and two upsample stages.
4. Final vertical blur into the second `W x H/4` texture.

The blur radius is `0.0001`, and the weights are:

```text
0.227027, 0.316216, 0.316216, 0.070270, 0.070270
```

## Judgement Line Composition

`Assets/InFalsus/Recovered/a7.shader`

- Samples the filtered and blurred triangle masks.
- Uses `pow(max(grid, blurred), 0.3)` and smoothstep.
- Uses the original analytic line SDF with constants 40, 2, 16.8, and
  0.1.
- Uses `_TriangleBoundary = 0.95` for the white fringe.
- Uses original intensity thresholds 0.450004, 0.638239, color ramp, and
  linear-to-sRGB conversion.
- Uses the 109 rad/s and 34 rad/s dual-sine pulse.
- Fades linearly over 0.3 seconds after the active SkyArea ends.

## Resolution Policy

The shipped full mask uses the camera width divided by 3840 and a
2048 reference size. That formula is preserved above 1080p.

At 1152 x 720, the exact formula created a 341 x 341 mask in the
batch capture path and made the triangles visibly blocky. The runtime
now clamps the mask scale to 0.75, producing 1536 x 1536 at low
resolutions while still producing 2048 at 4K. Geometry, triangle count,
opacity timing, blur kernel, colors, and composition math are unchanged.

## Diagnostic Capture

`GameplayPlayCapture` now:

- Preserves the parsed source chart in play-mode capture.
- Starts the diagnostic gameplay clock.
- Resizes the triangle mask to the requested capture width before
  rendering.
- Supports `INFALSUS_CAPTURE_WIDTH` and
  `INFALSUS_CAPTURE_HEIGHT`.
- Optionally writes the filtered triangle mask.

## Verification

Unity batch self-check:

```text
[Falsum] Self check passed: 25 checks.
```

Runtime capture:

- 1152 x 720, active SkyArea at 0.9 s and 6.2 s.
- 1920 x 1080, active SkyArea at 0.9 s and 6.2 s.
- Triangle mask is 1536 x 1536 in both verification resolutions.
- Body texture, group entry brackets, group seams, ground shadow,
  judgement glow, and randomized triangles are visible.

Screenshots:

- `Diagnostics/Screenshots/skyarea_final_1152_0.900.png`
- `Diagnostics/Screenshots/skyarea_final_1152_6.200.png`
- `Diagnostics/Screenshots/skyarea_final_1152_sky_mask.png`
- `Diagnostics/Screenshots/skyarea_crisp1536_0.900.png`
- `Diagnostics/Screenshots/skyarea_crisp1536_6.200.png`
- `Diagnostics/Screenshots/skyarea_crisp1536_sky_mask.png`

