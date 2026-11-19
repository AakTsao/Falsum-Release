# FALSUM GRID Ruler Implementation Plan

Spec: `docs/superpowers/specs/2026-11-08-falsum-grid-ruler-design.md`

## Work split (disjoint write scopes)

| Stage | Owner | Files | Depends on |
|---|---|---|---|
| P1 Foundation | lead | `Core/FalsumBeatGrid.cs`, `Runtime/FalsumPreferences.cs`, `Runtime/FalsumPreferencesMigration.cs` | — |
| P2 Rendering | lead | `Runtime/FalsumEditorWorld.cs`, `UI/FalsumModePlane.cs` | P1 (ruler state struct) |
| P3 Prefs UI | prefs-ui | `UI/FalsumEditingPreferencesPage.cs`, `UI/FalsumPreferencesView.cs`, both `Localization/*.json` | P1 |
| P4 Editor wiring | editor-wiring | `Runtime/FalsumChartEditor.cs`, `Runtime/FalsumChartEditor.MiddleEdit.cs`, `Runtime/FalsumChartEditor.AnchorEdit.cs` | P1 |
| P5 Self-check + docs | lead | `Editor/FalsumSelfCheck.cs`, `docs/memory/*` | P1–P4 |

P2, P3, P4 are mutually disjoint and may run concurrently once P1 lands.
No teammate writes `FalsumEditorWorld.cs` or `FalsumModePlane.cs`.

## P1 Foundation (lead)

1. `FalsumBeatGrid.cs`
   - `public const int MaxGridDenominator = 192;`
   - `public const string DefaultDenominatorSequence = "12, 24, 36, 48, 50, 72, 96, 100";`
   - `TryParseDenominatorSequence(string, out List<int>, out string)` —
     separators `, ; | \t \r \n space`, each `1..192`, no duplicates,
     max 8, order preserved, empty rejected.
   - `FormatDenominatorSequence(IList<int>)` — `", "` join, empty → default.
   - `StepDenominator(IList<int> seq, int current, int direction)` —
     strict neighbour in `direction`, clamped to first/last; a non-member
     `current` snaps to the nearest member in that direction.
   - New error keys via `L.Get`: `falsum.error.gridSequence.required`,
     `.tooMany` (`{max}`), `.range` (`{value}`), `.duplicate` (`{value}`).
2. `FalsumPreferences.cs`
   - `FalsumGridPreferences.DenominatorSequence` = the default constant.
   - `Normalize()` grid block: parse → on failure substitute default →
     always re-serialize (same shape as `LineColors`).
   - `CurrentVersion` 7 → 8.
3. `FalsumPreferencesMigration.cs`
   - `LegacyPreferencesDto` gains `public FalsumGridPreferences Grid;`.
   - `PresenceGrid` bit + `HasTopLevelKey(json, "Grid")` probe.
   - `Build` calls `BuildGrid(dto.Grid, present)`; the pre-v6 `Advanced`
     lift applies only when the `Grid` group was **absent**.

## P2 Rendering (lead)

1. `FalsumEditorWorld.cs`
   - Declare `public struct FalsumRulerState` (Visible, Divisions,
     FocusLine, StartNormalized, EndNormalized, Color; `NaN` = no marker).
   - Replace `Refresh`'s `snapLine/snapDivisions/snapColor` trio with one
     `FalsumRulerState`; keep behaviour identical for the snap arrow.
   - New `MeshBuffer _rulerMesh = CreateMesh("FalsumEditorRuler", 3032, true)`.
   - `BuildRuler(nowMs, timePlaneMs, ruler)`: baseline + tiers + focus +
     start diamond / end square + selection band, all through
     `AddScreenSpaceLineQuad` with the `BuildGrid` width recipe.
   - Tier test is integer divisibility (`i*4 % d == 0`, else `i*8 % d == 0`);
     short-tick stride guard at 4 px.
2. `FalsumModePlane.cs`
   - Delete `_gridLines`, `SetGridLines`, `MaxGridCells`, the
     `GridLine_` allocation loop.
   - Add `_infoBox` Image + 3 caption rows (`GridLabel` reused, plus
     `StartLabel` / `EndLabel`), all fed by new `SetState` params
     `int startCell, int endCell`.
   - Keep face + 4 borders; keep `PlaceWorldText` / overlay path for every
     caption so `DebugOverlayAlignment` stays meaningful.

## P3 Preferences UI (prefs-ui)

1. `FalsumEditingPreferencesPage.cs`
   - `DenominatorSequenceInvalid` flag alongside `GridColorsInvalid`.
   - New `TextField` row after grid width: parse-or-reject with
     `is-invalid`, canonical re-serialize on success,
     `preferences-field preferences-grid-colors` classes.
2. `FalsumPreferencesView.cs`
   - `RequestCommit` guard for the new flag; reset on close.
3. Both localization JSONs — every new key in both files, identical
   placeholder sets, no Han/ASCII text in `.cs` `text=`/`tooltip=`.

## P4 Editor wiring (editor-wiring)

1. `FalsumChartEditor.cs`
   - `_gridDenominatorSequence` (from preferences) + `_denominatorIndex`.
   - `Decrease/IncreaseFlickDenominator` → `StepDenominator`.
   - `SetMode` / radial: no longer reset to 24 (session memory).
   - `ApplyGridPreferences`: parse the sequence, clamp the current value.
   - `CommitFlick`'s `< 48` doubling guard → `denominator * 2 <= MaxGridDenominator`.
   - `SnapNormalizedCell` clamp `1..48` → `1..MaxGridDenominator`.
   - Pass `FalsumRulerState` (start/end from `_flickStart`/`_flickCurrent`
     or `_skyStartAnchor`/`_skyEndAnchor`) into `Refresh`.
   - `UpdateModePlane`: compute start/end cells and hand them to `SetState`.
2. `FalsumChartEditor.MiddleEdit.cs`
   - `StepMiddleEditGrid` → `StepDenominator` on the sequence.
   - Update the `middle_grid_step` self-check expectations
     (`16,-1 → 12`, `16,+1 → 24`, `192,+1 → 192`, `1,-1 → 1`).
3. `FalsumChartEditor.AnchorEdit.cs`
   - Surface the plane during anchor drags whose `Shared` state exists and
     whose operation is plane-facing; feed `FalsumRulerState` from
     `shared.Grid` / `ProjectionTimeMs`. No local grid math (A-18).

## P5 Verify (lead)

1. Compile: `Unity -batchmode -quit … -logFile Diagnostics/UnityCompile.log`
2. `FalsumSelfCheck.Run` → `Diagnostics/FalsumSelfCheck.json` `passed:true`
3. `python Tools/falsum_localization_audit.py` → exit 0
4. `Capture Editor` screenshot vs mockup
5. Update `docs/memory/01-architecture.md` + `02-invariants.md`,
   `05-gaps.md` (record the `Grid`-group migration fix).
