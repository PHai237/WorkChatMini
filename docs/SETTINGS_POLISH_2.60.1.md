# WorkChat Mini 2.60.1 — Settings and icon polish

## What changed

- Chat alerts is a native Settings subpage (Messenger / Discord / Zalo toggles), not a separate Popup. All subpages share the same Back button and Close control.
- The retained legacy ContextMenu entry (not shown by the gear button) also routes into this page.
- Independent per-service notifications stay persisted via the existing `ApplyChatAlertChoiceAsync` logic. No data or setting migration.
- App rail pixel PNGs display at 29 DIP vs. 33 DIP, preserving 46 DIP button size and the 50/58 DIP responsive rail width; smaller Setting row symbols, gear and split icons.

## Validation

Static XML/C# checks verify name bindings, test expectations, source guard invariants, line limit and CRLF in all batch scripts. Windows WPF compilation + Quick / Stress self-tests and visual QA remain required.

## Manual QA on Windows

1. Gear → Settings → Chat alerts: confirm Back returns directly to Settings root, and Close dismisses the flyout.
2. Toggle Messenger, Discord and Zalo independently, check their selections remain after closing and reopening Settings.
3. Open Zoom, Playback, Window → Size/Position and use Back. Verify parent page is correct.
4. Verify sidebar icons are less prominent but buttons remain easy to click in normal and narrow-window modes.
5. Use `PUSH.bat` exactly as before; confirm Windows preflight / quick / stress are green.
