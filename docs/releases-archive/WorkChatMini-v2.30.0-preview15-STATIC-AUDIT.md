# WorkChat Mini v2.30.0-preview15 — Static Audit

**Base:** full v2.30.0-preview14.1 (user Windows Quick and Stress both 22 PASS / 0 FAIL / 0 WARN). **Preview15 Windows compile and tests:** NOT RUN. No .NET/WPF SDK installed in this environment.

## Checks performed

- **PASS** — C#/XAML below 1000 lines: 189 files; largest (984, '_Internal/Source/MainWindow.SelfTest.cs')
- **PASS** — WPF XAML/csproj XML parsing: 6 XML parsed
- **PASS** — Changed C# lexical delimiters: []
- **PASS** — WPF event handler source names: 87 handlers; missing=[]
- **PASS** — Windows batch encoding BUILD.bat
- **PASS** — Windows batch encoding AUTO-TEST.bat
- **PASS** — Windows batch encoding SOURCE-CHECK.bat
- **PASS** — Version label alignment
- **PASS** — Assembly metadata
- **PASS** — No compiled artifacts or bin/obj paths
- **PASS** — Source change scope: ['_Internal/Source/AI/StoryConversationPaging.cs', '_Internal/Source/AI/StoryConversationSearch.cs', '_Internal/Source/AI/StoryLongSessionSelfTest.cs', '_Internal/Source/AI/StoryPanel.ConversationPaging.cs', '_Internal/Source/AI/StoryPanel.ConversationSearch.cs', '_Internal/Source/AI/StoryPanel.xaml.cs', '_Internal/Source/WorkChatMini.csproj']
- **PASS** — All other original production code unchanged
- **PASS** — WPF bounded history wiring
- **PASS** — Stale search result handling
- **PASS** — Accent-insensitive search fallback
- **PASS** — Long session selftest navigation fixture
- **PASS** — External paging math simulation (not compiled C#): 0,1,2,60,61,100,180,181,500,2000,10000 messages
- **PASS** — External search-fixture sanity (not compiled C#)

## Scope

- `_Internal/Source/AI/StoryConversationPaging.cs`
- `_Internal/Source/AI/StoryConversationSearch.cs`
- `_Internal/Source/AI/StoryLongSessionSelfTest.cs`
- `_Internal/Source/AI/StoryPanel.ConversationPaging.cs`
- `_Internal/Source/AI/StoryPanel.ConversationSearch.cs`
- `_Internal/Source/AI/StoryPanel.xaml.cs`
- `_Internal/Source/WorkChatMini.csproj`

## Limitations

Structural, lexer, XML, and Python-based math/Unicode sanity checks are **not C# compilation**, WPF runtime testing, performance measurements, disk crash-recovery injection, or proof that all user data is safe. The new C# regression assertions are not executed here. The full preview14.1 Windows test reports cover the old baseline only. Windows `AUTO-TEST.bat` Quick + Stress, plus live WPF testing of Earlier/Later/Latest and searches during Story switching, remain required. In particular, preserving exact scroll positions with variable-height messages is best-effort and needs manual UI review.
