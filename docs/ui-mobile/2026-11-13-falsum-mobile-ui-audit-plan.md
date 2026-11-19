# Mobile UI Audit Implementation Plan

1. Preserve current workspace versions and establish a Unity compile baseline.
2. Fix mobile-shell callback lifetime, inline-style restoration, safe-area hosts, and short-screen rail scrolling.
3. Implement four independently scrolling menu columns, the events modal, and the connected numeric keypad without changing View-to-Editor contracts.
4. Add mobile-only compact controls and preference/dialog overrides.
5. Add focused regression checks and actual UI Toolkit multi-viewport captures.
6. Synchronize only task-owned changes after comparing current project files with their captured baselines.
7. Run compile, focused/full self-checks, and localization audit; inspect screenshots and fix regressions.
8. Report verified results and explicit remaining platform/testing limits.
