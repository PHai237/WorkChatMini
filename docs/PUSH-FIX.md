# WorkChat — GitHub source publishing verification

The public `main` branch was checked on 2026-10-12 and still contained `_Internal/VERSION.txt` = `2.30.0-preview23`, although the delivered `WorkChat.zip` contains `2.40.0`. This means the new source was not published to `main` at the time of inspection.

`PUSH.bat` delegates to `_Internal/Tools/Ship-GitHub.ps1`. The original script could exit successfully when there were no staged changes or the user cancelled, and it did not independently verify GitHub's file contents after a push.

Changes: cancellation/no-op now return a clear failure; successful push checks `refs/heads/main`, then reads the actual `_Internal/VERSION.txt` through GitHub API and requires version `2.40.0` before dispatching the release workflow. It does not modify the installed application or any Story data.

Run the replacement `WorkChat.zip` from a NEW, empty extracted folder; run `PUSH.bat`, type `YES`, and confirm the exact `VERIFIED GITHUB MAIN VERSION: 2.40.0` line. GitHub Actions must then pass independently. If it fails, save the complete console output and do not repeatedly dispatch the workflow.

## Pager correction

`git diff --cached --stat` previously could invoke a terminal pager (`less`), displaying `(END)` and pausing before the `Type YES` confirmation. Press `q` to resume a currently blocked PUSH. This source version sets `GIT_PAGER`, `GH_PAGER`, and `PAGER` to `cat` and runs `git --no-pager diff --cached --stat`, then checks its exit code.
