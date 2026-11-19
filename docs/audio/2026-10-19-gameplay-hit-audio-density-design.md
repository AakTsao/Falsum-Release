# Dense Hit Audio And Master Headroom

## Problem

Gameplay tap hits currently create one `PlayOneShot` call per judged note.
Simultaneous taps therefore duplicate an identical transient, raise the summed
peak, and consume extra real audio voices. The song and effect assets can also
reach full scale before they are mixed, so overlapping audio has no guaranteed
output ceiling.

## Design

- Coalesce tap hit sounds whose chart timestamps are within 1 ms. A chord still
  shows every judgement and hit effect, but produces one tap transient.
- Keep the music at 0.72 gain, one-shot effects at 0.42, and hold loops at 0.24.
  These values create headroom before the final mix.
- Raise Unity's real voice count from 32 to 64 so dense sky/flick sections do
  not depend on the previous limit.
- Use an explicit 48-voice one-shot pool. When every slot is busy, stop and
  reuse the slot whose scheduled end time is earliest so a new hit is never
  dropped by Unity's global voice allocator.
- Add a linked-channel look-ahead limiter to the `AudioListener`. Its final
  sample ceiling is -1.5 dBFS, with a hard ceiling fallback.

## Verification

- Extend the gameplay self-check with chord-window and limiter tests. Synthetic
  input above full scale must leave the limiter no higher than its configured
  ceiling.
- Run the Unity gameplay self-check in batch mode.

