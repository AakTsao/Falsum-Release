# FALSUM Mobile UI Audit

Date: 2026-11-13. Approved in chat, including the request to use multiple vertical menu columns and begin implementation. Revised after the user requested substantially denser non-rail UI and a bottom-right keypad.

## Scope

Landscape phones only. Preserve the existing left/right tool rails, their control-scale preference, chart data, editing gestures, and desktop layout.

Keep the existing UXML, View events, localization, and mobile stylesheet. Do not create a parallel editor.

## Design

- Reduce floating columns from 200/254 to maximum widths of 144/176 panel units. Use compact type and real layout dimensions rather than transforms; rail sizes and their scale preference remain unchanged.
- Arrange File, Edit, View, and Tools in four vertical menu columns with fixed headings and independently scrolling actions. Use two columns only below 540 safe panel units. Use flat 28-unit action rows and smaller window chrome.
- Replace the mobile events drawer with a centered modal window. Preserve filtering, inline editing, locate/copy/delete/apply actions, and adding events. Keep header and footer outside the scrolling list.
- Keep preferences navigation as a compact vertical list on landscape phones. Reduce headings, fields, buttons and padding together. Stack long setting rows on narrow phones and keep footer actions visible.
- Apply safe-area insets to the actual absolutely positioned rail/overlay hosts. Recalculate on viewport or safe-area changes.
- Let short-screen rails scroll without changing their button size.
- Open the menu on the first tap. Unregister shell callbacks on detach and restore inline styles, reparented elements, and transient windows.
- Connect the numeric keypad to mobile and property/event numeric inputs. Dock its approximately 196-by-184-unit window at the bottom-right, inside the safe area and clear of the rails/progress strip. Use no fullscreen scrim and leave the chart interactive. Support decimals, minus signs, and fractions through the existing sanitizer; cancel leaves values unchanged and confirm submits once.
- Escape/system-back closes one topmost mobile surface per frame. Do not close underlying mobile windows when another application dialog owns focus.

## Verification

The baseline Unity batch compile passed before editing.

Run Unity compilation, the existing editor/mobile self-checks, focused mobile UI regression tests, and the localization audit. Capture actual UI Toolkit layouts at 19:9, 16:9, and short landscape viewports, including the menu, preferences, events, and keypad. Verify changed dimensions, scrolling, safe boundaries, and mobile-to-desktop round trips.

Browser drawings are design previews only. They are not Unity screenshots or proof of mobile-device behavior. No Android device or release build is promised by this audit.
