# FALSUM GRID Ruler Design

## Scope

Replace the mode plane's full-height grid lines with a horizontal **ruler
bar** that renders like measure lines, add the left-side info box with
`GRID` / start-cell / end-cell readouts, and make the grid denominator a
preference-owned sequence stepped by `-` / `=`.

In scope: Flick placement, SkyArea placement, middle-edit, and anchor
lateral drags in the perspective 3D view. Out of scope: the orthographic
2D view keeps its current plane ticks (user decision), and chart/note
serialization is unchanged.

## Current State

| Concern | Today |
|---|---|
| Mode plane | `FalsumModePlane` world-space uGUI canvas: face, 5 px border, `divisions + 1` full-height vertical `Image` lines, 4 captions |
| Captions | World `Text` drives layout only; pixels come from `FalsumWorldTextOverlay` (screen space) |
| Measure lines | `FalsumEditorWorld.BuildGrid` → `AddScreenSpaceLineQuad` + `Falsum/Editor/Grid Line`, pixel width, glow 2.8×, queue 3030 |
| Flick / SkyArea denominator | `_flickDenominator = 24`, `-`/`=` walk a hardcoded `12 → 24 → 36 → 48` ladder |
| Middle-edit grid | `_middleEdit.Grid`, `-`/`=` double/halve over `1..192` (a second, unrelated rule) |
| Persistence | None: neither value is a preference |
| Anchor lateral drag | Shows **no** mode plane: `TryGetMiddlePlaneState` reads `_middleEdit`, which anchor drags never set |

Two live defects this design also fixes:

1. `FalsumPreferencesMigration.LegacyPreferencesDto` has no `Grid` member
   (`:101-113`), so `Build` always replaces the grid group with a fresh
   default (`:307`). Today `LineColors` / `LineWidthScale` are silently
   lost on every reload, and any new field in that group would inherit it.
2. The `-`/`=` keys are not routed through `FalsumInputAction`, and
   `falsum.modePlane.gridAdjustHint` ("`[-] / [=]`") mislabels them. Keys
   stay hardcoded (unchanged behaviour); only the label text is corrected.

## Architecture

The ruler splits along the existing seam: **geometry in the 3D world,
chrome and text in the mode plane**.

- **Geometry** — `FalsumEditorWorld` owns a new `MeshBuffer _rulerMesh`
  (queue **3032**, between the grid at 3030 and `LineMaterial` at 3035),
  created through `CreateMesh(name, queue, useVertexColorLineShader: true)`
  so it picks up the *same* `FalsumEditorGridLine` material as measure
  lines. Every stroke is an `AddScreenSpaceLineQuad` call, so width is
  screen pixels extruded from UV.y exactly like `BuildGrid` (`:4673-4715`).
- **Chrome** — `FalsumModePlane` keeps its face and 4 border parts, deletes
  `_gridLines[]` / `SetGridLines` / `MaxGridCells`, and gains one
  `_infoBox` `Image` plus three caption rows.
- **Text** — the info box rows are world `Text` instances used only as
  layout drivers, exactly like the existing captions; pixels are drawn by
  `FalsumWorldTextOverlay.Place`, which divides pixel sizes by
  `CanvasScaleFactor`. This is the same path time-plane text already uses,
  so the readouts stay legible at any depth and `DebugOverlayAlignment`
  keeps working.

Data flow per frame, unchanged in shape:

```
FalsumChartEditor.UpdateOverlays
  → FalsumModePlane.SetState(kind, prompt, divisions, focusLine, …, startCell, endCell)
  → FalsumEditorWorld.Refresh(…, FalsumRulerState ruler, …)
       → BuildRuler(nowMs, timePlaneMs, ruler)
```

`FalsumRulerState` is a small struct that **replaces** the existing
`snapLine` / `snapDivisions` / `snapColor` arguments of
`FalsumEditorWorld.Refresh`. The struct is declared in
`FalsumEditorWorld.cs` (not in Core) because it carries a `UnityEngine.Color`;
the pure tier/step math it depends on lives in `FalsumBeatGrid` so the
offline self-check can assert it without a scene. BuildSnapArrow reads the
same struct, so this is a net parameter reduction:

```csharp
public struct FalsumRulerState
{
    public bool Visible;
    public int Divisions;        // 1..192, 0 == hidden
    public int FocusLine;        // -1 == no focus tick
    public float StartNormalized; // NaN == no start marker
    public float EndNormalized;   // NaN == no end marker
    public Color Color;
}
```

The 2D world keeps its own `(flickDivision, snapLine, snapDivisions,
snapColor)` arguments and is not touched.

## Ruler Geometry

Sits in the time plane's slice, `z = _lineLocal.z + NoteZAt(timePlaneMs,
nowMs)`, spanning `-TrackHalfWidth..+TrackHalfWidth`. Baseline at
`_floorY + PlaneHeight * 0.34`, ticks rising from it, lengths as fractions
of `ModePlaneWorldHeight()` so the ruler scales with the plane:

| Element | Length | Core width | Glow |
|---|---|---|---|
| Baseline | full span | 2.2 px | 2.8× |
| Major tick | 0.12 × height | 2.6 px | 2.8× |
| Mid tick | 0.085 × height | 2.2 px | 2.8× |
| Short tick | 0.055 × height | 1.8 px | 2.8× |
| Start marker | diamond, 0.05 × width | — | — |
| End marker | square, 0.045 × width | — | — |
| Selection band | start..end, full height | — | alpha 0.10 |

Every core width passes through the same
`_grid.LineWidthScale * max(0.5, Screen.height / 1080)` factor `BuildGrid`
uses (`:1407-1412`), and each tick emits glow-then-core like measure lines
(`:1413-1430`).

### Tick tiers

Integer-exact, no float tolerance — tier is decided by divisibility:

- `i * 4 % divisions == 0` → major
- else `i * 8 % divisions == 0` → mid
- else → short

Default 24 therefore yields majors at 0/6/12/18/24, mids at 0/3/…/24 and
shorts at every cell. Tiers degrade gracefully for denominators whose
eighths are non-integral: 12 and 36 collapse mid into major, 50 and 100
give majors only at 0/quarter/…, 48 and 72 and 96 keep all three.

### Density guard

Short ticks are dropped when their screen spacing falls below 4 px.
Spacing is measured by projecting the baseline endpoints once per frame
(`Camera.main.WorldToScreenPoint`); `stride = ceil(4.0 / pxPerCell)` and a
short tick is drawn only when `i % stride == 0`. Major and mid ticks are
never strided. This mirrors the 2D world's existing `visibleStep` guard
(`Falsum2DWorld.cs:1362-1372`).

### Markers per mode

| Mode | Start | End | Focus |
|---|---|---|---|
| Flick placement | `_flickStart` | `_flickCurrent` | snapped cell |
| SkyArea placement | `_skyStartAnchor` | `_skyEndAnchor` | snapped cell |
| Middle edit | focus-side value | none | boundary/position line |
| Anchor lateral drag | dragged endpoint | none | snapped cell |

Anchor drags surface the plane only when the drag's `Shared` state exists
and describes a plane-facing operation (`Translate`, `GroundBoundary`,
`SkyBoundary`). `DragEasing`, `EndpointLateral`, and the two group-scale
operations do not show a ruler. The anchor layer reads
`shared.Grid` / `shared.ProjectionTimeMs` and performs **no** grid math of
its own — invariant A-18.

## Info Box

One `Image` pinned top-left of the plane (offset `(16, -16)`, size
`330 × 104` canvas units, fill `(0.16, 0.45, 0.82, 0.96)`, 4 px padding),
holding three caption rows drawn through the overlay:

| Row | Key | Value |
|---|---|---|
| 1 | `falsum.modePlane.gridValue` (existing) | `GRID: {grid}` |
| 2 | `falsum.modePlane.rulerStart` (new) | `起始位置: {cell}` / `Start: {cell}` |
| 3 | `falsum.modePlane.rulerEnd` (new) | `终止位置: {cell}` / `End: {cell}` |

Cells are integers `round(normalized * divisions)` clamped to
`0..divisions`, matching the ruler's lattice exactly so the author can
count ticks. Rows 2 and 3 render only when the corresponding marker
exists; a mode with no start/end shows `GRID` alone.

## Preference

`FalsumGridPreferences` gains one field, stored as text for the same
reason `LineColors` is (a hand-edited file must be able to carry a
malformed value that `Normalize` repairs, and `Clone()` is a JsonUtility
round-trip):

```csharp
public const string DefaultDenominatorSequence =
    "12, 24, 36, 48, 50, 72, 96, 100";
public string DenominatorSequence = DefaultDenominatorSequence;
```

`FalsumBeatGrid` gains the parse/format pair next to the colour helpers:

- `MaxGridDenominator = 192` (single source of truth; the editor's
  `MaxEditableFlickGrid` becomes an alias of it, preserving invariant A-8).
- `TryParseDenominatorSequence` — comma/semicolon/pipe/whitespace split
  (same separators as `TryParseColorSequence`), each entry an integer in
  `1..192`, no duplicates, at most 8 entries, **order preserved** (not
  sorted), empty input rejected.
- `FormatDenominatorSequence` — canonical `", "` join; empty list falls
  back to the default sequence.
- Errors use new keys `falsum.error.gridSequence.required` / `.tooMany`
  (`{max}`) / `.range` (`{value}`) / `.duplicate` (`{value}`).

`Normalize()` extends its grid block (`FalsumPreferences.cs:451-465`) with
the same parse → substitute-default → re-serialize shape it already
applies to `LineColors`.

### Migration fix

`LegacyPreferencesDto` gains `public FalsumGridPreferences Grid;`,
`JsonPresence` gains a `PresenceGrid` bit probed with `HasTopLevelKey("Grid")`,
and `Build` calls a new `BuildGrid(dto.Grid, present)` instead of always
constructing a default. The pre-v6 `Advanced` lift still runs, but only
when the `Grid` group was absent, so a v6 file is not clobbered by stale
`Advanced` values. `CurrentVersion` goes 7 → 8 so the store rewrites every
profile with the new group.

This is what makes `LineColors` / `LineWidthScale` persist for the first
time; the new field depends on it.

## Denominator Stepping

One sequence, one operator, three consumers:

```csharp
// nearest entry strictly below current, clamped to the first entry
static int StepDown(int current);
// nearest entry strictly above current, clamped to the last entry
static int StepUp(int current);
```

Both live in `FalsumBeatGrid` and are integer-only.

| Consumer | Change |
|---|---|
| `_flickDenominator` | `Decrease/IncreaseFlickDenominator` call `StepDown`/`StepUp`; hint text unchanged (`falsum.hint.flick.denominator` only interpolates the value) |
| Middle-edit `StepMiddleEditGrid` | replaced by `StepDown`/`StepUp`, keeping the `1..192` clamp; a native grid that is not a sequence member (e.g. 200 from a `1/200` fraction) steps to the nearest member, which is what makes mixed denominators usable |
| Session reset | `SetMode` / radial placement **no longer** reset to 24; the last chosen value survives for the session (user decision). On preference apply the value is clamped to the sequence |

The middle-edit self-check assertions (`FalsumChartEditor.MiddleEdit.cs:533-539`)
change with the new semantics: `(16, -1) == 12`, `(16, +1) == 24`,
`(192, +1) == 192`, `(1, -1) == 1`.

Two magic numbers become derived:

- `CommitFlick`'s `denominator < 48` doubling guard (`:9353`) becomes
  `denominator * 2 <= MaxGridDenominator`, preserving the intent
  (represent an odd cell count without exceeding the 192 cap) without a
  literal.
- `SnapNormalizedCell`'s `1..48` clamp (`:12605-12609`) widens to
  `1..MaxGridDenominator` so the sequence's upper entries can be snapped.

## Preference UI

`FalsumEditingPreferencesPage.BuildGrid` appends one row after grid width:

- `TextField` bound to `preferences.Grid.DenominatorSequence`,
  class `preferences-field preferences-grid-colors` (the 380 px variant
  that an 8-entry list needs).
- On change: parse; invalid → `is-invalid` class + `DenominatorSequenceInvalid`
  page flag, draft untouched; valid → canonical re-serialize + `host.ClearStatus()`.
- `FalsumPreferencesView.RequestCommit` gains a sibling guard to the
  existing `GridColorsInvalid` one, using
  `falsum.error.preferences.fixGridSequence`; the flag is reset on close.

New keys, both locales, identical placeholder sets:
`falsum.preferences.editing.denominatorSequence` (+ `…Description`),
`falsum.error.preferences.fixGridSequence`, the four
`falsum.error.gridSequence.*`, and the two `falsum.modePlane.ruler*`.

## Self-Check

`FalsumSelfCheck` additions:

- `CheckBeatGrid`: parse/format round-trip, order preservation,
  duplicate/range/empty rejection, `StepDown`/`StepUp` first/last clamping,
  and a non-member native value stepping to the nearest member.
- `CheckPreferences`: default sequence text, `Normalize` repair of a
  malformed sequence, and the v7 `Grid` round-trip that the migration fix
  introduces.
- `middle_grid_step` updated to the sequence semantics above.

## Verification

1. `Unity -batchmode -quit -projectPath <proj> -logFile Diagnostics/UnityCompile.log`
2. `Unity … -executeMethod InFalsus.EditorTools.FalsumSelfCheck.Run`
3. `python Tools/falsum_localization_audit.py` (0 issues)
4. `Capture Editor` screenshot, compared against the reference mockup:
   ruler tiers, info box text, markers present in all four modes
5. Manual: enter Flick, press `-`/`=` across the full sequence, confirm the
   readout, tick tiers, and that leaving and re-entering keeps the value;
   open Preferences and confirm the row rejects a malformed sequence

## Risks

- The ruler adds one mesh and ~`divisions`+2 quad pairs per frame only
  while the plane is visible; the existing per-frame rebuild budget is
  unaffected because nothing is cached.
- Sequence membership is advisory for middle edit: a note carrying a
  native grid outside the sequence keeps that value until the user steps
  it, so charts with `1/200` denominators still open unchanged.
- Because `-`/`=` now step the middle-edit grid through the sequence, a
  note whose native grid is `200` can no longer be stepped *up* (200 is
  past the last entry, 100). This is accepted: `100` is the largest
  denominator the sequence offers, and `FalsumFraction.MaxDenominator`
  (200) remains the storage ceiling, not an interactive one.
- Bumping `CurrentVersion` rewrites every profile on first load; the
  additive `Grid` group is the only new content, and the migration
  presence bits keep older files lossless.
