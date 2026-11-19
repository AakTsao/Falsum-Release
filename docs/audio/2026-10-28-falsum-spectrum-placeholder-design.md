# Falsum Spectrum Placeholder Design

## Goal

Remove the current track-spectrum feature while preserving its visible entry
point in the Falsum editor. The placeholder entry will be rebuilt before a
future release.

## Scope

- Keep the `Spectrum` button in `FalsumEditor.uxml`.
- Keep the button's existing position, icon, and visual styling.
- Keep the button as an inert placeholder with no runtime behavior.
- Remove all audio-spectrum analysis, rendering, editor capture, and debug
  support.
- Preserve unrelated uncommitted work in the repository.

## Removal

Delete the spectrum-specific implementation files and metadata:

- `Assets/InFalsus/Editor/FalsumSpectrumCapture.cs`
- `Assets/InFalsus/Editor/FalsumSpectrumCapture.cs.meta`
- `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumSpectrumAnalysis.cs`
- `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumSpectrumAnalysis.cs.meta`
- `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumTrackSpectrumRenderer.cs`
- `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumTrackSpectrumRenderer.cs.meta`
- `Assets/InFalsus/Gameplay/Falsum/Resources/Falsum/Shaders/FalsumTrackSpectrum.shader`
- `Assets/InFalsus/Gameplay/Falsum/Resources/Falsum/Shaders/FalsumTrackSpectrum.shader.meta`
- `docs/superpowers/specs/2026-10-28-falsum-track-spectrum-design.md`

Remove spectrum integration from:

- `FalsumChartEditor`: analysis coroutine, analysis state, visibility state,
  world updates, debug properties, and editor event subscription.
- `FalsumEditorWorld`: renderer ownership, analysis/visibility refresh methods,
  per-frame refresh, and disposal.
- `Falsum2DWorld`: renderer ownership, analysis/visibility refresh methods,
  per-frame refresh, and disposal.
- `FalsumSelfCheck`: spectrum analysis test and analysis-specific assertions.
  The placeholder entry assertion remains.
- `README.md`: spectrum usage and capture-tool documentation.

## Entry Point

`FalsumEditorView` continues to resolve and render the `Spectrum` button so the
entry remains visible in the same transport location. It does not expose an
availability state, visibility state, event, or click handler. Clicking the
placeholder performs no action.

The existing `audio-lines` icon and `.spectrum-toggle` styles remain because
they define the placeholder's appearance.

## Data Flow

There is no spectrum data flow after this change. Loading or replacing audio
does not start analysis, and frame refresh does not update a spectrum renderer.

## Error Handling

No new error paths are introduced. The placeholder has no side effects, so
missing audio or missing analysis cannot affect editor operation.

## Verification

- Search production code for `FalsumSpectrum`, `TrackSpectrum`,
  `SpectrumAnalysis`, and spectrum renderer references.
- Confirm `FalsumEditor.uxml` still contains the `Spectrum` button.
- Confirm the placeholder icon is still loaded and rendered.
- Run the available Unity editor compilation or Falsum self-check.
- Review the diff to confirm unrelated working-tree changes remain untouched.

## Acceptance Criteria

- The Falsum editor still displays the spectrum entry in its existing location.
- The entry has no spectrum behavior.
- No spectrum analysis, renderer, shader, capture tool, or debug path remains.
- Unity code no longer depends on the deleted spectrum types.
