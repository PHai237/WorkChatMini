# WorkChat 2.40.0 — Windows CI compile repair + update UI

## Confirmed compile failure
`_Internal/Source/AI/StoryPanel.xaml.cs:740` used `Brushes.Transparent` in a WPF+WinForms project. Both `System.Drawing.Brushes` and `System.Windows.Media.Brushes` are visible, causing `CS0104`. Fixed by fully qualifying the WPF type.

## Updates clarified
More -> Updates checks the canonical `https://phai237.github.io/WorkChatMini/latest.json` feed automatically. The everyday flow is **Check GitHub -> Download & verify -> Restart & install**. Manual `WorkChat-Update.zip` selection is retained under an initially collapsed **Advanced** expander for use when the feed is unavailable. Never select a source `WorkChat.zip` there. The app still validates manifest/version, binary size, EXE SHA-256 and SHA-256 from the online feed, then asks before closing the app.

## Publish
`PUSH.bat` now runs SOURCE-CHECK before uploading source, and SOURCE-CHECK checks for unqualified `Brushes` usages in StoryPanel partials. The 2.40.0 version is unchanged because the previous 2.40.0 GitHub BUILD did not complete a release. On Windows, run PUSH.bat from this extracted full source ZIP, confirm YES, then wait for GitHub Actions/Release green.

## Validation boundary
Source-code static checks and ZIP integrity can run here; Windows WPF compile and Quick/Stress require GitHub Actions. Do not update the installed app until a real release passes.
