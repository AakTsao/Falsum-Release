# FALSUM Editor Preferences And Middle-Edit Status Design

## Scope

Update the in-project FALSUM editor shell in `InFalsus-Gameplay` only.

## Background Header

The gameplay-background preview header keeps only the `背景设置` title on the
left and the close button on the right. The title owns the remaining width so
the close button stays aligned to the panel edge.

## Difficulty Cards

The current-chart preferences page presents four difficulty cards.

- Enabled difficulties expose a level field and a chart-designer field.
- Disabled difficulties show `--` and `谱面未启用`.
- Clicking a disabled card selects that difficulty through the same chart
  switching path used by the song-info difficulty menu.
- Preferences remain open after switching and rebuild the current-chart page
  from the newly selected difficulty.

Difficulty edits live in `FalsumChartMetadataDraft`, so completion applies them
through the existing metadata commit path and cancellation discards them.

The preferences footer actions are explicitly right aligned.

## Middle-Edit Status

The top bar shows a persistent middle-edit status block near 55% of the panel
width. It displays:

- The active middle-edit mode.
- A description of that mode's mouse operation.
- The current key bindings for the other two middle-edit modes.

The status uses the user's active bindings and refreshes after preferences are
applied.

## Cover Shortcut

The song-info card cover becomes the direct entry point for the current-chart
preferences page and opens the existing preferences view.

## Shortcut Categories

The shortcuts page is split into four groups:

- Playback.
- File and history.
- Clipboard and selection.
- Middle-edit modes.

## Validation

- Enabled difficulty ratings must parse as non-negative integers.
- Existing metadata validation and rollback behavior remains authoritative.
- Unity compilation, editor self-checks, and targeted interaction checks must
  pass.
