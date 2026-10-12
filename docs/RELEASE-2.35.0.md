# WorkChat 2.35.0 — Source audit / continuity safety

- Audited app update validation, Windows install helper, streaming, Story persistence,
  chapter index/cache, GitHub workflow, and WPF resource lifecycle.
- Fixed chapter-index contamination: only files named by the persisted Story Memory
  committed manuscript ledger may enter chapter text search. Loose/orphan `.md`
  files are excluded instead of being promoted to evidence for Gemini.
- Duplicate chapter numbers or duplicate filenames are excluded from the allowlist;
  invalid memory JSON still fails closed through the existing load guard.
- Extended the long-retrieval self-test with an explicitly uncommitted orphan chapter,
  plus registration of legitimate fixture chapters in the manuscript ledger.
- No change to canon schema, accepted prose, WebView2 profiles, updater format or
  AI provider request count. This is a data-isolation bug fix, not a new Story feature.

Windows compilation and live Quick/Stress are NOT available in this environment.
Do not install until GitHub Actions verifies build and both test suites.
