# UI review — v2.30.0-preview1

This is a **source-level** UI inventory, not a Windows screenshot or a runtime visual acceptance test.

## Design rules adopted

- Keep the WorkChat shell cool dark/blue and AI Story warm dark/brass; unify **interaction behavior**, not necessarily brand colors.
- Native WPF resource styles and ControlTemplates. No third-party UI library, blur-heavy effects, rich animations, or embedded Story WebView.
- A consistent 7–10 px radius, clear hover/keyboard focus, compact controls, visible active states, generous touch targets in dangerous dialogs.
- No decorative duplicate selectors, floating windows for Story memory, or modal interruptions during brainstorming.
- Keep user data actions explicit: destructive Clear browsing data and story deletion remain confirmation-driven.

## Source inspected and changes

| Surface | Existing architecture and findings | Changes in preview1 |
|---|---|---|
| Main shell, service switcher, tray | XAML shell, single main window; five Web tabs, service-specific controls; performance guard logic is coupled to MainWindow | Kept all resource, tray, titlebar and call policies untouched; keyboard focus visuals improved in shared Browser button styles |
| Browser toolbar/new-tab | Reusable BrowserStyles.xaml already existed; chrome and quick links had hover but some focus states weak | Added focus contrast without animations |
| Clear browsing data | Borderless dialog existed but native checkbox/button visual treatment looked inconsistent and lacked clear focus styling | New shared BrowserDialogStyles.xaml, rounded/hover/focus checkbox/button templates, drag header, shadow, roomier dimensions; unchanged destructive options |
| Story sidebar | ChatGPT-like Stories + tools list with a modeless right inspector | New compact Creative Ideas entry alongside existing Memory/Characters/Canon/etc., with collapsed-sidebar state and active selection |
| Story conversation | Custom bubble/copy button; renderer rebuild is costly for long histories | Co-Creation notes use a small inline editor and *only update the modified bubble*, avoiding a whole-conversation redraw |
| Story composer/input/settings | Existing dark/brass theme; TextBox controls were rectangular while neighboring buttons were rounded | New rounded input chrome and keyboard-focus border via PART_ContentHost control template; no change to Enter/Shift+Enter |
| New Story / Welcome | Genres UI and manual memory were easy to interpret as prerequisites | Chat-first introductory copy; genre/tag pickers collapsed into an optional on-demand section |
| Story inspector & branches | Existing inline controls and right inspector; branch snapshots already include conversation JSON | Creative Ideas inspector shows active/archived directions and archive/restore without popups; data remains part of branch checkpoints |
| Story delete confirmation | Already custom inline, no native MessageBox | Preserved; deliberately did not redesign an already modern flow |

## What remains for Windows visual validation

- Test native window scaling at 100%, 125%, 150% and compact widths; check checkboxes, rounded TextBox selection/caret and tooltip placement.
- Keyboard Tab/Shift+Tab, focus outlines, Enter to send, Shift+Enter newline, Escape behavior, screen readers and high-contrast preference.
- Render 100+ chat messages and switch branches; inspect scrolling and memory behaviour of the new per-message note actions.
- Confirm Clear browsing data dialog dragging, keyboard default/cancel, and no unwanted close behavior.
- Compare the shell's rare startup error MessageBox and any OS-native system dialogs separately; they are deliberately **not** all redesigned here.

## Safety, performance and maintenance

- New Co-Creation note fields in `StoryMessage` are optional JSON fields and do not change Memory schema v6.
- No AI API calls added; the notes/context are generated locally from explicit user confirmations or UI actions.
- Added UI resource smoke checks to existing Story foundation selftest, plus creation/archiving/context-budget/branch-isolation tests.
- Source audit optional in `SOURCE-CHECK.bat`; do not substitute it for BUILD/Quick/Stress.
- Build-policy limit stays <1000 lines per C#/XAML file; no dependency changes.

## v2.30.0-preview2 update — Co-Creation and chat responsiveness

- **Suggestion controls:** numbered/bulleted proposals are not presented as permanent decisions. A discreet Review action expands only on demand; each item is an editor prefill, not a one-click canon write.
- **Creative Ideas:** active notes sorted ahead of archived notes; in-place Edit, Archive/Restore, and lazy older-item navigation. All actions use the Story theme; no legacy native modal dialogs.
- **Long conversations:** rendering only the newest 60 bubbles reduces visual-tree and layout work. Older messages can be loaded in groups without touching durable conversation data. This is UI paging, not full WPF virtualization.
- **Keyboard/a11y follow-up on Windows:** check tab-order and focus inside suggested-idea editors, wrap width, reading position after loading older messages, inspector scrolling, and compact/high-DPI layouts.
- **Still not runtime-certified:** static XAML audits do not verify frame pacing or exact pixel layout. Verify using an actual Windows build and, ideally, an older test Story with hundreds of messages.
