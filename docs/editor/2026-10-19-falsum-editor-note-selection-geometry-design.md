# Falsum Editor Note Selection Geometry

## Problem

Editor selection does not consistently follow the rendered note body:

- A side-lane hold longer than two measures becomes unpickable after its head
  leaves the view, even while its tail is still visible.
- Side-lane and wide holds use broad analytic hit areas that can extend well
  beyond the rendered body.
- Tap selection outlines use an approximate rectangle that is smaller than the
  rendered note.
- Pick padding is measured in canvas-local units, so its effective screen size
  changes substantially with resolution.

## Design

- Use the rendered mesh points already exposed by `NoteRenderer` as the source
  of truth for tap, hold, and flick selection outlines and pick polygons.
- For holds, treat the note as visible when the interval between its head and
  tail intersects the visible track. Do not require the head itself to remain
  inside the view.
- Use the same screen-space polygon for drawing the selection outline and for
  hit testing.
- Apply a small resolution-independent pick margin of roughly 4-6 screen pixels
  around the rendered polygon. Convert that margin to canvas-local units before
  testing.
- Keep sky areas on their existing dedicated segmented geometry path.

## Verification

- Extend editor self-checks with:
  - a side-lane hold longer than two measures whose head has left the view but
    whose tail remains visible;
  - a wide hold with points just outside the rendered body that must not select
    the note;
  - a tap whose selection polygon is generated from rendered geometry.
- Run Unity editor self-checks in batch mode and capture selection screenshots
  for visual comparison.

## Non-Goals

- Changing note rendering dimensions.
- Changing selection behavior for sky areas.
- Reworking unrelated editor controls or drag-selection behavior.

## Follow-Up: Occlusion And Pick Filters

- Treat flick and sky-area notes as an upper pick layer over tap and hold
  notes. When both layers cover the pointer, the upper layer wins. Depth still
  chooses between notes in the same layer.
- Draw a selected note outline only while its visual is active and near the
  view. Selection state remains intact and the outline returns when the note
  re-enters the view.
- Keep sky-area selection visibility behavior unchanged.
- Expose a `FalsumNotePickMask` bit mask with `GroundNotes` and `SkyNotes`
  presets. `FalsumChartEditor.SelectionMask` is the single integration point
  for the reserved "only TAP/HOLD" and "only SKYAREA/FLICK" menu actions.
