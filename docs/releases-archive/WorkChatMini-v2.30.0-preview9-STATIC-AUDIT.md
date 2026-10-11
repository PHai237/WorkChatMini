# WorkChat Mini v2.30.0-preview9 — Static Audit

**Ngày:** 2026-10-10. **Source baseline:** complete preview8 ZIP. **Windows validated baseline:** preview6, Quick and Stress 22 PASS / 0 FAIL / 0 WARN (user-supplied tests). **preview7–9 are not Windows validated.**

**Phạm vi:** structural XML/XAML parser, handler method-name presence, Pygments lexer delimiter check for modified C#, source diff, batch byte encoding, ZIP CRC. This is *not* C#/WPF compilation or behavioral runtime verification.

## Kiểm tra tĩnh

- **PASS: Source C#/XAML under 1000 lines** — 178 files; maximum (984, 'MainWindow.SelfTest.cs')
- **PASS: XAML XML parsing** — 6 files; []
- **PASS: XAML handler symbol presence** — 87 handlers; missing=[]
- **PASS: Changed C# punctuation balance** — 9 changed/new; []
- **PASS: Version metadata** — 2.30.0-preview9
- **PASS: CMD batch encoding/CRLF** — [('BUILD.bat', True, True, True), ('AUTO-TEST.bat', True, True, True), ('SOURCE-CHECK.bat', True, True, True)]
- **PASS: Generated binaries absent** — []
- **PASS: Core shell and AI provider unchanged** — 32 protected; changed=[]
- **PASS: Change scope** — []

## Regression tests included (not executed in this environment)

- Portable backup round-trip in a disposable test project, validates SHA-256 of archived bytes and presence of branch/checkpoint files.
- ZIP manifest-tampering is rejected without modifying the Story directory.
- Chapter orphan collision refusal with intact original text.
- Live manuscript audit finds missing files, unsafe names and duplicate chapter numbers.
- Existing 22 self-test cases remain; assertions are extended inside Story memory self-test.

## Windows acceptance when the user wants a combined test batch

1. `AUTO-TEST.bat`: Release BUILD with TreatWarningsAsErrors, Quick and Stress with expected 22/0/0 each.
2. In a disposable Story, commit a chapter, create a branch and checkpoint, run Storage Health and create ZIP. The ZIP should contain project, conversation, memory, chapters and branch/checkpoint data plus backup-manifest.json.
3. Confirm that the backup can be copied, opened and reverified. **Do not extract it over personal data as a test**; no automatic restore has been implemented.
4. Check responsive Story UI, no Story switch/send while the backup is active, and that edits unlock after completion or failure.
5. Examine compiler output carefully for WPF/Windows Forms name collisions and nullable mismatches; structural parser alone cannot detect these.

## Known limitations

No dotnet SDK, WPF compiler or Windows runtime in this environment. Source checks do not guarantee compile, runtime behavior or absence of data races. Backup ZIP is **not encrypted**; users should keep it in a private location. Backup verification checks archived data and rereads source; it is not a transactional filesystem snapshot against arbitrary external writes. No automatic backup schedule or importer is included.
