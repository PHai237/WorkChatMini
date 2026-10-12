# Publishes this checked source to the EXISTING public WorkChatMini repository.
# Does not build locally, alter the installed WorkChat app, or touch Story data.
param([Parameter(Mandatory=$true)][string]$SourceRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# This BAT runs in an interactive terminal. Never launch Git/gh's `less`
# pager: it shows `(END)` and blocks the publish confirmation prompt.
$env:GIT_PAGER = 'cat'
$env:GH_PAGER = 'cat'
$env:PAGER = 'cat'
$root = [IO.Path]::GetFullPath($SourceRoot)
$repository = 'PHai237/WorkChatMini'
$version = (Get-Content -LiteralPath (Join-Path $root '_Internal/VERSION.txt') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Release version must be x.y.z without a preview suffix.' }
foreach ($name in @('gh','git')) {
    if (-not (Get-Command $name -ErrorAction SilentlyContinue)) { throw "Missing $name. Install Git and GitHub CLI first." }
}
& gh auth status
if ($LASTEXITCODE -ne 0) { throw 'GitHub CLI login missing: run gh auth login first.' }
if (-not (Test-Path -LiteralPath (Join-Path $root '.github/workflows/release-windows.yml') -PathType Leaf)) {
    throw 'Run PUSH.bat only from the extracted WorkChat.zip source root.'
}
# Explicit allowlist. Never publish local .git or user data, even if placed in this folder.
$allowedRoots = @('.github','_Internal','docs','.gitignore','README.md','BUILD.bat','AUTO-TEST.bat','SOURCE-CHECK.bat','PUSH.bat')
$unexpected = @(Get-ChildItem -LiteralPath $root -Force | Where-Object { $_.Name -notin $allowedRoots })
if ($unexpected.Count -gt 0) { throw ('Unexpected files at source root: ' + (($unexpected | ForEach-Object Name) -join ', ')) }
$allowedExts = @('.cs','.xaml','.csproj','.md','.ps1','.bat','.txt','.json','.yml','.yaml','.ico','.png','.iss','.manifest','.gitignore')
$files = @(Get-ChildItem -LiteralPath $root -File -Force -Recurse | Where-Object {
    $_.FullName -notmatch '[\\/](bin|obj|Build|Releases|Logs|CrashReports)[\\/]' -and
    $_.Extension -in $allowedExts -and $_.Name -notin @('desktop.ini','Thumbs.db')
})
if ($files.Count -lt 200) { throw 'Source package looks incomplete. Publishing stopped.' }
$repoWork = Join-Path ([IO.Path]::GetTempPath()) ('WorkChatShip-' + [Guid]::NewGuid().ToString('N'))
$preflightBranch = ''
$preflightPushed = $false
try {
    Write-Host "Cloning $repository into a temporary directory..."
    & gh repo clone $repository $repoWork -- --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Could not clone repository. Check gh auth and permissions.' }
    $branch = (& git -C $repoWork branch --show-current).Trim()
    if ($branch -ne 'main') { throw "Expected main branch; found '$branch'." }
    $dirty = @(& git -C $repoWork status --porcelain)
    if ($dirty.Count -gt 0) { throw 'Fresh repository checkout is unexpectedly dirty.' }
    foreach ($file in $files) {
        if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Symlink / reparse point rejected: $($file.FullName)"
        }
        $rel = $file.FullName.Substring($root.TrimEnd([char]92, [char]47).Length).TrimStart([char]92, [char]47)
        if ($rel -match '(^|[\\/])\.env(?:\.|$)|(?i)\.(?:key|pem|pfx|p12|zip|exe|dll)$') {
            throw "Sensitive or binary file in source: $rel"
        }
        $target = Join-Path $repoWork $rel
        $targetDir = Split-Path -Parent $target
        if (-not (Test-Path -LiteralPath $targetDir)) { New-Item -ItemType Directory -Force -Path $targetDir | Out-Null }
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    }
    # Remove only obsolete top-level developer BATs from the old release bridge.
    foreach ($obsolete in @('INSTALL-APP.bat','RESTORE-PREVIOUS.bat','UPDATE-NOW.bat',
                           'FIRST-AND-LAST-LOCAL-BUILD.bat','PUBLISH-GITHUB-ONCE.bat')) {
        $old = Join-Path $repoWork $obsolete
        if (Test-Path -LiteralPath $old -PathType Leaf) { Remove-Item -LiteralPath $old -Force }
    }
    & git -C $repoWork add -A
    if ($LASTEXITCODE -ne 0) { throw 'git add failed' }
    $staged = @(& git -C $repoWork diff --cached --name-only)
    if ($staged.Count -eq 0) {
        $checkedOutVersion = (& git -C $repoWork show 'HEAD:_Internal/VERSION.txt').Trim()
        if ($LASTEXITCODE -ne 0 -or $checkedOutVersion -ne $version) {
            throw "No staged changes but GitHub checkout version is $checkedOutVersion (expected $version)."
        }
        Write-Host "GitHub already contains the $version source. Skipping commit; you can still request the release workflow." -ForegroundColor Green
    }
    $unsafe = @($staged | Where-Object { $_ -match '(?i)(^|/)(\.env(?:\.|$)|[^/]+\.(exe|dll|zip|pfx|pem|key)|[^/]+\.secrets\.json$|Logs|CrashReports)(/|$)' })
    if ($unsafe.Count -gt 0) { throw ('Refusing sensitive/generated files: ' + ($unsafe -join ', ')) }
    Write-Host "Version: $version | Staged changes: $($staged.Count)" -ForegroundColor Cyan
    if ($staged.Count -gt 0) {
        Write-Host 'Changes to publish (pager disabled):' -ForegroundColor Cyan
        & git --no-pager -C $repoWork diff --cached --stat
        if ($LASTEXITCODE -ne 0) { throw 'Could not summarize staged changes.' }
    }
    Write-Host 'This will first compile source on Windows in a temporary branch, then publish ONLY if it passes.' -ForegroundColor Yellow
    $ok = (Read-Host 'Type YES to continue').Trim()
    if ($ok -cne 'YES') { throw 'Cancelled. You must type YES in capitals. No new workflow was started.' }
    # Validate even when there are no new staged files: an unchanged source
    # might have failed last time, and never deserves a release without compile.
    $hasSourceChanges = $staged.Count -gt 0
    # Push to a disposable branch first. A REAL Windows WPF compile must pass
    # before this exact commit reaches main or starts a release. Static
    # SOURCE-CHECK.bat alone cannot catch CS0176/CS0104 semantic errors.
    $preflightBranch = 'workchat-preflight/v' + $version + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 10)
    & git -C $repoWork checkout -b $preflightBranch
    if ($LASTEXITCODE -ne 0) { throw 'Could not create temporary preflight branch.' }
    if ($hasSourceChanges) {
        & git -C $repoWork commit -m "WorkChat $version - Windows compile preflight"
        if ($LASTEXITCODE -ne 0) { throw 'Git commit failed. Set git user.name/email once if needed.' }
    }
    $testedSha = (& git -C $repoWork rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or !$testedSha) { throw 'Could not determine preflight commit SHA.' }
    Write-Host 'Uploading to temporary validation branch (main is unchanged)...' -ForegroundColor Cyan
    & git -C $repoWork push origin "HEAD:refs/heads/$preflightBranch"
    if ($LASTEXITCODE -ne 0) { throw 'Temporary branch push failed. main is unchanged.' }
    $preflightPushed = $true
    Write-Host 'Waiting for Windows compile preflight. No local .NET SDK needed.' -ForegroundColor Cyan
    $preflightRunId = $null
    for ($attempt = 0; $attempt -lt 90; $attempt++) {
        $runsJson = & gh run list --repo $repository --branch $preflightBranch --event push --limit 30 --json databaseId,headSha,workflowName,status,conclusion
        if ($LASTEXITCODE -ne 0) { throw 'Could not query GitHub Actions preflight run; main is unchanged.' }
        $runs = @(($runsJson -join "`n") | ConvertFrom-Json)
        foreach ($run in $runs) {
            if ($run.headSha -eq $testedSha -and $run.workflowName -eq 'WorkChat Mini - Windows Compile Preflight') {
                $preflightRunId = [string]$run.databaseId
                break
            }
        }
        if ($preflightRunId) { break }
        Start-Sleep -Seconds 6
    }
    if (-not $preflightRunId) {
        throw 'GitHub did not start Windows compile preflight. main is unchanged. Check Actions settings.'
    }
    Write-Host "Windows preflight run: https://github.com/$repository/actions/runs/$preflightRunId"
    & gh run watch $preflightRunId --repo $repository --exit-status
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'GitHub compilation failed; showing compiler errors automatically...' -ForegroundColor Red
        & gh run view $preflightRunId --repo $repository --log-failed
        throw "Windows compile preflight FAILED (run $preflightRunId). main remains unchanged."
    }
    Write-Host 'WINDOWS COMPILE PREFLIGHT PASSED.' -ForegroundColor Green
    if ($hasSourceChanges) {
        Write-Host 'Publishing only this tested commit to main...' -ForegroundColor Green
        # Non-force fast-forward push fails if main moved meanwhile. Never erase
        # another contributor's commits or silently ship untested content.
        & git -C $repoWork push origin 'HEAD:refs/heads/main'
        if ($LASTEXITCODE -ne 0) { throw 'main changed while preflight ran or push failed. Do not force-push.' }
    }
    # Verify the *remote* branch points at the newly pushed commit. Do not
    # mistake a successful local commit / queued workflow for published source.
    $localSha = (& git -C $repoWork rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or !$localSha) { throw 'Cannot resolve local pushed commit.' }
    $remoteRef = @(& git -C $repoWork ls-remote origin refs/heads/main)
    if ($LASTEXITCODE -ne 0 -or $remoteRef.Count -ne 1) { throw 'Cannot verify GitHub main after push.' }
    $remoteSha = ($remoteRef[0] -split '\s+')[0]
    if ($remoteSha -ne $localSha) {
        throw "GitHub main verification failed: remote=$remoteSha; pushed=$localSha. Do not run update yet."
    }
    # Confirm the actual version file hosted by GitHub, not merely the local copy.
    $encoded = (& gh api "repos/$repository/contents/_Internal/VERSION.txt?ref=main" --jq '.content')
    if ($LASTEXITCODE -ne 0 -or !$encoded) { throw 'GitHub version file cannot be read after push.' }
    # PowerShell binds -replace inside a method argument list as an extra argument.
    # Compute the cleaned Base64 string separately: FromBase64String takes ONE string.
    $cleanBase64 = (($encoded -join '') -replace '\s', '')
    $remoteVersion = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($cleanBase64)).Trim()
    if ($remoteVersion -ne $version) {
        throw "GitHub still serves $remoteVersion but this package is $version. Publish verification FAILED."
    }
    Write-Host "VERIFIED GITHUB MAIN VERSION: $remoteVersion | commit $remoteSha" -ForegroundColor Green
    Write-Host 'Source pushed and verified. Requesting GitHub Windows build...' -ForegroundColor Green
    & gh workflow run release-windows.yml --repo $repository --ref main
    if ($LASTEXITCODE -ne 0) { throw 'Push succeeded, but starting workflow failed. Run it manually from GitHub Actions.' }
    Write-Host "DONE: GitHub MAIN SOURCE is $version, workflow requested (NOT YET BUILT)." -ForegroundColor Green
    Write-Host 'Watch https://github.com/PHai237/WorkChatMini/actions' -ForegroundColor Green
    Write-Host 'After ALL jobs pass, open WorkChat > More > Updates > Check.'
}
catch {
    # Keep the real cause visible even when the cleanup phase runs afterward.
    Write-Host ("PUSH FAILED before cleanup: " + $_.Exception.Message) -ForegroundColor Red
    throw
}
finally {
    # Git writes routine progress to stderr even on success. Windows PowerShell
    # 5.1 can promote redirected native stderr to NativeCommandError when
    # ErrorActionPreference is Stop. Cleanup must NEVER hide the original error.
    if ($preflightPushed -and $preflightBranch -and (Test-Path -LiteralPath $repoWork)) {
        $cleanupExitCode = 1
        $originalErrorPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            & git -C $repoWork push --quiet origin ":refs/heads/$preflightBranch" 2>&1 | Out-Null
            $cleanupExitCode = $LASTEXITCODE
        }
        catch {
            # A failed attempt to remove a disposable branch is nonfatal.
            Write-Host ("Temporary branch cleanup warning: " + $_.Exception.Message) -ForegroundColor Yellow
        }
        finally {
            $ErrorActionPreference = $originalErrorPreference
        }
        if ($cleanupExitCode -ne 0) {
            Write-Warning "Could not delete temporary branch $preflightBranch; remove it from GitHub when convenient."
        }
    }
    if (Test-Path -LiteralPath $repoWork) {
        Remove-Item -LiteralPath $repoWork -Recurse -Force -ErrorAction SilentlyContinue
    }
}
