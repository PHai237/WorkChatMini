# WorkChat Mini 2.33.0

## AI Story — Discussion-first Brainstorming

- Tightened the Story Chat pipeline so ordinary brainstorming, critique and idea discussion stay discussion-first instead of drifting into unsolicited scene/chapter prose.
- Added bounded private brainstorming guidance for preparation, draft and editor stages. This does not add a new API round-trip, setting or storage format.
- Chat can still include tiny illustrative examples, but they are steered to remain explicit examples rather than implicit canon or surprise manuscript text.

## UI / icon polish

- Replaced the old Story icon art with a new closed-book pixel icon on a matching tile background. It now aligns with Messenger/Facebook/YouTube/Discord/Zalo/Web more cleanly in the sidebar and menus.
- Story icon resources and top-level app icons are decoded closer to display size to reduce unnecessary image memory overhead.

## Scope preserved

- No change to Story storage layout, manuscripts, checkpoints, backups, updater flow or GitHub publishing flow.
- No extra model pass introduced. Existing preparation → draft → editor → continuity pipeline remains intact.
