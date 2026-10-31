# WorkChat Mini 2.60.4 — UI skills applied

## Purpose
WorkChat is a native Windows WPF + WebView2 multi-service workspace. The user wants a polished, immersive, Discord-adjacent dark dashboard, not a stripped-down flat utility app. The design should be premium but still fast, compact and practical on a low-powered laptop.

## Repositories reviewed and translated into WPF

| Upstream | Adopted for WorkChat | Applicability |
|---|---|---|
| https://github.com/pbakaus/impeccable | Operate mode: reduce redundant navigation, make hierarchy and interaction states explicit, enforce quality gate. | Direct design guidance; no external runtime. |
| https://github.com/tasteskill/tasteskill | Audit-before-redesign, avoid generic marketing filler, hierarchy/spacing/visual variation, preserve existing app behavior. | Direct design guidance; primarily web-oriented. |
| https://github.com/greensock/gsap-skills | Keep motion short and event-driven rather than perpetual; page change uses one-shot WPF opacity animation (~125 ms). | Guidance only; GSAP itself is JavaScript and *not* used as a WPF dependency. |
| https://github.com/jakubkrehel/skills | Review keyboard focus, selected-vs-hover state, colors, typography, compact widths and clarity of copy. | Direct transferable audit principles. |
| https://github.com/shoryabansalgithub/ui-craft | Enforce consistent design token and avoid indiscriminate cards. | Reference. |
| https://github.com/kylezantos/design-motion-principles | Motion serves transitions, not decoration. Respect Windows' animation preference. | Direct guidance. |
| https://github.com/lepoco/wpfui | Native navigation, state cues, Fluent-affinity; no framework rewrite. | WPF-specific reference. |
| https://github.com/podo/design-agent-skills | Discovery index for specialist skills, not a dependency to ship. | Reference only. |

These projects were inspected as design references. Their tools were **not installed or executed inside the WPF application**, and their source files were not copied into the distribution.

## Changes made in 2.60.4

1. **A useful overview**: replaces the decorative tagline card with the active WorkChat service and the real notification enablement count. No claims about browser connectivity or notification delivery.
2. **Eliminate duplication at desktop width**: the persistent navigation rail owns preference destinations. Overview keeps direct quick actions and Updates/Exit. On compact windows the destinations return to the overview, since the rail is hidden.
3. **Selected state survives hover**: row and nav templates give selected+hover priority over plain hover, while keeping explicit keyboard focus rings.
4. **Density and hierarchy**: smaller Settings symbols, slightly tighter buttons, fewer redundant labels and text strings, same pixel-art app icons and generous click areas.
5. **Purposeful motion**: one single-shot opacity transition on Settings page change, only when Windows enables menu animation. No GSAP, animation thread or background timer.
6. **No functionality changes**: same chat alert toggles, resource policy, window size/position choices, WebView2 profile, PUSH.bat and release pipeline.

## Release checklist

- Static validation: XAML/XML parsable, all StaticResource identifiers found, C#/XAML files under 1000 lines, batch CRLF, project/build/version matches.
- Windows compilation and Quick/Stress tests via `PUSH.bat` **must** pass (not performed here).
- Visual QA: Settings with window widths 330 / 520 / 960 DIP, heights 400 / 650 / 900 DIP, and 100 / 150 / 200% DPI.
- Confirm Sidebar selected state on hover, keyboard Tab focus, Esc/back, New window, chat alerts, resource/zoom/window controls, popup dismissal and hover on disabled buttons.
- Confirm reduced animation in Windows accessibility settings and observe memory impact during repeated open/close.

## Design constraints for future versions

Do not substitute web CSS skills or GSAP directly for WPF. If WorkChat gains an independently authored HTML page or web frontend, GSAP can be evaluated for that page separately, with teardown, reduced-motion and performance checks. Keep the rendered product and Windows UI testing as the final evidence, not a textual skill checklist alone.
